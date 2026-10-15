#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CATHODE;

namespace CathodeLib.Radiosity
{
    public static partial class RadiosityBaker
    {
        /// <summary>
        /// The emitter colour for a surface light: the emissive material's DIFFUSE_TINT constant
        /// (see <see cref="RadiosityGeometry.ResolveMoverEmissiveMaterial"/>), else the mean of its
        /// diffuse map, else the mover's EmissiveTint - gamma-encoded by the
        /// <see cref="EmissiveLightColour(Vector3)"/> overload.
        /// </summary>
        /// <remarks>
        /// CA_ENVIRONMENT has no emissive texture sampler, so a fixture's light colour comes from its material and is shared
        /// by the whole family: retail records one value per family where the per-mover EmissiveTint varies.
        /// </remarks>
        private static Vector3 EmissiveLightColour(
            Movers.MOVER_DESCRIPTOR mover, RadiosityGeometry geometry)
        {
            // CA's authored emissive colour IS the material's DIFFUSE_TINT constant (27 Aug 2026: 93.5% of Solace's and
            // 95.7% of ChallengeMap3's RADIOSITY_LEVEL.BIN records within 0.02; EMISSIVE_TINT and the diffuse mean are decode
            // traps). Read after command-driven material remapping on purpose: CA read it before
            // ([[ca-pre-remap-sampling-bug]]), so on a remapped mover our value is the correct one.
            Materials.Material tintMat = RadiosityGeometry.ResolveMoverEmissiveMaterial(mover);
            if (tintMat != null && RadiosityGeometry.TryMaterialConstant(
                    tintMat, (int)CATHODE.ShaderTypes.CA_ENVIRONMENT.PARAMETERS.DIFFUSE_TINT, 3, out int tintRemap))
            {
                float r = tintMat.PixelShaderConstants[tintRemap];
                float g = tintMat.PixelShaderConstants[tintRemap + 1];
                float b = tintMat.PixelShaderConstants[tintRemap + 2];
                if (!float.IsNaN(r) && !float.IsNaN(g) && !float.IsNaN(b))
                    return EmissiveLightColour(new Vector3(
                        Math.Max(0f, Math.Min(1f, r)),
                        Math.Max(0f, Math.Min(1f, g)),
                        Math.Max(0f, Math.Min(1f, b))) * 255.0f);
            }

            Vector3 tintBytes = mover.EmissiveTint;
            if (geometry?.MaterialSampler != null)
            {
                Materials.Material mat = RadiosityGeometry.ResolveMoverEmissiveMaterial(mover);
                if (mat != null)
                {
                    Vector3 mean = geometry.MaterialSampler.Mean(geometry.MaterialSampler.Register(mat));
                    // A material with no decodable diffuse map means back to a flat fallback;
                    // only take a mean that actually carries colour information.
                    if (mean.X > 0.0f || mean.Y > 0.0f || mean.Z > 0.0f)
                        tintBytes = mean * 255.0f;
                }
            }
            return EmissiveLightColour(tintBytes);
        }

        /// <summary>
        /// The emitter colour a surface light stores: the colour CA's compiler was fed, under a square root (a gamma-2.0
        /// encode), truncated to a byte: <c>floor(sqrt(255 * tintByte))</c>. Decoded on ChallengeMap3 (25 Aug 2026; all 12
        /// colours checked, e.g. (0.929,0.839,0.643) -> 245,233,204). A pull toward luminance grey, which it replaced,
        /// mis-corrected every hue by a different amount.
        /// </summary>
        private static Vector3 EmissiveLightColour(Vector3 tintBytes)
        {
            // Truncation, not rounding: retail stores 204 for 164 (sqrt gives 204.5) and 85 for
            // 29 (85.99). Rounding here is what produced our 205-for-204 colour mismatches.
            return new Vector3(
                (float)Math.Floor(Math.Sqrt(255.0 * Math.Max(0.0f, Math.Min(255.0f, tintBytes.X)))) / 255.0f,
                (float)Math.Floor(Math.Sqrt(255.0 * Math.Max(0.0f, Math.Min(255.0f, tintBytes.Y)))) / 255.0f,
                (float)Math.Floor(Math.Sqrt(255.0 * Math.Max(0.0f, Math.Min(255.0f, tintBytes.Z)))) / 255.0f);
        }

        /// <summary>
        /// Encode an emissive radiosity multiplier into a surface light's Scale byte.
        /// </summary>
        /// <remarks>
        /// Scale is fixed point in sixteenths, biased by one: the engine reads <c>(Scale + 1) / 16</c>.
        /// <c>ceil(16 * multiplier) - 1</c> reproduces retail's value set ({0, 1, 3, 7, 15, 23, 31} on BSP_TORRENS) for all
        /// but one of its 376 emissive movers. Feed it the multiplier alone: the tint's brightness belongs in the light's RGB.
        /// The field is a full byte, not five bits: Solace's Scale 159 and 255 lights carry 88% of its light energy.
        /// </remarks>
        private static byte EmissiveScaleByte(float multiplier)
        {
            if (multiplier <= 0.0f) return 0;
            int v = (int)Math.Ceiling(16.0 * multiplier) - 1;
            return (byte)Math.Max(0, Math.Min(255, v));
        }

        /// <summary>
        /// The flux constant K in <c>sample Weight = K x sqrt(area / samples)</c> (<see cref="EmissiveWeightByte"/>), used by
        /// every full bake and by a delta bake with fewer than 20 joined emitters. Fitted over every retail emitter of two
        /// levels (RADIOSITY_LEVEL.BIN entity joins): the implied constant piles against the same 176-178 ceiling on both.
        /// </summary>
        private const float DefaultWeightK = 177.0f;

        /// <summary>
        /// What the shipped bake said about each emitting entity (delta path), plus the flux coefficient fitted from it for
        /// entities it does not cover.
        /// </summary>
        /// <remarks>
        /// The shipped bake is ground truth for its own emitters in ways no formula recovers: its Weights encode level state
        /// (Solace's dark sections carry fixtures whose flux is written near zero) and its RGB the true light colours.
        /// Entities in it take its values verbatim; the area model covers new or modified content.
        /// </remarks>
        private sealed class RetailLightPriors
        {
            /// <summary>
            /// One retail light slice of an entity that ships several, chained through LightSlice.SiblingIndex: one per
            /// emissive STATE, each with its own colour, scale, sample count and flux. The engine plays the sibling matching
            /// the entity's runtime state; merging them into one always-on slice lit powered-down rooms.
            /// </summary>
            public sealed class SiblingState
            {
                public int Items;
                public float SumWeight;
                public byte Scale, R, G, B;
            }

            public sealed class Prior
            {
                public float SumWeight;
                public int Items;
                public byte Scale;
                public byte R, G, B;

                /// <summary>
                /// Retail's per-state slices for this entity in chain order (head first); empty
                /// when retail ships a single slice. SumWeight/Items above stay the merged totals.
                /// </summary>
                public List<SiblingState> Siblings = new List<SiblingState>();

                /// <summary>
                /// Weight for one of <paramref name="samples"/> samples such that the entity's TOTAL flux matches retail's:
                /// weights are absolute per-sample gains, so sharing the sum makes the sample count a placement detail
                /// (carrying the retail mean at our count scaled Solace's fixtures 0.3x-3x). The 191 cap can under-deliver
                /// when we place far fewer samples than <see cref="Items"/>.
                /// </summary>
                public byte WeightFor(int samples) => (byte)Math.Max(1, Math.Min(191,
                    (int)Math.Round(SumWeight / Math.Max(1, samples))));
            }

            public float K = DefaultWeightK;
            public readonly Dictionary<(uint, uint), Prior> Priors = new Dictionary<(uint, uint), Prior>();

            private Dictionary<uint, Prior> _byResourceId;
            private HashSet<uint> _ambiguousResourceIds;

            /// <summary>This entity's own prior and nothing else - no resource_id fallback.</summary>
            public Prior LookupExact(Resources.Resource resource)
            {
                if (resource == null) return null;
                return Priors.TryGetValue((resource.composite_instance_id.AsUInt32, resource.resource_id.AsUInt32), out Prior exact) ? exact : null;
            }

