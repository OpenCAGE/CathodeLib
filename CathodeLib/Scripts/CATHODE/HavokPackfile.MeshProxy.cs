using System;
using System.Collections.Generic;
using System.Numerics;

namespace CATHODE
{
    /* Writing a NEW collision proxy - an hkpStaticCompoundShape over a freshly encoded hkpBvCompressedMeshShape -
       into a 2012 packfile (COLLISION.HKX / COLLISION.HKX64), from a plain triangle list.

       What retail ships, measured over every PC level (59,024 proxies): a proxy is a compound whose instances are
       all hkpBvCompressedMeshShape children (never boxes, never nested compounds), 79% of them with one child at
       the identity, and the compound, its instance and the mesh all carry the physics material's write index as
       their userData. The mesh is an hkcdStaticMeshTree<..., 11, 21>: a Codec3Axis5 tree over sections, each
       section a Codec3Axis4 tree over at most 127 primitives, its own vertices packed to 11/11/10 bits against a
       per-section codec and the vertices it shares with other sections kept once, 21 bits per axis against the
       whole mesh. Every field below was read off those files and the reading was checked with a walk of all
       82,636 retail mesh trees (each walk reaching every leaf once, each section's leaf naming the tree node
       that holds it). See the harness modes bvdump / meshtree in ReleaseSweep. */
    public partial class HavokPackfile
    {
        /// <summary>Retail never puts more primitives than this in a section (the Codec3Axis4 leaf payload is 7 bits).</summary>
        const int MeshSectionMaxPrimitives = 127;
        /// <summary>A section's vertices are addressed by a byte; retail stops at 254 packed. Headroom is left for the shared ones.</summary>
        const int MeshSectionMaxVertices = 200;
        /// <summary>The Codec3Axis5 leaf payload over sections is 15 bits.</summary>
        const int MeshMaxSections = 32767;
        /// <summary>The shared-vertex index is a u16, so a mesh can share at most this many vertices between its sections.</summary>
        const int MeshMaxSharedVertices = 65536;
        /// <summary>A section this wide would quantise its vertices coarser than 4 mm (11 bits over the extent); retail keeps them smaller than this.</summary>
        const float MeshSectionMaxExtent = 8f;
        const int MeshMaxTriangles = 2000000;

        /// <summary>
        /// Everything an append changes, so a failed append (or a mismatch between the two packfiles) can be
        /// put back exactly. The payload is copied whole: it is a few megabytes and the alternative - undoing an
        /// append field by field - would have to know every in-place patch the registry padding makes.
        /// </summary>
        public sealed class AppendCheckpoint
        {
            internal byte[] Payload;
            internal byte[] Classnames;
            internal List<LocalFixup> Local;
            internal List<GlobalFixup> Global;
            internal List<VirtualFixup> Virtual;
            internal List<PackfileObject> Objects;
            internal List<StaticCompoundShape> Compounds;
            internal List<PhysicsSystem> Physics;
        }

        public AppendCheckpoint CreateCheckpoint()
        {
            return new AppendCheckpoint
            {
                Payload = (byte[])DataPayload.Clone(),
                Classnames = (byte[])ClassnamesData.Clone(),
                Local = new List<LocalFixup>(LocalFixups),
                Global = new List<GlobalFixup>(GlobalFixups),
                Virtual = new List<VirtualFixup>(VirtualFixups),
                Objects = new List<PackfileObject>(Objects),
                Compounds = new List<StaticCompoundShape>(StaticCompoundShapes),
                Physics = new List<PhysicsSystem>(PhysicsSystems),
            };
        }

        public void RestoreCheckpoint(AppendCheckpoint checkpoint)
        {
            if (checkpoint == null)
                throw new ArgumentNullException(nameof(checkpoint));
            DataPayload = checkpoint.Payload;
            ClassnamesData = checkpoint.Classnames;
            LocalFixups = checkpoint.Local;
            GlobalFixups = checkpoint.Global;
            VirtualFixups = checkpoint.Virtual;
            Objects = checkpoint.Objects;
            //The views are put back rather than re-parsed: an append only adds compounds, and re-parsing would hand
            //out new objects for every compound, leaving the references others hold (a COLLISION.MAP row's
            //CollisionProxy, a picker's selection) pointing at copies no longer in the list
            StaticCompoundShapes = new List<StaticCompoundShape>(checkpoint.Compounds);
            PhysicsSystems = new List<PhysicsSystem>(checkpoint.Physics);
        }

