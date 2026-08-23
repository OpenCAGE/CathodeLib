#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Numerics;
using CATHODE;
using CATHODE.Enums;
using CATHODE.Scripting;
using CathodeLib;

namespace CathodeLib.NavMesh
{
    /// <summary>
    /// World-space triangle soup collected from instanced collision for Recast,
    /// plus bake-time authoring volumes (barriers / platforms / exclusions).
    /// </summary>
    public sealed class CollisionNavMeshSoup
    {
        public float[] Verts = Array.Empty<float>();
        public int[] Tris = Array.Empty<int>();
        public int TriangleCount => Tris.Length / 3;
        public int VertexCount => Verts.Length / 3;
        public List<Vector3> ReachabilitySeeds = new List<Vector3>();
        public List<BarrierVolume> Barriers = new List<BarrierVolume>();
        public List<AuthoringBoxVolume> WalkablePlatforms = new List<AuthoringBoxVolume>();
        public List<AuthoringBoxVolume> ExclusionAreas = new List<AuthoringBoxVolume>();
        public List<OffMeshLinkDraft> OffMeshLinks = new List<OffMeshLinkDraft>();
        public List<BackstageNodeDraft> BackstageNodes = new List<BackstageNodeDraft>();
        /// <summary>Bake-host MAP instances skipped as small props (crate-scale).</summary>
        public int PropInstancesSkipped;
        /// <summary>Soup tris dropped as absurd edge-length outliers.</summary>
        public int AbsurdTrisCulled;

        /// <summary>Oriented box used for NavMeshBarrier PATH_CLOSED volumes.</summary>
        public sealed class BarrierVolume
        {
            public int AreaId;
            public Vector3 Centre;
            public Quaternion Rotation = Quaternion.Identity;
            public Vector3 HalfExtents;
            public Resources.Resource Resource;
            public NAVIGATION_CHARACTER_CLASS_COMBINATION InitialClasses =
                NAVIGATION_CHARACTER_CLASS_COMBINATION.ALL;
            public EntityHandle Entity;
            public ShortGuid ResourceGuid;
        }

        /// <summary>Oriented box for walkable platforms / exclusion areas.</summary>
        public sealed class AuthoringBoxVolume
        {
            public Vector3 Centre;
            public Quaternion Rotation = Quaternion.Identity;
            public Vector3 HalfExtents;
        }

        /// <summary>
        /// A PathfindingAlienBackstageNode: <see cref="Bottom"/> is the entity position (the
        /// frontstage vent mouth). The sheet vertex sits a fixed height straight above it
        /// (<see cref="NavMeshBakeSettings.BackstageNodeHeight"/>); each node also becomes a
        /// vertical Backstage off-mesh connection between the two.
        /// </summary>
        public sealed class BackstageNodeDraft
        {
            public Vector3 Bottom;
            public float ExtraCost = 1f;
            public bool OpenOnReset = true;
            /// <summary>Nodes triangulate only with others sharing their network id.</summary>
            public int NetworkId;
            public EntityHandle Entity;
        }

        public sealed class OffMeshLinkDraft
        {
            public Vector3 Start;
            public Vector3 End;
            public NavigationMesh.OffMeshLinkType LinkType;
            public float ExtraCost = 1f;
            public NAVIGATION_CHARACTER_CLASS_COMBINATION CharacterClasses =
                NAVIGATION_CHARACTER_CLASS_COMBINATION.ALL;
            public bool OpenOnReset = true;
            public EntityHandle Entity;
            public float Radius = 0.3f;
        }

