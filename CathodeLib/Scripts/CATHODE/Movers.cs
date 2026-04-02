using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System;
using System.Threading.Tasks;
using CATHODE.Scripting;
using CathodeLib;
using CathodeLib.ObjectExtensions;
using System.Linq;
using System.IO.Compression;


#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/MODELS.MVR
    /// </summary>
    public class Movers : CathodeFile
    {
        public List<MOVER_DESCRIPTOR> Entries = new List<MOVER_DESCRIPTOR>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE;

        protected override bool HandlesLoadingManually => true;
        private RenderableElements _reds;
        private Resources _resources;

        public bool Compressed { get { return _compressed; } set { _compressed = value; } }
        private bool _compressed = false;

        private List<MOVER_DESCRIPTOR> _writeList = new List<MOVER_DESCRIPTOR>();

        public Movers(string path, RenderableElements reds, Resources resources) : base(path)
        {
            _reds = reds;
            _resources = resources;

            _loaded = Load();
        }

        public void ClearReferences()
        {
            _reds = null;
            _resources = null;
        }

        ~Movers()
        {
            ClearReferences();
            Entries.Clear();
            _writeList.Clear();
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            _compressed = _filepath != null && _filepath != "" && Path.GetExtension(_filepath).ToLower() == ".gz";

            //note: first 12 always renderable but not linked to commands -> they are always the same models across every level. is it the content of GLOBAL?

            using (BinaryReader reader = new BinaryReader(_compressed ? Utilities.GZIPDecompress(stream) : stream))
            {
                reader.BaseStream.Position += 4;
                int entryCount = reader.ReadInt32();
                reader.BaseStream.Position += 24;
                
                for (int i = 0; i < entryCount; i++)
                {
                    MOVER_DESCRIPTOR mvr = new MOVER_DESCRIPTOR();
                    mvr.transform = Utilities.Consume<Matrix4x4>(reader);
                    mvr.gpu_constants = Utilities.ConsumeArray<float>(reader, 24);
                    mvr.render_constants = Utilities.ConsumeArray<float>(reader, 21);
                    int redsIndex = reader.ReadInt32();
                    int redsCount = reader.ReadInt32();
                    mvr.renderable_elements = _reds.GetAtWriteIndex(redsIndex, redsCount);
                    mvr.resource = _resources.GetAtWriteIndex(reader.ReadInt32()); //todo - is this not looked up by the id in resources?
                    reader.BaseStream.Position += 12;
                    mvr.cull_flags = (CullFlag)reader.ReadInt32();
                    mvr.entity = Utilities.Consume<EntityHandle>(reader);
                    mvr.environment_map_index = reader.ReadInt32();
                    mvr.emissive_tint = new Vector3(reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                    mvr.emissive_flags = (EmissiveFlag)reader.ReadByte();
                    mvr.emissive_intensity_multiplier = reader.ReadSingle();
                    mvr.emissive_radiosity_multiplier = reader.ReadSingle();
                    mvr.primary_zone_id = Utilities.Consume<ShortGuid>(reader);
                    mvr.secondary_zone_id = Utilities.Consume<ShortGuid>(reader);
                    mvr.lighting_master_id = reader.ReadInt32();
                    reader.BaseStream.Position += 2;
                    mvr.flags = Utilities.Consume<MoverFlag>(reader);
                    reader.BaseStream.Position += 8;
                    Entries.Add(mvr);
                }
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

            int non_stationary = 0;
            for (int i = 0; i < Entries.Count; i++)
                if (!Entries[i].flags.stationary)
                    non_stationary++;

            byte[][] entryBuffers = new byte[Entries.Count][];
            Parallel.For(0, Entries.Count, i =>
            {
                entryBuffers[i] = SerializeEntry(Entries[i]);
            });

            using (Stream stream = File.OpenWrite(_filepath))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.BaseStream.SetLength(0);
                writer.Write((Entries.Count * 320) + 32);
                writer.Write(Entries.Count);
                writer.Write(non_stationary);
                writer.Write(0);
                writer.Write(320);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                for (int i = 0; i < entryBuffers.Length; i++)
                    writer.Write(entryBuffers[i]);
            }

            if (_compressed)
                Utilities.GZIPCompress(_filepath);

            _writeList.Clear();
            _writeList.AddRange(Entries);
            return true;
        }

        private byte[] SerializeEntry(MOVER_DESCRIPTOR entry)
        {
            using (MemoryStream stream = new MemoryStream(320))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                Utilities.Write<Matrix4x4>(writer, entry.transform);
                for (int x = 0; x < 24; x++)
                    writer.Write(entry.gpu_constants[x]);
                for (int x = 0; x < 21; x++)
                    writer.Write(entry.render_constants[x]);
                if (entry.renderable_elements.Count == 0)
                {
                    writer.Write(-1);
                    writer.Write(-1);
                }
                else
                {
                    writer.Write(_reds.GetWriteIndex(entry.renderable_elements));
                    writer.Write(entry.renderable_elements.Count);
                }
                writer.Write(_resources.GetWriteIndex(entry.resource));
                writer.Write(new byte[12]);
                writer.Write((int)entry.cull_flags);
                Utilities.Write<EntityHandle>(writer, entry.entity);
                writer.Write(entry.environment_map_index);
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                writer.Write((byte)entry.emissive_tint.x);
                writer.Write((byte)entry.emissive_tint.y);
                writer.Write((byte)entry.emissive_tint.z);
#else
                writer.Write((byte)entry.emissive_tint.X);
                writer.Write((byte)entry.emissive_tint.Y);
                writer.Write((byte)entry.emissive_tint.Z);
#endif
                writer.Write((byte)entry.emissive_flags);
                writer.Write(entry.emissive_intensity_multiplier);
                writer.Write(entry.emissive_radiosity_multiplier);
                Utilities.Write<ShortGuid>(writer, entry.primary_zone_id);
                Utilities.Write<ShortGuid>(writer, entry.secondary_zone_id);
                writer.Write(entry.lighting_master_id);
                writer.Write((Int16)(-1)); //todo - sanity check this is actually -1 not 0
                Utilities.Write<MoverFlag>(writer, entry.flags);
                writer.Write(new byte[8]);

                return stream.ToArray();
            }
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Get the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public int GetWriteIndex(MOVER_DESCRIPTOR mover)
        {
            if (!_writeList.Contains(mover)) return -1;
            return _writeList.IndexOf(mover);
        }

        /// <summary>
        /// Get the object at the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public MOVER_DESCRIPTOR GetAtWriteIndex(int index)
        {
            if (_writeList.Count <= index || index < 0) return null;
            return _writeList[index];
        }

        /// <summary>
        /// Copy an entry into the file, along with all child objects.
        /// </summary>
        public MOVER_DESCRIPTOR ImportEntry(MOVER_DESCRIPTOR mover, Models models)
        {
            if (mover == null)
                return null;

            MOVER_DESCRIPTOR newMover = mover.Copy();

            newMover.renderable_elements = _reds.ImportEntry(newMover.renderable_elements, models);
            newMover.resource = _resources.ImportEntry(newMover.resource);

            //todo: do something with entity reference

            //todo: env map index

            //todo: set zone to global?

            var existing = Entries.FirstOrDefault(o => o == newMover);
            if (existing != null)
                return existing;

            Entries.Add(newMover);
            return newMover;
        }
        #endregion

        #region STRUCTURES
        [Flags]
        public enum CullFlag : int
        {
            NO_CAST_SHADOWS = 1 << 0,
            NO_RENDER = 1 << 2,
            INCLUDE_IN_REFLECTIVE = 1 << 3,
            ALWAYS_PASS = 1 << 4,
            NO_SIZE_CULLING = 1 << 5,
            NO_CAST_TORCH_SHADOW = 1 << 6,
            DEFAULT = 1 << 7,
        };

        [Flags]
        public enum EmissiveFlag : byte
        {
            None = 0,
            ReplaceTint = 1,
            ReplaceIntensity = 2,
            MasterOff = 4
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct MoverFlag
        {
            public bool requires_script
            {
                get
                {
                    return (flags & 0x0004) != 0;
                }
                set
                {
                    flags |= 0x0004;
                }
            }
            public bool visible
            {
                get
                {
                    return (flags & 0x0001) != 0;
                }
                set
                {
                    flags |= 0x0001;
                }
            }
            public bool stationary
            {
                get
                {
                    return (flags & 0x0002) != 0;
                }
                set
                {
                    flags |= 0x0002;
                }
            }
            private short flags;
        }

        public class MOVER_DESCRIPTOR : IEquatable<MOVER_DESCRIPTOR>
        {
            public Matrix4x4 transform;

            public float[] gpu_constants; 
            public float[] render_constants; // see struct MODEL_PARAMS, etc

            public List<RenderableElements.Element> renderable_elements = new List<RenderableElements.Element>(); 

            public Resources.Resource resource = null; //Resources.bin index value

            public CullFlag cull_flags = CullFlag.DEFAULT;

            public EntityHandle entity; //The entity in the Commands file
            public int environment_map_index = -1; //environment_map.bin index

            public Vector3 emissive_tint = new Vector3(255, 255, 255); // sRGB
            public EmissiveFlag emissive_flags = EmissiveFlag.None;
            public float emissive_intensity_multiplier = 1.0f;
            public float emissive_radiosity_multiplier = 0.0f;

            public ShortGuid primary_zone_id; //zero is "unzoned"
            public ShortGuid secondary_zone_id; //zero is "unzoned"
            public int lighting_master_id = 0;

            public MoverFlag flags;

            public static bool operator ==(MOVER_DESCRIPTOR x, MOVER_DESCRIPTOR y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return false;
                return x.Equals(y);
            }

            public static bool operator !=(MOVER_DESCRIPTOR x, MOVER_DESCRIPTOR y)
            {
                return !(x == y);
            }

            public bool Equals(MOVER_DESCRIPTOR other)
            {
                if (other == null) return false;
                if (ReferenceEquals(this, other)) return true;

                // Compare Matrix4x4
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                for (int i = 0; i < 16; i++)
                {
                    if (Math.Abs(transform[i] - other.transform[i]) > float.Epsilon)
                        return false;
                }
#else
                if (transform != other.transform) return false;
#endif

                // Compare gpu_constants array (24 elements)
                if (gpu_constants == null && other.gpu_constants != null) return false;
                if (gpu_constants != null && other.gpu_constants == null) return false;
                if (gpu_constants != null && other.gpu_constants != null)
                {
                    if (gpu_constants.Length != other.gpu_constants.Length) return false;
                    for (int i = 0; i < gpu_constants.Length; i++)
                    {
                        if (Math.Abs(gpu_constants[i] - other.gpu_constants[i]) > float.Epsilon)
                            return false;
                    }
                }

                // Compare render_constants array (21 elements)
                if (render_constants == null && other.render_constants != null) return false;
                if (render_constants != null && other.render_constants == null) return false;
                if (render_constants != null && other.render_constants != null)
                {
                    if (render_constants.Length != other.render_constants.Length) return false;
                    for (int i = 0; i < render_constants.Length; i++)
                    {
                        if (Math.Abs(render_constants[i] - other.render_constants[i]) > float.Epsilon)
                            return false;
                    }
                }

                // Compare renderable_elements list
                if (renderable_elements == null && other.renderable_elements != null) return false;
                if (renderable_elements != null && other.renderable_elements == null) return false;
                if (renderable_elements != null && other.renderable_elements != null)
                {
                    if (renderable_elements.Count != other.renderable_elements.Count) return false;
                    for (int i = 0; i < renderable_elements.Count; i++)
                    {
                        if (renderable_elements[i] != other.renderable_elements[i]) return false;
                    }
                }

                // Compare resource
                if (resource == null && other.resource != null) return false;
                if (resource != null && other.resource == null) return false;
                if (resource != null && other.resource != null)
                {
                    if (resource.composite_instance_id != other.resource.composite_instance_id) return false;
                    if (resource.resource_id != other.resource.resource_id) return false;
                }

                if (cull_flags != other.cull_flags) return false;

                // Compare entity
                if (entity != other.entity) return false;

                if (environment_map_index != other.environment_map_index) return false;

                // Compare emissive_tint
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                if (emissive_tint != other.emissive_tint) return false;
#else
                if (emissive_tint != other.emissive_tint) return false;
#endif

                if (emissive_flags != other.emissive_flags) return false;
                if (Math.Abs(emissive_intensity_multiplier - other.emissive_intensity_multiplier) > float.Epsilon) return false;
                if (Math.Abs(emissive_radiosity_multiplier - other.emissive_radiosity_multiplier) > float.Epsilon) return false;

                if (primary_zone_id != other.primary_zone_id) return false;
                if (secondary_zone_id != other.secondary_zone_id) return false;
                if (lighting_master_id != other.lighting_master_id) return false;

                // Compare MoverFlag struct
                if (flags.requires_script != other.flags.requires_script) return false;
                if (flags.visible != other.flags.visible) return false;
                if (flags.stationary != other.flags.stationary) return false;

                return true;
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as MOVER_DESCRIPTOR);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                    for (int i = 0; i < 16; i++)
                    {
                        hash = hash * 23 + transform[i].GetHashCode();
                    }
#else
                    hash = hash * 23 + transform.GetHashCode();
#endif
                    if (gpu_constants != null)
                    {
                        for (int i = 0; i < gpu_constants.Length; i++)
                        {
                            hash = hash * 23 + gpu_constants[i].GetHashCode();
                        }
                    }
                    if (render_constants != null)
                    {
                        for (int i = 0; i < render_constants.Length; i++)
                        {
                            hash = hash * 23 + render_constants[i].GetHashCode();
                        }
                    }
                    if (renderable_elements != null)
                    {
                        hash = hash * 23 + renderable_elements.Count.GetHashCode();
                        foreach (var element in renderable_elements)
                        {
                            hash = hash * 23 + (element?.GetHashCode() ?? 0);
                        }
                    }
                    if (resource != null)
                    {
                        hash = hash * 23 + resource.composite_instance_id.GetHashCode();
                        hash = hash * 23 + resource.resource_id.GetHashCode();
                    }
                    hash = hash * 23 + cull_flags.GetHashCode();
                    hash = hash * 23 + (entity?.GetHashCode() ?? 0);
                    hash = hash * 23 + environment_map_index.GetHashCode();
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                    hash = hash * 23 + emissive_tint.x.GetHashCode();
                    hash = hash * 23 + emissive_tint.y.GetHashCode();
                    hash = hash * 23 + emissive_tint.z.GetHashCode();
#else
                    hash = hash * 23 + emissive_tint.X.GetHashCode();
                    hash = hash * 23 + emissive_tint.Y.GetHashCode();
                    hash = hash * 23 + emissive_tint.Z.GetHashCode();
#endif
                    hash = hash * 23 + emissive_flags.GetHashCode();
                    hash = hash * 23 + emissive_intensity_multiplier.GetHashCode();
                    hash = hash * 23 + emissive_radiosity_multiplier.GetHashCode();
                    hash = hash * 23 + primary_zone_id.GetHashCode();
                    hash = hash * 23 + secondary_zone_id.GetHashCode();
                    hash = hash * 23 + lighting_master_id.GetHashCode();
                    hash = hash * 23 + flags.requires_script.GetHashCode();
                    hash = hash * 23 + flags.visible.GetHashCode();
                    hash = hash * 23 + flags.stationary.GetHashCode();
                    return hash;
                }
            }

            ~MOVER_DESCRIPTOR()
            {
                gpu_constants = null;
                render_constants = null;
                entity = null;
            }
        };
        #endregion
    }
}