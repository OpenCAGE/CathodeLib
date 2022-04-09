using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace CATHODE.Commands
{
    //This should be initialised per-commandspak, and serves as a helpful extension to manage entity & composite names
    public class CompositeNameLookup
    {
        private CommandsPAK commandsPAK;
        private Dictionary<ShortGuid, Dictionary<ShortGuid, string>> composites;

        public CompositeNameLookup(CommandsPAK commands)
        {
            commandsPAK = commands;
            commandsPAK.OnSaved += SaveCustomNames;

            LoadVanillaNames();
            LoadCustomNames();
        }


        /* Get the name of an entity contained within a composite */
        public string GetEntityName(ShortGuid compositeID, ShortGuid entityID)
        {
            if (!composites.ContainsKey(compositeID) || !composites[compositeID].ContainsKey(entityID))
                return entityID.ToString();

            return composites[compositeID][entityID];
        }

        /* Set the name of an entity contained within a composite */
        public void SetEntityName(ShortGuid compositeID, ShortGuid entityID, string name)
        {
            composites[compositeID][entityID] = name;
        }

        /* Clear the name of an entity contained within a composite */
        public void ClearEntityName(ShortGuid compositeID, ShortGuid entityID)
        {
            composites[compositeID].Remove(entityID);
        }


        /* Load all standard entity/composite names from our offline DB */
        private void LoadVanillaNames()
        {
            BinaryReader reader = new BinaryReader(new MemoryStream(CathodeLib.Properties.Resources.composite_entity_names));
            ConsumeDatabase(reader);
            reader.Close();
        }

        /* Pull non-vanilla entity names from the CommandsPAK */
        private void LoadCustomNames()
        {
            BinaryReader reader = new BinaryReader(File.OpenRead(commandsPAK.Filepath));
            reader.BaseStream.Position = 20;
            int end_of_pak = (reader.ReadInt32() * 4) + (reader.ReadInt32() * 4);
            reader.BaseStream.Position = end_of_pak;
            if ((int)reader.BaseStream.Length - end_of_pak == 0)
            {
                reader.Close();
                return;
            }
            if (reader.ReadByte() != 0xAB) //This is used to "version" our write at the end of the file
            {
                reader.Close();
                return;
            }
            ConsumeDatabase(reader);
            reader.Close();
        }

        /* Write non-vanilla entity names to the CommandsPAK */
        private void SaveCustomNames()
        {
            Stream writer = File.OpenWrite(commandsPAK.Filepath);
            writer.Position = writer.Length;
            writer.Write((char)0xAB);
            writer.Close();
        }

        /* Consume our stored entity info */
        private void ConsumeDatabase(BinaryReader reader)
        {
            int compCount = reader.ReadInt32();
            composites = new Dictionary<ShortGuid, Dictionary<ShortGuid, string>>(compCount);
            for (int i = 0; i < compCount; i++)
            {
                ShortGuid compositeID = Utilities.Consume<ShortGuid>(reader);
                int entityCount = reader.ReadInt32();
                composites.Add(compositeID, new Dictionary<ShortGuid, string>(entityCount));
                for (int x = 0; x < entityCount; x++)
                {
                    ShortGuid entityID = Utilities.Consume<ShortGuid>(reader);
                    composites[compositeID].Add(entityID, reader.ReadString());
                }
            }
        }
    }
}
