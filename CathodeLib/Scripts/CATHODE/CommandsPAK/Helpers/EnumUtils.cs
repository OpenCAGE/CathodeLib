using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CATHODE;
using CATHODE.Scripting;
using CathodeLib;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#endif

namespace CATHODE.Scripting
{
    public static class EnumUtils
    {
        private static List<EnumDescriptor> lookup_enum;
        static EnumUtils()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            lookup_enum = ReadDB(File.ReadAllBytes(Application.streamingAssetsPath + "/NodeDBs/composite_entity_names.bin")).Cast<EnumDescriptor>().ToList();
#else
            lookup_enum = ReadDB(CathodeLib.Properties.Resources.cathode_enum_lut).Cast<EnumDescriptor>().ToList();
#endif
        }

        //Check the formatted enum dump for content
        public static EnumDescriptor GetEnum(string name)
        {
            ShortGuid id = ShortGuidUtils.Generate(name);
            return GetEnum(id);
        }
        public static EnumDescriptor GetEnum(ShortGuid id)
        {
            return lookup_enum.FirstOrDefault(o => o.ID == id);
        }

        //Read a generic entity database file
        private static List<EnumDescriptor> ReadDB(byte[] db_content)
        {
            List<EnumDescriptor> toReturn = new List<EnumDescriptor>();
            BinaryReader reader = new BinaryReader(new MemoryStream(db_content));
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                EnumDescriptor thisDesc = new EnumDescriptor();
                thisDesc.ID = new ShortGuid(reader.ReadBytes(4));
                thisDesc.Name = reader.ReadString();
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++) 
                    thisDesc.Entries.Add(new EnumDescriptor.Entry() { Name = reader.ReadString(), Index = reader.ReadInt32() });
                toReturn.Add(thisDesc);
            }
            reader.Close();
            return toReturn;
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
}