        /// <summary>
        /// Collect walkable collision from the WORLD host compound (secondary), excluding
        /// PATH_CLOSED barrier boxes (and optional ExclusiveMaster state excludes),
        /// plus platforms / exclusions / seeds from Commands.
        /// </summary>
        /// <param name="extraSkip">
        /// Additional compound instances to omit (e.g. ExclusiveMaster inactive geometry).
        /// </param>
        /// <param name="sharedAuthoring">
        /// When non-null, reuses Barriers / platforms / exclusions / seeds instead of re-scanning.
        /// </param>
        public static CollisionNavMeshSoup CollectFromLevel(
            Level level,
            ISet<HavokPackfile.CompoundInstance> extraSkip = null,
            CollisionNavMeshSoup sharedAuthoring = null,
            NavMeshBakeSettings settings = null,
            Instancing placement = null)
        {
            if (level == null)
                throw new ArgumentNullException(nameof(level));

            settings ??= NavMeshBakeSettings.CreateDefault();
            var soup = new CollisionNavMeshSoup();
            HavokPackfile hkx = level.Collision;
            if (hkx == null)
                throw new InvalidOperationException("Level has no collision HKX packfile.");

            HavokPackfile.StaticCompoundShape host = ResolveBakeHost(hkx);
            if (host == null)
                throw new InvalidOperationException("Collision HKX has no world host compound.");

            var skip = new HashSet<HavokPackfile.CompoundInstance>();
            if (sharedAuthoring != null)
            {
                soup.Barriers = sharedAuthoring.Barriers ?? soup.Barriers;
                // Rebuild PATH_CLOSED skip only (barrier volume list already filled).
                Dictionary<uint, FunctionEntity> barrierEntities = IndexBarrierEntities(level);
                CollectBarrierVolumes(level, hkx, host, barrierEntities, new List<BarrierVolume>(), skip);
            }
            else
            {
                Dictionary<uint, FunctionEntity> barrierEntities = IndexBarrierEntities(level);
                CollectBarrierVolumes(level, hkx, host, barrierEntities, soup.Barriers, skip);
            }

            CollectSoundBarrierSkip(level, skip);

            if (settings.SkipGhostedCollision)
                CollectGhostedSkip(level, skip);

            if (settings.SkipSmallPropCollision)
                soup.PropInstancesSkipped = CollectSmallPropSkip(level, hkx, host, settings, skip);

            if (extraSkip != null)
            {
                foreach (HavokPackfile.CompoundInstance inst in extraSkip)
                    if (inst != null)
                        skip.Add(inst);
            }

            HavokPackfile.PreviewMesh mesh = hkx.BuildBakeMesh(host, skip);
            if (mesh.Positions.Count == 0 || mesh.Indices.Count < 3)
                throw new InvalidOperationException("Bake mesh from world host produced no triangles.");

            if (sharedAuthoring != null)
            {
                soup.WalkablePlatforms = sharedAuthoring.WalkablePlatforms ?? soup.WalkablePlatforms;
                soup.ExclusionAreas = sharedAuthoring.ExclusionAreas ?? soup.ExclusionAreas;
                soup.ReachabilitySeeds = sharedAuthoring.ReachabilitySeeds ?? soup.ReachabilitySeeds;
                soup.OffMeshLinks = sharedAuthoring.OffMeshLinks ?? soup.OffMeshLinks;
                soup.BackstageNodes = sharedAuthoring.BackstageNodes ?? soup.BackstageNodes;
            }
            else
            {
                CollectAuthoringVolumes(level, soup, placement);
            }

            foreach (AuthoringBoxVolume platform in soup.WalkablePlatforms)
                AppendOrientedBox(mesh, platform.Centre, platform.Rotation, platform.HalfExtents);

            soup.Verts = new float[mesh.Positions.Count * 3];
            for (int i = 0; i < mesh.Positions.Count; i++)
            {
                Vector3 p = mesh.Positions[i];
                soup.Verts[i * 3 + 0] = p.X;
                soup.Verts[i * 3 + 1] = p.Y;
                soup.Verts[i * 3 + 2] = p.Z;
            }
            soup.Tris = mesh.Indices.ToArray();

            if (settings.CullAbsurdSoupTris)
                CullAbsurdSoupTriangles(soup, settings.MaxAbsurdSoupEdge);

            return soup;
        }

