using CATHODE.Scripting;
using CathodeLib;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
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

        protected override bool HandlesLoadingManually => true;
        private Models _models;
        private Materials _materials;

        public bool Compressed { get { return _compressed; } set { _compressed = value; } }
        private bool _compressed = false;

        private List<Element> _writeList = new List<Element>();

        public RenderableElements(string path, Models models, Materials materials) : base(path)
        {
            _models = models;
            _materials = materials;

            _loaded = Load();
        }

        public void ClearReferences()
        {
            _models = null;
            _materials = null;
        }

        ~RenderableElements()
        {
            ClearReferences();
            Entries.Clear();
            _writeList.Clear();
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            _compressed = _filepath != null && _filepath != "" && Path.GetExtension(_filepath).ToLower() == ".gz";

            using (BinaryReader reader = new BinaryReader(_compressed ? Utilities.GZIPDecompress(stream) : stream))
            {
                List<Tuple<int, byte>> lods = new List<Tuple<int, byte>>();
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
                    lods.Add(new Tuple<int, byte>(reader.ReadInt32(), reader.ReadByte()));
                    Entries.Add(element);
                }
                for (int i = 0; i < entryCount; i++)
                    for (int x = 0; x < lods[i].Item2; x++)
                        Entries[i].LODs.Add(Entries[lods[i].Item1 + x]);
            }

            _writeList.AddRange(Entries);
            return true;
        }

        override protected bool SaveInternal()
        {
            byte[][] entryBuffers = new byte[Entries.Count][];
            Parallel.For(0, Entries.Count, i =>
            {
                entryBuffers[i] = SerializeElement(Entries[i]);
            });

            Stream stream = File.OpenWrite(_filepath);
            if (_compressed)
                stream = new GZipStream(stream, CompressionMode.Compress);

            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(Entries.Count);
                for (int i = 0; i < entryBuffers.Length; i++)
                    writer.Write(entryBuffers[i]);
            }
            stream.Close();

            _writeList.Clear();
            _writeList.AddRange(Entries);
            return true;
        }

        private byte[] SerializeElement(Element element)
        {
            using (MemoryStream stream = new MemoryStream(32)) 
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((int)element.ModelLocation);
                writer.Write(_models.GetWriteIndex(element.Model));
                writer.Write(element.ModelSubplatformDependent);
                writer.Write((int)element.MaterialLocation);
                writer.Write(_materials.GetWriteIndex(element.Material));
                writer.Write(element.MaterialSubplatformDependent);
                writer.Write(element.LODs.Count == 0 ? -1 : Entries.IndexOf(element.LODs[0]));
                writer.Write((byte)element.LODs.Count);
                return stream.ToArray();
            }
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Get the current write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public int GetWriteIndex(List<Element> element, int greaterThan = 0)
        {
            if (element == null || element.Count == 0)
                return -1;

            if (greaterThan >= Entries.Count)
                return -1;

            for (int i = greaterThan; i < Entries.Count; i++)
            {
                if (Entries[i] != element[0])
                    continue;

                if (element.Count == 1)
                {
                    return i;
                }
                else
                {
                    for (int x = 1; x < element.Count; x++)
                    {
                        if (Entries[i + x] != element[x])
                            break;
                        if (x == element.Count - 1)
                            return i;
                    }
                }
            }
            return -1;
        }

        /// <summary>
        /// Get the object at the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public List<Element> GetAtWriteIndex(int index, int count)
        {
            if (_writeList.Count < index + count || index < 0) 
                return new List<Element>();

            List<Element> elements = new List<Element>();
            for (int i = 0; i < count; i++)
                elements.Add(_writeList[index + i]);
            return elements;
        }

        /// <summary>
        /// Copy an entry into the file, along with all child objects.
        /// </summary>
        public List<Element> ImportEntry(List<Element> elements, Models sourceModels)
        {
            if (elements == null)
                return null;

            List<Element> newElements = new List<Element>();
            for (int i = 0; i < elements.Count; i++)
            {
                Element newElement = elements[i].Copy();

                if (newElement.ModelLocation == PakLocation.GLOBAL || newElement.MaterialLocation == PakLocation.GLOBAL)
                    throw new Exception("Unexpected model/material location - GLOBAL is unsupported.");
                
                Models.CS2 cs2 = _models.ImportEntry(sourceModels.FindModelForSubmesh(elements[i].Model)); //We add the WHOLE cs2, if it doesn't exist, even though we only point to a submesh of it
                newElement.Model = cs2.GetSubmesh(newElement.Model);
                newElement.Material = _materials.ImportEntry(newElement.Material);

                newElements.Add(newElement);
                Entries.Add(newElement);
            }

            //Add LODs after so they're also sequential 
            for (int i = 0; i < elements.Count; i++)
            {
                newElements[i].LODs = ImportEntry(newElements[i].LODs, sourceModels);
            }

            return newElements;
        }
        #endregion

        #region STRUCTURES
        public class Element : IEquatable<Element>
        {
            public PakLocation ModelLocation = PakLocation.LEVEL;
            public Models.CS2.Component.LOD.Submesh Model = null;
            public bool ModelSubplatformDependent = false;

            public PakLocation MaterialLocation = PakLocation.LEVEL;
            public Materials.Material Material = null;
            public bool MaterialSubplatformDependent = false;

            public List<Element> LODs = new List<Element>();

            //see RenderableElementCache::process_renderable_element_descriptors_for_movers
            public static bool operator ==(Element x, Element y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
                if (x.ModelLocation != y.ModelLocation) return false;
                if (!ReferenceEquals(x.Model, y.Model)) return false;
                if (x.ModelSubplatformDependent != y.ModelSubplatformDependent) return false;
                if (x.MaterialLocation != y.MaterialLocation) return false;
                if (!ReferenceEquals(x.Material, y.Material)) return false;
                if (x.MaterialSubplatformDependent != y.MaterialSubplatformDependent) return false;
                if (!ListsEqual(x.LODs, y.LODs)) return false;
                return true;
            }

            public static bool operator !=(Element x, Element y)
            {
                return !(x == y);
            }

            public bool Equals(Element other)
            {
                return this == other;
            }

            public override bool Equals(object obj)
            {
                return obj is Element element && this == element;
            }

            public override int GetHashCode()
            {
                int hashCode = -1234567890;
                hashCode = hashCode * -1521134295 + ModelLocation.GetHashCode();
                hashCode = hashCode * -1521134295 + (Model?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + ModelSubplatformDependent.GetHashCode();
                hashCode = hashCode * -1521134295 + MaterialLocation.GetHashCode();
                hashCode = hashCode * -1521134295 + (Material?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + MaterialSubplatformDependent.GetHashCode();
                hashCode = hashCode * -1521134295 + (LODs?.GetHashCode() ?? 0);
                return hashCode;
            }

            private static bool ListsEqual(List<Element> x, List<Element> y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return false;
                if (x.Count != y.Count) return false;
                for (int i = 0; i < x.Count; i++)
                {
                    if (x[i] != y[i]) return false;
                }
                return true;
            }
        }
        #endregion
    }
}