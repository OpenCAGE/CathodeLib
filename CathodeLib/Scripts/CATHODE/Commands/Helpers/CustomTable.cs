using CATHODE.ShaderTypes;
using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using static CathodeLib.CompositeFlowgraphTable;
using static CathodeLib.CompositeFlowgraphTable.FlowgraphMeta;
using static CathodeLib.CompositeFlowgraphTable.FlowgraphMeta.NodeMeta;

namespace CathodeLib
{
    public static class CustomTable
    {
        private static readonly byte _version = 50;

        public readonly static VanillaData Vanilla = new VanillaData();
        public class VanillaData
        {
            public VanillaData()
            {
#if UNITY_EDITOR || UNITY_STANDALONE
                byte[] content = File.ReadAllBytes(UnityEngine.Application.streamingAssetsPath + "/info.dat");
#elif GODOT
                byte[] content = Utilities.ReadStreamingAsset("info.dat");
#else
                byte[] content = CathodeLib.Properties.Resources.info;
                if (File.Exists(Paths.CustomInfoDat))
                    content = File.ReadAllBytes(Paths.CustomInfoDat);
#endif

                using (MemoryStream stream = new MemoryStream())
                using (GZipStream compressedStream = new GZipStream(new MemoryStream(content), CompressionMode.Decompress))
                {
                    compressedStream.CopyTo(stream);
                    content = stream.ToArray();
                }

                CompositePaths = (CompositePathTable)ReadTable(content, CustomTableType.COMPOSITE_PATHS);
                CompositePinInfos = (CompositePinInfoTable)ReadTable(content, CustomTableType.COMPOSITE_PIN_INFO);
                EntityNames = (EntityNameTable)ReadTable(content, CustomTableType.ENTITY_NAMES);
                ShortGuids = (GuidNameTable)ReadTable(content, CustomTableType.SHORT_GUIDS);
                CathodeEntities = (CathodeEntityTable)ReadTable(content, CustomTableType.CATHODE_ENTITY_INFO);
                CathodeEnums = (CathodeEnumTable)ReadTable(content, CustomTableType.CATHODE_ENUM_INFO);
                MaterialMappings = (MaterialMappingTable)ReadTable(content, CustomTableType.MATERIAL_MAPPINGS);
                MaterialNames = (MaterialNameTable)ReadTable(content, CustomTableType.MATERIAL_NAMES);
                FileHashes = (FileHashTable)ReadTable(content, CustomTableType.FILE_HASHES);
            }

            public readonly CompositePathTable CompositePaths;
            public readonly CompositePinInfoTable CompositePinInfos;
            public readonly EntityNameTable EntityNames;
            public readonly GuidNameTable ShortGuids;
            public readonly CathodeEntityTable CathodeEntities;
            public readonly CathodeEnumTable CathodeEnums;
            public readonly MaterialMappingTable MaterialMappings;
            public readonly MaterialNameTable MaterialNames;
            public readonly FileHashTable FileHashes;
        }

        private static byte[] _embeddedContent = null;
        private static readonly object _embeddedLock = new object();

        /// <summary>
        /// Read a table from the info.dat CathodeLib ships, ignoring any local override.
        /// </summary>
        public static Table ReadEmbeddedTable(CustomTableType table)
        {
            lock (_embeddedLock)
            {
                if (_embeddedContent == null)
                {
#if UNITY_EDITOR || UNITY_STANDALONE
                    byte[] content = File.ReadAllBytes(UnityEngine.Application.streamingAssetsPath + "/info.dat");
#elif GODOT
                    byte[] content = Utilities.ReadStreamingAsset("info.dat");
#else
                    byte[] content = CathodeLib.Properties.Resources.info;
#endif
                    using (MemoryStream stream = new MemoryStream())
                    using (GZipStream compressedStream = new GZipStream(new MemoryStream(content), CompressionMode.Decompress))
                    {
                        compressedStream.CopyTo(stream);
                        _embeddedContent = stream.ToArray();
                    }
                }
            }
            return ReadTable(_embeddedContent, table);
        }