            /// <summary>
            /// The prior for an entity whose light decision we can trust as this entity's own:
            /// the exact (instance, resource) match, or a loose match on a resource_id that
            /// exactly ONE retail entity carries - which is what a duplicated composite produces,
            /// since duplication mints a new composite instance id but keeps every resource_id.
            /// </summary>
            /// <remarks>
            /// This is what the lit/unlit decision must use. A resource_id shared by several retail instances only says the
            /// fixture CLASS is lit somewhere (a fixture placed 30 times with 5 lit would force-light the other 25); H24 did
            /// that and took a duplicate's light slices past the engine's 1024 cap, which blacked out the area.
            /// </remarks>
            public Prior LookupOwn(Resources.Resource resource)
            {
                if (resource == null)
                    return null;
                if (Priors.TryGetValue(
                        (resource.composite_instance_id.AsUInt32, resource.resource_id.AsUInt32),
                        out Prior exact))
                    return exact;
                BuildLooseIndex();
                uint id = resource.resource_id.AsUInt32;
                return _ambiguousResourceIds.Contains(id) ? null
                     : (_byResourceId.TryGetValue(id, out Prior prior) ? prior : null);
            }

            /// <summary>
            /// As <see cref="LookupOwn"/>, but an ambiguous resource_id still yields the merged prior. Only for VALUES
            /// (colour, Scale, flux, sample count), where the fixture class beats the scratch fallbacks.
            /// </summary>
            /// <remarks>
            /// Duplicated retail content finds its light values this way: a copy keeps every resource_id under a new
            /// composite_instance_id, so the exact key misses on every copied mover. The K fit
            /// (<see cref="CalibrateWeightCoefficient"/>) joins exact matches only (<see cref="LookupExact"/>).
            /// </remarks>
            public Prior Lookup(Resources.Resource resource)
            {
                if (resource == null)
                    return null;
                if (Priors.TryGetValue(
                        (resource.composite_instance_id.AsUInt32, resource.resource_id.AsUInt32),
                        out Prior prior))
                    return prior;
                BuildLooseIndex();
                return _byResourceId.TryGetValue(resource.resource_id.AsUInt32, out prior) ? prior : null;
            }

            /// <summary>
            /// Build the resource_id indices the loose lookups read. <see cref="CalibrateWeightCoefficient"/>
            /// calls this once the priors are final, before any slice bakes in parallel; the lookups' own call
            /// only builds for a prior set that returned early (empty). The indices are built in locals and
            /// published with <see cref="_byResourceId"/> last, so a reader that finds it set sees the other
            /// one complete.
            /// </summary>
            internal void BuildLooseIndex()
            {
                if (System.Threading.Volatile.Read(ref _byResourceId) != null)
                    return;
                // Several retail instances of one fixture can share a resource_id, and a mover cannot be paired with one of
                // them, so merge: colour and Scale weighted by sample count, flux and count as plain means. This flattens a
                // room's palette; taking the nearest retail instance measured worse (24.73 against 24.00: it finds the wrong
                // twin of a translated copy).
                var groups = new Dictionary<uint, List<Prior>>();
                foreach (KeyValuePair<(uint, uint), Prior> kv in Priors)
                {
                    if (!groups.TryGetValue(kv.Key.Item2, out List<Prior> list))
                        groups[kv.Key.Item2] = list = new List<Prior>();
                    list.Add(kv.Value);
                }
                var byResourceId = new Dictionary<uint, Prior>();
                var ambiguousResourceIds = new HashSet<uint>();
                foreach (KeyValuePair<uint, List<Prior>> g in groups)
                {
                    if (g.Value.Count == 1) { byResourceId[g.Key] = g.Value[0]; continue; }
                    ambiguousResourceIds.Add(g.Key);
                    double items = 0, weight = 0, r = 0, gg = 0, b = 0, scale = 0, n = 0;
                    foreach (Prior p in g.Value)
                    {
                        int w = Math.Max(1, p.Items);
                        items += p.Items; weight += p.SumWeight;
                        r += p.R * w; gg += p.G * w; b += p.B * w; scale += p.Scale * w; n += w;
                    }
                    byResourceId[g.Key] = new Prior
                    {
                        Items = Math.Max(1, (int)Math.Round(items / g.Value.Count)),
                        SumWeight = (float)(weight / g.Value.Count),
                        R = (byte)Math.Round(r / n),
                        G = (byte)Math.Round(gg / n),
                        B = (byte)Math.Round(b / n),
                        Scale = (byte)Math.Round(scale / n)
                    };
                }
                System.Threading.Volatile.Write(ref _ambiguousResourceIds, ambiguousResourceIds);
                System.Threading.Volatile.Write(ref _byResourceId, byResourceId);
            }
        }

        /// <summary>
        /// True when an emitter must produce no surface light: the shipped bake being patched has no prior for it and its
        /// EmissiveRadiosityMultiplier is authored zero (every emitter retail leaves unlit at full material strength stores
        /// 0 while its lit same-model siblings store more). A bake with no priors admits every emitter.
        /// </summary>
        private static bool SuppressedByRetail(RetailLightPriors priors, Movers.MOVER_DESCRIPTOR mover)
        {
            if (mover == null)
                return false;
            // A retail prior is ground truth: the retail bake lit this entity, so we must too (suppressing SCI_Hub's
            // authored-0 lit entities dimmed the level, rmse 16.7 -> 20.7). This entity's own prior only - see LookupOwn.
            if (priors.LookupOwn(mover.Resource) != null)
                return false;

            if (priors.Priors.Count > 0)
            {
                // An emitter with no prior is usually a GUID join failure, not content retail chose not to light, so only
                // the MVR-multiplier rule applies (suppressing more measured worse, SCI_Hub 16.7 -> 17.3).
                return mover.EmissiveRadiosityMultiplier <= 0.0f;
            }
            // No priors (every full bake): admit everything with a resolvable emissive. The authored multiplier is a
            // runtime dynamic-light input, not a bake admission flag: 927 of the 1,148 emitters CA's own Solace bake lit
            // carry 0, and honouring it was the largest cause of missing emitters.
            return false;
        }

