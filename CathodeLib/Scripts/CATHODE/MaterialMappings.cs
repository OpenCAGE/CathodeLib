using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/MATERIAL_MAPPINGS.PAK */
    public class MaterialMappings : CathodeFile
    {
        public List<Entry> Entries = new List<Entry>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE;
        public MaterialMappings(string path) : base(path) { }

        //This is always the start of the mapping filepath - remove it for ease when adding new ones
        private const string _path = "n:/content/build/library/_material_libraries_/mappings/";

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 8; //magic, version
                int entryCount = reader.ReadInt32();

                for (int x = 0; x < entryCount; x++)
                {
                    Entry entry = new Entry();
                    reader.BaseStream.Position += 4; //shortguid hash of filename (useful?)
                    int count = reader.ReadInt32();
                    reader.BaseStream.Position += 4; //this is to->from id count, stored last, but always empty
                    int strLength = reader.ReadInt32();
                    entry.Name = Utilities.ReadString(reader.ReadBytes(strLength));
                    entry.Name = entry.Name.Substring(_path.Length, entry.Name.Length - 4 - _path.Length);
                    for (int p = 0; p < count; p++)
                    {
                        Entry.Mapping mapping = new Entry.Mapping();
                        strLength = reader.ReadInt32();
                        mapping.from = Utilities.ReadString(reader.ReadBytes(strLength));
                        strLength = reader.ReadInt32();
                        mapping.to = Utilities.ReadString(reader.ReadBytes(strLength));
                        entry.Mappings.Add(mapping);
                    }
                    Entries.Add(entry);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(new byte[4] { 0xAE, 0xB0, 0xEB, 0xDE });
                writer.Write(4);
                writer.Write(Entries.Count);
                foreach (Entry entry in Entries)
                {
                    string fullPath = _path + entry.Name + ".xml";
                    Utilities.Write(writer, ShortGuidUtils.Generate(fullPath, false));
                    writer.Write(entry.Mappings.Count);
                    writer.Write(0);
                    writer.Write(fullPath.Length);
                    Utilities.WriteString(fullPath, writer);
                    foreach (Entry.Mapping mapping in entry.Mappings)
                    {
                        writer.Write(mapping.from.Length);
                        Utilities.WriteString(mapping.from, writer);
                        writer.Write(mapping.to.Length);
                        Utilities.WriteString(mapping.to, writer);
                    }
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Entry
        {
            public string Name;
            public List<Mapping> Mappings = new List<Mapping>();

            public class Mapping
            {
                public string from;
                public string to;

                public override string ToString()
                {
                    return from + "->" + to;
                }
            }

            public override string ToString()
            {
                return Name + " [" + Mappings.Count + "]";
            }
        }
        #endregion
    }
}