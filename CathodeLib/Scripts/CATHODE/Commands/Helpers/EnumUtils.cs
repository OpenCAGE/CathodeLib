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
            byte[] dbContent = File.ReadAllBytes(Application.streamingAssetsPath + "/NodeDBs/cathode_enum_lut.bin");
#else
            byte[] dbContent = CathodeLib.Properties.Resources.cathode_enum_lut;
            if (File.Exists("LocalDB/cathode_enum_lut.bin"))
                dbContent = File.ReadAllBytes("LocalDB/cathode_enum_lut.bin");
#endif
            lookup_enum = ReadDB(dbContent).Cast<EnumDescriptor>().ToList();
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
            //TODO: compress

            List<EnumDescriptor> toReturn = new List<EnumDescriptor>();
            BinaryReader reader = new BinaryReader(new MemoryStream(db_content));
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                EnumDescriptor thisDesc = new EnumDescriptor();
                thisDesc.ID = new ShortGuid(reader.ReadBytes(4));
                thisDesc.Name = reader.ReadString();
                int entryCount = reader.ReadInt32();
                for (int x = 0; x < entryCount; x++) 
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
