#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CATHODE;
using CATHODE.Scripting;
using CATHODE.ShaderTypes;
using CathodeLib;
using NanoRT;

namespace CathodeLib.Radiosity
{
    /// <summary>
    /// World-space renderable triangle soup for the lighting bake, grouped into the radiosity
    /// "instances" that RADIOSITY_INSTANCE_MAP addresses, with a BVH over the whole level.
    /// </summary>
    /// <remarks>
    /// This is the radiosity counterpart to <c>CollisionNavMeshSoup</c>. It reads render meshes
    /// rather than collision hulls because the bake needs lightmap UVs (UV set 1) and materials,
    /// neither of which exist on the Havok side.
    /// </remarks>
    public sealed class RadiosityGeometry
    {
        /// <summary>
        /// One radiosity island: everything sharing a RESOURCES.BIN entry gets a single rect in
        /// the lightmap atlas, which is how retail groups them.
        /// </summary>
        public sealed class Instance
        {
            /// <summary>
            /// The composite instance every resource in this island belongs to. Verified against
            /// retail: all 1377 BSP_TORRENS instances have resources from exactly one composite.
            /// </summary>
            public ShortGuid CompositeInstanceID;

            /// <summary>
            /// The island id retail's bake assigned this instance's movers, or -1 for geometry
            /// retail never baked.
            /// </summary>
            /// <remarks>
            /// The instance index (RADIOSITY_INSTANCE_MAP's lightmap_transform) is not an
            /// arbitrary ordinal: it is the island id the runtime state system addresses -
            /// scripts toggling a RadiosityIsland resolve to it. Retail Solace runs 1856 islands
            /// over 1758 composites (islands subdivide composites, never merge them), so a
            /// per-composite renumbering permutes the state targets: powered-off sections render
            /// lit and lit rooms go dark. Matched islands must keep retail's id and grouping.
            /// </remarks>
            public int RetailIslandId = -1;

            /// <summary>Indices into <c>Level.Movers.Entries</c> that belong to this island.</summary>
            public readonly List<int> Movers = new List<int>();

            /// <summary>
            /// RESOURCES.BIN index per entry of <see cref="Movers"/>, which is what column two of
            /// RADIOSITY_INSTANCE_MAP holds.
            /// </summary>
            public readonly List<int> MoverResourceIndices = new List<int>();

            /// <summary>Triangle indices into <see cref="Tris"/>.</summary>
            public readonly List<int> Triangles = new List<int>();

            /// <summary>
            /// World surface area contributed by each entry of <see cref="Movers"/>.
            /// </summary>
            /// <remarks>
            /// Every mover of a composite shares one atlas rect - retail does the same, with an
            /// identical rect on all movers in 1057 of Solace's 1144 multi-mover composites - so
            /// the rect's texels have to be shared out between them rather than taken by whoever
            /// rasterises first.
            /// </remarks>
            public readonly List<float> MoverAreas = new List<float>();

            /// <summary>
            /// Fraction of the shared 0..1 lightmap square this instance's triangles actually
            /// occupy, between 0 and 1.
            /// </summary>
            /// <remarks>
            /// Rect size has to account for this. The atlas rect covers the whole unit square, so
            /// if the authored UVs only use a third of it then two thirds of the rect's texels
            /// land on nothing and the surface gets a third of the probes its area deserves.
            /// Sizing from world area alone left only 47.8% of our Solace rects within 25% of
            /// retail's, with a spread from 0.04x to 78x - which is what made probe coverage
            /// clump in some composites and disappear in others.
            /// </remarks>
            public float UvCoverage = 1.0f;

            /// <summary>Coarse occupancy of the unit UV square, used to compute UvCoverage.</summary>
            internal bool[] UvGrid;

            public Vector3 BoundsMin = new Vector3(float.MaxValue);
            public Vector3 BoundsMax = new Vector3(float.MinValue);

            /// <summary>Total world-space surface area (m²), which drives the atlas rect size.</summary>
            public float SurfaceArea;

            /// <summary>Assigned by <see cref="RadiosityBaker"/>.</summary>
            public int SliceIndex = -1;
            public int AtlasX, AtlasY, AtlasWidth, AtlasHeight;

            public Vector3 Centre => (BoundsMin + BoundsMax) * 0.5f;
        }

        /// <summary>xyz-interleaved world-space vertex positions.</summary>
        public float[] Verts = Array.Empty<float>();

        /// <summary>Three vertex indices per triangle.</summary>
        public int[] Tris = Array.Empty<int>();

        /// <summary>World-space vertex normals, one per vertex.</summary>
        public Vector3[] Normals = Array.Empty<Vector3>();

        /// <summary>Lightmap UVs (mesh UV set 1), one per vertex, nominally in 0..1.</summary>
        public Vector2[] LightmapUVs = Array.Empty<Vector2>();

        /// <summary>
        /// Diffuse UVs (mesh UV set 0), one per vertex. These tile, so they run well outside 0..1;
        /// <see cref="RadiosityMaterialSampler"/> wraps them.
        /// </summary>
        public Vector2[] DiffuseUVs = Array.Empty<Vector2>();

        /// <summary>Index into <see cref="Instances"/> for each triangle.</summary>
        public int[] TriangleInstance = Array.Empty<int>();

        /// <summary>
        /// Material sampler slot for each triangle, or <see cref="RadiosityMaterialSampler.NoMaterial"/>.
        /// </summary>
        public int[] TriangleMaterial = Array.Empty<int>();

        /// <summary>
        /// Which entry of the owning instance's <see cref="Instance.Movers"/> each triangle came
        /// from, so the rasteriser can share one atlas rect out between them.
        /// </summary>
        public int[] TriangleMoverSlot = Array.Empty<int>();

        /// <summary>
        /// Average material albedo for each triangle. Used where a surface has no diffuse UVs to
        /// sample at; where it does, <see cref="RadiosityMaterialSampler.Sample"/> is what runs.
        /// </summary>
        public Vector3[] TriangleAlbedo = Array.Empty<Vector3>();

        /// <summary>Emitted radiance for each triangle, zero for non-emissive surfaces.</summary>
        public Vector3[] TriangleEmissive = Array.Empty<Vector3>();

        public List<Instance> Instances = new List<Instance>();

        /// <summary>Acceleration structure over the whole soup. Built by <see cref="Build"/>.</summary>
        public BVHAccel Bvh { get; private set; }

