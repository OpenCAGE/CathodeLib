using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using static CATHODE.SkeleDB;

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
                byte[] content = File.ReadAllBytes(Application.streamingAssetsPath + "/info.dat");
#else
                byte[] content = CathodeLib.Properties.Resources.info;
                if (File.Exists("LocalDB\\info.dat"))
                    content = File.ReadAllBytes("LocalDB\\info.dat");
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
            }

            public readonly CompositePathTable CompositePaths;
            public readonly CompositePinInfoTable CompositePinInfos;
            public readonly EntityNameTable EntityNames;
            public readonly GuidNameTable ShortGuids;
            public readonly CathodeEntityTable CathodeEntities;
            public readonly CathodeEnumTable CathodeEnums;
        }

        /* Write a CathodeLib data table to disk */
        public static void WriteTable(string filepath, CustomTableType table, Table content)
        {
            //TODO: Perhaps we should write to a buffer, and then gzip the buffer, and then append that, instead?

            if (!File.Exists(filepath))
                File.WriteAllBytes(filepath, new byte[0]);

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

        /* Read a CathodeLib data table from disk or memory */
        public static Table ReadTable(string filepath, CustomTableType table)
        {
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
                }
            }
            return data;
        }

        private static CustomTableFileType GetFileType(string filepath)
        {
            switch (Path.GetFileName(filepath).ToUpper())
            {
                case "COMMANDS.PAK":
                    return CustomTableFileType.COMMANDS_PAK;
                case "COMMANDS.BIN":
                    return CustomTableFileType.COMMANDS_BIN;
            }
            return CustomTableFileType.STANDALONE;
        }

        private static bool TableExists(BinaryReader reader, CustomTableFileType type, out int endPos)
        {
            switch (type)
            {
                case CustomTableFileType.COMMANDS_PAK:
                    reader.BaseStream.Position = 20;
                    endPos = (reader.ReadInt32() * 4) + (reader.ReadInt32() * 4);
                    break;
                case CustomTableFileType.COMMANDS_BIN:
                    endPos = reader.ReadInt32();
                    break;
                default:
                    endPos = 0;
                    break;
            }
            reader.BaseStream.Position = endPos;
            return (int)reader.BaseStream.Length - endPos != 0 && reader.ReadByte() == _version;
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

        public Dictionary<ShortGuid, Dictionary<ShortGuid, string>> names;

        public override void Read(BinaryReader reader)
        {
            if (reader == null)
            {
                names = new Dictionary<ShortGuid, Dictionary<ShortGuid, string>>();
                return;
            }

            int compositeCount = reader.ReadInt32();
            names = new Dictionary<ShortGuid, Dictionary<ShortGuid, string>>(compositeCount);
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

        public Dictionary<string, ShortGuid> cache;
        public Dictionary<ShortGuid, string> cacheReversed;

        public override void Read(BinaryReader reader)
        {
            if (reader == null)
            {
                cache = new Dictionary<string, ShortGuid>();
                cacheReversed = new Dictionary<ShortGuid, string>();
                return;
            }

            int count = reader.ReadInt32();
            cache = new Dictionary<string, ShortGuid>();
            cacheReversed = new Dictionary<ShortGuid, string>();
            for (int i = 0; i < count; i++)
            {
                ShortGuid id = Utilities.Consume<ShortGuid>(reader);
                string str = reader.ReadString();
                if (!cache.ContainsKey(str))
                {
                    cache.Add(str, id);
                    cacheReversed.Add(id, str);
                }
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

        public List<ShortGuid> purged;

        public override void Read(BinaryReader reader)
        {
            if (reader == null)
            {
                purged = new List<ShortGuid>();
                return;
            }

            int count = reader.ReadInt32();
            purged = new List<ShortGuid>(count);
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

        public List<ModificationInfo> modification_info;

        public override void Read(BinaryReader reader)
        {
            if (reader == null)
            {
                modification_info = new List<ModificationInfo>();
                return;
            }

            int count = reader.ReadInt32();
            modification_info = new List<ModificationInfo>(count);
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

        public List<FlowgraphMeta> flowgraphs;

        public override void Read(BinaryReader reader)
        {
            flowgraphs = new List<FlowgraphMeta>();
            if (reader == null)
                return;

            int count = reader.ReadInt32();
            if (count == 0)
                return;

            byte version = reader.ReadByte();
            if (version != FlowgraphMeta.VERSION)
            {
                //Add compatibility here when required
            }
            for (int i = 0; i < count; i++)
            {
                FlowgraphMeta flowgraph = new FlowgraphMeta();

                flowgraph.CompositeGUID = Utilities.Consume<ShortGuid>(reader);
                flowgraph.Name = reader.ReadString();

                flowgraph.CanvasPosition = new PointF(reader.ReadSingle(), reader.ReadSingle());
                flowgraph.CanvasScale = reader.ReadSingle();

                flowgraph.UsesShortenedNames = reader.ReadBoolean();
                flowgraph.IsUnfinished = reader.ReadBoolean();
                reader.BaseStream.Position += 8; //reserved

                int nodeMetaCount = reader.ReadInt32();
                for (int x = 0; x < nodeMetaCount; x++)
                {
                    FlowgraphMeta.NodeMeta node = new FlowgraphMeta.NodeMeta();
                    node.EntityGUID = Utilities.Consume<ShortGuid>(reader);
                    node.NodeID = reader.ReadInt32();

                    node.Position = new Point(reader.ReadInt32(), reader.ReadInt32());

                    int inCount = reader.ReadInt32();
                    node.PinsIn = Utilities.ConsumeArray<ShortGuid>(reader, inCount).ToList();
                    int outCount = reader.ReadInt32();
                    node.PinsOut = Utilities.ConsumeArray<ShortGuid>(reader, outCount).ToList();

                    int connectionCount = reader.ReadInt32();
                    for (int z = 0; z < connectionCount; z++)
                    {
                        FlowgraphMeta.NodeMeta.ConnectionMeta connection = new FlowgraphMeta.NodeMeta.ConnectionMeta();
                        connection.ParameterGUID = Utilities.Consume<ShortGuid>(reader);
                        connection.ConnectedEntityGUID = Utilities.Consume<ShortGuid>(reader);
                        connection.ConnectedParameterGUID = Utilities.Consume<ShortGuid>(reader);
                        connection.ConnectedNodeID = reader.ReadInt32();
                        node.Connections.Add(connection);
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

                writer.Write(flowgraphs[i].UsesShortenedNames);
                writer.Write(flowgraphs[i].IsUnfinished);
                writer.Write(new byte[8]); //reserved

                writer.Write(flowgraphs[i].Nodes.Count);
                for (int x = 0; x < flowgraphs[i].Nodes.Count; x++)
                {
                    Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].EntityGUID);
                    writer.Write(flowgraphs[i].Nodes[x].NodeID);

                    writer.Write(flowgraphs[i].Nodes[x].Position.X);
                    writer.Write(flowgraphs[i].Nodes[x].Position.Y);

                    writer.Write(flowgraphs[i].Nodes[x].PinsIn.Count);
                    Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].PinsIn);
                    writer.Write(flowgraphs[i].Nodes[x].PinsOut.Count);
                    Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].PinsOut);

                    writer.Write(flowgraphs[i].Nodes[x].Connections.Count);
                    for (int z = 0; z < flowgraphs[i].Nodes[x].Connections.Count; z++)
                    {
                        Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].Connections[z].ParameterGUID);
                        Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].Connections[z].ConnectedEntityGUID);
                        Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].Connections[z].ConnectedParameterGUID);
                        writer.Write(flowgraphs[i].Nodes[x].Connections[z].ConnectedNodeID);
                    }
                }
            }
        }

        public class FlowgraphMeta
        {
            public const byte VERSION = 1;
            public bool UsesShortenedNames = false;
            public bool IsUnfinished = false;

            public ShortGuid CompositeGUID;
            public string Name;

            public PointF CanvasPosition;
            public float CanvasScale;

            public List<NodeMeta> Nodes = new List<NodeMeta>();

            public class NodeMeta
            {
                public ShortGuid EntityGUID;
                public int NodeID;

                public Point Position;

                public List<ShortGuid> PinsIn = new List<ShortGuid>();
                public List<ShortGuid> PinsOut = new List<ShortGuid>();

                public List<ConnectionMeta> Connections = new List<ConnectionMeta>(); //NOTE: This is connections OUT of this node

                public class ConnectionMeta
                {
                    public ShortGuid ParameterGUID;
                    public ShortGuid ConnectedEntityGUID;
                    public ShortGuid ConnectedParameterGUID;
                    public int ConnectedNodeID;
                }
            }
        }
    }
    public class CompositeFlowgraphCompatibilityTable : CustomTable.Table
    {
        public CompositeFlowgraphCompatibilityTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomTableType.COMPOSITE_FLOWGRAPH_COMPATIBILITY_INFO;
        }

        public List<CompatibilityInfo> compatibility_info;

        public override void Read(BinaryReader reader)
        {
            if (reader == null)
            {
                compatibility_info = new List<CompatibilityInfo>();
                return;
            }

            int count = reader.ReadInt32();
            compatibility_info = new List<CompatibilityInfo>(count);
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

        public Dictionary<ShortGuid, Dictionary<ShortGuid, HashSet<ShortGuid>>> modified_params;

        public override void Read(BinaryReader reader)
        {
            if (reader == null)
            {
                modified_params = new Dictionary<ShortGuid, Dictionary<ShortGuid, HashSet<ShortGuid>>>();
                return;
            }

            int count = reader.ReadInt32();
            modified_params = new Dictionary<ShortGuid, Dictionary<ShortGuid, HashSet<ShortGuid>>>(count);
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

        public Dictionary<ShortGuid, HashSet<ShortGuid>> applied_defaults;

        public override void Read(BinaryReader reader)
        {
            if (reader == null)
            {
                applied_defaults = new Dictionary<ShortGuid, HashSet<ShortGuid>>();
                return;
            }

            int count = reader.ReadInt32();
            applied_defaults = new Dictionary<ShortGuid, HashSet<ShortGuid>>(count);
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

        public Dictionary<ShortGuid, List<PinInfo>> composite_pin_infos;

        public override void Read(BinaryReader reader)
        {
            if (reader == null)
            {
                composite_pin_infos = new Dictionary<ShortGuid, List<PinInfo>>();
                return;
            }

            byte version = reader.ReadByte();
            if (version == 0 || version == 1)
            {
                composite_pin_infos = new Dictionary<ShortGuid, List<PinInfo>>();
                return;
            }
            int count = reader.ReadInt32();
            composite_pin_infos = new Dictionary<ShortGuid, List<PinInfo>>(count);
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

        public Dictionary<ShortGuid, string> composite_paths;

        public override void Read(BinaryReader reader)
        {
            if (reader == null)
            {
                composite_paths = new Dictionary<ShortGuid, string>();
                return;
            }

            int compositeCount = reader.ReadInt32();
            composite_paths = new Dictionary<ShortGuid, string>(compositeCount);
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

        /* Gets a pretty Composite name */
        public string GetFullPath(ShortGuid guid)
        {
            if (composite_paths.TryGetValue(guid, out string toReturn))
                return toReturn;
            return "";
        }

        /* Gets a pretty Composite name, including trimming direct paths */
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
}
