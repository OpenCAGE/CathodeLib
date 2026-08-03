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

        public List<LocalFixup> LocalFixups = new List<LocalFixup>();
        public List<GlobalFixup> GlobalFixups = new List<GlobalFixup>();
        public List<VirtualFixup> VirtualFixups = new List<VirtualFixup>();

        public List<PackfileObject> Objects = new List<PackfileObject>();

        /// <summary>Typed hkpStaticCompoundShape views in CollisionProxyIndex order.</summary>
        public List<StaticCompoundShape> StaticCompoundShapes = new List<StaticCompoundShape>();

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

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            Objects.Clear();
            StaticCompoundShapes.Clear();
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

                ReadLocalFixups(file, (int)(dataAbs + dataLocal), (int)(dataAbs + dataGlobal));
                ReadGlobalFixups(file, (int)(dataAbs + dataGlobal), (int)(dataAbs + dataVirtual));
                ReadVirtualFixups(file, (int)(dataAbs + dataVirtual), (int)(dataAbs + dataExports));

                Dictionary<int, string> classNames = ParseClassNames(ClassnamesData);
                BuildObjectList(classNames);
                ParseStaticCompoundInstances(classNames);
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
                Objects.Add(obj);
            }
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
