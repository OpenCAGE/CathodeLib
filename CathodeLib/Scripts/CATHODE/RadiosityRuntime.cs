using CathodeLib;
using CathodeLib.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/RENDERABLE/RADIOSITY_RUNTIME.BIN
    /// </summary>
    public class RadiosityRuntime : CathodeFile
    {
        public List<VolumeProbeVisSlice> VolumeProbeVisPalette = new List<VolumeProbeVisSlice>();
        public List<int> InstanceSliceIndices = new List<int>();

        public List<byte> SliceNeighbourCounts = new List<byte>();
        public List<short> SliceNeighbourArrayOffsets = new List<short>();
        public List<byte> FlattenedOtherSliceIndices = new List<byte>();
        public List<FixupRange> FlattenedFixupRanges = new List<FixupRange>();

        public List<RuntimeInfluenceFixup> InfluenceFixups = new List<RuntimeInfluenceFixup>();

        public List<RuntimeDataSlice> Slices = new List<RuntimeDataSlice>();

        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE;

        public bool Compressed { get { return _compressed; } set { _compressed = value; } }
        private bool _compressed = false;

        /// <summary>
        /// True when this radiosity was generated from scratch rather than shipped with the game.
        /// </summary>
        /// <remarks>
        /// The delta bake exists to protect CA's shipped lighting, which we cannot reproduce at
        /// parity: it keeps every retail slice verbatim and lights only what an edit invalidated.
        /// None of that applies to data we generated ourselves - there is no retail bake left
        /// underneath to protect, and patching our own output just compounds the delta path's
        /// approximations with every save. <see cref="Radiosity.RadiosityBaker.BakeLevel"/>
        /// therefore regenerates the whole level whenever this is set.
        ///
        /// Persisted as a small text marker beside the BIN rather than inside it: the file opens
        /// with a fixed 8-byte header (magic + 44, constant across every retail level) that the
        /// engine reads, and there is no spare field to claim.
        /// </remarks>
        public bool FullyRegenerated = false;

        private Resources _resources;

        public RadiosityRuntime(string path, Resources resources) : base(path)
        {
            _resources = resources;
        }
        public RadiosityRuntime(MemoryStream stream, Resources resources, string path = "") : base(stream, path)
        {
            _resources = resources;
        }
        public RadiosityRuntime(byte[] data, Resources resources, string path = "") : base(data, path)
        {
            _resources = resources;
        }

        public void ClearReferences()
        {
            _resources = null;
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            _compressed = _filepath != null && _filepath != "" && Path.GetExtension(_filepath).ToLower() == ".gz";

            using (BinaryReader reader = new BinaryReader(_compressed ? Utilities.GZIPDecompress(stream) : stream))
            {
                reader.BaseStream.Position += 8;

                VolumeProbeVisPalette = Utilities.ConsumeArray<VolumeProbeVisSlice>(reader, reader.ReadInt32()).ToList();

                int[] sliceOffsets = Utilities.ConsumeArray<int>(reader, reader.ReadInt32());
                InstanceSliceIndices = Utilities.ConsumeArray<int>(reader, reader.ReadInt32()).ToList();

                SliceNeighbourCounts = Utilities.ConsumeArray<byte>(reader, reader.ReadInt32()).ToList();
                SliceNeighbourArrayOffsets = Utilities.ConsumeArray<short>(reader, reader.ReadInt32()).ToList();
                FlattenedOtherSliceIndices = Utilities.ConsumeArray<byte>(reader, reader.ReadInt32()).ToList();
                FlattenedFixupRanges = Utilities.ConsumeArray<FixupRange>(reader, reader.ReadInt32()).ToList();

                int influenceFixupCount = reader.ReadInt32();
                Utilities.Align(reader);
                InfluenceFixups = Utilities.ConsumeArray<RuntimeInfluenceFixup>(reader, influenceFixupCount).ToList();

                Slices.Clear();
                for (int i = 0; i < sliceOffsets.Length; i++)
                {
                    reader.BaseStream.Position = sliceOffsets[i];
                    Slices.Add(new RuntimeDataSlice(reader));
                }
            }

            FullyRegenerated = ReadOwnershipMarker(_filepath);
            return true;
        }

        override protected bool SaveInternal()
        {
            if (_compressed && Path.GetExtension(_filepath).ToLower() != ".gz")
                _filepath += ".gz";
            else if (!_compressed && Path.GetExtension(_filepath).ToLower() == ".gz")
                _filepath = _filepath.Substring(0, _filepath.Length - 3);

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(1952739954); // "rrdt"
                writer.Write(44);

                writer.Write(VolumeProbeVisPalette.Count);
                Utilities.Write(writer, VolumeProbeVisPalette);

                writer.Write(Slices.Count);
                long sliceOffsetsPos = writer.BaseStream.Position;
                for (int i = 0; i < Slices.Count; i++)
                    writer.Write(0);

                writer.Write(InstanceSliceIndices.Count);
                Utilities.Write(writer, InstanceSliceIndices);

                writer.Write(SliceNeighbourCounts.Count);
                Utilities.Write(writer, SliceNeighbourCounts);

                writer.Write(SliceNeighbourArrayOffsets.Count);
                Utilities.Write(writer, SliceNeighbourArrayOffsets);

                writer.Write(FlattenedOtherSliceIndices.Count);
                Utilities.Write(writer, FlattenedOtherSliceIndices);

                writer.Write(FlattenedFixupRanges.Count);
                Utilities.Write(writer, FlattenedFixupRanges);

                writer.Write(InfluenceFixups.Count);
                Utilities.Align(writer);
                Utilities.Write(writer, InfluenceFixups);

                ResolveSurfaceLightEntities();

                int[] sliceOffsets = new int[Slices.Count];
                for (int i = 0; i < Slices.Count; i++)
                {
                    sliceOffsets[i] = (int)writer.BaseStream.Position;
                    Slices[i].Write(writer);
                }

                long endPos = writer.BaseStream.Position;
                writer.BaseStream.Position = sliceOffsetsPos;
                Utilities.Write(writer, sliceOffsets);
                writer.BaseStream.Position = endPos;
            }

            if (_compressed)
                Utilities.GZIPCompress(_filepath);

            WriteOwnershipMarker(_filepath, FullyRegenerated);
            return true;
        }

        #region OWNERSHIP_MARKER

        /// <summary>
        /// Where the <see cref="FullyRegenerated"/> marker for a given RADIOSITY_RUNTIME.BIN (or
        /// .BIN.GZ) lives. Nothing in the game reads it; deleting it only costs the next bake its
        /// knowledge that the data is ours, and it will patch instead of regenerating.
        /// </summary>
        /// <remarks>
        /// Named to match the sidecar convention the Commands custom tables already use - the
        /// original path with .META appended - so all of OpenCAGE's out-of-band level data reads
        /// the same way on disk. See <see cref="CathodeLib.CustomTable"/>.
        /// </remarks>
        public static string GetOwnershipMarkerPath(string runtimePath)
        {
            return string.IsNullOrEmpty(runtimePath) ? null : runtimePath + ".META";
        }

        private const string OwnershipMarkerLengthKey = "runtime_bytes=";

        private static bool ReadOwnershipMarker(string runtimePath)
        {
            string marker = GetOwnershipMarkerPath(runtimePath);
            if (marker == null || !File.Exists(marker) || !File.Exists(runtimePath))
                return false;

            try
            {
                long actualLength = new FileInfo(runtimePath).Length;
                foreach (string line in File.ReadAllLines(marker))
                {
                    if (!line.StartsWith(OwnershipMarkerLengthKey, StringComparison.Ordinal))
                        continue;

                    /* The recorded length is what stops a STALE marker claiming data that is no
                     * longer ours: putting the vanilla BIN back (a mod uninstall, a verified
                     * install, a restore from a pristine copy) leaves the marker behind, and the
                     * level would then throw CA's bake away on the next save. Any two distinct
                     * bakes differ in length in practice, and the only cost of a collision is a
                     * regeneration we did not need. */
                    return long.TryParse(
                               line.Substring(OwnershipMarkerLengthKey.Length).Trim(),
                               NumberStyles.Integer,
                               CultureInfo.InvariantCulture,
                               out long recordedLength)
                           && recordedLength == actualLength;
                }
            }
            catch { }

            return false;
        }

        private static void WriteOwnershipMarker(string runtimePath, bool fullyRegenerated)
        {
            string marker = GetOwnershipMarkerPath(runtimePath);
            if (marker == null)
                return;

            try
            {
                if (!fullyRegenerated)
                {
                    //A patched bake still rests on CA's data, so a marker left over from an
                    //earlier full regeneration would now be claiming something untrue.
                    File.Delete(marker);
                    return;
                }

                File.WriteAllText(marker,
                    "# OpenCAGE radiosity ownership marker." + Environment.NewLine +
                    "# This level's lighting was generated from scratch rather than shipped with" + Environment.NewLine +
                    "# the game, so future saves regenerate it instead of patching it. The game" + Environment.NewLine +
                    "# never reads this file and it is safe to delete." + Environment.NewLine +
                    "version=1" + Environment.NewLine +
                    "generated=" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + Environment.NewLine +
                    OwnershipMarkerLengthKey + new FileInfo(runtimePath).Length.ToString(CultureInfo.InvariantCulture) + Environment.NewLine);
            }
            catch { }
        }

        #endregion

        private void ResolveSurfaceLightEntities()
        {
            if (_resources == null)
                return;

            foreach (RuntimeDataSlice slice in Slices)
            {
                ResolveSlice(slice.SurfaceLights.LightSlices, slice.SurfaceLights.LightSliceEntities);
                ResolveSlice(slice.LiveSurfaceLights, slice.LiveSurfaceLightEntities);
            }

            void ResolveSlice(List<RuntimeSurfaceLights.LightSlice> lightSlices, List<Resources.Resource> entities)
            {
                if (entities == null || entities.Count != lightSlices.Count)
                    return;

                for (int i = 0; i < lightSlices.Count; i++)
                {
                    if (entities[i] == null)
                        continue;
                    RuntimeSurfaceLights.LightSlice ls = lightSlices[i];
                    ls.EntityInstanceIndex = _resources.GetWriteIndex(entities[i]);
                    lightSlices[i] = ls;
                }
            }
        }
        #endregion

        #region STRUCTURES
        public class RuntimeDataSlice
        {
            public RuntimeDataSliceCounts Counts = new RuntimeDataSliceCounts();

            public List<ColourRGBA8> SurfaceProbeInfluences = new List<ColourRGBA8>();
            public List<Vector4u8> SurfaceProbeWeights = new List<Vector4u8>();
            public List<Vector4> SurfaceProbePositions = new List<Vector4>();
            public List<Vector4u16> InputProbePositions = new List<Vector4u16>();
            public List<ColourRGBA8> InputProbeNormals = new List<ColourRGBA8>();
            public List<ColourRGBA8> InputProbeAlbedo = new List<ColourRGBA8>();
            public List<Vector4u16> ClusterPositions = new List<Vector4u16>();
            public List<ColourRGBA8> Scatter = new List<ColourRGBA8>();
            public List<ProbeTreeNode> InputProbeTreeNodes = new List<ProbeTreeNode>();
            public List<ProbeTreeNode> SurfaceProbeTreeNodes = new List<ProbeTreeNode>();
            public VolumeProbeHash VolumeProbeHash = new VolumeProbeHash();
            public RuntimeSurfaceLights SurfaceLights = new RuntimeSurfaceLights();
            public List<uint> InputProbeTreeQuads = new List<uint>();
            public List<uint> SurfaceProbeTreeQuads = new List<uint>();
            public List<ColourRGBA8> MangleMap = new List<ColourRGBA8>();
            public List<ProbeTileDims> InputProbeTiles = new List<ProbeTileDims>();
            public TiledScatterData TiledScatter = new TiledScatterData();
            public TiledSurfaceLights TiledSurfaceLights = new TiledSurfaceLights();
            public List<RuntimeSurfaceLights.LightSlice> LiveSurfaceLights = new List<RuntimeSurfaceLights.LightSlice>();

            /// <summary>
            /// Optional, parallel to <see cref="LiveSurfaceLights"/>. Set by the baker so
            /// EntityInstanceIndex can be resolved on save. Ignored when null or mismatched.
            /// </summary>
            public List<Resources.Resource> LiveSurfaceLightEntities = null;
            public DoorInfo Doors = new DoorInfo();
            public TiledDoorInfo TiledDoors = new TiledDoorInfo();

            public RuntimeDataSlice() { }
            public RuntimeDataSlice(BinaryReader reader) { Read(reader); }

            public void Read(BinaryReader reader)
            {
                fourcc magic = Utilities.Consume<fourcc>(reader);
                if (magic.ToString() != "rrds")
                    throw new Exception("Invalid magic for slice: " + magic);
                int version = reader.ReadInt32();
                if (version != 10003)
                    throw new Exception("Invalid version for slice: " + version);

                Utilities.Consume<RuntimeDataSliceOffsets>(reader); //don't need to use these
                Counts = Utilities.Consume<RuntimeDataSliceCounts>(reader);

                SurfaceProbeInfluences = Utilities.ConsumeArray<ColourRGBA8>(reader, reader.ReadInt32()).ToList();
                SurfaceProbeWeights = Utilities.ConsumeArray<Vector4u8>(reader, reader.ReadInt32()).ToList();
                SurfaceProbePositions = Utilities.ConsumeArray<Vector4>(reader, reader.ReadInt32()).ToList();
                InputProbePositions = Utilities.ConsumeArray<Vector4u16>(reader, reader.ReadInt32()).ToList();
                InputProbeNormals = Utilities.ConsumeArray<ColourRGBA8>(reader, reader.ReadInt32()).ToList();
                InputProbeAlbedo = Utilities.ConsumeArray<ColourRGBA8>(reader, reader.ReadInt32()).ToList();
                ClusterPositions = Utilities.ConsumeArray<Vector4u16>(reader, reader.ReadInt32()).ToList();
                Scatter = Utilities.ConsumeArray<ColourRGBA8>(reader, reader.ReadInt32()).ToList();
                InputProbeTreeNodes = Utilities.ConsumeArray<ProbeTreeNode>(reader, reader.ReadInt32()).ToList();
                SurfaceProbeTreeNodes = Utilities.ConsumeArray<ProbeTreeNode>(reader, reader.ReadInt32()).ToList();

                VolumeProbeHash = new VolumeProbeHash(reader, Counts);
                SurfaceLights = new RuntimeSurfaceLights(reader);

                InputProbeTreeQuads = Utilities.ConsumeArray<uint>(reader, reader.ReadInt32()).ToList();
                SurfaceProbeTreeQuads = Utilities.ConsumeArray<uint>(reader, reader.ReadInt32()).ToList();
                MangleMap = Utilities.ConsumeArray<ColourRGBA8>(reader, reader.ReadInt32()).ToList();
                InputProbeTiles = Utilities.ConsumeArray<ProbeTileDims>(reader, Counts.NumInputProbeTiles).ToList();
                TiledScatter = new TiledScatterData(reader);
                TiledSurfaceLights = new TiledSurfaceLights(reader);
                LiveSurfaceLights = Utilities.ConsumeArray<RuntimeSurfaceLights.LightSlice>(reader, reader.ReadInt32()).ToList();

                reader.BaseStream.Position += 16; //always zero

                Doors = new DoorInfo(reader);
                TiledDoors = new TiledDoorInfo(reader);
            }

            public void Write(BinaryWriter writer)
            {
                writer.Write(1935962738); //"rrds"
                writer.Write(10003);

                long offsetsPos = writer.BaseStream.Position;
                writer.BaseStream.Position += Marshal.SizeOf(typeof(RuntimeDataSliceOffsets));
                writer.BaseStream.Position += Marshal.SizeOf(typeof(RuntimeDataSliceCounts));

                RuntimeDataSliceOffsets offsets = new RuntimeDataSliceOffsets();

                offsets.SecIndexmap = (int)writer.BaseStream.Position;
                writer.Write(SurfaceProbeInfluences.Count);
                Utilities.Write(writer, SurfaceProbeInfluences);

                offsets.SecWeightmap = (int)writer.BaseStream.Position;
                writer.Write(SurfaceProbeWeights.Count);
                Utilities.Write(writer, SurfaceProbeWeights);

                offsets.SecSurfaceProbePositions = (int)writer.BaseStream.Position;
                writer.Write(SurfaceProbePositions.Count);
                Utilities.Write(writer, SurfaceProbePositions);

                offsets.SecInputProbePositions = (int)writer.BaseStream.Position;
                writer.Write(InputProbePositions.Count);
                Utilities.Write(writer, InputProbePositions);

                offsets.SecInputProbeNormals = (int)writer.BaseStream.Position;
                writer.Write(InputProbeNormals.Count);
                Utilities.Write(writer, InputProbeNormals);

                offsets.SecInputProbeAlbedos = (int)writer.BaseStream.Position;
                writer.Write(InputProbeAlbedo.Count);
                Utilities.Write(writer, InputProbeAlbedo);

                offsets.SecClusterPositions = (int)writer.BaseStream.Position;
                writer.Write(ClusterPositions.Count);
                Utilities.Write(writer, ClusterPositions);

                offsets.SecScatterVertices = (int)writer.BaseStream.Position;
                writer.Write(Scatter.Count);
                Utilities.Write(writer, Scatter);

                offsets.SecInputProbeTree = (int)writer.BaseStream.Position;
                writer.Write(InputProbeTreeNodes.Count);
                Utilities.Write(writer, InputProbeTreeNodes);

                offsets.SecSurfaceProbeTree = (int)writer.BaseStream.Position;
                writer.Write(SurfaceProbeTreeNodes.Count);
                Utilities.Write(writer, SurfaceProbeTreeNodes);

                offsets.SecVolumeProbeHash = (int)writer.BaseStream.Position;
                VolumeProbeHash.Write(writer);

                offsets.SecSurfaceLights = (int)writer.BaseStream.Position;
                SurfaceLights.Write(writer);

                offsets.SecInputQuads = (int)writer.BaseStream.Position;
                writer.Write(InputProbeTreeQuads.Count);
                Utilities.Write(writer, InputProbeTreeQuads);

                offsets.SecSurfaceQuads = (int)writer.BaseStream.Position;
                writer.Write(SurfaceProbeTreeQuads.Count);
                Utilities.Write(writer, SurfaceProbeTreeQuads);

                offsets.SecMangle = (int)writer.BaseStream.Position;
                writer.Write(MangleMap.Count);
                Utilities.Write(writer, MangleMap);

                offsets.SecInputProbeTiles = (int)writer.BaseStream.Position;
                Utilities.Write(writer, InputProbeTiles);

                offsets.SecTiledScatter = (int)writer.BaseStream.Position;
                TiledScatter.Write(writer);

                offsets.SecTiledSurfaceLights = (int)writer.BaseStream.Position;
                TiledSurfaceLights.Write(writer);

                offsets.SecLiveSurfaceLights = (int)writer.BaseStream.Position;
                writer.Write(LiveSurfaceLights.Count);
                Utilities.Write(writer, LiveSurfaceLights);

                offsets.SecLiveTiledSurfaceLights = (int)writer.BaseStream.Position;
                writer.Write(new byte[16]);

                offsets.SecDoors = (int)writer.BaseStream.Position;
                Doors.Write(writer);

                offsets.SecTiledDoors = (int)writer.BaseStream.Position;
                TiledDoors.Write(writer);

                RuntimeDataSliceCounts counts = BuildCounts();
                long endPos = writer.BaseStream.Position;
                writer.BaseStream.Position = offsetsPos;
                Utilities.Write(writer, offsets);
                Utilities.Write(writer, counts);
                writer.BaseStream.Position = endPos;
            }

            RuntimeDataSliceCounts BuildCounts()
            {
                RuntimeDataSliceCounts counts = Counts ?? new RuntimeDataSliceCounts();
                counts.NumSurfaceLightsVerts = SurfaceLights.Lights.Count;
                counts.NumDynamicSurfaceLights = SurfaceLights.LightSlices.Count;
                counts.NumVolumeHashNodes = VolumeProbeHash.Nodes.Count;
                counts.NumVolumeHashItems = VolumeProbeHash.Items.Count;
                counts.NumVolumeHashOffsets = VolumeProbeHash.Offsets.Count;
                counts.NumScatterVertices = Scatter.Count;
                counts.NumInputQuadVerts = InputProbeTreeQuads.Count;
                counts.NumSurfaceQuadVerts = SurfaceProbeTreeQuads.Count;
                counts.NumInputProbeTreeNodes = InputProbeTreeNodes.Count;
                counts.NumInputProbeTiles = InputProbeTiles.Count;
                counts.NumSurfaceProbeTreeNodes = SurfaceProbeTreeNodes.Count;
                counts.NumTiledScatterEvents = TiledScatter.EventTexOffsets.Count;
                counts.NumScatterTiles = TiledScatter.TileTexOffsets.Count;
                counts.NumTiledSurfaceLightProbes = TiledSurfaceLights.Lights.Count;
                counts.NumTiledDynamicSurfaceLights = TiledSurfaceLights.DynamicLightEntities.Count;
                counts.NumTiledDynamicSurfaceLightIndices = TiledSurfaceLights.DynamicLightsPixelIndices.Count;
                counts.NumSurfaceLightTiles = TiledSurfaceLights.Tiles.Count;
                counts.NumLiveSurfaceLights = LiveSurfaceLights.Count;
                counts.NumDoorTransfers = Doors.Transfers.Count;
                counts.NumDoors = Doors.Doors.Count;
                counts.NumTiledDoorTransfers = TiledDoors.Transfers.Count;
                return counts;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class VolumeProbeVisSlice
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8 * 8)]
            public byte[] Grid;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class FixupRange
        {
            public int First;
            public int Num;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class RuntimeInfluenceFixup
        {
            public int WeightTexOffset;
            public int InflTexOffset;
            public byte Weight;
            public byte Padding;
            public Vector2u8 ClusterTex;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class RuntimeDataSliceOffsets
        {
            public int SecIndexmap;
            public int SecWeightmap;
            public int SecSurfaceProbePositions;

            public int SecInputProbePositions;
            public int SecInputProbeNormals;
            public int SecInputProbeAlbedos;
            public int SecClusterPositions;

            public int SecScatterVertices;
            public int SecInputProbeTree;
            public int SecSurfaceProbeTree;
            public int SecVolumeProbeHash;
            public int SecSurfaceLights;
            public int SecInputQuads;
            public int SecSurfaceQuads;
            public int SecMangle;
            public int SecInputProbeTiles;
            public int SecTiledScatter;
            public int SecTiledSurfaceLights;
            public int SecLiveSurfaceLights;
            public int SecLiveTiledSurfaceLights;
            public int SecDoors;
            public int SecTiledDoors;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class RuntimeDataSliceCounts
        {
            public int NumSurfaceLightsVerts;
            public int NumDynamicSurfaceLights;
            public int NumVolumeHashNodes;
            public int NumVolumeHashItems;
            public int NumVolumeHashOffsets;
            public int NumScatterVertices;
            public int NumInputQuadVerts;
            public int NumSurfaceQuadVerts;

            public int NumInputProbeTreeNodes;
            public int NumInputProbeTiles;
            public int NumSurfaceProbeTreeNodes;
            public int NumTiledScatterEvents;
            public int NumScatterTiles;

            public int NumTiledSurfaceLightProbes;
            public int NumTiledDynamicSurfaceLights;
            public int NumTiledDynamicSurfaceLightIndices;
            public int NumSurfaceLightTiles;
            public int NumLiveSurfaceLights;

            //These are always zero
            public int UnknownZero1 = 0;
            public int UnknownZero2 = 0;

            public int NumDoorTransfers;
            public int NumDoors;
            public int NumTiledDoorTransfers;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class ProbeTileDims
        {
            public byte X;
            public byte Y;
            public byte Width;
            public byte Height;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class ProbeTreeNode
        {
            public Vector3 MinBounds;
            public Vector3 MaxBounds;
            public ushort ChildA;
            public ushort ChildB;
            public ushort IdxFirst;
            public ushort IdxCount;

            public bool IsLeaf() => ChildA == 0 && ChildB == 0;
        }

        public class VolumeProbeHash
        {
            public uint NumSubdivsPerLevel;
            public Vector3 AabbMin;
            public Vector3 AabbMax;
            public Vector3u32 Dims = new Vector3u32();
            public List<Probe> Items = new List<Probe>();
            public List<ushort> Nodes = new List<ushort>();
            public List<ushort> Offsets = new List<ushort>();

            public VolumeProbeHash() { }
            public VolumeProbeHash(BinaryReader reader, RuntimeDataSliceCounts counts) { Read(reader, counts); }

            public void Read(BinaryReader reader, RuntimeDataSliceCounts counts)
            {
                NumSubdivsPerLevel = reader.ReadUInt32();
                AabbMin = Utilities.Consume<Vector3>(reader);
                AabbMax = Utilities.Consume<Vector3>(reader);
                Dims = Utilities.Consume<Vector3u32>(reader);
                Utilities.Align(reader);
                Items = Utilities.ConsumeArray<Probe>(reader, counts.NumVolumeHashItems).ToList();
                Nodes = Utilities.ConsumeArray<ushort>(reader, counts.NumVolumeHashNodes).ToList();
                Utilities.Align(reader);
                Offsets = Utilities.ConsumeArray<ushort>(reader, counts.NumVolumeHashOffsets).ToList();
                Utilities.Align(reader);
            }

            public void Write(BinaryWriter writer)
            {
                writer.Write(NumSubdivsPerLevel);
                Utilities.Write(writer, AabbMin);
                Utilities.Write(writer, AabbMax);
                Utilities.Write(writer, Dims);
                Utilities.Align(writer);
                Utilities.Write(writer, Items);
                Utilities.Write(writer, Nodes);
                Utilities.Align(writer);
                Utilities.Write(writer, Offsets);
                Utilities.Align(writer);
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public class Probe
            {
                public Vector2u8 UV = new Vector2u8() { X = 255, Y = 255 };
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
                public byte[] VisPaletteEntries = new byte[6];
            }
        }

        public class RuntimeSurfaceLights
        {
            public List<Light> Lights = new List<Light>();
            public List<LightSlice> LightSlices = new List<LightSlice>();

            /// <summary>
            /// Optional, parallel to <see cref="LightSlices"/>. Set by the baker so
            /// EntityInstanceIndex can be resolved on save. Ignored when null or mismatched.
            /// </summary>
            public List<Resources.Resource> LightSliceEntities = null;

            public RuntimeSurfaceLights() { }
            public RuntimeSurfaceLights(BinaryReader reader) { Read(reader); }

            public void Read(BinaryReader reader)
            {
                Lights = Utilities.ConsumeArray<Light>(reader, reader.ReadInt32()).ToList();
                LightSlices = Utilities.ConsumeArray<LightSlice>(reader, reader.ReadInt32()).ToList();
            }

            public void Write(BinaryWriter writer)
            {
                writer.Write(Lights.Count);
                Utilities.Write(writer, Lights);
                writer.Write(LightSlices.Count);
                Utilities.Write(writer, LightSlices);
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct LightSlice
            {
                public uint FirstItem;
                public int EntityInstanceIndex;
                public ushort NumItems;
                public ushort SiblingIndex;
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct Light
            {
                public byte U, V, AnimHi, AnimLo;
                public byte R, G, B, Scale;
                public byte Weight, Padding1, Padding2, Padding3;
                public byte TintR, TintG, TintB, Flags;
            }
        }

        public class TiledScatterData
        {
            public List<ushort> EventTexOffsets = new List<ushort>();
            public List<byte> EventTileOffsets = new List<byte>();
            public List<uint> TileTexOffsets = new List<uint>();
            public List<uint> TileEventOffsets = new List<uint>();
            public List<uint> TileEventCounts = new List<uint>();

            public TiledScatterData() { }
            public TiledScatterData(BinaryReader reader) { Read(reader); }

            public void Read(BinaryReader reader)
            {
                EventTexOffsets = Utilities.ConsumeArray<ushort>(reader, reader.ReadInt32()).ToList();
                Utilities.Align(reader);
                EventTileOffsets = Utilities.ConsumeArray<byte>(reader, reader.ReadInt32()).ToList();
                Utilities.Align(reader);
                TileTexOffsets = Utilities.ConsumeArray<uint>(reader, reader.ReadInt32()).ToList();
                TileEventOffsets = Utilities.ConsumeArray<uint>(reader, reader.ReadInt32()).ToList();
                TileEventCounts = Utilities.ConsumeArray<uint>(reader, reader.ReadInt32()).ToList();
            }

            public void Write(BinaryWriter writer)
            {
                writer.Write(EventTexOffsets.Count);
                Utilities.Write(writer, EventTexOffsets);
                Utilities.Align(writer);
                writer.Write(EventTileOffsets.Count);
                Utilities.Write(writer, EventTileOffsets);
                Utilities.Align(writer);
                writer.Write(TileTexOffsets.Count);
                Utilities.Write(writer, TileTexOffsets);
                writer.Write(TileEventOffsets.Count);
                Utilities.Write(writer, TileEventOffsets);
                writer.Write(TileEventCounts.Count);
                Utilities.Write(writer, TileEventCounts);
            }
        }

        public class TiledSurfaceLights
        {
            public List<RuntimeSurfaceLights.Light> Lights = new List<RuntimeSurfaceLights.Light>();
            public List<SliceU16> Tiles = new List<SliceU16>();
            public List<int> DynamicLightEntities = new List<int>();
            public List<SliceU16> DynamicLights = new List<SliceU16>();
            public List<ushort> DynamicLightsPixelIndices = new List<ushort>();
            public List<ushort> DynamicLightSiblingIndices = new List<ushort>();

            public TiledSurfaceLights() { }
            public TiledSurfaceLights(BinaryReader reader) { Read(reader); }

            public void Read(BinaryReader reader)
            {
                Lights = Utilities.ConsumeArray<RuntimeSurfaceLights.Light>(reader, reader.ReadInt32()).ToList();
                Tiles = Utilities.ConsumeArray<SliceU16>(reader, reader.ReadInt32()).ToList();
                DynamicLightEntities = Utilities.ConsumeArray<int>(reader, reader.ReadInt32()).ToList();
                DynamicLights = Utilities.ConsumeArray<SliceU16>(reader, reader.ReadInt32()).ToList();
                DynamicLightsPixelIndices = Utilities.ConsumeArray<ushort>(reader, reader.ReadInt32()).ToList();
                Utilities.Align(reader);
                DynamicLightSiblingIndices = Utilities.ConsumeArray<ushort>(reader, reader.ReadInt32()).ToList();
                Utilities.Align(reader);
            }

            public void Write(BinaryWriter writer)
            {
                writer.Write(Lights.Count);
                Utilities.Write(writer, Lights);
                writer.Write(Tiles.Count);
                Utilities.Write(writer, Tiles);
                writer.Write(DynamicLightEntities.Count);
                Utilities.Write(writer, DynamicLightEntities);
                writer.Write(DynamicLights.Count);
                Utilities.Write(writer, DynamicLights);
                writer.Write(DynamicLightsPixelIndices.Count);
                Utilities.Write(writer, DynamicLightsPixelIndices);
                Utilities.Align(writer);
                writer.Write(DynamicLightSiblingIndices.Count);
                Utilities.Write(writer, DynamicLightSiblingIndices);
                Utilities.Align(writer);
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class DoorTransfer
        {
            public Vector2u8 InputProbe;
            public Vector2u8 SurfaceProbe;
            public float Weight;
        }

        public class DoorInfo
        {
            public List<SliceU16> Doors = new List<SliceU16>();
            public List<DoorTransfer> Transfers = new List<DoorTransfer>();
            public List<int> NavmeshBarrierCathodeInstanceIndex = new List<int>();

            public DoorInfo() { }
            public DoorInfo(BinaryReader reader) { Read(reader); }

            public void Read(BinaryReader reader)
            {
                Doors = Utilities.ConsumeArray<SliceU16>(reader, reader.ReadInt32()).ToList();
                Transfers = Utilities.ConsumeArray<DoorTransfer>(reader, reader.ReadInt32()).ToList();
                NavmeshBarrierCathodeInstanceIndex = Utilities.ConsumeArray<int>(reader, reader.ReadInt32()).ToList();
            }

            public void Write(BinaryWriter writer)
            {
                writer.Write(Doors.Count);
                Utilities.Write(writer, Doors);
                writer.Write(Transfers.Count);
                Utilities.Write(writer, Transfers);
                writer.Write(NavmeshBarrierCathodeInstanceIndex.Count);
                Utilities.Write(writer, NavmeshBarrierCathodeInstanceIndex);
            }
        }

        public class TiledDoorInfo
        {
            public List<DoorTransfer> Transfers = new List<DoorTransfer>();
            public List<SliceU16> Tiles = new List<SliceU16>();
            public List<int> NavmeshBarrierCathodeInstanceIndex = new List<int>();
            public List<SliceU16> DynamicTransfers = new List<SliceU16>();
            public List<ushort> DynamicTransferPixelIndices = new List<ushort>();

            public TiledDoorInfo() { }
            public TiledDoorInfo(BinaryReader reader) { Read(reader); }

            public void Read(BinaryReader reader)
            {
                Transfers = Utilities.ConsumeArray<DoorTransfer>(reader, reader.ReadInt32()).ToList();
                Tiles = Utilities.ConsumeArray<SliceU16>(reader, reader.ReadInt32()).ToList();
                NavmeshBarrierCathodeInstanceIndex = Utilities.ConsumeArray<int>(reader, reader.ReadInt32()).ToList();
                DynamicTransfers = Utilities.ConsumeArray<SliceU16>(reader, reader.ReadInt32()).ToList();
                DynamicTransferPixelIndices = Utilities.ConsumeArray<ushort>(reader, reader.ReadInt32()).ToList();
                Utilities.Align(reader);
            }

            public void Write(BinaryWriter writer)
            {
                writer.Write(Transfers.Count);
                Utilities.Write(writer, Transfers);
                writer.Write(Tiles.Count);
                Utilities.Write(writer, Tiles);
                writer.Write(NavmeshBarrierCathodeInstanceIndex.Count);
                Utilities.Write(writer, NavmeshBarrierCathodeInstanceIndex);
                writer.Write(DynamicTransfers.Count);
                Utilities.Write(writer, DynamicTransfers);
                writer.Write(DynamicTransferPixelIndices.Count);
                Utilities.Write(writer, DynamicTransferPixelIndices);
                Utilities.Align(writer);
            }
        }
        #endregion
    }
}
