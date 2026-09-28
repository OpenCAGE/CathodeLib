using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CATHODE;
using static CATHODE.HavokPackfile;

namespace CathodeLib.Havok
{
    /* Writing a NEW collision proxy - an hkpStaticCompoundShape over a freshly encoded hkpBvCompressedMeshShape -
       into a 2012 packfile (COLLISION.HKX / COLLISION.HKX64), or the 2018 tagfile the mobile and Switch builds
       ship instead (see the tagfile section below), from a plain triangle list.

       What retail ships, measured over every PC level (59,024 proxies): a proxy is a compound whose instances are
       all hkpBvCompressedMeshShape children (never boxes, never nested compounds), 79% of them with one child at
       the identity, and the compound, its instance and the mesh all carry the physics material's write index as
       their userData. The mesh is an hkcdStaticMeshTree<..., 11, 21>: a Codec3Axis5 tree over sections, each
       section a Codec3Axis4 tree over at most 127 primitives, its own vertices packed to 11/11/10 bits against a
       per-section codec and the vertices it shares with other sections kept once, 21 bits per axis against the
       whole mesh. Every field below was read off those files and the reading was checked with a walk of all
       82,636 retail mesh trees (each walk reaching every leaf once, each section's leaf naming the tree node
       that holds it). See the harness modes bvdump / meshtree in ReleaseSweep. */
    /// <summary>
    /// Appending new collision proxies to a <see cref="HavokPackfile"/> (extension methods), and the checkpoint
    /// every append - this one and <see cref="PhysicsSystemWriter.AddConvexPhysicsSystem"/> - is undone with.
    /// </summary>
    public static class CollisionProxyWriter
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

        public static AppendCheckpoint CreateCheckpoint(this HavokPackfile packfile)
        {
            return new AppendCheckpoint
            {
                Payload = (byte[])packfile.DataPayload.Clone(),
                Classnames = (byte[])packfile.ClassnamesData.Clone(),
                Local = new List<LocalFixup>(packfile.LocalFixups),
                Global = new List<GlobalFixup>(packfile.GlobalFixups),
                Virtual = new List<VirtualFixup>(packfile.VirtualFixups),
                Objects = new List<PackfileObject>(packfile.Objects),
                //A tagfile edit that moves an array moves its object entry with it, in place
                ObjectOffsets = packfile.Objects.Select(o => o.DataOffset).ToArray(),
                Compounds = new List<StaticCompoundShape>(packfile.StaticCompoundShapes),
                Physics = new List<PhysicsSystem>(packfile.PhysicsSystems),
                //A tagfile keeps its items and pointer lists outside the payload, and an append moves some in place
                Tags = packfile.Tagfile?.Snapshot(),
            };
        }

        public static void RestoreCheckpoint(this HavokPackfile packfile, AppendCheckpoint checkpoint)
        {
            if (checkpoint == null)
                throw new ArgumentNullException(nameof(checkpoint));
            //Copies, not the checkpoint's own storage: the next append writes into these in place, and the
            //checkpoint has to be able to put the same state back again
            packfile.DataPayload = (byte[])checkpoint.Payload.Clone();
            packfile.ClassnamesData = (byte[])checkpoint.Classnames.Clone();
            packfile.LocalFixups = new List<LocalFixup>(checkpoint.Local);
            packfile.GlobalFixups = new List<GlobalFixup>(checkpoint.Global);
            packfile.VirtualFixups = new List<VirtualFixup>(checkpoint.Virtual);
            packfile.Objects = new List<PackfileObject>(checkpoint.Objects);
            if (checkpoint.ObjectOffsets != null)
                for (int i = 0; i < checkpoint.ObjectOffsets.Length && i < packfile.Objects.Count; i++)
                    packfile.Objects[i].DataOffset = checkpoint.ObjectOffsets[i];
            //The views are put back rather than re-parsed: an append only adds compounds, and re-parsing would hand
            //out new objects for every compound, leaving the references others hold (a COLLISION.MAP row's
            //CollisionProxy, a picker's selection) pointing at copies no longer in the list
            packfile.StaticCompoundShapes = new List<StaticCompoundShape>(checkpoint.Compounds);
            packfile.PhysicsSystems = new List<PhysicsSystem>(checkpoint.Physics);
            if (checkpoint.Tags != null)
                packfile.Tagfile?.Restore(checkpoint.Tags);
        }