        /// <summary>
        /// Per-level flux coefficient K in <c>per-entity Weight sum = K x sqrt(emissive area)</c>,
        /// fitted against the shipped bake the delta path patches.
        /// </summary>
        /// <remarks>
        /// <para>Delta path only: a full bake reuses no retail light values (its prior set is empty, K =
        /// <see cref="DefaultWeightK"/>). A prior is retail's OUTPUT, so it is stale by construction: an edited material
        /// keeps the light retail baked for the old one. On an unedited level the priors are total (Solace: every light of
        /// all three slices).</para>
        /// <para>The fit joins the shipped light slices to our measured emissive areas through RESOURCES.BIN (exact matches
        /// only), which also absorbs any uniform bias in our area measurement. With fewer than 20 joined emitters K stays
        /// <see cref="DefaultWeightK"/>.</para>
        /// </remarks>
        private static RetailLightPriors CalibrateWeightCoefficient(
            Level level, Dictionary<int, float> emissiveAreas, Action<string> log)
        {
            var result = new RetailLightPriors();

            RadiosityRuntime retail = level.RadiosityRuntime;
            if (retail == null || retail.Slices.Count == 0 || level.Resources == null)
                return result;

            // Retail per-entity light data, keyed by resource GUID pair. EntityInstanceIndex is
            // an index into the load-time RESOURCES order, which GetAtWriteIndex still reflects.
            foreach (RadiosityRuntime.RuntimeDataSlice slice in retail.Slices)
            {
                var lights = slice.SurfaceLights;
                if (lights?.LightSlices == null)
                    continue;
                // Per entity within THIS runtime slice: every retail slice with its chain link, so
                // sibling chains (one slice per emissive state) can be reproduced rather than merged.
                var perKey = new Dictionary<(uint, uint), List<(int idx, int sib, RetailLightPriors.SiblingState st)>>();
                for (int li = 0; li < lights.LightSlices.Count; li++)
                {
                    RadiosityRuntime.RuntimeSurfaceLights.LightSlice ls = lights.LightSlices[li];
                    Resources.Resource resource = level.Resources.GetAtWriteIndex(ls.EntityInstanceIndex);
                    if (resource == null || ls.NumItems == 0 || ls.FirstItem >= lights.Lights.Count)
                        continue;

                    var key = (resource.composite_instance_id.AsUInt32, resource.resource_id.AsUInt32);
                    RadiosityRuntime.RuntimeSurfaceLights.Light first = lights.Lights[(int)ls.FirstItem];
                    if (!result.Priors.TryGetValue(key, out RetailLightPriors.Prior prior))
                    {
                        result.Priors[key] = prior = new RetailLightPriors.Prior
                        {
                            Scale = first.Scale,
                            R = first.R,
                            G = first.G,
                            B = first.B
                        };
                    }
                    var state = new RetailLightPriors.SiblingState { Scale = first.Scale, R = first.R, G = first.G, B = first.B };
                    for (uint i = ls.FirstItem; i < ls.FirstItem + ls.NumItems && i < lights.Lights.Count; i++)
                    {
                        prior.SumWeight += lights.Lights[(int)i].Weight;
                        prior.Items++;
                        state.SumWeight += lights.Lights[(int)i].Weight;
                        state.Items++;
                    }
                    if (!perKey.TryGetValue(key, out var list)) perKey[key] = list = new List<(int, int, RetailLightPriors.SiblingState)>();
                    list.Add((li, ls.SiblingIndex, state));
                }
                // Order each multi-slice entity's slices along its SiblingIndex chain (head = the
                // slice nothing points at) and keep the longest chain seen for the entity.
                foreach (var kv in perKey)
                {
                    if (kv.Value.Count < 2 || !result.Priors.TryGetValue(kv.Key, out RetailLightPriors.Prior prior)) continue;
                    var byIdx = kv.Value.ToDictionary(e => e.idx, e => e);
                    var pointed = new HashSet<int>(kv.Value.Where(e => e.sib != 0).Select(e => e.sib));
                    var heads = kv.Value.Where(e => !pointed.Contains(e.idx)).ToList();
                    if (heads.Count != 1) continue;   // not one clean chain; leave merged
                    var chain = new List<RetailLightPriors.SiblingState>();
                    int cur = heads[0].idx, guard = 0;
                    while (byIdx.TryGetValue(cur, out var e) && guard++ < 64)
                    {
                        chain.Add(e.st);
                        if (e.sib == 0) break;
                        cur = e.sib;
                    }
                    if (chain.Count >= 2 && chain.Count > prior.Siblings.Count)
                        prior.Siblings = chain;
                }
            }
            if (result.Priors.Count == 0)
                return result;

            // Fit the area model over the joined emitters for whatever the priors do not cover.
            double sxx = 0, sxy = 0;
            int joined = 0;
            foreach (KeyValuePair<int, float> pair in emissiveAreas)
            {
                if (pair.Value <= 0 || pair.Key >= level.Movers.Entries.Count)
                    continue;
                // Exact (instance, resource) matches only: the fit has always joined only an entity's
                // own retail light; a loose resource_id join would change K.
                RetailLightPriors.Prior prior = result.LookupExact(level.Movers.Entries[pair.Key].Resource);
                if (prior == null || prior.SumWeight <= 0)
                    continue;
                double x = Math.Sqrt(pair.Value);
                sxx += x * x;
                sxy += x * prior.SumWeight;
                joined++;
            }
            if (joined >= 20 && sxx > 0)
                result.K = (float)(sxy / sxx);

            // The priors are final: build the loose index now, on this thread, rather than lazily inside the
            // parallel slice bakes.
            result.BuildLooseIndex();

            log?.Invoke("Radiosity light priors: " + result.Priors.Count + " retail entities, K = " +
                        result.K.ToString("0") + " from " + joined + " joined emitters");
            return result;
        }

        /// <summary>
        /// A sample's Weight: <paramref name="weightK"/> x sqrt(area / samples) - each sample
        /// carries the sqrt-flux of its own AREA SHARE, so the per-entity sum is
        /// K x sqrt(samples x area) and grows with sample count.
        /// </summary>
        /// <remarks>
        /// Decoded 27 Aug 2026: per-emitter ratios against retail fell as n^-0.5 in retail's sample count, which only the
        /// area-SHARE form gives, and SumW = C x sqrt(n x area) fits every emitter of both levels best, with C piling
        /// against one ceiling (<see cref="DefaultWeightK"/>).
        /// </remarks>
        private static byte EmissiveWeightByte(float weightK, float emissiveArea, int samples)
        {
            if (emissiveArea <= 0.0f || samples <= 0) return 1;
            int v = (int)Math.Round(weightK * Math.Sqrt(emissiveArea / samples));
            return (byte)Math.Max(1, Math.Min(191, v));
        }

        /// <summary>
        /// Re-emit every light slice whose entity ships a retail sibling chain as that chain: one
        /// slice per emissive state, on the samples we placed for the entity, each with retail's
        /// colour, scale, sample count and flux for that state, linked head-to-tail through
        /// SiblingIndex exactly as retail links them. Runs once per slice after every emitter pass,
        /// rebuilding the light list, the slice list, the entity list and the LIVE copy together.
        /// Decoded 18 Sep 2026. SiblingIndex indexes the same slice's light-slice list and 0 means
        /// none (group 0 is never a target); a reference that does not resolve inside its own slice
        /// crashes the engine on load (CM7, SCI_Hub).
        /// </summary>
        private static void ExpandSiblingChains(IEnumerable<SliceBake> bakes, RetailLightPriors priors, Action<string> log)
        {
            if (bakes == null) return;
            int entities = 0, added = 0, skippedCap = 0;
            foreach (SliceBake bake in bakes)
            {
                RadiosityRuntime.RuntimeDataSlice sl = bake?.Slice;
                RadiosityRuntime.RuntimeSurfaceLights lights = sl?.SurfaceLights;
                if (lights?.LightSlices == null || lights.LightSliceEntities == null ||
                    lights.LightSliceEntities.Count != lights.LightSlices.Count)
                    continue;
                bool any = false;
                foreach (Resources.Resource ent in lights.LightSliceEntities)
                    if (ent != null && (priors.LookupExact(ent)?.Siblings.Count ?? 0) >= 2) { any = true; break; }
                if (!any) continue;

                var newLights = new List<RadiosityRuntime.RuntimeSurfaceLights.Light>(lights.Lights.Count);
                var newSlices = new List<RadiosityRuntime.RuntimeSurfaceLights.LightSlice>(lights.LightSlices.Count);
                var newEnts = new List<Resources.Resource>(lights.LightSlices.Count);
                int chainSlices = lights.LightSlices.Count;
                for (int i = 0; i < lights.LightSlices.Count; i++)
                {
                    RadiosityRuntime.RuntimeSurfaceLights.LightSlice ls = lights.LightSlices[i];
                    Resources.Resource ent = lights.LightSliceEntities[i];
                    var items = new List<RadiosityRuntime.RuntimeSurfaceLights.Light>(ls.NumItems);
                    for (uint k = ls.FirstItem; k < ls.FirstItem + ls.NumItems && k < lights.Lights.Count; k++)
                        items.Add(lights.Lights[(int)k]);
                    RetailLightPriors.Prior prior = ent != null ? priors.LookupExact(ent) : null;
                    bool expand = prior != null && prior.Siblings.Count >= 2 && items.Count > 0 && ls.SiblingIndex == 0;
                    if (expand && chainSlices + prior.Siblings.Count - 1 > MaxSurfaceLightSlices) { expand = false; skippedCap++; }
                    if (!expand)
                    {
                        ls.FirstItem = (uint)newLights.Count;
                        ls.NumItems = (ushort)items.Count;
                        newLights.AddRange(items);
                        newSlices.Add(ls);
                        newEnts.Add(ent);
                        continue;
                    }
                    entities++;
                    chainSlices += prior.Siblings.Count - 1;
                    for (int s = 0; s < prior.Siblings.Count; s++)
                    {
                        RetailLightPriors.SiblingState st = prior.Siblings[s];
                        int n = Math.Max(1, Math.Min(items.Count, st.Items));
                        // A state retail ships dark (all samples Weight 0, e.g. the 'off' colour of a two-state fixture) stays dark.
                        byte w = st.SumWeight <= 0 ? (byte)0 : (byte)Math.Max(1, Math.Min(191, (int)Math.Round(st.SumWeight / n)));
                        uint first = (uint)newLights.Count;
                        for (int k = 0; k < n; k++)
                        {
                            RadiosityRuntime.RuntimeSurfaceLights.Light l = items[k];
                            l.R = st.R; l.G = st.G; l.B = st.B; l.Scale = st.Scale; l.Weight = w;
                            newLights.Add(l);
                        }
                        if (s > 0)
                        {
                            RadiosityRuntime.RuntimeSurfaceLights.LightSlice prev = newSlices[newSlices.Count - 1];
                            prev.SiblingIndex = (ushort)newSlices.Count;
                            newSlices[newSlices.Count - 1] = prev;
                            added++;
                        }
                        newSlices.Add(new RadiosityRuntime.RuntimeSurfaceLights.LightSlice
                        {
                            FirstItem = first,
                            NumItems = (ushort)n,
                            EntityInstanceIndex = ls.EntityInstanceIndex,
                            SiblingIndex = 0
                        });
                        newEnts.Add(ent);
                    }
                }
                lights.Lights = newLights;
                lights.LightSlices = newSlices;
                lights.LightSliceEntities = newEnts;
                sl.LiveSurfaceLights = new List<RadiosityRuntime.RuntimeSurfaceLights.LightSlice>(newSlices);
                sl.LiveSurfaceLightEntities = new List<Resources.Resource>(newEnts);
            }
            if (entities > 0 || skippedCap > 0)
                log?.Invoke("    retail sibling chains: " + entities + " entities re-emitted as " + (entities + added) +
                            " state slices (+" + added + ")" + (skippedCap > 0 ? ", " + skippedCap + " left merged at the 1024-slice cap" : ""));
        }

