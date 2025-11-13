using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System;
using System.Threading.Tasks;
using CATHODE.Scripting;
using CathodeLib;
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
        private Materials _materials;

        public Movers(string path, RenderableElements reds, Resources resources, Materials materials) : base(path)
        {
            _reds = reds;
            _resources = resources;
            _materials = materials;

            _loaded = Load();
        }

        private List<MOVER_DESCRIPTOR> _writeList = new List<MOVER_DESCRIPTOR>(); 

        ~Movers()
        {
            Entries.Clear();
            _writeList.Clear();
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            //note: first 12 always renderable but not linked to commands -> they are always the same models across every level. is it the content of GLOBAL?

            using (BinaryReader reader = new BinaryReader(stream))
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
                    if (redsIndex != -1)
                    {
                        for (int x = 0; x < redsCount; x++)
                            mvr.renderable_elements.Add(_reds.GetAtWriteIndex(redsIndex + x));
                    }
                    mvr.resource = _resources.GetAtWriteIndex(reader.ReadInt32());
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
                    mvr.material_mapping = _materials.GetAtWriteIndex(reader.ReadInt16());
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
            int non_stationary = 0;
            for (int i = 0; i < Entries.Count; i++)
                if (!Entries[i].flags.stationary)
                    non_stationary++;

            byte[][] entryBuffers = new byte[Entries.Count][];
            Parallel.For(0, Entries.Count, i =>
            {
                entryBuffers[i] = SerializeEntry(Entries[i]);
            });
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
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
                    writer.Write(_reds.GetWriteIndex(entry.renderable_elements[0]));
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
                writer.Write((Int16)_materials.GetWriteIndex(entry.material_mapping));
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

        public class MOVER_DESCRIPTOR
        {
            public Matrix4x4 transform;

            public float[] gpu_constants; 
            public float[] render_constants;

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
            public Materials.Material material_mapping; //is this defo Material not MaterialMappings.PAK?

            public MoverFlag flags;

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