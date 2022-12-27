using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CATHODE;
using CATHODE.Commands;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#endif

namespace CathodeLib
{
    public class EntityDBDescriptor
    {
        public ShortGuid ID;
        public string ID_cachedstring;
    }
    public class ShortGuidDescriptor : EntityDBDescriptor
    {
        public string Description;
    }
    public class EnumDescriptor : EntityDBDescriptor
    {
        public string Name;
        public List<string> Entries = new List<string>();
    }

    public class EntityDB
    {
        private enum DatabaseType
        {
            ENUM_LOOKUP,
            COMPOSITE_LOOKUP,
        }

        static EntityDB()
        {
            lookup_enum = ReadDB(Properties.Resources.cathode_enum_lut, DatabaseType.ENUM_LOOKUP).Cast<EnumDescriptor>().ToList(); //Correctly formatted enum list from EXE
            //SetupEntityParameterList();
        }

        //Check the formatted enum dump for content
        public static EnumDescriptor GetEnum(ShortGuid id)
        {
            return lookup_enum.FirstOrDefault(o => o.ID == id);
        }

        //Get the known-valid params for a entity (this list is incomplete, and needs populating with default vals)
        public static string[] GetEntityParameterList(string entity_name)
        {
            if (!entity_parameters.ContainsKey(entity_name)) return null;
            return entity_parameters[entity_name];
        }

        //Read a generic entity database file
        private static List<EntityDBDescriptor> ReadDB(byte[] db_content, DatabaseType db_type)
        {
            List<EntityDBDescriptor> toReturn = new List<EntityDBDescriptor>();
            BinaryReader reader = new BinaryReader(new MemoryStream(db_content));
            switch (db_type)
            {
                case DatabaseType.ENUM_LOOKUP:
                {
                    reader.BaseStream.Position += 1;
                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        EnumDescriptor thisDesc = new EnumDescriptor();
                        thisDesc.ID = new ShortGuid(reader.ReadBytes(4));
                        thisDesc.Name = reader.ReadString();
                        int entryCount = reader.ReadInt32();
                        for (int i = 0; i < entryCount; i++) thisDesc.Entries.Add(reader.ReadString());
                        toReturn.Add(thisDesc);
                    }
                    break;
                }
            }
            reader.Close();
            for (int i = 0; i < toReturn.Count; i++) toReturn[i].ID_cachedstring = toReturn[i].ID.ToString();
            return toReturn;
        }

        private static List<EnumDescriptor> lookup_enum;
        private static Dictionary<string, string[]> entity_parameters = new Dictionary<string, string[]>();
    }
}