        public Vector3 BoundsMin = new Vector3(float.MaxValue);
        public Vector3 BoundsMax = new Vector3(float.MinValue);

        public int TriangleCount => Tris.Length / 3;
        public int VertexCount => Verts.Length / 3;

        /// <summary>Movers skipped because they were dynamic, hidden, or had no usable mesh.</summary>
        public int MoversSkipped;

        /// <summary>Triangles dropped by the absurd-edge filter.</summary>
        public int TrianglesCulled;

        /// <summary>Movers that carry lightmap UVs, versus those falling back to planar UVs.</summary>
        public int MoversWithLightmapUVs;
        public int MoversWithoutLightmapUVs;

        /// <summary>Movers skipped because their composite has no static-radiosity geometry.</summary>
        public int MoversNotLightmapped;

        /// <summary>Skip-reason breakdown, for telling "retail lightmaps it and we do not" apart
        /// from "neither of us does".</summary>
        public int SkippedNotBakeable, SkippedDynamicMaterial, SkippedNoResource, SkippedNoGeometry;

        /// <summary>Renderable elements skipped because their shader does not describe a surface.</summary>
        public int ElementsNotSurfaces;

        /// <summary>
        /// Ubershaders that draw something other than a light-bouncing surface. A mover carrying
        /// one is geometry in the renderer's sense but not in radiosity's: a fog volume, a
        /// particle sheet, a light cone, a refraction pane, a debug box.
        /// </summary>
        /// <remarks>
        /// The renderable-type filter in <see cref="IsBakeable"/> catches most of these, but not
        /// all: DLC/ChallengeMap16 has several <c>SURFACEEFFECTBOX_*</c> movers on
        /// <c>CA_EFFECT_OVERLAY</c> that type-check as bakeable geometry and then contribute albedo
        /// at luminance 152 and chroma 109 - into a level whose retail albedo table averages 18.6
        /// with 90% of samples below 39. Between them they supplied about a fifth of that level's
        /// total albedo chroma.
        /// </remarks>
        private static readonly HashSet<SHADER_LIST> NonSurfaceShaders = new HashSet<SHADER_LIST>
        {
            // Effects and overlays.
            SHADER_LIST.CA_EFFECT,
            SHADER_LIST.CA_EFFECT_OVERLAY,
            SHADER_LIST.CA_DISTORTION_OVERLAY,
            SHADER_LIST.CA_WATER_CAUSTICS_OVERLAY,
            SHADER_LIST.CA_PARTICLE,
            SHADER_LIST.CA_RIBBON,
            SHADER_LIST.CA_LENS_FLARE,

            // Volumes that are drawn but do not reflect.
            SHADER_LIST.CA_FOGPLANE,
            SHADER_LIST.CA_FOGSPHERE,
            SHADER_LIST.CA_VOLUME_LIGHT,
            SHADER_LIST.CA_LIGHT_DECAL,

            // Light and deferred passes.
            SHADER_LIST.CA_DEFERRED,
            SHADER_LIST.CA_DEFERRED_DEPTH,
            SHADER_LIST.CA_DEFERRED_CONST,
            SHADER_LIST.CA_DIRECTIONAL_DEFERRED,
            SHADER_LIST.CA_LIGHTPROBE,
            SHADER_LIST.CA_ALPHALIGHT_POSITION,
            SHADER_LIST.CA_ALPHALIGHT_CLEAR,
            SHADER_LIST.CA_ALPHALIGHT_LIGHT,

            // Transparent or view-dependent surfaces, which have no stable albedo.
            SHADER_LIST.CA_REFRACTION,
            SHADER_LIST.CA_SIMPLE_REFRACTION,

            // Sky and space, which are unreachable backdrops rather than lit geometry.
            SHADER_LIST.CA_SKYDOME,
            SHADER_LIST.CA_PLANET,
            SHADER_LIST.CA_GALAXY,

            // Non-rendering utility passes.
            SHADER_LIST.CA_DEBUG,
            SHADER_LIST.CA_SHADOWCASTER,
            SHADER_LIST.CA_VELOCITY,
            SHADER_LIST.CA_OCCLUSION_TEST,
            SHADER_LIST.CA_OCCLUSION_CULLING,
            SHADER_LIST.CA_POST_PROCESSING,
            SHADER_LIST.CA_FILTERS,
            SHADER_LIST.CA_MOTION_BLUR_HI_SPEC,
        };

        /// <summary>True when an element's shader draws something that is not a lit surface.</summary>
        private static bool IsNonSurface(RenderableElements.Element element)
        {
            Shaders.Shader shader = element?.Material?.Shader;
            return shader != null && NonSurfaceShaders.Contains(shader.Ubershader);
        }

        public RadiosityMaterialSampler MaterialSampler { get; private set; }

        /// <summary>
        /// Walk MODELS.MVR and build the soup. The level must already have been through an
        /// <see cref="Instancing"/> pass so movers, REDS and RESOURCES are in their final state.
        /// </summary>
        public static RadiosityGeometry CollectFromLevel(Level level, RadiosityBakeSettings settings = null, Action<string> log = null)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (level.Movers == null) throw new InvalidOperationException("Level has no MODELS.MVR.");

            settings ??= RadiosityBakeSettings.CreateDefault();

            var geo = new RadiosityGeometry();
            geo.MaterialSampler = new RadiosityMaterialSampler(settings);

            var verts = new List<float>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var diffuseUvs = new List<Vector2>();
            var tris = new List<int>();
            var triInstance = new List<int>();
            var triMaterial = new List<int>();
            var triMoverSlot = new List<int>();
            var triAlbedo = new List<Vector3>();
            var triEmissive = new List<Vector3>();

            var instanceIndexByGroup = new Dictionary<(uint, uint), int>();
            // A CS2 submesh is shared between movers, so cache the decode.
            var meshCache = new Dictionary<Models.CS2.Component.LOD.Submesh, cMesh>();

            // Retail's island assignment per resource, resolved from the loaded instance map.
            // Grouping follows it wherever it has an answer so islands keep retail's shape and,
            // downstream, retail's id.
            Dictionary<(uint, uint), int> retailIslands = CollectRetailIslands(level);

            HashSet<ShortGuid> lightmappedComposites = settings.StaticRadiosityCompositesOnly
                ? CollectLightmappedComposites(level)
                : null;

