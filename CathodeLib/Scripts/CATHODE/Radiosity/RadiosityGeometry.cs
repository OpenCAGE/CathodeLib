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
    public sealed class RadiosityGeometry
    {
        /// <summary>
        /// One radiosity island: everything sharing a RESOURCES.BIN entry gets a single rect in
        /// the lightmap atlas, which is how retail groups them.
        /// </summary>
        internal sealed class Instance
        {
            /// <summary>The composite instance every resource in this island belongs to (an island never spans composites).</summary>
            public ShortGuid CompositeInstanceID;

            /// <summary>
            /// The island id retail's bake assigned this instance's movers, or -1 for geometry retail never baked.
            /// </summary>
            public int RetailIslandId = -1;

            /// <summary>Indices into <c>Level.Movers.Entries</c> that belong to this island.</summary>
            public readonly List<int> Movers = new List<int>();

            /// <summary>Triangle indices into <see cref="Tris"/>.</summary>
            public readonly List<int> Triangles = new List<int>();

            /// <summary>
            /// World surface area contributed by each entry of <see cref="Movers"/>. Every mover of an island shares one
            /// atlas rect (as in retail), so its texels are shared out between them.
            /// </summary>
            public readonly List<float> MoverAreas = new List<float>();

            /// <summary>
            /// Fraction of the shared 0..1 lightmap square this instance's triangles actually occupy, between 0 and 1. The
            /// rect covers the whole unit square, so a chart using a third of it would otherwise get a third of the probes
            /// its area deserves (sizing from area alone left 47.8% of our Solace rects within 25% of retail's).
            /// </summary>
            public float UvCoverage = 1.0f;

            /// <summary>
            /// Width over height of the region of the unit UV square this instance's triangles occupy, which the atlas
            /// rect's shape follows (the world bounding box missed retail's rect shapes by |log| 0.77 on ChallengeMap4,
            /// this by 0.51).
            /// </summary>
            public float UvAspect = 1.0f;

            /// <summary>Coarse occupancy of the unit UV square, used to compute UvCoverage.</summary>
            internal bool[] UvGrid;

            /// <summary>
            /// Raw (unwrapped) bounds of this instance's lightmap UVs, for
            /// <see cref="TryRectFromUvConvention"/>. Retail's compiler wrote each chart into its
            /// rect with texel centres at (i + 0.5) / (w - 0.5), so the chart's extent along U is
            /// (w - 1) / (w - 0.5) and the rect size is recoverable from the mesh alone.
            /// </summary>
            public float UvMinU = float.MaxValue, UvMaxU = float.MinValue, UvMinV = float.MaxValue, UvMaxV = float.MinValue;

            public Vector3 BoundsMin = new Vector3(float.MaxValue);
            public Vector3 BoundsMax = new Vector3(float.MinValue);

            /// <summary>Total world-space surface area (m²), which drives the atlas rect size.</summary>
            public float SurfaceArea;

            /// <summary>Assigned by <see cref="RadiosityBaker"/>.</summary>
            public int SliceIndex = -1;
            public int AtlasX, AtlasY, AtlasWidth, AtlasHeight;

            /// <summary>
            /// Radiance donor for a delta slice: rasterised into the atlas as cluster-only texels
            /// (live cluster field, input probes, light slices, scatter) so the slice's real
            /// islands can gather the surrounding room's bounce natively in-slice, but no surface
            /// probes and no MODEL_PARAMS / instance-map writes - the mover keeps rendering from
            /// its retail slice.
            /// </summary>
            public bool DonorOnly;

            /// <summary>
            /// World-space uniform surface samples for the probe-only path, one texel each. When
            /// set, the atlas rasteriser places these instead of rasterising the lightmap UVs -
            /// see <c>RadiosityTuning.DeltaProbeSpacing</c>.
            /// </summary>
            internal List<UniformSurfaceSample> UniformSamples;

            public Vector3 Centre => (BoundsMin + BoundsMax) * 0.5f;
        }

        /// <summary>A probe site produced by uniform world-grid surface sampling.</summary>
        internal struct UniformSurfaceSample
        {
            public int Tri;
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 DiffuseUv;
        }

        /// <summary>xyz-interleaved world-space vertex positions.</summary>
        internal float[] Verts = Array.Empty<float>();

        /// <summary>Three vertex indices per triangle.</summary>
        internal int[] Tris = Array.Empty<int>();

        /// <summary>World-space vertex normals, one per vertex.</summary>
        internal Vector3[] Normals = Array.Empty<Vector3>();

        /// <summary>Lightmap UVs (mesh UV set 1), one per vertex, nominally in 0..1.</summary>
        internal Vector2[] LightmapUVs = Array.Empty<Vector2>();

        /// <summary>
        /// Diffuse UVs (mesh UV set 0), one per vertex. These tile, so they run well outside 0..1;
        /// <see cref="RadiosityMaterialSampler"/> wraps them.
        /// </summary>
        internal Vector2[] DiffuseUVs = Array.Empty<Vector2>();

        /// <summary>Index into <see cref="Instances"/> for each triangle.</summary>
        internal int[] TriangleInstance = Array.Empty<int>();

        /// <summary>
        /// Material sampler slot for each triangle, or <see cref="RadiosityMaterialSampler.NoMaterial"/>.
        /// </summary>
        internal int[] TriangleMaterial = Array.Empty<int>();

        /// <summary>
        /// Which entry of the owning instance's <see cref="Instance.Movers"/> each triangle came
        /// from, so the rasteriser can share one atlas rect out between them.
        /// </summary>
        internal int[] TriangleMoverSlot = Array.Empty<int>();

        /// <summary>
        /// Average material albedo for each triangle. Used where a surface has no diffuse UVs to
        /// sample at; where it does, <see cref="RadiosityMaterialSampler.Sample"/> is what runs.
        /// </summary>
        internal Vector3[] TriangleAlbedo = Array.Empty<Vector3>();

        /// <summary>Emitted radiance for each triangle, zero for non-emissive surfaces.</summary>
        internal Vector3[] TriangleEmissive = Array.Empty<Vector3>();

        internal List<Instance> Instances = new List<Instance>();

        /// <summary>Acceleration structure over the whole soup. Built by <see cref="Build"/>.</summary>
        internal BVHAccel Bvh { get; private set; }

        internal int TriangleCount => Tris.Length / 3;
        internal int VertexCount => Verts.Length / 3;

        /// <summary>Movers skipped because they were dynamic, hidden, or had no usable mesh.</summary>
        internal int MoversSkipped;

        /// <summary>Triangles dropped by the absurd-edge filter.</summary>
        internal int TrianglesCulled;

        /// <summary>Movers that carry lightmap UVs, versus those without (their lightmap UVs are written as zero).</summary>
        internal int MoversWithLightmapUVs;
        internal int MoversWithoutLightmapUVs;

        /// <summary>Movers skipped because their composite has no static-radiosity geometry.</summary>
        internal int MoversNotLightmapped;

        /// <summary>Skip-reason breakdown, for telling "retail lightmaps it and we do not" apart
        /// from "neither of us does".</summary>
        internal int SkippedNotBakeable, SkippedDynamicMaterial, SkippedNoResource, SkippedNoGeometry;

        /// <summary>Renderable elements skipped because their shader does not describe a surface.</summary>
        internal int ElementsNotSurfaces;

        /// <summary>Islands created by splitting transparent movers out of their composite's island (see
        /// <see cref="SplitTransparentIslands"/>).</summary>
        internal int IslandsSplitOff;

        /// <summary>
        /// Ubershaders that draw something other than a light-bouncing surface. A mover carrying
        /// one is geometry in the renderer's sense but not in radiosity's: a fog volume, a
        /// particle sheet, a light cone, a refraction pane, a debug box.
        /// </summary>
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

        internal RadiosityMaterialSampler MaterialSampler { get; private set; }

        /// <summary>
        /// Built only by <see cref="CollectFromLevel"/>. The type is public because the public
        /// <see cref="RadiosityOccluders.TryCollect"/> takes one; callers outside the library pass null there.
        /// </summary>
        private RadiosityGeometry() { }

        /// <summary>
        /// Walk MODELS.MVR and build the soup. The level must already have been through an
        /// <see cref="Instancing"/> pass so movers, REDS and RESOURCES are in their final state.
        /// </summary>
        /// <param name="staticCompositesOnly">Only collect composites that contain at least one mover requiring
        /// RADIOSITY_STATIC (as retail lightmapped them). This is what keeps a full bake's slice count near retail's: without
        /// it Solace wants 23 slices against retail's 3, because about two thirds of its composites get a lightmap rect
        /// they have no shader to sample. The delta bakes collect without it: added content is never on the list.</param>
        /// <param name="splitTransparentIslands">Give transparent movers islands of their own where retail has none for them
        /// (<see cref="SplitTransparentIslands"/>). Full bakes only: the delta bakes keep one island per composite.</param>
        internal static RadiosityGeometry CollectFromLevel(Level level, bool staticCompositesOnly, bool splitTransparentIslands,
            Action<string> log = null)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (level.Movers == null) throw new InvalidOperationException("Level has no MODELS.MVR.");

            var geo = new RadiosityGeometry();
            geo.MaterialSampler = new RadiosityMaterialSampler();

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

            HashSet<ShortGuid> lightmappedComposites = staticCompositesOnly
                ? CollectLightmappedComposites(level)
                : null;

            for (int moverIndex = 0; moverIndex < level.Movers.Entries.Count; moverIndex++)
            {
                Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[moverIndex];
                if (!IsBakeable(mover))
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
                instance.MoverAreas.Add(0.0f);
                int moverSlot = instance.Movers.Count - 1;

                bool contributedGeometry = false;
                bool hadLightmapUVs = false;

                // Every element is baked, not just the first: a mover's renderable elements are its separate submesh and
                // material pairs, all of which draw (the first alone discarded 15433 m2 of Solace's 26436).
                foreach (RenderableElements.Element element in mover.RenderableElements)
                {
                    if (element?.Model == null)
                        continue;

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
                    Vector3 emissive = ResolveEmissive(mover, element);
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
                        // /16 undoes ToMesh's x16. CA's compiler samples the x16 UV (RADIOSITY_ALBEDO_SAMPLES.BIN, 23 Sep:
                        // /16 reads only the top-left 1/16 of a whole-panel texture), but that point sample alone
                        // overshoots retail's probe albedo.
                        diffuseUvs.Add(hasDiffuseUVs ? mesh.UVs[0][v] * (1.0f / 16.0f) : Vector2.Zero);

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
                        if (longestEdge > RadiosityTuning.MaxTriangleEdge)
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

            if (splitTransparentIslands)
                SplitTransparentIslands(level, geo, verts, uvs, tris, triInstance, triMoverSlot);

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
                        (geo.IslandsSplitOff > 0 ? " islandsSplitOff=" + geo.IslandsSplitOff : "") +
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

        /// <summary>
        /// A transparent lit surface: a CA_ENVIRONMENT material that alpha-blends (USE_ALPHA_AS_BLENDFACTOR or FORCE_TO_ALPHA),
        /// is lit as alpha (ALPHA_LIGHTING) and is not a DECAL - window and screen glass, light cases.
        /// </summary>
        private static bool IsTransparentSurfaceElement(RenderableElements.Element element)
        {
            Shaders.Shader shader = element?.Material?.Shader;
            if (shader == null || shader.Ubershader != SHADER_LIST.CA_ENVIRONMENT)
                return false;
            long f = shader.UbershaderFeatureFlags;
            bool blends = Bit(f, (int)CA_ENVIRONMENT.FEATURES.USE_ALPHA_AS_BLENDFACTOR) || Bit(f, (int)CA_ENVIRONMENT.FEATURES.FORCE_TO_ALPHA);
            return blends && Bit(f, (int)CA_ENVIRONMENT.FEATURES.ALPHA_LIGHTING) && !Bit(f, (int)CA_ENVIRONMENT.FEATURES.DECAL);
        }

        private static bool Bit(long flags, int bit) => ((flags >> bit) & 1) != 0;

        /// <summary>Does any surface element of the mover draw a transparent lit material?</summary>
        private static bool IsTransparentMover(Movers.MOVER_DESCRIPTOR mover)
        {
            if (mover?.RenderableElements == null) return false;
            foreach (RenderableElements.Element element in mover.RenderableElements)
                if (element?.Model != null && !IsNonSurface(element) && IsTransparentSurfaceElement(element))
                    return true;
            return false;
        }

        /// <summary>Side of the lightmap-UV raster <see cref="SplitTransparentIslands"/> compares charts on.</summary>
        private const int SplitGridSize = 64;

        /// <summary>
        /// Retail's island rule, for islands retail never baked: a composite's transparent movers
        /// (<see cref="IsTransparentSurfaceElement"/>) leave its island and share islands among themselves, first fit in mover
        /// order - a mover joins the first island whose lightmap-UV cells (64x64 raster) overlap at most
        /// <see cref="RadiosityTuning.IslandSplitMaxOverlap"/> of its own, else starts a new one. Everything else stays in
        /// one island, however much its charts overlap.
        /// </summary>
        private static void SplitTransparentIslands(Level level, RadiosityGeometry geo, List<float> verts, List<Vector2> uvs,
            List<int> tris, List<int> triInstance, List<int> triMoverSlot)
        {
            const int words = SplitGridSize * SplitGridSize / 64;
            var result = new List<Instance>(geo.Instances.Count);
            foreach (Instance inst in geo.Instances)
            {
                result.Add(inst);
                int n = inst.Movers.Count;
                if (inst.RetailIslandId >= 0 || n < 2)
                    continue;
                var transparent = new bool[n];
                bool any = false;
                for (int s = 0; s < n; s++)
                    any |= transparent[s] = IsTransparentMover(level.Movers.Entries[inst.Movers[s]]) &&
                                            inst.MoverAreas[s] >= RadiosityTuning.IslandSplitMinArea;
                if (!any)
                    continue;

                // Each transparent mover's chart, rasterised over its triangles.
                var grids = new ulong[n][];
                foreach (int t in inst.Triangles)
                {
                    int s = triMoverSlot[t];
                    if (!transparent[s]) continue;
                    grids[s] ??= new ulong[words];
                    RasterUv(grids[s], uvs[tris[t * 3]], uvs[tris[t * 3 + 1]], uvs[tris[t * 3 + 2]]);
                }

                // First fit among the transparent movers; label 0 = the composite's island.
                var label = new int[n];
                var islandGrids = new List<ulong[]>();
                for (int s = 0; s < n; s++)
                {
                    if (!transparent[s] || grids[s] == null) continue;
                    int cells = 0;
                    foreach (ulong w in grids[s]) cells += PopCount(w);
                    int k = -1;
                    for (int j = 0; j < islandGrids.Count && k < 0; j++)
                    {
                        int overlap = 0;
                        for (int w = 0; w < words; w++) overlap += PopCount(grids[s][w] & islandGrids[j][w]);
                        if (overlap <= RadiosityTuning.IslandSplitMaxOverlap * cells) k = j;
                    }
                    if (k < 0) { k = islandGrids.Count; islandGrids.Add(new ulong[words]); }
                    for (int w = 0; w < words; w++) islandGrids[k][w] |= grids[s][w];
                    label[s] = k + 1;
                }
                if (islandGrids.Count == 0)
                    continue;

                // Groups in order of their first mover, each with its mover slots and triangles in their original order.
                var order = new List<int>();
                for (int s = 0; s < n; s++) if (!order.Contains(label[s])) order.Add(label[s]);
                var slotsOf = order.ToDictionary(l => l, l => Enumerable.Range(0, n).Where(s => label[s] == l).ToList());
                var trianglesOf = order.ToDictionary(l => l, l => new List<int>());
                foreach (int t in inst.Triangles) trianglesOf[label[triMoverSlot[t]]].Add(t);

                int[] movers = inst.Movers.ToArray();
                float[] moverAreas = inst.MoverAreas.ToArray();
                result.RemoveAt(result.Count - 1);
                for (int g = 0; g < order.Count; g++)
                {
                    // The first group reuses the original object, so anything holding it keeps a valid island.
                    Instance target = g == 0 ? inst : new Instance { CompositeInstanceID = inst.CompositeInstanceID };
                    if (g == 0)
                    {
                        target.Movers.Clear(); target.MoverAreas.Clear(); target.Triangles.Clear();
                        target.UvGrid = null;
                        target.UvMinU = float.MaxValue; target.UvMaxU = float.MinValue; target.UvMinV = float.MaxValue; target.UvMaxV = float.MinValue;
                        target.BoundsMin = new Vector3(float.MaxValue); target.BoundsMax = new Vector3(float.MinValue);
                        target.SurfaceArea = 0;
                    }
                    else
                        geo.IslandsSplitOff++;
                    var newSlot = new Dictionary<int, int>();
                    foreach (int s in slotsOf[order[g]])
                    {
                        newSlot[s] = target.Movers.Count;
                        target.Movers.Add(movers[s]);
                        target.MoverAreas.Add(moverAreas[s]);
                        target.SurfaceArea += moverAreas[s];
                    }
                    foreach (int t in trianglesOf[order[g]])
                    {
                        target.Triangles.Add(t);
                        triMoverSlot[t] = newSlot[triMoverSlot[t]];
                        int i0 = tris[t * 3], i1 = tris[t * 3 + 1], i2 = tris[t * 3 + 2];
                        foreach (int vi in new[] { i0, i1, i2 })
                        {
                            Vector3 p = At(verts, vi);
                            target.BoundsMin = Vector3.Min(target.BoundsMin, p);
                            target.BoundsMax = Vector3.Max(target.BoundsMax, p);
                        }
                        MarkUvCoverage(target, uvs, i0, i1, i2);
                    }
                    result.Add(target);
                }
            }
            geo.Instances.Clear();
            geo.Instances.AddRange(result);
            for (int i = 0; i < geo.Instances.Count; i++)
                foreach (int t in geo.Instances[i].Triangles)
                    triInstance[t] = i;
        }

        /// <summary>Set bits of a word (netstandard2.0 has no BitOperations).</summary>
        private static int PopCount(ulong v)
        {
            v -= (v >> 1) & 0x5555555555555555UL;
            v = (v & 0x3333333333333333UL) + ((v >> 2) & 0x3333333333333333UL);
            v = (v + (v >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((v * 0x0101010101010101UL) >> 56);
        }

        /// <summary>Mark the cells of a <see cref="SplitGridSize"/>-square raster of the raw (unwrapped) unit UV square whose
        /// centres a triangle covers, or the cell holding its centroid when it covers none.</summary>
        private static void RasterUv(ulong[] grid, Vector2 a, Vector2 b, Vector2 c)
        {
            const int g = SplitGridSize;
            Vector2 ga = a * g, gb = b * g, gc = c * g;
            int minX = Math.Max(0, Math.Min(g - 1, (int)Math.Floor(Math.Min(ga.X, Math.Min(gb.X, gc.X)))));
            int maxX = Math.Max(0, Math.Min(g - 1, (int)Math.Floor(Math.Max(ga.X, Math.Max(gb.X, gc.X)))));
            int minY = Math.Max(0, Math.Min(g - 1, (int)Math.Floor(Math.Min(ga.Y, Math.Min(gb.Y, gc.Y)))));
            int maxY = Math.Max(0, Math.Min(g - 1, (int)Math.Floor(Math.Max(ga.Y, Math.Max(gb.Y, gc.Y)))));
            float den = (gb.Y - gc.Y) * (ga.X - gc.X) + (gc.X - gb.X) * (ga.Y - gc.Y);
            bool marked = false;
            if (Math.Abs(den) > 1e-9f)
            {
                float inv = 1f / den, a0 = (gb.Y - gc.Y) * inv, b0 = (gc.X - gb.X) * inv, a1 = (gc.Y - ga.Y) * inv, b1 = (ga.X - gc.X) * inv;
                for (int y = minY; y <= maxY; y++)
                    for (int x = minX; x <= maxX; x++)
                    {
                        float px = x + 0.5f - gc.X, py = y + 0.5f - gc.Y, l0 = a0 * px + b0 * py, l1 = a1 * px + b1 * py;
                        if (l0 < -1e-4f || l1 < -1e-4f || l0 + l1 > 1.0001f) continue;
                        grid[(y * g + x) >> 6] |= 1UL << ((y * g + x) & 63);
                        marked = true;
                    }
            }
            if (!marked)
            {
                int cx = Math.Max(0, Math.Min(g - 1, (int)((a.X + b.X + c.X) / 3f * g)));
                int cy = Math.Max(0, Math.Min(g - 1, (int)((a.Y + b.Y + c.Y) / 3f * g)));
                grid[(cy * g + cx) >> 6] |= 1UL << ((cy * g + cx) & 63);
            }
        }

        /// <summary>Resolution of the per-instance UV occupancy grid.</summary>
        private const int UvGridSize = 16;

        /// <summary>
        /// Widen the instance's raw UV bounds by a triangle, and mark the cells of the unit UV
        /// square that the triangle's UV bounding box covers.
        /// </summary>
        private static void MarkUvCoverage(Instance instance, List<Vector2> uvs, int i0, int i1, int i2)
        {
            if (instance.UvGrid == null)
                instance.UvGrid = new bool[UvGridSize * UvGridSize];

            foreach (int vi in new[] { i0, i1, i2 })
            {
                Vector2 raw = uvs[vi];
                if (raw.X < instance.UvMinU) instance.UvMinU = raw.X;
                if (raw.X > instance.UvMaxU) instance.UvMaxU = raw.X;
                if (raw.Y < instance.UvMinV) instance.UvMinV = raw.Y;
                if (raw.Y > instance.UvMaxV) instance.UvMaxV = raw.Y;
            }

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
        /// Retail's rect size for geometry that still carries retail's lightmap UVs, read off the
        /// mesh alone.
        /// </summary>
        internal static bool TryRectFromUvConvention(Instance instance, out int width, out int height)
        {
            width = height = 0;
            if (instance == null || instance.UvMaxU < instance.UvMinU || instance.UvMaxV < instance.UvMinV)
                return false;
            if (!Solve(instance.UvMaxU - instance.UvMinU, out width)) return false;
            if (!Solve(instance.UvMaxV - instance.UvMinV, out height)) return false;
            return true;

            // extent e = (w - 1) / (w - 0.5)  =>  w = (1 - 0.5 e) / (1 - e). The chart's far edge
            // sits at exactly 1.0 and the near one at 0.5 / (w - 0.5), so a chart that was not
            // written by the compiler (or one clipped to a partial island) lands between integers.
            static bool Solve(float extent, out int size)
            {
                size = 0;
                if (!(extent > 0.55f) || extent >= 0.992f)      // below w=2 or beyond w=64
                    return false;
                double w = (1.0 - 0.5 * extent) / (1.0 - extent);
                int rounded = (int)Math.Round(w);
                if (rounded < 2 || rounded > 64 || Math.Abs(w - rounded) > 0.2)
                    return false;
                size = rounded;
                return true;
            }
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

            // The rect's SHAPE has to come from the same grid, because the rect maps this square and
            // nothing else. Columns and rows that carry any UV at all, rather than a bounding box,
            // so one stray triangle cannot stretch the shape to the full square.
            int columns = 0, rows = 0;
            for (int x = 0; x < UvGridSize; x++)
                for (int y = 0; y < UvGridSize; y++)
                    if (instance.UvGrid[y * UvGridSize + x]) { columns++; break; }
            for (int y = 0; y < UvGridSize; y++)
                for (int x = 0; x < UvGridSize; x++)
                    if (instance.UvGrid[y * UvGridSize + x]) { rows++; break; }
            instance.UvAspect = columns > 0 && rows > 0 ? (float)columns / rows : 1.0f;

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
        internal BVHAccel OccluderBvh { get; private set; }

        /// <summary>Triangle count of the occluder soup, or 0 when occluding against render meshes.</summary>
        internal int OccluderTriangleCount { get; private set; }

        /// <summary>Build the BVH. Separated from collection so callers can log the timings.</summary>
        internal void Build(Action<string> log = null)
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
        internal float OccluderEndpointSlack { get; set; }

        /// <summary>Largest share of a link, per end, that <see cref="OccluderEndpointSlack"/> may skip.</summary>
        internal float OccluderSlackFraction { get; set; } = 0.15f;

        /// <summary>Build the occluder BVH from a separate triangle soup.</summary>
        internal void BuildOccluders(float[] verts, int[] tris, Action<string> log = null)
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
        internal Vector3 VisibilityOrigin(Vector3 position, Vector3 normal, float range, float offset)
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
        internal bool Visible(Vector3 from, Vector3 to, float epsilon)
        {
            BVHAccel bvh = OccluderBvh ?? Bvh;

            Vector3 delta = to - from;
            float distance = delta.Length();

            // The slack is capped at a fraction of the link per end, so a short ray is not left effectively untested (a
            // flat 0.35 m let 68.1% of our 0-1 m links pass through render geometry, against retail's 34.2%).
            float slack = epsilon;
            if (OccluderBvh != null)
                slack += Math.Min(OccluderEndpointSlack, distance * OccluderSlackFraction);

            if (distance <= slack * 2.0f)
                return true;

            Vector3 direction = delta / distance;
            var ray = new Ray(from + direction * slack, direction, 0.0f, distance - slack * 2.0f);
            return !bvh.Occluded(ref ray);
        }

        /// <summary>
        /// <see cref="Visible"/> against the RENDER meshes (never the collision occluder set), with a plain epsilon at each end,
        /// optionally softened: the line counts as visible when the centre ray or any of <paramref name="softRays"/> rays, both
        /// ends jittered up to <paramref name="jitter"/> metres across the line, is unblocked. The jitter is seeded from the
        /// endpoints, so a bake is deterministic. The retail-rule cut's visibility test.
        /// </summary>
        internal bool VisibleRenderSoft(Vector3 from, Vector3 to, float epsilon, int softRays, float jitter)
        {
            bool Clear(Vector3 a, Vector3 b)
            {
                Vector3 delta = b - a; float distance = delta.Length();
                if (distance <= epsilon * 2.0f) return true;
                Vector3 direction = delta / distance;
                var ray = new Ray(a + direction * epsilon, direction, 0.0f, distance - epsilon * 2.0f);
                return !Bvh.Occluded(ref ray);
            }
            if (Clear(from, to)) return true;
            Vector3 d = to - from; float len = d.Length(); if (len < 1e-3f) return true; d /= len;
            Vector3 t1 = Vector3.Normalize(Vector3.Cross(d, Math.Abs(d.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX)), t2 = Vector3.Cross(d, t1);
            uint Q(float v) => unchecked((uint)(int)Math.Round(v * 1024.0f));
            uint s = unchecked(Q(from.X) * 73856093u ^ Q(from.Y) * 19349663u ^ Q(from.Z) * 83492791u ^ Q(to.X) * 2654435761u ^ Q(to.Y) * 40503u ^ Q(to.Z) * 97u);
            if (s == 0) s = 0x9E3779B9u;
            float Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / (float)0x800000 - 1f; }
            for (int i = 0; i < softRays; i++)
            {
                float u1 = Next(), v1 = Next(), u2 = Next(), v2 = Next();
                if (Clear(from + (t1 * u1 + t2 * v1) * jitter, to + (t1 * u2 + t2 * v2) * jitter)) return true;
            }
            return false;
        }

        internal Vector3 TriangleCentroid(int tri)
        {
            Vector3 a = At(Tris[tri * 3 + 0]);
            Vector3 b = At(Tris[tri * 3 + 1]);
            Vector3 c = At(Tris[tri * 3 + 2]);
            return (a + b + c) * (1.0f / 3.0f);
        }

        internal float TriangleArea(int tri)
        {
            Vector3 a = At(Tris[tri * 3 + 0]);
            Vector3 b = At(Tris[tri * 3 + 1]);
            Vector3 c = At(Tris[tri * 3 + 2]);
            return Vector3.Cross(b - a, c - a).Length() * 0.5f;
        }

        /// <summary>Geometric normal, flipped towards the interpolated vertex normals.</summary>
        internal Vector3 TriangleNormal(int tri)
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

        /// <summary>Barycentric point on a triangle, plus its interpolated normal, lightmap UV and
        /// diffuse UV (for albedo sampling).</summary>
        internal void SamplePoint(int tri, float u, float v, out Vector3 position, out Vector3 normal,
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
        internal Vector2 DiffuseUvAt(int tri, float u, float v)
        {
            int i0 = Tris[tri * 3 + 0], i1 = Tris[tri * 3 + 1], i2 = Tris[tri * 3 + 2];
            return DiffuseUVs[i0] * (1.0f - u - v) + DiffuseUVs[i1] * u + DiffuseUVs[i2] * v;
        }

        /// <summary>
        /// Albedo at a barycentric point: sampled from the material's diffuse map where the
        /// surface has diffuse UVs, and the material's mean colour where it does not.
        /// </summary>
        internal Vector3 SampleAlbedo(int tri, Vector2 diffuseUv)
        {
            int slot = TriangleMaterial[tri];
            return slot == RadiosityMaterialSampler.NoMaterial
                ? TriangleAlbedo[tri]
                : MaterialSampler.Sample(slot, diffuseUv);
        }

        internal Vector3 At(int vertexIndex) => new Vector3(Verts[vertexIndex * 3], Verts[vertexIndex * 3 + 1], Verts[vertexIndex * 3 + 2]);

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

        private static bool IsBakeable(Movers.MOVER_DESCRIPTOR mover)
        {
            if (mover?.RenderableElements == null || mover.RenderableElements.Count == 0)
                return false;
            if (mover.CullFlags.HasFlag(Movers.CullFlag.NO_RENDER))
                return false;
            // Radiosity is a static bake; moving geometry is lit through the volume probes instead.
            if (mover.Flags != null && !mover.Flags.Stationary)
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
        internal static bool RequiresDynamicRadiosity(Movers.MOVER_DESCRIPTOR mover)
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
        private static Vector3 ResolveEmissive(
            Movers.MOVER_DESCRIPTOR mover, RenderableElements.Element element)
        {
            if (mover.EmissiveFlags.HasFlag(Movers.EmissiveFlag.MasterOff))
                return Vector3.Zero;
            // The material feature alone decides emission: 101 of SCI_Hub's movers carry a multiplier on a non-emissive
            // material and retail lights none of them.
            if (!IsEmissiveMaterial(element))
                return Vector3.Zero;

            float multiplier = ResolveEmissiveStrength(mover, element);
            Vector3 tint = mover.EmissiveTint / 255.0f;
            return tint * multiplier;
        }

        /// <summary>
        /// An emissive element's strength: the material's own EMISSIVE_MULT constant.
        /// </summary>
        internal static float ResolveEmissiveStrength(
            Movers.MOVER_DESCRIPTOR mover, RenderableElements.Element element)
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
                : RadiosityTuning.DefaultEmissiveMultiplier;
            return Math.Max(RadiosityTuning.EmissiveMultiplierFloor,
                   Math.Min(RadiosityTuning.EmissiveMultiplierCeiling, multiplier));
        }

        /// <summary>
        /// The MATERIAL behind a mover's strongest emissive element - the one whose diffuse map
        /// gives the emitter its colour.
        /// </summary>
        internal static Materials.Material ResolveMoverEmissiveMaterial(
            Movers.MOVER_DESCRIPTOR mover)
        {
            if (mover?.RenderableElements == null)
                return null;
            Materials.Material best = null;
            float bestStrength = 0.0f;
            foreach (RenderableElements.Element element in mover.RenderableElements)
            {
                if (!IsEmissiveMaterial(element) || element.Material == null)
                    continue;
                float s = ResolveEmissiveStrength(mover, element);
                if (best != null && s <= bestStrength)
                    continue;
                bestStrength = s;
                best = element.Material;
            }
            return best;
        }

        /// <summary>Strongest emissive element's strength, for the mover-level light passes.</summary>
        internal static float ResolveMoverEmissiveStrength(Movers.MOVER_DESCRIPTOR mover)
        {
            if (mover?.RenderableElements == null)
                return 0.0f;
            float best = 0.0f;
            foreach (RenderableElements.Element element in mover.RenderableElements)
            {
                if (!IsEmissiveMaterial(element))
                    continue;
                float s = ResolveEmissiveStrength(mover, element);
                if (s > best) best = s;
            }
            return best;
        }

        /// <summary>
        /// World-space area AND area-weighted centroid of a mover's emissive elements. The
        /// centroid is what places surface-light samples for an emitter whose emissive surface
        /// never claimed a live atlas texel - the mesh is then the only record of where it is.
        /// Returns false when the mover has no emissive geometry at all.
        /// </summary>
        internal static bool TryMeasureEmissiveCentroid(
            Movers.MOVER_DESCRIPTOR mover, Dictionary<Models.CS2.Component.LOD.Submesh, cMesh> meshCache,
            out Vector3 centroid, out float area)
        {
            centroid = Vector3.Zero;
            area = 0.0f;
            if (mover?.RenderableElements == null)
                return false;

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
                    float triArea = Vector3.Cross(b - a, c - a).Length() * 0.5f;
                    centroid += (a + b + c) / 3.0f * triArea;
                    area += triArea;
                }
            }
            if (area <= 0.0f)
                return false;
            centroid /= area;
            return true;
        }

        /// <summary>
        /// World-space area of a mover's emissive elements, measured from the meshes. For movers
        /// outside the bake geometry, whose area the triangle soup never recorded.
        /// </summary>
        internal static float MeasureEmissiveArea(
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

        /// <summary>
        /// Resolve a material parameter index to its first constant slot, checking room for its
        /// <paramref name="components"/>; false when the shader does not remap it.
        /// </summary>
        internal static bool TryMaterialConstant(Materials.Material material, int parameter, int components, out int remap)
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
        internal static Vector3 ResolveMoverEmissive(Movers.MOVER_DESCRIPTOR mover)
        {
            if (mover?.RenderableElements == null)
                return Vector3.Zero;
            Vector3 best = Vector3.Zero;
            float bestPeak = 0;
            foreach (RenderableElements.Element element in mover.RenderableElements)
            {
                Vector3 e = ResolveEmissive(mover, element);
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