        /// <summary>
        /// Remove tris with an edge longer than <paramref name="maxEdge"/> (decode / domain junk).
        /// </summary>
        static void CullAbsurdSoupTriangles(CollisionNavMeshSoup soup, float maxEdge)
        {
            maxEdge = Math.Max(1f, maxEdge);
            float maxEdgeSq = maxEdge * maxEdge;
            var keep = new List<int>(soup.Tris.Length);
            int culled = 0;

            for (int t = 0; t + 2 < soup.Tris.Length; t += 3)
            {
                int i0 = soup.Tris[t], i1 = soup.Tris[t + 1], i2 = soup.Tris[t + 2];
                if (i0 < 0 || i1 < 0 || i2 < 0
                    || i0 >= soup.VertexCount || i1 >= soup.VertexCount || i2 >= soup.VertexCount)
                {
                    culled++;
                    continue;
                }

                Vector3 a = new Vector3(soup.Verts[i0 * 3], soup.Verts[i0 * 3 + 1], soup.Verts[i0 * 3 + 2]);
                Vector3 b = new Vector3(soup.Verts[i1 * 3], soup.Verts[i1 * 3 + 1], soup.Verts[i1 * 3 + 2]);
                Vector3 c = new Vector3(soup.Verts[i2 * 3], soup.Verts[i2 * 3 + 1], soup.Verts[i2 * 3 + 2]);
                float e0 = Vector3.DistanceSquared(a, b);
                float e1 = Vector3.DistanceSquared(b, c);
                float e2 = Vector3.DistanceSquared(c, a);
                if (e0 > maxEdgeSq || e1 > maxEdgeSq || e2 > maxEdgeSq)
                {
                    culled++;
                    continue;
                }

                keep.Add(i0);
                keep.Add(i1);
                keep.Add(i2);
            }

            soup.AbsurdTrisCulled = culled;
            if (culled > 0)
            {
                soup.Tris = keep.ToArray();
                CompactSoupVertices(soup);
            }
        }

        /// <summary>Drop unreferenced verts so orphan outliers cannot inflate Recast bounds.</summary>
        static void CompactSoupVertices(CollisionNavMeshSoup soup)
        {
            if (soup.VertexCount == 0 || soup.Tris.Length == 0)
            {
                soup.Verts = Array.Empty<float>();
                soup.Tris = Array.Empty<int>();
                return;
            }

            var used = new bool[soup.VertexCount];
            for (int i = 0; i < soup.Tris.Length; i++)
            {
                int vi = soup.Tris[i];
                if (vi >= 0 && vi < used.Length)
                    used[vi] = true;
            }

            var remap = new int[soup.VertexCount];
            var newVerts = new List<float>(soup.Verts.Length);
            for (int i = 0; i < soup.VertexCount; i++)
            {
                if (!used[i])
                {
                    remap[i] = -1;
                    continue;
                }
                remap[i] = newVerts.Count / 3;
                newVerts.Add(soup.Verts[i * 3]);
                newVerts.Add(soup.Verts[i * 3 + 1]);
                newVerts.Add(soup.Verts[i * 3 + 2]);
            }

            for (int i = 0; i < soup.Tris.Length; i++)
                soup.Tris[i] = remap[soup.Tris[i]];

            soup.Verts = newVerts.ToArray();
        }

        public static HavokPackfile.StaticCompoundShape ResolveBakeHost(HavokPackfile hkx)
        {
            // Secondary host = WORLD-flagged / walkable colliders (+ barrier boxes).
            return hkx.WorldHostSecondary ?? hkx.WorldHostPrimary;
        }

        /// <summary>
        /// Omit sound barriers. They occlude sound and nothing else - a character walks straight
        /// through one - so they must not carve holes in the navmesh.
        /// </summary>
        /// <remarks>
        /// A SoundBarrier entity is written as collision type SOUND or SOUND_BARRIER depending on
        /// its band_aid flag, and both mean the same thing here. Small-prop skipping used to be the
        /// only thing removing them, which left two gaps: it is gated on SkipSmallPropCollision, and
        /// it only drops boxes below crate scale, so a barrier spanning a doorway or window - the
        /// normal case - always survived into the soup.
        /// </remarks>
        static void CollectSoundBarrierSkip(Level level, HashSet<HavokPackfile.CompoundInstance> skip)
        {
            if (level?.CollisionMaps?.Entries == null || skip == null)
                return;

            foreach (CollisionMaps.COLLISION_MAPPING entry in level.CollisionMaps.Entries)
            {
                if (entry?.CollisionInstance == null)
                    continue;

                CollisionMaps.CollisionType type =
                    (CollisionMaps.CollisionType)((uint)entry.Flags & (uint)CollisionMaps.CollisionFlags.COLLISION_TYPE_MASK);
                if (type == CollisionMaps.CollisionType.SOUND || type == CollisionMaps.CollisionType.SOUND_BARRIER)
                    skip.Add(entry.CollisionInstance);
            }
        }