            for (int moverIndex = 0; moverIndex < level.Movers.Entries.Count; moverIndex++)
            {
                Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[moverIndex];
                if (!IsBakeable(mover, settings))
                {
                    geo.MoversSkipped++;
                    if (RequiresDynamicRadiosity(mover)) geo.SkippedDynamicMaterial++;
                    else geo.SkippedNotBakeable++;
                    continue;
                }

                if (lightmappedComposites != null &&
                    !lightmappedComposites.Contains(mover.Resource?.composite_instance_id ?? ShortGuid.Invalid))
                {
                    geo.MoversSkipped++;
                    geo.MoversNotLightmapped++;
                    continue;
                }

                int resourceIndex = level.Resources.GetWriteIndex(mover.Resource);
                if (resourceIndex < 0)
                {
                    geo.MoversSkipped++;
                    geo.SkippedNoResource++;
                    continue;
                }

                // Group by retail island where the resource is in the retail bake, else by
                // composite instance (retail islands never span composites; they subdivide them).
                ShortGuid composite = mover.Resource.composite_instance_id;
                var resourceKey = (mover.Resource.composite_instance_id.AsUInt32, mover.Resource.resource_id.AsUInt32);
                bool hasRetailIsland = retailIslands.TryGetValue(resourceKey, out int retailIsland);
                (uint, uint) groupKey = hasRetailIsland
                    ? ((uint)0xFFFFFFFF, (uint)retailIsland)
                    : (composite.AsUInt32, 0u);
                if (!instanceIndexByGroup.TryGetValue(groupKey, out int instanceIndex))
                {
                    instanceIndex = geo.Instances.Count;
                    instanceIndexByGroup[groupKey] = instanceIndex;
                    geo.Instances.Add(new Instance
                    {
                        CompositeInstanceID = composite,
                        RetailIslandId = hasRetailIsland ? retailIsland : -1
                    });
                }
                Instance instance = geo.Instances[instanceIndex];
                instance.Movers.Add(moverIndex);
                instance.MoverResourceIndices.Add(resourceIndex);
                instance.MoverAreas.Add(0.0f);
                int moverSlot = instance.Movers.Count - 1;

                bool contributedGeometry = false;
                bool hadLightmapUVs = false;

                // Every element is baked, not just the first. A mover's renderable elements are its
                // separate submesh and material pairs, all of which draw; its LOD chain hangs off
                // each element's own LODs list, so stopping at the first was not skipping LODs, it
                // was skipping most of the model. On Solace 45.8% of bakeable movers carry more
                // than one, and taking only the first discarded 15433 m2 of 26436 - including 3751
                // of the level's 6163 m2 of floor, which is why rooms turned up with lit walls and
                // a bare floor.
                int elementsTaken = 0;
                foreach (RenderableElements.Element element in mover.RenderableElements)
                {
                    if (element?.Model == null)
                        continue;
                    if (settings.MaxElementsPerMover > 0 && elementsTaken >= settings.MaxElementsPerMover)
                        break;

                    // An effect or volume shader means this element is drawn but does not bounce
                    // light, so it must not claim atlas space or contribute albedo.
                    if (IsNonSurface(element))
                    {
                        geo.ElementsNotSurfaces++;
                        continue;
                    }

                    if (!meshCache.TryGetValue(element.Model, out cMesh mesh))
                    {
                        mesh = element.Model.ToMesh();
                        meshCache[element.Model] = mesh;
                    }
                    if (mesh.Vertices.Count == 0 || mesh.Indices.Count < 3)
                        continue;

                    int materialSlot = geo.MaterialSampler.Register(element.Material);
                    Vector3 albedo = geo.MaterialSampler.Mean(materialSlot);
                    Vector3 emissive = ResolveEmissive(mover, element, settings);
                    int lightmapChannel = FindLightmapChannel(mesh);
                    float uvScale = LightmapUvScale(mesh, lightmapChannel);
                    // Channel 0 is the tiling diffuse set. ToMesh scales every channel by 16.
                    bool hasDiffuseUVs = mesh.UVs.Length > 0 && mesh.UVs[0] != null &&
                                         mesh.UVs[0].Count == mesh.Vertices.Count;
                    bool hasNormals = mesh.Normals.Count == mesh.Vertices.Count;
                    hadLightmapUVs |= lightmapChannel >= 0;

                    Matrix4x4 transform = mover.Transform;
                    Matrix4x4 normalTransform = NormalMatrix(transform);

                    int baseVertex = verts.Count / 3;
                    for (int v = 0; v < mesh.Vertices.Count; v++)
                    {
                        Vector3 world = Vector3.Transform(mesh.Vertices[v], transform);
                        verts.Add(world.X);
                        verts.Add(world.Y);
                        verts.Add(world.Z);

                        Vector3 n = hasNormals ? Vector3.TransformNormal(mesh.Normals[v], normalTransform) : Vector3.UnitY;
                        float len = n.Length();
                        normals.Add(len > 1e-6f ? n / len : Vector3.UnitY);

                        uvs.Add(lightmapChannel >= 0 ? mesh.UVs[lightmapChannel][v] * uvScale : Vector2.Zero);
                        diffuseUvs.Add(hasDiffuseUVs ? mesh.UVs[0][v] * (1.0f / 16.0f) : Vector2.Zero);

                        geo.BoundsMin = Vector3.Min(geo.BoundsMin, world);
                        geo.BoundsMax = Vector3.Max(geo.BoundsMax, world);
                        instance.BoundsMin = Vector3.Min(instance.BoundsMin, world);
                        instance.BoundsMax = Vector3.Max(instance.BoundsMax, world);
                    }

                    for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
                    {
                        int i0 = baseVertex + mesh.Indices[i];
                        int i1 = baseVertex + mesh.Indices[i + 1];
                        int i2 = baseVertex + mesh.Indices[i + 2];

                        Vector3 a = At(verts, i0), b = At(verts, i1), c = At(verts, i2);
                        Vector3 cross = Vector3.Cross(b - a, c - a);
                        float area = cross.Length() * 0.5f;
                        if (area <= 1e-9f)
                            continue;

                        float longestEdge = Math.Max((b - a).Length(), Math.Max((c - b).Length(), (a - c).Length()));
                        if (longestEdge > settings.MaxTriangleEdge)
                        {
                            geo.TrianglesCulled++;
                            continue;
                        }

                        instance.Triangles.Add(tris.Count / 3);

                        instance.SurfaceArea += area;

                        instance.MoverAreas[moverSlot] += area;

                        MarkUvCoverage(instance, uvs, i0, i1, i2);

                        tris.Add(i0);
                        tris.Add(i1);
                        tris.Add(i2);
                        triInstance.Add(instanceIndex);
                        triMaterial.Add(hasDiffuseUVs ? materialSlot : RadiosityMaterialSampler.NoMaterial);
                        triMoverSlot.Add(moverSlot);
                        triAlbedo.Add(albedo);
                        triEmissive.Add(emissive);
                        contributedGeometry = true;
                    }
                    elementsTaken++;
                }

                if (!contributedGeometry)
                {
                    geo.MoversSkipped++;
                    geo.SkippedNoGeometry++;
                }
                else if (hadLightmapUVs)
                    geo.MoversWithLightmapUVs++;
                else
                    geo.MoversWithoutLightmapUVs++;
            }