        /// <summary>
        /// Append a new collision proxy built from a triangle mesh: a one-instance hkpStaticCompoundShape over a
        /// new hkpBvCompressedMeshShape, registered in the proxy list so it has an ordinal a COLLISION.MAP row can
        /// name. The mesh is in the proxy's own space, in metres, with the winding the game's own meshes decode to
        /// (the space <see cref="BuildPreviewMesh(StaticCompoundShape)"/> gives back for a template compound).
        /// </summary>
        /// <param name="positions">Vertex positions.</param>
        /// <param name="triangles">Three indices per triangle into <paramref name="positions"/>.</param>
        /// <param name="userData">What retail stores here is the write index of the row's physics material.</param>
        /// <param name="filterInfo">The collision type of the template instance; retail uses 9 (BALLISTICS) for prop materials and 3 (STANDARD) for world collision.</param>
        /// <returns>The new compound, last in <see cref="StaticCompoundShapes"/>, with its <c>ProxyIndex</c> assigned.</returns>
        /// <remarks>
        /// Both packfiles of a level (32 and 64-bit) need the proxy at the same ordinal; call this on each with the
        /// same mesh and compare the results, restoring a <see cref="CreateCheckpoint"/> on either side if they
        /// disagree. Not available on the mobile/Switch tagfiles.
        /// </remarks>
        public StaticCompoundShape AddMeshCollisionProxy(IList<Vector3> positions, IList<int> triangles, uint userData = 0, uint filterInfo = 9)
        {
            if (Tagfile != null)
                throw new NotSupportedException("New collision meshes can only be written to the PC packfiles, not a mobile/Switch tagfile.");
            if (positions == null || triangles == null)
                throw new ArgumentNullException(positions == null ? nameof(positions) : nameof(triangles));

            BuiltMesh built = MeshProxyBuilder.Build(positions, triangles);
            uint meshOffset = AppendMeshShapeObject(built, userData);
            uint compoundOffset = AppendCompoundShell(userData, Math.Max(1, BitsOf(built.MaxKeyValue)));

            //The new compound is last in the payload and in the object list, so its ordinal is the number of compounds
            //before it - the same rank a reload assigns. Only its own view is made: re-parsing them all would replace
            //every compound object, and the rows and pickers holding the old ones would be left with stale copies.
            PackfileObject compoundObject = Objects[Objects.Count - 1];
            int ordinal = 0;
            for (int i = 0; i < Objects.Count - 1; i++)
                if (Objects[i].Class == ObjectClass.StaticCompoundShape) ordinal++;
            compoundObject.ProxyIndex = ordinal;
            StaticCompoundShape compound = new StaticCompoundShape { ProxyIndex = ordinal, DataOffset = compoundOffset };
            StaticCompoundShapes.Add(compound);

            compound.DomainMin = new Vector4(float.MaxValue, float.MaxValue, float.MaxValue, 0f);
            compound.DomainMax = new Vector4(float.MinValue, float.MinValue, float.MinValue, 0f);
            compound.AddInstance(new CompoundInstance
            {
                Translation = new Vector4(0f, 0f, 0f, 0.5f),  //W = 0x3F000000: retail's template instance flag word, no bits set
                Rotation = Quaternion.Identity,
                Scale = new Vector4(1f, 1f, 1f, 0.5f),
                FilterInfo = filterInfo,
                ChildFilterInfoMask = 0,
                UserData = userData,
                ShapeDataOffset = meshOffset,
                ShapeClassName = "hkpBvCompressedMeshShape",
            });
            RewriteCompoundArrays(compound);
            EnsureProxyListCoversCompounds();
            RefreshCompoundShapeKeyBits();

            //The reader is the first oracle: what went in must come back out, triangle for triangle
            PreviewMesh check = BuildBakeMesh(compound);
            if (check.TriangleCount != built.PrimitiveCount)
                throw new InvalidOperationException("The new collision mesh reads back with " + check.TriangleCount + " triangles where " + built.PrimitiveCount + " were written.");
            return compound;
        }

        static int BitsOf(uint value)
        {
            int bits = 0;
            while (value != 0) { bits++; value >>= 1; }
            return bits;
        }

        // ------------------------------------------------------------------ the mesh object