        /// <summary>
        /// Omit COLLISION.MAP instances that start ghosted (no solid collision at runtime).
        /// </summary>
        static void CollectGhostedSkip(Level level, HashSet<HavokPackfile.CompoundInstance> skip)
        {
            if (level?.CollisionMaps?.Entries == null || skip == null)
                return;

            const CollisionMaps.CollisionFlags ghostMask =
                CollisionMaps.CollisionFlags.GHOSTED | CollisionMaps.CollisionFlags.PRE_GHOSTED;

            foreach (CollisionMaps.COLLISION_MAPPING entry in level.CollisionMaps.Entries)
            {
                if (entry?.CollisionInstance == null)
                    continue;
                if ((entry.Flags & ghostMask) == 0)
                    continue;
                skip.Add(entry.CollisionInstance);
            }
        }

        /// <summary>
        /// Skip crate-scale bake-host colliders so Recast neither walks on their tops nor
        /// carves floor holes around them. Large structural blockers are kept.
        /// </summary>
        static int CollectSmallPropSkip(
            Level level,
            HavokPackfile hkx,
            HavokPackfile.StaticCompoundShape bakeHost,
            NavMeshBakeSettings settings,
            HashSet<HavokPackfile.CompoundInstance> skip)
        {
            if (level?.CollisionMaps?.Entries == null || hkx == null || bakeHost == null || skip == null)
                return 0;

            float maxXZ = Math.Max(0.05f, settings.SmallPropMaxXZExtent);
            float maxY = Math.Max(0.05f, settings.SmallPropMaxYExtent);
            int added = 0;

            foreach (CollisionMaps.COLLISION_MAPPING entry in level.CollisionMaps.Entries)
            {
                HavokPackfile.CompoundInstance inst = entry?.CollisionInstance;
                if (inst == null || inst.Owner == null || !ReferenceEquals(inst.Owner, bakeHost))
                    continue;
                if (skip.Contains(inst))
                    continue;

                CollisionMaps.CollisionType type =
                    (CollisionMaps.CollisionType)((uint)entry.Flags & (uint)CollisionMaps.CollisionFlags.COLLISION_TYPE_MASK);
                if (type == CollisionMaps.CollisionType.PATH_CLOSED)
                    continue;
                // Sound barriers are dropped wholesale by CollectSoundBarrierSkip, so SOUND is not
                // listed here - crate scale has nothing to do with why they leave the soup.
                if (type != CollisionMaps.CollisionType.STANDARD
                    && type != CollisionMaps.CollisionType.TRANSPARENT
                    && type != CollisionMaps.CollisionType.PLAYER_ONLY)
                    continue;

                // Only box shapes — BvCompressed / compound floor tiles often have small
                // domains and must remain in the soup.
                if (!string.Equals(inst.ShapeClassName, "hkpBoxShape", StringComparison.Ordinal))
                    continue;
                if (!hkx.TryGetBoxHalfExtents(inst.ShapeDataOffset, out Vector3 he))
                    continue;

                Vector3 scale = new Vector3(
                    Math.Abs(inst.Scale.X),
                    Math.Abs(inst.Scale.Y),
                    Math.Abs(inst.Scale.Z));
                Vector3 extents = new Vector3(he.X * scale.X * 2f, he.Y * scale.Y * 2f, he.Z * scale.Z * 2f);
                float xz = Math.Max(extents.X, extents.Z);
                if (xz < maxXZ && extents.Y < maxY)
                {
                    skip.Add(inst);
                    added++;
                }
            }

            return added;
        }

