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
        private Dictionary<ShortGuid, List<BinEntity>> composites = new Dictionary<ShortGuid, List<BinEntity>>();

        public CompositeNameLookup(CommandsPAK commands)
        {
            commandsPAK = commands;
            commandsPAK.OnSaved += SaveCustomNames;

            LoadVanillaNames();
            LoadCustomNames();

            //TODO: apply PATH here?
        }


        /* Get the name of an entity contained within a composite */
        public string GetEntityName(ShortGuid compositeID, ShortGuid entityID)
        {
            return GetEntity(compositeID, entityID).name;
        }

        /* Set the name of an entity contained within a composite */
        public void SetEntityName(ShortGuid compositeID, ShortGuid entityID, string name)
        {
            GetEntity(compositeID, entityID).SetNewName(name);
        }

        /* Clear the name of an entity contained within a composite */
        public void ClearEntityName(ShortGuid compositeID, ShortGuid entityID)
        {
            if (!composites.ContainsKey(compositeID)) return;
            composites[compositeID].Remove(composites[compositeID].FirstOrDefault(o => o.id == entityID));
        }


        /* Load all standard entity/composite names from our offline DB */
        private void LoadVanillaNames()
        {
            BinaryReader reader = new BinaryReader(new MemoryStream(CathodeLib.Properties.Resources.composite_entity_names));
            ConsumeDatabase(reader, true);
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
            ConsumeDatabase(reader, false);
            reader.Close();
        }

        /* Write non-vanilla entity names to the CommandsPAK */
        private void SaveCustomNames()
        {
            Stream writer = File.OpenWrite(commandsPAK.Filepath);
            writer.Position = writer.Length;
            new BinaryFormatter().Serialize(writer, composites);
            writer.Close();
        }

        private void ConsumeDatabase(BinaryReader reader, bool containsPath)
        {
            int compCount = reader.ReadInt32();
            for (int i = 0; i < compCount; i++)
            {
                ShortGuid compositeID = Utilities.Consume<ShortGuid>(reader);
                if (containsPath) reader.ReadString();
                int entityCount = reader.ReadInt32();
                List<BinEntity> entities = new List<BinEntity>(entityCount);
                for (int x = 0; x < entityCount; x++)
                {
                    BinEntity entity = new BinEntity();
                    entity.id = Utilities.Consume<ShortGuid>(reader);
                    entity.name = reader.ReadString();
                    entities.Add(entity);
                }
                composites.Add(compositeID, entities);
            }
        }

        /* Get an entity info object */
        private BinEntity GetEntity(ShortGuid compositeID, ShortGuid entityID)
        {
            if (!composites.ContainsKey(compositeID))
                composites.Add(compositeID, new List<BinEntity>());

            BinEntity entity = composites[compositeID].FirstOrDefault(o => o.id == entityID);
            if (entity.name == null)
            {
                entity = new BinEntity();
                entity.id = entityID;
                entity.name = entityID.ToString();
                composites[compositeID].Add(entity);
            }
            return entity;
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BinEntity
        {
            public ShortGuid id;
            public string name;

            public void SetNewName(string _n)
            {
                name = _n;
            }
        }
    }
}
