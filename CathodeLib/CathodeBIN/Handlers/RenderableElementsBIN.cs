﻿using System.Collections.Generic;
using System.IO;

namespace CATHODE
{
    public class RenderableElementsBIN : CathodeBIN
    {
        private List<RenderableElement> renderable_elements = new List<RenderableElement>();

        /* Load the REDS.BIN */
        public RenderableElementsBIN(string pathToBin)
        {
            filepath = pathToBin;

            BinaryReader reader = new BinaryReader(File.OpenRead(filepath));

            renderable_elements.Capacity = reader.ReadInt32();
            ReadEntries(reader);

            reader.Close();
        }

        /* Save the REDS.BIN */
        public void Save()
        {
            BinaryWriter writer = new BinaryWriter(File.OpenWrite(filepath));

            writer.Write(renderable_elements.Count);
            foreach (RenderableElement reds_entry in renderable_elements)
            {
                writer.Write(0);
                writer.Write(reds_entry.model_index);
                writer.Write((char)0);
                writer.Write(0);
                writer.Write(reds_entry.material_index);
                writer.Write((char)0);
                writer.Write(reds_entry.unk1);
                writer.Write((char)reds_entry.unk2);
            }

            writer.Close();
        }

        /* Add a new REDs entry */
        public int AddRenderableElement(RenderableElement red_entry)
        {
            renderable_elements.Add(red_entry);
            return renderable_elements.Count - 1;
        }

        /* Get RED */
        public RenderableElement GetRenderableElement(int index)
        {
            if (index < 0 || index >= renderable_elements.Count ) return null;
            return renderable_elements[index];
        }

        /* Get REDs */
        public List<RenderableElement> GetRenderableElements()
        {
            return renderable_elements;
        }

        /* Get REDs count */
        public int GetRenderableElementsCount()
        {
            return renderable_elements.Count;
        }

        /* Read all renderable elements entries */
        private void ReadEntries(BinaryReader reader)
        {
            for (int i = 0; i < renderable_elements.Capacity; i++)
            {
                RenderableElement this_entry = new RenderableElement();
                reader.BaseStream.Position += 4;
                this_entry.model_index = reader.ReadInt32();
                reader.BaseStream.Position += 5;
                this_entry.material_index = reader.ReadInt32();
                reader.BaseStream.Position += 1;
                this_entry.unk1 = reader.ReadInt32();
                this_entry.unk2 = (int)reader.ReadChar();
                renderable_elements.Add(this_entry);
            }
        }
    }
}