        /// <summary>
        /// hkpBvCompressedMeshShape: header copied from a retail one of this file (vtable slot, hkcdShape bytes,
        /// bvTreeType 3, convexRadius 0, weldingType NONE, empty palettes), then the embedded tree written from
        /// <paramref name="built"/> with its arrays appended after the object.
        /// </summary>
        uint AppendMeshShapeObject(BuiltMesh built, uint userData)
        {
            PackfileObject template = null;
            for (int i = 0; i < Objects.Count; i++)
                if (Objects[i].Class == ObjectClass.BvCompressedMeshShape) { template = Objects[i]; break; }
            if (template == null)
                throw new InvalidOperationException("This packfile holds no hkpBvCompressedMeshShape to model the new one on.");

            int ptr = Header.PointerSize;
            int arraySize = ptr + 8;
            int headerLen = ptr == 8 ? 0x70 : 0x50;
            int treeLen = ptr == 8 ? 160 : 144;
            int objectSize = headerLen + treeLen;

            int sectionCount = built.Sections.Count;
            int dst = AlignPayload(DataPayload.Length, 16);
            int topNodesOff = AlignPayload(dst + objectSize, 16);
            int sectionsOff = AlignPayload(topNodesOff + built.TopNodes.Count * 5, 16);
            int[] sectionNodesOff = new int[sectionCount];
            int cursor = AlignPayload(sectionsOff + sectionCount * 96, 16);
            for (int s = 0; s < sectionCount; s++)
            {
                sectionNodesOff[s] = cursor;
                cursor = AlignPayload(cursor + built.Sections[s].Nodes.Count * 4, 16);
            }
            int primitivesOff = cursor;
            int sharedIdxOff = AlignPayload(primitivesOff + built.Primitives.Count * 4, 16);
            int packedOff = AlignPayload(sharedIdxOff + built.SharedIndex.Count * 2, 16);
            int sharedOff = AlignPayload(packedOff + built.Packed.Count * 4, 16);
            int runsOff = AlignPayload(sharedOff + built.Shared.Count * 8, 16);
            int end = AlignPayload(runsOff + sectionCount * 8, 16);

            byte[] grown = new byte[end];
            Buffer.BlockCopy(DataPayload, 0, grown, 0, DataPayload.Length);
            DataPayload = grown;
            byte[] d = DataPayload;

            //Header: the retail shape's bytes up to its tree, palettes made empty in case the template's were not
            Buffer.BlockCopy(d, (int)template.DataOffset, d, dst, headerLen);
            int palettes = ptr == 8 ? 56 : 32;
            for (int k = 0; k < 3; k++)
            {
                int field = dst + palettes + k * arraySize;
                Array.Clear(d, field, ptr);
                WriteUInt32(d, field + ptr, 0);
                WriteUInt32(d, field + ptr + 4, 0x80000000u);
            }
            int userDataAt = dst + (ptr == 8 ? 24 : 12);
            WriteUInt32(d, userDataAt, userData);
            if (ptr == 8) WriteUInt32(d, userDataAt + 4, 0);

            //The tree
            int tree = dst + headerLen;
            Array.Clear(d, tree, treeLen);
            WriteArrayHeader(d, tree, ptr, built.TopNodes.Count);
            WriteVector4(d, tree + 16, new Vector4(built.DomainMin, 0f));
            WriteVector4(d, tree + 32, new Vector4(built.DomainMax, 0f));
            WriteUInt32(d, tree + 48, built.NumPrimitiveKeys);
            WriteUInt32(d, tree + 52, built.BitsPerKey);
            WriteUInt32(d, tree + 56, built.MaxKeyValue);
            int arrays = tree + (ptr == 8 ? 64 : 60);
            WriteArrayHeader(d, arrays, ptr, sectionCount);
            WriteArrayHeader(d, arrays + arraySize, ptr, built.Primitives.Count);
            WriteArrayHeader(d, arrays + 2 * arraySize, ptr, built.SharedIndex.Count);
            WriteArrayHeader(d, arrays + 3 * arraySize, ptr, built.Packed.Count);
            WriteArrayHeader(d, arrays + 4 * arraySize, ptr, built.Shared.Count);
            WriteArrayHeader(d, arrays + 5 * arraySize, ptr, sectionCount);

            LocalFixups.Add(new LocalFixup { Src = (uint)tree, Dst = (uint)topNodesOff });
            LocalFixups.Add(new LocalFixup { Src = (uint)arrays, Dst = (uint)sectionsOff });
            LocalFixups.Add(new LocalFixup { Src = (uint)(arrays + arraySize), Dst = (uint)primitivesOff });
            if (built.SharedIndex.Count > 0)
                LocalFixups.Add(new LocalFixup { Src = (uint)(arrays + 2 * arraySize), Dst = (uint)sharedIdxOff });
            LocalFixups.Add(new LocalFixup { Src = (uint)(arrays + 3 * arraySize), Dst = (uint)packedOff });
            if (built.Shared.Count > 0)
                LocalFixups.Add(new LocalFixup { Src = (uint)(arrays + 4 * arraySize), Dst = (uint)sharedOff });
            LocalFixups.Add(new LocalFixup { Src = (uint)(arrays + 5 * arraySize), Dst = (uint)runsOff });

            //Nodes over sections: 5 bytes each
            for (int n = 0; n < built.TopNodes.Count; n++)
                Buffer.BlockCopy(built.TopNodes[n], 0, d, topNodesOff + n * 5, 5);

            //Sections, each with its own node array
            for (int s = 0; s < sectionCount; s++)
            {
                BuiltSection sec = built.Sections[s];
                int rec = sectionsOff + s * 96;
                WriteArrayHeader(d, rec, ptr, sec.Nodes.Count);
                LocalFixups.Add(new LocalFixup { Src = (uint)rec, Dst = (uint)sectionNodesOff[s] });
                WriteVector4(d, rec + 16, new Vector4(sec.DomainMin, 0f));
                WriteVector4(d, rec + 32, new Vector4(sec.DomainMax, 0f));
                WriteSingle(d, rec + 48, sec.CodecBase.X);
                WriteSingle(d, rec + 52, sec.CodecBase.Y);
                WriteSingle(d, rec + 56, sec.CodecBase.Z);
                WriteSingle(d, rec + 60, sec.CodecScale.X);
                WriteSingle(d, rec + 64, sec.CodecScale.Y);
                WriteSingle(d, rec + 68, sec.CodecScale.Z);
                WriteUInt32(d, rec + 72, (uint)sec.FirstPacked);
                WriteUInt32(d, rec + 76, ((uint)sec.FirstSharedIndex << 8) | (uint)sec.NumPacked);
                WriteUInt32(d, rec + 80, ((uint)sec.FirstPrimitive << 8) | (uint)sec.NumPrimitives);
                WriteUInt32(d, rec + 84, ((uint)s << 8) | 1u);   //one data run per section
                d[rec + 88] = (byte)sec.NumPacked;
                d[rec + 89] = (byte)sec.NumShared;
                d[rec + 90] = (byte)(sec.LeafIndex & 0xFF);
                d[rec + 91] = (byte)(sec.LeafIndex >> 8);
                //+92 page, +93 flags, +94 layer: zero, as retail
                for (int n = 0; n < sec.Nodes.Count; n++)
                    Buffer.BlockCopy(sec.Nodes[n], 0, d, sectionNodesOff[s] + n * 4, 4);
            }

            for (int p = 0; p < built.Primitives.Count; p++)
                Buffer.BlockCopy(built.Primitives[p], 0, d, primitivesOff + p * 4, 4);
            for (int i = 0; i < built.SharedIndex.Count; i++)
            {
                d[sharedIdxOff + i * 2] = (byte)(built.SharedIndex[i] & 0xFF);
                d[sharedIdxOff + i * 2 + 1] = (byte)(built.SharedIndex[i] >> 8);
            }
            for (int i = 0; i < built.Packed.Count; i++)
                WriteUInt32(d, packedOff + i * 4, built.Packed[i]);
            for (int i = 0; i < built.Shared.Count; i++)
            {
                WriteUInt32(d, sharedOff + i * 8, (uint)(built.Shared[i] & 0xFFFFFFFFul));
                WriteUInt32(d, sharedOff + i * 8 + 4, (uint)(built.Shared[i] >> 32));
            }
            for (int s = 0; s < sectionCount; s++)
            {
                int run = runsOff + s * 8;
                WriteUInt32(d, run, 0);                              //value: every primitive's data is 0
                d[run + 4] = 0;                                      //index
                d[run + 5] = (byte)built.Sections[s].NumPrimitives;  //count
            }

            int nameOffset = template.ClassNameOffset;
            VirtualFixups.Add(new VirtualFixup { Src = (uint)dst, SectionIndex = 0, NameOffset = nameOffset });
            Objects.Add(new PackfileObject
            {
                DataOffset = (uint)dst,
                ClassNameOffset = nameOffset,
                ClassName = "hkpBvCompressedMeshShape",
                Class = ObjectClass.BvCompressedMeshShape,
                ProxyIndex = -1,
            });
            return (uint)dst;
        }

