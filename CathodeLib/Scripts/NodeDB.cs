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
    public class NodeDBDescriptor
    {
        public cGUID ID;
    }
    public class ShortGUIDDescriptor : NodeDBDescriptor
    {
        public string Description;
    }
    public class EnumDescriptor : NodeDBDescriptor
    {
        public string Name;
        public List<string> Entries = new List<string>();
    }

    public class NodeDB
    {
        static NodeDB()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            cathode_id_map = ReadDB(File.ReadAllBytes(Application.streamingAssetsPath + "/NodeDBs/cathode_generic_lut.bin")).Cast<ShortGUIDDescriptor>().ToList(); //Names for node types, parameters, enums, etc from EXE
            cathode_enum_map = ReadDB(File.ReadAllBytes(Application.streamingAssetsPath + "/NodeDBs/cathode_enum_lut.bin")).Cast<EnumDescriptor>().ToList(); //Correctly formatted enum list from EXE
            node_friendly_names = ReadDB(File.ReadAllBytes(Application.streamingAssetsPath + "/NodeDBs/cathode_nodename_lut.bin")).Cast<ShortGUIDDescriptor>().ToList(); //Names for unique nodes from commands BIN
#else
            cathode_id_map = ReadDB(CathodeLib.Properties.Resources.cathode_generic_lut).Cast<ShortGUIDDescriptor>().ToList(); //Names for node types, parameters, enums, etc from EXE
            cathode_enum_map = ReadDB(CathodeLib.Properties.Resources.cathode_enum_lut).Cast<EnumDescriptor>().ToList(); //Correctly formatted enum list from EXE
            node_friendly_names = ReadDB(CathodeLib.Properties.Resources.cathode_nodename_lut).Cast<ShortGUIDDescriptor>().ToList(); //Names for unique nodes from commands BIN
#endif
        }

        //Check the CATHODE data dump for a corresponding name
        public static string GetCathodeName(cGUID id)
        {
            if (id.val == null) return "";
            foreach (ShortGUIDDescriptor db_entry in cathode_id_map) if (db_entry.ID == id) return db_entry.Description;
            return id.ToString();
        }
        public static string GetCathodeName(cGUID id, CommandsPAK pak) //This is performed separately to be able to remap nodes that are flowgraphs
        {
            if (id.val == null) return "";
            foreach (ShortGUIDDescriptor db_entry in cathode_id_map) if (db_entry.ID == id) return db_entry.Description;
            CathodeFlowgraph flow = pak.GetFlowgraph(id); if (flow == null) return id.ToString();
            return flow.name;
        }

        //Reverse CATHODE name check
        public static cGUID GetCathodeGUID(string text)
        {
            ShortGUIDDescriptor thisDesc = cathode_id_map.FirstOrDefault(o => o.Description == text);
            if (thisDesc == null) return new cGUID();
            return thisDesc.ID;
        }

        //Check the COMMANDS.BIN dump for node in-editor names
        public static string GetEditorName(cGUID id)
        {
            if (id.val == null) return "";
            foreach (ShortGUIDDescriptor db_entry in node_friendly_names) if (db_entry.ID == id) return db_entry.Description;
            return id.ToString();
        }

        //Reverse editor name check
        public static cGUID GetEditorGUID(string text)
        {
            ShortGUIDDescriptor thisDesc = node_friendly_names.FirstOrDefault(o => o.Description == text);
            if (thisDesc == null) return new cGUID();
            return thisDesc.ID;
        }

        //Check the formatted enum dump for content
        public static EnumDescriptor GetEnum(cGUID id)
        {
            return cathode_enum_map.FirstOrDefault(o => o.ID == id);
        }

        private static List<NodeDBDescriptor> ReadDB(byte[] db_content)
        {
            List<NodeDBDescriptor> toReturn = new List<NodeDBDescriptor>();

            MemoryStream readerStream = new MemoryStream(db_content);
            BinaryReader reader = new BinaryReader(readerStream);
            int type = reader.ReadChar(); //0 = normal db, 1 = enum db
            switch (type)
            {
                case 0:
                {
                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        ShortGUIDDescriptor thisDesc = new ShortGUIDDescriptor();
                        thisDesc.ID = new cGUID(reader.ReadBytes(4));
                        thisDesc.Description = reader.ReadString();
                        toReturn.Add(thisDesc);
                    }
                    break;
                }
                case 1:
                {
                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        EnumDescriptor thisDesc = new EnumDescriptor();
                        thisDesc.ID = new cGUID(reader.ReadBytes(4));
                        thisDesc.Name = reader.ReadString();
                        int entryCount = reader.ReadInt32();
                        for (int i = 0; i < entryCount; i++) thisDesc.Entries.Add(reader.ReadString());
                        toReturn.Add(thisDesc);
                    }
                    break;
                }
            }
            reader.Close();

            return toReturn;
        }

        private static List<ShortGUIDDescriptor> cathode_id_map;
        private static List<EnumDescriptor> cathode_enum_map;
        private static List<ShortGUIDDescriptor> node_friendly_names;
    }
}