        #region SURFACE LIGHTS

        /// <summary>
        /// World-space emissive surface area per mover index, summed over every emissive triangle
        /// the geometry pass collected for it. This is what a surface light's Weight encodes.
        /// </summary>
        private static Dictionary<int, float> ComputeEmissiveAreas(RadiosityGeometry geometry)
        {
            var areas = new Dictionary<int, float>();
            foreach (RadiosityGeometry.Instance instance in geometry.Instances)
            {
                foreach (int tri in instance.Triangles)
                {
                    if (tri >= geometry.TriangleEmissive.Length || geometry.TriangleEmissive[tri] == Vector3.Zero)
                        continue;
                    int slot = tri < geometry.TriangleMoverSlot.Length ? geometry.TriangleMoverSlot[tri] : 0;
                    if (slot < 0 || slot >= instance.Movers.Count)
                        continue;
                    int moverIndex = instance.Movers[slot];
                    areas.TryGetValue(moverIndex, out float a);
                    areas[moverIndex] = a + geometry.TriangleArea(tri);
                }
            }
            return areas;
        }

        /// <summary>
        /// Light sources that feed the engine's direct passes (CA_RADIOSITY_DIRECT_*): the
        /// slice's emissive movers. One LightSlice per source, holding the input probes that
        /// sample it.
        /// </summary>
        /// <remarks>
        /// Scripted LIGHT movers (deferred light entities) are not sampled: retail attaches no light slice to one (SCI_Hub
        /// join), and they already light the scene at runtime (injecting them double-counted 17% of the direct energy).
        /// </remarks>
        private static RadiosityRuntime.RuntimeSurfaceLights BuildSurfaceLights(
            Level level, RadiosityGeometry geometry, List<RadiosityGeometry.Instance> instances,
            SurfaceTexel[] texels, int[] inputProbeForTexel, bool lostEmittersMustBeVisible,
            Dictionary<int, float> emissiveAreas, RetailLightPriors lightPriors, Action<string> log)
        {
            var lights = new RadiosityRuntime.RuntimeSurfaceLights
            {
                LightSliceEntities = new List<Resources.Resource>()
            };

            // Only probes this slice actually owns can sample a light. Both passes need the same
            // set, and the emissive pass needs to be able to search it by position when an emitter
            // is too small to reach the probes it should.
            var candidates = new List<int>();
            for (int i = 0; i < AtlasTexels; i++)
                if (texels[i].Live && inputProbeForTexel[i] >= 0) candidates.Add(i);
            ProbeGrid grid = candidates.Count == 0
                ? null : new ProbeGrid(texels, candidates, RadiosityTuning.MaxInfluenceDistance);

            BuildEmissiveSurfaceLights(level, geometry, texels, inputProbeForTexel, grid, lights, emissiveAreas, lightPriors, log);
            BuildLostEmitterLights(level, geometry, instances, texels, inputProbeForTexel, grid, lights, lostEmittersMustBeVisible, lightPriors, log);
            TrimLightSlices(lights, log);
            return lights;
        }

        /// <summary>
        /// Hold the slice's light table to the engine's limit, keeping the brightest emitters.
        /// </summary>
        /// <remarks>
        /// Overflowing the light-slice table is silent: the slice renders BLACK in game (H24's 1199- and 1092-slice delta
        /// slices lost a whole duplicated environment). Retail's slices each cover one room; ours cover zones and can reach
        /// the limit. Runs after every pass that can add a slice.
        /// </remarks>
        private static void TrimLightSlices(RadiosityRuntime.RuntimeSurfaceLights lights, Action<string> log)
        {
            if (lights?.LightSlices == null || lights.LightSlices.Count <= MaxSurfaceLightSlices)
                return;

            int before = lights.LightSlices.Count;
            // Rank by the flux the slice actually delivers - Weight is an absolute per-sample
            // gain, so the sum over its items is the entity's contribution to the room.
            var ranked = new List<(int index, long flux)>(before);
            for (int i = 0; i < before; i++)
            {
                RadiosityRuntime.RuntimeSurfaceLights.LightSlice ls = lights.LightSlices[i];
                long flux = 0;
                for (uint k = ls.FirstItem; k < ls.FirstItem + ls.NumItems && k < lights.Lights.Count; k++)
                    flux += lights.Lights[(int)k].Weight;
                ranked.Add((i, flux));
            }
            ranked.Sort((a, b) => b.flux != a.flux ? b.flux.CompareTo(a.flux) : a.index.CompareTo(b.index));

            // Keep the strongest, then restore source order so the table stays deterministic.
            var keep = ranked.Take(MaxSurfaceLightSlices).Select(r => r.index).ToList();
            keep.Sort();

            var newLights = new List<RadiosityRuntime.RuntimeSurfaceLights.Light>(lights.Lights.Count);
            var newSlices = new List<RadiosityRuntime.RuntimeSurfaceLights.LightSlice>(keep.Count);
            List<Resources.Resource> newEntities =
                lights.LightSliceEntities != null && lights.LightSliceEntities.Count == before
                    ? new List<Resources.Resource>(keep.Count) : null;
            foreach (int i in keep)
            {
                RadiosityRuntime.RuntimeSurfaceLights.LightSlice ls = lights.LightSlices[i];
                uint first = (uint)newLights.Count;
                int items = 0;
                for (uint k = ls.FirstItem; k < ls.FirstItem + ls.NumItems && k < lights.Lights.Count; k++)
                {
                    newLights.Add(lights.Lights[(int)k]);
                    items++;
                }
                ls.FirstItem = first;
                ls.NumItems = (ushort)items;
                newSlices.Add(ls);
                newEntities?.Add(lights.LightSliceEntities[i]);
            }

            lights.Lights = newLights;
            lights.LightSlices = newSlices;
            if (newEntities != null)
                lights.LightSliceEntities = newEntities;
            log?.Invoke("  WARNING: " + before + " light slices exceed the engine limit of " + MaxSurfaceLightSlices +
                        " - kept the " + newSlices.Count + " brightest, dropped " + (before - newSlices.Count) +
                        " (an overflowed slice renders black)");
        }