        static void CollectBarrierVolumes(
            Level level,
            HavokPackfile hkx,
            HavokPackfile.StaticCompoundShape bakeHost,
            Dictionary<uint, FunctionEntity> barrierEntities,
            List<BarrierVolume> barriers,
            HashSet<HavokPackfile.CompoundInstance> skip)
        {
            if (level.CollisionMaps?.Entries == null)
                return;

            ShortGuid openOnResetGuid = ShortGuids.open_on_reset;
            ShortGuid whenOpenGuid = ShortGuids.allowed_character_classes_when_open;
            ShortGuid whenClosedGuid = ShortGuids.allowed_character_classes_when_closed;

            int nextAreaId = 1;
            foreach (CollisionMaps.COLLISION_MAPPING entry in level.CollisionMaps.Entries)
            {
                CollisionMaps.CollisionType type =
                    (CollisionMaps.CollisionType)((uint)entry.Flags & (uint)CollisionMaps.CollisionFlags.COLLISION_TYPE_MASK);
                if (type != CollisionMaps.CollisionType.PATH_CLOSED)
                    continue;

                HavokPackfile.CompoundInstance inst = entry.CollisionInstance;
                if (inst == null)
                    continue;

                if (inst.Owner != null && ReferenceEquals(inst.Owner, bakeHost))
                    skip.Add(inst);

                Vector3 centre = new Vector3(inst.Translation.X, inst.Translation.Y, inst.Translation.Z);
                Vector3 halfExtents = new Vector3(0.5f, 1f, 0.5f);
                if (string.Equals(inst.ShapeClassName, "hkpBoxShape", StringComparison.Ordinal)
                    && hkx.TryGetBoxHalfExtents(inst.ShapeDataOffset, out Vector3 he))
                {
                    Vector3 scale = new Vector3(
                        Math.Abs(inst.Scale.X),
                        Math.Abs(inst.Scale.Y),
                        Math.Abs(inst.Scale.Z));
                    halfExtents = new Vector3(he.X * scale.X, he.Y * scale.Y, he.Z * scale.Z);
                }

                FunctionEntity barrierEnt = null;
                if (entry.Entity != null)
                    barrierEntities.TryGetValue(entry.Entity.entity_id.AsUInt32, out barrierEnt);
                if (barrierEnt == null && entry.ResourceGUID != ShortGuid.Invalid)
                    barrierEntities.TryGetValue(entry.ResourceGUID.AsUInt32, out barrierEnt);

                NAVIGATION_CHARACTER_CLASS_COMBINATION initial =
                    ResolveInitialClasses(barrierEnt, openOnResetGuid, whenOpenGuid, whenClosedGuid);

                Resources.Resource resource = ResolveOrAddResource(level, entry);

                if (nextAreaId > 511)
                    throw new InvalidOperationException("Barrier area id exceeds 9-bit dt_area_t limit (511).");

                barriers.Add(new BarrierVolume
                {
                    AreaId = nextAreaId++,
                    Centre = centre,
                    Rotation = inst.Rotation,
                    HalfExtents = halfExtents,
                    Resource = resource,
                    InitialClasses = initial,
                    Entity = entry.Entity,
                    ResourceGuid = entry.ResourceGUID
                });
            }
        }

        static Resources.Resource ResolveOrAddResource(Level level, CollisionMaps.COLLISION_MAPPING entry)
        {
            if (level.Resources == null || entry == null)
                return null;

            ShortGuid resourceId = entry.ResourceGUID;
            if (resourceId == ShortGuid.Invalid && entry.Entity != null)
                resourceId = entry.Entity.entity_id;
            if (resourceId == ShortGuid.Invalid)
                return null;

            ShortGuid compositeInstanceId = entry.Entity != null
                ? entry.Entity.composite_instance_id
                : ShortGuid.Invalid;

            return level.Resources.AddUniqueResource(resourceId, compositeInstanceId);
        }

        static NAVIGATION_CHARACTER_CLASS_COMBINATION ResolveInitialClasses(
            FunctionEntity barrierEnt,
            ShortGuid openOnResetGuid,
            ShortGuid whenOpenGuid,
            ShortGuid whenClosedGuid)
        {
            bool openOnReset = true;
            NAVIGATION_CHARACTER_CLASS_COMBINATION whenOpen = NAVIGATION_CHARACTER_CLASS_COMBINATION.ALL;
            NAVIGATION_CHARACTER_CLASS_COMBINATION whenClosed = NAVIGATION_CHARACTER_CLASS_COMBINATION.NONE;

            if (barrierEnt != null)
            {
                Parameter openParam = barrierEnt.GetParameter(openOnResetGuid);
                if (openParam?.content is cBool openBool)
                    openOnReset = openBool.value;

                Parameter openClasses = barrierEnt.GetParameter(whenOpenGuid);
                if (openClasses?.content is cEnum openEnum && openEnum.enumIndex >= 0)
                    whenOpen = (NAVIGATION_CHARACTER_CLASS_COMBINATION)openEnum.enumIndex;

                Parameter closedClasses = barrierEnt.GetParameter(whenClosedGuid);
                if (closedClasses?.content is cEnum closedEnum && closedEnum.enumIndex >= 0)
                    whenClosed = (NAVIGATION_CHARACTER_CLASS_COMBINATION)closedEnum.enumIndex;
            }

            return openOnReset ? whenOpen : whenClosed;
        }