        static void WriteArrayHeader(byte[] d, int field, int ptr, int count)
        {
            Array.Clear(d, field, ptr);
            WriteUInt32(d, field + ptr, (uint)count);
            WriteUInt32(d, field + ptr + 4, (uint)count | 0x80000000u);
        }

        // ------------------------------------------------------------------ the compound shell

        /// <summary>
        /// hkpStaticCompoundShape with no instances yet: a retail one-mesh template's bytes with the arrays and
        /// domain cleared, the key width set and the two array fields given local fixups, so that
        /// <see cref="RewriteCompoundArrays"/> can find and fill them once the instance is added.
        /// </summary>
        uint AppendCompoundShell(uint userData, int numBitsForChildShapeKey)
        {
            StaticCompoundShape template = null;
            PackfileObject templateObject = null;
            StaticCompoundShape primary = WorldHostPrimary, secondary = WorldHostSecondary;
            for (int i = 0; i < StaticCompoundShapes.Count && template == null; i++)
            {
                StaticCompoundShape c = StaticCompoundShapes[i];
                if (c == primary || c == secondary || c.Instances.Count != 1)
                    continue;
                if (!string.Equals(c.Instances[0].ShapeClassName, "hkpBvCompressedMeshShape", StringComparison.Ordinal))
                    continue;
                for (int o = 0; o < Objects.Count; o++)
                    if (Objects[o].DataOffset == c.DataOffset && Objects[o].Class == ObjectClass.StaticCompoundShape) { templateObject = Objects[o]; break; }
                if (templateObject != null)
                    template = c;
            }
            if (template == null)
                throw new InvalidOperationException("This packfile holds no one-mesh template compound to model the new one on.");

            //The two array fields are the object's two lowest local fixups (instances, then tree nodes)
            uint first = uint.MaxValue, second = uint.MaxValue;
            for (int i = 0; i < LocalFixups.Count; i++)
            {
                uint src = LocalFixups[i].Src;
                if (src < template.DataOffset || src >= template.DataOffset + 0x100) continue;
                if (src < first) { second = first; first = src; }
                else if (src < second) second = src;
            }
            if (second == uint.MaxValue)
                throw new InvalidOperationException("The template compound does not carry its two array fields.");

            int ptr = Header.PointerSize;
            int arraySize = ptr + 8;
            int instancesRel = (int)(first - template.DataOffset);
            int nodesRel = (int)(second - template.DataOffset);
            int domainRel = AlignPayload(nodesRel + arraySize, 16);
            int objectSize = domainRel + 32;

            int dst = AlignPayload(DataPayload.Length, 16);
            byte[] grown = new byte[dst + objectSize];
            Buffer.BlockCopy(DataPayload, 0, grown, 0, DataPayload.Length);
            DataPayload = grown;
            byte[] d = DataPayload;
            Buffer.BlockCopy(d, (int)template.DataOffset, d, dst, objectSize);

            WriteArrayHeader(d, dst + instancesRel, ptr, 0);
            WriteArrayHeader(d, dst + nodesRel, ptr, 0);
            Array.Clear(d, dst + domainRel, 32);
            int userDataAt = dst + (ptr == 8 ? 24 : 12);
            WriteUInt32(d, userDataAt, userData);
            if (ptr == 8) WriteUInt32(d, userDataAt + 4, 0);
            d[dst + (ptr == 8 ? 0x30 : 0x18)] = (byte)numBitsForChildShapeKey;

            //Provisional targets: the rewrite appends the real arrays and repoints these
            LocalFixups.Add(new LocalFixup { Src = (uint)(dst + instancesRel), Dst = (uint)(dst + objectSize) });
            LocalFixups.Add(new LocalFixup { Src = (uint)(dst + nodesRel), Dst = (uint)(dst + objectSize) });

            VirtualFixups.Add(new VirtualFixup { Src = (uint)dst, SectionIndex = 0, NameOffset = templateObject.ClassNameOffset });
            Objects.Add(new PackfileObject
            {
                DataOffset = (uint)dst,
                ClassNameOffset = templateObject.ClassNameOffset,
                ClassName = "hkpStaticCompoundShape",
                Class = ObjectClass.StaticCompoundShape,
                ProxyIndex = -1,
            });
            return (uint)dst;
        }