        /// <summary>
        /// Lights for emissive movers that never entered the bake geometry at all - dynamic
        /// radiosity movers and members of composites the static gate excluded.
        /// </summary>
        /// <remarks>
        /// On SCI_Hub, 532 of the 544 emitters retail lights and we did not are stationary RADIOSITY_DYNAMIC movers with an
        /// emissive material: rightly kept out of the lightmap, but their emission on the static surfaces around them is
        /// 23% of retail's direct energy. Each is sampled at the nearest input probes of the slice that owns the probes
        /// closest to it.
        /// </remarks>
        private static void AddUnbakedEmitterLights(
            Level level, RadiosityGeometry geometry, SliceBake[] slices,
            bool lostEmittersMustBeVisible, RetailLightPriors lightPriors, Action<string> log)
        {
            // Movers already part of the bake geometry are covered by the per-slice passes.
            var baked = new HashSet<int>();
            foreach (RadiosityGeometry.Instance instance in geometry.Instances)
                foreach (int moverIndex in instance.Movers)
                    baked.Add(moverIndex);

            // Per-slice live texels, for the nearest-probe searches.
            var sliceTexels = new List<int>[slices.Length];
            for (int s = 0; s < slices.Length; s++)
            {
                sliceTexels[s] = new List<int>();
                if (slices[s]?.Texels == null) continue;
                for (int i = 0; i < AtlasTexels; i++)
                    if (slices[s].Texels[i].Live && slices[s].InputProbeForTexel[i] >= 0)
                        sliceTexels[s].Add(i);
            }

            float reach = RadiosityTuning.UnbakedEmitterReach;
            float reachSq = reach * reach;
            int added = 0;

            // Entities that already carry a light slice from the per-slice passes. Needed both to
            // avoid double-lighting and because this pass also rescues baked movers below.
            var alreadyLit = new HashSet<(uint, uint)>();
            foreach (SliceBake sb in slices)
            {
                if (sb?.Slice?.SurfaceLights?.LightSliceEntities == null) continue;
                foreach (Resources.Resource r in sb.Slice.SurfaceLights.LightSliceEntities)
                    if (r != null) alreadyLit.Add((r.composite_instance_id.AsUInt32, r.resource_id.AsUInt32));
            }

            // These movers never entered the geometry pass, so their emissive area has to be
            // measured from the meshes directly. The decode is cached per submesh.
            var meshCache = new Dictionary<Models.CS2.Component.LOD.Submesh, cMesh>();

            for (int moverIndex = 0; moverIndex < level.Movers.Entries.Count; moverIndex++)
            {
                Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[moverIndex];
                if (mover?.Resource == null || mover.RenderableElements == null || mover.RenderableElements.Count == 0)
                    continue;
                if (alreadyLit.Contains((mover.Resource.composite_instance_id.AsUInt32, mover.Resource.resource_id.AsUInt32)))
                    continue;

                // A mover already in the bake geometry is normally the per-slice passes' business;
                // it is only rescued here when the retail bake attached a light to it and every
                // texel/lost-emitter path came up empty (e.g. our emissive strength resolves to 0
                // for a fixture retail lights dimly).
                RetailLightPriors.Prior prior = lightPriors.Lookup(mover.Resource);
                if (baked.Contains(moverIndex) && prior == null)
                    continue;
                if (mover.Flags != null && !mover.Flags.Stationary)
                    continue;
                // An invisible mover cannot be a light (BakeSliceOptions.LostEmittersMustBeVisible: ChallengeMap16's 40
                // invisible decal templates took a real fixture's prior and 1.4x the level's light table).
                if (lostEmittersMustBeVisible && mover.Flags != null && !mover.Flags.Visible)
                    continue;
                if (mover.CullFlags.HasFlag(Movers.CullFlag.NO_RENDER))
                    continue;
                switch (mover.GetRenderableType())
                {
                    case RenderableInstanceType.ENVIRONMENT:
                    case RenderableInstanceType.ENVIRONMENT_EXTRA:
                    case RenderableInstanceType.MISC:
                        break;
                    default:
                        continue;
                }

                if (SuppressedByRetail(lightPriors, mover))
                    continue;

                Vector3 emissive = RadiosityGeometry.ResolveMoverEmissive(mover);
                float peak = Math.Max(emissive.X, Math.Max(emissive.Y, emissive.Z));
                if (peak <= 0 && prior == null)
                    continue;
                // A LOOSE prior (resource_id only) can make a light of a mover with no emissive of its own (ChallengeMap16:
                // 40 plain grey parts shared a lit assembly's resource_id and took 1.4x the level's light table). The
                // delta path, the only one with priors, does not guard against it.

                // The emitter is sampled from its transform origin. A panel assembly's pivot can sit
                // metres from its glowing faces (day-4 fixture audit: our samples 1.6-5.4 m from the
                // biggest luminous panels, retail's 0-0.3 m); sampling from the emissive centroid
                // instead was built and left off.
                Vector3 position = new Vector3(mover.Transform.M41, mover.Transform.M42, mover.Transform.M43);

                // The slice whose probes sit closest to the emitter samples it, like retail's
                // single-slice attribution.
                int bestSlice = -1;
                float bestDistance = float.MaxValue;

                // Picking the slice with the MOST live texels within reach instead (for zone-chunked rooms) changed
                // nothing: stranded lights come from the per-slice BuildSurfaceLights, not from this pass (26 Aug 2026).
                for (int s = 0; s < slices.Length; s++)
                {
                    foreach (int texel in sliceTexels[s])
                    {
                        float d = Vector3.DistanceSquared(slices[s].Texels[texel].Position, position);
                        if (d < bestDistance) { bestDistance = d; bestSlice = s; }
                    }
                }
                if (bestSlice < 0 || bestDistance > reachSq)
                    continue;

                SliceBake slice = slices[bestSlice];
                var nearest = new List<(int texel, float distanceSq)>();
                foreach (int texel in sliceTexels[bestSlice])
                {
                    float d = Vector3.DistanceSquared(slice.Texels[texel].Position, position);
                    if (d > reachSq) continue;
                    // No injection through walls: the sample carries the light's full energy
                    // with no runtime visibility term.
                    if (!geometry.Visible(position, slice.Texels[texel].RayOrigin, RadiosityTuning.RayEpsilon))
                        continue;
                    nearest.Add((texel, d));
                }
                nearest.Sort((a, b) => a.distanceSq.CompareTo(b.distanceSq));

                float radiusSq = RadiosityTuning.EmitterSampleRadius * RadiosityTuning.EmitterSampleRadius;
                bool matchCount = prior != null;
                int wantProbes = matchCount
                    ? Math.Max(1, Math.Min(MaxLightsPerEmitter, prior.Items))
                    : RadiosityTuning.MinProbesPerEmitter;
                var chosen = new List<int>();
                var seen = new HashSet<int>();
                foreach ((int texel, float distanceSq) in nearest)
                {
                    bool needed = seen.Count < wantProbes;
                    if (!needed && distanceSq > radiusSq)
                        break;
                    if (seen.Count >= (matchCount ? wantProbes : MaxLightsPerEmitter))
                        break;
                    if (!seen.Add(slice.InputProbeForTexel[texel]))
                        continue;
                    chosen.Add(texel);
                }
                if (chosen.Count == 0)
                    continue;

                RadiosityRuntime.RuntimeSurfaceLights lights = slice.Slice.SurfaceLights;
                if (lights.Lights.Count + chosen.Count > MaxSurfaceLightProbes ||
                    lights.LightSlices.Count >= MaxSurfaceLightSlices)
                    continue;

                Vector3 tint = EmissiveLightColour(mover, geometry);
                float multiplier = RadiosityGeometry.ResolveMoverEmissiveStrength(mover);
                byte scale = prior != null ? prior.Scale : EmissiveScaleByte(multiplier);
                byte weight = prior != null
                    ? prior.WeightFor(chosen.Count)
                    : EmissiveWeightByte(lightPriors.K,
                        RadiosityGeometry.MeasureEmissiveArea(mover, meshCache), chosen.Count);

                uint first = (uint)lights.Lights.Count;
                foreach (int texel in chosen)
                {
                    InputProbeTexel(slice.InputProbeForTexel[texel], out int px, out int py);
                    lights.Lights.Add(new RadiosityRuntime.RuntimeSurfaceLights.Light
                    {
                        U = (byte)px,
                        V = (byte)py,
                        AnimHi = 4,
                        AnimLo = 0,
                        R = prior != null ? prior.R : ToByte(tint.X),
                        G = prior != null ? prior.G : ToByte(tint.Y),
                        B = prior != null ? prior.B : ToByte(tint.Z),
                        Scale = scale,
                        Weight = weight,
                        TintR = 255,
                        TintG = 255,
                        TintB = 255,
                        Flags = 0
                    });
                }

                var lightSlice = new RadiosityRuntime.RuntimeSurfaceLights.LightSlice
                {
                    FirstItem = first,
                    NumItems = (ushort)(lights.Lights.Count - first),
                    EntityInstanceIndex = -1,
                    SiblingIndex = 0
                };
                lights.LightSlices.Add(lightSlice);
                lights.LightSliceEntities.Add(mover.Resource);
                slice.Slice.LiveSurfaceLights.Add(lightSlice);
                slice.Slice.LiveSurfaceLightEntities.Add(mover.Resource);
                added++;
            }

            log?.Invoke("Radiosity unbaked-emitter lights: " + added + " movers");
        }