            geo.Verts = verts.ToArray();
            geo.Tris = tris.ToArray();
            geo.Normals = normals.ToArray();
            geo.LightmapUVs = uvs.ToArray();
            geo.DiffuseUVs = diffuseUvs.ToArray();
            geo.TriangleInstance = triInstance.ToArray();
            geo.TriangleMaterial = triMaterial.ToArray();
            geo.TriangleMoverSlot = triMoverSlot.ToArray();
            geo.TriangleAlbedo = triAlbedo.ToArray();
            geo.TriangleEmissive = triEmissive.ToArray();

            foreach (Instance inst in geo.Instances) inst.UvCoverage = ResolveUvCoverage(inst);


            // Drop islands that ended up with no geometry so they never claim atlas space.
            geo.Instances.RemoveAll(o => o.Triangles.Count == 0);
            geo.ReindexTriangleInstances();

            log?.Invoke("Radiosity soup: verts=" + geo.VertexCount + " tris=" + geo.TriangleCount +
                        " instances=" + geo.Instances.Count + " moversSkipped=" + geo.MoversSkipped +
                        " trisCulled=" + geo.TrianglesCulled +
                        " lightmapUVs=" + geo.MoversWithLightmapUVs + "/" + (geo.MoversWithLightmapUVs + geo.MoversWithoutLightmapUVs) +
                        " nonSurfaceElements=" + geo.ElementsNotSurfaces +
                        " albedoDecoded=" + geo.MaterialSampler.Decoded + " albedoFallback=" + geo.MaterialSampler.FellBack +
                        " diffuseUVs=" + geo.TriangleMaterial.Count(m => m != RadiosityMaterialSampler.NoMaterial) + "/" + geo.TriangleCount);

            log?.Invoke("Radiosity skips: notBakeable=" + geo.SkippedNotBakeable +
                        " dynamicMaterial=" + geo.SkippedDynamicMaterial +
                        " notLightmappedComposite=" + geo.MoversNotLightmapped +
                        " noResource=" + geo.SkippedNoResource +
                        " noGeometry=" + geo.SkippedNoGeometry +
                        " (retail lightmaps " + (level.RadiosityInstanceMap?.Entries.Count.ToString() ?? "?") + " movers)");

            return geo;
        }

        /// <summary>
        /// Resource GUID pair to retail island id (lightmap_transform), from the loaded retail
        /// instance map. Empty when the level ships none.
        /// </summary>
        private static Dictionary<(uint, uint), int> CollectRetailIslands(Level level)
        {
            var islands = new Dictionary<(uint, uint), int>();
            if (level.RadiosityInstanceMap?.Entries == null || level.Resources == null)
                return islands;

            foreach (RadiosityInstanceMap.Entry entry in level.RadiosityInstanceMap.Entries)
            {
                // Entry.Resource is null on load (the map's ctor resolves before its resources
                // field is assigned), so resolve the raw index here.
                Resources.Resource resource = entry.Resource
                    ?? level.Resources.GetAtWriteIndex(entry.resource_index);
                if (resource == null)
                    continue;
                islands[(resource.composite_instance_id.AsUInt32, resource.resource_id.AsUInt32)] =
                    entry.lightmap_transform;
            }
            return islands;
        }

        /// <summary>Resolution of the per-instance UV occupancy grid.</summary>
        private const int UvGridSize = 16;

        /// <summary>
        /// Mark the cells of the unit UV square a triangle touches. Its bounding box is close
        /// enough at this resolution and costs a couple of cells per triangle.
        /// </summary>
        private static void MarkUvCoverage(Instance instance, List<Vector2> uvs, int i0, int i1, int i2)
        {
            if (instance.UvGrid == null)
                instance.UvGrid = new bool[UvGridSize * UvGridSize];

            Vector2 a = Wrap(uvs[i0]), b = Wrap(uvs[i1]), c = Wrap(uvs[i2]);
            int minX = (int)(Math.Min(a.X, Math.Min(b.X, c.X)) * UvGridSize);
            int maxX = (int)(Math.Max(a.X, Math.Max(b.X, c.X)) * UvGridSize);
            int minY = (int)(Math.Min(a.Y, Math.Min(b.Y, c.Y)) * UvGridSize);
            int maxY = (int)(Math.Max(a.Y, Math.Max(b.Y, c.Y)) * UvGridSize);

            minX = Math.Max(0, Math.Min(UvGridSize - 1, minX));
            maxX = Math.Max(0, Math.Min(UvGridSize - 1, maxX));
            minY = Math.Max(0, Math.Min(UvGridSize - 1, minY));
            maxY = Math.Max(0, Math.Min(UvGridSize - 1, maxY));

            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    instance.UvGrid[y * UvGridSize + x] = true;
        }

        /// <summary>
        /// Occupied fraction of the unit UV square, floored so a degenerate parameterisation
        /// cannot demand an unbounded rect.
        /// </summary>
        private static float ResolveUvCoverage(Instance instance)
        {
            if (instance.UvGrid == null)
                return 1.0f;
            int filled = 0;
            foreach (bool b in instance.UvGrid) if (b) filled++;
            instance.UvGrid = null;
            float coverage = filled / (float)(UvGridSize * UvGridSize);
            return Math.Max(0.15f, Math.Min(1.0f, coverage));
        }

        private static Vector2 Wrap(Vector2 uv) =>
            new Vector2(uv.X - (float)Math.Floor(uv.X), uv.Y - (float)Math.Floor(uv.Y));