        /// <summary>
        /// Append a new collision proxy built from a triangle mesh: a one-instance hkpStaticCompoundShape over a
        /// new hkpBvCompressedMeshShape, registered in the proxy list so it has an ordinal a COLLISION.MAP row can
        /// name. The mesh is in the proxy's own space, in metres, with the winding the game's own meshes decode to
        /// (the space <see cref="HavokPackfile.BuildPreviewMesh(StaticCompoundShape)"/> gives back for a template compound).
        /// </summary>
        /// <param name="positions">Vertex positions.</param>
        /// <param name="triangles">Three indices per triangle into <paramref name="positions"/>.</param>
        /// <param name="userData">What retail stores here is the write index of the row's physics material.</param>
        /// <param name="filterInfo">The collision type of the template instance; retail uses 9 (BALLISTICS) for prop materials and 3 (STANDARD) for world collision.</param>
        /// <returns>The new compound, last in <see cref="HavokPackfile.StaticCompoundShapes"/>, with its <c>ProxyIndex</c> assigned.</returns>
        /// <remarks>
        /// Both packfiles of a level (32 and 64-bit) need the proxy at the same ordinal; call this on each with the
        /// same mesh and compare the results, restoring a <see cref="CreateCheckpoint"/> on either side if they
        /// disagree. The mobile/Switch builds ship only the 64-bit file, as a 2018 tagfile; the same call writes
        /// that, laid out from the file's own type table, and its ordinal is the proxy-list slot it is given.
        /// </remarks>
        public static StaticCompoundShape AddMeshCollisionProxy(this HavokPackfile packfile, IList<Vector3> positions, IList<int> triangles, uint userData = 0, uint filterInfo = 9)
        {
            if (positions == null || triangles == null)
                throw new ArgumentNullException(positions == null ? nameof(positions) : nameof(triangles));
            if (packfile.Tagfile != null)
                return AddMeshCollisionProxyTagfile(packfile, positions, triangles, userData, filterInfo);

            BuiltMesh built = MeshProxyBuilder.Build(positions, triangles);
            uint meshOffset = AppendMeshShapeObject(packfile, built, userData);
            uint compoundOffset = AppendCompoundShell(packfile, userData, Math.Max(1, BitsOf(built.MaxKeyValue)));

            //The new compound is last in the payload and in the object list, so its ordinal is the number of compounds
            //before it - the same rank a reload assigns. Only its own view is made: re-parsing them all would replace
            //every compound object, and the rows and pickers holding the old ones would be left with stale copies.
            PackfileObject compoundObject = packfile.Objects[packfile.Objects.Count - 1];
            int ordinal = 0;
            for (int i = 0; i < packfile.Objects.Count - 1; i++)
                if (packfile.Objects[i].Class == ObjectClass.StaticCompoundShape) ordinal++;
            compoundObject.ProxyIndex = ordinal;
            StaticCompoundShape compound = new StaticCompoundShape { ProxyIndex = ordinal, DataOffset = compoundOffset };
            packfile.StaticCompoundShapes.Add(compound);

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
            packfile.RewriteCompoundArrays(compound);
            packfile.EnsureProxyListCoversCompounds();
            packfile.RefreshCompoundShapeKeyBits();

            //The reader is the first oracle: what went in must come back out, triangle for triangle
            PreviewMesh check = packfile.BuildBakeMesh(compound);
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
        static uint AppendMeshShapeObject(HavokPackfile packfile, BuiltMesh built, uint userData)
        {
            PackfileObject template = null;
            for (int i = 0; i < packfile.Objects.Count; i++)
                if (packfile.Objects[i].Class == ObjectClass.BvCompressedMeshShape) { template = packfile.Objects[i]; break; }
            if (template == null)
                throw new InvalidOperationException("This packfile holds no hkpBvCompressedMeshShape to model the new one on.");

            int ptr = packfile.Header.PointerSize;
            int arraySize = ptr + 8;
            int headerLen = ptr == 8 ? 0x70 : 0x50;
            int treeLen = ptr == 8 ? 160 : 144;
            int objectSize = headerLen + treeLen;

            int sectionCount = built.Sections.Count;
            int dst = AlignPayload(packfile.DataPayload.Length, 16);
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
            Buffer.BlockCopy(packfile.DataPayload, 0, grown, 0, packfile.DataPayload.Length);
            packfile.DataPayload = grown;
            byte[] d = packfile.DataPayload;

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

            packfile.LocalFixups.Add(new LocalFixup { Src = (uint)tree, Dst = (uint)topNodesOff });
            packfile.LocalFixups.Add(new LocalFixup { Src = (uint)arrays, Dst = (uint)sectionsOff });
            packfile.LocalFixups.Add(new LocalFixup { Src = (uint)(arrays + arraySize), Dst = (uint)primitivesOff });
            if (built.SharedIndex.Count > 0)
                packfile.LocalFixups.Add(new LocalFixup { Src = (uint)(arrays + 2 * arraySize), Dst = (uint)sharedIdxOff });
            packfile.LocalFixups.Add(new LocalFixup { Src = (uint)(arrays + 3 * arraySize), Dst = (uint)packedOff });
            if (built.Shared.Count > 0)
                packfile.LocalFixups.Add(new LocalFixup { Src = (uint)(arrays + 4 * arraySize), Dst = (uint)sharedOff });
            packfile.LocalFixups.Add(new LocalFixup { Src = (uint)(arrays + 5 * arraySize), Dst = (uint)runsOff });

            //Nodes over sections: 5 bytes each
            for (int n = 0; n < built.TopNodes.Count; n++)
                Buffer.BlockCopy(built.TopNodes[n], 0, d, topNodesOff + n * 5, 5);

            //Sections, each with its own node array
            for (int s = 0; s < sectionCount; s++)
            {
                BuiltSection sec = built.Sections[s];
                int rec = sectionsOff + s * 96;
                WriteArrayHeader(d, rec, ptr, sec.Nodes.Count);
                packfile.LocalFixups.Add(new LocalFixup { Src = (uint)rec, Dst = (uint)sectionNodesOff[s] });
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
            packfile.VirtualFixups.Add(new VirtualFixup { Src = (uint)dst, SectionIndex = 0, NameOffset = nameOffset });
            packfile.Objects.Add(new PackfileObject
            {
                DataOffset = (uint)dst,
                ClassNameOffset = nameOffset,
                ClassName = "hkpBvCompressedMeshShape",
                Class = ObjectClass.BvCompressedMeshShape,
                ProxyIndex = -1,
            });
            return (uint)dst;
        }

        internal static void WriteArrayHeader(byte[] d, int field, int ptr, int count)
        {
            Array.Clear(d, field, ptr);
            WriteUInt32(d, field + ptr, (uint)count);
            WriteUInt32(d, field + ptr + 4, (uint)count | 0x80000000u);
        }

        // ------------------------------------------------------------------ the compound shell

        /// <summary>
        /// hkpStaticCompoundShape with no instances yet: a retail one-mesh template's bytes with the arrays and
        /// domain cleared, the key width set and the two array fields given local fixups, so that
        /// <see cref="HavokPackfile.RewriteCompoundArrays"/> can find and fill them once the instance is added.
        /// </summary>
        static uint AppendCompoundShell(HavokPackfile packfile, uint userData, int numBitsForChildShapeKey)
        {
            StaticCompoundShape template = null;
            PackfileObject templateObject = null;
            StaticCompoundShape primary = packfile.WorldHostPrimary, secondary = packfile.WorldHostSecondary;
            for (int i = 0; i < packfile.StaticCompoundShapes.Count && template == null; i++)
            {
                StaticCompoundShape c = packfile.StaticCompoundShapes[i];
                if (c == primary || c == secondary || c.Instances.Count != 1)
                    continue;
                if (!string.Equals(c.Instances[0].ShapeClassName, "hkpBvCompressedMeshShape", StringComparison.Ordinal))
                    continue;
                for (int o = 0; o < packfile.Objects.Count; o++)
                    if (packfile.Objects[o].DataOffset == c.DataOffset && packfile.Objects[o].Class == ObjectClass.StaticCompoundShape) { templateObject = packfile.Objects[o]; break; }
                if (templateObject != null)
                    template = c;
            }
            if (template == null)
                throw new InvalidOperationException("This packfile holds no one-mesh template compound to model the new one on.");

            //The two array fields are the object's two lowest local fixups (instances, then tree nodes)
            uint first = uint.MaxValue, second = uint.MaxValue;
            for (int i = 0; i < packfile.LocalFixups.Count; i++)
            {
                uint src = packfile.LocalFixups[i].Src;
                if (src < template.DataOffset || src >= template.DataOffset + 0x100) continue;
                if (src < first) { second = first; first = src; }
                else if (src < second) second = src;
            }
            if (second == uint.MaxValue)
                throw new InvalidOperationException("The template compound does not carry its two array fields.");

            int ptr = packfile.Header.PointerSize;
            int arraySize = ptr + 8;
            int instancesRel = (int)(first - template.DataOffset);
            int nodesRel = (int)(second - template.DataOffset);
            int domainRel = AlignPayload(nodesRel + arraySize, 16);
            int objectSize = domainRel + 32;

            int dst = AlignPayload(packfile.DataPayload.Length, 16);
            byte[] grown = new byte[dst + objectSize];
            Buffer.BlockCopy(packfile.DataPayload, 0, grown, 0, packfile.DataPayload.Length);
            packfile.DataPayload = grown;
            byte[] d = packfile.DataPayload;
            Buffer.BlockCopy(d, (int)template.DataOffset, d, dst, objectSize);

            WriteArrayHeader(d, dst + instancesRel, ptr, 0);
            WriteArrayHeader(d, dst + nodesRel, ptr, 0);
            Array.Clear(d, dst + domainRel, 32);
            int userDataAt = dst + (ptr == 8 ? 24 : 12);
            WriteUInt32(d, userDataAt, userData);
            if (ptr == 8) WriteUInt32(d, userDataAt + 4, 0);
            d[dst + (ptr == 8 ? 0x30 : 0x18)] = (byte)numBitsForChildShapeKey;

            //Provisional targets: the rewrite appends the real arrays and repoints these
            packfile.LocalFixups.Add(new LocalFixup { Src = (uint)(dst + instancesRel), Dst = (uint)(dst + objectSize) });
            packfile.LocalFixups.Add(new LocalFixup { Src = (uint)(dst + nodesRel), Dst = (uint)(dst + objectSize) });

            packfile.VirtualFixups.Add(new VirtualFixup { Src = (uint)dst, SectionIndex = 0, NameOffset = templateObject.ClassNameOffset });
            packfile.Objects.Add(new PackfileObject
            {
                DataOffset = (uint)dst,
                ClassNameOffset = templateObject.ClassNameOffset,
                ClassName = "hkpStaticCompoundShape",
                Class = ObjectClass.StaticCompoundShape,
                ProxyIndex = -1,
            });
            return (uint)dst;
        }

        // ------------------------------------------------------------------ the 2018 tagfile (iOS, Switch)

        /* The mobile and Switch builds ship COLLISION.HKX64 as a Havok 2018 tagfile. The mesh itself did not
           change: every iOS template mesh compared with the PC file at the same ordinal has byte-identical packed
           and shared vertices, nodes, codec parameters, domains and runs (6,101 meshes; the only differences are
           11 degenerate quads the 2018 export rewrote), so MeshProxyBuilder serves both. Everything around the
           bytes did:
             - an object only exists if an ITEM names it, and a pointer is that item's index, listed in PTCH under
               the member's declared type - there are no fixups;
             - an array's length is on its item; m_size and capacity stay 0, and an empty array is a null word with
               no item and no PTCH entry;
             - the Section record was repacked (four u32 first-indices, count bytes, a u16 leafIndex at 92) and
               lost its shared-vertex count, which is now the gap to the next section's first shared index;
             - the tree gained primitiveStoresIsFlatConvex, 0 in every retail mesh;
             - the ordinal a COLLISION.MAP row stores is the proxy's slot in the hkpListShape, not its rank in the
               file (a tagfile puts the three world hosts first).
           Type indices are per file - frontend has no box shape and numbers the mesh types three lower - so every
           offset, size, ITEM word and PTCH group is read off the file being written, never carried over.

           One trap shapes the code: HavokTagfile.CloneObject copies the template's pointer words, item indices
           and all, and SetArray on such a word MOVES the template's own array. So the mesh and compound get fresh
           items of their own and every array they own is a NewArray; nothing here is cloned.

           Checked by re-encoding every iOS template proxy through this path (4,451 over three levels, and four
           Switch levels): each reads back to its own triangles within quantisation (worst 3.6 mm), every retail
           item, byte and PTCH entry but the growing proxy list's is left as it was, and the proxy matches the one
           it re-encodes field for field but for the key width, which follows its own sections. */

        /// <summary>
        /// The tagfile half of <see cref="AddMeshCollisionProxy"/>: the same mesh and compound, laid out as 2018
        /// says, given items and PTCH entries, and registered in the proxy list - whose slot is the ordinal.
        /// </summary>
        static StaticCompoundShape AddMeshCollisionProxyTagfile(HavokPackfile packfile, IList<Vector3> positions, IList<int> triangles, uint userData, uint filterInfo)
        {
            HavokTagfile tags = packfile.Tagfile;
            TagProxyLayout layout = new TagProxyLayout(tags);

            //A proxy the list cannot hold has no ordinal a row could name, so refuse before writing anything
            int list = tags.ProxyListOffset();
            if (list < 0 || !tags.TryResolvePointer((uint)(list + layout.ListChildren), out _, out int listed))
                throw new InvalidOperationException("This collision file has no proxy list to register a new collision mesh in.");

            BuiltMesh built = MeshProxyBuilder.Build(positions, triangles);
            int firstNewItem = tags.ItemCount;
            uint meshOffset = AppendMeshShapeObjectTagfile(packfile, layout, built, userData);
            uint compoundOffset = AppendCompoundShellTagfile(packfile, layout, userData, Math.Max(1, BitsOf(built.MaxKeyValue)));

            //The views see the new objects without re-reading the file: a re-read would hand out new objects
            //for every compound and system, stranding whatever holds the old ones (rows, pickers, the importer)
            tags.RegisterNewItems(packfile, firstNewItem);

            /* The ordinal is the list slot it is about to take: past the templates and past the three world
             * hosts, which are unlisted in retail and fill the slots before it - the same padding the packfile
             * branch gives the PC list. Once they are in, the next proxy follows straight on. */
            int ordinal = listed;
            for (int i = 0; i < packfile.StaticCompoundShapes.Count; i++)
                ordinal = Math.Max(ordinal, packfile.StaticCompoundShapes[i].ProxyIndex + 1);
            StaticCompoundShape compound = new StaticCompoundShape { ProxyIndex = ordinal, DataOffset = compoundOffset };
            packfile.StaticCompoundShapes.Add(compound);

            compound.DomainMin = new Vector4(float.MaxValue, float.MaxValue, float.MaxValue, 0f);
            compound.DomainMax = new Vector4(float.MinValue, float.MinValue, float.MinValue, 0f);
            compound.AddInstance(new CompoundInstance
            {
                Translation = new Vector4(0f, 0f, 0f, 0.5f),  //W = 0x3F000000, no flag bits: all 6,101 iOS template instances
                Rotation = Quaternion.Identity,
                Scale = new Vector4(1f, 1f, 1f, 0.5f),
                FilterInfo = filterInfo,
                ChildFilterInfoMask = 0,
                UserData = userData,
                ShapeDataOffset = meshOffset,
                ShapeClassName = "hkpBvCompressedMeshShape",
            });

            //The shell's two array words are null, so the rewrite gives them items of their own - it cannot
            //reach the template's, which is why the shell was not cloned
            int rewriteItems = tags.ItemCount;
            packfile.RewriteCompoundArrays(compound);
            FitCompoundDomainToMesh(packfile, compound, built);
            packfile.EnsureProxyListCoversCompounds(refreshTagfileFixups: false);   //re-read once, just below
            packfile.RefreshCompoundShapeKeyBits();
            tags.RegisterNewItems(packfile, rewriteItems);

            if (!ListSlotHolds(tags, layout, list, ordinal, compoundOffset))
                throw new InvalidOperationException("The new collision mesh did not land in proxy list slot " + ordinal + ", so no COLLISION.MAP row could name it.");

            //The reader is the first oracle: what went in must come back out, triangle for triangle
            PreviewMesh check = packfile.BuildBakeMesh(compound);
            if (check.TriangleCount != built.PrimitiveCount)
                throw new InvalidOperationException("The new collision mesh reads back with " + check.TriangleCount + " triangles where " + built.PrimitiveCount + " were written.");
            return compound;
        }

        /// <summary>
        /// hkpBvCompressedMeshShape in a tagfile: a retail mesh's header, the tree in the 2018 layout, and every
        /// array a fresh item appended in retail's data order - top nodes, sections, primitives, shared index,
        /// packed vertices, shared vertices, runs, then each section's nodes.
        /// </summary>
        static uint AppendMeshShapeObjectTagfile(HavokPackfile packfile, TagProxyLayout layout, BuiltMesh built, uint userData)
        {
            HavokTagfile tags = packfile.Tagfile;
            PackfileObject template = null;
            for (int i = 0; i < packfile.Objects.Count; i++)
                if (packfile.Objects[i].Class == ObjectClass.BvCompressedMeshShape) { template = packfile.Objects[i]; break; }
            uint word = template == null ? 0 : ItemWordAt(tags, template.DataOffset);
            if (word == 0)
                throw new InvalidOperationException("This collision file holds no hkpBvCompressedMeshShape to model the new one on.");

            int sectionCount = built.Sections.Count;
            int dst = AlignPayload(packfile.DataPayload.Length, 16);
            int topNodesOff = AlignPayload(dst + layout.MeshSize, 16);
            int sectionsOff = AlignPayload(topNodesOff + built.TopNodes.Count * TagProxyLayout.TopNodeSize, 16);
            int primitivesOff = AlignPayload(sectionsOff + sectionCount * layout.SectionSize, 16);
            int sharedIdxOff = AlignPayload(primitivesOff + built.Primitives.Count * TagProxyLayout.PrimitiveSize, 16);
            int packedOff = AlignPayload(sharedIdxOff + built.SharedIndex.Count * 2, 16);
            int sharedOff = AlignPayload(packedOff + built.Packed.Count * 4, 16);
            int runsOff = AlignPayload(sharedOff + built.Shared.Count * 8, 16);
            int cursor = AlignPayload(runsOff + sectionCount * layout.RunSize, 16);
            int[] sectionNodesOff = new int[sectionCount];
            for (int s = 0; s < sectionCount; s++)
            {
                sectionNodesOff[s] = cursor;
                cursor = AlignPayload(cursor + built.Sections[s].Nodes.Count * TagProxyLayout.SectionNodeSize, 16);
            }

            byte[] grown = new byte[cursor];
            Buffer.BlockCopy(packfile.DataPayload, 0, grown, 0, packfile.DataPayload.Length);
            packfile.DataPayload = grown;
            byte[] d = packfile.DataPayload;

            //Header: the retail shape's bytes up to its tree (dispatch type, bvTreeType 3, welding 6), with the
            //property bag and all three palettes null - as in every retail mesh, and the copy must not share them
            Buffer.BlockCopy(d, (int)template.DataOffset, d, dst, layout.MeshTree);
            if (layout.MeshPropertyBag >= 0)
                Array.Clear(d, dst + layout.MeshPropertyBag, 8);
            Array.Clear(d, dst + layout.MeshPalettes, layout.MeshTree - layout.MeshPalettes);
            WriteUInt32(d, dst + layout.MeshUserData, userData);
            WriteUInt32(d, dst + layout.MeshUserData + 4, 0);
            tags.AddItem(word, dst, 1);

            //The tree. primitiveStoresIsFlatConvex stays 0, and so does every m_size and capacity
            int tree = dst + layout.MeshTree;
            Array.Clear(d, tree, layout.TreeSize);
            WriteVector4(d, tree + layout.TreeDomain, new Vector4(built.DomainMin, 0f));
            WriteVector4(d, tree + layout.TreeDomain + 16, new Vector4(built.DomainMax, 0f));
            WriteUInt32(d, tree + layout.TreeNumPrimitiveKeys, built.NumPrimitiveKeys);
            WriteUInt32(d, tree + layout.TreeBitsPerKey, built.BitsPerKey);
            WriteUInt32(d, tree + layout.TreeMaxKeyValue, built.MaxKeyValue);
            NewArray(tags, tree, layout.TreeNodes, topNodesOff, built.TopNodes.Count);
            NewArray(tags, tree, layout.TreeSections, sectionsOff, sectionCount);
            NewArray(tags, tree, layout.TreePrimitives, primitivesOff, built.Primitives.Count);
            NewArray(tags, tree, layout.TreeSharedIndex, sharedIdxOff, built.SharedIndex.Count);
            NewArray(tags, tree, layout.TreePacked, packedOff, built.Packed.Count);
            NewArray(tags, tree, layout.TreeShared, sharedOff, built.Shared.Count);
            NewArray(tags, tree, layout.TreeRuns, runsOff, sectionCount);

            for (int n = 0; n < built.TopNodes.Count; n++)
                Buffer.BlockCopy(built.TopNodes[n], 0, d, topNodesOff + n * TagProxyLayout.TopNodeSize, TagProxyLayout.TopNodeSize);

            //Sections, 2018 layout: no shared count is written - the reader takes the gap to the next section's
            //first shared index (the last one's, to the end of the index array), which is exactly NumShared here
            for (int s = 0; s < sectionCount; s++)
            {
                BuiltSection sec = built.Sections[s];
                int rec = sectionsOff + s * layout.SectionSize;
                WriteVector4(d, rec + layout.SectionDomain, new Vector4(sec.DomainMin, 0f));
                WriteVector4(d, rec + layout.SectionDomain + 16, new Vector4(sec.DomainMax, 0f));
                int codec = rec + layout.SectionCodecParms;
                WriteSingle(d, codec, sec.CodecBase.X);
                WriteSingle(d, codec + 4, sec.CodecBase.Y);
                WriteSingle(d, codec + 8, sec.CodecBase.Z);
                WriteSingle(d, codec + 12, sec.CodecScale.X);
                WriteSingle(d, codec + 16, sec.CodecScale.Y);
                WriteSingle(d, codec + 20, sec.CodecScale.Z);
                WriteUInt32(d, rec + layout.SectionFirstPacked, (uint)sec.FirstPacked);
                WriteUInt32(d, rec + layout.SectionFirstShared, (uint)sec.FirstSharedIndex);
                WriteUInt32(d, rec + layout.SectionFirstPrimitive, (uint)sec.FirstPrimitive);
                WriteUInt32(d, rec + layout.SectionFirstRun, (uint)s);   //one data run per section
                d[rec + layout.SectionNumPacked] = (byte)sec.NumPacked;
                d[rec + layout.SectionNumPrimitives] = (byte)sec.NumPrimitives;
                d[rec + layout.SectionNumRuns] = 1;
                d[rec + layout.SectionLeafIndex] = (byte)(sec.LeafIndex & 0xFF);
                d[rec + layout.SectionLeafIndex + 1] = (byte)(sec.LeafIndex >> 8);
                //page, layerData, flags: 0, as all 7,107 retail sections
                NewArray(tags, rec, layout.SectionNodes, sectionNodesOff[s], sec.Nodes.Count);
                for (int n = 0; n < sec.Nodes.Count; n++)
                    Buffer.BlockCopy(sec.Nodes[n], 0, d, sectionNodesOff[s] + n * TagProxyLayout.SectionNodeSize, TagProxyLayout.SectionNodeSize);
            }

            for (int p = 0; p < built.Primitives.Count; p++)
                Buffer.BlockCopy(built.Primitives[p], 0, d, primitivesOff + p * TagProxyLayout.PrimitiveSize, TagProxyLayout.PrimitiveSize);
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
                d[runsOff + s * layout.RunSize + layout.RunCount] = (byte)built.Sections[s].NumPrimitives;   //value and index 0
            return (uint)dst;
        }

        /// <summary>
        /// hkpStaticCompoundShape in a tagfile, with no instances yet: a retail one-mesh template's header with
        /// everything from its instance array on cleared (instances, extra infos, the disabled-key table and the
        /// tree - null or zero in all 4,451 iOS templates), its own userData and key width, and its own item.
        /// </summary>
        static uint AppendCompoundShellTagfile(HavokPackfile packfile, TagProxyLayout layout, uint userData, int numBitsForChildShapeKey)
        {
            HavokTagfile tags = packfile.Tagfile;
            StaticCompoundShape template = null;
            StaticCompoundShape primary = packfile.WorldHostPrimary, secondary = packfile.WorldHostSecondary;
            uint word = 0;
            for (int i = 0; i < packfile.StaticCompoundShapes.Count && template == null; i++)
            {
                StaticCompoundShape c = packfile.StaticCompoundShapes[i];
                if (c == primary || c == secondary || c.Instances.Count != 1)
                    continue;
                if (!string.Equals(c.Instances[0].ShapeClassName, "hkpBvCompressedMeshShape", StringComparison.Ordinal))
                    continue;
                word = ItemWordAt(tags, c.DataOffset);
                if (word != 0)
                    template = c;
            }
            if (template == null)
                throw new InvalidOperationException("This collision file holds no one-mesh template compound to model the new one on.");

            int dst = AlignPayload(packfile.DataPayload.Length, 16);
            byte[] grown = new byte[dst + layout.CompoundSize];
            Buffer.BlockCopy(packfile.DataPayload, 0, grown, 0, packfile.DataPayload.Length);
            packfile.DataPayload = grown;
            byte[] d = packfile.DataPayload;
            Buffer.BlockCopy(d, (int)template.DataOffset, d, dst, layout.CompoundSize);

            if (layout.CompoundPropertyBag >= 0)
                Array.Clear(d, dst + layout.CompoundPropertyBag, 8);
            Array.Clear(d, dst + layout.CompoundArrays, layout.CompoundSize - layout.CompoundArrays);
            WriteUInt32(d, dst + layout.CompoundUserData, userData);
            WriteUInt32(d, dst + layout.CompoundUserData + 4, 0);
            d[dst + layout.CompoundBits] = (byte)numBitsForChildShapeKey;
            tags.AddItem(word, dst, 1);
            return (uint)dst;
        }

        /// <summary>
        /// Retail's one-mesh proxy: the compound's domain is its mesh's, and its single tree node spans all of
        /// it (xyz 00, FF on a flat axis) - 4,436 of 4,451 iOS templates within 1e-6 m, the rest by float ULPs.
        /// The rewrite pads the domain by a centimetre, which suits a tree over placed instances; a template sits
        /// at the identity, where the pad is only a difference from what the game ships.
        /// </summary>
        static void FitCompoundDomainToMesh(HavokPackfile packfile, StaticCompoundShape compound, BuiltMesh built)
        {
            HavokTagfile.CompoundLayout layout = packfile.Tagfile.Compound();
            if (!packfile.Tagfile.TryResolvePointer(compound.DataOffset + (uint)layout.Nodes, out uint root, out int nodeCount) || nodeCount != 1)
                return;

            Vector4 min = new Vector4(built.DomainMin, 0f), max = new Vector4(built.DomainMax, 0f);
            compound.DomainMin = min;
            compound.DomainMax = max;
            byte[] d = packfile.DataPayload;
            WriteVector4(d, (int)compound.DataOffset + layout.Domain, min);
            WriteVector4(d, (int)compound.DataOffset + layout.Domain + 16, max);
            d[root] = EncodeCodec3Axis(min.X, max.X, min.X, max.X);
            d[root + 1] = EncodeCodec3Axis(min.Y, max.Y, min.Y, max.Y);
            d[root + 2] = EncodeCodec3Axis(min.Z, max.Z, min.Z, max.Z);
        }

        /// <summary>Whether proxy list slot <paramref name="slot"/> names the object at <paramref name="compound"/>.</summary>
        static bool ListSlotHolds(HavokTagfile tags, TagProxyLayout layout, int list, int slot, uint compound)
        {
            if (!tags.TryResolvePointer((uint)(list + layout.ListChildren), out uint children, out int count) || slot < 0 || slot >= count)
                return false;
            return tags.TryResolvePointer(children + (uint)(slot * layout.ListChildSize + layout.ListChildShape), out uint named, out _) && named == compound;
        }

        /// <summary>The ITEM word of the object at an offset, exactly as the file spells it; 0 if no item claims it.</summary>
        static uint ItemWordAt(HavokTagfile tags, uint offset)
        {
            int index = tags.IndexOfObjectAt((int)offset);
            List<HavokTagfile.Item> items = tags.Items();
            return index > 0 && index < items.Count ? items[index].Word : 0;
        }

        static void NewArray(HavokTagfile tags, int owner, TagArrayMember member, int elements, int count)
        {
            if (!tags.NewArray(owner + member.Offset, elements, count, member.Word, member.Group))
                throw new InvalidOperationException("Could not give the new collision mesh its " + member.Name + " array.");
        }

        /// <summary>An hkArray member: where it sits, the ITEM word its elements take, and the PTCH group its word is listed under.</summary>
        struct TagArrayMember
        {
            public string Name;
            public int Offset;
            public uint Word;
            public int Group;

            public static TagArrayMember Of(HavokTagfile tags, string type, string member)
            {
                TagArrayMember found = new TagArrayMember
                {
                    Name = member,
                    Offset = tags.OffsetOf(type, member),
                    Word = tags.ArrayWord(type, member),
                    Group = tags.PatchGroupOf(type, member),
                };
                if (found.Offset < 0 || found.Word == 0 || found.Group <= 0)
                    throw new NotSupportedException("This collision file's " + type + " has no " + member + " array the writer recognises.");
                return found;
            }
        }

        /// <summary>
        /// Everything the tagfile writer places, read off the file's own type table. A file lacking any of it
        /// is refused rather than written from a guess.
        /// </summary>
        sealed class TagProxyLayout
        {
            const string Mesh = "hkpBvCompressedMeshShape";
            const string Tree = "hkpBvCompressedMeshShapeTree";
            const string Section = "hkcdStaticMeshTree::Section";
            const string Run = "hkpBvCompressedMeshShapeTree::PrimitiveDataRun";
            const string Compound = "hkpStaticCompoundShape";

            /// <summary>What MeshProxyBuilder emits per element; checked against the file's own codec and primitive sizes.</summary>
            public const int TopNodeSize = 5, SectionNodeSize = 4, PrimitiveSize = 4;

            public readonly int MeshSize, MeshTree, MeshUserData, MeshPropertyBag, MeshPalettes;
            public readonly int TreeSize, TreeDomain, TreeNumPrimitiveKeys, TreeBitsPerKey, TreeMaxKeyValue;
            public readonly TagArrayMember TreeNodes, TreeSections, TreePrimitives, TreeSharedIndex, TreePacked, TreeShared, TreeRuns;
            public readonly int SectionSize, SectionDomain, SectionCodecParms, SectionFirstPacked, SectionFirstShared, SectionFirstPrimitive,
                SectionFirstRun, SectionNumPacked, SectionNumPrimitives, SectionNumRuns, SectionLeafIndex;
            public readonly TagArrayMember SectionNodes;
            public readonly int RunSize, RunCount;
            public readonly int CompoundSize, CompoundUserData, CompoundPropertyBag, CompoundBits, CompoundArrays;
            public readonly int ListChildren, ListChildSize, ListChildShape;

            public TagProxyLayout(HavokTagfile tags)
            {
                if (tags.SizeOf("hkcdCompressedAabbCodecs::Aabb5BytesCodec") != TopNodeSize
                    || tags.SizeOf("hkcdCompressedAabbCodecs::Aabb4BytesCodec") != SectionNodeSize
                    || tags.SizeOf("hkcdStaticMeshTree::Primitive") != PrimitiveSize)
                    throw new NotSupportedException("This collision file's mesh tree codecs are not the ones the writer encodes.");

                MeshSize = Size(tags, Mesh);
                MeshTree = Member(tags, Mesh, "tree");
                MeshUserData = Member(tags, Mesh, "userData");
                MeshPropertyBag = tags.OffsetOf(Mesh, "propertyBag");
                MeshPalettes = Math.Min(Member(tags, Mesh, "collisionFilterInfoPalette"),
                    Math.Min(Member(tags, Mesh, "userDataPalette"), Member(tags, Mesh, "userStringPalette")));

                TreeSize = Size(tags, Tree);
                TreeDomain = Member(tags, Tree, "domain");
                TreeNumPrimitiveKeys = Member(tags, Tree, "numPrimitiveKeys");
                TreeBitsPerKey = Member(tags, Tree, "bitsPerKey");
                TreeMaxKeyValue = Member(tags, Tree, "maxKeyValue");
                TreeNodes = TagArrayMember.Of(tags, Tree, "nodes");
                TreeSections = TagArrayMember.Of(tags, Tree, "sections");
                TreePrimitives = TagArrayMember.Of(tags, Tree, "primitives");
                TreeSharedIndex = TagArrayMember.Of(tags, Tree, "sharedVerticesIndex");
                TreePacked = TagArrayMember.Of(tags, Tree, "packedVertices");
                TreeShared = TagArrayMember.Of(tags, Tree, "sharedVertices");
                TreeRuns = TagArrayMember.Of(tags, Tree, "primitiveDataRuns");

                SectionSize = Size(tags, Section);
                SectionDomain = Member(tags, Section, "domain");
                SectionCodecParms = Member(tags, Section, "codecParms");
                SectionFirstPacked = Member(tags, Section, "firstPackedVertexIndex");
                SectionFirstShared = Member(tags, Section, "firstSharedVertexIndex");
                SectionFirstPrimitive = Member(tags, Section, "firstPrimitiveIndex");
                SectionFirstRun = Member(tags, Section, "firstDataRunIndex");
                SectionNumPacked = Member(tags, Section, "numPackedVertices");
                SectionNumPrimitives = Member(tags, Section, "numPrimitives");
                SectionNumRuns = Member(tags, Section, "numDataRuns");
                SectionLeafIndex = Member(tags, Section, "leafIndex");
                SectionNodes = TagArrayMember.Of(tags, Section, "nodes");

                RunSize = Size(tags, Run);
                RunCount = Member(tags, Run, "count");

                CompoundSize = Size(tags, Compound);
                CompoundUserData = Member(tags, Compound, "userData");
                CompoundPropertyBag = tags.OffsetOf(Compound, "propertyBag");
                CompoundBits = Member(tags, Compound, "numBitsForChildShapeKey");
                CompoundArrays = Math.Min(Member(tags, Compound, "instances"), Member(tags, Compound, "tree"));
                foreach (string cleared in new[] { "instanceExtraInfos", "disabledLargeShapeKeyTable" })
                {
                    int at = tags.OffsetOf(Compound, cleared);
                    if (at >= 0) CompoundArrays = Math.Min(CompoundArrays, at);
                }
                if (CompoundBits >= CompoundArrays || CompoundUserData >= CompoundArrays)
                    throw new NotSupportedException("This collision file's hkpStaticCompoundShape is not laid out as the writer expects.");

                ListChildren = Member(tags, "hkpListShape", "childInfo");
                ListChildSize = Size(tags, "hkpListShape::ChildInfo");
                ListChildShape = Member(tags, "hkpListShape::ChildInfo", "shape");
            }

            static int Size(HavokTagfile tags, string type)
            {
                int size = tags.SizeOf(type);
                if (size <= 0)
                    throw new NotSupportedException("This collision file declares no " + type + ".");
                return size;
            }

            static int Member(HavokTagfile tags, string type, string member)
            {
                int at = tags.OffsetOf(type, member);
                if (at < 0)
                    throw new NotSupportedException("This collision file's " + type + " has no " + member + ".");
                return at;
            }
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
        internal object Tags;
        internal uint[] ObjectOffsets;
    }
}