        static Dictionary<uint, FunctionEntity> IndexBarrierEntities(Level level)
        {
            var map = new Dictionary<uint, FunctionEntity>();
            if (level.Commands?.Entries == null)
                return map;

            foreach (Composite composite in level.Commands.Entries)
            {
                if (composite == null)
                    continue;
                foreach (FunctionEntity function in composite.GetFunctionEntitiesOfType(FunctionType.NavMeshBarrier))
                {
                    if (function == null)
                        continue;
                    uint key = function.shortGUID.AsUInt32;
                    if (!map.ContainsKey(key))
                        map[key] = function;

                    Parameter resource = function.GetParameter(ShortGuids.resource);
                    if (resource?.content is cResource cRes && cRes.shortGUID != ShortGuid.Invalid)
                    {
                        uint resKey = cRes.shortGUID.AsUInt32;
                        if (!map.ContainsKey(resKey))
                            map[resKey] = function;
                    }
                }
            }
            return map;
        }

        /// <summary>Entry-point hierarchy walk collecting seeds, platforms and exclusion volumes.</summary>
        static void CollectAuthoringVolumes(Level level, CollisionNavMeshSoup soup, Instancing placement)
        {
            soup.ReachabilitySeeds ??= new List<Vector3>();
            soup.WalkablePlatforms ??= new List<AuthoringBoxVolume>();
            soup.ExclusionAreas ??= new List<AuthoringBoxVolume>();
            soup.ReachabilitySeeds.Clear();
            soup.WalkablePlatforms.Clear();
            soup.ExclusionAreas.Clear();

            if (level.Commands?.EntryPoints == null || level.Commands.EntryPoints.Length == 0)
            {
                soup.ReachabilitySeeds = CollectReachabilitySeedsLocal(level);
                NavMeshAuthoringCollector.Collect(level, soup, placement);
                return;
            }

            Composite root = level.Commands.EntryPoints[0];
            if (root == null)
            {
                soup.ReachabilitySeeds = CollectReachabilitySeedsLocal(level);
                NavMeshAuthoringCollector.Collect(level, soup, placement);
                return;
            }

            WalkComposite(level.Commands, root, Matrix4x4.Identity, soup);
            NavMeshAuthoringCollector.Collect(level, soup, placement);
        }

        static void WalkComposite(Commands commands, Composite composite, Matrix4x4 parentWorld, CollisionNavMeshSoup soup)
        {
            if (composite?.functions == null)
                return;

            foreach (FunctionEntity function in composite.functions)
            {
                if (function == null)
                    continue;

                Matrix4x4 local = LocalTransformMatrix(function);
                Matrix4x4 world = local * parentWorld;

                if (!function.function.IsFunctionType)
                {
                    Composite child = commands.GetComposite(function.function);
                    if (child != null)
                        WalkComposite(commands, child, world, soup);
                    continue;
                }

                FunctionType type = (FunctionType)function.function.AsUInt32;
                switch (type)
                {
                    case FunctionType.NavMeshWalkablePlatform:
                        soup.WalkablePlatforms.Add(MakeAuthoringBox(world, function));
                        break;
                    case FunctionType.NavMeshExclusionArea:
                        soup.ExclusionAreas.Add(MakeAuthoringBox(world, function));
                        break;
                    case FunctionType.NavMeshReachabilitySeedPoint:
                        if (Matrix4x4.Decompose(world, out _, out _, out Vector3 seedPos))
                            soup.ReachabilitySeeds.Add(seedPos);
                        else
                            soup.ReachabilitySeeds.Add(world.Translation);
                        break;
                }
            }
        }

        static AuthoringBoxVolume MakeAuthoringBox(Matrix4x4 world, FunctionEntity function)
        {
            if (!Matrix4x4.Decompose(world, out Vector3 lossyScale, out Quaternion rotation, out Vector3 position))
            {
                position = world.Translation;
                rotation = Quaternion.Identity;
                lossyScale = Vector3.One;
            }

            Vector3 halfDim = new Vector3(0.5f, 1f, 0.5f);
            Parameter halfParam = function.GetParameter(ShortGuids.half_dimensions);
            if (halfParam?.content is cVector3 vec)
                halfDim = vec.value;

            Vector3 halfExtents = new Vector3(
                Math.Abs(halfDim.X * lossyScale.X),
                Math.Abs(halfDim.Y * lossyScale.Y),
                Math.Abs(halfDim.Z * lossyScale.Z));
            if (halfExtents.X < 1e-4f) halfExtents.X = 1e-4f;
            if (halfExtents.Y < 1e-4f) halfExtents.Y = 1e-4f;
            if (halfExtents.Z < 1e-4f) halfExtents.Z = 1e-4f;

            // Match Instancing barrier/box convention: entity origin at floor, centre lifted by half Y.
            Vector3 centre = position + Vector3.Transform(new Vector3(0f, halfExtents.Y, 0f), rotation);

            return new AuthoringBoxVolume
            {
                Centre = centre,
                Rotation = rotation,
                HalfExtents = halfExtents
            };
        }