        /// <summary>Rebuild the triangle -> instance table after instances have been filtered.</summary>
        private void ReindexTriangleInstances()
        {
            for (int i = 0; i < TriangleInstance.Length; i++)
                TriangleInstance[i] = -1;
            for (int i = 0; i < Instances.Count; i++)
            {
                foreach (int tri in Instances[i].Triangles)
                    TriangleInstance[tri] = i;
            }
        }

        /// <summary>
        /// Separate, lower-detail geometry used for occlusion, or null to occlude against the
        /// render meshes themselves.
        /// </summary>
        /// <remarks>
        /// Render meshes carry interior submeshes, back faces and panel detail that a probe gets
        /// caught inside, which is why a quarter of our surface probes saw nothing in any
        /// direction. The collision hulls are far simpler and do not have that problem.
        /// </remarks>
        public BVHAccel OccluderBvh { get; private set; }

        /// <summary>Triangle count of the occluder soup, or 0 when occluding against render meshes.</summary>
        public int OccluderTriangleCount { get; private set; }

        /// <summary>Build the BVH. Separated from collection so callers can log the timings.</summary>
        public void Build(Action<string> log = null)
        {
            Bvh = new BVHAccel();
            Bvh.Build(Verts, Tris);
            log?.Invoke("Radiosity BVH: depth=" + Bvh.Statistics.MaxTreeDepth +
                        " leaves=" + Bvh.Statistics.NumLeafNodes +
                        " branches=" + Bvh.Statistics.NumBranchNodes);
        }

        private float[] _occluderVerts = Array.Empty<float>();
        private int[] _occluderTris = Array.Empty<int>();

        /// <summary>
        /// How far to pull a visibility ray in at each end when occluding against the proxy.
        /// </summary>
        /// <remarks>
        /// Neither endpoint of a visibility ray lies on the proxy surface - both are points on the
        /// render meshes - so without this the shell around the target blocks the ray just before
        /// it arrives. It is the whole reason occluding against collision first measured worse than
        /// occluding against the render meshes: of facing surface point pairs on Solace, 4.1% are
        /// mutually visible through the render meshes but only 1.3% through collision with the ray
        /// run end to end. Pulling both ends in by 0.2 m restores 4.0%.
        /// </remarks>
        public float OccluderEndpointSlack { get; set; }

        /// <summary>Largest share of a link, per end, that <see cref="OccluderEndpointSlack"/> may skip.</summary>
        public float OccluderSlackFraction { get; set; } = 0.15f;

        /// <summary>Build the occluder BVH from a separate triangle soup.</summary>
        public void BuildOccluders(float[] verts, int[] tris, Action<string> log = null)
        {
            if (verts == null || tris == null || tris.Length < 3)
                return;

            var bvh = new BVHAccel();
            bvh.Build(verts, tris);
            OccluderBvh = bvh;
            _occluderVerts = verts;
            _occluderTris = tris;
            OccluderTriangleCount = tris.Length / 3;
            log?.Invoke("Radiosity occluder BVH: tris=" + OccluderTriangleCount +
                        " depth=" + bvh.Statistics.MaxTreeDepth +
                        " leaves=" + bvh.Statistics.NumLeafNodes);
        }

        /// <summary>
        /// Where a visibility ray for a surface point should start: on the occluder shell rather
        /// than inside it.
        /// </summary>
        /// <remarks>
        /// <para>A collision hull is a coarse shell built <em>around</em> its object, so a point on
        /// the render surface is normally inside it. Occluding against the shell without moving the
        /// ray origin therefore blocks everything immediately - it took our probes with no influence
        /// from 26% to 59%, worse than occluding against the render meshes.</para>
        /// <para>So the origin is moved onto the shell: cast along the surface normal, and if the
        /// first thing hit is the back of a triangle, we started inside a shell and the origin
        /// belongs just beyond where the ray leaves it. A front-facing hit means the shell is not
        /// around us but in front of us - a real occluder - and the origin stays put.</para>
        /// </remarks>
        public Vector3 VisibilityOrigin(Vector3 position, Vector3 normal, float range, float offset)
        {
            Vector3 fallback = position + normal * offset;
            if (OccluderBvh == null || range <= 0.0f)
                return fallback;

            var ray = new Ray(position, normal, 0.0f, range);
            if (!OccluderBvh.Traverse(ref ray, out Hit hit))
                return fallback;

            Vector3 face = OccluderNormal(hit.PrimId);
            if (Vector3.Dot(normal, face) <= 0.0f)
                return fallback;

            return position + normal * (hit.T + offset);
        }

        private Vector3 OccluderNormal(int tri)
        {
            int i0 = _occluderTris[tri * 3], i1 = _occluderTris[tri * 3 + 1], i2 = _occluderTris[tri * 3 + 2];
            Vector3 a = new Vector3(_occluderVerts[i0 * 3], _occluderVerts[i0 * 3 + 1], _occluderVerts[i0 * 3 + 2]);
            Vector3 b = new Vector3(_occluderVerts[i1 * 3], _occluderVerts[i1 * 3 + 1], _occluderVerts[i1 * 3 + 2]);
            Vector3 c = new Vector3(_occluderVerts[i2 * 3], _occluderVerts[i2 * 3 + 1], _occluderVerts[i2 * 3 + 2]);
            Vector3 n = Vector3.Cross(b - a, c - a);
            float length = n.Length();
            return length > 1e-9f ? n / length : Vector3.UnitY;
        }

        /// <summary>True when nothing blocks the straight line between two points.</summary>
        public bool Visible(Vector3 from, Vector3 to, float epsilon)
        {
            BVHAccel bvh = OccluderBvh ?? Bvh;

            Vector3 delta = to - from;
            float distance = delta.Length();

            // The slack is capped as a fraction of the link, so a short ray is not left effectively
            // untested. A flat 0.35 m at each end leaves only the middle 0.30 m of a 1 m link
            // examined, and that is what let light through walls: re-tested against the render
            // meshes, 68.1% of our 0-1 m links passed through geometry against retail's 34.2%, and
            // the excess was worst at short range rather than long. Capping at 15% per end holds
            // the untested portion to 30% at any distance while keeping the full slack on the long
            // links it was introduced for.
            float slack = epsilon;
            if (OccluderBvh != null)
                slack += Math.Min(OccluderEndpointSlack, distance * OccluderSlackFraction);

            if (distance <= slack * 2.0f)
                return true;

            Vector3 direction = delta / distance;
            var ray = new Ray(from + direction * slack, direction, 0.0f, distance - slack * 2.0f);
            return !bvh.Occluded(ref ray);
        }

