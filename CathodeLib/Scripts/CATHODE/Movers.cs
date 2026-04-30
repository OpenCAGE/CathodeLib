using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System;
using System.Threading.Tasks;
using CATHODE.Scripting;
using CathodeLib;
using CathodeLib.ObjectExtensions;
using System.Linq;

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
        private EnvironmentMaps _envMaps;

        public bool Compressed { get { return _compressed; } set { _compressed = value; } }
        private bool _compressed = false;

        private List<MOVER_DESCRIPTOR> _writeList = new List<MOVER_DESCRIPTOR>();
        private Dictionary<MOVER_DESCRIPTOR, int> _envMapPatch = new Dictionary<MOVER_DESCRIPTOR, int>();

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
            _envMaps = null;
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
                    mvr.gpu_constants = Utilities.Consume<MOVER_DESCRIPTOR.GPU_CONSTANTS>(reader);
                    mvr.render_constants = Utilities.Consume<MOVER_DESCRIPTOR.RENDER_CONSTANTS>(reader);
                    int redsIndex = reader.ReadInt32();
                    int redsCount = reader.ReadInt32();
                    mvr.renderable_elements = _reds.GetAtWriteIndex(redsIndex, redsCount);
                    mvr.resource = _resources.GetAtWriteIndex(reader.ReadInt32()); //todo - is this not looked up by the id in resources?
                    reader.BaseStream.Position += 12;
                    mvr.cull_flags = (CullFlag)reader.ReadInt32();
                    mvr.entity = Utilities.Consume<EntityHandle>(reader);
                    _envMapPatch.Add(mvr, reader.ReadInt32());
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
                Utilities.Write<MOVER_DESCRIPTOR.GPU_CONSTANTS>(writer, entry.gpu_constants);
                Utilities.Write<MOVER_DESCRIPTOR.RENDER_CONSTANTS>(writer, entry.render_constants);
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
                writer.Write(_envMaps.GetWriteIndex(entry.environment_map));
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

        /// <summary>
        /// Patches up environment maps after loading
        /// </summary>
        public void PatchEnvMaps(EnvironmentMaps envMaps)
        {
            _envMaps = envMaps;
            foreach (KeyValuePair<MOVER_DESCRIPTOR, int> entry in _envMapPatch)
            {
                entry.Key.environment_map = _envMaps.GetAtWriteIndex(entry.Value);
            }
            _envMapPatch.Clear();
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

            public GPU_CONSTANTS gpu_constants; 
            public RENDER_CONSTANTS render_constants;

            public List<RenderableElements.Element> renderable_elements = new List<RenderableElements.Element>(); 

            public Resources.Resource resource = null; //Resources.bin index value

            public CullFlag cull_flags = CullFlag.DEFAULT;

            public EntityHandle entity; //The entity in the Commands file
            public EnvironmentMaps.Mapping environment_map = null;

            public Vector3 emissive_tint = new Vector3(255, 255, 255); // sRGB
            public EmissiveFlag emissive_flags = EmissiveFlag.None;
            public float emissive_intensity_multiplier = 1.0f;
            public float emissive_radiosity_multiplier = 0.0f;

            public ShortGuid primary_zone_id; //zero is "unzoned"
            public ShortGuid secondary_zone_id; //zero is "unzoned"
            public int lighting_master_id = 0;

            public MoverFlag flags;

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public class GPU_CONSTANTS
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
                        Utilities.Write<T>(writer, value);
                        buffer = stream.ToArray();
                    }
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
	                public float Specularity;
	                public float NearDistShadowOffset;

                    public Vector3 VolumeColour;
                    public float VolumeDensity;
                    public float VolumeAttenuationEnd; 

	                public float AspectRatio;
                    public float GlossinessScale;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class PARTICLE_GPU_CONSTANTS
                {
	                public float ExpiryTime;
	                public float StartTime;
	                public float RandomNumber;
	                public float CurrentDistance;

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
                public class DYNAMIC_FX_GPU_CONSTANTS
                {
	                public float ExpiryTime;
	                public float StartTime;
	                public float RandomNumber;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class CHARACTER_GPU_CONSTANTS
                {
                    public Vector4 NoTintColour;
                    public Vector4 PrimaryTintColour;
                    public Vector4 SecondaryTintColour;
                    public Vector4 TertiaryTintColour;

                    public float BurnAmount; // fire damage
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class ENVIRONMENT_GPU_CONSTANTS
                {
                    public Vector4 VertexColourScalars;
                    public Vector4 DiffuseColourScalars;

	                public float AlphaBlendNoisePowerScale;
                    public float AlphaBlendNoiseUvScale;
                    public Vector2 AlphaBlendNoiseUvOffset;

                    public Vector2 AtlasOffset;
                    public Vector2 AtlasBias;

                    public float DirtMultiplyBlendSpecPowerScale;
                    public float DirtMapUvScale;

                    public float UvOffsetX;
                    public float UvOffsetY;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class PLANET_GPU_CONSTANTS
                {
                    public Vector3 SunPosition;
                    public float Scale;
                    public Vector3 ParallaxPosition;
	                public float OverbrightScalar;
                    public float LightWrapAngleScalar;
                    public float PenumraFalloffPowerScalar;
	                public float AtmosphereRimTransparencyScalar;
	                public float AtmosphereEdgeFalloffPower;
	                public float FlowCycleTime;
	                public float FlowSpeed;
	                public float FlowTexScale;
	                public float FlowWarpStrength;
	                public float AtmosphereTextureScrollSpeed;
	                public float AtmosphereDetailTextureScrollSpeed;
	                public float DetailTexScalar;
	                public float AtmosphereNormalMapScalar;
	                public float TerrainUvScalar;
	                public float AtmosphereNormalStrength;
	                public float TerrainNormalStrength;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class NONINTERACTIVE_WATER_GPU_CONSTANTS
                {
	                public float SoftnessEdgeReciprocal;
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
	                public float StartDistanceFadeScalar;
	                public float DistanceFadeScalar;
	                public float AngleFadeScalar;
	                public float FresnelPowerScalar;
	                public float HeightMaxDensityScalar;
                    public Vector3 ColourTint;
                    public float ThicknessScalar;
	                public float EdgeSoftnessScalar;
	                public float DiffuseMap0_UvScalar;
	                public float DiffuseMap0_SpeedScalar;
	                public float DiffuseMap1_UvScalar;
	                public float DiffuseMap1_SpeedScalar;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class LIGHTDECAL_GPU_CONSTANTS
                {
                    public Vector3 LightdecalIntensity;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class GPU_PFX_CONSTANTS
                {
                    public Vector3 PreviousPosition;
	                public float TimeDelta;
	                public float SpawnStart;
	                public float SpawnEnd;
	                public float PixelScale;
	                public float Type;

	                public float Speed;
	                public float SpeedVar;
	                public float Lifetime;
	                public float LifetimeVar;
	                public float SpreadMin;
	                public float SpreadMax;
	                public float EmitterSize;

	                public float InvTexHeight;
	                public float Time;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class GPU_PFX_COMPUTE_CONSTANTS
                {
                    public Vector3 PreviousPosition;
	                float	TimeDelta;
	                int		SpawnNumber;
	                int		Type;
	                float	RandomOffset;
	                float	SecondRandomScale;
	                float	PixelScale;

	                float	Speed;
	                float	SpeedVar;
	                float	Lifetime;
	                float	LifetimeVar;
	                float	SpreadMin;
	                float	SpreadMax;
	                float	EmitterSize;

	                float	InvTexHeight;
	                float	Time;
                }
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public class RENDER_CONSTANTS
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
                        Utilities.Write<T>(writer, value);
                        buffer = stream.ToArray();
                    }
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

                    public byte FlareIntensityScalePacked;
                    public byte RadiosityFractionPacked;

                    public Materials.LightFlags.LightType Type
                    {
                        get
                        {
                            return (Materials.LightFlags.LightType)_type;
                        }
                        set
                        {
                            _type = (byte)value;
                        }
                    }
                    private byte _type;

                    public byte ShadowPriorityOffset;
                    public byte SlopeScaleDepthBias;
                    public UInt16 Features;

                    public byte NotUsed;
                    public byte LightFadeType;
                    public byte FlareOccluderRadiusPacked;
                    public byte FlareSpotOffsetPacked;

                    public float DepthBias;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class MODEL_PARAMS
                {
                    public Vector3 CustomPositionArray1;
                    public Vector3 CustomPositionArray2;

                    public float EmissiveScale;
                    public int DecalID;
                    public float ActivateTime;
                    public int DrawPass;

                    public float EndTime;

                    public uint SkeletonIndex;
                    public uint PrevSkeletonIndex;
                    public uint NumBones;

                    public UInt16 WrinkleWeightSetID;
                    public Int16 Deprecated;

                    public byte Bitfield1Packed; 

                    public byte DamageType;

                    public UInt16 EmissiveSurfaceID;

                    public Vector3 DecalScale;
                    public uint DynamicTextureIndex;
                    public uint EnvironmentMapIndex;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class PLANET_PARAMS
                {
                    public Vector3 ColourRGB;
                    public Vector3 Offset;
                    public float Range;
                    public float Decay;
                    public float MinOcclusionDistance;
                    public float Intensity;
                    public float Density;
                    public bool BlocksLightShafts;
                    public bool UseSourceOcclusion;
                    public int DrawPass;
                    public Vector3 LensFlareColour;
                    public float LensFlareBrightness;
                    public Vector4 GlobalTint;
                }

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class DYNAMIC_PFX_PARAMS //CPU Particles
                {
                    public float DrawPass;
                    public float NumVerts;
                    public float PrimitiveCount;
                    public float VertexByteOffset;
                    public ShortGuid EntityGuid;
                    public ShortGuid ParentGuid;

                    public int Handle;
                    public bool ExpiredFinishEmissions;
                    public float Distance;
                    public Vector3 ImpactPoint;
                    public Vector3 ImpactNormal;
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
                return renderable_elements.CalculateRenderableType();
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
                if (transform != other.transform) return false;
#endif

                if (gpu_constants != other.gpu_constants) return false;
                if (render_constants != other.render_constants) return false;

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

                if (resource == null && other.resource != null) return false;
                if (resource != null && other.resource == null) return false;
                if (resource != null && other.resource != null)
                {
                    if (resource.composite_instance_id != other.resource.composite_instance_id) return false;
                    if (resource.resource_id != other.resource.resource_id) return false;
                }

                if (cull_flags != other.cull_flags) return false;
                if (entity != other.entity) return false;
                if (environment_map != other.environment_map) return false;

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
                    hash = hash * 23 + gpu_constants.GetHashCode();
                    hash = hash * 23 + render_constants.GetHashCode();
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
                    hash = hash * 23 + (environment_map?.GetHashCode() ?? 0);
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