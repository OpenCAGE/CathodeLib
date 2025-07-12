using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/REDS.BIN */
    public class RenderableElements : CathodeFile
    {
        public List<Element> Entries = new List<Element>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public RenderableElements(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    Element element = new Element();
                    element.ModelLocation = (PakLocation)reader.ReadInt32();
                    element.ModelIndex = reader.ReadInt32();
                    element.ModelSubplatformDependent = reader.ReadBoolean();
                    element.MaterialLocation = (PakLocation)reader.ReadInt32();
                    element.MaterialIndex = reader.ReadInt32();
                    element.MaterialSubplatformDependent = reader.ReadBoolean();
                    element.LODIndex = reader.ReadInt32();
                    element.LODCount = reader.ReadByte();
                    Entries.Add(element);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write((int)Entries[i].ModelLocation);
                    writer.Write(Entries[i].ModelIndex);
                    writer.Write(Entries[i].ModelSubplatformDependent);
                    writer.Write((int)Entries[i].MaterialLocation);
                    writer.Write(Entries[i].MaterialIndex);
                    writer.Write(Entries[i].MaterialSubplatformDependent);
                    writer.Write(Entries[i].LODIndex);
                    writer.Write((byte)Entries[i].LODCount);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Element
        {
            public Element() { }
            public Element(int modelIndex, int materialIndex)
            {
                ModelIndex = modelIndex;
                MaterialIndex = materialIndex;
            }

            public PakLocation ModelLocation = PakLocation.LEVEL;
            public int ModelIndex = -1;
            public bool ModelSubplatformDependent = false;

            public PakLocation MaterialLocation = PakLocation.LEVEL;
            public int MaterialIndex = -1;
            public bool MaterialSubplatformDependent = false;

            public int LODIndex = -1; //This is the index of the REDS entry that we use for our LOD model/material
            public int LODCount = 0; //This is the number of entries sequentially from the LODIndex to use for the LOD from REDS
        }
        #endregion
    }
}