using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using CathodeLib;

namespace CATHODE
{
    /// <summary>
    /// Havok packfile loader/saver for Alien: Isolation WORLD/*.HKX and *.HKX64
    /// (packfile version 9, contents version hk_2012.2.0-r1).
    ///
    /// COLLISION.MAP rows reference <see cref="StaticCompoundShape"/> / <see cref="CompoundInstance"/>
    /// objects from this packfile (written as CollisionProxyIndex / Index ordinals).
    ///
    /// Supports full packfile round-trip, typed compound instance read/write, and appending
    /// or removing instances on an existing static compound (rebuilds a loose Storage6 BVH).
    /// </summary>
    public class HavokPackfile : CathodeFile
    {
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE;

        public HeaderInfo Header = new HeaderInfo();

        /// <summary>Raw __classnames__ payload (signatures + names). Fixups are empty on AI files.</summary>
        public byte[] ClassnamesData = Array.Empty<byte>();

        /// <summary>Raw __types__ payload (empty on AI collision files).</summary>
        public byte[] TypesData = Array.Empty<byte>();

        /// <summary>Raw __data__ object bytes (before local fixup table).</summary>
        public byte[] DataPayload = Array.Empty<byte>();

        /// <summary>
        /// Absolute file offset of <see cref="DataPayload"/>. Lets a caller patch values back into the
        /// original file bytes without rebuilding the packfile.
        /// </summary>
        public uint DataSectionOffset => _dataSectionOffset;
        private uint _dataSectionOffset;

        public List<LocalFixup> LocalFixups = new List<LocalFixup>();
        public List<GlobalFixup> GlobalFixups = new List<GlobalFixup>();
        public List<VirtualFixup> VirtualFixups = new List<VirtualFixup>();

        public List<PackfileObject> Objects = new List<PackfileObject>();

        /// <summary>Typed hkpStaticCompoundShape views in CollisionProxyIndex order.</summary>
        public List<StaticCompoundShape> StaticCompoundShapes = new List<StaticCompoundShape>();

        /// <summary>Typed hkpPhysicsSystem views in <c>hkpPhysicsData.systems[]</c> / SystemIndex order.</summary>
        public List<PhysicsSystem> PhysicsSystems = new List<PhysicsSystem>();

        public HavokPackfile(string path) : base(path) { }
        public HavokPackfile(MemoryStream stream, string path = "") : base(stream, path) { }
        public HavokPackfile(byte[] data, string path = "") : base(data, path) { }

        public enum ObjectClass
        {
            Unknown = 0,
            RootLevelContainer,
            PhysicsData,
            PhysicsSystem,
            WorldCinfo,
            GroupFilter,
            DefaultConvexListFilter,
            RigidBody,
            ListShape,
            StaticCompoundShape,
            BvCompressedMeshShape,
            BoxShape,
        }

        public class HeaderInfo
        {
            public uint Magic0 = 0x57E0E057;
            public uint Magic1 = 0x10C0C010;
            public int UserTag;
            public int FileVersion = 9;
            public byte PointerSize = 4;
            public byte LittleEndian = 1;
            public byte PaddingOption;
            public byte BaseClass = 1;
            public int NumSections = 3;
            public int ContentsSectionIndex = 2;
            public int ContentsSectionOffset;
            public int ContentsClassNameSectionIndex;
            public int ContentsClassNameSectionOffset;
            public string ContentsVersion = "hk_2012.2.0-r1";
            public int Flags;
            public int MaxPredicate = -1;
        }

        public struct LocalFixup
        {
            public uint Src;
            public uint Dst;
        }

        public struct GlobalFixup
        {
            public uint Src;
            public uint DstSectionIndex;
            public uint Dst;
        }

        public struct VirtualFixup
        {
            public uint Src;
            public uint SectionIndex;
            public int NameOffset;
        }

        public class PackfileObject
        {
            public uint DataOffset;
            public int ClassNameOffset;
            public string ClassName;
            public ObjectClass Class;
            public int ProxyIndex = -1;
        }

        public class StaticCompoundShape
        {
            public int ProxyIndex;
            public uint DataOffset;
            public List<CompoundInstance> Instances = new List<CompoundInstance>();

            /// <summary>Compound-local AABB domain used by the Storage6 BVH (min xyz / max xyz).</summary>
            public Vector4 DomainMin;
            public Vector4 DomainMax;

            /// <summary>Append an instance and adopt it, so it can report its own slot.</summary>
            public CompoundInstance AddInstance(CompoundInstance instance)
            {
                instance.Owner = this;
                Instances.Add(instance);
                return instance;
            }

            public void ClearInstances()
            {
                for (int i = 0; i < Instances.Count; i++)
                    Instances[i].Owner = null;
                Instances.Clear();
            }
        }

        /// <summary>
        /// A hkpPhysicsSystem template in PHYSICS.HKX / HKX64.
        /// PHYSICS.MAP and Commands DYNAMIC_PHYSICS_SYSTEM use <see cref="SystemIndex"/> —
        /// the index into <c>hkpPhysicsData.systems[]</c> (not packfile appearance order).
        /// </summary>
        public class PhysicsSystem
        {
            public int SystemIndex;
            public uint DataOffset;
            public string Name;
            public PackfileObject Object;
        }

        /// <summary>
        /// Read-only summary of an <c>hkpRigidBody</c> in a physics system (packfile field layout).
        /// </summary>
        public class RigidBodyInfo
        {
            public uint DataOffset;
            public string Name;
            public string ShapeClassName;
            /// <summary>hkpMotion::MotionType (Dynamic=1, Keyframed=4, Fixed=5, …).</summary>
            public byte MotionType;
            public string MotionTypeName;
            /// <summary>1/mass from <c>m_inertiaAndMassInv.w</c>; 0 means infinite mass.</summary>
            public float MassInv;
            /// <summary>Mass in kg, or <see cref="float.PositiveInfinity"/> when <see cref="MassInv"/> is 0.</summary>
            public float Mass;
            public Vector3 InertiaInvLocal;
            public float ObjectRadius;
            public float LinearDamping;
            public float MaxLinearVelocity;
            public float GravityFactor;
            public uint CollisionFilterInfo;
        }

        public class CompoundInstance
        {
            /// <summary>Translation; W packs Havok instance flags in the low bits.</summary>
            public Vector4 Translation;
            public Quaternion Rotation;
            public Vector4 Scale;
            public uint FilterInfo;
            public uint ChildFilterInfoMask;
            public ulong UserData;

            /// <summary>Offset of referenced shape object within the data payload.</summary>
            public uint ShapeDataOffset;
            public string ShapeClassName;

            /// <summary>Byte offset of this instance struct within the data payload.</summary>
            public uint DataOffset;

            /// <summary>Compound holding this instance (set by <see cref="StaticCompoundShape.AddInstance"/>).</summary>
            public StaticCompoundShape Owner;

            /// <summary>Slot within <see cref="Owner"/> — this is the COLLISION.MAP Index.</summary>
            public int Index => Owner?.Instances.IndexOf(this) ?? -1;
        }

        /// <summary>
        /// Triangle mesh built for UI preview (real Havok collision geometry when available).
        /// </summary>
        public class PreviewMesh
        {
            public List<Vector3> Positions = new List<Vector3>();
            public List<int> Indices = new List<int>();
            /// <summary>Number of leaf shapes contributed (meshes, boxes, convex hulls).</summary>
            public int ShapeCount;
            public int TriangleCount => Indices.Count / 3;

            /// <summary>
            /// Which top-level instance each triangle came from, in ascending triangle order, as
            /// (first triangle, instance) pairs. Only filled when the caller asks for it.
            /// </summary>
            /// <remarks>
            /// A top-level instance of a world host is what COLLISION.MAP addresses, so this is the
            /// hook from a ray hit back to the collider's flags. Nested instances are not recorded:
            /// everything under a root belongs to the same mapping.
            /// </remarks>
            public List<KeyValuePair<int, CompoundInstance>> InstanceRanges;

            /// <summary>The instance that produced a triangle, or null if not tracked.</summary>
            public CompoundInstance InstanceOf(int triangle)
            {
                if (InstanceRanges == null || InstanceRanges.Count == 0) return null;
                int low = 0, high = InstanceRanges.Count - 1, found = -1;
                while (low <= high)
                {
                    int mid = (low + high) / 2;
                    if (InstanceRanges[mid].Key <= triangle) { found = mid; low = mid + 1; }
                    else high = mid - 1;
                }
                return found < 0 ? null : InstanceRanges[found].Value;
            }
        }

        const int PreviewShapeCap = 2048;
        const int PreviewTriangleCap = 250000;
        const int SectionStride = 96;

        /// <summary>
        /// Build a preview mesh for a collision proxy from nested compounds, BvCompressed meshes,
        /// boxes, and convex hulls (instance transforms applied).
        /// </summary>
        public PreviewMesh BuildPreviewMesh(StaticCompoundShape compound)
        {
            // Preview solidifies open authored shells via convex hulls so colliders read as volumes.
            return BuildTriangleMesh(compound, PreviewShapeCap, PreviewTriangleCap, capRootInstances: true,
                skipInstances: null, convexHullOpenShells: true);
        }

        /// <summary>
        /// Full uncapped triangle extract for navmesh baking.
        /// Uses authored BvCompressed primitives (not convex-hull solidification) so Recast
        /// sees real floors/walls instead of invented hull tops on open shells.
        /// </summary>
        /// <param name="skipInstances">
        /// Optional instances to omit (e.g. PATH_CLOSED NavMeshBarrier boxes stamped as area ids instead).
        /// </param>
        /// <param name="trackInstances">
        /// Record which top-level instance each triangle came from, so a ray hit can be traced back
        /// to its COLLISION.MAP flags.
        /// </param>
        public PreviewMesh BuildBakeMesh(StaticCompoundShape compound, ISet<CompoundInstance> skipInstances = null,
                                         bool trackInstances = false)
        {
            return BuildTriangleMesh(compound, int.MaxValue, int.MaxValue, capRootInstances: false,
                skipInstances, convexHullOpenShells: false, trackInstances);
        }

        PreviewMesh BuildTriangleMesh(
            StaticCompoundShape compound,
            int shapeCap,
            int triCap,
            bool capRootInstances,
            ISet<CompoundInstance> skipInstances = null,
            bool convexHullOpenShells = true,
            bool trackInstances = false)
        {
            var mesh = new PreviewMesh();
            if (trackInstances)
                mesh.InstanceRanges = new List<KeyValuePair<int, CompoundInstance>>();
            if (compound == null)
                return mesh;

            var shapeByOffset = new Dictionary<uint, StaticCompoundShape>();
            for (int i = 0; i < StaticCompoundShapes.Count; i++)
            {
                StaticCompoundShape s = StaticCompoundShapes[i];
                if (s != null && !shapeByOffset.ContainsKey(s.DataOffset))
                    shapeByOffset[s.DataOffset] = s;
            }

            EmitCompoundPreview(
                mesh,
                compound,
                shapeByOffset,
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One,
                depth: 0,
                shapeCap,
                triCap,
                capRootInstances,
                skipInstances,
                convexHullOpenShells);
            return mesh;
        }

        /// <summary>
        /// Build a preview for a physics system from each rigid body's shape geometry
        /// (mesh / convex / box) in shape-local space (motion transforms not applied).
        /// </summary>
        public PreviewMesh BuildPreviewMesh(PhysicsSystem system)
        {
            var mesh = new PreviewMesh();
            if (system == null)
                return mesh;

            if (!TryGetRigidBodyOffsets(system, out List<uint> bodyOffsets))
                return mesh;

            var classAtOffset = new Dictionary<uint, string>();
            for (int i = 0; i < Objects.Count; i++)
                classAtOffset[Objects[i].DataOffset] = Objects[i].ClassName;

            var globalBySrc = new Dictionary<uint, uint>(GlobalFixups.Count);
            for (int i = 0; i < GlobalFixups.Count; i++)
                globalBySrc[GlobalFixups[i].Src] = GlobalFixups[i].Dst;

            // hkpWorldObject.collidable: +32 (64-bit) / +16 (32-bit); shape pointer at start of collidable.
            uint collidableShapeField = Header.PointerSize == 8 ? 32u : 16u;

            for (int b = 0; b < bodyOffsets.Count && mesh.ShapeCount < PreviewShapeCap; b++)
            {
                uint shapeField = bodyOffsets[b] + collidableShapeField;
                if (!globalBySrc.TryGetValue(shapeField, out uint shapeOff))
                    continue;
                classAtOffset.TryGetValue(shapeOff, out string shapeClass);
                AppendShapePreview(mesh, shapeOff, shapeClass ?? "", Vector3.Zero, Quaternion.Identity, Vector3.One);
            }
            return mesh;
        }

        /// <summary>
        /// Read rigid-body summaries for a physics system (name, mass, motion, shape, filter, damping).
        /// Layout verified against AI PHYSICS.HKX / HKX64 (hk_2012.2.0-r1 packfiles).
        /// </summary>
        public List<RigidBodyInfo> GetRigidBodies(PhysicsSystem system)
        {
            var bodies = new List<RigidBodyInfo>();
            if (system == null || !TryGetRigidBodyOffsets(system, out List<uint> bodyOffsets))
                return bodies;

            bool is64 = Header.PointerSize == 8;
            uint shapeFieldOff = is64 ? 32u : 16u;
            uint filterFieldOff = is64 ? 0x4Cu : 0x2Cu;
            uint nameFieldOff = is64 ? 0xB0u : 0x78u;
            uint motionTypeOff = is64 ? 0xC8u : 0x88u;
            uint maxLinVelOff = is64 ? 0xCCu : 0x8Cu;
            uint radiusOff = is64 ? 0x210u : 0x190u;
            uint linDampOff = is64 ? 0x214u : 0x194u;
            uint inertiaOff = is64 ? 0x220u : 0x1A0u;
            uint gravityOff = is64 ? 0x280u : 0x1FCu;

            var classAtOffset = new Dictionary<uint, string>();
            for (int i = 0; i < Objects.Count; i++)
                classAtOffset[Objects[i].DataOffset] = Objects[i].ClassName;

            var globalBySrc = new Dictionary<uint, uint>(GlobalFixups.Count);
            for (int i = 0; i < GlobalFixups.Count; i++)
                globalBySrc[GlobalFixups[i].Src] = GlobalFixups[i].Dst;

            for (int b = 0; b < bodyOffsets.Count; b++)
            {
                uint off = bodyOffsets[b];
                if (off + gravityOff + 4 > (uint)DataPayload.Length)
                    continue;

                string shapeClass = null;
                if (globalBySrc.TryGetValue(off + shapeFieldOff, out uint shapeOff))
                    classAtOffset.TryGetValue(shapeOff, out shapeClass);

                float massInv = BitConverter.ToSingle(DataPayload, (int)(off + inertiaOff + 12));
                byte motionType = DataPayload[(int)(off + motionTypeOff)];

                bodies.Add(new RigidBodyInfo
                {
                    DataOffset = off,
                    Name = ReadStringPtr(off + nameFieldOff) ?? "",
                    ShapeClassName = shapeClass ?? "",
                    MotionType = motionType,
                    MotionTypeName = DescribeMotionType(motionType),
                    MassInv = massInv,
                    Mass = massInv > 1e-12f ? 1f / massInv : float.PositiveInfinity,
                    InertiaInvLocal = new Vector3(
                        BitConverter.ToSingle(DataPayload, (int)(off + inertiaOff)),
                        BitConverter.ToSingle(DataPayload, (int)(off + inertiaOff + 4)),
                        BitConverter.ToSingle(DataPayload, (int)(off + inertiaOff + 8))),
                    ObjectRadius = BitConverter.ToSingle(DataPayload, (int)(off + radiusOff)),
                    LinearDamping = BitConverter.ToSingle(DataPayload, (int)(off + linDampOff)),
                    MaxLinearVelocity = BitConverter.ToSingle(DataPayload, (int)(off + maxLinVelOff)),
                    GravityFactor = BitConverter.ToSingle(DataPayload, (int)(off + gravityOff)),
                    CollisionFilterInfo = BitConverter.ToUInt32(DataPayload, (int)(off + filterFieldOff)),
                });
            }
            return bodies;
        }

        bool TryGetRigidBodyOffsets(PhysicsSystem system, out List<uint> bodyOffsets)
        {
            bodyOffsets = null;
            if (system == null)
                return false;
            // rigidBodies hkArray: +16 on 64-bit, +8 on 32-bit (hkReferencedObject packing).
            uint rigidBodiesField = system.DataOffset + (Header.PointerSize == 8 ? 16u : 8u);
            return TryReadPointerArray(rigidBodiesField, out bodyOffsets);
        }

        static string DescribeMotionType(byte motionType)
        {
            switch (motionType)
            {
                case 0: return "Invalid";
                case 1: return "Dynamic";
                case 2: return "SphereInertia";
                case 3: return "BoxInertia";
                case 4: return "Keyframed";
                case 5: return "Fixed";
                case 6: return "ThinBoxInertia";
                case 7: return "Character";
                default: return "Unknown(" + motionType + ")";
            }
        }

        void EmitCompoundPreview(
            PreviewMesh mesh,
            StaticCompoundShape compound,
            Dictionary<uint, StaticCompoundShape> shapeByOffset,
            Vector3 translation,
            Quaternion rotation,
            Vector3 scale,
            int depth,
            int shapeCap = PreviewShapeCap,
            int triCap = PreviewTriangleCap,
            bool capRootInstances = true,
            ISet<CompoundInstance> skipInstances = null,
            bool convexHullOpenShells = true)
        {
            if (compound == null || mesh.ShapeCount >= shapeCap || mesh.TriangleCount >= triCap || depth > 12)
                return;

            List<CompoundInstance> instances = compound.Instances;
            if (instances == null || instances.Count == 0)
            {
                // Preview-only domain AABB when a compound has no instances.
                if (convexHullOpenShells && HasValidDomain(compound))
                {
                    AppendBox(mesh,
                        new Vector3(compound.DomainMin.X, compound.DomainMin.Y, compound.DomainMin.Z),
                        new Vector3(compound.DomainMax.X, compound.DomainMax.Y, compound.DomainMax.Z),
                        translation, rotation, scale);
                }
                return;
            }

            // Huge world hosts: prefer real leaf geometry, but cap fan-out for UI preview only.
            int maxInstances = (capRootInstances && depth == 0 && instances.Count > 512) ? 512 : instances.Count;
            int beforeShapes = mesh.ShapeCount;
            int beforeTris = mesh.TriangleCount;

            for (int i = 0; i < maxInstances && mesh.ShapeCount < shapeCap && mesh.TriangleCount < triCap; i++)
            {
                CompoundInstance inst = instances[i];
                if (skipInstances != null && skipInstances.Contains(inst))
                    continue;

                Vector3 t = translation + Vector3.Transform(
                    new Vector3(inst.Translation.X, inst.Translation.Y, inst.Translation.Z) * scale,
                    rotation);
                Quaternion r = Quaternion.Normalize(rotation * inst.Rotation);
                Vector3 s = scale * new Vector3(inst.Scale.X, inst.Scale.Y, inst.Scale.Z);

                // Only the outermost level is recorded: a nested instance belongs to the same
                // COLLISION.MAP entry as the root it hangs off.
                if (depth == 0 && mesh.InstanceRanges != null)
                    mesh.InstanceRanges.Add(new KeyValuePair<int, CompoundInstance>(mesh.TriangleCount, inst));

                if (shapeByOffset.TryGetValue(inst.ShapeDataOffset, out StaticCompoundShape child))
                    EmitCompoundPreview(mesh, child, shapeByOffset, t, r, s, depth + 1, shapeCap, triCap, capRootInstances, skipInstances, convexHullOpenShells);
                else
                    AppendShapePreview(mesh, inst.ShapeDataOffset, inst.ShapeClassName ?? "", t, r, s, shapeCap, triCap, convexHullOpenShells);
            }

            // Preview only: fall back to compound domain AABB when nothing decoded.
            // Bake skips this — huge domains invent walkable floors that blow Recast bounds.
            if (convexHullOpenShells
                && mesh.ShapeCount == beforeShapes && mesh.TriangleCount == beforeTris && HasValidDomain(compound))
            {
                AppendBox(mesh,
                    new Vector3(compound.DomainMin.X, compound.DomainMin.Y, compound.DomainMin.Z),
                    new Vector3(compound.DomainMax.X, compound.DomainMax.Y, compound.DomainMax.Z),
                    translation, rotation, scale);
            }
        }

        void AppendShapePreview(
            PreviewMesh mesh,
            uint shapeDataOffset,
            string shapeClass,
            Vector3 translation,
            Quaternion rotation,
            Vector3 scale,
            int shapeCap = PreviewShapeCap,
            int triCap = PreviewTriangleCap,
            bool convexHullOpenShells = true)
        {
            if (mesh.ShapeCount >= shapeCap || mesh.TriangleCount >= triCap)
                return;

            if (string.Equals(shapeClass, "hkpBvCompressedMeshShape", StringComparison.Ordinal))
            {
                if (TryAppendBvCompressedMesh(mesh, shapeDataOffset, translation, rotation, scale, convexHullOpenShells))
                    return;
            }
            else if (string.Equals(shapeClass, "hkpBoxShape", StringComparison.Ordinal))
            {
                if (TryGetBoxHalfExtents(shapeDataOffset, out Vector3 he))
                {
                    AppendBox(mesh, -he, he, translation, rotation, scale);
                    return;
                }
            }
            else if (string.Equals(shapeClass, "hkpConvexVerticesShape", StringComparison.Ordinal))
            {
                if (TryAppendConvexVertices(mesh, shapeDataOffset, translation, rotation, scale))
                    return;
            }
            else if (string.Equals(shapeClass, "hkpListShape", StringComparison.Ordinal))
            {
                if (TryAppendListShape(mesh, shapeDataOffset, translation, rotation, scale, convexHullOpenShells))
                    return;
            }

            // Preview-only AABB placeholder for undecoded shapes (bake must not invent solids).
            if (convexHullOpenShells
                && TryGetPreviewLocalAabb(shapeDataOffset, shapeClass, out Vector3 amin, out Vector3 amax))
                AppendBox(mesh, amin, amax, translation, rotation, scale);
        }