        // ------------------------------------------------------------------ the mesh tree

        sealed class BuiltSection
        {
            public List<byte[]> Nodes = new List<byte[]>();
            public Vector3 DomainMin, DomainMax, CodecBase, CodecScale;
            public int FirstPacked, NumPacked, FirstSharedIndex, NumShared, FirstPrimitive, NumPrimitives, LeafIndex;
        }

        sealed class BuiltMesh
        {
            public Vector3 DomainMin, DomainMax;
            public List<byte[]> TopNodes = new List<byte[]>();
            public List<BuiltSection> Sections = new List<BuiltSection>();
            public List<uint> Packed = new List<uint>();
            public List<ulong> Shared = new List<ulong>();
            public List<ushort> SharedIndex = new List<ushort>();
            public List<byte[]> Primitives = new List<byte[]>();
            public uint NumPrimitiveKeys, BitsPerKey, MaxKeyValue;
            public int PrimitiveCount => Primitives.Count;
        }

        struct MeshBox
        {
            public Vector3 Min, Max;
            public static MeshBox Empty => new MeshBox { Min = new Vector3(float.MaxValue), Max = new Vector3(float.MinValue) };
            public void Add(Vector3 p) { Min = Vector3.Min(Min, p); Max = Vector3.Max(Max, p); }
            public void Add(MeshBox b) { Min = Vector3.Min(Min, b.Min); Max = Vector3.Max(Max, b.Max); }
            public Vector3 Centre => (Min + Max) * 0.5f;
            public MeshBox ClampTo(MeshBox outer) => new MeshBox { Min = Vector3.Max(Min, outer.Min), Max = Vector3.Min(Max, outer.Max) };
        }

        static class MeshProxyBuilder
        {
            const ulong SharedMask = (1UL << 21) - 1UL;

            public static BuiltMesh Build(IList<Vector3> positions, IList<int> indices)
            {
                if (indices.Count % 3 != 0)
                    throw new ArgumentException("The index list must hold three entries per triangle.", nameof(indices));
                if (indices.Count / 3 > MeshMaxTriangles)
                    throw new ArgumentException("Too many triangles for one collision mesh.", nameof(indices));

                //Weld exact duplicates and drop what cannot collide: bad indices, non-finite points, empty triangles
                List<Vector3> verts = new List<Vector3>();
                Dictionary<Vector3, int> weld = new Dictionary<Vector3, int>();
                int[] remap = new int[positions.Count];
                for (int i = 0; i < positions.Count; i++)
                {
                    Vector3 p = positions[i];
                    if (!IsFinite(p)) { remap[i] = -1; continue; }
                    if (!weld.TryGetValue(p, out int at)) { at = verts.Count; verts.Add(p); weld[p] = at; }
                    remap[i] = at;
                }
                List<int[]> tris = new List<int[]>();
                for (int t = 0; t + 2 < indices.Count; t += 3)
                {
                    int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                    if (a < 0 || b < 0 || c < 0 || a >= remap.Length || b >= remap.Length || c >= remap.Length) continue;
                    a = remap[a]; b = remap[b]; c = remap[c];
                    if (a < 0 || b < 0 || c < 0 || a == b || b == c || a == c) continue;
                    if (Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).LengthSquared() <= 0f) continue;
                    tris.Add(new[] { a, b, c });
                }
                if (tris.Count == 0)
                    throw new ArgumentException("The mesh has no usable triangles.");

                //Tree domain: the shared vertices are quantised against it, so it is fixed first, from the input
                MeshBox domain = MeshBox.Empty;
                foreach (int[] tri in tris) { domain.Add(verts[tri[0]]); domain.Add(verts[tri[1]]); domain.Add(verts[tri[2]]); }

                List<List<int>> groups = Partition(tris, verts);
                if (groups.Count > MeshMaxSections)
                    throw new ArgumentException("Too many sections for one collision mesh.");

