using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace CathodeLib
{
    public static class CustomTable
    {
        private static readonly byte _version = 50;

        /* Write a CathodeLib data table to the Commands PAK */
        public static void WriteTable(string filepath, CustomEndTables table, Table content)
        {
            if (!File.Exists(filepath)) return;

            Dictionary<CustomEndTables, Table> toWrite = new Dictionary<CustomEndTables, Table>();
            for (int i = 0; i < (int)CustomEndTables.NUMBER_OF_END_TABLES; i++)
            {
                CustomEndTables tableType = (CustomEndTables)i;
                if (tableType == table)
                    toWrite.Add(tableType, content);
                else
                    toWrite.Add(tableType, ReadTable(filepath, tableType));
            }

            int endPos;
            using (BinaryReader reader = new BinaryReader(File.OpenRead(filepath)))
            {
                TableExists(reader, out endPos);
            }

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(filepath)))
            {
                writer.BaseStream.SetLength(endPos);
                writer.BaseStream.Position = endPos;
                writer.Write(_version);

                writer.Write((Int32)CustomEndTables.NUMBER_OF_END_TABLES);

                int posToWriteOffsets = (int)writer.BaseStream.Position;
                Dictionary<CustomEndTables, int> tableOffsets = new Dictionary<CustomEndTables, int>();
                for (int i = 0; i < (int)CustomEndTables.NUMBER_OF_END_TABLES; i++)
                    writer.Write((Int32)0);

                for (int i = 0; i < (int)CustomEndTables.NUMBER_OF_END_TABLES; i++)
                {
                    CustomEndTables tableType = (CustomEndTables)i;
                    tableOffsets.Add(tableType, (int)writer.BaseStream.Position);
                    if (toWrite[tableType] == null) writer.Write((Int32)0);
                    else
                    {
                        switch (tableType)
                        {
                            case CustomEndTables.ENTITY_NAMES:
                                ((EntityNameTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomEndTables.SHORT_GUIDS:
                                ((GuidNameTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomEndTables.COMPOSITE_PURGE_STATES:
                                ((CompositePurgeTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomEndTables.COMPOSITE_MODIFICATION_INFO:
                                ((CompositeModificationInfoTable)toWrite[tableType]).Write(writer);
                                break;
                            case CustomEndTables.COMPOSITE_FLOWGRAPHS:
                                ((CompositeFlowgraphsTable)toWrite[tableType]).Write(writer);
                                break;
                        }
                    }
                }

                writer.BaseStream.Position = posToWriteOffsets;
                for (int i = 0; i < (int)CustomEndTables.NUMBER_OF_END_TABLES; i++)
                    writer.Write(tableOffsets[(CustomEndTables)i]);
            }
        }

        /* Read a CathodeLib data table from the Commands PAK */
        public static Table ReadTable(string filepath, CustomEndTables table)
        {
            if (!File.Exists(filepath)) return null;

            Table data = null;
            using (BinaryReader reader = new BinaryReader(File.OpenRead(filepath)))
            {
                if (!TableExists(reader, out int endPos))
                    return null;

                int customDbCount = reader.ReadInt32();

                int dbOffset = -1;
                for (int i = 0; i < customDbCount; i++)
                {
                    CustomEndTables tbl = (CustomEndTables)i;
                    if (tbl == table)
                        dbOffset = reader.ReadInt32();
                    else
                        reader.BaseStream.Position += 4;
                }
                if (dbOffset == -1) return null;

                reader.BaseStream.Position = dbOffset;
                switch (table)
                {
                    case CustomEndTables.ENTITY_NAMES:
                        data = new EntityNameTable(reader);
                        break;
                    case CustomEndTables.SHORT_GUIDS:
                        data = new GuidNameTable(reader);
                        break;
                    case CustomEndTables.COMPOSITE_PURGE_STATES:
                        data = new CompositePurgeTable(reader);
                        break;
                    case CustomEndTables.COMPOSITE_MODIFICATION_INFO:
                        data = new CompositeModificationInfoTable(reader);
                        break;
                    case CustomEndTables.COMPOSITE_FLOWGRAPHS:
                        data = new CompositeFlowgraphsTable(reader);
                        break;
                }
            }
            return data;
        }

        private static bool TableExists(BinaryReader reader, out int endPos)
        {
            reader.BaseStream.Position = 20;
            endPos = (reader.ReadInt32() * 4) + (reader.ReadInt32() * 4);
            reader.BaseStream.Position = endPos;
            return (int)reader.BaseStream.Length - endPos != 0 && reader.ReadByte() == _version;
        }

        public class Table
        {
            public Table(BinaryReader reader)
            {
                Read(reader);
            }

            public CustomEndTables type = CustomEndTables.NUMBER_OF_END_TABLES;

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
            type = CustomEndTables.ENTITY_NAMES;
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
            type = CustomEndTables.SHORT_GUIDS;
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
            foreach (KeyValuePair<string, ShortGuid> composite in cache)
            {
                Utilities.Write<ShortGuid>(writer, composite.Value);
                writer.Write(composite.Key);
            }
        }
    }
    public class CompositePurgeTable : CustomTable.Table
    {
        public CompositePurgeTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomEndTables.COMPOSITE_PURGE_STATES;
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
            type = CustomEndTables.COMPOSITE_MODIFICATION_INFO;
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
                info.script_editor_version = reader.ReadInt32();
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
                writer.Write(modification_info[i].script_editor_version);
                writer.Write(modification_info[i].modification_date);
            }
        }

        public class ModificationInfo
        {
            public ShortGuid composite_id;
            public int script_editor_version;
            public int modification_date; //unix timecode
        }
    }
    public class CompositeFlowgraphsTable : CustomTable.Table //NOTE TO SELF: use this same class for reading/writing the default data stored in the script editor
    {
        public CompositeFlowgraphsTable(BinaryReader reader = null) : base(reader)
        {
            type = CustomEndTables.COMPOSITE_FLOWGRAPHS;
        }

        public List<FlowgraphMeta> flowgraphs;

        public override void Read(BinaryReader reader)
        {
            if (reader == null)
            {
                flowgraphs = new List<FlowgraphMeta>();
                return;
            }

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                FlowgraphMeta flowgraph = new FlowgraphMeta();

                flowgraph.CompositeGUID = Utilities.Consume<ShortGuid>(reader);
                flowgraph.Name = reader.ReadString();

                flowgraph.CanvasPosition = new PointF(reader.ReadSingle(), reader.ReadSingle());
                flowgraph.CanvasScale = reader.ReadSingle();

                reader.BaseStream.Position += 10; //reserved

                int nodeMetaCount = reader.ReadInt32();
                for (int x = 0; x < nodeMetaCount; x++)
                {
                    FlowgraphMeta.NodeMeta node = new FlowgraphMeta.NodeMeta();
                    node.EntityGUID = Utilities.Consume<ShortGuid>(reader);

                    node.Position = new Point(reader.ReadInt32(), reader.ReadInt32());

                    int inCount = reader.ReadInt32();
                    for (int z = 0; z < inCount; z++)
                    {
                        FlowgraphMeta.NodeMeta.ConnectionMeta connection = new FlowgraphMeta.NodeMeta.ConnectionMeta();
                        connection.ParameterGUID = Utilities.Consume<ShortGuid>(reader);
                        connection.ConnectedEntityGUID = Utilities.Consume<ShortGuid>(reader);
                        connection.ConnectedParameterGUID = Utilities.Consume<ShortGuid>(reader);
                        node.ConnectionsIn.Add(connection);
                    }
                    int outCount = reader.ReadInt32();
                    for (int z = 0; z < outCount; z++)
                    {
                        FlowgraphMeta.NodeMeta.ConnectionMeta connection = new FlowgraphMeta.NodeMeta.ConnectionMeta();
                        connection.ParameterGUID = Utilities.Consume<ShortGuid>(reader);
                        connection.ConnectedEntityGUID = Utilities.Consume<ShortGuid>(reader);
                        connection.ConnectedParameterGUID = Utilities.Consume<ShortGuid>(reader);
                        node.ConnectionsOut.Add(connection);
                    }

                    flowgraph.Nodes.Add(node);
                }
                flowgraphs.Add(flowgraph);
            }
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(flowgraphs.Count);
            for (int i = 0; i < flowgraphs.Count; i++)
            {
                Utilities.Write<ShortGuid>(writer, flowgraphs[i].CompositeGUID);
                writer.Write(flowgraphs[i].Name);

                writer.Write(flowgraphs[i].CanvasPosition.X);
                writer.Write(flowgraphs[i].CanvasPosition.Y);
                writer.Write(flowgraphs[i].CanvasScale);

                writer.Write(new byte[10]); //reserved

                writer.Write(flowgraphs[i].Nodes.Count);
                for (int x = 0; x < flowgraphs[i].Nodes.Count; x++)
                {
                    Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].EntityGUID);

                    writer.Write(flowgraphs[i].Nodes[x].Position.X);
                    writer.Write(flowgraphs[i].Nodes[x].Position.Y);

                    writer.Write(flowgraphs[i].Nodes[x].ConnectionsIn.Count);
                    for (int z = 0; z < flowgraphs[i].Nodes[x].ConnectionsIn.Count; z++)
                    {
                        Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].ConnectionsIn[z].ParameterGUID);
                        Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].ConnectionsIn[z].ConnectedEntityGUID);
                        Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].ConnectionsIn[z].ConnectedParameterGUID);
                    }
                    writer.Write(flowgraphs[i].Nodes[x].ConnectionsOut.Count);
                    for (int z = 0; z < flowgraphs[i].Nodes[x].ConnectionsOut.Count; z++)
                    {
                        Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].ConnectionsOut[z].ParameterGUID);
                        Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].ConnectionsOut[z].ConnectedEntityGUID);
                        Utilities.Write<ShortGuid>(writer, flowgraphs[i].Nodes[x].ConnectionsOut[z].ConnectedParameterGUID);
                    }
                }
            }
        }

        public class FlowgraphMeta
        {
            public ShortGuid CompositeGUID;
            public string Name;

            public PointF CanvasPosition;
            public float CanvasScale;

            public List<NodeMeta> Nodes = new List<NodeMeta>();

            public class NodeMeta
            {
                public ShortGuid EntityGUID;

                public Point Position;

                public List<ConnectionMeta> ConnectionsIn = new List<ConnectionMeta>();
                public List<ConnectionMeta> ConnectionsOut = new List<ConnectionMeta>();

                public class ConnectionMeta
                {
                    public ShortGuid ParameterGUID; 
                    public ShortGuid ConnectedEntityGUID;
                    public ShortGuid ConnectedParameterGUID;
                }
            }
        }
    }
}
