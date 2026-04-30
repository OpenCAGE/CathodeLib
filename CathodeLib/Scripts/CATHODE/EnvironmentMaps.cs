using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using static CATHODE.Movers;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/ENVIRONMENTMAP.BIN
    /// </summary>
    public class EnvironmentMaps : CathodeFile
    {
        public List<Mapping> Entries = new List<Mapping>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE | Implementation.CREATE;

        protected override bool HandlesLoadingManually => true;
        private Movers _movers;
        private Textures _textures;

        private List<Mapping> _writeList = new List<Mapping>();

        public EnvironmentMaps(string path, Movers movers, Textures textures) : base(path)
        {
            _movers = movers;
            _textures = textures;

            _loaded = Load();
        }

        public void ClearReferences()
        {
            _movers = null;
            _textures = null;
        }

        ~EnvironmentMaps()
        {
            ClearReferences();
            Entries.Clear();
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            Dictionary<int, List<Mapping>> envMapIndexes = new Dictionary<int, List<Mapping>>();

            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position += 8;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    Mapping entry = new Mapping();
                    entry.Mover = _movers.GetAtWriteIndex(reader.ReadInt32());
                    int envMapIndex = reader.ReadInt32();
                    if (envMapIndexes.ContainsKey(envMapIndex))
                        envMapIndexes[envMapIndex].Add(entry);
                    else
                        envMapIndexes.Add(envMapIndex, new List<Mapping>() { entry });
                    Entries.Add(entry);
                    _writeList.Add(entry);
                }
            }

            foreach (KeyValuePair<int, List<Mapping>> index in envMapIndexes)
            {
                Textures.TEX4 tex = index.Key == -1 ? null : _textures.GetAtWriteIndexForEnvMap(index.Key);
                foreach (Mapping mapping in index.Value)
                {
                    mapping.EnvironmentMap = tex;
                }
            }

            return true;
        }

        override protected bool SaveInternal()
        {
            List<Mapping> orderedEntries = Entries.OrderBy(o => _movers.GetWriteIndex(o.Mover)).ToList();

            byte[][] entryBuffers = new byte[orderedEntries.Count][];
            Parallel.For(0, orderedEntries.Count, i =>
            {
                entryBuffers[i] = SerializeMapping(orderedEntries[i]);
            });

            int totalEnvMaps = 0;
            foreach (Textures.TEX4 tex in _textures.Entries)            
            {
                if (tex.StateFlags.HasFlag(Textures.TextureStateFlag.CUBE))
                    totalEnvMaps++;
            }

            _writeList.Clear();
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                Utilities.WriteString("envm", writer);
                writer.Write(1);
                writer.Write(Entries.Count);
                for (int i = 0; i < entryBuffers.Length; i++)
                {
                    writer.Write(entryBuffers[i]);
                    _writeList.Add(Entries[i]);
                }
                writer.Write(totalEnvMaps);
            }
            return true;
        }

        private byte[] SerializeMapping(Mapping mapping)
        {
            using (MemoryStream stream = new MemoryStream(8)) 
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(_movers.GetWriteIndex(mapping.Mover));
                writer.Write(mapping.EnvironmentMap == null ? -1 : _textures.GetWriteIndexForEnvMap(mapping.EnvironmentMap));
                return stream.ToArray();
            }
        }
        #endregion

        //NOTE TO SELF - i need to update environmentmap_index indexes and Texture_Index in Commands when saving!
        //               at runtime on intiialise of the entity they are queried to update params like Priority and Colour/EmissiveFactor.

        #region HELPERS
        /// <summary>
        /// Get the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public int GetWriteIndex(Mapping mapping)
        {
            if (!_writeList.Contains(mapping)) return -1;
            return _writeList.IndexOf(mapping);
        }

        /// <summary>
        /// Get the object at the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public Mapping GetAtWriteIndex(int index)
        {
            if (_writeList.Count <= index || index < 0) return null;
            return _writeList[index];
        }
        #endregion

        #region STRUCTURES
        public class Mapping : IEquatable<Mapping>
        {
            public Movers.MOVER_DESCRIPTOR Mover;
            public Textures.TEX4 EnvironmentMap;

            public static bool operator ==(Mapping x, Mapping y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return false;
                return x.Equals(y);
            }

            public static bool operator !=(Mapping x, Mapping y)
            {
                return !(x == y);
            }

            public bool Equals(Mapping other)
            {
                if (other == null) return false;
                if (ReferenceEquals(this, other)) return true;

                if (Mover != other.Mover) return false;
                if (EnvironmentMap != other.EnvironmentMap) return false;

                return true;
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as Mapping);
            }

            public override int GetHashCode()
            {
                int hashCode = 471264742;
                hashCode = hashCode * -1521134295 + EqualityComparer<Movers.MOVER_DESCRIPTOR>.Default.GetHashCode(Mover);
                hashCode = hashCode * -1521134295 + EqualityComparer<Textures.TEX4>.Default.GetHashCode(EnvironmentMap);
                return hashCode;
            }
        };
        #endregion
    }
}