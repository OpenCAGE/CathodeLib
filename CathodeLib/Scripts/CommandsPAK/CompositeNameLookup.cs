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
        private Dictionary<ShortGuid, Dictionary<ShortGuid, string>> vanilla_composites;
        private Dictionary<ShortGuid, Dictionary<ShortGuid, string>> custom_composites;

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
            if (custom_composites.ContainsKey(compositeID) && custom_composites[compositeID].ContainsKey(entityID))
                return custom_composites[compositeID][entityID];
            if (vanilla_composites.ContainsKey(compositeID) && vanilla_composites[compositeID].ContainsKey(entityID))
                return vanilla_composites[compositeID][entityID];
            return entityID.ToString();
        }

        /* Set the name of an entity contained within a composite */
        public void SetEntityName(ShortGuid compositeID, ShortGuid entityID, string name)
        {
            if (!custom_composites.ContainsKey(compositeID))
                custom_composites.Add(compositeID, new Dictionary<ShortGuid, string>());

            if (!custom_composites[compositeID].ContainsKey(entityID))
                custom_composites[compositeID].Add(entityID, name);
            else
                custom_composites[compositeID][entityID] = name;
        }

        /* Clear the name of an entity contained within a composite */
        public void ClearEntityName(ShortGuid compositeID, ShortGuid entityID)
        {
            if (custom_composites.ContainsKey(compositeID))
                vanilla_composites[compositeID].Remove(entityID);
        }


        /* Load all standard entity/composite names from our offline DB */
        private void LoadVanillaNames()
        {
            BinaryReader reader = new BinaryReader(new MemoryStream(CathodeLib.Properties.Resources.composite_entity_names));
            vanilla_composites = ConsumeDatabase(reader);
            reader.Close();
        }

        /* Pull non-vanilla entity names from the CommandsPAK */
        private void LoadCustomNames()
        {
            BinaryReader reader = new BinaryReader(File.OpenRead(commandsPAK.Filepath));
            reader.BaseStream.Position = 20;
            int end_of_pak = (reader.ReadInt32() * 4) + (reader.ReadInt32() * 4);
            reader.BaseStream.Position = end_of_pak;
            if ((int)reader.BaseStream.Length - end_of_pak == 0 || reader.ReadByte() != (byte)255)
            {
                //0xAB above is used to "version" our write at the end of the file, to drop support for the old methods
                custom_composites = new Dictionary<ShortGuid, Dictionary<ShortGuid, string>>();
                reader.Close();
                return;
            }
            custom_composites = ConsumeDatabase(reader);
            reader.Close();
        }

        /* Write non-vanilla entity names to the CommandsPAK */
        private void SaveCustomNames()
        {
            BinaryWriter writer = new BinaryWriter(File.OpenWrite(commandsPAK.Filepath));
            writer.BaseStream.Position = writer.BaseStream.Length;
            writer.Write((byte)255);
            writer.Write(custom_composites.Count);
            foreach (KeyValuePair<ShortGuid, Dictionary<ShortGuid, string>> composite in custom_composites)
            {
                Utilities.Write<ShortGuid>(writer, composite.Key);
                writer.Write(composite.Value.Count);
                foreach (KeyValuePair<ShortGuid, string> entity in composite.Value)
                {
                    Utilities.Write<ShortGuid>(writer, entity.Key);
                    writer.Write(entity.Value);
                }
            }
            writer.Close();
        }

        /* Consume our stored entity info */
        private Dictionary<ShortGuid, Dictionary<ShortGuid, string>> ConsumeDatabase(BinaryReader reader)
        {
            int compCount = reader.ReadInt32();
            Dictionary<ShortGuid, Dictionary<ShortGuid, string>> composites = new Dictionary<ShortGuid, Dictionary<ShortGuid, string>>(compCount);
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
            Console.WriteLine("Loaded names for " + compCount + " composites!");
            return composites;
        }
    }
}
