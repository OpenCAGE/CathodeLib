using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CATHODE.Commands
{
    //This should be initialised per-commandspak, and serves as a helpful extension to manage entity & composite names
    public class CompositeNameLookup
    {
        private CommandsPAK commandsPAK;
        private List<BinComposite> composites = new List<BinComposite>();

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
            return GetEntity(GetComposite(compositeID), entityID).name;
        }

        /* Set the name of an entity contained within a composite */
        public void SetEntityName(ShortGuid compositeID, ShortGuid entityID, string name)
        {
            GetEntity(GetComposite(compositeID), entityID).name = name;
        }

        /* Clear the name of an entity contained within a composite */
        public void ClearEntityName(ShortGuid compositeID, ShortGuid entityID)
        {
            BinComposite composite = GetComposite(compositeID);
            composite.entities.Remove(GetEntity(composite, entityID));
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
            int content_after_pak = (int)reader.BaseStream.Length - end_of_pak;
            if (content_after_pak == 0)
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
            BinaryWriter writer = new BinaryWriter(File.OpenWrite(commandsPAK.Filepath));
            writer.BaseStream.Position = writer.BaseStream.Length;
            writer.BaseStream.SetLength(0);
            writer.Write(composites.Count);
            for (int i = 0; i < composites.Count; i++)
            {
                Utilities.Write<ShortGuid>(writer, composites[i].id);
                writer.Write(composites[i].entities.Count);
                for (int x = 0; x < composites[i].entities.Count; x++)
                {
                    Utilities.Write<ShortGuid>(writer, composites[i].entities[x].id);
                    writer.Write(composites[i].entities[x].name);
                }
            }
            writer.Close();
        }

        /* Consume our stored entity info */
        private void ConsumeDatabase(BinaryReader reader, bool containsPath)
        {
            int compCount = reader.ReadInt32();
            for (int i = 0; i < compCount; i++)
            {
                BinComposite composite = GetComposite(Utilities.Consume<ShortGuid>(reader));
                if (containsPath) composite.path = reader.ReadString();
                int entityCount = reader.ReadInt32();
                for (int x = 0; x < entityCount; x++)
                {
                    BinEntity entity = GetEntity(composite, Utilities.Consume<ShortGuid>(reader));
                    entity.name = reader.ReadString();
                }
            }
        }

        /* Get a composite info object */
        private BinComposite GetComposite(ShortGuid compositeID)
        {
            BinComposite composite = composites.FirstOrDefault(o => o.id == compositeID);
            if (composite == null)
            {
                composite = new BinComposite();
                composite.id = compositeID;
                composites.Add(composite);
            }
            return composite;
        }

        /* Get an entity info object */
        private BinEntity GetEntity(BinComposite composite, ShortGuid entityID)
        {
            BinEntity entity = composite.entities.FirstOrDefault(o => o.id == entityID);
            if (entity == null)
            {
                entity = new BinEntity();
                entity.id = entityID;
                entity.name = entityID.ToString();
                composite.entities.Add(entity);
            }
            return entity;
        }

        private class BinComposite
        {
            public ShortGuid id;
            public string path;
            public List<BinEntity> entities = new List<BinEntity>();
        }
        private class BinEntity
        {
            public ShortGuid id;
            public string name = "";
        }
    }
}