        /// <summary>
        /// Lights for emissive movers that ended the rasterise pass with no live emissive texel,
        /// sampled at the input probes nearest their emissive surface.
        /// </summary>
        /// <remarks>
        /// An emitter only reaches <see cref="BuildEmissiveSurfaceLights"/> through its live atlas texels, and many never get
        /// one (the emissive submesh loses a shared rect's texels to its mates): retail ships 1818 light slices on SCI_Hub,
        /// texel-derived emitters alone gave us 1211. Any mover with emissive triangles and no texel-derived light gets one
        /// here.
        /// </remarks>
        private static void BuildLostEmitterLights(
            Level level, RadiosityGeometry geometry, List<RadiosityGeometry.Instance> instances,
            SurfaceTexel[] texels, int[] inputProbeForTexel, ProbeGrid grid,
            RadiosityRuntime.RuntimeSurfaceLights lights, bool lostEmittersMustBeVisible,
            RetailLightPriors lightPriors, Action<string> log = null)
        {
            if (grid == null)
                return;

            // Movers already covered by the texel pass (or by an earlier lost-emitter entry).
            var covered = new HashSet<int>();
            for (int i = 0; i < AtlasTexels; i++)
                if (texels[i].Live && inputProbeForTexel[i] >= 0 && texels[i].Emissive != Vector3.Zero)
                    covered.Add(texels[i].MoverIndex);

            foreach (RadiosityGeometry.Instance instance in instances)
            {
                // Area-weighted emissive centroid and mean radiance per mover slot.
                var centroid = new Vector3[instance.Movers.Count];
                var areaSum = new float[instance.Movers.Count];
                var radiance = new Vector3[instance.Movers.Count];
                foreach (int tri in instance.Triangles)
                {
                    if (tri >= geometry.TriangleEmissive.Length || geometry.TriangleEmissive[tri] == Vector3.Zero)
                        continue;
                    int slot = tri < geometry.TriangleMoverSlot.Length ? geometry.TriangleMoverSlot[tri] : 0;
                    if (slot < 0 || slot >= instance.Movers.Count)
                        continue;
                    float area = geometry.TriangleArea(tri);
                    centroid[slot] += geometry.TriangleCentroid(tri) * area;
                    radiance[slot] += geometry.TriangleEmissive[tri] * area;
                    areaSum[slot] += area;
                }

                for (int slot = 0; slot < instance.Movers.Count; slot++)
                {
                    if (areaSum[slot] <= 0)
                        continue;
                    int moverIndex = instance.Movers[slot];
                    if (moverIndex < 0 || moverIndex >= level.Movers.Entries.Count || !covered.Add(moverIndex))
                        continue;

                    Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[moverIndex];
                    if (SuppressedByRetail(lightPriors, mover))
                        continue;
                    // Off-geometry emitters are where the invisible decal templates arrive (they are
                    // occluder instances with no emissive triangles): see AddUnbakedEmitterLights.
                    if (lostEmittersMustBeVisible && mover.Flags != null && !mover.Flags.Visible)
                        continue;
                    RetailLightPriors.Prior prior = lightPriors.Lookup(mover.Resource);
                    Vector3 centre = centroid[slot] / areaSum[slot];
                    Vector3 emissive = radiance[slot] / areaSum[slot];
                    float peak = Math.Max(emissive.X, Math.Max(emissive.Y, emissive.Z));
                    if (peak <= 0)
                        continue;

                    // Nearest distinct input probes, mirroring the texel pass's sampling. Probes
                    // the emitter cannot see are skipped - injection through a wall lights the
                    // wrong room regardless of the light's runtime state.
                    var nearest = new List<(int texel, float distanceSq)>();
                    foreach (int texel in grid.Neighbours(centre))
                    {
                        if (!geometry.Visible(centre, texels[texel].RayOrigin, RadiosityTuning.RayEpsilon))
                            continue;
                        nearest.Add((texel, (texels[texel].Position - centre).LengthSquared()));
                    }
                    if (nearest.Count == 0)
                    {
                        // A centroid buried inside the fixture's shell fails every ray: fall back to the nearest probes
                        // without the ray test, tight enough (1.5 m) to stay on the fixture's own wall.
                        float blindSq = 1.5f * 1.5f;
                        foreach (int texel in grid.Neighbours(centre))
                        {
                            float d = (texels[texel].Position - centre).LengthSquared();
                            if (d <= blindSq)
                                nearest.Add((texel, d));
                        }
                        if (nearest.Count == 0)
                            continue;
                    }
                    nearest.Sort((a, b) => a.distanceSq.CompareTo(b.distanceSq));

                    float radiusSq = RadiosityTuning.EmitterSampleRadius * RadiosityTuning.EmitterSampleRadius;
                    bool matchCount = prior != null;
                    int wantProbes = matchCount
                        ? Math.Max(1, Math.Min(MaxLightsPerEmitter, prior.Items))
                        : RadiosityTuning.MinProbesPerEmitter;
                    var chosen = new List<int>();
                    var seen = new HashSet<int>();
                    foreach ((int texel, float distanceSq) in nearest)
                    {
                        bool needed = seen.Count < wantProbes;
                        if (!needed && distanceSq > radiusSq)
                            break;
                        if (seen.Count >= (matchCount ? wantProbes : MaxLightsPerEmitter))
                            break;
                        if (!seen.Add(inputProbeForTexel[texel]))
                            continue;
                        chosen.Add(texel);
                    }
                    if (chosen.Count == 0)
                        continue;

                    Vector3 tint = EmissiveLightColour(mover, geometry);
                    float multiplier = RadiosityGeometry.ResolveMoverEmissiveStrength(mover);
                    byte scale = prior != null ? prior.Scale : EmissiveScaleByte(multiplier);
                    byte lostWeight = prior != null
                        ? prior.WeightFor(chosen.Count)
                        : EmissiveWeightByte(lightPriors.K, areaSum[slot], chosen.Count);

                    uint first = (uint)lights.Lights.Count;
                    foreach (int texel in chosen)
                    {
                        InputProbeTexel(inputProbeForTexel[texel], out int px, out int py);
                        lights.Lights.Add(new RadiosityRuntime.RuntimeSurfaceLights.Light
                        {
                            U = (byte)px,
                            V = (byte)py,
                            AnimHi = 4,
                            AnimLo = 0,
                            R = prior != null ? prior.R : ToByte(tint.X),
                            G = prior != null ? prior.G : ToByte(tint.Y),
                            B = prior != null ? prior.B : ToByte(tint.Z),
                            Scale = scale,
                            Weight = lostWeight,
                            TintR = 255,
                            TintG = 255,
                            TintB = 255,
                            Flags = 0
                        });
                    }

                    lights.LightSlices.Add(new RadiosityRuntime.RuntimeSurfaceLights.LightSlice
                    {
                        FirstItem = first,
                        NumItems = (ushort)(lights.Lights.Count - first),
                        EntityInstanceIndex = -1,
                        SiblingIndex = 0
                    });
                    lights.LightSliceEntities.Add(mover.Resource);
                }
            }

            // ---- emitters with no emissive TRIANGLES in the bake geometry at all -----------
            // Their emissive submesh was excluded from the bake geometry entirely (219 of the 1,342 emitters CA lit on
            // Solace, e.g. 94 of 95 SPOTS_01), so the mesh is the only record of them. Each is owned by exactly ONE slice:
            // the one holding the nearest bake instance to its emissive centroid, computable identically from every slice.
            // Admitting it into "any slice with a probe nearby" shipped its full flux into two or three interleaved slices
            // (the spliced table rendered 2.23x retail).
            var sliceInstances = new HashSet<RadiosityGeometry.Instance>(instances);
            var meshCache = new Dictionary<Models.CS2.Component.LOD.Submesh, cMesh>();
            int fallbackEmitters = 0, fallbackLights = 0;
            // Skip-reason census for the residual coverage tail (whole families such as CONDUIT_PIPES still shipped nothing
            // when this pass first landed).
            int skipNoCentroid = 0, skipElection = 0, skipNoVisible = 0;

            for (int moverIndex = 0; moverIndex < level.Movers.Entries.Count; moverIndex++)
            {
                if (covered.Contains(moverIndex))
                    continue;
                Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[moverIndex];
                if (mover?.RenderableElements == null || SuppressedByRetail(lightPriors, mover))
                    continue;
                float multiplier = RadiosityGeometry.ResolveMoverEmissiveStrength(mover);
                if (multiplier <= 0.0f)
                    continue;
                if (!RadiosityGeometry.TryMeasureEmissiveCentroid(mover, meshCache, out Vector3 centre, out float area))
                { skipNoCentroid++; continue; }

                RadiosityGeometry.Instance nearestInstance = null;
                float bestDistSq = float.MaxValue;
                foreach (RadiosityGeometry.Instance inst in geometry.Instances)
                {
                    if (inst.DonorOnly)
                        continue;
                    float d = (inst.Centre - centre).LengthSquared();
                    if (d < bestDistSq) { bestDistSq = d; nearestInstance = inst; }
                }
                if (nearestInstance == null || !sliceInstances.Contains(nearestInstance))
                { skipElection++; continue; }

                var nearest = new List<(int texel, float distanceSq)>();
                foreach (int texel in grid.Neighbours(centre))
                {
                    if (!geometry.Visible(centre, texels[texel].RayOrigin, RadiosityTuning.RayEpsilon))
                        continue;
                    nearest.Add((texel, (texels[texel].Position - centre).LengthSquared()));
                }
                if (nearest.Count == 0)
                {
                    // A centroid buried inside the fixture's own shell fails every visibility ray. Retail lights these, so
                    // fall back to the nearest probes WITHOUT the ray test, kept tight so the blind injection stays on the
                    // fixture's own wall rather than lighting the next room through it.
                    float blindSq = 1.5f * 1.5f;
                    foreach (int texel in grid.Neighbours(centre))
                    {
                        float d = (texels[texel].Position - centre).LengthSquared();
                        if (d <= blindSq)
                            nearest.Add((texel, d));
                    }
                    if (nearest.Count == 0)
                    { skipNoVisible++; continue; }
                }
                nearest.Sort((a, b) => a.distanceSq.CompareTo(b.distanceSq));

                RetailLightPriors.Prior prior = lightPriors.Lookup(mover.Resource);
                float radiusSq = RadiosityTuning.EmitterSampleRadius * RadiosityTuning.EmitterSampleRadius;
                bool matchCount = prior != null;
                int wantProbes = matchCount
                    ? Math.Max(1, Math.Min(MaxLightsPerEmitter, prior.Items))
                    : RadiosityTuning.MinProbesPerEmitter;
                var chosen = new List<int>();
                var seen = new HashSet<int>();
                foreach ((int texel, float distanceSq) in nearest)
                {
                    bool needed = seen.Count < wantProbes;
                    if (!needed && distanceSq > radiusSq)
                        break;
                    if (seen.Count >= (matchCount ? wantProbes : MaxLightsPerEmitter))
                        break;
                    if (!seen.Add(inputProbeForTexel[texel]))
                        continue;
                    chosen.Add(texel);
                }
                if (chosen.Count == 0)
                    continue;
                covered.Add(moverIndex);

                Vector3 tint = EmissiveLightColour(mover, geometry);
                byte scale = prior != null ? prior.Scale : EmissiveScaleByte(multiplier);
                byte weight = prior != null
                    ? prior.WeightFor(chosen.Count)
                    : EmissiveWeightByte(lightPriors.K, area, chosen.Count);

                uint first = (uint)lights.Lights.Count;
                foreach (int texel in chosen)
                {
                    InputProbeTexel(inputProbeForTexel[texel], out int px, out int py);
                    lights.Lights.Add(new RadiosityRuntime.RuntimeSurfaceLights.Light
                    {
                        U = (byte)px,
                        V = (byte)py,
                        AnimHi = 4,
                        AnimLo = 0,
                        R = prior != null ? prior.R : ToByte(tint.X),
                        G = prior != null ? prior.G : ToByte(tint.Y),
                        B = prior != null ? prior.B : ToByte(tint.Z),
                        Scale = scale,
                        Weight = weight,
                        TintR = 255,
                        TintG = 255,
                        TintB = 255,
                        Flags = 0
                    });
                }
                fallbackEmitters++;
                fallbackLights += (int)(lights.Lights.Count - first);

                lights.LightSlices.Add(new RadiosityRuntime.RuntimeSurfaceLights.LightSlice
                {
                    FirstItem = first,
                    NumItems = (ushort)(lights.Lights.Count - first),
                    EntityInstanceIndex = -1,
                    SiblingIndex = 0
                });
                lights.LightSliceEntities.Add(mover.Resource);
            }

            if (fallbackEmitters > 0 || skipNoVisible > 0)
                log?.Invoke("    off-geometry emitters: " + fallbackEmitters +
                            " movers with no emissive triangles in geometry emitted " + fallbackLights +
                            " lights (nearest-instance slice election; skipped: " +
                            skipNoCentroid + " no-centroid, " + skipElection + " other-slice, " +
                            skipNoVisible + " no-probe-even-blind)");
        }