                //A vertex two sections use is kept once, in the shared table, so their edges meet exactly
                Dictionary<int, int> sectionsUsing = new Dictionary<int, int>();
                foreach (List<int> group in groups)
                {
                    HashSet<int> seen = new HashSet<int>();
                    foreach (int t in group)
                        for (int k = 0; k < 3; k++)
                            if (seen.Add(tris[t][k])) { sectionsUsing.TryGetValue(tris[t][k], out int n); sectionsUsing[tris[t][k]] = n + 1; }
                }

                BuiltMesh built = new BuiltMesh { DomainMin = domain.Min, DomainMax = domain.Max };
                Dictionary<int, int> sharedSlot = new Dictionary<int, int>();
                List<Vector3> sharedDecoded = new List<Vector3>();
                int lastPrims = 0;
                for (int s = 0; s < groups.Count; s++)
                {
                    List<int> group = groups[s];
                    BuiltSection sec = new BuiltSection
                    {
                        FirstPacked = built.Packed.Count,
                        FirstSharedIndex = built.SharedIndex.Count,
                        FirstPrimitive = built.Primitives.Count,
                        NumPrimitives = group.Count,
                    };

                    //Local vertex table: this section's own vertices first, the shared ones after them
                    List<int> used = new List<int>();
                    HashSet<int> usedSet = new HashSet<int>();
                    foreach (int t in group)
                        for (int k = 0; k < 3; k++)
                            if (usedSet.Add(tris[t][k])) used.Add(tris[t][k]);
                    List<int> packedVerts = new List<int>();
                    List<int> sharedVerts = new List<int>();
                    Dictionary<int, int> local = new Dictionary<int, int>();
                    bool anyOwn = false;
                    foreach (int v in used) if (sectionsUsing[v] <= 1) { anyOwn = true; break; }
                    foreach (int v in used)
                    {
                        //A section made only of shared vertices keeps its first one for itself: readers expect at least one packed
                        bool shared = sectionsUsing[v] > 1 && (anyOwn || v != used[0]);
                        if (shared) { local[v] = -1 - sharedVerts.Count; sharedVerts.Add(v); }
                        else { local[v] = packedVerts.Count; packedVerts.Add(v); }
                    }
                    if (packedVerts.Count + sharedVerts.Count > 256 || packedVerts.Count > 254)
                        throw new InvalidOperationException("A section came out with more vertices than a byte can address.");

                    //Codec: the section's own vertices at 11/11/10 bits over their own bounds
                    MeshBox packedBox = MeshBox.Empty;
                    foreach (int v in packedVerts) packedBox.Add(verts[v]);
                    Vector3 codecBase = packedVerts.Count > 0 ? packedBox.Min : domain.Min;
                    Vector3 extent = packedVerts.Count > 0 ? packedBox.Max - packedBox.Min : Vector3.Zero;
                    Vector3 codecScale = new Vector3(extent.X / 2047f, extent.Y / 2047f, extent.Z / 1023f);
                    sec.CodecBase = codecBase;
                    sec.CodecScale = codecScale;
                    sec.NumPacked = packedVerts.Count;
                    sec.NumShared = sharedVerts.Count;

                    Vector3[] decoded = new Vector3[packedVerts.Count + sharedVerts.Count];
                    for (int i = 0; i < packedVerts.Count; i++)
                    {
                        Vector3 p = verts[packedVerts[i]];
                        uint qx = Quantise(p.X, codecBase.X, codecScale.X, 2047);
                        uint qy = Quantise(p.Y, codecBase.Y, codecScale.Y, 2047);
                        uint qz = Quantise(p.Z, codecBase.Z, codecScale.Z, 1023);
                        built.Packed.Add(qx | (qy << 11) | (qz << 22));
                        //Read back exactly as the reader does
                        decoded[i] = new Vector3(codecBase.X + qx * codecScale.X, codecBase.Y + qy * codecScale.Y, codecBase.Z + qz * codecScale.Z);
                    }
                    for (int i = 0; i < sharedVerts.Count; i++)
                    {
                        int v = sharedVerts[i];
                        if (!sharedSlot.TryGetValue(v, out int slot))
                        {
                            slot = built.Shared.Count;
                            if (slot >= MeshMaxSharedVertices)
                                throw new ArgumentException("Too many vertices shared between sections for one collision mesh (the shared-vertex index is 16-bit); split the mesh.");
                            ulong packed = QuantiseShared(verts[v], domain.Min, domain.Max);
                            built.Shared.Add(packed);
                            sharedDecoded.Add(DecompressSharedVertex21(packed, domain.Min, domain.Max));
                            sharedSlot[v] = slot;
                        }
                        built.SharedIndex.Add((ushort)slot);
                        decoded[packedVerts.Count + i] = sharedDecoded[slot];
                    }

                    //Primitives: triangles as quads whose last corner repeats, which is how Havok stores a triangle
                    List<KeyValuePair<MeshBox, int>> leaves = new List<KeyValuePair<MeshBox, int>>();
                    MeshBox sectionBox = MeshBox.Empty;
                    for (int p = 0; p < group.Count; p++)
                    {
                        int[] tri = tris[group[p]];
                        byte[] prim = new byte[4];
                        MeshBox box = MeshBox.Empty;
                        for (int k = 0; k < 3; k++)
                        {
                            int l = local[tri[k]];
                            int li = l >= 0 ? l : packedVerts.Count + (-1 - l);
                            prim[k] = (byte)li;
                            box.Add(decoded[li]);
                        }
                        prim[3] = prim[2];
                        built.Primitives.Add(prim);
                        leaves.Add(new KeyValuePair<MeshBox, int>(box, p));
                        sectionBox.Add(box);
                    }
                    sectionBox = sectionBox.ClampTo(domain);
                    sec.DomainMin = sectionBox.Min;
                    sec.DomainMax = sectionBox.Max;
                    EmitSectionTree(leaves, sectionBox, sec.Nodes);
                    built.Sections.Add(sec);
                    lastPrims = group.Count;
                }