        /// <summary>
        /// Decode <c>hkpBvCompressedMeshShape</c> leaf geometry from the embedded
        /// <c>hkcdStaticMeshTree&lt;..., 11, 21&gt;</c>.
        /// Preview may solidify open shells as convex hulls; bake keeps authored primitives.
        /// </summary>
        bool TryAppendBvCompressedMesh(
            PreviewMesh mesh,
            uint shapeOffset,
            Vector3 translation,
            Quaternion rotation,
            Vector3 scale,
            bool convexHullOpenShells = true)
        {
            if (!TryGetBvMeshArrays(shapeOffset,
                    out uint treeBase,
                    out uint sectionsOff, out int sectionCount,
                    out uint primitivesOff, out int primitiveCount,
                    out uint packedOff, out int packedCount,
                    out uint sharedOff, out int sharedCount,
                    out uint sharedIdxOff, out int sharedIdxCount))
                return false;

            if (sectionCount <= 0 || packedCount <= 0 || primitiveCount <= 0)
                return false;

            if (!TryReadTreeDomain(treeBase, out Vector3 treeMin, out Vector3 treeMax))
                return false;

            int trisBefore = mesh.TriangleCount;

            for (int s = 0; s < sectionCount && mesh.TriangleCount < PreviewTriangleCap; s++)
            {
                uint secOff = sectionsOff + (uint)(s * SectionStride);
                if (!TryReadBvSection(secOff,
                        out Vector3 codecBase, out Vector3 codecScale,
                        out int firstPacked, out int numPacked,
                        out int firstSharedIdx, out int numShared,
                        out int firstPrim, out int numPrim))
                    continue;

                if (numPacked <= 0 || numPrim <= 0)
                    continue;

                int localCount = numPacked + Math.Max(numShared, 0);
                var local = new Vector3[localCount];
                bool sectionOk = true;

                for (int i = 0; i < numPacked; i++)
                {
                    int vi = firstPacked + i;
                    if (vi < 0 || vi >= packedCount)
                    {
                        sectionOk = false;
                        break;
                    }
                    int o = (int)packedOff + vi * 4;
                    if (o + 4 > DataPayload.Length)
                    {
                        sectionOk = false;
                        break;
                    }
                    uint packed = BitConverter.ToUInt32(DataPayload, o);
                    local[i] = new Vector3(
                        codecBase.X + (packed & 0x7FFu) * codecScale.X,
                        codecBase.Y + ((packed >> 11) & 0x7FFu) * codecScale.Y,
                        codecBase.Z + ((packed >> 22) & 0x3FFu) * codecScale.Z);
                }
                if (!sectionOk)
                    continue;

                for (int i = 0; i < numShared; i++)
                {
                    int si = firstSharedIdx + i;
                    if (si < 0 || si >= sharedIdxCount || sharedCount <= 0)
                    {
                        sectionOk = false;
                        break;
                    }
                    int idxOff = (int)sharedIdxOff + si * 2;
                    if (idxOff + 2 > DataPayload.Length)
                    {
                        sectionOk = false;
                        break;
                    }
                    int sharedIndex = BitConverter.ToUInt16(DataPayload, idxOff);
                    if (sharedIndex < 0 || sharedIndex >= sharedCount)
                    {
                        sectionOk = false;
                        break;
                    }
                    int so = (int)sharedOff + sharedIndex * 8;
                    if (so + 8 > DataPayload.Length)
                    {
                        sectionOk = false;
                        break;
                    }
                    ulong sv = BitConverter.ToUInt64(DataPayload, so);
                    local[numPacked + i] = DecompressSharedVertex21(sv, treeMin, treeMax);
                }
                if (!sectionOk)
                    continue;

                // Preview: solidify open shells as convex hulls so colliders read as volumes.
                // Bake: keep authored triangles/quads — hull tops invent false walkable floors.
                if (convexHullOpenShells && TryAppendConvexHull(mesh, local, translation, rotation, scale))
                    continue;

                int baseIndex = mesh.Positions.Count;
                for (int i = 0; i < local.Length; i++)
                    mesh.Positions.Add(translation + Vector3.Transform(local[i] * scale, rotation));

                // Odd number of negative scale axes flips triangle winding for Recast.
                bool flipWinding = (scale.X * scale.Y * scale.Z) < 0f;

                int sectionTris = 0;
                for (int p = 0; p < numPrim && mesh.TriangleCount < PreviewTriangleCap; p++)
                {
                    int o = (int)primitivesOff + (firstPrim + p) * 4;
                    if (o + 4 > DataPayload.Length)
                        break;
                    int i0 = DataPayload[o];
                    int i1 = DataPayload[o + 1];
                    int i2 = DataPayload[o + 2];
                    int i3 = DataPayload[o + 3];
                    if (i0 >= localCount || i1 >= localCount || i2 >= localCount)
                        continue;

                    AddTriangle(mesh, baseIndex, i0, i1, i2, flipWinding);
                    sectionTris++;

                    if (i3 != i2 && i3 < localCount)
                    {
                        AddTriangle(mesh, baseIndex, i0, i2, i3, flipWinding);
                        sectionTris++;
                    }
                }

                if (sectionTris == 0)
                    mesh.Positions.RemoveRange(baseIndex, mesh.Positions.Count - baseIndex);
            }

            if (mesh.TriangleCount <= trisBefore)
                return false;

            mesh.ShapeCount++;
            return true;
        }

        bool TryGetBvMeshArrays(
            uint shapeOffset,
            out uint treeBase,
            out uint sectionsOff, out int sectionCount,
            out uint primitivesOff, out int primitiveCount,
            out uint packedOff, out int packedCount,
            out uint sharedOff, out int sharedCount,
            out uint sharedIdxOff, out int sharedIdxCount)
        {
            treeBase = 0;
            sectionsOff = primitivesOff = packedOff = sharedOff = sharedIdxOff = 0;
            sectionCount = primitiveCount = packedCount = sharedCount = sharedIdxCount = 0;

            // Embedded tree field offsets (Torrens COLLISION.HKX / HKX64), matching
            // hkcdStaticMeshTreeCommonConfig<uint, ulong, 11, 21> after the nodes array.
            uint sectionField, primitiveField, packedField, sharedField, sharedIdxField;
            if (Header.PointerSize == 8)
            {
                treeBase = shapeOffset + 0x70u;
                sectionField = shapeOffset + 0xB0u;
                primitiveField = shapeOffset + 0xC0u;
                sharedIdxField = shapeOffset + 0xD0u;
                packedField = shapeOffset + 0xE0u;
                sharedField = shapeOffset + 0xF0u;
            }
            else
            {
                treeBase = shapeOffset + 0x50u;
                sectionField = shapeOffset + 0x8Cu;
                primitiveField = shapeOffset + 0x98u;
                sharedIdxField = shapeOffset + 0xA4u;
                packedField = shapeOffset + 0xB0u;
                sharedField = shapeOffset + 0xBCu;
            }

            if (!TryGetHkArray(sectionField, out sectionsOff, out sectionCount) || sectionCount <= 0)
                return false;
            if (!TryGetHkArray(primitiveField, out primitivesOff, out primitiveCount) || primitiveCount <= 0)
                return false;
            if (!TryGetHkArray(packedField, out packedOff, out packedCount) || packedCount <= 0)
                return false;

            // Shared arrays are optional (many simple meshes have none).
            if (!TryGetHkArray(sharedField, out sharedOff, out sharedCount))
            {
                sharedOff = 0;
                sharedCount = 0;
            }
            if (!TryGetHkArray(sharedIdxField, out sharedIdxOff, out sharedIdxCount))
            {
                sharedIdxOff = 0;
                sharedIdxCount = 0;
            }

            return true;
        }

        bool TryReadTreeDomain(uint treeBase, out Vector3 min, out Vector3 max)
        {
            min = max = default;
            int off = (int)treeBase + 16;
            if (off + 32 > DataPayload.Length)
                return false;
            Vector4 vmin = ReadVector4(DataPayload, off);
            Vector4 vmax = ReadVector4(DataPayload, off + 16);
            min = new Vector3(vmin.X, vmin.Y, vmin.Z);
            max = new Vector3(vmax.X, vmax.Y, vmax.Z);
            return true;
        }

        /// <summary>
        /// Section layout from <c>hkcdStaticMeshTreeBaseSection</c>: domain @16,
        /// codecParms @48, firstPackedVertex @72, sharedVertices/primitives/dataRuns
        /// packed uint32s @76/80/84, numPackedVertices/numSharedIndices @88/89.
        /// Primitive/shared ranges pack as <c>(first &lt;&lt; 8) | count</c>.
        /// </summary>
        bool TryReadBvSection(
            uint sectionDataOffset,
            out Vector3 codecBase, out Vector3 codecScale,
            out int firstPacked, out int numPacked,
            out int firstSharedIdx, out int numShared,
            out int firstPrim, out int numPrim)
        {
            codecBase = codecScale = default;
            firstPacked = numPacked = firstSharedIdx = numShared = firstPrim = numPrim = 0;

            int sec = (int)sectionDataOffset;
            if (sec + SectionStride > DataPayload.Length)
                return false;

            codecBase = new Vector3(
                BitConverter.ToSingle(DataPayload, sec + 48),
                BitConverter.ToSingle(DataPayload, sec + 52),
                BitConverter.ToSingle(DataPayload, sec + 56));
            codecScale = new Vector3(
                BitConverter.ToSingle(DataPayload, sec + 60),
                BitConverter.ToSingle(DataPayload, sec + 64),
                BitConverter.ToSingle(DataPayload, sec + 68));

            // Flat sections legitimately use a 0 scale on an axis; only reject non-finite codecs.
            if (!IsFinite(codecBase) || !IsFinite(codecScale))
                return false;

            firstPacked = (int)BitConverter.ToUInt32(DataPayload, sec + 72);
            uint sharedData = BitConverter.ToUInt32(DataPayload, sec + 76);
            uint primData = BitConverter.ToUInt32(DataPayload, sec + 80);
            numPacked = DataPayload[sec + 88];
            numShared = DataPayload[sec + 89];
            firstSharedIdx = (int)(sharedData >> 8);
            firstPrim = (int)(primData >> 8);
            numPrim = (int)(primData & 0xFFu);
            return numPacked > 0 && numPrim > 0;
        }

        /// <summary>
        /// Shared verts are 21/21/21 in a uint64 as <c>x:21 y:21 pad:1 z:21</c>
        /// (Z starts at bit 43). Bit 42 is unused padding — shifting Z from 42
        /// places weld verts in the gaps between section AABBs and creates spikes.
        /// </summary>
        static Vector3 DecompressSharedVertex21(ulong packed, Vector3 domainMin, Vector3 domainMax)
        {
            const ulong mask = (1UL << 21) - 1UL;
            ulong qx = packed & mask;
            ulong qy = (packed >> 21) & mask;
            ulong qz = (packed >> 43) & mask;
            return new Vector3(
                domainMin.X + (domainMax.X - domainMin.X) * qx / mask,
                domainMin.Y + (domainMax.Y - domainMin.Y) * qy / mask,
                domainMin.Z + (domainMax.Z - domainMin.Z) * qz / mask);
        }

