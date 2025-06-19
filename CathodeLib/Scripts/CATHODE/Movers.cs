using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System;
using CATHODE.Scripting;
using CathodeLib;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/MODELS.MVR */
    public class Movers : CathodeFile
    {
        public List<MOVER_DESCRIPTOR> Entries = new List<MOVER_DESCRIPTOR>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE;
        public Movers(string path) : base(path) { }

        private List<MOVER_DESCRIPTOR> _writeList = new List<MOVER_DESCRIPTOR>(); //todo: deprecate this

        ~Movers()
        {
            Entries.Clear();
            _writeList.Clear();
        }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            //note: first 12 always renderable but not linked to commands -> they are always the same models across every level. is it the content of GLOBAL?

            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
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
                    mvr.renderable_element_index = reader.ReadInt32();
                    mvr.renderable_element_count = reader.ReadInt32();
                    mvr.resource_index = reader.ReadInt32();
                    reader.BaseStream.Position += 12;
                    mvr.render_info_descriptor = (CULL_FLAG)reader.ReadInt32();
                    mvr.entity = Utilities.Consume<EntityHandle>(reader);
                    mvr.environment_map_index = reader.ReadInt32();
                    mvr.emissive_tint = new Vector3(reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                    mvr.emissive_flags = (LightOverrideFlags)reader.ReadByte();
                    mvr.emissive_intensity_multiplier = reader.ReadSingle();
                    mvr.emissive_radiosity_multiplier = reader.ReadSingle();
                    mvr.primary_zone_id = Utilities.Consume<ShortGuid>(reader);
                    mvr.secondary_zone_id = Utilities.Consume<ShortGuid>(reader);
                    mvr.lighting_master_id = reader.ReadInt32();
                    mvr.material_mapping_index = reader.ReadInt16();
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

                for (int i = 0; i < Entries.Count; i++)
                {
                    Utilities.Write<Matrix4x4>(writer, Entries[i].transform);
                    for (int x = 0; x < 24; x++)
                        writer.Write(Entries[i].gpu_constants[x]);
                    for (int x = 0; x < 21; x++)
                        writer.Write(Entries[i].render_constants[x]);
                    writer.Write(Entries[i].renderable_element_index);
                    writer.Write(Entries[i].renderable_element_count);
                    writer.Write(Entries[i].resource_index);
                    writer.Write(new byte[12]);
                    writer.Write((int)Entries[i].render_info_descriptor);
                    Utilities.Write<EntityHandle>(writer, Entries[i].entity);
                    writer.Write(Entries[i].environment_map_index);
                    writer.Write((byte)Entries[i].emissive_tint.X);
                    writer.Write((byte)Entries[i].emissive_tint.Y);
                    writer.Write((byte)Entries[i].emissive_tint.Z);
                    writer.Write((byte)Entries[i].emissive_flags);
                    writer.Write(Entries[i].emissive_intensity_multiplier);
                    writer.Write(Entries[i].emissive_radiosity_multiplier);
                    Utilities.Write<ShortGuid>(writer, Entries[i].primary_zone_id);
                    Utilities.Write<ShortGuid>(writer, Entries[i].secondary_zone_id);
                    writer.Write(Entries[i].lighting_master_id);
                    writer.Write(Entries[i].material_mapping_index);
                    Utilities.Write<MoverFlag>(writer, Entries[i].flags);
                    writer.Write(new byte[8]);
                }
            }
            _writeList.Clear();
            _writeList.AddRange(Entries);
            return true;
        }
        #endregion

        #region HELPERS
        /* Get the current BIN index for a submesh (useful for cross-ref'ing with compiled binaries)
         * Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk */
        public int GetWriteIndex(MOVER_DESCRIPTOR mover)
        {
            if (!_writeList.Contains(mover)) return -1;
            return _writeList.IndexOf(mover);
        }

        /* Get a submesh by its current BIN index (useful for cross-ref'ing with compiled binaries)
         * Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk */
        public MOVER_DESCRIPTOR GetAtWriteIndex(int index)
        {
            if (_writeList.Count <= index) return null;
            return _writeList[index];
        }
        #endregion

        #region STRUCTURES
        [Flags]
        public enum CULL_FLAG : int
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
        public enum LightOverrideFlags : byte
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

            public int renderable_element_index; //reds.bin index
            public int renderable_element_count; //reds.bin count

            public int resource_index = 0; //Resources.bin index value

            public CULL_FLAG render_info_descriptor = CULL_FLAG.DEFAULT;

            public EntityHandle entity; //The entity in the Commands file
            public int environment_map_index = -1; //environment_map.bin index

            public Vector3 emissive_tint = new Vector3(255, 255, 255); // sRGB
            public LightOverrideFlags emissive_flags = LightOverrideFlags.None;
            public float emissive_intensity_multiplier = 1.0f;
            public float emissive_radiosity_multiplier = 0.0f;

            public ShortGuid primary_zone_id; //zero is "unzoned"
            public ShortGuid secondary_zone_id; //zero is "unzoned"
            public int lighting_master_id = 0;
            public short material_mapping_index;

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