        /// <summary>
        /// Write a CathodeLib data table to disk
        /// </summary>
        public static void WriteTable(string filepath, CustomTableType table, Table content)
        {
            //TODO: Perhaps we should write to a buffer, and then gzip the buffer, and then append that, instead?

            if (File.Exists(filepath + ".META"))
                filepath = filepath + ".META";

            /* A gzipped COMMANDS.BIN cannot carry tables after its content: the reader would have to
             * decompress to find where they start, and TableExists has no end position for it - so
             * writing in place truncates the whole script file to nothing but tables. The mobile and
             * Switch builds ship only that form. Use the sidecar reads already prefer. */
            else if (GetFileType(filepath) == CustomTableFileType.COMMANDS_COMPRESSED)
                filepath = filepath + ".META";

            if (!File.Exists(filepath))
                File.WriteAllBytes(filepath, new byte[0]);

            //Guard: files with unrecognised names are treated as STANDALONE, which assumes the whole
            //file is table data and truncates from position zero. If such a file has content but no
            //valid table structure (e.g. a COMMANDS.PAK saved under a different name), truncating
            //would destroy it - write to a .META sidecar alongside it instead. Reads already prefer
            //the .META when it exists, so this stays consistent.
            if (GetFileType(filepath) == CustomTableFileType.STANDALONE
                && !filepath.EndsWith(".META", StringComparison.OrdinalIgnoreCase))
            {
                bool isTableFile;
                using (BinaryReader reader = new BinaryReader(File.OpenRead(filepath)))
                    isTableFile = reader.BaseStream.Length == 0 || TableExists(reader, CustomTableFileType.STANDALONE, out _);

                if (!isTableFile)
                {
                    filepath = filepath + ".META";
                    if (!File.Exists(filepath))
                        File.WriteAllBytes(filepath, new byte[0]);
                }
            }

            Dictionary<CustomTableType, Table> toWrite = new Dictionary<CustomTableType, Table>();
            for (int i = 0; i < (int)CustomTableType.NUMBER_OF_END_TABLES; i++)
            {
                CustomTableType tableType = (CustomTableType)i;
                if (tableType == table)
                    toWrite.Add(tableType, content);
                else
                    toWrite.Add(tableType, ReadTable(filepath, tableType));
            }

            int endPos;
            using (BinaryReader reader = new BinaryReader(File.OpenRead(filepath)))
            {
                TableExists(reader, GetFileType(filepath), out endPos);
            }

            //Appending means truncating to where the host file's content ends, so a file that will
            //not say where that is must be left alone rather than written to at a guessed position.
            if (endPos < 0)
                throw new InvalidDataException("Cannot write CathodeLib tables to " + filepath
                    + ": its header does not say where its content ends.");

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(filepath)))
            {
                writer.BaseStream.SetLength(endPos);
                writer.BaseStream.Position = endPos;
                writer.Write(_version);

                writer.Write((Int32)CustomTableType.NUMBER_OF_END_TABLES);

                int posToWriteOffsets = (int)writer.BaseStream.Position;
                Dictionary<CustomTableType, int> tableOffsets = new Dictionary<CustomTableType, int>();
                for (int i = 0; i < (int)CustomTableType.NUMBER_OF_END_TABLES; i++)
                    writer.Write(-1);

                for (int i = 0; i < (int)CustomTableType.NUMBER_OF_END_TABLES; i++)
                {
                    CustomTableType tableType = (CustomTableType)i;
                    tableOffsets.Add(tableType, (int)writer.BaseStream.Position);
                    if (toWrite[tableType] == null) writer.Write((Int32)0);
                    else
                    {
#if DEBUG
                        long startSize = writer.BaseStream.Length;
#endif
                        switch (tableType)
                        {
                            case CustomTableType.ENTITY_NAMES:
                                ((EntityNameTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomTableType.SHORT_GUIDS:
                                ((GuidNameTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomTableType.COMPOSITE_PURGE_STATES:
                                ((CompositePurgeTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomTableType.COMPOSITE_MODIFICATION_INFO:
                                ((CompositeModificationInfoTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomTableType.COMPOSITE_FLOWGRAPHS:
                                ((CompositeFlowgraphTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomTableType.COMPOSITE_FLOWGRAPH_COMPATIBILITY_INFO:
                                ((CompositeFlowgraphCompatibilityTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomTableType.COMPOSITE_PARAMETER_MODIFICATION:
                                ((CompositeParameterModificationTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomTableType.ENTITY_APPLIED_DEFAULTS:
                                ((EntityAppliedDefaultsTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomTableType.COMPOSITE_PIN_INFO:
                                ((CompositePinInfoTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomTableType.CATHODE_ENTITY_INFO:
                                ((CathodeEntityTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomTableType.CATHODE_ENUM_INFO:
                                ((CathodeEnumTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomTableType.COMPOSITE_PATHS:
                                ((CompositePathTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomTableType.COMPOSITE_PAGE_HISTORY:
                                ((CompositePageHistoryTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomTableType.FLAGS:
                                ((FlagTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomTableType.MATERIAL_MAPPINGS:
                                ((MaterialMappingTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomTableType.MATERIAL_NAMES:
                                ((MaterialNameTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomTableType.FILE_HASHES:
                                ((FileHashTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomTableType.UBERSHADER_PATCHES:
                                ((UbershaderPatchTable)toWrite[tableType]).Write(writer);
                                break;
                        }
#if DEBUG
                        if (tableType == table)
                            Console.WriteLine("[" + (writer.BaseStream.Length - startSize) + "] Wrote table " + tableType);
#endif
                    }
                }

                writer.BaseStream.Position = posToWriteOffsets;
                for (int i = 0; i < (int)CustomTableType.NUMBER_OF_END_TABLES; i++)
                    writer.Write(tableOffsets[(CustomTableType)i]);
            }
        }

        /// <summary>
        /// Read a CathodeLib data table from disk or memory
        /// </summary>
        public static Table ReadTable(string filepath, CustomTableType table)
        {
            if (File.Exists(filepath + ".META"))
                filepath = filepath + ".META";

            if (!File.Exists(filepath))
                return null;
            return ReadTable(File.ReadAllBytes(filepath), table, GetFileType(filepath));
        }
        public static Table ReadTable(byte[] content, CustomTableType table, CustomTableFileType type = CustomTableFileType.STANDALONE)
        {
            Table data = null;
            using (MemoryStream stream = new MemoryStream(content))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (!TableExists(reader, type, out int endPos))
                    return null;

                int customDbCount = reader.ReadInt32();

                int dbOffset = -1;
                for (int i = 0; i < customDbCount; i++)
                {
                    CustomTableType tbl = (CustomTableType)i;
                    if (tbl == table)
                        dbOffset = reader.ReadInt32();
                    else
                        reader.BaseStream.Position += 4;
                }
                if (dbOffset == -1) return null;

                reader.BaseStream.Position = dbOffset;
                switch (table)
                {
                    case CustomTableType.ENTITY_NAMES:
                        data = new EntityNameTable(reader);
                        break;
                    case CustomTableType.SHORT_GUIDS:
                        data = new GuidNameTable(reader);
                        break;
                    case CustomTableType.COMPOSITE_PURGE_STATES:
                        data = new CompositePurgeTable(reader);
                        break;
                    case CustomTableType.COMPOSITE_MODIFICATION_INFO:
                        data = new CompositeModificationInfoTable(reader);
                        break;
                    case CustomTableType.COMPOSITE_FLOWGRAPHS:
                        data = new CompositeFlowgraphTable(reader);
                        break;
                    case CustomTableType.COMPOSITE_FLOWGRAPH_COMPATIBILITY_INFO:
                        data = new CompositeFlowgraphCompatibilityTable(reader);
                        break;
                    case CustomTableType.COMPOSITE_PARAMETER_MODIFICATION:
                        data = new CompositeParameterModificationTable(reader);
                        break;
                    case CustomTableType.ENTITY_APPLIED_DEFAULTS:
                        data = new EntityAppliedDefaultsTable(reader);
                        break;
                    case CustomTableType.COMPOSITE_PIN_INFO:
                        data = new CompositePinInfoTable(reader);
                        break;
                    case CustomTableType.CATHODE_ENTITY_INFO:
                        data = new CathodeEntityTable(reader);
                        break;
                    case CustomTableType.CATHODE_ENUM_INFO:
                        data = new CathodeEnumTable(reader);
                        break;
                    case CustomTableType.COMPOSITE_PATHS:
                        data = new CompositePathTable(reader);
                        break;
                    case CustomTableType.COMPOSITE_PAGE_HISTORY:
                        data = new CompositePageHistoryTable(reader);
                        break;
                    case CustomTableType.FLAGS:
                        data = new FlagTable(reader);
                        break;
                    case CustomTableType.MATERIAL_MAPPINGS:
                        data = new MaterialMappingTable(reader);
                        break;
                    case CustomTableType.MATERIAL_NAMES:
                        data = new MaterialNameTable(reader);
                        break;
                    case CustomTableType.FILE_HASHES:
                        data = new FileHashTable(reader);
                        break;
                    case CustomTableType.UBERSHADER_PATCHES:
                        data = new UbershaderPatchTable(reader);
                        break;
                }
            }
            return data;
        }

        public static CustomTableFileType GetFileType(string filepath)
        {
            switch (Path.GetFileName(filepath).ToUpper())
            {
                case "COMMANDS.PAK":
                    return CustomTableFileType.COMMANDS_PAK;
                case "COMMANDS.BIN":
                    return CustomTableFileType.COMMANDS_BIN;
                case "COMMANDS.BIN.GZ":
                    return CustomTableFileType.COMMANDS_COMPRESSED;
            }
            return CustomTableFileType.STANDALONE;
        }

        /// <summary>
        /// Is there a CathodeLib table block in this file, and where does the file's own content end?
        /// </summary>
        /// <param name="endPos">
        /// Where the host file's content ends and a table block may begin, or -1 when its header does
        /// not give a usable one. WriteTable truncates to this, so it must never be a guess.
        /// </param>
        private static bool TableExists(BinaryReader reader, CustomTableFileType type, out int endPos)
        {
            long length = reader.BaseStream.Length;
            switch (type)
            {
                case CustomTableFileType.COMMANDS_PAK:
                    if (length < 28) { endPos = -1; return false; }
                    reader.BaseStream.Position = 20;
                    endPos = (reader.ReadInt32() * 4) + (reader.ReadInt32() * 4);
                    break;
                case CustomTableFileType.COMMANDS_BIN:
                    if (length < 4) { endPos = -1; return false; }
                    reader.BaseStream.Position = 0;
                    endPos = reader.ReadInt32();
                    break;
                default:
                    //The whole of a standalone file is table data, so there is no header to trust.
                    endPos = 0;
                    break;
            }

            /* An end position outside the file means the header is not what we think it is. Say so
             * rather than seeking there: reads then simply find no tables, and the write path - which
             * truncates to this - refuses instead of taking the file's content with it. */
            if (endPos < 0 || endPos > length)
            {
                endPos = -1;
                return false;
            }

            //Nothing written after the content yet.
            if (endPos == length)
                return false;

            reader.BaseStream.Position = endPos;
            return reader.ReadByte() == _version;
        }

        public class Table
        {
            public Table(BinaryReader reader)
            {
                Read(reader);
            }

            public CustomTableType type = CustomTableType.NUMBER_OF_END_TABLES;

            public virtual void Read(BinaryReader reader)
            {

            }

            public virtual void Write(BinaryWriter writer)
            {
                writer.Write((Int32)0);
            }
        }
    }

    public class EntityNameTable : CustomTable.Table
    {
        public EntityNameTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.ENTITY_NAMES;
        }

        public Dictionary<ShortGuid, Dictionary<ShortGuid, string>> names = new Dictionary<ShortGuid, Dictionary<ShortGuid, string>>();

        public override void Read(BinaryReader reader)
        {
            names.Clear();

            if (reader == null)
                return;

            int compositeCount = reader.ReadInt32();
            for (int i = 0; i < compositeCount; i++)
            {
                ShortGuid compositeID = Utilities.Consume<ShortGuid>(reader);
                int entityCount = reader.ReadInt32();

                if (compositeID == ShortGuid.Invalid || entityCount == 0)
                    continue;

                names.Add(compositeID, new Dictionary<ShortGuid, string>(entityCount));
                for (int x = 0; x < entityCount; x++)
                {
                    ShortGuid entityID = Utilities.Consume<ShortGuid>(reader);
                    names[compositeID].Add(entityID, reader.ReadString());
                }
            }
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(names.Count);
            foreach (KeyValuePair<ShortGuid, Dictionary<ShortGuid, string>> composite in names)
            {
                Utilities.Write<ShortGuid>(writer, composite.Key);
                writer.Write(composite.Value.Count);
                foreach (KeyValuePair<ShortGuid, string> entity in composite.Value)
                {
                    Utilities.Write<ShortGuid>(writer, entity.Key);
                    writer.Write(entity.Value);
                }
            }
        }
    }
    public class GuidNameTable : CustomTable.Table
    {
        public GuidNameTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.SHORT_GUIDS;
        }

        public Dictionary<string, ShortGuid> cache = new Dictionary<string, ShortGuid>();
        public Dictionary<ShortGuid, string> cacheReversed = new Dictionary<ShortGuid, string>();

        public override void Read(BinaryReader reader)
        {
            cache.Clear();
            cacheReversed.Clear();

            if (reader == null)
                return;

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                ShortGuid id = Utilities.Consume<ShortGuid>(reader);
                string str = reader.ReadString();
                if (!cache.ContainsKey(str))
                    cache.Add(str, id);
                if (!cacheReversed.ContainsKey(id)) //NOTE: need to handle duplicates better - a warning perhaps?
                    cacheReversed.Add(id, str);
            }
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(cache.Count);
            foreach (KeyValuePair<string, ShortGuid> val in cache)
            {
                Utilities.Write<ShortGuid>(writer, val.Value);
                writer.Write(val.Key);
            }
        }
    }
    public class CompositePurgeTable : CustomTable.Table
    {
        public CompositePurgeTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.COMPOSITE_PURGE_STATES;
        }

        public List<ShortGuid> purged = new List<ShortGuid>();

        public override void Read(BinaryReader reader)
        {
            purged.Clear();

            if (reader == null)
                return;

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                ShortGuid compositeID = Utilities.Consume<ShortGuid>(reader);
                purged.Add(compositeID);
            }
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(purged.Count);
            for (int i = 0; i < purged.Count; i++)
            {
                Utilities.Write<ShortGuid>(writer, purged[i]);
            }
        }
    }
    public class CompositeModificationInfoTable : CustomTable.Table
    {
        public CompositeModificationInfoTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.COMPOSITE_MODIFICATION_INFO;
        }

        public List<ModificationInfo> modification_info = new List<ModificationInfo>();

        public override void Read(BinaryReader reader)
        {
            modification_info.Clear();

            if (reader == null)
                return;

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                ModificationInfo info = new ModificationInfo();
                info.composite_id = Utilities.Consume<ShortGuid>(reader);
                info.editor_version = reader.ReadInt32();
                info.modification_date = reader.ReadInt32();
                modification_info.Add(info);
            }
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(modification_info.Count);
            for (int i = 0; i < modification_info.Count; i++)
            {
                Utilities.Write<ShortGuid>(writer, modification_info[i].composite_id);
                writer.Write(modification_info[i].editor_version);
                writer.Write(modification_info[i].modification_date);
            }
        }

        public class ModificationInfo
        {
            public ShortGuid composite_id;
            public int editor_version; //use this to store a unique identifier for whatever tool version modified the composite
            public int modification_date; //unix timecode
        }
    }
    public class CompositeFlowgraphTable : CustomTable.Table //NOTE TO SELF: use this same class for reading/writing the default data stored in the script editor
    {
        public CompositeFlowgraphTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.COMPOSITE_FLOWGRAPHS;
        }

        public List<FlowgraphMeta> flowgraphs = new List<FlowgraphMeta>();

        public override void Read(BinaryReader reader)
        {
            flowgraphs.Clear();

            if (reader == null)
                return;

            int count = reader.ReadInt32();
            if (count == 0)
                return;

            byte version = reader.ReadByte();
            if (version != FlowgraphMeta.VERSION)
                return;

            for (int i = 0; i < count; i++)
            {
                FlowgraphMeta flowgraph = new FlowgraphMeta();

                flowgraph.CompositeGUID = Utilities.Consume<ShortGuid>(reader);
                flowgraph.Name = reader.ReadString();

                flowgraph.CanvasPosition = new PointF(reader.ReadSingle(), reader.ReadSingle());
                flowgraph.CanvasScale = reader.ReadSingle();

                flowgraph.SupportedLevels = (FlowgraphMeta.SupportedLevel)reader.ReadInt64();

                //Bit of a bodge: since the flag is never gonna be big enough for 64, use the last byte as a separate flag
                reader.BaseStream.Position -= 1;
                flowgraph.AlwaysUse = reader.ReadBoolean();

                int nodeMetaCount = reader.ReadInt32();
                for (int x = 0; x < nodeMetaCount; x++)
                {
                    FlowgraphMeta.NodeMeta node = new FlowgraphMeta.NodeMeta();
                    node.EntityGUID = Utilities.Consume<ShortGuid>(reader);
                    node.NodeID = reader.ReadInt32();
                    node.Position = new Point(reader.ReadInt32(), reader.ReadInt32());

                    int connectionCount = reader.ReadInt32();
                    for (int z = 0; z < connectionCount; z++)
                    {
                        FlowgraphMeta.NodeMeta.ConnectionMeta connection = new FlowgraphMeta.NodeMeta.ConnectionMeta();
                        connection.ParameterGUID = Utilities.Consume<ShortGuid>(reader);
                        connection.ConnectedEntityGUID = Utilities.Consume<ShortGuid>(reader);
                        connection.ConnectedParameterGUID = Utilities.Consume<ShortGuid>(reader);
                        connection.ConnectedNodeID = reader.ReadInt32();
                        node.ConnectionsOut.Add(connection);
                    }

                    bool hasUnlinkedPins = reader.ReadBoolean();
                    if (hasUnlinkedPins)
                    {
                        int unlinkedCount = reader.ReadInt32();
                        for (int z = 0; z < unlinkedCount; z++)
                        {
                            FlowgraphMeta.NodeMeta.UnlinkedPinMeta pin = new FlowgraphMeta.NodeMeta.UnlinkedPinMeta();
                            pin.ParameterGUID = Utilities.Consume<ShortGuid>(reader);
                            pin.PinLocation = reader.ReadByte();
                            pin.PinStyle = reader.ReadByte();
                            node.UnlinkedPins.Add(pin);
                        }
                    }

                    flowgraph.Nodes.Add(node);
                }
                flowgraphs.Add(flowgraph);
            }
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(flowgraphs.Count);
            writer.Write(FlowgraphMeta.VERSION);
            for (int i = 0; i < flowgraphs.Count; i++)
            {
                Utilities.Write<ShortGuid>(writer, flowgraphs[i].CompositeGUID);
                writer.Write(flowgraphs[i].Name);

                writer.Write(flowgraphs[i].CanvasPosition.X);
                writer.Write(flowgraphs[i].CanvasPosition.Y);
                writer.Write(flowgraphs[i].CanvasScale);

                writer.Write((long)flowgraphs[i].SupportedLevels);

                writer.BaseStream.Position -= 1;
                writer.Write(flowgraphs[i].AlwaysUse);

                writer.Write(flowgraphs[i].Nodes.Count);
                for (int x = 0; x < flowgraphs[i].Nodes.Count; x++)
                {
                    Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].EntityGUID);
                    writer.Write(flowgraphs[i].Nodes[x].NodeID);

                    writer.Write(flowgraphs[i].Nodes[x].Position.X);
                    writer.Write(flowgraphs[i].Nodes[x].Position.Y);

                    writer.Write(flowgraphs[i].Nodes[x].ConnectionsOut.Count);
                    for (int z = 0; z < flowgraphs[i].Nodes[x].ConnectionsOut.Count; z++)
                    {
                        Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].ConnectionsOut[z].ParameterGUID);
                        Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].ConnectionsOut[z].ConnectedEntityGUID);
                        Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].ConnectionsOut[z].ConnectedParameterGUID);
                        writer.Write(flowgraphs[i].Nodes[x].ConnectionsOut[z].ConnectedNodeID);
                    }

                    if (flowgraphs[i].Nodes[x].UnlinkedPins.Count == 0)
                    {
                        writer.Write(false);
                    }
                    else
                    {
                        writer.Write(true);
                        writer.Write(flowgraphs[i].Nodes[x].UnlinkedPins.Count);
                        for (int m = 0; m < flowgraphs[i].Nodes[x].UnlinkedPins.Count; m++)
                        {
                            Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].UnlinkedPins[m].ParameterGUID);
                            writer.Write(flowgraphs[i].Nodes[x].UnlinkedPins[m].PinLocation);
                            writer.Write(flowgraphs[i].Nodes[x].UnlinkedPins[m].PinStyle);
                        }
                    }
                }
            }
        }

        public class FlowgraphMeta : IEquatable<FlowgraphMeta>
        {
            public const byte VERSION = 3;

            public ShortGuid CompositeGUID;
            public string Name;

            public PointF CanvasPosition;
            public float CanvasScale;

            //NOTE: Only used on vanilla layouts
            public SupportedLevel SupportedLevels; //Defines flags for supported levels
            public bool AlwaysUse = false; //If this is true, ignore the flag, it can apply to any

            public List<NodeMeta> Nodes = new List<NodeMeta>();

            public bool Equals(FlowgraphMeta other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;

                if (CompositeGUID != other.CompositeGUID) return false;
                if (Name != other.Name) return false;

                return this.Nodes.SequenceEqual(other.Nodes);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as FlowgraphMeta);
            }

            public override int GetHashCode()
            {
                return 249714186 + EqualityComparer<List<NodeMeta>>.Default.GetHashCode(Nodes);
            }

            public class NodeMeta
            {
                public ShortGuid EntityGUID;
                public int NodeID;
                public Point Position;

                public List<ConnectionMeta> ConnectionsOut = new List<ConnectionMeta>();
                public List<UnlinkedPinMeta> UnlinkedPins = new List<UnlinkedPinMeta>(); //NOTE: Only used on non-vanilla layouts

                public bool Equals(NodeMeta other)
                {
                    if (other is null) return false;
                    if (ReferenceEquals(this, other)) return true;

                    return EntityGUID.Equals(other.EntityGUID) &&
                           NodeID == other.NodeID &&
                           Position.Equals(other.Position) &&
                           ConnectionsOut.SequenceEqual(other.ConnectionsOut);
                }

                public override bool Equals(object obj) => Equals(obj as NodeMeta);

                public override int GetHashCode()
                {
                    int hashCode = -779242009;
                    hashCode = hashCode * -1521134295 + EntityGUID.GetHashCode();
                    hashCode = hashCode * -1521134295 + NodeID.GetHashCode();
                    hashCode = hashCode * -1521134295 + Position.GetHashCode();
                    hashCode = hashCode * -1521134295 + EqualityComparer<List<ConnectionMeta>>.Default.GetHashCode(ConnectionsOut);
                    return hashCode;
                }

                public class ConnectionMeta
                {
                    public ShortGuid ParameterGUID;
                    public ShortGuid ConnectedEntityGUID;
                    public ShortGuid ConnectedParameterGUID;
                    public int ConnectedNodeID;

                    public bool Equals(ConnectionMeta other)
                    {
                        if (other is null) return false;
                        if (ReferenceEquals(this, other)) return true;

                        return ParameterGUID.Equals(other.ParameterGUID) &&
                               ConnectedEntityGUID.Equals(other.ConnectedEntityGUID) &&
                               ConnectedParameterGUID.Equals(other.ConnectedParameterGUID) &&
                               ConnectedNodeID == other.ConnectedNodeID;
                    }

                    public override bool Equals(object obj) => Equals(obj as ConnectionMeta);

                    public override int GetHashCode()
                    {
                        int hashCode = 1477210510;
                        hashCode = hashCode * -1521134295 + ParameterGUID.GetHashCode();
                        hashCode = hashCode * -1521134295 + ConnectedEntityGUID.GetHashCode();
                        hashCode = hashCode * -1521134295 + ConnectedParameterGUID.GetHashCode();
                        hashCode = hashCode * -1521134295 + ConnectedNodeID.GetHashCode();
                        return hashCode;
                    }
                }

                public class UnlinkedPinMeta
                {
                    public ShortGuid ParameterGUID;
                    public byte PinLocation;
                    public byte PinStyle;
                }
            }

            [Flags]
            public enum SupportedLevel : long
            {
                BSP_LV426_PT01 = 1 << 0,
                BSP_LV426_PT02 = 1 << 1,
                BSP_TORRENS = 1 << 2,
                BSPNOSTROMO_RIPLEY = 1 << 4,
                BSPNOSTROMO_TWOTEAMS = 1 << 5,
                CHALLENGEMAP1 = 1 << 6,
                CHALLENGEMAP3 = 1 << 7,
                CHALLENGEMAP4 = 1 << 8,
                CHALLENGEMAP5 = 1 << 9,
                CHALLENGEMAP7 = 1 << 10,
                CHALLENGEMAP9 = 1 << 11,
                CHALLENGEMAP11 = 1 << 12,
                CHALLENGEMAP12 = 1 << 13,
                CHALLENGEMAP14 = 1 << 14,
                CHALLENGEMAP16 = 1 << 15,
                SALVAGEMODE1 = 1 << 16,
                SALVAGEMODE2 = 1 << 17,
                ENG_ALIEN_NEST = 1 << 18,
                ENG_REACTORCORE = 1 << 19,
                ENG_TOWPLATFORM = 1 << 20,
                FRONTEND = 1 << 21,
                HAB_AIRPORT = 1 << 22,
                HAB_CORPORATEPENT = 1 << 23,
                HAB_SHOPPINGCENTRE = 1 << 24,
                SCI_ANDROIDLAB = 1 << 25,
                SCI_HOSPITALLOWER = 1 << 26,
                SCI_HOSPITALUPPER = 1 << 27,
                SCI_HUB = 1 << 28,
                SOLACE = 1 << 29,
                TECH_COMMS = 1 << 30,
                TECH_HUB = 1L << 31,
                TECH_MUTHRCORE = 1L << 32,
                TECH_RND = 1L << 33,
                TECH_RND_HZDLAB = 1L << 34,
            }
        }
    }
    public class CompositeFlowgraphCompatibilityTable : CustomTable.Table
    {
        public CompositeFlowgraphCompatibilityTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.COMPOSITE_FLOWGRAPH_COMPATIBILITY_INFO;
        }

        public List<CompatibilityInfo> compatibility_info = new List<CompatibilityInfo>();

        public override void Read(BinaryReader reader)
        {
            compatibility_info.Clear();

            if (reader == null)
                return;

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                CompatibilityInfo info = new CompatibilityInfo();
                info.composite_id = Utilities.Consume<ShortGuid>(reader);
                info.flowgraphs_supported = reader.ReadBoolean();
                compatibility_info.Add(info);
            }
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(compatibility_info.Count);
            for (int i = 0; i < compatibility_info.Count; i++)
            {
                Utilities.Write<ShortGuid>(writer, compatibility_info[i].composite_id);
                writer.Write(compatibility_info[i].flowgraphs_supported);
            }
        }

        public class CompatibilityInfo
        {
            public ShortGuid composite_id;
            public bool flowgraphs_supported;
        }
    }
    public class CompositeParameterModificationTable : CustomTable.Table
    {
        public CompositeParameterModificationTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.COMPOSITE_PARAMETER_MODIFICATION;
        }

        public Dictionary<ShortGuid, Dictionary<ShortGuid, HashSet<ShortGuid>>> modified_params = new Dictionary<ShortGuid, Dictionary<ShortGuid, HashSet<ShortGuid>>>();

        public override void Read(BinaryReader reader)
        {
            modified_params.Clear();

            if (reader == null)
                return;

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                Dictionary<ShortGuid, HashSet<ShortGuid>> entities = new Dictionary<ShortGuid, HashSet<ShortGuid>>();
                modified_params.Add(Utilities.Consume<ShortGuid>(reader), entities);
                int entity_count = reader.ReadInt32();
                for (int x = 0; x < entity_count; x++)
                {
                    HashSet<ShortGuid> parameters = new HashSet<ShortGuid>();
                    entities.Add(Utilities.Consume<ShortGuid>(reader), parameters);
                    int parameter_count = reader.ReadInt32();
                    for (int z = 0; z < parameter_count; z++)
                    {
                        parameters.Add(Utilities.Consume<ShortGuid>(reader));
                    }
                }
            }
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(modified_params.Count);
            foreach (KeyValuePair<ShortGuid, Dictionary<ShortGuid, HashSet<ShortGuid>>> composites in modified_params)
            {
                Utilities.Write<ShortGuid>(writer, composites.Key);
                writer.Write(composites.Value.Count);
                foreach (KeyValuePair<ShortGuid, HashSet<ShortGuid>> entity in composites.Value)
                {
                    Utilities.Write<ShortGuid>(writer, entity.Key);
                    writer.Write(entity.Value.Count);
                    foreach (ShortGuid parameter in entity.Value)
                    {
                        Utilities.Write<ShortGuid>(writer, parameter);
                    }
                }
            }
        }
    }
    public class EntityAppliedDefaultsTable : CustomTable.Table
    {
        public EntityAppliedDefaultsTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.ENTITY_APPLIED_DEFAULTS;
        }

        public Dictionary<ShortGuid, HashSet<ShortGuid>> applied_defaults = new Dictionary<ShortGuid, HashSet<ShortGuid>>();

        public override void Read(BinaryReader reader)
        {
            applied_defaults.Clear();

            if (reader == null)
                return;

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                HashSet<ShortGuid> entities = new HashSet<ShortGuid>();
                applied_defaults.Add(Utilities.Consume<ShortGuid>(reader), entities);
                int entity_count = reader.ReadInt32();
                for (int x = 0; x < entity_count; x++)
                {
                    entities.Add(Utilities.Consume<ShortGuid>(reader));
                }
            }
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(applied_defaults.Count);
            foreach (KeyValuePair<ShortGuid, HashSet<ShortGuid>> composites in applied_defaults)
            {
                Utilities.Write<ShortGuid>(writer, composites.Key);
                writer.Write(composites.Value.Count);
                foreach (ShortGuid entity in composites.Value)
                {
                    Utilities.Write<ShortGuid>(writer, entity);
                }
            }
        }
    }
    public class CompositePinInfoTable : CustomTable.Table
    {
        public CompositePinInfoTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.COMPOSITE_PIN_INFO;
        }

        public Dictionary<ShortGuid, List<PinInfo>> composite_pin_infos = new Dictionary<ShortGuid, List<PinInfo>>();

        public override void Read(BinaryReader reader)
        {
            composite_pin_infos.Clear();

            if (reader == null)
                return;

            byte version = reader.ReadByte();
            if (version == 0 || version == 1)
                return;

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                List<PinInfo> pin_infos = new List<PinInfo>();
                composite_pin_infos.Add(Utilities.Consume<ShortGuid>(reader), pin_infos);
                int pin_count = reader.ReadInt32();
                for (int z = 0; z < pin_count; z++)
                {
                    PinInfo pin_info = new PinInfo();
                    pin_info.VariableGUID = Utilities.Consume<ShortGuid>(reader);
                    pin_info.PinTypeGUID = Utilities.Consume<ShortGuid>(reader);
                    if (version >= 3)
                        pin_info.PinEnumTypeGUID = Utilities.Consume<ShortGuid>(reader);
                    //TODO: We should include the default index here too for enums.
                    pin_infos.Add(pin_info);
                }
            }
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(PinInfo.VERSION);
            writer.Write(composite_pin_infos.Count);
            foreach (KeyValuePair<ShortGuid, List<PinInfo>> composites in composite_pin_infos)
            {
                Utilities.Write<ShortGuid>(writer, composites.Key);
                writer.Write(composites.Value.Count);
                foreach (PinInfo pin_info in composites.Value)
                {
                    Utilities.Write<ShortGuid>(writer, pin_info.VariableGUID);
                    Utilities.Write<ShortGuid>(writer, pin_info.PinTypeGUID);
                    Utilities.Write<ShortGuid>(writer, pin_info.PinEnumTypeGUID);
                }
            }
        }

        public class PinInfo
        {
            public const byte VERSION = 3;
            public ShortGuid VariableGUID;
            public ShortGuid PinTypeGUID;
            public ShortGuid PinEnumTypeGUID; //For Enum and EnumString types
        }
    }
    public class CathodeEntityTable : CustomTable.Table
    {
        public CathodeEntityTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.CATHODE_ENTITY_INFO;
        }

        private const byte _version = 1;

        public byte[] content = null;

        public Dictionary<FunctionType, Dictionary<ParameterVariant, int>> FunctionVariantOffsets = new Dictionary<FunctionType, Dictionary<ParameterVariant, int>>();
        public Dictionary<FunctionType, FunctionType?> FunctionBaseClasses = new Dictionary<FunctionType, FunctionType?>();
        public Tuple<int, int> RelayInfoOffset;

        public override void Read(BinaryReader reader)
        {
            FunctionVariantOffsets.Clear();
            FunctionBaseClasses.Clear();

            if (reader == null)
                return;

            byte version = reader.ReadByte();
            if (version != _version)
                return;

            int length = reader.ReadInt32();
            content = reader.ReadBytes(length);

            if (content.Length == 0)
                return;

            using (BinaryReader contentReader = new BinaryReader(new MemoryStream(content)))
            {
                int functionTypeCount = contentReader.ReadInt32();
                for (int i = 0; i < functionTypeCount; i++)
                {
                    FunctionType function = (FunctionType)contentReader.ReadUInt32();

                    uint baseClass = contentReader.ReadUInt32();
                    FunctionBaseClasses.Add(function, baseClass == 0 ? (FunctionType?)null : (FunctionType)baseClass);

                    int numberOfVariants = contentReader.ReadInt32();
                    Dictionary<ParameterVariant, int> variantOffsets = new Dictionary<ParameterVariant, int>(numberOfVariants);
                    FunctionVariantOffsets.Add(function, variantOffsets);
                    for (int x = 0; x < numberOfVariants; x++)
                    {
                        variantOffsets.Add((ParameterVariant)contentReader.ReadInt32(), contentReader.ReadInt32());
                    }
                }

                RelayInfoOffset = new Tuple<int, int>(contentReader.ReadInt32(), contentReader.ReadInt32());
            }
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(_version);

            if (content == null)
            {
                writer.Write(0);
                return;
            }

            writer.Write(content.Length);
            writer.Write(content);
        }
    }
    public class CathodeEnumTable : CustomTable.Table
    {
        public CathodeEnumTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.CATHODE_ENUM_INFO;
        }

        private const byte _version = 1;

        public List<EnumDescriptor> enums = new List<EnumDescriptor>();

        public override void Read(BinaryReader reader)
        {
            enums.Clear();

            if (reader == null)
                return;

            byte version = reader.ReadByte();
            if (version != _version)
                return;

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                EnumDescriptor thisDesc = new EnumDescriptor();
                thisDesc.ID = new ShortGuid(reader.ReadBytes(4));
                thisDesc.Name = reader.ReadString();
                int entryCount = reader.ReadInt32();
                for (int x = 0; x < entryCount; x++)
                    thisDesc.Entries.Add(new EnumDescriptor.Entry() { Name = reader.ReadString(), Index = reader.ReadInt32() });
                enums.Add(thisDesc);
            }
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(_version);
            writer.Write(enums.Count);
            for (int i = 0; i < enums.Count; i++)
            {
                writer.Write(enums[i].ID.ToBytes());
                writer.Write(enums[i].Name);
                writer.Write(enums[i].Entries.Count);
                for (int x = 0; x < enums[i].Entries.Count; x++)
                {
                    writer.Write(enums[i].Entries[x].Name);
                    writer.Write(enums[i].Entries[x].Index);
                }
            }
        }

        public class EnumDescriptor
        {
            public string Name;
            public List<Entry> Entries = new List<Entry>();
            public ShortGuid ID;

            public class Entry
            {
                public string Name;
                public int Index;
            }
        }
    }
    public class CompositePathTable : CustomTable.Table
    {
        public CompositePathTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.COMPOSITE_PATHS;
        }

        public Dictionary<ShortGuid, string> composite_paths = new Dictionary<ShortGuid, string>();

        public override void Read(BinaryReader reader)
        {
            composite_paths.Clear();

            if (reader == null)
                return;

            int compositeCount = reader.ReadInt32();
            for (int i = 0; i < compositeCount; i++)
                composite_paths.Add(Utilities.Consume<ShortGuid>(reader), reader.ReadString());
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(composite_paths.Count);
            foreach (KeyValuePair<ShortGuid, string> composites in composite_paths)
            {
                Utilities.Write<ShortGuid>(writer, composites.Key);
                writer.Write(composites.Value);
            }
        }

        /// <summary>
        /// Gets a pretty Composite name
        /// </summary>
        public string GetFullPath(ShortGuid guid)
        {
            if (composite_paths.TryGetValue(guid, out string toReturn))
                return toReturn;
            return "";
        }

        /// <summary>
        /// Gets a pretty Composite name, including trimming direct paths
        /// </summary>
        public string GetPrettyPath(ShortGuid guid)
        {
            string fullPath = GetFullPath(guid);
            if (fullPath.Length < 1) return "";
            string first25 = fullPath.Substring(0, 25).ToUpper();
            switch (first25)
            {
                case @"N:\CONTENT\BUILD\LIBRARY\":
                    return fullPath.Substring(25);
                case @"N:\CONTENT\BUILD\LEVELS\P":
                    return fullPath.Substring(17);
            }
            return fullPath;
        }
    }
    public class CompositePageHistoryTable : CustomTable.Table
    {
        public CompositePageHistoryTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.COMPOSITE_PAGE_HISTORY;
        }