        public Vector3 TriangleCentroid(int tri)
        {
            Vector3 a = At(Tris[tri * 3 + 0]);
            Vector3 b = At(Tris[tri * 3 + 1]);
            Vector3 c = At(Tris[tri * 3 + 2]);
            return (a + b + c) * (1.0f / 3.0f);
        }

        public float TriangleArea(int tri)
        {
            Vector3 a = At(Tris[tri * 3 + 0]);
            Vector3 b = At(Tris[tri * 3 + 1]);
            Vector3 c = At(Tris[tri * 3 + 2]);
            return Vector3.Cross(b - a, c - a).Length() * 0.5f;
        }

        /// <summary>Geometric normal, flipped towards the interpolated vertex normals.</summary>
        public Vector3 TriangleNormal(int tri)
        {
            int i0 = Tris[tri * 3 + 0], i1 = Tris[tri * 3 + 1], i2 = Tris[tri * 3 + 2];
            Vector3 n = Vector3.Cross(At(i1) - At(i0), At(i2) - At(i0));
            float len = n.Length();
            if (len <= 1e-9f)
                return Vector3.UnitY;
            n /= len;

            Vector3 shading = Normals[i0] + Normals[i1] + Normals[i2];
            return Vector3.Dot(n, shading) < 0 ? -n : n;
        }

        /// <summary>Barycentric point on a triangle, plus its interpolated normal and lightmap UV.</summary>
        public void SamplePoint(int tri, float u, float v, out Vector3 position, out Vector3 normal, out Vector2 uv)
        {
            SamplePoint(tri, u, v, out position, out normal, out uv, out _);
        }

        /// <summary>As above, and also the interpolated diffuse UV for albedo sampling.</summary>
        public void SamplePoint(int tri, float u, float v, out Vector3 position, out Vector3 normal,
                                out Vector2 uv, out Vector2 diffuseUv)
        {
            int i0 = Tris[tri * 3 + 0], i1 = Tris[tri * 3 + 1], i2 = Tris[tri * 3 + 2];
            float w = 1.0f - u - v;

            position = At(i0) * w + At(i1) * u + At(i2) * v;

            Vector3 n = Normals[i0] * w + Normals[i1] * u + Normals[i2] * v;
            float len = n.Length();
            normal = len > 1e-6f ? n / len : TriangleNormal(tri);

            uv = LightmapUVs[i0] * w + LightmapUVs[i1] * u + LightmapUVs[i2] * v;
            diffuseUv = DiffuseUVs[i0] * w + DiffuseUVs[i1] * u + DiffuseUVs[i2] * v;
        }

        /// <summary>Interpolated diffuse UV alone, for extra albedo taps that need nothing else.</summary>
        public Vector2 DiffuseUvAt(int tri, float u, float v)
        {
            int i0 = Tris[tri * 3 + 0], i1 = Tris[tri * 3 + 1], i2 = Tris[tri * 3 + 2];
            return DiffuseUVs[i0] * (1.0f - u - v) + DiffuseUVs[i1] * u + DiffuseUVs[i2] * v;
        }

        /// <summary>
        /// Albedo at a barycentric point: sampled from the material's diffuse map where the
        /// surface has diffuse UVs, and the material's mean colour where it does not.
        /// </summary>
        public Vector3 SampleAlbedo(int tri, Vector2 diffuseUv)
        {
            int slot = TriangleMaterial[tri];
            return slot == RadiosityMaterialSampler.NoMaterial
                ? TriangleAlbedo[tri]
                : MaterialSampler.Sample(slot, diffuseUv);
        }

        public Vector3 At(int vertexIndex) => new Vector3(Verts[vertexIndex * 3], Verts[vertexIndex * 3 + 1], Verts[vertexIndex * 3 + 2]);

        private static Vector3 At(List<float> verts, int vertexIndex) =>
            new Vector3(verts[vertexIndex * 3], verts[vertexIndex * 3 + 1], verts[vertexIndex * 3 + 2]);

        /// <summary>
        /// Pick the mesh's lightmap UV set. Channel 0 is always the tiling diffuse UV (it runs
        /// well outside 0..1); the lightmap channel is whichever secondary set stays inside the
        /// unit square. Across BSP_TORRENS that is channel 2 on 3118 meshes and channel 1 on 90,
        /// so both are checked rather than assuming one.
        /// </summary>
        private static int FindLightmapChannel(cMesh mesh)
        {
            int best = -1;
            foreach (int channel in new[] { 2, 1, 3 })
            {
                if (channel >= mesh.UVs.Length || mesh.UVs[channel] == null || mesh.UVs[channel].Count != mesh.Vertices.Count)
                    continue;

                float max = 0, min = 0;
                foreach (Vector2 uv in mesh.UVs[channel])
                {
                    max = Math.Max(max, Math.Max(uv.X, uv.Y));
                    min = Math.Min(min, Math.Min(uv.X, uv.Y));
                }

                // ToMesh multiplies every UV by 16, so an authored 0..1 set arrives as 0..16.
                if (min < -0.01f || max > 16.05f)
                    continue;

                best = channel;
                break;
            }
            return best;
        }

        /// <summary>
        /// Scale that maps the chosen channel back to 0..1. Sets authored 0..1 arrive scaled by
        /// 16 from <c>ToMesh</c>; a handful are already unit range.
        /// </summary>
        private static float LightmapUvScale(cMesh mesh, int channel)
        {
            if (channel < 0)
                return 1.0f;

            float max = 0;
            foreach (Vector2 uv in mesh.UVs[channel])
                max = Math.Max(max, Math.Max(uv.X, uv.Y));

            return max > 1.001f ? 1.0f / 16.0f : 1.0f;
        }

