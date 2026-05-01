using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System;
using System.Threading.Tasks;
using CATHODE.Scripting;
using CathodeLib;
using CathodeLib.ObjectExtensions;
using System.Linq;
using System.Collections.Concurrent;
using static CATHODE.Lights;



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
        private Textures _textures;

        public bool Compressed { get { return _compressed; } set { _compressed = value; } }
        private bool _compressed = false;

        private List<MOVER_DESCRIPTOR> _writeList = new List<MOVER_DESCRIPTOR>();

        public Movers(string path, RenderableElements reds, Resources resources, Textures textures) : base(path)
        {
            _reds = reds;
            _resources = resources;
            _textures = textures;

            _loaded = Load();
        }

        public void ClearReferences()
        {
            _reds = null;
            _resources = null;
            _textures = null;
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
            //NOTE: Loading via byte[] or MemoryStream is not currently supported. Must be loaded via disk from a filepath!
            if (_filepath == "")
                return false;

            _compressed = _filepath != null && _filepath != "" && Path.GetExtension(_filepath).ToLower() == ".gz";

            //note: first 12 always renderable but not linked to commands -> they are always the same models across every level. is it the content of GLOBAL?

            using (BinaryReader reader = new BinaryReader(_compressed ? Utilities.GZIPDecompress(stream) : stream))
            {
                reader.BaseStream.Position += 4;
                int entryCount = reader.ReadInt32();
                reader.BaseStream.Position += 24;

                Textures.TEX4[] environmentMaps = new Textures.TEX4[entryCount]; 
                using (BinaryReader envMapReader = new BinaryReader(File.OpenRead(GetEnvMapPath())))
                {
                    envMapReader.BaseStream.Position += 8;
                    int envMapEntryCount = envMapReader.ReadInt32();
                    for (int i = 0; i < envMapEntryCount; i++)
                    {
                        environmentMaps[envMapReader.ReadInt32()] = _textures.GetAtWriteIndex(envMapReader.ReadInt32());
                    }
                }

                for (int i = 0; i < entryCount; i++)
                {
                    MOVER_DESCRIPTOR mvr = new MOVER_DESCRIPTOR();
                    mvr.Transform = Utilities.Consume<Matrix4x4>(reader);
                    mvr.GPUConstants = Utilities.Consume<MOVER_DESCRIPTOR.GPU_CONSTANTS>(reader);
                    mvr.RenderConstants = Utilities.Consume<MOVER_DESCRIPTOR.RENDER_CONSTANTS>(reader);
                    int redsIndex = reader.ReadInt32();
                    int redsCount = reader.ReadInt32();
                    mvr.RenderableElements = _reds.GetAtWriteIndex(redsIndex, redsCount);
                    mvr.Resource = _resources.GetAtWriteIndex(reader.ReadInt32());
                    reader.BaseStream.Position += 12;
                    mvr.CullFlags = (CullFlag)reader.ReadInt32();
                    mvr.Entity = Utilities.Consume<EntityHandle>(reader);
                    reader.BaseStream.Position += 4;
                    mvr.EnvironmentMap = environmentMaps[i];
                    mvr.EmissiveTint = new Vector3(reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                    mvr.EmissiveFlags = (EmissiveFlag)reader.ReadByte();
                    mvr.EmisiveIntensityMultiplier = reader.ReadSingle();
                    mvr.EmissiveRadiosityMultiplier = reader.ReadSingle();
                    mvr.PrimaryZoneID = Utilities.Consume<ShortGuid>(reader);
                    mvr.SecondaryZoneID = Utilities.Consume<ShortGuid>(reader);
                    mvr.LightingMasterID = reader.ReadInt32();
                    reader.BaseStream.Position += 2;
                    mvr.Flags = Utilities.Consume<MoverFlag>(reader);
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

            int totalEnvMaps = 0;
            foreach (Textures.TEX4 tex in _textures.Entries)
            {
                if (tex.StateFlags.HasFlag(Textures.TextureStateFlag.CUBE))
                    totalEnvMaps++;
            }

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(GetEnvMapPath())))
            {
                writer.BaseStream.SetLength(0);
                Utilities.WriteString("envm", writer);
                writer.Write(1);
                writer.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(i);
                    writer.Write(Entries[i].EnvironmentMap == null ? -1 : _textures.GetWriteIndexForEnvMap(Entries[i].EnvironmentMap));
                }
                writer.Write(totalEnvMaps);
            }

            int nonStationary = 0;
            for (int i = 0; i < Entries.Count; i++)
                if (!Entries[i].Flags.Stationary)
                    nonStationary++;

            byte[][] entryBuffers = new byte[Entries.Count][];
            Parallel.For(0, Entries.Count, i =>
            {
                entryBuffers[i] = SerializeEntry(Entries[i], i);
            });

            using (Stream stream = File.OpenWrite(_filepath))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.BaseStream.SetLength(0);
                writer.Write((Entries.Count * 320) + 32);
                writer.Write(Entries.Count);
                writer.Write(nonStationary);
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

        private byte[] SerializeEntry(MOVER_DESCRIPTOR entry, int index)
        {
            using (MemoryStream stream = new MemoryStream(320))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                Utilities.Write<Matrix4x4>(writer, entry.Transform);
                Utilities.Write<MOVER_DESCRIPTOR.GPU_CONSTANTS>(writer, entry.GPUConstants);
                Utilities.Write<MOVER_DESCRIPTOR.RENDER_CONSTANTS>(writer, entry.RenderConstants);
                if (entry.RenderableElements.Count == 0)
                {
                    writer.Write(-1);
                    writer.Write(-1);
                }
                else
                {
                    writer.Write(_reds.GetWriteIndex(entry.RenderableElements));
                    writer.Write(entry.RenderableElements.Count);
                }
                writer.Write(_resources.GetWriteIndex(entry.Resource));
                writer.Write(new byte[12]);
                writer.Write((int)entry.CullFlags);
                Utilities.Write<EntityHandle>(writer, entry.Entity);
                writer.Write(index);
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                writer.Write((byte)entry.emissive_tint.x);
                writer.Write((byte)entry.emissive_tint.y);
                writer.Write((byte)entry.emissive_tint.z);
#else
                writer.Write((byte)entry.EmissiveTint.X);
                writer.Write((byte)entry.EmissiveTint.Y);
                writer.Write((byte)entry.EmissiveTint.Z);
#endif
                writer.Write((byte)entry.EmissiveFlags);
                writer.Write(entry.EmisiveIntensityMultiplier);
                writer.Write(entry.EmissiveRadiosityMultiplier);
                Utilities.Write<ShortGuid>(writer, entry.PrimaryZoneID);
                Utilities.Write<ShortGuid>(writer, entry.SecondaryZoneID);
                writer.Write(entry.LightingMasterID);
                writer.Write((Int16)(-1)); //todo - sanity check this is actually -1 not 0
                Utilities.Write<MoverFlag>(writer, entry.Flags);
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

            newMover.RenderableElements = _reds.ImportEntry(newMover.RenderableElements, models);
            newMover.Resource = _resources.ImportEntry(newMover.Resource);

            //todo: do something with entity reference

            //todo: env map index

            //todo: set zone to global?

            var existing = Entries.FirstOrDefault(o => o == newMover);
            if (existing != null)
                return existing;

            Entries.Add(newMover);
            return newMover;
        }

        private string GetEnvMapPath()
        {
            return _filepath.Substring(0, _filepath.Length - Path.GetFileName(_filepath).Length) + "ENVIRONMENTMAP.BIN";
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
            public bool RequiresScript
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
            public bool Visible
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
            public bool Stationary
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
            public Matrix4x4 Transform;

            public GPU_CONSTANTS GPUConstants; 
            public RENDER_CONSTANTS RenderConstants;

            public List<RenderableElements.Element> RenderableElements = new List<RenderableElements.Element>(); 

            public Resources.Resource Resource = null;

            public CullFlag CullFlags = CullFlag.DEFAULT;

            public EntityHandle Entity;
            public Textures.TEX4 EnvironmentMap = null;

            public Vector3 EmissiveTint = new Vector3(255, 255, 255);
            public EmissiveFlag EmissiveFlags = EmissiveFlag.None;
            public float EmisiveIntensityMultiplier = 1.0f;
            public float EmissiveRadiosityMultiplier = 0.0f;

            public ShortGuid PrimaryZoneID; //zero is "unzoned"
            public ShortGuid SecondaryZoneID; //zero is "unzoned"
            public int LightingMasterID = 0;

            public MoverFlag Flags;

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public class GPU_CONSTANTS : IEquatable<GPU_CONSTANTS>
            {
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 96)]
                private byte[] buffer;

                public T GetAs<T>()
                {
                    using (MemoryStream stream = new MemoryStream(buffer))
                    using (BinaryReader reader = new BinaryReader(stream))
                    {
                        return Utilities.Consume<T>(reader);
                    }
                }

                public void SetAs<T>(T value)
                {
                    using (MemoryStream stream = new MemoryStream())
                    using (BinaryWriter writer = new BinaryWriter(stream))
                    {
                        writer.Write(new byte[96]);
                        writer.BaseStream.Position = 0;
                        Utilities.Write<T>(writer, value);
                        buffer = stream.ToArray();
                    }
                }

                public bool Equals(GPU_CONSTANTS other)
                {
                    if (other == null) return false;
                    return other.buffer.SequenceEqual(buffer);
                }

                public override bool Equals(object obj)
                {
                    return Equals(obj as GPU_CONSTANTS);
                }

                public static bool operator ==(GPU_CONSTANTS x, GPU_CONSTANTS y)
                {
                    if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                    if (ReferenceEquals(y, null)) return false;
                    return x.Equals(y);
                }

                public static bool operator !=(GPU_CONSTANTS x, GPU_CONSTANTS y)
                {
                    return !(x == y);
                }

                public override int GetHashCode()
                {
                    return 143091379 + EqualityComparer<byte[]>.Default.GetHashCode(buffer);
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class DEFERRED_GPU_CONSTANTS
                {
                    public Vector3 Colour;
	                public float ShadowFade;

	                public float AttenuationDefocus;
	                public float AttenuationBegin;
	                public float NearDist;

	                public float Softness;
	                public float DiffuseBias;
	                public float OuterAngle; // length, if strip light
                    public float InnerAngle;

                    public float ArealightRadius;
	                public float LightFade;
	                public float AttenuationEnd;
                    private float _unused;
                    public float NearDistShadowOffset;

                    public Vector3 VolumeColour;
                    public float VolumeDensity;
                    public float VolumeAttenuationEnd; 

	                public float AspectRatio;
                    public float GlossinessScale;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class PARTICLE_GPU_CONSTANTS //GPU Particles
                {
	                public float ExpiryTime;
                    private float _unused1;
                    public float RandomNumber;
                    private float _unused2;

                    public float AspectRatio;
	                public float FadeAtDistance;
	                public float AlphaIn;
	                public float AlphaOut;

	                public float SizeStartMin;
	                public float SizeStartMax;
	                public float SizeEndMin;
	                public float SizeEndMax;

	                public float MaskAmountMin;
	                public float MaskAmountMax;
	                public float MaskAmountMidpoint;
	                public float AlphaRefValue;

	                public float ParticleExpiryTimeMin;
	                public float ParticleExpiryTimeMax;
	                public float ColourScaleMin;
	                public float ColourScaleMax;

                    public Vector3 Wind;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class DYNAMIC_FX_GPU_CONSTANTS //CPU Particles
                {
	                public float ExpiryTime;
                    private float _unused;
                    public float RandomNumber;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class ENVIRONMENT_GPU_CONSTANTS
                {
                    public Vector4 VertexColourScalars = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
                    public Vector4 DiffuseColourScalars = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);

                    public float AlphaBlendNoisePowerScale = 1.0f;
                    public float AlphaBlendNoiseUvScale = 1.0f;
                    public Vector2 AlphaBlendNoiseUvOffset = new Vector2(0.0f, 0.0f);

                    public Vector2 AtlasOffset = new Vector2(0.0f, 0.0f);
                    public Vector2 AtlasBias = new Vector2(1.0f, 1.0f);

                    public float DirtMultiplyBlendSpecPowerScale = 1.0f;
                    public float DirtMapUvScale = 1.0f;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class FOGSPHERE_GPU_CONSTANTS
                {
                    public Vector3 ColourTint;
	                public float Intensity;
	                public float Opacity;
	                public float Density;
	                public float FresnelPower;
	                public float SoftnessEdge;
	                public float FarBlendDistance;
	                public float NearBlendDistance;
	                public float SecondaryFarBlendDistance;
	                public float SecondaryNearBlendDistance;
	                public float Radius;
                    public Vector3 DepthIntersectionColour;
                    public float DepthIntersectionAlpha;
                    public float DepthIntersectionRange;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class FOGPLANE_GPU_CONSTANTS
                {
	                public float StartDistanceFadeScalar = 1.0f;
	                public float DistanceFadeScalar = 1.0f;
	                public float AngleFadeScalar = 1.0f;
	                public float FresnelPowerScalar = 1.0f;
	                public float HeightMaxDensityScalar = 1.0f;
                    public Vector3 ColourTint = new Vector3(1.0f, 1.0f, 1.0f);
                    public float ThicknessScalar = 1.0f;
	                public float EdgeSoftnessScalar = 1.0f;
	                public float DiffuseMap0_UvScalar = 1.0f;
	                public float DiffuseMap0_SpeedScalar = 1.0f;
	                public float DiffuseMap1_UvScalar = 1.0f;
	                public float DiffuseMap1_SpeedScalar = 1.0f;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class LIGHTDECAL_GPU_CONSTANTS
                {
                    public Vector3 LightdecalIntensity;
                }
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public class RENDER_CONSTANTS : IEquatable<RENDER_CONSTANTS>
            {
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 84)]
                private byte[] buffer;

                public T GetAs<T>()
                {
                    using (MemoryStream stream = new MemoryStream(buffer))
                    using (BinaryReader reader = new BinaryReader(stream))
                    {
                        return Utilities.Consume<T>(reader);
                    }
                }

                public void SetAs<T>(T value)
                {
                    using (MemoryStream stream = new MemoryStream())
                    using (BinaryWriter writer = new BinaryWriter(stream))
                    {
                        writer.Write(new byte[84]);
                        writer.BaseStream.Position = 0;
                        Utilities.Write<T>(writer, value);
                        buffer = stream.ToArray();
                    }
                }

                public bool Equals(RENDER_CONSTANTS other)
                {
                    if (other == null) return false;
                    return other.buffer.SequenceEqual(buffer);
                }

                public override bool Equals(object obj)
                {
                    return Equals(obj as RENDER_CONSTANTS);
                }

                public static bool operator ==(RENDER_CONSTANTS x, RENDER_CONSTANTS y)
                {
                    if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                    if (ReferenceEquals(y, null)) return false;
                    return x.Equals(y);
                }

                public static bool operator !=(RENDER_CONSTANTS x, RENDER_CONSTANTS y)
                {
                    return !(x == y);
                }

                public override int GetHashCode()
                {
                    return 143091379 + EqualityComparer<byte[]>.Default.GetHashCode(buffer);
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class DEFERRED_PARAMS
                {
                    public float Visibility
                    {
                        get
                        {
                            return (float)_visibility * (1.0f / 255.0f);
                        }
                        set
                        {
                            _visibility = (byte)(255.0f * Math.Min(1.0f, Math.Max(0.0f, value)));
                        }
                    }
                    private byte _visibility;

                    public float FlareIntensityScale
                    {
                        get
                        {
                            float x = (float)_flareIntensityScale * (1.0f / 255.0f);
                            return x * x * 100.0f;
                        }
                        set
                        {
                            _flareIntensityScale = (byte)(255.0f * Math.Min(1.0f, Math.Sqrt(Math.Max(0.0f, value / 100.0f))));
                        }
                    }
                    private byte _flareIntensityScale;

                    public bool UsesRadiosity => _radiosityFraction != 0;
                    public float RadiosityFraction
                    {
                        get
                        {
                            float x = (float)_radiosityFraction * (1.0f / 255.0f);
                            return x * x * 4.0f;
                        }
                        set
                        {
                            _radiosityFraction = (byte)(255.0f * Math.Min(1.0f, Math.Sqrt(Math.Max(0.0f, value / 4.0f))));
                        }
                    }
                    private byte _radiosityFraction;

                    public LightType Type
                    {
                        get
                        {
                            return (LightType)_type;
                        }
                        set
                        {
                            _type = (byte)value;
                        }
                    }
                    private byte _type;

                    public byte ShadowPriorityOffset;
                    public byte SlopeScaleDepthBias;

                    public LightFeature Features;

                    private byte _unused;

                    public LightFadeType LightFadeType
                    {
                        get
                        {
                            return (LightFadeType)_lightFadeType;
                        }
                        set
                        {
                            _lightFadeType = (byte)value;
                        }
                    }
                    private byte _lightFadeType;

                    public float FlareOccluderRadius
                    {
                        get
                        {
                            return (float)_flareOccluderRadius * (1.0f / 255.0f);
                        }
                        set
                        {
                            _flareOccluderRadius = (byte)(255.0f * Math.Max(0.0f, Math.Min(1.0f, value)));
                        }
                    }
                    private byte _flareOccluderRadius;

                    public float FlareSpotOffset
                    {
                        get
                        {
                            return (float)_flareSpotOffset * (1.0f / 255.0f) - 0.5f;
                        }
                        set
                        {
                            _flareSpotOffset = (byte)(255.0f * Math.Max(0.0f, Math.Min(1.0f, value + 0.5f)));
                        }
                    }
                    private byte _flareSpotOffset;

                    public float DepthBias;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class MODEL_PARAMS
                {
                    public Vector3 CustomPositionArray1;
                    public Vector3 CustomPositionArray2;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class DYNAMIC_PFX_PARAMS //CPU Particles
                {
                    public float DrawPass;

                    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
                    private byte[] _unused;

                    public ShortGuid EntityGuid;
                    public ShortGuid ParentGuid;

                    private int _unused2 = -1;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class PARTICLE_PARAMS //GPU Particles
                {
                    public float DrawPass;
                    public float NumVerts;
                    public float PrimitiveCount;
                    public float VertexOffset;
                    public ShortGuid EntityGuid;
                    public ShortGuid ParentGuid;

                    public Vector3 BoundingBoxMin;
                    public Vector3 BoundingBoxMax;
                }
            }

            public RenderableInstanceType GetRenderableType()
            {
                return RenderableElements.CalculateRenderableType();
            }

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

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                for (int i = 0; i < 16; i++)
                {
                    if (Math.Abs(transform[i] - other.transform[i]) > float.Epsilon)
                        return false;
                }
#else
                if (Transform != other.Transform) return false;
#endif

                if (GPUConstants != other.GPUConstants) return false;
                if (RenderConstants != other.RenderConstants) return false;

                if (RenderableElements == null && other.RenderableElements != null) return false;
                if (RenderableElements != null && other.RenderableElements == null) return false;
                if (RenderableElements != null && other.RenderableElements != null)
                {
                    if (RenderableElements.Count != other.RenderableElements.Count) return false;
                    for (int i = 0; i < RenderableElements.Count; i++)
                    {
                        if (RenderableElements[i] != other.RenderableElements[i]) return false;
                    }
                }

                if (Resource == null && other.Resource != null) return false;
                if (Resource != null && other.Resource == null) return false;
                if (Resource != null && other.Resource != null)
                {
                    if (Resource.composite_instance_id != other.Resource.composite_instance_id) return false;
                    if (Resource.resource_id != other.Resource.resource_id) return false;
                }

                if (CullFlags != other.CullFlags) return false;
                if (Entity != other.Entity) return false;
                if (EnvironmentMap != other.EnvironmentMap) return false;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                if (emissive_tint != other.emissive_tint) return false;
#else
                if (EmissiveTint != other.EmissiveTint) return false;
#endif

                if (EmissiveFlags != other.EmissiveFlags) return false;
                if (Math.Abs(EmisiveIntensityMultiplier - other.EmisiveIntensityMultiplier) > float.Epsilon) return false;
                if (Math.Abs(EmissiveRadiosityMultiplier - other.EmissiveRadiosityMultiplier) > float.Epsilon) return false;

                if (PrimaryZoneID != other.PrimaryZoneID) return false;
                if (SecondaryZoneID != other.SecondaryZoneID) return false;
                if (LightingMasterID != other.LightingMasterID) return false;

                if (Flags.RequiresScript != other.Flags.RequiresScript) return false;
                if (Flags.Visible != other.Flags.Visible) return false;
                if (Flags.Stationary != other.Flags.Stationary) return false;

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
                    hash = hash * 23 + Transform.GetHashCode();
#endif
                    hash = hash * 23 + GPUConstants.GetHashCode();
                    hash = hash * 23 + RenderConstants.GetHashCode();
                    if (RenderableElements != null)
                    {
                        hash = hash * 23 + RenderableElements.Count.GetHashCode();
                        foreach (var element in RenderableElements)
                        {
                            hash = hash * 23 + (element?.GetHashCode() ?? 0);
                        }
                    }
                    if (Resource != null)
                    {
                        hash = hash * 23 + Resource.composite_instance_id.GetHashCode();
                        hash = hash * 23 + Resource.resource_id.GetHashCode();
                    }
                    hash = hash * 23 + CullFlags.GetHashCode();
                    hash = hash * 23 + (Entity?.GetHashCode() ?? 0);
                    hash = hash * 23 + (EnvironmentMap?.GetHashCode() ?? 0);
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                    hash = hash * 23 + emissive_tint.x.GetHashCode();
                    hash = hash * 23 + emissive_tint.y.GetHashCode();
                    hash = hash * 23 + emissive_tint.z.GetHashCode();
#else
                    hash = hash * 23 + EmissiveTint.X.GetHashCode();
                    hash = hash * 23 + EmissiveTint.Y.GetHashCode();
                    hash = hash * 23 + EmissiveTint.Z.GetHashCode();
#endif
                    hash = hash * 23 + EmissiveFlags.GetHashCode();
                    hash = hash * 23 + EmisiveIntensityMultiplier.GetHashCode();
                    hash = hash * 23 + EmissiveRadiosityMultiplier.GetHashCode();
                    hash = hash * 23 + PrimaryZoneID.GetHashCode();
                    hash = hash * 23 + SecondaryZoneID.GetHashCode();
                    hash = hash * 23 + LightingMasterID.GetHashCode();
                    hash = hash * 23 + Flags.RequiresScript.GetHashCode();
                    hash = hash * 23 + Flags.Visible.GetHashCode();
                    hash = hash * 23 + Flags.Stationary.GetHashCode();
                    return hash;
                }
            }

            ~MOVER_DESCRIPTOR()
            {
                GPUConstants = null;
                RenderConstants = null;
                Entity = null;
            }
        };
        #endregion
    }

    public enum RenderableInstanceType
    {
        LIGHT,
        DYNAMICFX,
        DYNAMICFX_UNIQUE_MAT,
        ENVIRONMENT,
        CHARACTER,
        MISC,
        PLANET,
        ENVIRONMENT_EXTRA, //Supports alphalight, emissives, and decals
        FOGSPHERE,
    }
}