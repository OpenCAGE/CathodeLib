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

        //Where each element sits in Entries, so a run can be found without scanning the table.
        //Two lookups, because GetWriteIndex answers reference matches before value matches and the
        //distinction is load-bearing: retail keeps value-identical duplicate runs on purpose.
        //Element's own GetHashCode is not usable as a key (it hashes LODs by reference while ==
        //compares it by value), hence ElementIdentity.
        private Dictionary<Element, List<int>> _startsByRef = null;
        private Dictionary<Element, List<int>> _startsByValue = null;
        private List<Element> _startsList = null;
        private int _startsCount = 0;
        //Save serialises elements in parallel and each one resolves its LOD run here.
        private readonly object _startsLock = new object();

        private sealed class ElementReference : IEqualityComparer<Element>
        {
            public static readonly ElementReference Instance = new ElementReference();
            public bool Equals(Element x, Element y) { return ReferenceEquals(x, y); }
            public int GetHashCode(Element element) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(element); }
        }

        private sealed class ElementIdentity : IEqualityComparer<Element>
        {
            public static readonly ElementIdentity Instance = new ElementIdentity();
            public bool Equals(Element x, Element y) { return x == y; }
            public int GetHashCode(Element element)
            {
                if (element == null) return 0;
                int hash = (int)element.ModelLocation;
                hash = hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(element.Model);
                hash = hash * 31 + (element.ModelSubplatformDependent ? 1 : 0);
                hash = hash * 31 + (int)element.MaterialLocation;
                hash = hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(element.Material);
                hash = hash * 31 + (element.MaterialSubplatformDependent ? 1 : 0);
                hash = hash * 31 + (element.LODs == null ? 0 : element.LODs.Count);
                return hash;
            }
        }

        //Entries is only ever appended to, so the lookups are extended rather than rebuilt.
        private void EnsureStartIndex()
        {
            if (_startsByRef == null || !ReferenceEquals(_startsList, Entries) || _startsCount > Entries.Count)
            {
                _startsByRef = new Dictionary<Element, List<int>>(ElementReference.Instance);
                _startsByValue = new Dictionary<Element, List<int>>(ElementIdentity.Instance);
                _startsList = Entries;
                _startsCount = 0;
            }
            for (; _startsCount < Entries.Count; _startsCount++)
            {
                Element entry = Entries[_startsCount];
                if (entry == null) continue;
                AddStart(_startsByRef, entry, _startsCount);
                AddStart(_startsByValue, entry, _startsCount);
            }
        }

        private static void AddStart(Dictionary<Element, List<int>> index, Element element, int at)
        {
            List<int> starts;
            if (!index.TryGetValue(element, out starts))
                index[element] = starts = new List<int>(1);
            starts.Add(at);
        }

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
            if (_compressed && Path.GetExtension(_filepath).ToLower() != ".gz")
                _filepath += ".gz";
            else if (!_compressed && Path.GetExtension(_filepath).ToLower() == ".gz")
                _filepath = _filepath.Substring(0, _filepath.Length - 3);

            byte[][] entryBuffers = new byte[Entries.Count][];
            Parallel.For(0, Entries.Count, i =>
            {
                entryBuffers[i] = SerializeElement(Entries[i]);
            });

            using (Stream stream = File.OpenWrite(_filepath))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(Entries.Count);
                for (int i = 0; i < entryBuffers.Length; i++)
                    writer.Write(entryBuffers[i]);
            }

            if (_compressed)
                Utilities.GZIPCompress(_filepath);

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
                writer.Write((element.LODs == null || element.LODs.Count == 0) ? -1 : GetWriteIndex(element.LODs));
                writer.Write((byte)(element.LODs?.Count ?? 0));
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

            lock (_startsLock)
            {
                EnsureStartIndex();

                //Reference match first: retail's file holds VALUE-identical duplicate runs on purpose
                //(every instanced FX mover gets its own entry), and matching by value alone collapsed
                //them all onto the first copy on save - a plain load+save moved every fogsphere mover's
                //redsIndex, which is a fidelity loss even before instancing edits anything.
                List<int> starts;
                if (_startsByRef.TryGetValue(element[0], out starts))
                {
                    for (int s = 0; s < starts.Count; s++)
                    {
                        int i = starts[s];
                        if (i < greaterThan) continue;
                        bool all = true;
                        for (int x = 1; x < element.Count; x++)
                            if (i + x >= Entries.Count || !ReferenceEquals(Entries[i + x], element[x])) { all = false; break; }
                        if (all)
                            return i;
                    }
                }

                if (_startsByValue.TryGetValue(element[0], out starts))
                {
                    for (int s = 0; s < starts.Count; s++)
                    {
                        int i = starts[s];
                        if (i < greaterThan) continue;
                        if (element.Count == 1)
                            return i;
                        bool all = true;
                        for (int x = 1; x < element.Count; x++)
                            if (i + x >= Entries.Count || Entries[i + x] != element[x]) { all = false; break; }
                        if (all)
                            return i;
                    }
                }
                return -1;
            }
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
        /// Append a fresh copy of a run and return the copies, even when a value-identical run is
        /// already registered. Retail gives every instanced FX mover (fogsphere / particle /
        /// ribbon) its OWN renderable entry rather than sharing the composite resource's - the
        /// per-instance entry is the mover's identity to the engine.
        /// </summary>
        public List<Element> RegisterDuplicateRun(List<Element> elements)
        {
            if (elements == null || elements.Count == 0)
                return elements;
            var copies = new List<Element>(elements.Count);
            foreach (Element el in elements)
            {
                if (el == null) { copies.Add(null); continue; }
                copies.Add(new Element
                {
                    ModelLocation = el.ModelLocation,
                    Model = el.Model,
                    ModelSubplatformDependent = el.ModelSubplatformDependent,
                    MaterialLocation = el.MaterialLocation,
                    Material = el.Material,
                    MaterialSubplatformDependent = el.MaterialSubplatformDependent,
                    LODs = el.LODs != null ? new List<Element>(el.LODs) : new List<Element>()
                });
            }
            lock (Entries)
            {
                foreach (Element el in copies)
                    if (el != null)
                        Entries.Add(el);
            }
            return copies;
        }

        /// <summary>
        /// Ensure a sequence of renderable elements are registered.
        /// </summary>
        public List<Element> EnsureRegistered(List<Element> elements)
        {
            return EnsureRegistered(elements, 0);
        }

        /// <summary>
        /// Ensure a sequence of renderable elements are registered as a contiguous run, with each
        /// element's LOD chain placed after it.
        /// </summary>
        public List<Element> EnsureRegistered(List<Element> elements, int mustStartAfter)
        {
            if (elements == null || elements.Count == 0)
                return elements ?? new List<Element>();

            // Reuse a run that is already registered rather than appending a second copy: retail's
            // file holds one run per distinct renderable set, not one per user. Appending per user
            // grew the file to ~1.6x retail and made registration quadratic (BSP_LV426_Pt01's
            // instancing pass went from 250s to 44s once this was restored).
            if (GetWriteIndex(elements, mustStartAfter) >= 0)
                return elements;
            
            var parentIndices = new List<int>(elements.Count);
            for (int i = 0; i < elements.Count; i++)
            {
                Element el = elements[i];
                if (el == null)
                {
                    parentIndices.Add(-1);
                    continue;
                }
                parentIndices.Add(Entries.Count);
                Entries.Add(el);
            }

            for (int i = 0; i < elements.Count; i++)
            {
                Element el = elements[i];
                if (el?.LODs == null || el.LODs.Count == 0)
                    continue;
                el.LODs = EnsureRegistered(el.LODs, parentIndices[i] + 1);
            }

            return elements;
        }

        /// <summary>
        /// Copy an entry into the file, along with all child objects.
        /// </summary>
        public List<Element> ImportEntry(List<Element> elements, Models sourceModels, bool overwriteExisting = false)
        {
            if (elements == null)
                return null;

            List<Element> newElements = new List<Element>();
            for (int i = 0; i < elements.Count; i++)
            {
                Element newElement = elements[i].Copy();

                if (newElement.ModelLocation == PakLocation.GLOBAL || newElement.MaterialLocation == PakLocation.GLOBAL)
                    throw new Exception("Unexpected model/material location - GLOBAL is unsupported.");
                
                Models.CS2 sourceCs2 = sourceModels.FindModel(elements[i].Model);
                Models.CS2 cs2 = _models.ImportEntry(sourceCs2, overwriteExisting); //We add the WHOLE cs2, if it doesn't exist, even though we only point to a submesh of it
                newElement.Model = cs2?.GetCorrespondingSubmesh(sourceCs2, elements[i].Model);
                newElement.Material = _materials.ImportEntry(newElement.Material, overwriteExisting);

                newElements.Add(newElement);
                Entries.Add(newElement);
            }

            //Add LODs after so they're also sequential 
            for (int i = 0; i < elements.Count; i++)
            {
                newElements[i].LODs = ImportEntry(newElements[i].LODs, sourceModels, overwriteExisting);
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

            /// <summary>
            /// Copy this element. The model, the material and the LOD elements are SHARED with the
            /// original, not cloned - an element is a three-field pointer record, and the things it
            /// points at belong to the level's model and material tables.
            /// </summary>
            public Element Copy()
            {
                return new Element()
                {
                    ModelLocation = ModelLocation,
                    Model = Model,
                    ModelSubplatformDependent = ModelSubplatformDependent,
                    MaterialLocation = MaterialLocation,
                    Material = Material,
                    MaterialSubplatformDependent = MaterialSubplatformDependent,
                    LODs = LODs == null ? null : new List<Element>(LODs),
                };
            }

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