        static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z)
                || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
        }

        /// <summary>
        /// Build a convex hull from section vertices and append it for collision preview.
        /// Open authored shells become solid volumes this way.
        /// </summary>
        static bool TryAppendConvexHull(
            PreviewMesh mesh,
            Vector3[] localVerts,
            Vector3 translation,
            Quaternion rotation,
            Vector3 scale)
        {
            if (localVerts == null || localVerts.Length < 3)
                return false;

            // Dedup nearly-identical verts (quantization siblings).
            var pts = new List<Vector3>(localVerts.Length);
            for (int i = 0; i < localVerts.Length; i++)
            {
                Vector3 p = localVerts[i];
                bool dup = false;
                for (int j = 0; j < pts.Count; j++)
                {
                    if ((pts[j] - p).LengthSquared() < 1e-12f) { dup = true; break; }
                }
                if (!dup)
                    pts.Add(p);
            }
            if (pts.Count < 3)
                return false;

            if (!TryBuildConvexHullTriangles(pts, out List<int> hullTris))
                return false;
            if (hullTris.Count < 3)
                return false;

            int baseIndex = mesh.Positions.Count;
            for (int i = 0; i < pts.Count; i++)
                mesh.Positions.Add(translation + Vector3.Transform(pts[i] * scale, rotation));

            for (int i = 0; i + 2 < hullTris.Count && mesh.TriangleCount < PreviewTriangleCap; i += 3)
            {
                mesh.Indices.Add(baseIndex + hullTris[i]);
                mesh.Indices.Add(baseIndex + hullTris[i + 1]);
                mesh.Indices.Add(baseIndex + hullTris[i + 2]);
            }
            return true;
        }

        /// <summary>
        /// Incremental 3D convex hull. Returns triangle indices into <paramref name="pts"/>.
        /// </summary>
        static bool TryBuildConvexHullTriangles(List<Vector3> pts, out List<int> tris)
        {
            tris = new List<int>();
            int n = pts.Count;
            if (n < 3)
                return false;

            // Find initial tetrahedron (or flat triangle).
            int i0 = 0, i1 = -1, i2 = -1, i3 = -1;
            float best = 0f;
            for (int i = 1; i < n; i++)
            {
                float d = (pts[i] - pts[i0]).LengthSquared();
                if (d > best) { best = d; i1 = i; }
            }
            if (i1 < 0 || best < 1e-16f)
                return false;

            best = 0f;
            Vector3 e01 = pts[i1] - pts[i0];
            for (int i = 0; i < n; i++)
            {
                if (i == i0 || i == i1) continue;
                float a = Vector3.Cross(e01, pts[i] - pts[i0]).LengthSquared();
                if (a > best) { best = a; i2 = i; }
            }
            if (i2 < 0 || best < 1e-20f)
                return false;

            best = 0f;
            Vector3 nrm = Vector3.Cross(pts[i1] - pts[i0], pts[i2] - pts[i0]);
            for (int i = 0; i < n; i++)
            {
                if (i == i0 || i == i1 || i == i2) continue;
                float d = Math.Abs(Vector3.Dot(nrm, pts[i] - pts[i0]));
                if (d > best) { best = d; i3 = i; }
            }

            var faces = new List<(int a, int b, int c)>();
            if (i3 < 0 || best < 1e-8f)
            {
                // Degenerate flat cloud — emit both sides of the triangle fan on the plane.
                faces.Add((i0, i1, i2));
                faces.Add((i0, i2, i1));
            }
            else
            {
                // Orient tetra so volume is positive.
                if (Vector3.Dot(nrm, pts[i3] - pts[i0]) > 0f)
                {
                    int t = i2; i2 = i3; i3 = t;
                    nrm = Vector3.Cross(pts[i1] - pts[i0], pts[i2] - pts[i0]);
                }
                faces.Add((i0, i1, i2));
                faces.Add((i0, i2, i3));
                faces.Add((i0, i3, i1));
                faces.Add((i1, i3, i2));
            }

            var used = new bool[n];
            used[i0] = used[i1] = used[i2] = true;
            if (i3 >= 0) used[i3] = true;

            for (int pi = 0; pi < n; pi++)
            {
                if (used[pi]) continue;
                Vector3 p = pts[pi];

                var visible = new List<int>();
                for (int f = 0; f < faces.Count; f++)
                {
                    var face = faces[f];
                    Vector3 fn = Vector3.Cross(pts[face.b] - pts[face.a], pts[face.c] - pts[face.a]);
                    if (Vector3.Dot(fn, p - pts[face.a]) > 1e-8f)
                        visible.Add(f);
                }
                if (visible.Count == 0)
                    continue; // inside

                // Horizon edges: edges of visible faces used exactly once.
                var edgeUse = new Dictionary<long, (int a, int b, int count)>();
                for (int vi = 0; vi < visible.Count; vi++)
                {
                    var face = faces[visible[vi]];
                    AccHullEdge(edgeUse, face.a, face.b);
                    AccHullEdge(edgeUse, face.b, face.c);
                    AccHullEdge(edgeUse, face.c, face.a);
                }

                // Remove visible faces (highest index first).
                visible.Sort();
                for (int vi = visible.Count - 1; vi >= 0; vi--)
                    faces.RemoveAt(visible[vi]);

                foreach (var kv in edgeUse)
                {
                    if (kv.Value.count != 1) continue;
                    int a = kv.Value.a, b = kv.Value.b;
                    // Outward face: a,b from original winding; new apex pi.
                    Vector3 fn = Vector3.Cross(pts[b] - pts[a], p - pts[a]);
                    // Ensure outward relative to hull centroid of remaining? Use cross with inside test:
                    // horizon edge directed so face (a,b,pi) has outward normal.
                    faces.Add((a, b, pi));
                }
                used[pi] = true;
            }

            // Fix winding: ensure normals point away from centroid.
            Vector3 centroid = Vector3.Zero;
            for (int i = 0; i < n; i++) centroid += pts[i];
            centroid /= n;

            for (int f = 0; f < faces.Count; f++)
            {
                int a = faces[f].a, b = faces[f].b, c = faces[f].c;
                Vector3 fn = Vector3.Cross(pts[b] - pts[a], pts[c] - pts[a]);
                if (Vector3.Dot(fn, centroid - pts[a]) > 0f)
                {
                    int tmp = b; b = c; c = tmp;
                }
                tris.Add(a);
                tris.Add(b);
                tris.Add(c);
            }
            return tris.Count >= 3;
        }

        static void AccHullEdge(Dictionary<long, (int a, int b, int count)> edgeUse, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (edgeUse.TryGetValue(key, out var e))
                edgeUse[key] = (e.a, e.b, e.count + 1);
            else
                edgeUse[key] = (a, b, 1);
        }

        bool TryAppendConvexVertices(
            PreviewMesh mesh,
            uint shapeOffset,
            Vector3 translation,
            Quaternion rotation,
            Vector3 scale)
        {
            // 64-bit: rotatedVertices @+64, numVertices @+80, planeEquations @+88, connectivity @+104
            // 32-bit: rotatedVertices @+48, numVertices @+60, planeEquations @+64, connectivity @+76
            // (hkArray is 12 vs 16 bytes; confirmed against aabbHalfExtents @32/48 pattern used elsewhere)
            int ptrSize = Header.PointerSize;
            uint rotatedField = shapeOffset + (ptrSize == 8 ? 64u : 48u);
            int numVertOff = (int)shapeOffset + (ptrSize == 8 ? 80 : 60);
            uint planesField = shapeOffset + (ptrSize == 8 ? 88u : 64u);
            uint connectivityField = shapeOffset + (ptrSize == 8 ? 104u : 76u);

            if (numVertOff + 4 > DataPayload.Length)
                return false;
            int numVertices = BitConverter.ToInt32(DataPayload, numVertOff);
            if (numVertices <= 0 || numVertices > 4096)
                return false;

            if (!TryGetHkArray(rotatedField, out uint rotatedOff, out int rotatedCount) || rotatedCount < numVertices)
                return false;

            // Each "rotated vertex" is stored as hkVector4 in many packs (Matrix3 array is 3 columns,
            // but AI files often serialize as contiguous float4 positions). Detect stride.
            int avail = (int)Math.Min((uint)DataPayload.Length - rotatedOff, (uint)(rotatedCount * 48));
            int stride = 16;
            if (rotatedCount >= numVertices * 3 && avail >= numVertices * 48)
                stride = 48; // true Matrix3 (3×float4 columns); use translation column / first row xz

            var verts = new List<Vector3>(numVertices);
            for (int i = 0; i < numVertices; i++)
            {
                int o = (int)rotatedOff + i * stride;
                if (o + 12 > DataPayload.Length)
                    return false;
                // Matrix3 form: take column 0's translation-like first three floats of each slot when stride 16.
                verts.Add(new Vector3(
                    BitConverter.ToSingle(DataPayload, o),
                    BitConverter.ToSingle(DataPayload, o + 4),
                    BitConverter.ToSingle(DataPayload, o + 8)));
            }

            int baseIndex = mesh.Positions.Count;
            for (int i = 0; i < verts.Count; i++)
                mesh.Positions.Add(translation + Vector3.Transform(verts[i] * scale, rotation));

            int trisBefore = mesh.TriangleCount;

            // Prefer authored connectivity (face → vertex indices).
            if (TryAppendConvexConnectivity(mesh, connectivityField, baseIndex, numVertices))
            {
                mesh.ShapeCount++;
                return true;
            }

            // Fall back: fan triangles from plane equations / convex hull of verts (simple fan about centroid).
            if (verts.Count >= 3)
            {
                // Build faces from plane equations when present.
                if (TryGetHkArray(planesField, out uint planesOff, out int planeCount) && planeCount > 0)
                {
                    for (int p = 0; p < planeCount && mesh.TriangleCount < PreviewTriangleCap; p++)
                    {
                        int o = (int)planesOff + p * 16;
                        if (o + 16 > DataPayload.Length)
                            break;
                        Vector4 plane = ReadVector4(DataPayload, o);
                        var face = new List<int>(8);
                        for (int v = 0; v < verts.Count; v++)
                        {
                            float d = verts[v].X * plane.X + verts[v].Y * plane.Y + verts[v].Z * plane.Z + plane.W;
                            if (Math.Abs(d) < 0.01f)
                                face.Add(v);
                        }
                        for (int i = 1; i + 1 < face.Count; i++)
                        {
                            mesh.Indices.Add(baseIndex + face[0]);
                            mesh.Indices.Add(baseIndex + face[i]);
                            mesh.Indices.Add(baseIndex + face[i + 1]);
                        }
                    }
                }
            }

            if (mesh.TriangleCount == trisBefore && verts.Count >= 4)
            {
                // Last resort: AABB of the hull (still better than nothing for tiny hulls).
                mesh.Positions.RemoveRange(baseIndex, mesh.Positions.Count - baseIndex);
                Vector3 min = verts[0], max = verts[0];
                for (int i = 1; i < verts.Count; i++)
                {
                    min = Vector3.Min(min, verts[i]);
                    max = Vector3.Max(max, verts[i]);
                }
                AppendBox(mesh, min, max, translation, rotation, scale);
                return true;
            }

            if (mesh.TriangleCount == trisBefore)
            {
                mesh.Positions.RemoveRange(baseIndex, mesh.Positions.Count - baseIndex);
                return false;
            }

            mesh.ShapeCount++;
            return true;
        }

        bool TryAppendConvexConnectivity(PreviewMesh mesh, uint connectivityField, int baseIndex, int numVertices)
        {
            // connectivity is a pointer (global fixup) to hkpConvexVerticesConnectivity.
            uint connOff = 0;
            bool found = false;
            for (int i = 0; i < GlobalFixups.Count; i++)
            {
                if (GlobalFixups[i].Src == connectivityField)
                {
                    connOff = GlobalFixups[i].Dst;
                    found = true;
                    break;
                }
            }
            if (!found)
                return false;

            int ptrSize = Header.PointerSize;
            // hkReferencedObject (8/16) then vertexIndices array, numVerticesPerFace array.
            uint indicesField = connOff + (ptrSize == 8 ? 16u : 8u);
            uint facesField = connOff + (ptrSize == 8 ? 32u : 20u);
            if (!TryGetHkArray(indicesField, out uint indicesOff, out int indexCount) || indexCount <= 0)
                return false;
            if (!TryGetHkArray(facesField, out uint facesOff, out int faceCount) || faceCount <= 0)
                return false;

            int cursor = 0;
            int trisBefore = mesh.TriangleCount;
            for (int f = 0; f < faceCount && mesh.TriangleCount < PreviewTriangleCap; f++)
            {
                int fo = (int)facesOff + f;
                if (fo >= DataPayload.Length)
                    break;
                int n = DataPayload[fo];
                if (n < 3 || cursor + n > indexCount)
                    break;
                // Fan triangulation of the face.
                int i0 = BitConverter.ToUInt16(DataPayload, (int)indicesOff + cursor * 2);
                for (int i = 1; i + 1 < n; i++)
                {
                    int ia = BitConverter.ToUInt16(DataPayload, (int)indicesOff + (cursor + i) * 2);
                    int ib = BitConverter.ToUInt16(DataPayload, (int)indicesOff + (cursor + i + 1) * 2);
                    if (i0 >= numVertices || ia >= numVertices || ib >= numVertices)
                        continue;
                    mesh.Indices.Add(baseIndex + i0);
                    mesh.Indices.Add(baseIndex + ia);
                    mesh.Indices.Add(baseIndex + ib);
                }
                cursor += n;
            }
            return mesh.TriangleCount > trisBefore;
        }

        bool TryAppendListShape(
            PreviewMesh mesh,
            uint shapeOffset,
            Vector3 translation,
            Quaternion rotation,
            Vector3 scale,
            bool convexHullOpenShells = true)
        {
            // childInfo hkArray at +40 (64-bit) / +28 (32-bit approx).
            uint childField = shapeOffset + (Header.PointerSize == 8 ? 40u : 28u);
            if (!TryGetHkArray(childField, out uint childOff, out int childCount) || childCount <= 0)
                return false;

            // Each ChildInfo has a shape pointer (global fixup) at the start.
            int stride = Header.PointerSize == 8 ? 32 : 16;
            var classAtOffset = new Dictionary<uint, string>();
            for (int i = 0; i < Objects.Count; i++)
                classAtOffset[Objects[i].DataOffset] = Objects[i].ClassName;
            var globalBySrc = new Dictionary<uint, uint>(GlobalFixups.Count);
            for (int i = 0; i < GlobalFixups.Count; i++)
                globalBySrc[GlobalFixups[i].Src] = GlobalFixups[i].Dst;

            int before = mesh.ShapeCount;
            for (int i = 0; i < childCount && mesh.ShapeCount < PreviewShapeCap; i++)
            {
                uint slot = childOff + (uint)(i * stride);
                if (!globalBySrc.TryGetValue(slot, out uint childShape))
                    continue;
                classAtOffset.TryGetValue(childShape, out string childClass);
                AppendShapePreview(mesh, childShape, childClass ?? "", translation, rotation, scale,
                    PreviewShapeCap, PreviewTriangleCap, convexHullOpenShells);
            }
            return mesh.ShapeCount > before;
        }

        /// <summary>
        /// Resolve a pointer field through the local fixup table (i.e. a pointer to somewhere else in __data__).
        /// </summary>
        public bool TryResolveLocal(uint pointerFieldOffset, out uint dataOffset)
        {
            for (int i = 0; i < LocalFixups.Count; i++)
            {
                if (LocalFixups[i].Src == pointerFieldOffset)
                {
                    dataOffset = LocalFixups[i].Dst;
                    return dataOffset < (uint)DataPayload.Length;
                }
            }
            dataOffset = 0;
            return false;
        }

        /// <summary>
        /// Read a null terminated string out of __data__.
        /// </summary>
        public string ReadStringAt(uint dataOffset)
        {
            if (dataOffset >= (uint)DataPayload.Length) return null;
            int end = (int)dataOffset;
            while (end < DataPayload.Length && DataPayload[end] != 0) end++;
            return Encoding.ASCII.GetString(DataPayload, (int)dataOffset, end - (int)dataOffset);
        }

        /// <summary>
        /// Read an <c>hkArray</c> field: resolves the data pointer and reads the element count.
        /// </summary>
        public bool TryGetHkArray(uint arrayFieldOffset, out uint dataOffset, out int count)
        {
            dataOffset = 0;
            count = 0;
            int ptrSize = Header.PointerSize;
            int sizePos = (int)arrayFieldOffset + ptrSize;
            if (sizePos + 4 > DataPayload.Length)
                return false;

            count = BitConverter.ToInt32(DataPayload, sizePos);
            if (count < 0 || count > 5_000_000)
                return false;
            if (count == 0)
                return true;

            for (int f = 0; f < LocalFixups.Count; f++)
            {
                if (LocalFixups[f].Src == arrayFieldOffset)
                {
                    dataOffset = LocalFixups[f].Dst;
                    return dataOffset < (uint)DataPayload.Length;
                }
            }
            return false;
        }

        bool TryGetPreviewLocalAabb(uint shapeDataOffset, string shapeClass, out Vector3 min, out Vector3 max)
        {
            min = default;
            max = default;
            if (string.Equals(shapeClass, "hkpBoxShape", StringComparison.Ordinal)
                && TryGetBoxHalfExtents(shapeDataOffset, out Vector3 he))
            {
                min = -he;
                max = he;
                return true;
            }

            // hkpConvexVerticesShape AABB: 64-bit half@48 centre@64; 32-bit half@32 centre@48.
            if (string.Equals(shapeClass, "hkpConvexVerticesShape", StringComparison.Ordinal)
                || string.Equals(shapeClass, "hkpConvexTranslateShape", StringComparison.Ordinal)
                || string.Equals(shapeClass, "hkpConvexTransformShape", StringComparison.Ordinal))
            {
                int heOff = (int)shapeDataOffset + (Header.PointerSize == 8 ? 48 : 32);
                int cOff = (int)shapeDataOffset + (Header.PointerSize == 8 ? 64 : 48);
                if (cOff + 16 <= DataPayload.Length)
                {
                    Vector4 half = ReadVector4(DataPayload, heOff);
                    Vector4 center = ReadVector4(DataPayload, cOff);
                    Vector3 he3 = new Vector3(Math.Abs(half.X), Math.Abs(half.Y), Math.Abs(half.Z));
                    Vector3 c3 = new Vector3(center.X, center.Y, center.Z);
                    min = c3 - he3;
                    max = c3 + he3;
                    return he3.LengthSquared() > 1e-12f;
                }
            }

            return false;
        }

        static bool HasValidDomain(StaticCompoundShape compound)
        {
            return compound.DomainMin.X <= compound.DomainMax.X
                && compound.DomainMin.Y <= compound.DomainMax.Y
                && compound.DomainMin.Z <= compound.DomainMax.Z
                && !(float.IsInfinity(compound.DomainMin.X) || float.IsInfinity(compound.DomainMax.X));
        }

        static void AddTriangle(PreviewMesh mesh, int baseIndex, int i0, int i1, int i2, bool flipWinding)
        {
            if (flipWinding)
            {
                mesh.Indices.Add(baseIndex + i0);
                mesh.Indices.Add(baseIndex + i2);
                mesh.Indices.Add(baseIndex + i1);
            }
            else
            {
                mesh.Indices.Add(baseIndex + i0);
                mesh.Indices.Add(baseIndex + i1);
                mesh.Indices.Add(baseIndex + i2);
            }
        }

        static void AppendBox(
            PreviewMesh mesh,
            Vector3 localMin,
            Vector3 localMax,
            Vector3 translation,
            Quaternion rotation,
            Vector3 scale)
        {
            if (mesh.ShapeCount >= PreviewShapeCap || mesh.TriangleCount >= PreviewTriangleCap)
                return;

            int baseIndex = mesh.Positions.Count;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = new Vector3(
                    (corner & 1) == 0 ? localMin.X : localMax.X,
                    (corner & 2) == 0 ? localMin.Y : localMax.Y,
                    (corner & 4) == 0 ? localMin.Z : localMax.Z);
                mesh.Positions.Add(translation + Vector3.Transform(local * scale, rotation));
            }

            // 12 triangles, outward winding for a solid box
            int[] tris =
            {
                0, 2, 3, 0, 3, 1,
                4, 5, 7, 4, 7, 6,
                0, 1, 5, 0, 5, 4,
                2, 6, 7, 2, 7, 3,
                0, 4, 6, 0, 6, 2,
                1, 3, 7, 1, 7, 5,
            };
            bool flipWinding = (scale.X * scale.Y * scale.Z) < 0f;
            for (int i = 0; i + 2 < tris.Length; i += 3)
                AddTriangle(mesh, baseIndex, tris[i], tris[i + 1], tris[i + 2], flipWinding);
            mesh.ShapeCount++;
        }

        /// <summary>
        /// Append a new instance to an existing static compound, reusing another instance's shape.
        /// Returns the new instance index (COLLISION.MAP Index). Rebuilds the compound BVH with a
        /// loose Storage6 tree (correct queries, weaker mid-phase culling).
        /// New instance/tree bytes are appended to the data payload (old arrays become orphaned).
        /// </summary>
        public int AddInstance(
            int proxyIndex,
            Vector3 translation,
            Quaternion rotation,
            Vector3 scale,
            int templateInstanceIndex = 0,
            float aabbPadding = 1.0f)
        {
            StaticCompoundShape compound = FindCompound(proxyIndex);
            if (compound == null)
                throw new ArgumentOutOfRangeException(nameof(proxyIndex), "No static compound with that CollisionProxyIndex.");
            if (compound.Instances.Count == 0)
                throw new InvalidOperationException("Cannot add to an empty compound — no template shape to reference.");
            if (templateInstanceIndex < 0 || templateInstanceIndex >= compound.Instances.Count)
                throw new ArgumentOutOfRangeException(nameof(templateInstanceIndex));

            return AddInstance(proxyIndex, translation, rotation, scale, compound.Instances[templateInstanceIndex], aabbPadding);
        }

        /// <summary>
        /// Append a fresh instance using explicit shape/filter properties (no live template slot required).
        /// Immediately rewrites that compound's arrays. Prefer <see cref="BeginInstanceRebuild"/> /
        /// <see cref="EnqueueInstance"/> / <see cref="CommitInstanceRebuild"/> when adding many.
        /// </summary>
        public int AddInstance(
            int proxyIndex,
            Vector3 translation,
            Quaternion rotation,
            Vector3 scale,
            CompoundInstance properties,
            float aabbPadding = 1.0f)
        {
            if (properties == null)
                throw new ArgumentNullException(nameof(properties));
            StaticCompoundShape compound = FindCompound(proxyIndex);
            if (compound == null)
                throw new ArgumentOutOfRangeException(nameof(proxyIndex), "No static compound with that CollisionProxyIndex.");

            CompoundInstance inst = CloneInstanceProperties(properties, translation, rotation, scale);
            ExpandDomain(compound, translation, aabbPadding);
            ScrubShapeFixupsForInstances(compound);
            compound.AddInstance(inst);
            RewriteCompoundArrays(compound);
            return compound.Instances.Count - 1;
        }

        /// <summary>Compounds cleared+refilled during the current instance rebuild (lazy).</summary>
        HashSet<StaticCompoundShape> _rebuildTouched;

        /// <summary>
        /// Snapshot per-proxy prototype instances (shape/filter). Does <b>not</b> clear compounds yet —
        /// rigid bodies in this packfile require every referenced compound to keep getInstances().size() &gt; 0.
        /// Compounds are cleared on first <see cref="EnqueueInstance"/> and only those are rewritten on commit;
        /// untouched compounds keep their retail instances.
        /// </summary>
        public Dictionary<int, CompoundInstance> BeginInstanceRebuild()
        {
            _rebuildTouched = new HashSet<StaticCompoundShape>();
            var prototypes = new Dictionary<int, CompoundInstance>();
            for (int i = 0; i < StaticCompoundShapes.Count; i++)
            {
                StaticCompoundShape compound = StaticCompoundShapes[i];
                if (compound.Instances.Count > 0 && !prototypes.ContainsKey(compound.ProxyIndex))
                {
                    CompoundInstance src = compound.Instances[0];
                    prototypes[compound.ProxyIndex] = new CompoundInstance
                    {
                        Translation = src.Translation,
                        Rotation = src.Rotation,
                        Scale = src.Scale,
                        FilterInfo = src.FilterInfo,
                        ChildFilterInfoMask = src.ChildFilterInfoMask,
                        UserData = src.UserData,
                        ShapeDataOffset = src.ShapeDataOffset,
                        ShapeClassName = src.ShapeClassName,
                    };
                }
            }
            return prototypes;
        }

        /// <summary>
        /// Empty a compound so it is rebuilt purely from enqueued instances. Called lazily on first
        /// <see cref="EnqueueInstance"/>, or up-front by callers that clear the world hosts.
        /// </summary>
        public void PrepareCompoundForRebuild(StaticCompoundShape compound)
        {
            if (_rebuildTouched == null)
                _rebuildTouched = new HashSet<StaticCompoundShape>();
            if (!_rebuildTouched.Add(compound))
                return;

            ScrubShapeFixupsForInstances(compound);
            compound.ClearInstances();
            // Fresh domain — ExpandDomain grows from the enqueued instances.
            compound.DomainMin = new Vector4(float.MaxValue, float.MaxValue, float.MaxValue, 0);
            compound.DomainMax = new Vector4(float.MinValue, float.MinValue, float.MinValue, 0);
        }

        /// <summary>
        /// Append an instance in memory only (no packfile rewrite yet). Returns the new instance object.
        /// </summary>
        public CompoundInstance EnqueueInstance(
            int proxyIndex,
            Vector3 translation,
            Quaternion rotation,
            Vector3 scale,
            CompoundInstance properties,
            float aabbPadding = 1.0f)
        {
            if (properties == null)
                throw new ArgumentNullException(nameof(properties));
            StaticCompoundShape compound = FindCompound(proxyIndex);
            if (compound == null)
                throw new ArgumentOutOfRangeException(nameof(proxyIndex), "No static compound with that CollisionProxyIndex.");

            PrepareCompoundForRebuild(compound);
            CompoundInstance inst = CloneInstanceProperties(properties, translation, rotation, scale);
            ExpandDomain(compound, translation, aabbPadding);
            compound.AddInstance(inst);
            return inst;
        }

        /// <summary>
        /// Append an instance to an existing compound object (in memory only).
        /// </summary>
        public CompoundInstance EnqueueInstance(
            StaticCompoundShape compound,
            Vector3 translation,
            Quaternion rotation,
            Vector3 scale,
            CompoundInstance properties,
            float aabbPadding = 1.0f)
        {
            if (compound == null)
                throw new ArgumentNullException(nameof(compound));
            if (properties == null)
                throw new ArgumentNullException(nameof(properties));

            PrepareCompoundForRebuild(compound);
            CompoundInstance inst = CloneInstanceProperties(properties, translation, rotation, scale);
            ExpandDomain(compound, translation, aabbPadding);
            compound.AddInstance(inst);
            return inst;
        }

        /// <summary>
        /// Rewrite instance/tree arrays for compounds touched during this rebuild only.
        /// Untouched compounds keep retail instances so rigid-body COL_EVERYTHING asserts stay happy.
        /// </summary>
        public void CommitInstanceRebuild()
        {
            if (_rebuildTouched == null)
                return;

            foreach (StaticCompoundShape compound in _rebuildTouched)
            {
                if (compound.Instances.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Rebuilt compound proxy " + compound.ProxyIndex
                        + " has zero instances — rigid bodies require at least one.");
                }
                if (!TryGetCompoundArrayFields(compound.DataOffset, out _, out _))
                {
                    throw new InvalidOperationException(
                        "Could not locate compound instance/tree array fields for proxy "
                        + compound.ProxyIndex + " with " + compound.Instances.Count + " instances.");
                }
                RewriteCompoundArrays(compound);
            }
            _rebuildTouched = null;
        }

        static CompoundInstance CloneInstanceProperties(
            CompoundInstance properties,
            Vector3 translation,
            Quaternion rotation,
            Vector3 scale)
        {
            uint flagsW = FloatToUInt(properties.Translation.W);
            return new CompoundInstance
            {
                Translation = new Vector4(translation.X, translation.Y, translation.Z, UIntToFloat(flagsW)),
                Rotation = rotation,
                Scale = new Vector4(scale.X, scale.Y, scale.Z, properties.Scale.W),
                FilterInfo = properties.FilterInfo,
                ChildFilterInfoMask = properties.ChildFilterInfoMask,
                UserData = properties.UserData,
                ShapeDataOffset = properties.ShapeDataOffset,
                ShapeClassName = properties.ShapeClassName,
            };
        }

        static void ExpandDomain(StaticCompoundShape compound, Vector3 translation, float aabbPadding)
        {
            Vector3 pad = new Vector3(aabbPadding, aabbPadding, aabbPadding);
            ExpandDomain(compound, translation - pad, translation + pad);
        }

        static void ExpandDomain(StaticCompoundShape compound, Vector3 aabbMin, Vector3 aabbMax)
        {
            compound.DomainMin = new Vector4(
                Math.Min(compound.DomainMin.X, aabbMin.X),
                Math.Min(compound.DomainMin.Y, aabbMin.Y),
                Math.Min(compound.DomainMin.Z, aabbMin.Z),
                0);
            compound.DomainMax = new Vector4(
                Math.Max(compound.DomainMax.X, aabbMax.X),
                Math.Max(compound.DomainMax.Y, aabbMax.Y),
                Math.Max(compound.DomainMax.Z, aabbMax.Z),
                0);
        }

        /// <summary>
        /// Union the world-space AABB of a child compound (local domain transformed by TRS) into the host domain.
        /// </summary>
        public void ExpandDomainWithTransformedChild(
            StaticCompoundShape host,
            StaticCompoundShape child,
            Vector3 translation,
            Quaternion rotation,
            Vector3 scale,
            float extraPadding = 0.25f)
        {
            if (host == null)
                return;
            if (child == null || child.DomainMin.X > child.DomainMax.X)
            {
                ExpandDomain(host, translation, Math.Max(extraPadding, 1f));
                return;
            }

            GetTransformedAabb(
                new Vector3(child.DomainMin.X, child.DomainMin.Y, child.DomainMin.Z),
                new Vector3(child.DomainMax.X, child.DomainMax.Y, child.DomainMax.Z),
                translation, rotation, scale,
                out Vector3 wMin, out Vector3 wMax);
            Vector3 pad = new Vector3(extraPadding, extraPadding, extraPadding);
            ExpandDomain(host, wMin - pad, wMax + pad);
        }

        /// <summary>
        /// Union a centre-origin box (±halfExtents, optionally scaled) into the host domain.
        /// </summary>
        public void ExpandDomainWithBox(
            StaticCompoundShape host,
            Vector3 centre,
            Quaternion rotation,
            Vector3 halfExtents,
            Vector3 scale,
            float extraPadding = 0.05f)
        {
            if (host == null)
                return;
            Vector3 localMin = new Vector3(-halfExtents.X, -halfExtents.Y, -halfExtents.Z);
            Vector3 localMax = halfExtents;
            GetTransformedAabb(localMin, localMax, centre, rotation, scale, out Vector3 wMin, out Vector3 wMax);
            Vector3 pad = new Vector3(extraPadding, extraPadding, extraPadding);
            ExpandDomain(host, wMin - pad, wMax + pad);
        }

        const int BoxShapeObjectSize32 = 48;
        const int BoxShapeObjectSize64 = 64;
        const int BoxShapeHalfExtentsOffset32 = 32;
        const int BoxShapeHalfExtentsOffset64 = 48;

        int BoxShapeObjectSize => Header.PointerSize == 8 ? BoxShapeObjectSize64 : BoxShapeObjectSize32;
        int BoxShapeHalfExtentsOffset => Header.PointerSize == 8 ? BoxShapeHalfExtentsOffset64 : BoxShapeHalfExtentsOffset32;

        /// <summary>Read <c>hkpBoxShape.halfExtents</c> (xyz) from a shape object in the data payload.</summary>
        public bool TryGetBoxHalfExtents(uint dataOffset, out Vector3 halfExtents)
        {
            halfExtents = Vector3.Zero;
            int o = (int)dataOffset + BoxShapeHalfExtentsOffset;
            if (o + 16 > DataPayload.Length)
                return false;
            halfExtents = new Vector3(
                BitConverter.ToSingle(DataPayload, o),
                BitConverter.ToSingle(DataPayload, o + 4),
                BitConverter.ToSingle(DataPayload, o + 8));
            return halfExtents.X > 0 || halfExtents.Y > 0 || halfExtents.Z > 0;
        }

        /// <summary>
        /// Clone an existing <c>hkpBoxShape</c> blob, patch <c>halfExtents</c>, and register it as a new object.
        /// Returns the new shape's data offset for use as <see cref="CompoundInstance.ShapeDataOffset"/>.
        /// Object size / halfExtents field offset differ between 32-bit (48 / +32) and 64-bit (64 / +48) packs.
        /// </summary>
        public uint AppendBoxShape(Vector3 halfExtents)
        {
            PackfileObject template = null;
            for (int i = 0; i < Objects.Count; i++)
            {
                if (Objects[i].Class == ObjectClass.BoxShape)
                {
                    template = Objects[i];
                    break;
                }
            }
            if (template == null)
                throw new InvalidOperationException("No existing hkpBoxShape to clone — packfile has no box templates.");

            int boxSize = BoxShapeObjectSize;
            int heOff = BoxShapeHalfExtentsOffset;
            int src = (int)template.DataOffset;
            if (src < 0 || src + boxSize > DataPayload.Length)
                throw new InvalidOperationException("Template hkpBoxShape object is truncated.");

            int dst = AlignPayload(DataPayload.Length, 16);
            byte[] grown = new byte[dst + boxSize];
            Buffer.BlockCopy(DataPayload, 0, grown, 0, DataPayload.Length);
            Buffer.BlockCopy(DataPayload, src, grown, dst, boxSize);
            DataPayload = grown;

            float hx = Math.Abs(halfExtents.X);
            float hy = Math.Abs(halfExtents.Y);
            float hz = Math.Abs(halfExtents.Z);
            if (hx < 1e-4f) hx = 1e-4f;
            if (hy < 1e-4f) hy = 1e-4f;
            if (hz < 1e-4f) hz = 1e-4f;
            float hw = Math.Min(hx, Math.Min(hy, hz));
            WriteVector4(DataPayload, dst + heOff, new Vector4(hx, hy, hz, hw));

            uint dataOffset = (uint)dst;
            // VirtualFixup.SectionIndex is the *classnames* section (0), not the data section (2).
            // Retail boxes all use Sec=0; Sec=2 makes Havok fail to resolve the class → shape=null.
            uint classSection = 0;
            for (int i = 0; i < VirtualFixups.Count; i++)
            {
                if (VirtualFixups[i].Src == template.DataOffset)
                {
                    classSection = VirtualFixups[i].SectionIndex;
                    break;
                }
            }
            VirtualFixups.Add(new VirtualFixup
            {
                Src = dataOffset,
                SectionIndex = classSection,
                NameOffset = template.ClassNameOffset,
            });
            Objects.Add(new PackfileObject
            {
                DataOffset = dataOffset,
                ClassNameOffset = template.ClassNameOffset,
                ClassName = "hkpBoxShape",
                Class = ObjectClass.BoxShape,
                ProxyIndex = -1,
            });
            return dataOffset;
        }

        static void GetTransformedAabb(
            Vector3 localMin,
            Vector3 localMax,
            Vector3 translation,
            Quaternion rotation,
            Vector3 scale,
            out Vector3 worldMin,
            out Vector3 worldMax)
        {
            // System.Numerics uses row vectors (v * M). Compose S * R * T so local corners are
            // scaled, then rotated, then translated. T * R * S (column-vector TRS) wrongly
            // rotates around the world origin after translating and inflates host domains /
            // BVH leaves — mid-phase then misses walkable geometry.
            worldMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            worldMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = new Vector3(
                    (corner & 1) == 0 ? localMin.X : localMax.X,
                    (corner & 2) == 0 ? localMin.Y : localMax.Y,
                    (corner & 4) == 0 ? localMin.Z : localMax.Z);
                Vector3 world = translation + Vector3.Transform(local * scale, rotation);
                worldMin = Vector3.Min(worldMin, world);
                worldMax = Vector3.Max(worldMax, world);
            }
        }

        /// <summary>
        /// Remove one instance from a static compound by index (COLLISION.MAP Index).
        /// Remaining instances keep relative order; indexes above the removed slot shift down by 1
        /// (callers must remap any MAP rows that pointed at the same proxy with Index &gt; removed).
        /// Rebuilds the compound BVH. Returns false if the proxy/index was not found.
        /// </summary>
        public bool RemoveInstance(int proxyIndex, int instanceIndex)
        {
            StaticCompoundShape compound = FindCompound(proxyIndex);
            if (compound == null)
                return false;
            if (instanceIndex < 0 || instanceIndex >= compound.Instances.Count)
                return false;

            ScrubShapeFixupsForInstances(compound);
            compound.Instances.RemoveAt(instanceIndex);
            RewriteCompoundArrays(compound);
            return true;
        }

        /// <summary>
        /// Remove multiple instances from a compound in one rewrite. <paramref name="instanceIndexes"/>
        /// may be unsorted / contain duplicates (duplicates ignored). Higher surviving indexes shift
        /// down by the number of removed slots below them — remap MAP rows accordingly.
        /// Returns the number of instances actually removed.
        /// </summary>
        public int RemoveInstances(int proxyIndex, IEnumerable<int> instanceIndexes)
        {
            StaticCompoundShape compound = FindCompound(proxyIndex);
            if (compound == null || instanceIndexes == null)
                return 0;

            var remove = new HashSet<int>();
            foreach (int idx in instanceIndexes)
            {
                if (idx >= 0 && idx < compound.Instances.Count)
                    remove.Add(idx);
            }
            if (remove.Count == 0)
                return 0;

            ScrubShapeFixupsForInstances(compound);
            var kept = new List<CompoundInstance>(compound.Instances.Count - remove.Count);
            for (int i = 0; i < compound.Instances.Count; i++)
            {
                if (!remove.Contains(i))
                    kept.Add(compound.Instances[i]);
            }
            int removed = compound.Instances.Count - kept.Count;
            compound.Instances = kept;
            RewriteCompoundArrays(compound);
            return removed;
        }

        /// <summary>
        /// Drop global fixups for the current instance shape-pointer fields before rewriting arrays,
        /// so orphaned slots do not keep stale links.
        /// </summary>
        void ScrubShapeFixupsForInstances(StaticCompoundShape compound)
        {
            if (compound?.Instances == null || compound.Instances.Count == 0)
                return;
            var scrub = new HashSet<uint>();
            for (int i = 0; i < compound.Instances.Count; i++)
                scrub.Add(compound.Instances[i].DataOffset + 48);

            for (int g = GlobalFixups.Count - 1; g >= 0; g--)
            {
                if (scrub.Contains(GlobalFixups[g].Src))
                    GlobalFixups.RemoveAt(g);
            }
        }

        /// <summary>Find the static compound for a COLLISION.MAP CollisionProxyIndex, or null.</summary>
        public StaticCompoundShape GetCompound(int proxyIndex)
        {
            for (int i = 0; i < StaticCompoundShapes.Count; i++)
            {
                if (StaticCompoundShapes[i].ProxyIndex == proxyIndex)
                    return StaticCompoundShapes[i];
            }
            return null;
        }

        /// <summary>Find the physics system for a PHYSICS.MAP / DYNAMIC_PHYSICS_SYSTEM SystemIndex, or null.</summary>
        public PhysicsSystem GetPhysicsSystem(int systemIndex)
        {
            if (systemIndex < 0 || systemIndex >= PhysicsSystems.Count)
                return null;
            return PhysicsSystems[systemIndex];
        }

        StaticCompoundShape FindCompound(int proxyIndex) => GetCompound(proxyIndex);

        StaticCompoundShape _worldHostPrimary;
        StaticCompoundShape _worldHostSecondary;
        bool _worldHostsResolved;

        /// <summary>
        /// Compound holding placed non-walkable colliders (ballistic). Placed geometry lives on
        /// these two "world host" compounds rather than on the per-mesh template compounds, which
        /// stay as identity shape definitions.
        /// </summary>
        public StaticCompoundShape WorldHostPrimary { get { ResolveWorldHosts(); return _worldHostPrimary; } }

        /// <summary>Compound holding placed walkable (WORLD-flagged) colliders and barrier boxes.</summary>
        public StaticCompoundShape WorldHostSecondary { get { ResolveWorldHosts(); return _worldHostSecondary; } }

        /// <summary>
        /// Pick the host a COLLISION.MAP row belongs to. The WORLD flag selects the walkable host;
        /// this is what makes a row's Index unambiguous without storing the compound alongside it.
        /// </summary>
        public StaticCompoundShape WorldHostFor(bool world)
        {
            ResolveWorldHosts();
            return world && _worldHostSecondary != null ? _worldHostSecondary : _worldHostPrimary;
        }

        /// <summary>
        /// Identify the world hosts as the largest compounds whose instances mostly reference other
        /// compounds (Torrens: ~2623 + ~1033). Cached, because rebuilding empties them.
        /// </summary>
        void ResolveWorldHosts()
        {
            if (_worldHostsResolved)
                return;
            _worldHostsResolved = true;

            List<StaticCompoundShape> hosts = StaticCompoundShapes
                .Where(c => c != null && c.Instances != null && c.Instances.Count >= 16)
                .Where(c => c.Instances.Count(i =>
                    string.Equals(i.ShapeClassName, "hkpStaticCompoundShape", StringComparison.Ordinal))
                    > c.Instances.Count / 2)
                .OrderByDescending(c => c.Instances.Count)
                .ToList();

            if (hosts.Count == 0)
                return;
            _worldHostPrimary = hosts[0];
            _worldHostSecondary = hosts.Count > 1 ? hosts[1] : hosts[0];
        }

        #region CROSS_PACKFILE_IMPORT
        /// <summary>
        /// Import a static compound (and its referenced shapes) from another same-pointer-size packfile.
        /// <paramref name="remapCache"/> maps source DataOffset → dest DataOffset for reuse within one port.
        /// </summary>
        public StaticCompoundShape ImportStaticCompoundShape(
            HavokPackfile source,
            StaticCompoundShape src,
            Dictionary<uint, uint> remapCache = null)
        {
            if (source == null || src == null)
                return null;
            if (Header.PointerSize != source.Header.PointerSize)
                throw new InvalidOperationException("Cannot import Havok data between packfiles with different pointer sizes (HKX vs HKX64).");

            if (remapCache != null && remapCache.TryGetValue(src.DataOffset, out uint existing))
            {
                for (int i = 0; i < StaticCompoundShapes.Count; i++)
                {
                    if (StaticCompoundShapes[i].DataOffset == existing)
                        return StaticCompoundShapes[i];
                }
            }

            uint newRoot = ImportObjectGraph(source, src.DataOffset, remapCache);
            RebuildTypedViewsFromObjects();

            for (int i = 0; i < StaticCompoundShapes.Count; i++)
            {
                if (StaticCompoundShapes[i].DataOffset == newRoot)
                    return StaticCompoundShapes[i];
            }
            return null;
        }

        /// <summary>
        /// Import a physics system (and its referenced rigid bodies / shapes) from another same-pointer-size packfile.
        /// Registers the system in <c>hkpPhysicsData.systems[]</c>.
        /// </summary>
        public PhysicsSystem ImportPhysicsSystem(
            HavokPackfile source,
            PhysicsSystem src,
            Dictionary<uint, uint> remapCache = null)
        {
            if (source == null || src == null)
                return null;
            if (Header.PointerSize != source.Header.PointerSize)
                throw new InvalidOperationException("Cannot import Havok data between packfiles with different pointer sizes (HKX vs HKX64).");

            if (remapCache != null && remapCache.TryGetValue(src.DataOffset, out uint existing))
            {
                for (int i = 0; i < PhysicsSystems.Count; i++)
                {
                    if (PhysicsSystems[i].DataOffset == existing)
                        return PhysicsSystems[i];
                }
            }

            uint newRoot = ImportObjectGraph(source, src.DataOffset, remapCache);
            AppendPhysicsSystemToPhysicsData(newRoot);
            RebuildTypedViewsFromObjects();

            for (int i = 0; i < PhysicsSystems.Count; i++)
            {
                if (PhysicsSystems[i].DataOffset == newRoot)
                    return PhysicsSystems[i];
            }
            return null;
        }

        /// <summary>
        /// Copy the object subgraph rooted at <paramref name="sourceRootOffset"/> into this packfile.
        /// Returns the new root data offset.
        /// </summary>
        public uint ImportObjectGraph(HavokPackfile source, uint sourceRootOffset, Dictionary<uint, uint> remapCache = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (Header.PointerSize != source.Header.PointerSize)
                throw new InvalidOperationException("Pointer size mismatch.");

            if (remapCache != null && remapCache.TryGetValue(sourceRootOffset, out uint cached))
                return cached;

            CollectReachable(source, sourceRootOffset, out HashSet<uint> objectOffsets, out List<(uint Start, uint End)> extraRanges);
            if (objectOffsets.Count == 0)
                throw new InvalidOperationException("Source Havok object graph is empty.");

            // Object byte ranges (exclusive end = next object offset or payload end).
            List<uint> sortedSrcObjects = source.Objects.Select(o => o.DataOffset).Distinct().OrderBy(o => o).ToList();
            var objectRanges = new List<(uint Start, uint End, PackfileObject Obj)>();
            for (int i = 0; i < source.Objects.Count; i++)
            {
                PackfileObject obj = source.Objects[i];
                if (!objectOffsets.Contains(obj.DataOffset))
                    continue;
                uint end = (uint)source.DataPayload.Length;
                for (int s = 0; s < sortedSrcObjects.Count; s++)
                {
                    if (sortedSrcObjects[s] > obj.DataOffset && sortedSrcObjects[s] < end)
                        end = sortedSrcObjects[s];
                }
                objectRanges.Add((obj.DataOffset, end, obj));
            }
            objectRanges.Sort((a, b) => a.Start.CompareTo(b.Start));

            // Merge extra ranges and clamp so they do not overlap object blobs we already copy.
            var extras = MergeRanges(extraRanges);
            extras = SubtractObjectRanges(extras, objectRanges);

            // Destination allocation map: source absolute offset → dest absolute offset (for any copied byte).
            var srcToDst = new Dictionary<uint, uint>();
            int writeAt = AlignPayload(DataPayload.Length, 16);
            var pieces = new List<(uint SrcStart, int Length, int DstStart)>();

            void Schedule(uint srcStart, uint srcEnd)
            {
                if (srcEnd <= srcStart) return;
                int len = (int)(srcEnd - srcStart);
                writeAt = AlignPayload(writeAt, 16);
                pieces.Add((srcStart, len, writeAt));
                for (uint o = srcStart; o < srcEnd; o++)
                    srcToDst[o] = (uint)(writeAt + (int)(o - srcStart));
                writeAt += len;
            }

            for (int i = 0; i < objectRanges.Count; i++)
                Schedule(objectRanges[i].Start, objectRanges[i].End);
            for (int i = 0; i < extras.Count; i++)
                Schedule(extras[i].Start, extras[i].End);

            if (pieces.Count == 0)
                throw new InvalidOperationException("Nothing to copy from source Havok graph.");

            byte[] grown = new byte[writeAt];
            Buffer.BlockCopy(DataPayload, 0, grown, 0, DataPayload.Length);
            for (int i = 0; i < pieces.Count; i++)
            {
                Buffer.BlockCopy(source.DataPayload, (int)pieces[i].SrcStart, grown, pieces[i].DstStart, pieces[i].Length);
            }
            DataPayload = grown;

            // Classnames
            var destClassByName = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in ParseClassNames(ClassnamesData))
                destClassByName[kv.Value] = kv.Key;

            for (int i = 0; i < objectRanges.Count; i++)
            {
                PackfileObject srcObj = objectRanges[i].Obj;
                int nameOff = EnsureClassName(source, srcObj, destClassByName);
                uint newOff = srcToDst[srcObj.DataOffset];

                VirtualFixups.Add(new VirtualFixup
                {
                    Src = newOff,
                    SectionIndex = 0,
                    NameOffset = nameOff,
                });
                Objects.Add(new PackfileObject
                {
                    DataOffset = newOff,
                    ClassNameOffset = nameOff,
                    ClassName = srcObj.ClassName,
                    Class = srcObj.Class,
                    ProxyIndex = -1,
                });

                if (remapCache != null)
                    remapCache[srcObj.DataOffset] = newOff;
            }

            // Remap fixups whose source falls in copied regions.
            for (int i = 0; i < source.LocalFixups.Count; i++)
            {
                LocalFixup lf = source.LocalFixups[i];
                if (!srcToDst.TryGetValue(lf.Src, out uint newSrc))
                    continue;
                if (!srcToDst.TryGetValue(lf.Dst, out uint newDst))
                    continue;
                LocalFixups.Add(new LocalFixup { Src = newSrc, Dst = newDst });
            }
            for (int i = 0; i < source.GlobalFixups.Count; i++)
            {
                GlobalFixup gf = source.GlobalFixups[i];
                if (!srcToDst.TryGetValue(gf.Src, out uint newSrc))
                    continue;
                if (!srcToDst.TryGetValue(gf.Dst, out uint newDst))
                    continue;
                GlobalFixups.Add(new GlobalFixup
                {
                    Src = newSrc,
                    DstSectionIndex = gf.DstSectionIndex,
                    Dst = newDst,
                });
            }

            if (!srcToDst.TryGetValue(sourceRootOffset, out uint newRoot))
                throw new InvalidOperationException("Failed to remap imported Havok root.");
            if (remapCache != null)
                remapCache[sourceRootOffset] = newRoot;
            return newRoot;
        }

        void CollectReachable(
            HavokPackfile source,
            uint root,
            out HashSet<uint> objectOffsets,
            out List<(uint Start, uint End)> extraRanges)
        {
            objectOffsets = new HashSet<uint>();
            extraRanges = new List<(uint Start, uint End)>();

            var objectsByOffset = new Dictionary<uint, PackfileObject>();
            for (int i = 0; i < source.Objects.Count; i++)
                objectsByOffset[source.Objects[i].DataOffset] = source.Objects[i];

            List<uint> sorted = objectsByOffset.Keys.OrderBy(o => o).ToList();
            uint ObjectEnd(uint off)
            {
                uint end = (uint)source.DataPayload.Length;
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (sorted[i] > off && sorted[i] < end)
                        end = sorted[i];
                }
                return end;
            }

            var queue = new Queue<uint>();
            if (objectsByOffset.ContainsKey(root))
                queue.Enqueue(root);

            // Seed compound instance shapes even if root offset lookup failed somehow.
            for (int c = 0; c < source.StaticCompoundShapes.Count; c++)
            {
                if (source.StaticCompoundShapes[c].DataOffset != root)
                    continue;
                if (objectsByOffset.ContainsKey(root))
                    queue.Enqueue(root);
                break;
            }

            while (queue.Count > 0)
            {
                uint off = queue.Dequeue();
                if (!objectsByOffset.ContainsKey(off) || !objectOffsets.Add(off))
                    continue;

                uint end = ObjectEnd(off);

                for (int g = 0; g < source.GlobalFixups.Count; g++)
                {
                    GlobalFixup gf = source.GlobalFixups[g];
                    if (gf.Src >= off && gf.Src < end && objectsByOffset.ContainsKey(gf.Dst))
                        queue.Enqueue(gf.Dst);
                }

                for (int l = 0; l < source.LocalFixups.Count; l++)
                {
                    LocalFixup lf = source.LocalFixups[l];
                    if (lf.Src < off || lf.Src >= end)
                        continue;

                    int bytes = InferArrayByteLength(source, lf.Src, lf.Dst);
                    if (bytes > 0)
                        extraRanges.Add((lf.Dst, lf.Dst + (uint)bytes));

                    // Globals inside the array region → more objects.
                    uint arrEnd = lf.Dst + (uint)Math.Max(bytes, 0);
                    if (bytes <= 0)
                        arrEnd = lf.Dst + 256; // small probe window
                    for (int g = 0; g < source.GlobalFixups.Count; g++)
                    {
                        GlobalFixup gf = source.GlobalFixups[g];
                        if (gf.Src >= lf.Dst && gf.Src < arrEnd && objectsByOffset.ContainsKey(gf.Dst))
                            queue.Enqueue(gf.Dst);
                    }
                }

                // Typed compound shapes
                for (int c = 0; c < source.StaticCompoundShapes.Count; c++)
                {
                    StaticCompoundShape compound = source.StaticCompoundShapes[c];
                    if (compound.DataOffset != off)
                        continue;
                    for (int n = 0; n < compound.Instances.Count; n++)
                    {
                        uint shape = compound.Instances[n].ShapeDataOffset;
                        if (shape != 0 && objectsByOffset.ContainsKey(shape))
                            queue.Enqueue(shape);
                    }
                }
            }

            // Expand extras for nested local fixups (mesh internal arrays / strings).
            int expandGuard = 0;
            bool expanded;
            do
            {
                expanded = false;
                if (++expandGuard > 64)
                    break;
                var snapshot = new List<(uint Start, uint End)>(extraRanges);
                for (int i = 0; i < snapshot.Count; i++)
                {
                    uint start = snapshot[i].Start;
                    uint end = snapshot[i].End;
                    for (int l = 0; l < source.LocalFixups.Count; l++)
                    {
                        LocalFixup lf = source.LocalFixups[l];
                        if (lf.Src < start || lf.Src >= end)
                            continue;
                        int bytes = InferArrayByteLength(source, lf.Src, lf.Dst);
                        if (bytes <= 0)
                        {
                            // C-string or raw blob: copy until NUL or next object.
                            int strEnd = (int)lf.Dst;
                            while (strEnd < source.DataPayload.Length && source.DataPayload[strEnd] != 0)
                                strEnd++;
                            if (strEnd < source.DataPayload.Length)
                                strEnd++; // include NUL
                            bytes = Math.Max(1, strEnd - (int)lf.Dst);
                        }
                        uint newEnd = lf.Dst + (uint)bytes;
                        extraRanges.Add((lf.Dst, newEnd));
                        expanded = true;

                        for (int g = 0; g < source.GlobalFixups.Count; g++)
                        {
                            GlobalFixup gf = source.GlobalFixups[g];
                            if (gf.Src >= lf.Dst && gf.Src < newEnd && objectsByOffset.ContainsKey(gf.Dst)
                                && objectOffsets.Add(gf.Dst))
                            {
                                queue.Enqueue(gf.Dst);
                                expanded = true;
                            }
                        }
                    }
                }
                while (queue.Count > 0)
                {
                    uint off = queue.Dequeue();
                    // Re-run object expansion lightly
                    if (!objectsByOffset.ContainsKey(off))
                        continue;
                    uint oEnd = ObjectEnd(off);
                    for (int g = 0; g < source.GlobalFixups.Count; g++)
                    {
                        GlobalFixup gf = source.GlobalFixups[g];
                        if (gf.Src >= off && gf.Src < oEnd && objectsByOffset.ContainsKey(gf.Dst)
                            && objectOffsets.Add(gf.Dst))
                        {
                            queue.Enqueue(gf.Dst);
                            expanded = true;
                        }
                    }
                    for (int l = 0; l < source.LocalFixups.Count; l++)
                    {
                        LocalFixup lf = source.LocalFixups[l];
                        if (lf.Src < off || lf.Src >= oEnd)
                            continue;
                        int bytes = InferArrayByteLength(source, lf.Src, lf.Dst);
                        if (bytes > 0)
                        {
                            extraRanges.Add((lf.Dst, lf.Dst + (uint)bytes));
                            expanded = true;
                        }
                    }
                }
            } while (expanded);
        }

        static int InferArrayByteLength(HavokPackfile source, uint arrayFieldSrc, uint arrayDataDst)
        {
            int ptrSize = source.Header.PointerSize;
            int sizePos = (int)arrayFieldSrc + ptrSize;
            if (sizePos + 4 > source.DataPayload.Length)
                return 0;
            int count = BitConverter.ToInt32(source.DataPayload, sizePos);
            if (count <= 0 || count > 1_000_000)
                return 0;

            // Prefer stride implied by consecutive global fixups in the array.
            uint first = 0;
            uint second = 0;
            int found = 0;
            for (int g = 0; g < source.GlobalFixups.Count; g++)
            {
                uint src = source.GlobalFixups[g].Src;
                if (src < arrayDataDst)
                    continue;
                if (found == 0) { first = src; found = 1; }
                else if (found == 1 && src > first)
                {
                    second = src;
                    found = 2;
                    break;
                }
            }
            if (found == 2)
            {
                uint stride = second - first;
                if (stride > 0 && stride <= 512)
                    return count * (int)stride;
            }

            // Compound instance arrays
            int instanceStride = ptrSize == 8 ? 80 : 64;
            if (count * instanceStride < source.DataPayload.Length)
            {
                // If array field is the instances field of a compound, instance stride fits.
                for (int c = 0; c < source.StaticCompoundShapes.Count; c++)
                {
                    StaticCompoundShape compound = source.StaticCompoundShapes[c];
                    int instancesArrayOffset = ptrSize == 8 ? 0x38 : 0x20;
                    if (arrayFieldSrc == compound.DataOffset + (uint)instancesArrayOffset)
                        return count * instanceStride;
                }
            }

            return count * ptrSize;
        }

        static List<(uint Start, uint End)> MergeRanges(List<(uint Start, uint End)> ranges)
        {
            if (ranges == null || ranges.Count == 0)
                return new List<(uint Start, uint End)>();
            var sorted = ranges.Where(r => r.End > r.Start).OrderBy(r => r.Start).ToList();
            var merged = new List<(uint Start, uint End)>();
            uint start = sorted[0].Start, end = sorted[0].End;
            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i].Start <= end)
                    end = Math.Max(end, sorted[i].End);
                else
                {
                    merged.Add((start, end));
                    start = sorted[i].Start;
                    end = sorted[i].End;
                }
            }
            merged.Add((start, end));
            return merged;
        }

        static List<(uint Start, uint End)> SubtractObjectRanges(
            List<(uint Start, uint End)> extras,
            List<(uint Start, uint End, PackfileObject Obj)> objects)
        {
            var result = new List<(uint Start, uint End)>();
            for (int i = 0; i < extras.Count; i++)
            {
                uint start = extras[i].Start;
                uint end = extras[i].End;
                var pieces = new List<(uint Start, uint End)> { (start, end) };
                for (int o = 0; o < objects.Count; o++)
                {
                    uint os = objects[o].Start, oe = objects[o].End;
                    var next = new List<(uint Start, uint End)>();
                    for (int p = 0; p < pieces.Count; p++)
                    {
                        uint ps = pieces[p].Start, pe = pieces[p].End;
                        if (oe <= ps || os >= pe)
                        {
                            next.Add((ps, pe));
                            continue;
                        }
                        if (ps < os)
                            next.Add((ps, os));
                        if (oe < pe)
                            next.Add((oe, pe));
                    }
                    pieces = next;
                }
                result.AddRange(pieces);
            }
            return MergeRanges(result);
        }

        int EnsureClassName(HavokPackfile source, PackfileObject srcObj, Dictionary<string, int> destClassByName)
        {
            string name = srcObj.ClassName ?? "";
            if (destClassByName.TryGetValue(name, out int existing))
                return existing;

            // Copy the 5-byte prefix (u32 sig + u8) + name + NUL from the source classnames blob.
            int srcNameOff = srcObj.ClassNameOffset;
            int srcEntryStart = srcNameOff - 5;
            if (srcEntryStart < 0 || srcNameOff >= source.ClassnamesData.Length)
                throw new InvalidOperationException("Invalid source classname offset for " + name);

            int srcEnd = srcNameOff;
            while (srcEnd < source.ClassnamesData.Length && source.ClassnamesData[srcEnd] != 0)
                srcEnd++;
            if (srcEnd < source.ClassnamesData.Length)
                srcEnd++; // NUL

            int copyLen = srcEnd - srcEntryStart;
            int destEntryStart = ClassnamesData.Length;
            // Align not required for classnames stream; append raw.
            byte[] grown = new byte[destEntryStart + copyLen];
            Buffer.BlockCopy(ClassnamesData, 0, grown, 0, ClassnamesData.Length);
            Buffer.BlockCopy(source.ClassnamesData, srcEntryStart, grown, destEntryStart, copyLen);
            ClassnamesData = grown;

            int newNameOff = destEntryStart + 5;
            destClassByName[name] = newNameOff;
            return newNameOff;
        }

        void AppendPhysicsSystemToPhysicsData(uint systemDataOffset)
        {
            PackfileObject physicsData = null;
            for (int i = 0; i < Objects.Count; i++)
            {
                if (Objects[i].Class == ObjectClass.PhysicsData)
                {
                    physicsData = Objects[i];
                    break;
                }
            }
            if (physicsData == null)
                return;

            int ptrSize = Header.PointerSize;
            uint systemsField = physicsData.DataOffset + 16 + (uint)ptrSize;
            TryReadPointerArray(systemsField, out List<uint> existing);
            if (existing == null)
                existing = new List<uint>();

            // Avoid duplicating the same system pointer.
            for (int i = 0; i < existing.Count; i++)
            {
                if (existing[i] == systemDataOffset)
                    return;
            }
            existing.Add(systemDataOffset);

            // Scrub previous systems-array storage fixups.
            uint oldArr = 0;
            bool hadOldArr = false;
            int oldCount = 0;
            for (int i = 0; i < LocalFixups.Count; i++)
            {
                if (LocalFixups[i].Src == systemsField)
                {
                    oldArr = LocalFixups[i].Dst;
                    hadOldArr = true;
                    break;
                }
            }
            int sizePos = (int)systemsField + ptrSize;
            if (sizePos + 4 <= DataPayload.Length)
                oldCount = BitConverter.ToInt32(DataPayload, sizePos);
            if (oldCount < 0) oldCount = 0;

            for (int i = LocalFixups.Count - 1; i >= 0; i--)
            {
                if (LocalFixups[i].Src == systemsField)
                    LocalFixups.RemoveAt(i);
            }
            if (hadOldArr)
            {
                uint oldEnd = oldArr + (uint)(oldCount * ptrSize);
                for (int g = GlobalFixups.Count - 1; g >= 0; g--)
                {
                    uint src = GlobalFixups[g].Src;
                    if (src >= oldArr && src < oldEnd)
                        GlobalFixups.RemoveAt(g);
                }
            }

            int count = existing.Count;
            int newArrOff = AlignPayload(DataPayload.Length, 16);
            int arrBytes = count * ptrSize;
            byte[] grown = new byte[newArrOff + arrBytes];
            Buffer.BlockCopy(DataPayload, 0, grown, 0, DataPayload.Length);
            DataPayload = grown;

            LocalFixups.Add(new LocalFixup { Src = systemsField, Dst = (uint)newArrOff });
            WriteUInt32(DataPayload, (int)systemsField + ptrSize, (uint)count);
            WriteUInt32(DataPayload, (int)systemsField + ptrSize + 4, (uint)count | 0x80000000u);

            for (int n = 0; n < count; n++)
            {
                uint slot = (uint)(newArrOff + n * ptrSize);
                GlobalFixups.Add(new GlobalFixup
                {
                    Src = slot,
                    DstSectionIndex = 2,
                    Dst = existing[n],
                });
            }
        }

        void RebuildTypedViewsFromObjects()
        {
            // Reassign compound ordinals in virtual-fixup / Objects order.
            int compoundOrdinal = 0;
            for (int i = 0; i < Objects.Count; i++)
            {
                if (Objects[i].Class == ObjectClass.StaticCompoundShape)
                    Objects[i].ProxyIndex = compoundOrdinal++;
                else if (Objects[i].Class != ObjectClass.PhysicsSystem)
                    Objects[i].ProxyIndex = -1;
            }

            StaticCompoundShapes.Clear();
            PhysicsSystems.Clear();
            for (int i = 0; i < Objects.Count; i++)
            {
                PackfileObject obj = Objects[i];
                if (obj.Class == ObjectClass.PhysicsSystem)
                {
                    PhysicsSystems.Add(new PhysicsSystem
                    {
                        SystemIndex = -1,
                        DataOffset = obj.DataOffset,
                        Object = obj,
                    });
                }
            }

            Dictionary<int, string> classNames = ParseClassNames(ClassnamesData);
            ParseStaticCompoundInstances(classNames);
            ParsePhysicsSystemIndexes();
            _worldHostPrimary = null;
            _worldHostSecondary = null;
        }
        #endregion

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            Objects.Clear();
            StaticCompoundShapes.Clear();
            PhysicsSystems.Clear();
            LocalFixups.Clear();
            GlobalFixups.Clear();
            VirtualFixups.Clear();

            byte[] file = stream.ToArray();
            if (file.Length < 0x40)
                return false;

            using (BinaryReader reader = new BinaryReader(new MemoryStream(file)))
            {
                if (!ReadHeader(reader))
                    return false;

                // Section headers (0x30 each) start at 0x40:
                //   char[16] tag | uint32 marker(0xFF000000) | abs | local | global | virtual | exports | imports | end
                reader.BaseStream.Position = 0x40;
                uint classAbs = 0, classLocal = 0;
                uint typesAbs = 0, typesLocal = 0;
                uint dataAbs = 0, dataLocal = 0, dataGlobal = 0, dataVirtual = 0, dataExports = 0;

                for (int i = 0; i < Header.NumSections; i++)
                {
                    string name = ReadSectionTag(reader);
                    reader.ReadUInt32(); // marker 0xFF000000
                    uint abs = reader.ReadUInt32();
                    uint local = reader.ReadUInt32();
                    uint global = reader.ReadUInt32();
                    uint virt = reader.ReadUInt32();
                    uint exports = reader.ReadUInt32();
                    reader.ReadUInt32(); // imports
                    reader.ReadUInt32(); // end

                    if (name == "__classnames__")
                    {
                        classAbs = abs; classLocal = local;
                    }
                    else if (name == "__types__")
                    {
                        typesAbs = abs; typesLocal = local;
                    }
                    else if (name == "__data__")
                    {
                        dataAbs = abs; dataLocal = local; dataGlobal = global;
                        dataVirtual = virt; dataExports = exports;
                    }
                }

                ClassnamesData = Slice(file, (int)classAbs, (int)classLocal);
                TypesData = typesLocal == 0 ? Array.Empty<byte>() : Slice(file, (int)typesAbs, (int)typesLocal);
                DataPayload = Slice(file, (int)dataAbs, (int)dataLocal);
                _dataSectionOffset = dataAbs;

                ReadLocalFixups(file, (int)(dataAbs + dataLocal), (int)(dataAbs + dataGlobal));
                ReadGlobalFixups(file, (int)(dataAbs + dataGlobal), (int)(dataAbs + dataVirtual));
                ReadVirtualFixups(file, (int)(dataAbs + dataVirtual), (int)(dataAbs + dataExports));

                Dictionary<int, string> classNames = ParseClassNames(ClassnamesData);
                BuildObjectList(classNames);
                ParseStaticCompoundInstances(classNames);
                ParsePhysicsSystemIndexes();
            }

            return Objects.Count > 0;
        }

        override protected bool SaveInternal()
        {
            if (DataPayload == null || ClassnamesData == null)
                return false;

            // Apply any in-place instance edits back into the data payload before writing.
            WriteBackCompoundInstances();

            int headerSize = 0x40;
            int sectionHeaderSize = Header.NumSections * 0x30;
            int classAbs = headerSize + sectionHeaderSize;
            int dataAbs = classAbs + ClassnamesData.Length;

            int localBytes = LocalFixups.Count * 8;
            int globalBytes = GlobalFixups.Count * 12;
            int virtualBytes = VirtualFixups.Count * 12;
            const int virtualTerminatorBytes = 4; // trailing 0xFFFFFFFF

            int dataLocal = DataPayload.Length;
            int dataGlobal = dataLocal + localBytes;
            int dataVirtual = dataGlobal + globalBytes;
            int dataExports = dataVirtual + virtualBytes + virtualTerminatorBytes;
            int dataEnd = dataExports; // no exports/imports on AI collision files

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                WriteHeader(writer);

                // __classnames__
                WriteSectionHeader(writer, "__classnames__", (uint)classAbs,
                    (uint)ClassnamesData.Length, (uint)ClassnamesData.Length, (uint)ClassnamesData.Length,
                    (uint)ClassnamesData.Length, (uint)ClassnamesData.Length, (uint)ClassnamesData.Length);

                // __types__ (empty placeholder; AbsoluteDataStart == dataAbs)
                WriteSectionHeader(writer, "__types__", (uint)dataAbs, 0, 0, 0, 0, 0, 0);

                // __data__
                WriteSectionHeader(writer, "__data__", (uint)dataAbs,
                    (uint)dataLocal, (uint)dataGlobal, (uint)dataVirtual,
                    (uint)dataExports, (uint)dataEnd, (uint)dataEnd);

                writer.Write(ClassnamesData);
                writer.Write(DataPayload);

                // Retail / Havok writers emit fixups ordered by destination (local) / source (global/virtual).
                LocalFixups.Sort((a, b) => a.Dst.CompareTo(b.Dst));
                GlobalFixups.Sort((a, b) => a.Src.CompareTo(b.Src));
                VirtualFixups.Sort((a, b) => a.Src.CompareTo(b.Src));

                for (int i = 0; i < LocalFixups.Count; i++)
                {
                    writer.Write(LocalFixups[i].Src);
                    writer.Write(LocalFixups[i].Dst);
                }
                for (int i = 0; i < GlobalFixups.Count; i++)
                {
                    writer.Write(GlobalFixups[i].Src);
                    writer.Write(GlobalFixups[i].DstSectionIndex);
                    writer.Write(GlobalFixups[i].Dst);
                }
                for (int i = 0; i < VirtualFixups.Count; i++)
                {
                    writer.Write(VirtualFixups[i].Src);
                    writer.Write(VirtualFixups[i].SectionIndex);
                    writer.Write(VirtualFixups[i].NameOffset);
                }
                writer.Write(unchecked((int)0xFFFFFFFF));

                File.WriteAllBytes(_filepath, ms.ToArray());
            }

            return true;
        }
        #endregion

        #region ANIMATION
        /* Field offsets for the animation classes. hkReferencedObject is 8 bytes on 32 bit and 16 on
         * 64 bit, pointers follow suit, and hkArray is a pointer plus size and capacity. */
        private uint ObjectHeaderSize => Header.PointerSize == 8 ? 16u : 8u;
        private uint ArraySize => (uint)Header.PointerSize + 8u;

        /// <summary>
        /// Read every <c>hkaSplineCompressedAnimation</c> in the packfile, paired with the binding
        /// that says which skeleton and which bones it drives.
        /// </summary>
        public List<AnimationClip> GetAnimations()
        {
            List<AnimationClip> clips = new List<AnimationClip>();
            if (DataPayload.Length == 0) return clips;

            //Bindings point at their animation, so index the animations by where they live
            Dictionary<uint, PackfileObject> animations = new Dictionary<uint, PackfileObject>();
            for (int i = 0; i < Objects.Count; i++)
                if (Objects[i].ClassName == "hkaSplineCompressedAnimation")
                    animations[Objects[i].DataOffset] = Objects[i];

            Dictionary<uint, uint> globalBySrc = new Dictionary<uint, uint>();
            for (int i = 0; i < GlobalFixups.Count; i++)
                globalBySrc[GlobalFixups[i].Src] = GlobalFixups[i].Dst;

            for (int i = 0; i < Objects.Count; i++)
            {
                if (Objects[i].ClassName != "hkaAnimationBinding") continue;

                AnimationClip clip = ReadBinding(Objects[i].DataOffset, globalBySrc);
                if (clip == null) continue;

                if (clip.AnimationOffset != 0 && animations.TryGetValue(clip.AnimationOffset, out PackfileObject animation))
                    ReadSplineAnimation(animation.DataOffset, clip);
                clips.Add(clip);
            }

            //Any animation without a binding is still worth reporting
            foreach (KeyValuePair<uint, PackfileObject> entry in animations)
            {
                if (clips.Any(x => x.AnimationOffset == entry.Key)) continue;
                AnimationClip clip = new AnimationClip { AnimationOffset = entry.Key };
                ReadSplineAnimation(entry.Key, clip);
                clips.Add(clip);
            }
            return clips;
        }

        private AnimationClip ReadBinding(uint at, Dictionary<uint, uint> globalBySrc)
        {
            uint name = at + ObjectHeaderSize;
            uint animation = name + (uint)Header.PointerSize;
            uint tracks = animation + (uint)Header.PointerSize;
            if (tracks + ArraySize > DataPayload.Length) return null;

            AnimationClip clip = new AnimationClip
            {
                SkeletonName = TryResolveLocal(name, out uint nameAt) ? ReadStringAt(nameAt) : "",
                AnimationOffset = globalBySrc.TryGetValue(animation, out uint target) ? target : 0,
            };

            //Which skeleton bone each transform track drives
            if (TryGetHkArray(tracks, out uint data, out int count))
                for (int i = 0; i < count && data + (i * 2) + 2 <= DataPayload.Length; i++)
                    clip.TrackToBone.Add(BitConverter.ToInt16(DataPayload, (int)(data + (i * 2))));

            /* Three arrays in a row - the bone indices above, then the float slots and the
             * partitions - and the blend hint is the byte straight after them. */
            uint hint = tracks + (ArraySize * 3);
            if (hint < DataPayload.Length) clip.Additive = DataPayload[hint] != 0;

            return clip;
        }

        private void ReadSplineAnimation(uint at, AnimationClip clip)
        {
            uint animation = at + ObjectHeaderSize;
            if (animation + 20 > DataPayload.Length) return;

            clip.Duration = BitConverter.ToSingle(DataPayload, (int)animation + 4);
            clip.TransformTrackCount = BitConverter.ToInt32(DataPayload, (int)animation + 8);
            clip.FloatTrackCount = BitConverter.ToInt32(DataPayload, (int)animation + 12);

            //hkaAnimation is the type, duration and two counts, then a pointer and an array
            uint spline = animation + 16 + (uint)Header.PointerSize + ArraySize;
            if (spline + 28 > DataPayload.Length) return;

            clip.FrameCount = BitConverter.ToInt32(DataPayload, (int)spline);
            clip.BlockCount = BitConverter.ToInt32(DataPayload, (int)spline + 4);
            clip.MaxFramesPerBlock = BitConverter.ToInt32(DataPayload, (int)spline + 8);
            clip.MaskAndQuantizationSize = BitConverter.ToInt32(DataPayload, (int)spline + 12);
            clip.BlockDuration = BitConverter.ToSingle(DataPayload, (int)spline + 16);
            clip.FrameDuration = BitConverter.ToSingle(DataPayload, (int)spline + 24);

            /* Then five hkArrays: the offset of each block into the stream, the same for float
             * tracks, per-track offsets for the two, and the stream itself. */
            /* Seven scalars, then the arrays - which on a 64 bit packfile have to start on an eight
             * byte boundary, so there are four bytes of padding in front of them. */
            uint arrays = spline + 28;
            if (Header.PointerSize == 8) arrays = (arrays + 7u) & ~7u;

            if (TryGetHkArray(arrays, out uint blockData, out int blockCount))
                for (int i = 0; i < blockCount && blockData + (i * 4) + 4 <= DataPayload.Length; i++)
                    clip.BlockOffsets.Add(BitConverter.ToUInt32(DataPayload, (int)(blockData + (i * 4))));

            //the float tracks live in the same stream, starting where the transform tracks stop
            if (TryGetHkArray(arrays + ArraySize, out uint floatData, out int floatCount))
                for (int i = 0; i < floatCount && floatData + (i * 4) + 4 <= DataPayload.Length; i++)
                    clip.FloatBlockOffsets.Add(BitConverter.ToUInt32(DataPayload, (int)(floatData + (i * 4))));

            if (!TryGetHkArray(arrays + (ArraySize * 4), out uint stream, out int streamLength)) return;
            clip.DataOffset = stream;
            clip.DataLength = streamLength;

            //each block opens with a four byte mask per transform track saying what it stores
            for (int b = 0; b < clip.BlockOffsets.Count; b++)
            {
                uint blockStart = stream + clip.BlockOffsets[b];
                List<TransformMask> masks = new List<TransformMask>();
                for (int t = 0; t < clip.TransformTrackCount; t++)
                {
                    uint mask = blockStart + (uint)(t * 4);
                    if (mask + 4 > DataPayload.Length) break;
                    masks.Add(new TransformMask
                    {
                        Quantization = DataPayload[mask],
                        Position = DataPayload[mask + 1],
                        Rotation = DataPayload[mask + 2],
                        Scale = DataPayload[mask + 3],
                    });
                }
                clip.Blocks.Add(masks);
            }
        }

        /// <summary>
        /// Walk one block of a clip's compressed stream and describe what each transform track holds.
        ///
        /// This reads the *structure* - which components are curves, their knot vectors and where
        /// their control points sit - and decodes the values that are stored plainly. It does not
        /// yet dequantize control points or evaluate the curves, so animated components come back
        /// as spans rather than samples. <see cref="BlockTracks.Complete"/> says whether the walk
        /// landed exactly on the end of the block, which is the check that the layout was right.
        /// </summary>
        public BlockTracks ReadBlockTracks(AnimationClip clip, int block)
        {
            BlockTracks result = new BlockTracks();
            if (clip == null || block < 0 || block >= clip.Blocks.Count || block >= clip.BlockOffsets.Count)
                return result;

            //the mask is a byte per float track on top of four per transform track, so it can land odd
            long at = Align4(clip.DataOffset + clip.BlockOffsets[block] + clip.MaskAndQuantizationSize);
            /* The float track data shares the block and starts where the transform tracks stop, but
             * its offset is measured from the block rather than from the start of the stream. */
            long end = clip.DataOffset + (block < clip.FloatBlockOffsets.Count && clip.FloatTrackCount > 0
                ? clip.BlockOffsets[block] + clip.FloatBlockOffsets[block]
                : (block + 1 < clip.BlockOffsets.Count ? clip.BlockOffsets[block + 1] : (uint)clip.DataLength));

            foreach (TransformMask mask in clip.Blocks[block])
            {
                TrackCurves track = new TrackCurves();
                result.Tracks.Add(track);
                if (at > end) return result;

                track.Position = ReadCurve(mask.Position, ScalarWidth(mask.TranslationQuantization), ref at);
                at = Align4(at);
                track.Rotation = ReadRotationCurve(mask.Rotation, RotationWidth(mask.RotationQuantization), ref at);
                at = Align4(at);
                track.Scale = ReadCurve(mask.Scale, ScalarWidth(mask.ScaleQuantization), ref at);
                at = Align4(at);
                if (at < 0) return result;
            }

            /* Whatever follows a block starts on a sixteen byte boundary, so landing anywhere in the
             * pad before it is right. Anything further out means the layout didn't hold. */
            long padded = clip.DataOffset + (((at - clip.DataOffset) + 15) & ~15L);
            result.Complete = at >= 0 && (at == end || padded == end);
            return result;
        }

        private static long Align4(long at) { return at < 0 ? at : (at + 3) & ~3L; }
        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static int ScalarWidth(int quantization) { return quantization == 0 ? 1 : quantization == 1 ? 2 : 4; }
        private static int RotationWidth(int quantization)
        {
            switch (quantization)
            {
                case 0: return 4;    //POLAR32
                case 1: return 5;    //THREECOMP40
                case 2: return 6;    //THREECOMP48
                case 3: return 3;    //THREECOMP24
                case 4: return 2;    //STRAIGHT16
                default: return 16;  //UNCOMPRESSED
            }
        }

        /* A vector track: a curve for some components, a plain float for the rest. */
        private ComponentCurve ReadCurve(byte mask, int width, ref long at)
        {
            int spline = (mask >> 4) & 0x0F, stat = mask & 0x0F;
            if (spline == 0 && stat == 0) return null;

            ComponentCurve curve = new ComponentCurve { SplineComponents = spline, StaticComponents = stat, Width = width };
            if (spline != 0 && !ReadNurbs(curve, ref at)) { at = -1; return curve; }
            if (at < 0) return curve;

            /* Floats come first, in X Y Z order: a curved component contributes the range it was
             * quantized into, a held one contributes its value. Only then do the control points
             * follow, which are packed bytes and need no alignment of their own. */
            at = Align4(at);
            for (int c = 0; c < 3; c++)
            {
                if ((spline & (1 << c)) != 0)
                {
                    if (at < 0 || at + 8 > DataPayload.Length) { at = -1; return curve; }
                    curve.Minimum[c] = BitConverter.ToSingle(DataPayload, (int)at);
                    curve.Maximum[c] = BitConverter.ToSingle(DataPayload, (int)at + 4);
                    //a range that isn't a finite number means we are not where we think we are
                    if (!IsFinite(curve.Minimum[c]) || !IsFinite(curve.Maximum[c])) { at = -1; return curve; }
                    at += 8;
                }
                else if ((stat & (1 << c)) != 0)
                {
                    if (at < 0 || at + 4 > DataPayload.Length) { at = -1; return curve; }
                    curve.Static[c] = BitConverter.ToSingle(DataPayload, (int)at);
                    if (!IsFinite(curve.Static[c])) { at = -1; return curve; }
                    at += 4;
                }
            }

            /* The splined axes share one curve, and their control points are stored a point at a
             * time rather than an axis at a time: x0 y0 z0, x1 y1 z1, ... So each axis starts one
             * value further in than the last and then steps over the others.
             *
             * The total is the same either way, which is why the block accounting never noticed:
             * reading them as three contiguous lanes consumes exactly as many bytes and still lands
             * on the end of the block. It just samples x, y, z, x, y, z into the x axis, which shows
             * up as a violent wobble with a period equal to the number of splined axes. */
            int lanes = 0;
            for (int c = 0; c < 3; c++) if ((spline & (1 << c)) != 0) lanes++;

            long start = at;
            int lane = 0;
            for (int c = 0; c < 3; c++)
            {
                if ((spline & (1 << c)) == 0) continue;
                if (at < 0) return curve;
                curve.ControlPoints[c] = (uint)(start + (lane * width));
                lane++;
            }
            curve.Stride = lanes * width;
            at = start + ((curve.Items + 1) * (long)curve.Stride);
            return curve;
        }

        /* A rotation is stored whole rather than per component, so it is one value or one curve. */
        private ComponentCurve ReadRotationCurve(byte mask, int width, ref long at)
        {
            int spline = (mask >> 4) & 0x0F, stat = mask & 0x0F;
            if (spline == 0 && stat == 0) return null;

            ComponentCurve curve = new ComponentCurve { SplineComponents = spline, StaticComponents = stat, Width = width };
            if (spline != 0)
            {
                if (!ReadNurbs(curve, ref at)) { at = -1; return curve; }
                curve.ControlPoints[0] = (uint)at;
                curve.Stride = width;
                at += (curve.Items + 1) * width;
            }
            else
            {
                curve.ControlPoints[0] = (uint)at;
                curve.Stride = width;
                at += width;
            }
            return curve;
        }

        /* uint16 item count, byte degree, then count + degree + 2 byte knots. */
        private bool ReadNurbs(ComponentCurve curve, ref long at)
        {
            if (at < 0 || at + 3 > DataPayload.Length) return false;
            curve.Items = BitConverter.ToUInt16(DataPayload, (int)at);
            curve.Degree = DataPayload[at + 2];
            if (curve.Items > 4096) return false;

            curve.Knots = (uint)(at + 3);
            curve.KnotCount = curve.Items + curve.Degree + 2;
            at += 3 + curve.KnotCount;
            return at <= DataPayload.Length;
        }

        /// <summary>
        /// Sample every transform track of a clip at one frame. Tracks come back in track order, so
        /// <see cref="AnimationClip.TrackToBone"/> maps them onto the skeleton. Returns an empty list
        /// if the frame's block doesn't decode - see <see cref="BlockTracks.Complete"/>.
        /// </summary>
        public List<SampledTransform> Sample(AnimationClip clip, int frame)
        {
            List<SampledTransform> pose = new List<SampledTransform>();
            if (clip == null || clip.MaxFramesPerBlock <= 0 || frame < 0 || frame >= clip.FrameCount) return pose;

            int block = frame / clip.MaxFramesPerBlock;
            float at = frame % clip.MaxFramesPerBlock;
            if (block >= clip.Blocks.Count) return pose;

            BlockTracks tracks = ReadBlockTracks(clip, block);
            if (!tracks.Complete) return pose;

            foreach (TrackCurves track in tracks.Tracks)
            {
                pose.Add(new SampledTransform
                {
                    Translation = SampleVector(track.Position, at, 0f),
                    Rotation = SampleRotation(track.Rotation, at),
                    Scale = SampleVector(track.Scale, at, 1f),
                    HasTranslation = Carries(track.Position),
                    HasRotation = Carries(track.Rotation),
                    HasScale = Carries(track.Scale),
                });
            }
            return pose;
        }

        /// <summary>Sample every frame of a clip, which is what an exporter wants.</summary>
        public List<List<SampledTransform>> SampleAll(AnimationClip clip)
        {
            List<List<SampledTransform>> frames = new List<List<SampledTransform>>();
            if (clip == null) return frames;
            for (int i = 0; i < clip.FrameCount; i++) frames.Add(Sample(clip, i));
            return frames;
        }

        /* Components the track doesn't mention keep the default - zero for a translation, one for a scale. */
        /// <summary>Whether this track stored anything at all for one channel.</summary>
        private static bool Carries(ComponentCurve curve)
        {
            return curve != null && (curve.SplineComponents != 0 || curve.StaticComponents != 0);
        }

        private System.Numerics.Vector3 SampleVector(ComponentCurve curve, float at, float fallback)
        {
            System.Numerics.Vector3 value = new System.Numerics.Vector3(fallback, fallback, fallback);
            if (curve == null) return value;

            for (int c = 0; c < 3; c++)
            {
                if ((curve.SplineComponents & (1 << c)) != 0)
                {
                    float[] points = new float[curve.Items + 1];
                    for (int i = 0; i <= curve.Items; i++)
                        points[i] = Dequantize(curve.ControlPoints[c] + (uint)(i * curve.Stride), curve.Width, curve.Minimum[c], curve.Maximum[c]);
                    SetComponent(ref value, c, Evaluate(points, curve, at));
                }
                else if ((curve.StaticComponents & (1 << c)) != 0) SetComponent(ref value, c, curve.Static[c]);
            }
            return value;
        }

        private System.Numerics.Quaternion SampleRotation(ComponentCurve curve, float at)
        {
            if (curve == null) return System.Numerics.Quaternion.Identity;
            if (!curve.IsSpline) return DecodeQuaternion(curve.ControlPoints[0], curve.Width);

            /* Interpolate the components and renormalise. Line up each control point with the one
             * before it first, or the double cover makes the curve jump halfway through. */
            System.Numerics.Quaternion[] points = new System.Numerics.Quaternion[curve.Items + 1];
            for (int i = 0; i <= curve.Items; i++)
            {
                points[i] = DecodeQuaternion(curve.ControlPoints[0] + (uint)(i * curve.Stride), curve.Width);
                if (i != 0 && System.Numerics.Quaternion.Dot(points[i - 1], points[i]) < 0)
                    points[i] = new System.Numerics.Quaternion(-points[i].X, -points[i].Y, -points[i].Z, -points[i].W);
            }

            float[] lane = new float[points.Length];
            float[] result = new float[4];
            for (int c = 0; c < 4; c++)
            {
                for (int i = 0; i < points.Length; i++)
                    lane[i] = c == 0 ? points[i].X : c == 1 ? points[i].Y : c == 2 ? points[i].Z : points[i].W;
                result[c] = Evaluate(lane, curve, at);
            }

            System.Numerics.Quaternion sampled = new System.Numerics.Quaternion(result[0], result[1], result[2], result[3]);
            float length = sampled.Length();
            return length > 1e-8f ? System.Numerics.Quaternion.Normalize(sampled) : System.Numerics.Quaternion.Identity;
        }

        private static void SetComponent(ref System.Numerics.Vector3 value, int component, float to)
        {
            if (component == 0) value.X = to;
            else if (component == 1) value.Y = to;
            else value.Z = to;
        }

        /* A control point is a fraction of the range the curve declared, or a plain float at 32 bits. */
        private float Dequantize(uint at, int width, float minimum, float maximum)
        {
            if (at + width > DataPayload.Length) return minimum;
            switch (width)
            {
                case 1: return minimum + (DataPayload[at] / 255f) * (maximum - minimum);
                case 2: return minimum + (BitConverter.ToUInt16(DataPayload, (int)at) / 65535f) * (maximum - minimum);
                default: return BitConverter.ToSingle(DataPayload, (int)at);
            }
        }

        /// <summary>
        /// Unpack one quantized rotation. Retail only ever uses THREECOMP40: three 12 bit components,
        /// two bits naming the one left out, and a bit for its sign. The missing component is whatever
        /// makes the quaternion unit length.
        /// </summary>
        public System.Numerics.Quaternion DecodeQuaternion(uint at, int width)
        {
            if (at + width > DataPayload.Length) return System.Numerics.Quaternion.Identity;
            if (width != 5) return System.Numerics.Quaternion.Identity;

            ulong packed = 0;
            for (int i = 0; i < 5; i++) packed |= (ulong)DataPayload[at + i] << (8 * i);

            const double range = 1.4142135623730951;   //the three smallest components live in +-1/sqrt(2)
            const double offset = -0.7071067811865476;
            double[] three =
            {
                (packed & 0xFFF) * (range / 4095.0) + offset,
                ((packed >> 12) & 0xFFF) * (range / 4095.0) + offset,
                ((packed >> 24) & 0xFFF) * (range / 4095.0) + offset,
            };

            int missing = (int)((packed >> 36) & 0x03);
            double[] q = new double[4];
            int next = 0;
            for (int i = 0; i < 4; i++) if (i != missing) q[i] = three[next++];

            double largest = Math.Sqrt(Math.Max(0.0, 1.0 - (q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3])));
            if (((packed >> 38) & 1) != 0) largest = -largest;
            q[missing] = largest;

            return new System.Numerics.Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3]);
        }

        /* de Boor over the byte knot vector the curve carries. Degree 0 is a step, 1 is linear. */
        private float Evaluate(float[] points, ComponentCurve curve, float at)
        {
            if (points.Length == 0) return 0f;
            if (points.Length == 1 || curve.Degree == 0) return points[Math.Min(points.Length - 1, (int)at)];

            int degree = curve.Degree;
            int knotCount = curve.KnotCount;
            if (curve.Knots + knotCount > DataPayload.Length) return points[0];

            //the span holding this frame
            int span = degree;
            while (span < knotCount - 1 && DataPayload[curve.Knots + span + 1] <= at) span++;
            if (span - degree < 0) span = degree;
            if (span > points.Length - 1) span = points.Length - 1;

            float[] work = new float[degree + 1];
            for (int i = 0; i <= degree; i++)
            {
                int index = span - degree + i;
                work[i] = points[Math.Max(0, Math.Min(points.Length - 1, index))];
            }

            for (int r = 1; r <= degree; r++)
                for (int i = degree; i >= r; i--)
                {
                    int low = span - degree + i, high = span + 1 + i - r;
                    if (low < 0 || high >= knotCount) continue;
                    float a = DataPayload[curve.Knots + low], b = DataPayload[curve.Knots + high];
                    float alpha = b - a <= 0 ? 0f : (at - a) / (b - a);
                    work[i] = (1f - alpha) * work[i - 1] + alpha * work[i];
                }
            return work[degree];
        }

        /// <summary>
        /// Read every <c>hkaSkeletonMapper</c>: which bones of one skeleton drive which of another.
        /// </summary>
        public List<SkeletonMapper> GetSkeletonMappers()
        {
            List<SkeletonMapper> mappers = new List<SkeletonMapper>();
            for (int i = 0; i < Objects.Count; i++)
            {
                if (Objects[i].ClassName != "hkaSkeletonMapper") continue;

                SkeletonMapper mapper = new SkeletonMapper { DataOffset = Objects[i].DataOffset };
                uint end = ObjectEnd(Objects[i].DataOffset);

                /* An empty hkArray has no fixup at all, so rather than hardcoding field offsets,
                 * take the arrays this object actually points at and tell them apart by how many
                 * bytes each element gets: a simple mapping is two bone indices plus a 16 byte
                 * aligned hkQsTransform (64), an unmapped bone is a bare int16. */
                foreach (KeyValuePair<uint, int> array in GetObjectArrays(Objects[i].DataOffset, end))
                {
                    int stride = ElementStride(array.Key, array.Value);
                    if (stride >= 48)
                    {
                        /* Two bone indices, then the hkQsTransform on the next sixteen byte boundary:
                         * translation, rotation and scale as four floats each, of which the last of
                         * the translation and scale is padding. */
                        for (int x = 0; x < array.Value && array.Key + (x * 64) + 64 <= DataPayload.Length; x++)
                        {
                            int at = (int)(array.Key + (x * 64));
                            mapper.Mappings.Add(new BoneMapping
                            {
                                BoneA = BitConverter.ToInt16(DataPayload, at),
                                BoneB = BitConverter.ToInt16(DataPayload, at + 2),
                                Translation = ReadVector3(at + 16),
                                Rotation = ReadQuaternion(at + 32),
                                Scale = ReadVector3(at + 48),
                            });
                        }
                    }
                    else if (stride <= 2)
                    {
                        for (int x = 0; x < array.Value && array.Key + (x * 2) + 2 <= DataPayload.Length; x++)
                            mapper.UnmappedBones.Add(BitConverter.ToInt16(DataPayload, (int)(array.Key + (x * 2))));
                    }
                }
                mappers.Add(mapper);
            }
            return mappers;
        }

        private System.Numerics.Vector3 ReadVector3(int at)
        {
            return new System.Numerics.Vector3(
                BitConverter.ToSingle(DataPayload, at),
                BitConverter.ToSingle(DataPayload, at + 4),
                BitConverter.ToSingle(DataPayload, at + 8));
        }

        private System.Numerics.Quaternion ReadQuaternion(int at)
        {
            return new System.Numerics.Quaternion(
                BitConverter.ToSingle(DataPayload, at),
                BitConverter.ToSingle(DataPayload, at + 4),
                BitConverter.ToSingle(DataPayload, at + 8),
                BitConverter.ToSingle(DataPayload, at + 12));
        }

        /// <summary>
        /// Read the <c>hkaRagdollInstance</c>: the physics bodies standing in for the skeleton,
        /// and which bone each one belongs to.
        /// </summary>
        public RagdollInstance GetRagdoll()
        {
            PackfileObject instance = Objects.FirstOrDefault(x => x.ClassName == "hkaRagdollInstance");
            if (instance == null) return null;

            uint ptr = (uint)Header.PointerSize;
            uint bodies = instance.DataOffset + ObjectHeaderSize;
            uint constraints = bodies + ArraySize;
            uint boneMap = constraints + ArraySize;

            RagdollInstance ragdoll = new RagdollInstance();
            Dictionary<uint, uint> globalBySrc = new Dictionary<uint, uint>();
            for (int i = 0; i < GlobalFixups.Count; i++)
                globalBySrc[GlobalFixups[i].Src] = GlobalFixups[i].Dst;

            //Each element is a pointer to an hkpRigidBody elsewhere in the file
            if (TryGetHkArray(bodies, out uint bodyList, out int bodyCount))
            {
                uint nameField = Header.PointerSize == 8 ? 0xB0u : 0x78u;
                for (int i = 0; i < bodyCount; i++)
                {
                    if (!globalBySrc.TryGetValue(bodyList + (uint)(i * ptr), out uint body)) { ragdoll.Bodies.Add(""); continue; }
                    ragdoll.Bodies.Add(TryResolveLocal(body + nameField, out uint nameAt) ? ReadStringAt(nameAt) : "");
                }
            }

            if (TryGetHkArray(constraints, out uint _, out int constraintCount))
                ragdoll.ConstraintCount = constraintCount;

            if (TryGetHkArray(boneMap, out uint map, out int mapCount))
                for (int i = 0; i < mapCount && map + (i * 4) + 4 <= DataPayload.Length; i++)
                    ragdoll.BoneToBody.Add(BitConverter.ToInt32(DataPayload, (int)(map + (i * 4))));

            return ragdoll;
        }

        /* Where the object's data runs to - the next object, or the end of the payload */
        private uint ObjectEnd(uint start)
        {
            uint end = (uint)DataPayload.Length;
            for (int i = 0; i < Objects.Count; i++)
                if (Objects[i].DataOffset > start && Objects[i].DataOffset < end) end = Objects[i].DataOffset;
            return end;
        }

        /* Every array this object points at, as data offset -> element count */
        private List<KeyValuePair<uint, int>> GetObjectArrays(uint start, uint end)
        {
            List<KeyValuePair<uint, int>> arrays = new List<KeyValuePair<uint, int>>();
            for (int i = 0; i < LocalFixups.Count; i++)
            {
                if (LocalFixups[i].Src < start || LocalFixups[i].Src >= end) continue;
                int sizeAt = (int)LocalFixups[i].Src + Header.PointerSize;
                if (sizeAt + 4 > DataPayload.Length) continue;

                int count = BitConverter.ToInt32(DataPayload, sizeAt);
                if (count > 0 && count < 1_000_000) arrays.Add(new KeyValuePair<uint, int>(LocalFixups[i].Dst, count));
            }
            return arrays;
        }

        /* How many bytes each element of an array gets, from the space before whatever follows it */
        private int ElementStride(uint data, int count)
        {
            if (count <= 0) return 0;
            uint next = (uint)DataPayload.Length;
            for (int i = 0; i < LocalFixups.Count; i++)
                if (LocalFixups[i].Dst > data && LocalFixups[i].Dst < next) next = LocalFixups[i].Dst;
            for (int i = 0; i < Objects.Count; i++)
                if (Objects[i].DataOffset > data && Objects[i].DataOffset < next) next = Objects[i].DataOffset;
            return (int)((next - data) / count);
        }

        /// <summary>
        /// A ragdoll: the rigid bodies that stand in for a skeleton while it's simulated.
        /// </summary>
        public class RagdollInstance
        {
            /// <summary>Rigid body names, in ragdoll order.</summary>
            public List<string> Bodies = new List<string>();

            public int ConstraintCount;

            /// <summary>Body index for each skeleton bone, or -1 where a bone isn't simulated.</summary>
            public List<int> BoneToBody = new List<int>();

            public override string ToString() => Bodies.Count + " bodies, " + ConstraintCount + " constraints";
        }

        /// <summary>
        /// One animation clip: how long it is, and which bones of which skeleton it drives.
        /// </summary>
        public class AnimationClip
        {
            /// <summary>Skeleton the clip was authored against.</summary>
            public string SkeletonName = "";

            public float Duration;
            public int TransformTrackCount;
            public int FloatTrackCount;
            public int FrameCount;
            public int BlockCount;
            public int MaxFramesPerBlock;
            public float FrameDuration;

            /// <summary>Skeleton bone index driven by each transform track.</summary>
            public List<int> TrackToBone = new List<int>();

            /// <summary>
            /// Whether the clip holds deltas to layer over a base pose rather than a pose of its own.
            /// The impact and recoil clips are all built this way: on their own their tracks read as
            /// no rotation and no offset, and they only mean anything added on top of something else.
            /// </summary>
            public bool Additive;

            /// <summary>Where the animation sits in __data__, so callers can match it back up.</summary>
            public uint AnimationOffset;

            /// <summary>Bytes of mask at the head of every block - four per transform track.</summary>
            public int MaskAndQuantizationSize;

            public float BlockDuration;

            /// <summary>Where each block begins, relative to <see cref="DataOffset"/>.</summary>
            public List<uint> BlockOffsets = new List<uint>();

            /// <summary>Where each block's float track data begins, relative to <see cref="DataOffset"/>.</summary>
            public List<uint> FloatBlockOffsets = new List<uint>();

            /// <summary>What each transform track stores, per block. See <see cref="TransformMask"/>.</summary>
            public List<List<TransformMask>> Blocks = new List<List<TransformMask>>();

            /// <summary>The compressed stream inside __data__, and how long it runs.</summary>
            public uint DataOffset;
            public int DataLength;

            public override string ToString() => (SkeletonName.Length == 0 ? "animation" : SkeletonName) + " " + Duration.ToString("0.###") + "s";
        }

        /// <summary>One bone's transform at one frame.</summary>
        public class SampledTransform
        {
            public System.Numerics.Vector3 Translation;
            public System.Numerics.Quaternion Rotation;
            public System.Numerics.Vector3 Scale;

            /* A track only stores the channels the clip actually drives, so a value can be here
             * because the clip said so or because nothing did. The two are not the same thing: an
             * unwritten channel means "leave the bone as the rig rests", not "zero". */
            public bool HasTranslation;
            public bool HasRotation;
            public bool HasScale;

            public override string ToString() => Translation.ToString() + " " + Rotation.ToString();
        }

        /// <summary>What every transform track in one block of a clip holds.</summary>
        public class BlockTracks
        {
            public List<TrackCurves> Tracks = new List<TrackCurves>();

            /// <summary>
            /// Whether the walk consumed the block exactly. False means the layout didn't hold for
            /// this block and nothing on it should be trusted.
            /// </summary>
            public bool Complete;

            public override string ToString() => Tracks.Count + " track(s)" + (Complete ? "" : " (incomplete)");
        }

        /// <summary>One transform track: up to three curves, any of which may be absent.</summary>
        public class TrackCurves
        {
            public ComponentCurve Position;
            public ComponentCurve Rotation;
            public ComponentCurve Scale;
        }

        /// <summary>
        /// A component of a track. Components named by <see cref="StaticComponents"/> hold one value
        /// in <see cref="Static"/>; those named by <see cref="SplineComponents"/> are a curve whose
        /// control points have not been dequantized yet - <see cref="ControlPoints"/> says where they
        /// are and <see cref="Minimum"/>/<see cref="Maximum"/> the range they expand into.
        /// </summary>
        public class ComponentCurve
        {
            /// <summary>X/Y/Z (and W, for a rotation) bits driven by a curve.</summary>
            public int SplineComponents;

            /// <summary>X/Y/Z (and W) bits held at a single value.</summary>
            public int StaticComponents;

            /// <summary>Fully decoded, for the components <see cref="StaticComponents"/> names.</summary>
            public float[] Static = new float[3];

            public float[] Minimum = new float[3];
            public float[] Maximum = new float[3];

            /// <summary>Where each component's control points start in <c>DataPayload</c>.</summary>
            public uint[] ControlPoints = new uint[3];

            /// <summary>Bytes per control point.</summary>
            public int Width;

            public int Items, Degree, KnotCount;

            /// <summary>Bytes between one control point of a component and the next. Splined axes
            /// share a curve and interleave their points, so this is width times the axis count.</summary>
            public int Stride;

            /// <summary>Where the byte knot vector starts in <c>DataPayload</c>.</summary>
            public uint Knots;

            public bool IsSpline { get { return SplineComponents != 0; } }

            public override string ToString() =>
                IsSpline ? "curve items=" + Items + " degree=" + Degree : "static";
        }

        /// <summary>
        /// The four bytes at the head of a block that say what one transform track stores.
        ///
        /// The curves themselves are not decoded yet. What is confirmed: the stream that follows the
        /// masks holds, per spline, a <c>uint16</c> item count, a <c>byte</c> degree, then
        /// <c>count + degree + 2</c> byte knots, then <c>count + 1</c> control points quantized to
        /// the width <see cref="RotationQuantization"/> names - 5 bytes each for THREECOMP40.
        /// </summary>
        public class TransformMask
        {
            /// <summary>Packed quantization widths - see the properties below.</summary>
            public byte Quantization;

            /// <summary>Which position components are stored, and how.</summary>
            public byte Position;

            /// <summary>Which rotation components are stored. The nibbles do not read as cleanly
            /// as the position ones do, so this is left raw.</summary>
            public byte Rotation;

            /// <summary>Which scale components are stored, and how.</summary>
            public byte Scale;

            /// <summary>Bits 0-1: how translation control points are quantized.</summary>
            public int TranslationQuantization { get { return Quantization & 0x03; } }

            /// <summary>Bits 2-5: 0 POLAR32, 1 THREECOMP40, 2 THREECOMP48, 3 THREECOMP24, 4 STRAIGHT16, 5 UNCOMPRESSED.</summary>
            public int RotationQuantization { get { return (Quantization >> 2) & 0x0F; } }

            /// <summary>Bits 6-7: how scale control points are quantized.</summary>
            public int ScaleQuantization { get { return (Quantization >> 6) & 0x03; } }

            /// <summary>Position components driven by a curve, as X/Y/Z bits.</summary>
            public int PositionSpline { get { return Position & 0x0F; } }

            /// <summary>Position components held at one value, as X/Y/Z bits.</summary>
            public int PositionStatic { get { return (Position >> 4) & 0x0F; } }

            public int ScaleSpline { get { return Scale & 0x0F; } }
            public int ScaleStatic { get { return (Scale >> 4) & 0x0F; } }

            public override string ToString() =>
                "pos=0x" + Position.ToString("X2") + " rot=0x" + Rotation.ToString("X2") + " scale=0x" + Scale.ToString("X2");
        }

        /// <summary>
        /// A retargeting mapper between two skeletons.
        /// </summary>
        public class SkeletonMapper
        {
            public uint DataOffset;
            public List<BoneMapping> Mappings = new List<BoneMapping>();
            public List<int> UnmappedBones = new List<int>();

            public override string ToString() => Mappings.Count + " mapped, " + UnmappedBones.Count + " unmapped";
        }

        public class BoneMapping
        {
            public int BoneA;
            public int BoneB;

            /// <summary>
            /// Where bone A sits relative to bone B - Havok's <c>m_aFromBTransform</c>. Two rigs that
            /// share a bone rarely rest it the same way, and this is the difference. Without it a
            /// retarget copies raw local transforms and the mesh comes out twisted.
            /// </summary>
            public System.Numerics.Vector3 Translation;
            public System.Numerics.Quaternion Rotation = System.Numerics.Quaternion.Identity;
            public System.Numerics.Vector3 Scale = System.Numerics.Vector3.One;

            public override string ToString() => BoneA + " -> " + BoneB;
        }
        #endregion

        #region HEADER_SECTIONS
        bool ReadHeader(BinaryReader reader)
        {
            Header.Magic0 = reader.ReadUInt32();
            Header.Magic1 = reader.ReadUInt32();
            if (Header.Magic0 != 0x57E0E057 || Header.Magic1 != 0x10C0C010)
                return false;

            Header.UserTag = reader.ReadInt32();
            Header.FileVersion = reader.ReadInt32();
            Header.PointerSize = reader.ReadByte();
            Header.LittleEndian = reader.ReadByte();
            Header.PaddingOption = reader.ReadByte();
            Header.BaseClass = reader.ReadByte();
            Header.NumSections = reader.ReadInt32();
            Header.ContentsSectionIndex = reader.ReadInt32();
            Header.ContentsSectionOffset = reader.ReadInt32();
            Header.ContentsClassNameSectionIndex = reader.ReadInt32();
            Header.ContentsClassNameSectionOffset = reader.ReadInt32();

            byte[] verBytes = reader.ReadBytes(16);
            int verLen = Array.IndexOf(verBytes, (byte)0);
            if (verLen < 0) verLen = verBytes.Length;
            Header.ContentsVersion = Encoding.ASCII.GetString(verBytes, 0, verLen);
            Header.Flags = reader.ReadInt32();
            Header.MaxPredicate = reader.ReadInt32();
            return true;
        }

        void WriteHeader(BinaryWriter writer)
        {
            writer.Write(Header.Magic0);
            writer.Write(Header.Magic1);
            writer.Write(Header.UserTag);
            writer.Write(Header.FileVersion);
            writer.Write(Header.PointerSize);
            writer.Write(Header.LittleEndian);
            writer.Write(Header.PaddingOption);
            writer.Write(Header.BaseClass);
            writer.Write(Header.NumSections);
            writer.Write(Header.ContentsSectionIndex);
            writer.Write(Header.ContentsSectionOffset);
            writer.Write(Header.ContentsClassNameSectionIndex);
            writer.Write(Header.ContentsClassNameSectionOffset);

            byte[] ver = new byte[16];
            for (int i = 0; i < ver.Length; i++) ver[i] = 0xFF;
            byte[] name = Encoding.ASCII.GetBytes(Header.ContentsVersion ?? "");
            int copy = Math.Min(name.Length, 15);
            Array.Copy(name, ver, copy);
            ver[copy] = 0;
            writer.Write(ver);
            writer.Write(Header.Flags);
            writer.Write(Header.MaxPredicate);
        }

        static string ReadSectionTag(BinaryReader reader)
        {
            byte[] nameBytes = reader.ReadBytes(16);
            int nameLen = Array.IndexOf(nameBytes, (byte)0);
            if (nameLen < 0) nameLen = 16;
            return Encoding.ASCII.GetString(nameBytes, 0, nameLen);
        }

        static void WriteSectionHeader(BinaryWriter writer, string name,
            uint abs, uint local, uint global, uint virt, uint exports, uint imports, uint end)
        {
            byte[] tag = new byte[16];
            byte[] nb = Encoding.ASCII.GetBytes(name ?? "");
            Array.Copy(nb, tag, Math.Min(nb.Length, 16));
            writer.Write(tag);
            writer.Write(0xFF000000); // Havok section AbsoluteDataStart high-byte marker
            writer.Write(abs);
            writer.Write(local);
            writer.Write(global);
            writer.Write(virt);
            writer.Write(exports);
            writer.Write(imports);
            writer.Write(end);
        }

        static byte[] Slice(byte[] data, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset + length > data.Length)
                return Array.Empty<byte>();
            byte[] slice = new byte[length];
            Buffer.BlockCopy(data, offset, slice, 0, length);
            return slice;
        }
        #endregion

        #region FIXUPS
        void ReadLocalFixups(byte[] file, int start, int end)
        {
            for (int p = start; p + 8 <= end && p + 8 <= file.Length; p += 8)
            {
                uint src = BitConverter.ToUInt32(file, p);
                uint dst = BitConverter.ToUInt32(file, p + 4);
                if (src == 0xFFFFFFFF) break;
                LocalFixups.Add(new LocalFixup { Src = src, Dst = dst });
            }
        }

        void ReadGlobalFixups(byte[] file, int start, int end)
        {
            for (int p = start; p + 12 <= end && p + 12 <= file.Length; p += 12)
            {
                uint src = BitConverter.ToUInt32(file, p);
                uint sec = BitConverter.ToUInt32(file, p + 4);
                uint dst = BitConverter.ToUInt32(file, p + 8);
                if (src == 0xFFFFFFFF) break;
                GlobalFixups.Add(new GlobalFixup { Src = src, DstSectionIndex = sec, Dst = dst });
            }
        }

        void ReadVirtualFixups(byte[] file, int start, int end)
        {
            for (int p = start; p + 12 <= end && p + 12 <= file.Length; p += 12)
            {
                uint src = BitConverter.ToUInt32(file, p);
                uint sec = BitConverter.ToUInt32(file, p + 4);
                int nameOff = BitConverter.ToInt32(file, p + 8);
                if (src == 0xFFFFFFFF) break;
                VirtualFixups.Add(new VirtualFixup { Src = src, SectionIndex = sec, NameOffset = nameOff });
            }
        }
        #endregion

        #region OBJECTS_AND_COMPOUNDS
        static Dictionary<int, string> ParseClassNames(byte[] classnames)
        {
            var map = new Dictionary<int, string>();
            int i = 0;
            while (i + 5 < classnames.Length)
            {
                i += 4;
                if (i >= classnames.Length) break;
                i++;
                int s = i;
                while (i < classnames.Length && classnames[i] != 0) i++;
                if (i > s)
                    map[s] = Encoding.ASCII.GetString(classnames, s, i - s);
                i++;
            }
            return map;
        }

        void BuildObjectList(Dictionary<int, string> classNames)
        {
            int compoundOrdinal = 0;
            PhysicsSystems.Clear();
            for (int i = 0; i < VirtualFixups.Count; i++)
            {
                VirtualFixup vf = VirtualFixups[i];
                classNames.TryGetValue(vf.NameOffset, out string className);
                var obj = new PackfileObject
                {
                    DataOffset = vf.Src,
                    ClassNameOffset = vf.NameOffset,
                    ClassName = className ?? "",
                    Class = Classify(className),
                };
                if (obj.Class == ObjectClass.StaticCompoundShape)
                {
                    obj.ProxyIndex = compoundOrdinal++;
                }
                else if (obj.Class == ObjectClass.PhysicsSystem)
                {
                    // SystemIndex assigned later from hkpPhysicsData.systems[] order.
                    PhysicsSystems.Add(new PhysicsSystem
                    {
                        SystemIndex = -1,
                        DataOffset = obj.DataOffset,
                        Object = obj,
                    });
                }
                Objects.Add(obj);
            }
        }

        /// <summary>
        /// Assign <see cref="PhysicsSystem.SystemIndex"/> from <c>hkpPhysicsData.systems[]</c>
        /// (the order Commands / PHYSICS.MAP use). Falls back to appearance order if PhysicsData
        /// is missing.
        /// </summary>
        void ParsePhysicsSystemIndexes()
        {
            if (PhysicsSystems.Count == 0)
                return;

            var byOffset = new Dictionary<uint, PhysicsSystem>(PhysicsSystems.Count);
            int nameFieldOffset = Header.PointerSize == 8 ? 80 : 56; // hkArray packing differs 32 vs 64
            for (int i = 0; i < PhysicsSystems.Count; i++)
            {
                PhysicsSystem ps = PhysicsSystems[i];
                byOffset[ps.DataOffset] = ps;
                ps.Name = ReadStringPtr(ps.DataOffset + (uint)nameFieldOffset);
            }

            PackfileObject physicsData = null;
            for (int i = 0; i < Objects.Count; i++)
            {
                if (Objects[i].Class == ObjectClass.PhysicsData)
                {
                    physicsData = Objects[i];
                    break;
                }
            }

            List<PhysicsSystem> ordered = null;
            if (physicsData != null)
            {
                int ptrSize = Header.PointerSize;
                // hkpPhysicsData: hkReferencedObject (16) + worldCinfo ptr + systems hkArray
                uint systemsField = physicsData.DataOffset + 16 + (uint)ptrSize;
                if (TryReadPointerArray(systemsField, out List<uint> systemOffsets) && systemOffsets.Count > 0)
                {
                    ordered = new List<PhysicsSystem>(systemOffsets.Count);
                    for (int i = 0; i < systemOffsets.Count; i++)
                    {
                        if (!byOffset.TryGetValue(systemOffsets[i], out PhysicsSystem ps))
                            continue;
                        ordered.Add(ps);
                    }
                    if (ordered.Count != PhysicsSystems.Count)
                        ordered = null; // incomplete — fall back
                }
            }

            if (ordered == null)
            {
                // Appearance / virtual-fixup order (stable but not the game index).
                ordered = new List<PhysicsSystem>(PhysicsSystems);
            }

            PhysicsSystems.Clear();
            for (int i = 0; i < ordered.Count; i++)
            {
                PhysicsSystem ps = ordered[i];
                ps.SystemIndex = i;
                if (ps.Object != null)
                    ps.Object.ProxyIndex = i;
                PhysicsSystems.Add(ps);
            }
        }

        /// <summary>
        /// Resolve an hkArray&lt;T*&gt; field: local fixup to storage, global fixups for each element.
        /// </summary>
        bool TryReadPointerArray(uint arrayFieldOffset, out List<uint> elementOffsets)
        {
            elementOffsets = null;
            int ptrSize = Header.PointerSize;
            int sizePos = (int)arrayFieldOffset + ptrSize;
            if (sizePos + 4 > DataPayload.Length)
                return false;

            int count = BitConverter.ToInt32(DataPayload, sizePos);
            if (count < 0)
                return false;

            uint arrayDataOffset = 0;
            bool foundLocal = false;
            for (int f = 0; f < LocalFixups.Count; f++)
            {
                if (LocalFixups[f].Src == arrayFieldOffset)
                {
                    arrayDataOffset = LocalFixups[f].Dst;
                    foundLocal = true;
                    break;
                }
            }
            if (!foundLocal && count > 0)
                return false;

            elementOffsets = new List<uint>(count);
            if (count == 0)
                return true;

            var globalBySrc = new Dictionary<uint, uint>(GlobalFixups.Count);
            for (int i = 0; i < GlobalFixups.Count; i++)
                globalBySrc[GlobalFixups[i].Src] = GlobalFixups[i].Dst;

            for (int n = 0; n < count; n++)
            {
                uint slot = arrayDataOffset + (uint)(n * ptrSize);
                if (!globalBySrc.TryGetValue(slot, out uint dst))
                    return false;
                elementOffsets.Add(dst);
            }
            return true;
        }

        string ReadStringPtr(uint stringPtrFieldOffset)
        {
            for (int f = 0; f < LocalFixups.Count; f++)
            {
                if (LocalFixups[f].Src != stringPtrFieldOffset)
                    continue;
                int off = (int)LocalFixups[f].Dst;
                if (off < 0 || off >= DataPayload.Length)
                    return null;
                int end = off;
                while (end < DataPayload.Length && DataPayload[end] != 0)
                    end++;
                return Encoding.ASCII.GetString(DataPayload, off, end - off);
            }
            return null;
        }

        void ParseStaticCompoundInstances(Dictionary<int, string> classNames)
        {
            var classAtOffset = new Dictionary<uint, string>();
            for (int i = 0; i < Objects.Count; i++)
                classAtOffset[Objects[i].DataOffset] = Objects[i].ClassName;

            var globalBySrc = new Dictionary<uint, uint>();
            for (int i = 0; i < GlobalFixups.Count; i++)
                globalBySrc[GlobalFixups[i].Src] = GlobalFixups[i].Dst;

            int ptrSize = Header.PointerSize;
            int instancesArrayOffset = ptrSize == 8 ? 0x38 : 0x20;
            int instanceStride = ptrSize == 8 ? 80 : 64;

            for (int i = 0; i < Objects.Count; i++)
            {
                PackfileObject obj = Objects[i];
                if (obj.Class != ObjectClass.StaticCompoundShape)
                    continue;

                var compound = new StaticCompoundShape
                {
                    ProxyIndex = obj.ProxyIndex,
                    DataOffset = obj.DataOffset,
                };

                int arrayField = (int)obj.DataOffset + instancesArrayOffset;
                if (arrayField + ptrSize + 8 > DataPayload.Length)
                {
                    StaticCompoundShapes.Add(compound);
                    continue;
                }

                // hkArray: pointer (ignored in file — resolved via local fixup), size, capacity|flags
                int sizePos = arrayField + ptrSize;
                int count = BitConverter.ToInt32(DataPayload, sizePos);
                if (count < 0) count = 0;

                uint arrayDataOffset = 0;
                bool foundLocal = false;
                for (int f = 0; f < LocalFixups.Count; f++)
                {
                    if (LocalFixups[f].Src == (uint)arrayField)
                    {
                        arrayDataOffset = LocalFixups[f].Dst;
                        foundLocal = true;
                        break;
                    }
                }

                if (foundLocal && count > 0)
                {
                    for (int n = 0; n < count; n++)
                    {
                        uint instOff = arrayDataOffset + (uint)(n * instanceStride);
                        if (instOff + instanceStride > DataPayload.Length)
                            break;

                        var inst = ReadCompoundInstance(instOff, ptrSize, globalBySrc, classAtOffset);
                        compound.AddInstance(inst);
                    }
                }

                // Domain AABB sits in the embedded tree header (after the nodes hkArray field).
                if (TryGetCompoundTreeField(compound.DataOffset, out uint treeField, out uint domainOff))
                {
                    if (domainOff + 32 <= (uint)DataPayload.Length)
                    {
                        compound.DomainMin = ReadVector4(DataPayload, (int)domainOff);
                        compound.DomainMax = ReadVector4(DataPayload, (int)domainOff + 16);
                    }
                }

                StaticCompoundShapes.Add(compound);
            }
        }

        bool TryGetCompoundArrayFields(uint compoundDataOffset, out uint instancesField, out uint treeField)
        {
            instancesField = 0;
            treeField = 0;
            var fields = new List<uint>();
            uint end = compoundDataOffset + 0x100;
            for (int i = 0; i < LocalFixups.Count; i++)
            {
                uint src = LocalFixups[i].Src;
                if (src >= compoundDataOffset && src < end)
                    fields.Add(src);
            }
            fields.Sort();
            if (fields.Count < 2)
                return false;
            instancesField = fields[0];
            treeField = fields[1];
            return true;
        }

        bool TryGetCompoundTreeField(uint compoundDataOffset, out uint treeField, out uint domainOffset)
        {
            domainOffset = 0;
            if (!TryGetCompoundArrayFields(compoundDataOffset, out _, out treeField))
                return false;
            // nodes hkArray then pad to 16, then domain AABB (32 bytes).
            uint afterArray = treeField + (uint)Header.PointerSize + 8;
            domainOffset = (afterArray + 15u) & ~15u;
            return true;
        }

        void RewriteCompoundArrays(StaticCompoundShape compound)
        {
            int ptrSize = Header.PointerSize;
            int instanceStride = ptrSize == 8 ? 80 : 64;
            int count = compound.Instances.Count;

            if (!TryGetCompoundArrayFields(compound.DataOffset, out uint instancesField, out uint treeField))
                throw new InvalidOperationException("Could not locate compound instance/tree array fields.");
            if (!TryGetCompoundTreeField(compound.DataOffset, out _, out uint domainOffset))
                throw new InvalidOperationException("Could not locate compound domain AABB.");

            // Build Storage6 BVH with quantized child AABBs (retail-style). All-zero xyz on
            // non-root nodes is unsafe — Havok treats those as empty during mid-phase queries.
            List<byte[]> nodes = count > 0
                ? BuildStorage6Tree(compound)
                : new List<byte[]>();

            int instancesBytes = count * instanceStride;
            int nodesBytes = nodes.Count * 6;
            int nodesAligned = nodesBytes == 0 ? 0 : ((nodesBytes + 15) & ~15);

            // Append new blobs at end of data payload (old arrays orphaned).
            int newInstancesOff = AlignPayload(DataPayload.Length, 16);
            int newNodesOff = AlignPayload(newInstancesOff + Math.Max(instancesBytes, 0), 16);
            int newPayloadLen = newNodesOff + nodesAligned;
            // Always grow at least to the instances write cursor so size-0 still has a stable Dst.
            if (newPayloadLen < newInstancesOff)
                newPayloadLen = newInstancesOff;

            byte[] grown = new byte[newPayloadLen];
            Buffer.BlockCopy(DataPayload, 0, grown, 0, DataPayload.Length);
            DataPayload = grown;

            for (int n = 0; n < count; n++)
            {
                int o = newInstancesOff + n * instanceStride;
                CompoundInstance inst = compound.Instances[n];
                WriteCompoundInstanceBytes(DataPayload, o, inst, ptrSize);
                inst.DataOffset = (uint)o;

                uint shapePtrField = (uint)(o + 48);
                GlobalFixups.Add(new GlobalFixup
                {
                    Src = shapePtrField,
                    DstSectionIndex = 2,
                    Dst = inst.ShapeDataOffset,
                });
            }

            for (int n = 0; n < nodes.Count; n++)
                Buffer.BlockCopy(nodes[n], 0, DataPayload, newNodesOff + n * 6, 6);

            // Update hkArray size/capacity and local fixups.
            WriteUInt32(DataPayload, (int)instancesField + ptrSize, (uint)count);
            WriteUInt32(DataPayload, (int)instancesField + ptrSize + 4, (uint)count | 0x80000000u);
            WriteUInt32(DataPayload, (int)treeField + ptrSize, (uint)nodes.Count);
            WriteUInt32(DataPayload, (int)treeField + ptrSize + 4, (uint)nodes.Count | 0x80000000u);

            // size 0 arrays: Havok still accepts a local fixup Dst; point at the append cursor.
            SetLocalFixupDst(instancesField, (uint)newInstancesOff);
            SetLocalFixupDst(treeField, (uint)(nodes.Count > 0 ? newNodesOff : newInstancesOff));

            // Domain AABB in embedded tree header (leave as-is if empty — still valid bounds).
            WriteVector4(DataPayload, (int)domainOffset, compound.DomainMin);
            WriteVector4(DataPayload, (int)domainOffset + 16, compound.DomainMax);
        }

        void SetLocalFixupDst(uint src, uint dst)
        {
            for (int i = 0; i < LocalFixups.Count; i++)
            {
                if (LocalFixups[i].Src == src)
                {
                    LocalFixup lf = LocalFixups[i];
                    lf.Dst = dst;
                    LocalFixups[i] = lf;
                    return;
                }
            }
            LocalFixups.Add(new LocalFixup { Src = src, Dst = dst });
        }

        void WriteCompoundInstanceBytes(byte[] data, int offset, CompoundInstance inst, int ptrSize)
        {
            int o = offset;
            WriteVector4(data, o, inst.Translation); o += 16;
            WriteSingle(data, o, inst.Rotation.X); o += 4;
            WriteSingle(data, o, inst.Rotation.Y); o += 4;
            WriteSingle(data, o, inst.Rotation.Z); o += 4;
            WriteSingle(data, o, inst.Rotation.W); o += 4;
            WriteVector4(data, o, inst.Scale); o += 16;
            // shape pointer zeroed; global fixup carries the link
            if (ptrSize == 8)
            {
                WriteUInt64(data, o, 0); o += 8;
            }
            else
            {
                WriteUInt32(data, o, 0); o += 4;
            }
            WriteUInt32(data, o, inst.FilterInfo); o += 4;
            WriteUInt32(data, o, inst.ChildFilterInfoMask); o += 4;
            if (ptrSize == 8)
                WriteUInt64(data, o, inst.UserData);
            else
                WriteUInt32(data, o, (uint)inst.UserData);
        }

        /// <summary>
        /// Build a balanced Storage6 BVH with 2N-1 nodes. Leaf AABBs come from each instance's
        /// referenced child compound domain (transformed into host space). Each node's three
        /// <c>xyz</c> bytes hold the child AABB compressed against the parent AABB — see
        /// <see cref="EncodeCodec3Axis"/>.
        /// </summary>
        List<byte[]> BuildStorage6Tree(StaticCompoundShape compound)
        {
            int leafCount = compound.Instances.Count;
            var leafAabbs = new (Vector3 Min, Vector3 Max)[leafCount];
            var shapeByOffset = new Dictionary<uint, StaticCompoundShape>();
            for (int i = 0; i < StaticCompoundShapes.Count; i++)
            {
                StaticCompoundShape s = StaticCompoundShapes[i];
                if (s != null && !shapeByOffset.ContainsKey(s.DataOffset))
                    shapeByOffset[s.DataOffset] = s;
            }

            Vector3 unionMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 unionMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            for (int i = 0; i < leafCount; i++)
            {
                CompoundInstance inst = compound.Instances[i];
                Vector3 t = new Vector3(inst.Translation.X, inst.Translation.Y, inst.Translation.Z);
                Vector3 s = new Vector3(inst.Scale.X, inst.Scale.Y, inst.Scale.Z);
                if (shapeByOffset.TryGetValue(inst.ShapeDataOffset, out StaticCompoundShape child)
                    && child.DomainMin.X <= child.DomainMax.X)
                {
                    GetTransformedAabb(
                        new Vector3(child.DomainMin.X, child.DomainMin.Y, child.DomainMin.Z),
                        new Vector3(child.DomainMax.X, child.DomainMax.Y, child.DomainMax.Z),
                        t, inst.Rotation, s,
                        out leafAabbs[i].Min, out leafAabbs[i].Max);
                }
                else if (string.Equals(inst.ShapeClassName, "hkpBoxShape", StringComparison.Ordinal)
                    && TryGetBoxHalfExtents(inst.ShapeDataOffset, out Vector3 he))
                {
                    GetTransformedAabb(-he, he, t, inst.Rotation, s,
                        out leafAabbs[i].Min, out leafAabbs[i].Max);
                }
                else
                {
                    // Point / unknown shape — small pad around the instance origin.
                    Vector3 pad = new Vector3(0.5f, 0.5f, 0.5f);
                    leafAabbs[i].Min = t - pad;
                    leafAabbs[i].Max = t + pad;
                }

                unionMin = Vector3.Min(unionMin, leafAabbs[i].Min);
                unionMax = Vector3.Max(unionMax, leafAabbs[i].Max);
            }

            // Retail roots use xyz=(0,0,0): domain must tightly contain leaf AABBs. Rebuild from
            // the union so inflated ExpandDomain padding cannot shrink the root away from content.
            if (leafCount > 0 && unionMin.X <= unionMax.X)
            {
                const float domainPad = 0.01f;
                Vector3 pad = new Vector3(domainPad, domainPad, domainPad);
                unionMin -= pad;
                unionMax += pad;
                compound.DomainMin = new Vector4(unionMin.X, unionMin.Y, unionMin.Z, 0);
                compound.DomainMax = new Vector4(unionMax.X, unionMax.Y, unionMax.Z, 0);
            }

            Vector3 domainMin = new Vector3(compound.DomainMin.X, compound.DomainMin.Y, compound.DomainMin.Z);
            Vector3 domainMax = new Vector3(compound.DomainMax.X, compound.DomainMax.Y, compound.DomainMax.Z);

            for (int i = 0; i < leafCount; i++)
            {
                leafAabbs[i].Min = Vector3.Clamp(leafAabbs[i].Min, domainMin, domainMax);
                leafAabbs[i].Max = Vector3.Clamp(leafAabbs[i].Max, domainMin, domainMax);
            }

            var leafIndices = new List<int>(leafCount);
            for (int i = 0; i < leafCount; i++)
                leafIndices.Add(i);

            var nodes = new List<byte[]>();
            if (leafCount > 0)
                BuildStorage6Recursive(nodes, leafIndices, leafAabbs, domainMin, domainMax);
            VerifyStorage6Tree(nodes, leafAabbs, domainMin, domainMax);
            return nodes;
        }

        /// <summary>
        /// Walk the built tree exactly as Havok will and check every leaf's decoded AABB still
        /// contains its instance. A clipped leaf is invisible to mid-phase queries, which shows up
        /// in game as geometry you fall straight through.
        /// </summary>
        static void VerifyStorage6Tree(
            List<byte[]> nodes,
            (Vector3 Min, Vector3 Max)[] leafAabbs,
            Vector3 domainMin,
            Vector3 domainMax)
        {
            if (nodes.Count == 0)
                return;

            int clipped = 0;
            float worst = 0f;
            var stack = new Stack<(int Index, Vector3 Min, Vector3 Max)>();
            stack.Push((0, domainMin, domainMax));
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                byte[] node = nodes[cur.Index];
                DecodeCodec3Axis(cur.Min, cur.Max, node[0], node[1], node[2],
                    out Vector3 min, out Vector3 max);

                int payload = node[4] | (node[5] << 8);
                if ((node[3] & 0x80) == 0)
                {
                    var leaf = leafAabbs[payload];
                    float over = Math.Max(
                        Math.Max(Math.Max(min.X - leaf.Min.X, min.Y - leaf.Min.Y), min.Z - leaf.Min.Z),
                        Math.Max(Math.Max(leaf.Max.X - max.X, leaf.Max.Y - max.Y), leaf.Max.Z - max.Z));
                    if (over > 1e-4f)
                    {
                        clipped++;
                        if (over > worst) worst = over;
                    }
                    continue;
                }

                stack.Push((cur.Index + 1, min, max));
                stack.Push((cur.Index + 2 * payload, min, max));
            }

            if (clipped > 0)
                Console.WriteLine("  WARNING: " + clipped + " BVH leaves clipped (worst " + worst + "m)");
        }

        static int BuildStorage6Recursive(
            List<byte[]> nodes,
            List<int> leaves,
            (Vector3 Min, Vector3 Max)[] leafAabbs,
            Vector3 parentMin,
            Vector3 parentMax)
        {
            int nodeIndex = nodes.Count;
            nodes.Add(new byte[6]); // placeholder

            Vector3 trueMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 trueMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < leaves.Count; i++)
            {
                var a = leafAabbs[leaves[i]];
                trueMin = Vector3.Min(trueMin, a.Min);
                trueMax = Vector3.Max(trueMax, a.Max);
            }
            trueMin = Vector3.Max(trueMin, parentMin);
            trueMax = Vector3.Min(trueMax, parentMax);

            byte qx = EncodeCodec3Axis(parentMin.X, parentMax.X, trueMin.X, trueMax.X);
            byte qy = EncodeCodec3Axis(parentMin.Y, parentMax.Y, trueMin.Y, trueMax.Y);
            byte qz = EncodeCodec3Axis(parentMin.Z, parentMax.Z, trueMin.Z, trueMax.Z);

            // Children are compressed against the box the runtime will decode, not the tight one.
            DecodeCodec3Axis(parentMin, parentMax, qx, qy, qz, out Vector3 nodeMin, out Vector3 nodeMax);

            if (leaves.Count == 1)
            {
                nodes[nodeIndex] = EncodeStorage6Node(qx, qy, qz, 0, (ushort)leaves[0]);
                return 1; // leaf count
            }

            // Split along longest axis of the true (tight) AABB.
            Vector3 extent = trueMax - trueMin;
            int axis = 0;
            if (extent.Y > extent.X) axis = 1;
            if (extent.Z > ((axis == 0) ? extent.X : extent.Y)) axis = 2;

            leaves.Sort((a, b) =>
            {
                float ca = Centre(leafAabbs[a], axis);
                float cb = Centre(leafAabbs[b], axis);
                return ca.CompareTo(cb);
            });

            int mid = leaves.Count / 2;
            if (mid < 1) mid = 1;
            if (mid >= leaves.Count) mid = leaves.Count - 1;
            var left = leaves.GetRange(0, mid);
            var right = leaves.GetRange(mid, leaves.Count - mid);

            int leftLeaves = BuildStorage6Recursive(nodes, left, leafAabbs, nodeMin, nodeMax);
            int rightLeaves = BuildStorage6Recursive(nodes, right, leafAabbs, nodeMin, nodeMax);
            _ = rightLeaves;

            // Storage6 loData = number of LEAVES in the left subtree (not node count).
            // Traversal: left = i+1, right = i + 2*loData.
            nodes[nodeIndex] = EncodeStorage6Node(qx, qy, qz, 0x80, (ushort)leftLeaves);
            return leftLeaves + rightLeaves;
        }

        static float Centre((Vector3 Min, Vector3 Max) a, int axis)
        {
            switch (axis)
            {
                case 1: return (a.Min.Y + a.Max.Y) * 0.5f;
                case 2: return (a.Min.Z + a.Max.Z) * 0.5f;
                default: return (a.Min.X + a.Max.X) * 0.5f;
            }
        }

        /// <summary>
        /// Codec3Axis packs one axis of a child AABB into a single byte, as two 4-bit insets
        /// measured from each end of the parent AABB. Both insets are squared on decode:
        /// <c>childMin = parentMin + (hi/15)^2 * extent</c> and
        /// <c>childMax = parentMax - (lo/15)^2 * extent</c>, giving fine resolution where a child
        /// hugs the parent bounds and coarse resolution in the middle. A collapsed axis stores
        /// 255, which decodes back to the (zero-width) parent range.
        /// </summary>
        static byte EncodeCodec3Axis(float parentMin, float parentMax, float childMin, float childMax)
        {
            float extent = parentMax - parentMin;
            if (Math.Abs(extent) < 1e-6f)
                return 255;
            int hi = Codec3AxisNibble((childMin - parentMin) / extent);
            int lo = Codec3AxisNibble((parentMax - childMax) / extent);
            return (byte)((hi << 4) | lo);
        }

        /// <summary>Largest nibble whose squared inset still stays outside the child bound.</summary>
        static int Codec3AxisNibble(float inset)
        {
            if (!(inset > 0f)) return 0;
            if (inset >= 1f) return 15;
            int n = (int)(15.0 * Math.Sqrt(inset));
            if (n > 15) n = 15;
            while (n > 0 && (n / 15.0) * (n / 15.0) > inset) n--;
            return n;
        }

        static void DecodeCodec3Axis(
            Vector3 parentMin, Vector3 parentMax, byte qx, byte qy, byte qz,
            out Vector3 childMin, out Vector3 childMax)
        {
            DecodeCodec3AxisComponent(parentMin.X, parentMax.X, qx, out float minX, out float maxX);
            DecodeCodec3AxisComponent(parentMin.Y, parentMax.Y, qy, out float minY, out float maxY);
            DecodeCodec3AxisComponent(parentMin.Z, parentMax.Z, qz, out float minZ, out float maxZ);
            childMin = new Vector3(minX, minY, minZ);
            childMax = new Vector3(maxX, maxY, maxZ);
        }

        static void DecodeCodec3AxisComponent(float parentMin, float parentMax, byte q, out float min, out float max)
        {
            float extent = parentMax - parentMin;
            float hi = (q >> 4) / 15f;
            float lo = (q & 0xF) / 15f;
            min = parentMin + hi * hi * extent;
            max = parentMax - lo * lo * extent;
        }

        [Obsolete("Use BuildStorage6Tree — all-zero xyz mid-phase is unsafe for non-root nodes.")]
        static List<byte[]> BuildLooseStorage6Tree(int leafCount)
        {
            var nodes = new List<byte[]>();
            if (leafCount <= 0)
                return nodes;

            var leafIndices = new List<int>(leafCount);
            for (int i = 0; i < leafCount; i++)
                leafIndices.Add(i);

            BuildLooseStorage6Recursive(nodes, leafIndices);
            return nodes;
        }

        static int BuildLooseStorage6Recursive(List<byte[]> nodes, List<int> leaves)
        {
            int nodeIndex = nodes.Count;
            nodes.Add(new byte[6]); // placeholder

            if (leaves.Count == 1)
            {
                nodes[nodeIndex] = EncodeStorage6Node(0, 0, 0, 0, (ushort)leaves[0]);
                return 1; // leaf count
            }

            int mid = leaves.Count / 2;
            if (mid < 1) mid = 1;
            var left = leaves.GetRange(0, mid);
            var right = leaves.GetRange(mid, leaves.Count - mid);

            int leftLeaves = BuildLooseStorage6Recursive(nodes, left);
            int rightLeaves = BuildLooseStorage6Recursive(nodes, right);
            _ = rightLeaves;

            nodes[nodeIndex] = EncodeStorage6Node(0, 0, 0, 0x80, (ushort)leftLeaves);
            return leftLeaves + rightLeaves;
        }

        static byte[] EncodeStorage6Node(byte x, byte y, byte z, byte hi, ushort lo)
        {
            return new byte[]
            {
                x, y, z, hi,
                (byte)(lo & 0xFF),
                (byte)((lo >> 8) & 0xFF),
            };
        }

        static int AlignPayload(int offset, int align)
        {
            int mask = align - 1;
            return (offset + mask) & ~mask;
        }

        static uint FloatToUInt(float f)
        {
            return BitConverter.ToUInt32(BitConverter.GetBytes(f), 0);
        }

        static float UIntToFloat(uint u)
        {
            return BitConverter.ToSingle(BitConverter.GetBytes(u), 0);
        }

        CompoundInstance ReadCompoundInstance(
            uint offset,
            int ptrSize,
            Dictionary<uint, uint> globalBySrc,
            Dictionary<uint, string> classAtOffset)
        {
            int o = (int)offset;
            var inst = new CompoundInstance { DataOffset = offset };

            inst.Translation = ReadVector4(DataPayload, o); o += 16;
            inst.Rotation = new Quaternion(
                BitConverter.ToSingle(DataPayload, o),
                BitConverter.ToSingle(DataPayload, o + 4),
                BitConverter.ToSingle(DataPayload, o + 8),
                BitConverter.ToSingle(DataPayload, o + 12));
            o += 16;
            inst.Scale = ReadVector4(DataPayload, o); o += 16;

            uint shapePtrField = offset + 48;
            if (globalBySrc.TryGetValue(shapePtrField, out uint shapeOff))
            {
                inst.ShapeDataOffset = shapeOff;
                classAtOffset.TryGetValue(shapeOff, out string shapeClass);
                inst.ShapeClassName = shapeClass ?? "";
            }

            o = (int)offset + 48 + ptrSize;
            inst.FilterInfo = BitConverter.ToUInt32(DataPayload, o); o += 4;
            inst.ChildFilterInfoMask = BitConverter.ToUInt32(DataPayload, o); o += 4;
            if (ptrSize == 8)
            {
                inst.UserData = BitConverter.ToUInt64(DataPayload, o);
            }
            else
            {
                inst.UserData = BitConverter.ToUInt32(DataPayload, o);
            }

            return inst;
        }

        void WriteBackCompoundInstances()
        {
            int ptrSize = Header.PointerSize;
            for (int c = 0; c < StaticCompoundShapes.Count; c++)
            {
                StaticCompoundShape compound = StaticCompoundShapes[c];
                for (int n = 0; n < compound.Instances.Count; n++)
                {
                    CompoundInstance inst = compound.Instances[n];
                    int o = (int)inst.DataOffset;
                    if (o < 0 || o + 48 + ptrSize + 8 > DataPayload.Length)
                        continue;

                    WriteVector4(DataPayload, o, inst.Translation); o += 16;
                    WriteSingle(DataPayload, o, inst.Rotation.X); o += 4;
                    WriteSingle(DataPayload, o, inst.Rotation.Y); o += 4;
                    WriteSingle(DataPayload, o, inst.Rotation.Z); o += 4;
                    WriteSingle(DataPayload, o, inst.Rotation.W); o += 4;
                    WriteVector4(DataPayload, o, inst.Scale); o += 16;

                    // Shape pointer stays zero in-file; global fixup table owns the link.
                    o = (int)inst.DataOffset + 48 + ptrSize;
                    WriteUInt32(DataPayload, o, inst.FilterInfo); o += 4;
                    WriteUInt32(DataPayload, o, inst.ChildFilterInfoMask); o += 4;
                    if (ptrSize == 8)
                        WriteUInt64(DataPayload, o, inst.UserData);
                    else
                        WriteUInt32(DataPayload, o, (uint)inst.UserData);
                }
            }
        }

        static Vector4 ReadVector4(byte[] data, int offset)
        {
            return new Vector4(
                BitConverter.ToSingle(data, offset),
                BitConverter.ToSingle(data, offset + 4),
                BitConverter.ToSingle(data, offset + 8),
                BitConverter.ToSingle(data, offset + 12));
        }

        static void WriteVector4(byte[] data, int offset, Vector4 v)
        {
            WriteSingle(data, offset, v.X);
            WriteSingle(data, offset + 4, v.Y);
            WriteSingle(data, offset + 8, v.Z);
            WriteSingle(data, offset + 12, v.W);
        }

        static void WriteSingle(byte[] data, int offset, float v)
        {
            byte[] bytes = BitConverter.GetBytes(v);
            Buffer.BlockCopy(bytes, 0, data, offset, 4);
        }

        static void WriteUInt32(byte[] data, int offset, uint v)
        {
            byte[] bytes = BitConverter.GetBytes(v);
            Buffer.BlockCopy(bytes, 0, data, offset, 4);
        }

        static void WriteUInt64(byte[] data, int offset, ulong v)
        {
            byte[] bytes = BitConverter.GetBytes(v);
            Buffer.BlockCopy(bytes, 0, data, offset, 8);
        }

        static ObjectClass Classify(string className)
        {
            if (string.IsNullOrEmpty(className))
                return ObjectClass.Unknown;
            switch (className)
            {
                case "hkRootLevelContainer": return ObjectClass.RootLevelContainer;
                case "hkpPhysicsData": return ObjectClass.PhysicsData;
                case "hkpPhysicsSystem": return ObjectClass.PhysicsSystem;
                case "hkpWorldCinfo": return ObjectClass.WorldCinfo;
                case "hkpGroupFilter": return ObjectClass.GroupFilter;
                case "hkpDefaultConvexListFilter": return ObjectClass.DefaultConvexListFilter;
                case "hkpRigidBody": return ObjectClass.RigidBody;
                case "hkpListShape": return ObjectClass.ListShape;
                case "hkpStaticCompoundShape": return ObjectClass.StaticCompoundShape;
                case "hkpBvCompressedMeshShape": return ObjectClass.BvCompressedMeshShape;
                case "hkpBoxShape": return ObjectClass.BoxShape;
                default: return ObjectClass.Unknown;
            }
        }
        #endregion
    }
}