        private static void BuildEmissiveSurfaceLights(
            Level level, RadiosityGeometry geometry, SurfaceTexel[] texels, int[] inputProbeForTexel, ProbeGrid grid,
            RadiosityRuntime.RuntimeSurfaceLights lights,
            Dictionary<int, float> emissiveAreas, RetailLightPriors lightPriors, Action<string> log)
        {
            int colourChecked = 0, colourExact = 0, colourNear = 0;
            // How many LIGHTS ship retail's prior values versus our own derivation? The emitter-level
            // prior count does not answer this - one emitter becomes several lights.
            int lightsFromPrior = 0, lightsDerived = 0, emittersPrior = 0, emittersDerived = 0;
            var byMover = new Dictionary<int, List<int>>();
            for (int i = 0; i < AtlasTexels; i++)
            {
                if (!texels[i].Live || inputProbeForTexel[i] < 0)
                    continue;
                if (texels[i].Emissive == Vector3.Zero)
                    continue;
                if (!byMover.TryGetValue(texels[i].MoverIndex, out List<int> bucket))
                    byMover[texels[i].MoverIndex] = bucket = new List<int>();
                bucket.Add(i);
            }

            // Our lights sit on 20-25% fewer distinct input probes than retail's (ChallengeMap4). Preferring unclaimed
            // probes was REJECTED: spreading one entity's flux over more probes delivers MORE light, so it brightened the
            // over-bright rooms further (1.40x -> 1.45x); the probe count is not what makes retail's dim rooms dim.
            foreach (KeyValuePair<int, List<int>> pair in byMover.OrderBy(o => o.Key))
            {
                if (pair.Key < 0 || pair.Key >= level.Movers.Entries.Count)
                    continue;

                Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[pair.Key];
                if (SuppressedByRetail(lightPriors, mover))
                    continue;
                RetailLightPriors.Prior prior = lightPriors.Lookup(mover.Resource);
                uint first = (uint)lights.Lights.Count;

                // One light per emissive texel, up to retail's maximum (MaxLightsPerEmitter; retail averages 2.91 per
                // slice, median 2). Above the cap, thin evenly: the texels are spread over the emitter, so every nth keeps
                // the coverage.
                List<int> emissiveTexels = pair.Value;
                int target = Math.Min(MaxLightsPerEmitter, Math.Max(1, emissiveTexels.Count));
                if (emissiveTexels.Count > target)
                {
                    var thinned = new List<int>(target);
                    double step = emissiveTexels.Count / (double)target;
                    for (int i = 0; i < target; i++)
                        thinned.Add(emissiveTexels[Math.Min(emissiveTexels.Count - 1, (int)(i * step))]);
                    emissiveTexels = thinned;
                }

                // Colour and strength are stored separately: RGB keeps the tint at its own brightness (retail's light RGB
                // reaches 255 on only 47% of BSP_TORRENS lights) and Scale carries the multiplier. Normalising RGB to a
                // 255 peak pushed brightness into Scale, off its sixteenths grid.
                Vector3 emissiveTint = EmissiveLightColour(mover, geometry);
                // Self-check of the colour decode: where a mover has a retail prior, score our derivation against the
                // colour retail shipped (logged).
                if (prior != null)
                {
                    colourChecked++;
                    if (prior.R == ToByte(emissiveTint.X) && prior.G == ToByte(emissiveTint.Y) &&
                        prior.B == ToByte(emissiveTint.Z)) colourExact++;
                    else if (Math.Abs(prior.R - ToByte(emissiveTint.X)) <= 2 &&
                             Math.Abs(prior.G - ToByte(emissiveTint.Y)) <= 2 &&
                             Math.Abs(prior.B - ToByte(emissiveTint.Z)) <= 2) colourNear++;
                }
                // The material's EMISSIVE_MULT, retail's Scale source (see RadiosityGeometry.ResolveEmissiveStrength for the
                // decode and the 1.0 fallback).
                float multiplier = RadiosityGeometry.ResolveMoverEmissiveStrength(mover);
                byte emissiveScale = EmissiveScaleByte(multiplier);

                // Two texels of one emitter resolving to the same input probe would stack two lights at one position
                // (42% -> 77% coincident once input probes were thinned; retail 59%).
                var probesUsed = new HashSet<int>();

                // An emitter's own texels are a poor sample set: its rect is small, so most resolve to one or two probes
                // and cast from a point. Retail's item counts (53% two, 25% three, 11% four) barely depend on emitter size
                // (correlation with log area 0.099 over 1834 slices): they are the probes that lie near the emitter, so
                // gather everything within a short radius, floored so nothing ships as one point light. With a retail
                // prior, its sample count is the target (Weight is per sample): ChallengeMap4's light table came within
                // 0.7% of retail's count and 0.03% of its energy.
                bool matchCount = prior != null;
                int wantProbes = matchCount
                    ? Math.Max(1, Math.Min(MaxLightsPerEmitter, prior.Items))
                    : RadiosityTuning.MinProbesPerEmitter;

                var extra = new List<int>();
                var distinct = new HashSet<int>();
                foreach (int texel in emissiveTexels)
                    if (inputProbeForTexel[texel] >= 0) distinct.Add(inputProbeForTexel[texel]);

                Vector3 centre = Vector3.Zero;
                foreach (int texel in emissiveTexels) centre += texels[texel].Position;
                centre /= Math.Max(1, emissiveTexels.Count);

                var nearby = new List<(int texel, float distanceSq)>();
                // Retail puts a big fixture's samples within ~1 m of it on as many distinct probes as it has items
                // (Solace's Anesidora assemblies: 79 on 79, p90 1.0 m). The grid's 32 m cells reach far past that, so a
                // count-matched emitter that is short takes the next-nearest visible probes below.
                foreach (int texel in grid.Neighbours(centre))
                {
                    int probe = inputProbeForTexel[texel];
                    if (probe < 0 || distinct.Contains(probe))
                        continue;
                    // No injection through walls: a sample injects the light's full energy with no runtime visibility
                    // term, so a nearest-by-distance pick could light the next room through the wall the fixture hangs on.
                    if (!geometry.Visible(centre, texels[texel].RayOrigin, RadiosityTuning.RayEpsilon))
                        continue;
                    nearby.Add((texel, (texels[texel].Position - centre).LengthSquared()));
                }
                nearby.Sort((a, b) => a.distanceSq.CompareTo(b.distanceSq));

                // No distance cap while a count-matched emitter is still short: it takes the next-nearest
                // visible probe however far away (52 of BSP_Torrens' theatre panels' 73 samples sat in a
                // vent shaft 20-30 m away, 23 Sep 2026). A hard cap was built and left off.
                float radiusSq = RadiosityTuning.EmitterSampleRadius * RadiosityTuning.EmitterSampleRadius;
                foreach ((int texel, float distanceSq) in nearby)
                {
                    bool needed = distinct.Count < wantProbes;
                    if (!needed && distanceSq > radiusSq)
                        break;
                    if (distinct.Count >= MaxLightsPerEmitter)
                        break;
                    if (!distinct.Add(inputProbeForTexel[texel]))
                        continue;
                    extra.Add(texel);
                }

                // The topped-up texels carry the emitter's radiance, not their own - they are
                // standing in for the emitter at a probe it could not reach, and most of them are
                // not emissive surfaces themselves.
                Vector3 emitterRadiance = Vector3.Zero;
                foreach (int texel in emissiveTexels) emitterRadiance += texels[texel].Emissive;
                emitterRadiance /= Math.Max(1, emissiveTexels.Count);

                // The emitter's own texels nearest its centroid first, so a count cap keeps the
                // samples compact around the fixture the way retail's are, instead of taking the
                // first N in atlas order across a 6 m assembly.
                IEnumerable<int> ownTexels = emissiveTexels;
                if (emissiveTexels.Count > 1)
                {
                    Vector3 c2 = Vector3.Zero;
                    foreach (int texel in emissiveTexels) c2 += texels[texel].Position;
                    c2 /= emissiveTexels.Count;
                    ownTexels = emissiveTexels.OrderBy(t => Vector3.DistanceSquared(texels[t].Position, c2)).ToList();
                }
                foreach (int texel in ownTexels.Concat(extra))
                {
                    // Matching retail's count is a cap as well as a floor: an over-count scales
                    // the entity's flux up by the same per-sample-gain arithmetic.
                    if (probesUsed.Count >= (matchCount ? wantProbes : MaxLightsPerEmitter))
                        break;
                    int probe = inputProbeForTexel[texel];
                    if (probe < 0 || !probesUsed.Add(probe))
                        continue;

                    InputProbeTexel(probe, out int px, out int py);
                    Vector3 emissive = texels[texel].Emissive;
                    if (emissive == Vector3.Zero)
                        emissive = emitterRadiance;
                    float peak = Math.Max(emissive.X, Math.Max(emissive.Y, emissive.Z));
                    if (peak <= 0)
                        continue;

                    lights.Lights.Add(new RadiosityRuntime.RuntimeSurfaceLights.Light
                    {
                        U = (byte)px,
                        V = (byte)py,
                        // TODO: AnimHi / AnimLo drive flicker animation. Retail writes (4, 0) for
                        // steady lights, so that is what a static bake emits.
                        AnimHi = 4,
                        AnimLo = 0,
                        R = prior != null ? prior.R : ToByte(emissiveTint.X),
                        G = prior != null ? prior.G : ToByte(emissiveTint.Y),
                        B = prior != null ? prior.B : ToByte(emissiveTint.Z),
                        Scale = prior != null ? prior.Scale : emissiveScale,
                        // Patched once the emitter's sample count is known (below).
                        Weight = 1,
                        // Retail leaves the tint neutral on every light (3915 of 3915 on Solace); the colour rides in R/G/B.
                        TintR = 255,
                        TintG = 255,
                        TintB = 255,
                        Flags = 0
                    });
                }

                int count = lights.Lights.Count - (int)first;
                if (prior != null) { lightsFromPrior += count; if (count > 0) emittersPrior++; }
                else               { lightsDerived  += count; if (count > 0) emittersDerived++; }
                if (count == 0)
                    continue;

                // The flux is per entity, shared over however many samples it ended up with -
                // retail's own flux where the entity exists in the retail bake, the fitted area
                // model otherwise.
                byte flux = prior != null
                    ? prior.WeightFor(count)
                    : EmissiveWeightByte(lightPriors.K,
                        emissiveAreas.TryGetValue(pair.Key, out float a) ? a : 0.0f, count);
                for (int k = (int)first; k < lights.Lights.Count; k++)
                {
                    RadiosityRuntime.RuntimeSurfaceLights.Light light = lights.Lights[k];
                    light.Weight = flux;
                    lights.Lights[k] = light;
                }

                // Retail stores a RESOURCES.BIN index here, which is how the runtime turns a light
                // entity on and off. The index is resolved from the Resource on save, because this
                // runs before RESOURCES.BIN has been renumbered.
                lights.LightSlices.Add(new RadiosityRuntime.RuntimeSurfaceLights.LightSlice
                {
                    FirstItem = first,
                    NumItems = (ushort)Math.Min(ushort.MaxValue, count),
                    EntityInstanceIndex = -1,
                    SiblingIndex = 0
                });
                lights.LightSliceEntities.Add(mover.Resource);
            }

            if (colourChecked > 0)
                log?.Invoke("    light colour vs retail priors: " + colourExact + " exact, " + colourNear +
                            " within 2, of " + colourChecked + " emitters with a prior (" +
                            (100.0 * colourExact / colourChecked).ToString("0.0") + "% exact)");
            log?.Invoke("    LIGHT SOURCE: " + lightsFromPrior + " lights from retail priors, " +
                        lightsDerived + " derived by us  (" +
                        (lightsFromPrior + lightsDerived > 0 ? (100.0 * lightsFromPrior / (lightsFromPrior + lightsDerived)).ToString("0.0") : "-") +
                        "% scavenged);  emitters: " + emittersPrior + " prior, " + emittersDerived + " derived");
        }

        #endregion
    }
}
#endif
