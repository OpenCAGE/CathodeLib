using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static CATHODE.Resources;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/REDS.BIN
    /// </summary>
    public class RenderableElements : CathodeFile
    {
        public List<Element> Entries = new List<Element>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        private List<Element> _writeList = new List<Element>();

        protected override bool HandlesLoadingManually => true;
        private Models _models;
        private Materials _materials;

        public RenderableElements(string path, Models models, Materials materials) : base(path)
        {
            _models = models;
            _materials = materials;

            _loaded = Load();
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    Element element = new Element();
                    element.ModelLocation = (PakLocation)reader.ReadInt32();
                    element.Model = _models.GetAtWriteIndex(reader.ReadInt32());
                    element.ModelSubplatformDependent = reader.ReadBoolean();
                    element.MaterialLocation = (PakLocation)reader.ReadInt32();
                    element.Material = _materials.GetAtWriteIndex(reader.ReadInt32());
                    element.MaterialSubplatformDependent = reader.ReadBoolean();
                    element.LODIndex = reader.ReadInt32();
                    element.LODCount = reader.ReadByte();
                    Entries.Add(element);
                }
            }
            _writeList.AddRange(Entries);
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
                    writer.Write(_models.GetWriteIndex(Entries[i].Model));
                    writer.Write(Entries[i].ModelSubplatformDependent);
                    writer.Write((int)Entries[i].MaterialLocation);
                    writer.Write(_materials.GetWriteIndex(Entries[i].Material));
                    writer.Write(Entries[i].MaterialSubplatformDependent);
                    writer.Write(Entries[i].LODIndex);
                    writer.Write((byte)Entries[i].LODCount);
                }
            }
            _writeList.Clear();
            _writeList.AddRange(Entries);
            return true;
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Get the current BIN index for a submesh (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public int GetWriteIndex(Element element)
        {
            if (!_writeList.Contains(element)) return -1;
            return _writeList.IndexOf(element);
        }

        /// <summary>
        /// Get a submesh by its current BIN index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public Element GetAtWriteIndex(int index)
        {
            if (_writeList.Count <= index || index < 0) return null;
            return _writeList[index];
        }
        #endregion

        #region STRUCTURES
        public class Element
        {
            public PakLocation ModelLocation = PakLocation.LEVEL;
            public Models.CS2.Component.LOD.Submesh Model = null;
            public bool ModelSubplatformDependent = false;

            public PakLocation MaterialLocation = PakLocation.LEVEL;
            public Materials.Material Material = null;
            public bool MaterialSubplatformDependent = false;

            public int LODIndex = -1; //This is the index of the REDS entry that we use for our LOD model/material
            public int LODCount = 0; //This is the number of entries sequentially from the LODIndex to use for the LOD from REDS
        }
        #endregion
    }
}