        public Dictionary<ShortGuid, string> last_composite_page = new Dictionary<ShortGuid, string>();

        public override void Read(BinaryReader reader)
        {
            last_composite_page.Clear();

            if (reader == null)
                return;

            int compositeCount = reader.ReadInt32();
            for (int i = 0; i < compositeCount; i++)
                last_composite_page.Add(Utilities.Consume<ShortGuid>(reader), reader.ReadString());
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(last_composite_page.Count);
            foreach (KeyValuePair<ShortGuid, string> composites in last_composite_page)
            {
                Utilities.Write<ShortGuid>(writer, composites.Key);
                writer.Write(composites.Value);
            }
        }
    }
    public class FlagTable : CustomTable.Table
    {
        public FlagTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.FLAGS;
        }

        public bool HasBeenModified = false;
        public bool HasSetEntityNames = false;

        public override void Read(BinaryReader reader)
        {
            if (reader == null)
                return;

            int metadataCount = reader.ReadInt32();
            for (int i = 0; i < metadataCount; i++)
            {
                switch (i)
                {
                    case 0:
                        HasBeenModified = reader.ReadBoolean();
                        break;
                    case 1:
                        HasSetEntityNames = reader.ReadBoolean();
                        break;
                }
            }
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(2);
            writer.Write(HasBeenModified);
            writer.Write(HasSetEntityNames);
        }
    }
    public class MaterialMappingTable : CustomTable.Table
    {
        public MaterialMappingTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.MATERIAL_MAPPINGS;
        }

        public List<MappingAlias> MappingAliases = new List<MappingAlias>();
        public List<Mapping> Mappings = new List<Mapping>();

        public class MappingAlias : IEquatable<MappingAlias>
        {
            public bool AlwaysUse = false;
            public SupportedLevel SupportedLevels; 
            public ShortGuid MappingID;
            public ShortGuid CompositeID;
            public List<ShortGuid> EntityPath = new List<ShortGuid>();

            public bool Equals(MappingAlias other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;

                return AlwaysUse == other.AlwaysUse &&
                       SupportedLevels == other.SupportedLevels &&
                       MappingID == other.MappingID &&
                       CompositeID == other.CompositeID &&
                       EntityPath == other.EntityPath;
            }

            public override bool Equals(object obj) => Equals(obj as MappingAlias);

            public override int GetHashCode()
            {
                int hashCode = 1308473823;
                hashCode = hashCode * -1521134295 + AlwaysUse.GetHashCode();
                hashCode = hashCode * -1521134295 + SupportedLevels.GetHashCode();
                hashCode = hashCode * -1521134295 + MappingID.GetHashCode();
                hashCode = hashCode * -1521134295 + CompositeID.GetHashCode();
                hashCode = hashCode * -1521134295 + EqualityComparer<List<ShortGuid>>.Default.GetHashCode(EntityPath);
                return hashCode;
            }
        }
        public class Mapping : IEquatable<Mapping>
        {
            public bool AlwaysUse = false;
            public SupportedLevel SupportedLevels;
            public ShortGuid MappingID;
            public ShortGuid CompositeID;
            public ShortGuid EntityID;

            public bool Equals(Mapping other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;

                return AlwaysUse == other.AlwaysUse &&
                       SupportedLevels == other.SupportedLevels &&
                       MappingID == other.MappingID &&
                       CompositeID == other.CompositeID &&
                       EntityID == other.EntityID;
            }

            public override bool Equals(object obj) => Equals(obj as Mapping);

            public override int GetHashCode()
            {
                int hashCode = -107168883;
                hashCode = hashCode * -1521134295 + AlwaysUse.GetHashCode();
                hashCode = hashCode * -1521134295 + SupportedLevels.GetHashCode();
                hashCode = hashCode * -1521134295 + MappingID.GetHashCode();
                hashCode = hashCode * -1521134295 + CompositeID.GetHashCode();
                hashCode = hashCode * -1521134295 + EntityID.GetHashCode();
                return hashCode;
            }
        }

        public override void Read(BinaryReader reader)
        {
            MappingAliases.Clear();
            Mappings.Clear();

            if (reader == null)
                return;

            int totalCount = reader.ReadInt32();
            if (totalCount == 0)
                return;

            int aliasCount = reader.ReadInt32();
            for (int i = 0; i < aliasCount; i++)
            {
                bool alwaysUse = reader.ReadBoolean();
                MappingAliases.Add(new MappingAlias()
                {
                    AlwaysUse = alwaysUse,
                    SupportedLevels = alwaysUse ? 0 : (FlowgraphMeta.SupportedLevel)reader.ReadInt64(),
                    MappingID = Utilities.Consume<ShortGuid>(reader),
                    CompositeID = Utilities.Consume<ShortGuid>(reader)
                });
                int pathLength = reader.ReadInt32();
                for (int x = 0; x < pathLength; x++)
                {
                    MappingAliases[MappingAliases.Count - 1].EntityPath.Add(Utilities.Consume<ShortGuid>(reader));
                }
            }
            int nonAliasCount = reader.ReadInt32();
            for (int i = 0; i < nonAliasCount; i++)
            {
                bool alwaysUse = reader.ReadBoolean();
                Mappings.Add(new Mapping()
                {
                    AlwaysUse = alwaysUse,
                    SupportedLevels = alwaysUse ? 0 : (FlowgraphMeta.SupportedLevel)reader.ReadInt64(),
                    MappingID = Utilities.Consume<ShortGuid>(reader),
                    CompositeID = Utilities.Consume<ShortGuid>(reader),
                    EntityID = Utilities.Consume<ShortGuid>(reader)
                });
            }
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(MappingAliases.Count + Mappings.Count);

            writer.Write(MappingAliases.Count);
            foreach (MappingAlias map in MappingAliases)
            {
                writer.Write(map.AlwaysUse);
                if (!map.AlwaysUse)
                    writer.Write((long)map.SupportedLevels);
                Utilities.Write<ShortGuid>(writer, map.MappingID);
                Utilities.Write<ShortGuid>(writer, map.CompositeID);
                writer.Write(map.EntityPath.Count);
                foreach (ShortGuid entry in map.EntityPath)
                {
                    Utilities.Write<ShortGuid>(writer, entry);
                }
            }
            writer.Write(Mappings.Count);
            foreach (Mapping map in Mappings)
            {
                writer.Write(map.AlwaysUse);
                if (!map.AlwaysUse)
                    writer.Write((long)map.SupportedLevels);
                Utilities.Write<ShortGuid>(writer, map.MappingID);
                Utilities.Write<ShortGuid>(writer, map.CompositeID);
                Utilities.Write<ShortGuid>(writer, map.EntityID);
            }
        }
    }
    public class MaterialNameTable : CustomTable.Table
    {
        public MaterialNameTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.MATERIAL_NAMES;
        }

        public Dictionary<string, string> material_names = new Dictionary<string, string>();

        public override void Read(BinaryReader reader)
        {
            material_names.Clear();

            if (reader == null)
                return;

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
                material_names.Add(reader.ReadString(), reader.ReadString());
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(material_names.Count);
            foreach (KeyValuePair<string, string> composites in material_names)
            {
                writer.Write(composites.Key);
                writer.Write(composites.Value);
            }
        }
    }
    public class FileHashTable : CustomTable.Table
    {
        public FileHashTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.FILE_HASHES;
        }

        public class Entry
        {
            public string Path; //Relative to the game root, forward slashes, uppercase
            public long Size;
            public byte[] Sha256; //32 bytes
            public int Platforms; //Bitmask of PlatformBit(PatchManager.Platform) values

            public bool SameContent(long size, byte[] sha256)
            {
                if (Size != size || Sha256 == null || sha256 == null || Sha256.Length != sha256.Length)
                    return false;
                for (int i = 0; i < sha256.Length; i++)
                    if (Sha256[i] != sha256[i])
                        return false;
                return true;
            }
        }

        public static int PlatformBit(PatchManager.Platform platform)
        {
            return 1 << (int)platform;
        }

        public Dictionary<string, List<Entry>> files = new Dictionary<string, List<Entry>>();

        /// <summary>
        /// The one true spelling of a path in this table: relative to the game root, forward slashes, uppercase.
        /// </summary>
        public static string NormalisePath(string path)
        {
            return (path ?? "").Replace('\\', '/').TrimStart('/').ToUpperInvariant();
        }

        /// <summary>
        /// Get the expected hash info for a file on the given platform.
        /// </summary>
        public Entry Lookup(PatchManager.Platform platform, string path)
        {
            List<Entry> variants;
            if (!files.TryGetValue(NormalisePath(path), out variants))
                return null;
            int bit = PlatformBit(platform);
            for (int i = 0; i < variants.Count; i++)
                if ((variants[i].Platforms & bit) != 0)
                    return variants[i];
            return null;
        }

        /// <summary>
        /// Record that the given platforms ship these bytes for this path. 
        /// </summary>
        public Entry Merge(int platformMask, string path, long size, byte[] sha256)
        {
            string normalised = NormalisePath(path);
            List<Entry> variants;
            if (!files.TryGetValue(normalised, out variants))
                files[normalised] = variants = new List<Entry>();
            for (int i = 0; i < variants.Count; i++)
            {
                if (variants[i].SameContent(size, sha256))
                {
                    variants[i].Platforms |= platformMask;
                    return variants[i];
                }
            }
            Entry entry = new Entry() { Path = normalised, Size = size, Sha256 = sha256, Platforms = platformMask };
            variants.Add(entry);
            return entry;
        }

        public override void Read(BinaryReader reader)
        {
            files.Clear();

            if (reader == null)
                return;

            int version = reader.ReadInt32();
            if (version < 2)
                return;

            int fileCount = reader.ReadInt32();
            for (int i = 0; i < fileCount; i++)
            {
                string path = reader.ReadString();
                int variantCount = reader.ReadInt32();
                List<Entry> variants = new List<Entry>(variantCount);
                for (int x = 0; x < variantCount; x++)
                {
                    Entry entry = new Entry();
                    entry.Path = path;
                    entry.Platforms = reader.ReadInt32();
                    entry.Size = reader.ReadInt64();
                    entry.Sha256 = reader.ReadBytes(32);
                    variants.Add(entry);
                }
                files[path] = variants;
            }
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write((Int32)2);
            writer.Write(files.Count);
            foreach (KeyValuePair<string, List<Entry>> file in files)
            {
                writer.Write(file.Key ?? string.Empty);
                writer.Write(file.Value.Count);
                foreach (Entry entry in file.Value)
                {
                    writer.Write(entry.Platforms);
                    writer.Write(entry.Size);
                    writer.Write(entry.Sha256);
                }
            }
        }
    }
    public class UbershaderPatchTable : CustomTable.Table
    {
        public UbershaderPatchTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.UBERSHADER_PATCHES;
        }

        public class Patch
        {
            public SHADER_LIST Ubershader;
            public int Platforms; //Bitmask of FileHashTable.PlatformBit values. Zero means every build.
            public Dictionary<string, string> Stages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); //Stage suffix ("vs"/"ps"/"hs"/"ds") to its master HLSL.

            public bool SupportsPlatform(PatchManager.Platform platform)
            {
                return Platforms == 0 || (Platforms & FileHashTable.PlatformBit(platform)) != 0;
            }
        }

        public List<Patch> patches = new List<Patch>();

        /// <summary>
        /// The master set for an ubershader on a build, or null when nothing here claims it.
        /// </summary>
        public Patch Lookup(SHADER_LIST ubershader, PatchManager.Platform platform)
        {
            Patch fallback = null;
            for (int i = 0; i < patches.Count; i++)
            {
                if (patches[i].Ubershader != ubershader) continue;
                if (patches[i].Platforms != 0 && patches[i].SupportsPlatform(platform)) return patches[i];
                if (patches[i].Platforms == 0) fallback = patches[i];
            }
            return fallback;
        }

        /// <summary>
        /// Add or replace the entry for an (ubershader, platform mask) pair, so regenerating over an
        /// existing info.dat patches it in place rather than appending a duplicate.
        /// </summary>
        public void Set(SHADER_LIST ubershader, int platforms, Dictionary<string, string> stages)
        {
            for (int i = 0; i < patches.Count; i++)
            {
                if (patches[i].Ubershader != ubershader || patches[i].Platforms != platforms) continue;
                patches[i].Stages = stages;
                return;
            }
            patches.Add(new Patch() { Ubershader = ubershader, Platforms = platforms, Stages = stages });
        }

        /// <summary>
        /// Drop every entry for an ubershader, whatever build it claims.
        /// </summary>
        public void Remove(SHADER_LIST ubershader)
        {
            patches.RemoveAll(o => o.Ubershader == ubershader);
        }

        public override void Read(BinaryReader reader)
        {
            patches.Clear();

            if (reader == null)
                return;

            int version = reader.ReadInt32();
            if (version < 1)
                return;

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                Patch patch = new Patch();
                patch.Ubershader = (SHADER_LIST)reader.ReadInt32();
                patch.Platforms = reader.ReadInt32();
                int stageCount = reader.ReadInt32();
                for (int x = 0; x < stageCount; x++)
                {
                    string stage = reader.ReadString();
                    patch.Stages[stage] = reader.ReadString();
                }
                patches.Add(patch);
            }
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write((Int32)1);
            writer.Write(patches.Count);
            foreach (Patch patch in patches)
            {
                writer.Write((Int32)patch.Ubershader);
                writer.Write(patch.Platforms);
                writer.Write(patch.Stages.Count);
                foreach (KeyValuePair<string, string> stage in patch.Stages)
                {
                    writer.Write(stage.Key ?? string.Empty);
                    writer.Write(stage.Value ?? string.Empty);
                }
            }
        }
    }
}