        /// <summary>
        /// Composite instances that own at least one mover asking for <c>RADIOSITY_STATIC</c>.
        /// </summary>
        /// <remarks>
        /// <para>This is what decides whether anything in a composite gets a lightmap rect, and it
        /// is the single biggest factor in how many slices a level needs. Measured against retail:
        /// selecting composites this way gives 1260 / 1763 / 7838 for BSP_TORRENS / Solace /
        /// Tech_Hub against retail's 1243 / 1758 / 7825. Taking every renderable static mover
        /// instead - which is what the baker used to do - gives 1522 / 4980 / 11078, and Solace
        /// then needs 23 slices where retail needs 3.</para>
        /// <para>The whole composite comes along once it qualifies, not just its static movers:
        /// that is the rule that reproduces 100% of the resources retail lists on all three levels.
        /// The surplus it admits is inert, since a mover whose shader lacks the static requirement
        /// never samples the atlas, whereas leaving one out costs it its lighting.</para>
        /// </remarks>
        private static HashSet<ShortGuid> CollectLightmappedComposites(Level level)
        {
            const long staticBit = 1L << (int)SHADER_REQUIREMENTS.RADIOSITY_STATIC;
            var composites = new HashSet<ShortGuid>();

            foreach (Movers.MOVER_DESCRIPTOR mover in level.Movers.Entries)
            {
                if (mover?.Resource == null || mover.RenderableElements == null)
                    continue;
                if (!RequiresFlag(mover, staticBit))
                    continue;
                composites.Add(mover.Resource.composite_instance_id);
            }
            return composites;
        }

        private static bool RequiresFlag(Movers.MOVER_DESCRIPTOR mover, long bit)
        {
            foreach (RenderableElements.Element element in mover.RenderableElements)
            {
                if (((element?.Material?.Shader?.UbershaderRequirementFlags ?? 0) & bit) != 0) return true;
                if (element?.LODs == null) continue;
                foreach (RenderableElements.Element lod in element.LODs)
                    if (((lod?.Material?.Shader?.UbershaderRequirementFlags ?? 0) & bit) != 0) return true;
            }
            return false;
        }

        private static bool IsBakeable(Movers.MOVER_DESCRIPTOR mover, RadiosityBakeSettings settings)
        {
            if (mover?.RenderableElements == null || mover.RenderableElements.Count == 0)
                return false;
            if (settings.SkipNonRendered && mover.CullFlags.HasFlag(Movers.CullFlag.NO_RENDER))
                return false;
            if (settings.StaticGeometryOnly && mover.Flags != null && !mover.Flags.Stationary)
                return false;

            switch (mover.GetRenderableType())
            {
                case RenderableInstanceType.ENVIRONMENT:
                case RenderableInstanceType.ENVIRONMENT_EXTRA:
                case RenderableInstanceType.MISC:
                    break;
                default:
                    // Lights, particles, fog spheres and planets emit or are dynamic; they are
                    // handled through the surface-light path rather than as bake geometry.
                    return false;
            }

            return !RequiresDynamicRadiosity(mover);
        }

