using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE
{
    public class ShortGUIDDescriptor
    {
        public byte[] ID;
        public string Description;
    }

    public class NodeDB
    {
        static NodeDB()
        {
            cathode_id_map = ReadDB(CathodeLib.Properties.Resources.cathode_id_map); //Names for node types, parameters, and enums
            node_friendly_names = ReadDB(CathodeLib.Properties.Resources.node_friendly_names); //Names for unique nodes
        }

        //Check the CATHODE data dump for a corresponding name
        public static string GetName(byte[] id)
        {
            if (id == null) return "";
            foreach (ShortGUIDDescriptor db_entry in cathode_id_map) if (db_entry.ID.SequenceEqual(id)) return db_entry.Description;
            return BitConverter.ToString(id);
        }
        public static string GetNodeTypeName(byte[] id, CommandsPAK pak) //This is performed separately to be able to remap nodes that are flowgraphs
        {
            if (id == null) return "";
            foreach (ShortGUIDDescriptor db_entry in cathode_id_map) if (db_entry.ID.SequenceEqual(id)) return db_entry.Description;
            CathodeFlowgraph flow = pak.GetFlowgraph(id); if (flow == null) return BitConverter.ToString(id);
            return flow.name;
        }

        //Check the COMMANDS.BIN dump for node in-editor names
        public static string GetFriendlyName(byte[] id)
        {
            if (id == null) return "";
            foreach (ShortGUIDDescriptor db_entry in node_friendly_names) if (db_entry.ID.SequenceEqual(id)) return db_entry.Description;
            return BitConverter.ToString(id);
        }

        private static List<ShortGUIDDescriptor> ReadDB(byte[] db_content)
        {
            List<ShortGUIDDescriptor> toReturn = new List<ShortGUIDDescriptor>();

            MemoryStream readerStream = new MemoryStream(db_content);
            BinaryReader reader = new BinaryReader(readerStream);
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                ShortGUIDDescriptor thisDesc = new ShortGUIDDescriptor();
                thisDesc.ID = reader.ReadBytes(4);
                thisDesc.Description = reader.ReadString();
                toReturn.Add(thisDesc);
            }
            reader.Close();

            return toReturn;
        }

        private static List<ShortGUIDDescriptor> cathode_id_map;
        private static List<ShortGUIDDescriptor> node_friendly_names;
    }
}
