using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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

            protected virtual void Read(BinaryReader reader)
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

        protected override void Read(BinaryReader reader)
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

        protected override void Read(BinaryReader reader)
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
}