                //The tree over sections, and where each section's leaf landed in it
                List<KeyValuePair<MeshBox, int>> sectionLeaves = new List<KeyValuePair<MeshBox, int>>();
                for (int s = 0; s < built.Sections.Count; s++)
                    sectionLeaves.Add(new KeyValuePair<MeshBox, int>(new MeshBox { Min = built.Sections[s].DomainMin, Max = built.Sections[s].DomainMax }, s));
                int[] leafIndex = new int[built.Sections.Count];
                EmitTopTree(sectionLeaves, domain, built.TopNodes, leafIndex);
                for (int s = 0; s < built.Sections.Count; s++)
                    built.Sections[s].LeafIndex = leafIndex[s];

                //Keys: (section << 8) | (primitive << 1 | triangle-in-primitive); every primitive here is one triangle
                built.NumPrimitiveKeys = (uint)built.Primitives.Count;
                built.MaxKeyValue = ((uint)(built.Sections.Count - 1) << 8) | ((uint)(lastPrims - 1) << 1);
                built.BitsPerKey = (uint)BitsOf(built.MaxKeyValue + 1);
                return built;
            }

            static bool IsFinite(Vector3 v) =>
                !float.IsNaN(v.X) && !float.IsInfinity(v.X) && !float.IsNaN(v.Y) && !float.IsInfinity(v.Y) && !float.IsNaN(v.Z) && !float.IsInfinity(v.Z);

            static uint Quantise(float value, float origin, float scale, uint max)
            {
                if (!(scale > 0f)) return 0;
                double q = Math.Round((value - origin) / scale);
                if (q < 0) q = 0;
                if (q > max) q = max;
                return (uint)q;
            }

            static ulong QuantiseShared(Vector3 p, Vector3 min, Vector3 max)
            {
                ulong qx = QuantiseAxis(p.X, min.X, max.X), qy = QuantiseAxis(p.Y, min.Y, max.Y), qz = QuantiseAxis(p.Z, min.Z, max.Z);
                return qx | (qy << 21) | (qz << 43);
            }

            static ulong QuantiseAxis(float value, float min, float max)
            {
                float extent = max - min;
                if (!(extent > 0f)) return 0;
                double q = Math.Round((value - min) / extent * SharedMask);
                if (q < 0) q = 0;
                if (q > SharedMask) q = SharedMask;
                return (ulong)q;
            }

            /// <summary>
            /// Cut the triangle list into sections no bigger than retail's, splitting the longest axis of the
            /// centroids at the median until every group fits.
            /// </summary>
            static List<List<int>> Partition(List<int[]> tris, List<Vector3> verts)
            {
                List<List<int>> done = new List<List<int>>();
                Stack<List<int>> work = new Stack<List<int>>();
                List<int> all = new List<int>(tris.Count);
                for (int t = 0; t < tris.Count; t++) all.Add(t);
                work.Push(all);
                while (work.Count > 0)
                {
                    List<int> group = work.Pop();
                    if (group.Count <= MeshSectionMaxPrimitives)
                    {
                        HashSet<int> distinct = new HashSet<int>();
                        MeshBox bounds = MeshBox.Empty;
                        foreach (int t in group)
                            for (int k = 0; k < 3; k++)
                                if (distinct.Add(tris[t][k])) bounds.Add(verts[tris[t][k]]);
                        Vector3 size = bounds.Max - bounds.Min;
                        bool small = group.Count < 2 || Math.Max(size.X, Math.Max(size.Y, size.Z)) <= MeshSectionMaxExtent;
                        if (distinct.Count <= MeshSectionMaxVertices && small) { done.Add(group); continue; }
                    }
                    if (group.Count < 2)
                        throw new InvalidOperationException("A single triangle cannot exceed the section limits.");
                    MeshBox centres = MeshBox.Empty;
                    Vector3[] centre = new Vector3[group.Count];
                    for (int i = 0; i < group.Count; i++)
                    {
                        int[] tri = tris[group[i]];
                        centre[i] = (verts[tri[0]] + verts[tri[1]] + verts[tri[2]]) / 3f;
                        centres.Add(centre[i]);
                    }
                    Vector3 ext = centres.Max - centres.Min;
                    int axis = ext.X >= ext.Y && ext.X >= ext.Z ? 0 : ext.Y >= ext.Z ? 1 : 2;
                    int[] order = new int[group.Count];
                    for (int i = 0; i < order.Length; i++) order[i] = i;
                    Array.Sort(order, (a, b) => Component(centre[a], axis).CompareTo(Component(centre[b], axis)));
                    int mid = group.Count / 2;
                    List<int> left = new List<int>(mid), right = new List<int>(group.Count - mid);
                    for (int i = 0; i < order.Length; i++) (i < mid ? left : right).Add(group[order[i]]);
                    work.Push(right);
                    work.Push(left);
                }
                return done;
            }

