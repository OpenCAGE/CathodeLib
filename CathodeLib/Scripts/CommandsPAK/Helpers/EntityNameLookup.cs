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
    public class EntityNameLookup
    {
        private CommandsPAK commandsPAK;
        private Dictionary<ShortGuid, Dictionary<ShortGuid, string>> vanilla_composites;
        private Dictionary<ShortGuid, Dictionary<ShortGuid, string>> custom_composites;

        public EntityNameLookup(CommandsPAK commands = null)
        {
            commandsPAK = commands;
            if (commandsPAK != null) 
                commandsPAK.OnSaved += SaveCustomNames;

            LoadVanillaNames();
            LoadCustomNames();
        }

#if DEBUG
        /* For testing only: get from any composite */
        public string GetFromAnyComposite(ShortGuid id)
        {
            foreach (KeyValuePair<ShortGuid, Dictionary<ShortGuid, string>> composite in vanilla_composites)
            {
                foreach (KeyValuePair<ShortGuid, string> entity in composite.Value)
                {
                    if (composite.Key == id) return entity.Value;
                }
            }
            return id.ToString();
        }
#endif

        /* Get the name of an entity contained within a composite */
        public string GetEntityName(ShortGuid compositeID, ShortGuid entityID)
        {
            if (custom_composites != null)
                if (custom_composites.ContainsKey(compositeID) && custom_composites[compositeID].ContainsKey(entityID))
                    return custom_composites[compositeID][entityID];
            if (vanilla_composites != null)
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
            vanilla_composites = ConsumeDatabase(reader, reader.ReadInt32());
            reader.Close();
        }

        /* Pull non-vanilla entity names from the CommandsPAK */
        private void LoadCustomNames()
        {
            if (commandsPAK == null) return;
            int endPos = GetEndOfCommands();
            BinaryReader reader = new BinaryReader(File.OpenRead(commandsPAK.Filepath));
            reader.BaseStream.Position = endPos;
            if ((int)reader.BaseStream.Length - endPos == 0 || reader.ReadByte() != (byte)254)
            {
                custom_composites = new Dictionary<ShortGuid, Dictionary<ShortGuid, string>>();
                reader.Close();
                return;
            }
            reader.BaseStream.Position += 4;
            int compCount = reader.ReadInt32();
            reader.BaseStream.Position += 8;
            custom_composites = ConsumeDatabase(reader, compCount);
            reader.Close();
        }

        /* Write non-vanilla entity names to the CommandsPAK */
        private void SaveCustomNames()
        {
            if (commandsPAK == null) return;
            int endPos = GetEndOfCommands();
            //TODO: move this writing functionality into its own thing
            BinaryWriter writer = new BinaryWriter(File.OpenWrite(commandsPAK.Filepath));
            bool hasAlreadyWritten = (int)writer.BaseStream.Length - endPos != 0;
            int posToJumpBackTo = 0;
            writer.BaseStream.Position = endPos;
            writer.Write((byte)254);
            writer.Write((int)writer.BaseStream.Position + 16);
            writer.Write(custom_composites.Count);
            if (hasAlreadyWritten)
            {
                writer.BaseStream.Position += 8;
            }
            else
            {
                posToJumpBackTo = (int)writer.BaseStream.Position;
                writer.Write(0);
                writer.Write(0);
            }
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
            int posToWrite = (int)writer.BaseStream.Position;
            writer.BaseStream.Position = posToJumpBackTo;
            writer.Write(posToWrite);
            writer.Close();
        }

        /* Consume our stored entity info */
        private Dictionary<ShortGuid, Dictionary<ShortGuid, string>> ConsumeDatabase(BinaryReader reader, int compCount)
        {
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

        /* Get the position where we start writing our own data */
        private int GetEndOfCommands()
        {
            BinaryReader reader = new BinaryReader(File.OpenRead(commandsPAK.Filepath));
            reader.BaseStream.Position = 20;
            int end_of_pak = (reader.ReadInt32() * 4) + (reader.ReadInt32() * 4);
            reader.Close();
            return end_of_pak;
        }
    }
}