        /// <summary>
        /// True when any element of the mover asks for RADIOSITY_DYNAMIC.
        /// </summary>
        /// <remarks>
        /// <para>Dynamic instances are lit from the volume probes, not from the lightmap atlas, and
        /// retail never puts one in RADIOSITY_INSTANCE_MAP - 0 of the 3171 movers it maps in
        /// BSP_TORRENS require the dynamic bit. Giving one a rect also makes the engine see both a
        /// static allocation and a dynamic requirement on one instance, which asserts with "mixing
        /// static and dynamic radiosity in the same instance"
        /// (renderable_environment_instance.cpp:154).</para>
        /// <para>Deliberately <i>not</i> requiring RADIOSITY_STATIC to be present: retail maps 93
        /// movers that carry neither bit, so demanding the static bit drops geometry that should be
        /// baked. Only the dynamic bit disqualifies a mover.</para>
        /// </remarks>
        public static bool RequiresDynamicRadiosity(Movers.MOVER_DESCRIPTOR mover)
        {
            const long dynamicBit = 1L << (int)SHADER_REQUIREMENTS.RADIOSITY_DYNAMIC;

            foreach (RenderableElements.Element element in mover.RenderableElements)
            {
                if (((element?.Material?.Shader?.UbershaderRequirementFlags ?? 0) & dynamicBit) != 0)
                    return true;

                // A LOD swapping to a dynamic material would mix the two just as badly.
                if (element?.LODs == null)
                    continue;
                foreach (RenderableElements.Element lod in element.LODs)
                {
                    if (((lod?.Material?.Shader?.UbershaderRequirementFlags ?? 0) & dynamicBit) != 0)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether this element emits, and how strongly, as radiance injected into the probe set.
        /// </summary>
        /// <remarks>
        /// <para>An element emits because its <em>material</em> compiles the environment
        /// ubershader's EMISSIVE feature, not because the mover carries an emissive multiplier.
        /// Retail's own data settles it: joining every surface light slice back through
        /// RESOURCES.BIN to the mover that emits it, the material feature flag picks out all 1639
        /// entities retail lights across Solace and BSP_TORRENS with 52 extras, where
        /// <c>EmissiveRadiosityMultiplier &gt; 0</c> finds only 41.5% of them. That field is zero on
        /// most of retail's emitters.</para>
        /// <para>It also has to be resolved per element rather than per mover: a mover's elements
        /// are separate submesh and material pairs, so a lamp whose housing and glass are two
        /// elements should emit only from the glass.</para>
        /// <para>Strength falls back to 1.0 where the multiplier is zero, which is what retail
        /// stores: of its lights on emitters with no radiosity multiplier, 69.2% carry Scale 15 -
        /// the encoding of exactly 1.0 - and it is the mode of the distribution overall.</para>
        /// </remarks>
        private static Vector3 ResolveEmissive(
            Movers.MOVER_DESCRIPTOR mover, RenderableElements.Element element, RadiosityBakeSettings settings)
        {
            if (mover.EmissiveFlags.HasFlag(Movers.EmissiveFlag.MasterOff))
                return Vector3.Zero;
            // The material feature alone decides emission. A positive EmissiveRadiosityMultiplier
            // used to qualify a mover too, but 101 of SCI_Hub's movers carry a multiplier on a
            // non-emissive material and retail lights none of them - the field evidently scales
            // emission where it exists rather than creating it.
            if (!IsEmissiveMaterial(element))
                return Vector3.Zero;

            float multiplier = ResolveEmissiveStrength(mover, element, settings);
            Vector3 tint = mover.EmissiveTint / 255.0f;
            return tint * (multiplier * settings.EmissiveScale);
        }

        /// <summary>
        /// An emissive element's strength: the material's own EMISSIVE_MULT constant.
        /// </summary>
        /// <remarks>
        /// <para>This is retail's Scale source, decoded on BSP_TORRENS by joining each light slice
        /// back to its mover's material: EMISSIVE_MULT 0.5 maps to Scale 7 on 118 of 118 emitters,
        /// 1.0 to Scale 15 on 513 of 517, 1.5-2 to Scale 23 on 78 of 81, and the sub-0.15 group to
        /// Scale 0-1 on all 158 - exactly <c>EmissiveScaleByte(EMISSIVE_MULT)</c>. The mover's
        /// EmissiveRadiosityMultiplier, which this replaced, scatters against retail in both
        /// directions and forced a clamp that flattened every fixture to one strength.</para>
        /// <para>The mover multiplier and the old clamp remain only as the fallback for a material
        /// whose shader does not remap the constant.</para>
        /// </remarks>
        public static float ResolveEmissiveStrength(
            Movers.MOVER_DESCRIPTOR mover, RenderableElements.Element element, RadiosityBakeSettings settings)
        {
            Materials.Material material = element?.Material;
            if (material?.Shader != null &&
                TryMaterialConstant(material, (int)CA_ENVIRONMENT.PARAMETERS.EMISSIVE_MULT, 1, out int remap))
            {
                float value = material.PixelShaderConstants[remap];
                if (!float.IsNaN(value) && !float.IsInfinity(value) && value >= 0.0f)
                    return value;
            }

            float multiplier = mover.EmissiveRadiosityMultiplier > 0.0f
                ? mover.EmissiveRadiosityMultiplier
                : settings.DefaultEmissiveMultiplier;
            return Math.Max(settings.EmissiveMultiplierFloor,
                   Math.Min(settings.EmissiveMultiplierCeiling, multiplier));
        }

        /// <summary>Strongest emissive element's strength, for the mover-level light passes.</summary>
        public static float ResolveMoverEmissiveStrength(Movers.MOVER_DESCRIPTOR mover, RadiosityBakeSettings settings)
        {
            if (mover?.RenderableElements == null)
                return 0.0f;
            float best = 0.0f;
            foreach (RenderableElements.Element element in mover.RenderableElements)
            {
                if (!IsEmissiveMaterial(element))
                    continue;
                float s = ResolveEmissiveStrength(mover, element, settings);
                if (s > best) best = s;
            }
            return best;
        }

        /// <summary>
        /// World-space area of a mover's emissive elements, measured from the meshes. For movers
        /// outside the bake geometry, whose area the triangle soup never recorded.
        /// </summary>
        public static float MeasureEmissiveArea(
            Movers.MOVER_DESCRIPTOR mover, Dictionary<Models.CS2.Component.LOD.Submesh, cMesh> meshCache)
        {
            if (mover?.RenderableElements == null)
                return 0.0f;

            float area = 0.0f;
            foreach (RenderableElements.Element element in mover.RenderableElements)
            {
                if (!IsEmissiveMaterial(element) || element.Model == null)
                    continue;
                if (!meshCache.TryGetValue(element.Model, out cMesh mesh))
                    meshCache[element.Model] = mesh = element.Model.ToMesh();
                if (mesh.Vertices.Count == 0)
                    continue;
                for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
                {
                    Vector3 a = Vector3.Transform(mesh.Vertices[mesh.Indices[i]], mover.Transform);
                    Vector3 b = Vector3.Transform(mesh.Vertices[mesh.Indices[i + 1]], mover.Transform);
                    Vector3 c = Vector3.Transform(mesh.Vertices[mesh.Indices[i + 2]], mover.Transform);
                    area += Vector3.Cross(b - a, c - a).Length() * 0.5f;
                }
            }
            return area;
        }

        /// <summary>Resolve a material parameter index to its first constant slot, or false.</summary>
        private static bool TryMaterialConstant(Materials.Material material, int parameter, int components, out int remap)
        {
            remap = -1;
            Shaders.Shader shader = material.Shader;
            if (shader == null || parameter < 0 || parameter >= shader.PixelShaderParameterRemaps.Count)
                return false;
            remap = shader.PixelShaderParameterRemaps[parameter];
            return remap != 255 && remap >= 0 && remap + components - 1 < material.PixelShaderConstants.Count;
        }

        /// <summary>
        /// The emissive radiance a mover would inject, whether or not it entered the bake
        /// geometry. Zero when nothing about it emits.
        /// </summary>
        /// <remarks>
        /// Exists for the unbaked-emitter light pass: 532 of the 544 emitters retail lights on
        /// SCI_Hub that we did not are RADIOSITY_DYNAMIC movers - excluded from the lightmap
        /// geometry, correctly, but their emission still lands on the static world around them.
        /// </remarks>
        public static Vector3 ResolveMoverEmissive(Movers.MOVER_DESCRIPTOR mover, RadiosityBakeSettings settings)
        {
            if (mover?.RenderableElements == null)
                return Vector3.Zero;
            Vector3 best = Vector3.Zero;
            float bestPeak = 0;
            foreach (RenderableElements.Element element in mover.RenderableElements)
            {
                Vector3 e = ResolveEmissive(mover, element, settings);
                float peak = Math.Max(e.X, Math.Max(e.Y, e.Z));
                if (peak > bestPeak) { bestPeak = peak; best = e; }
            }
            return best;
        }

        /// <summary>Does this element's material compile the ubershader's EMISSIVE feature?</summary>
        private static bool IsEmissiveMaterial(RenderableElements.Element element)
        {
            Shaders.Shader shader = element?.Material?.Shader;
            if (shader == null || shader.Ubershader != SHADER_LIST.CA_ENVIRONMENT)
                return false;
            return (shader.UbershaderFeatureFlags & (1L << (int)CA_ENVIRONMENT.FEATURES.EMISSIVE)) != 0;
        }

        /// <summary>Inverse-transpose of the upper 3x3, so non-uniform scales keep normals correct.</summary>
        private static Matrix4x4 NormalMatrix(Matrix4x4 transform)
        {
            Matrix4x4 upper = transform;
            upper.M14 = upper.M24 = upper.M34 = 0;
            upper.M41 = upper.M42 = upper.M43 = 0;
            upper.M44 = 1;
            if (!Matrix4x4.Invert(upper, out Matrix4x4 inverse))
                return upper;
            return Matrix4x4.Transpose(inverse);
        }
    }
}
#endif