            static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

            /// <summary>Codec3Axis4 nodes over a section's primitives, preorder, each box encoded against its parent's decoded box.</summary>
            static void EmitSectionTree(List<KeyValuePair<MeshBox, int>> items, MeshBox parentDecoded, List<byte[]> nodes)
            {
                MeshBox union = MeshBox.Empty;
                foreach (KeyValuePair<MeshBox, int> item in items) union.Add(item.Key);
                union = union.ClampTo(parentDecoded);
                byte qx = EncodeCodec3Axis(parentDecoded.Min.X, parentDecoded.Max.X, union.Min.X, union.Max.X);
                byte qy = EncodeCodec3Axis(parentDecoded.Min.Y, parentDecoded.Max.Y, union.Min.Y, union.Max.Y);
                byte qz = EncodeCodec3Axis(parentDecoded.Min.Z, parentDecoded.Max.Z, union.Min.Z, union.Max.Z);
                DecodeCodec3Axis(parentDecoded.Min, parentDecoded.Max, qx, qy, qz, out Vector3 decodedMin, out Vector3 decodedMax);
                MeshBox decoded = new MeshBox { Min = decodedMin, Max = decodedMax };
                if (items.Count == 1)
                {
                    nodes.Add(new[] { qx, qy, qz, (byte)(items[0].Value << 1) });
                    return;
                }
                Split(items, out List<KeyValuePair<MeshBox, int>> left, out List<KeyValuePair<MeshBox, int>> right);
                nodes.Add(new[] { qx, qy, qz, (byte)((left.Count << 1) | 1) });
                EmitSectionTree(left, decoded, nodes);
                EmitSectionTree(right, decoded, nodes);
            }

            /// <summary>Codec3Axis5 nodes over the sections, preorder; records the node index of each section's leaf.</summary>
            static void EmitTopTree(List<KeyValuePair<MeshBox, int>> items, MeshBox parentDecoded, List<byte[]> nodes, int[] leafIndex)
            {
                MeshBox union = MeshBox.Empty;
                foreach (KeyValuePair<MeshBox, int> item in items) union.Add(item.Key);
                union = union.ClampTo(parentDecoded);
                byte qx = EncodeCodec3Axis(parentDecoded.Min.X, parentDecoded.Max.X, union.Min.X, union.Max.X);
                byte qy = EncodeCodec3Axis(parentDecoded.Min.Y, parentDecoded.Max.Y, union.Min.Y, union.Max.Y);
                byte qz = EncodeCodec3Axis(parentDecoded.Min.Z, parentDecoded.Max.Z, union.Min.Z, union.Max.Z);
                DecodeCodec3Axis(parentDecoded.Min, parentDecoded.Max, qx, qy, qz, out Vector3 decodedMin, out Vector3 decodedMax);
                MeshBox decoded = new MeshBox { Min = decodedMin, Max = decodedMax };
                if (items.Count == 1)
                {
                    int data = items[0].Value;
                    leafIndex[data] = nodes.Count;
                    nodes.Add(new[] { qx, qy, qz, (byte)((data >> 8) & 0x7F), (byte)(data & 0xFF) });
                    return;
                }
                Split(items, out List<KeyValuePair<MeshBox, int>> left, out List<KeyValuePair<MeshBox, int>> right);
                nodes.Add(new[] { qx, qy, qz, (byte)(0x80 | ((left.Count >> 8) & 0x7F)), (byte)(left.Count & 0xFF) });
                EmitTopTree(left, decoded, nodes, leafIndex);
                EmitTopTree(right, decoded, nodes, leafIndex);
            }

            /// <summary>Halve a list of boxes on the longest axis of their centres.</summary>
            static void Split(List<KeyValuePair<MeshBox, int>> items, out List<KeyValuePair<MeshBox, int>> left, out List<KeyValuePair<MeshBox, int>> right)
            {
                MeshBox centres = MeshBox.Empty;
                foreach (KeyValuePair<MeshBox, int> item in items) centres.Add(item.Key.Centre);
                Vector3 ext = centres.Max - centres.Min;
                int axis = ext.X >= ext.Y && ext.X >= ext.Z ? 0 : ext.Y >= ext.Z ? 1 : 2;
                List<KeyValuePair<MeshBox, int>> sorted = new List<KeyValuePair<MeshBox, int>>(items);
                sorted.Sort((a, b) => Component(a.Key.Centre, axis).CompareTo(Component(b.Key.Centre, axis)));
                int mid = sorted.Count / 2;
                left = sorted.GetRange(0, mid);
                right = sorted.GetRange(mid, sorted.Count - mid);
            }
        }
    }
}