        static Matrix4x4 LocalTransformMatrix(FunctionEntity function)
        {
            Parameter param = function.GetParameter(ShortGuids.position);
            if (param?.content is cTransform transform)
            {
                Quaternion rotation = Quaternion.CreateFromYawPitchRoll(
                    transform.rotation.Y * (float)Math.PI / 180.0f,
                    transform.rotation.X * (float)Math.PI / 180.0f,
                    transform.rotation.Z * (float)Math.PI / 180.0f);
                return Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(transform.position);
            }
            return Matrix4x4.Identity;
        }

        static void AppendOrientedBox(
            HavokPackfile.PreviewMesh mesh,
            Vector3 centre,
            Quaternion rotation,
            Vector3 halfExtents)
        {
            Vector3 localMin = -halfExtents;
            Vector3 localMax = halfExtents;
            int baseIndex = mesh.Positions.Count;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = new Vector3(
                    (corner & 1) == 0 ? localMin.X : localMax.X,
                    (corner & 2) == 0 ? localMin.Y : localMax.Y,
                    (corner & 4) == 0 ? localMin.Z : localMax.Z);
                mesh.Positions.Add(centre + Vector3.Transform(local, rotation));
            }

            int[] tris =
            {
                0, 2, 3, 0, 3, 1,
                4, 5, 7, 4, 7, 6,
                0, 1, 5, 0, 5, 4,
                2, 6, 7, 2, 7, 3,
                0, 4, 6, 0, 6, 2,
                1, 3, 7, 1, 7, 5,
            };
            for (int i = 0; i < tris.Length; i++)
                mesh.Indices.Add(baseIndex + tris[i]);
            mesh.ShapeCount++;
        }

        static List<Vector3> CollectReachabilitySeedsLocal(Level level)
        {
            var seeds = new List<Vector3>();
            if (level.Commands?.Entries == null)
                return seeds;

            foreach (Composite composite in level.Commands.Entries)
            {
                if (composite == null)
                    continue;
                foreach (FunctionEntity function in composite.GetFunctionEntitiesOfType(FunctionType.NavMeshReachabilitySeedPoint))
                {
                    if (function == null)
                        continue;
                    Parameter param = function.GetParameter(ShortGuids.position);
                    if (param?.content is cTransform transform)
                        seeds.Add(transform.position);
                }
            }
            return seeds;
        }

        /// <summary>True if <paramref name="point"/> lies inside the oriented box (optional inflate).</summary>
        public static bool PointInOrientedBox(
            Vector3 point,
            Vector3 centre,
            Quaternion rotation,
            Vector3 halfExtents,
            float inflate = 0f)
        {
            Vector3 local = Vector3.Transform(point - centre, Quaternion.Conjugate(rotation));
            float hx = halfExtents.X + inflate;
            float hy = halfExtents.Y + inflate;
            float hz = halfExtents.Z + inflate;
            return Math.Abs(local.X) <= hx && Math.Abs(local.Y) <= hy && Math.Abs(local.Z) <= hz;
        }

        /// <summary>World AABB of an oriented box.</summary>
        public static void OrientedBoxAabb(
            Vector3 centre,
            Quaternion rotation,
            Vector3 halfExtents,
            out Vector3 min,
            out Vector3 max)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = new Vector3(
                    (corner & 1) == 0 ? -halfExtents.X : halfExtents.X,
                    (corner & 2) == 0 ? -halfExtents.Y : halfExtents.Y,
                    (corner & 4) == 0 ? -halfExtents.Z : halfExtents.Z);
                Vector3 world = centre + Vector3.Transform(local, rotation);
                min = Vector3.Min(min, world);
                max = Vector3.Max(max, world);
            }
        }
    }
}
#endif