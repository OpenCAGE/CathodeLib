using CATHODE;
using CATHODE.ShaderTypes;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.Linq;

#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib.Ubershaders
{
    public enum PermutationSource
    {
        None,        //combination is not obtainable
        LevelPool,   //an entry with this mask already exists in the level's shader pak
        Database,    //this entry has a shader patch in cathodelib
        Recompiled,  //this entry requires a full patch from cathodelib
        Unimplemented, //this type doesn't exist in retail data
        Relabelled,  //the requested mask compiles to the bytecode the material already has - same blob, new mask
    }

    /* Resolves a (family, feature mask) request to a concrete shader entry for a level, trying the
     * cheapest source first: the level's own pool, then the database, then a fresh compile.
     *
     * A rebind keeps the material's texture assignments: sampler remap values index into the
     * material's own TextureReferences, so they are carried from the old entry rather than taken
     * from whatever material the database entry shipped with. */
    public static class ShaderPermutationService
    {
        private static IUbershaderCatalogue _database;
        private static string _databaseRoot;
        private static bool _databaseResolved;

        /// <summary>
        /// Set by the host to supply the permutation tier. Left null - as it is during
        /// instancing - the service resolves from the level pool and the database only.
        /// </summary>
        public static Func<string, IUbershaderCatalogue> CatalogueProvider;

        public static IUbershaderCatalogue Database(string gameRoot)
        {
            if (!_databaseResolved || _databaseRoot != gameRoot)
            {
                _database = CatalogueProvider == null ? null : CatalogueProvider(gameRoot);
                _databaseResolved = true;
                _databaseRoot = gameRoot;
            }
            return _database;
        }

        /// <summary>
        /// Call after the host rebuilds its catalogue so the next query sees the new data.
        /// </summary>
        public static void InvalidateDatabase()
        {
            _database = null;
            _databaseRoot = null;
            _databaseResolved = false;
        }

        /// <summary>
        /// For each feature bit, where would toggling it from the current mask get its shader?
        /// </summary>
        public static Dictionary<int, PermutationSource> AvailabilityForToggles(Shaders levelShaders, SHADER_LIST family, long currentMask, string gameRoot, IEnumerable<int> featureBits)
        {
            HashSet<long> levelMasks = LevelMasks(levelShaders, family);
            IUbershaderCatalogue db = Database(gameRoot);
            HashSet<long> dbMasks = db != null ? db.FamilyMasks(family) : new HashSet<long>();
            bool canCompile = UberShaderRecompiler.CanCompile(family);

            /* Which states of each bit any shipped shader has ever been seen in. A feature no
             * shipped permutation ever turns on had no blob to decode, so the database carries no
             * implementation of it - and the same goes the other way for one that is always on. */
            long everSet = 0, everClear = 0;
            foreach (long m in levelMasks) { everSet |= m; everClear |= ~m; }
            foreach (long m in dbMasks) { everSet |= m; everClear |= ~m; }

            Dictionary<int, PermutationSource> result = new Dictionary<int, PermutationSource>();
            foreach (int bit in featureBits)
            {
                long b = 1L << bit;
                long toggled = currentMask ^ b;
                if (levelMasks.Contains(toggled)) result[bit] = PermutationSource.LevelPool;
                else if (dbMasks.Contains(toggled)) result[bit] = PermutationSource.Database;
                else if (!canCompile) result[bit] = PermutationSource.None;
                /* Nothing shipped in the state being asked for, and the database doesn't react to the
                 * bit either: there is no rendering behind this checkbox to give them. Features
                 * that only drive render state (LOD bias, double-sided, the particle sim flags)
                 * compile to the same blob in retail too, so they pass on the first test. */
                else if (((((toggled & b) != 0) ? everSet : everClear) & b) == 0
                         && !UberShaderRecompiler.ToggleAffectsMaster(family, currentMask, bit))
                    result[bit] = PermutationSource.Unimplemented;
                else result[bit] = PermutationSource.Recompiled;
            }
            return result;
        }

        /// <summary>
        /// True when arbitrary feature combinations can be built for this family - i.e. we ship 
        /// info for it and a compiler is present. When false the only permutations
        /// obtainable are the ones already shipped, so an editor should offer a pick-from-list
        /// instead of free checkboxes.
        /// </summary>
        /// <remarks>
        /// Deliberately derived rather than listed: the set of families the shipped table carries
        /// is the single source of truth, so adding or removing an entry changes what the UI allows
        /// without any second list needing to be kept in step.
        /// </remarks>
        public static bool CanBuildArbitraryPermutations(SHADER_LIST family)
        {
            return UberShaderRecompiler.CanCompile(family);
        }

        /// <summary>One feature combination a material of this family could be bound to.</summary>
        public class Permutation
        {
            public long Mask;
            public PermutationSource Source;   //LevelPool or Database
            public int MaterialUses;           //materials in THIS level already on this mask
        }

        /// <summary>
        /// Every permutation of a family that can be bound without compiling anything: the masks
        /// this level already carries, plus everything the database holds. Ordered most
        /// useful first - the ones this level's own materials actually use, then the rest of the
        /// level's pool, then the database.
        /// </summary>
        public static List<Permutation> AvailablePermutations(Materials materials, Shaders levelShaders, SHADER_LIST family, string gameRoot)
        {
            Dictionary<long, Permutation> found = new Dictionary<long, Permutation>();

            foreach (long mask in LevelMasks(levelShaders, family))
                found[mask] = new Permutation { Mask = mask, Source = PermutationSource.LevelPool };

            IUbershaderCatalogue db = Database(gameRoot);
            if (db != null)
                foreach (long mask in db.FamilyMasks(family))
                    if (!found.ContainsKey(mask))
                        found[mask] = new Permutation { Mask = mask, Source = PermutationSource.Database };

            if (materials?.Entries != null)
            {
                foreach (Materials.Material material in materials.Entries)
                {
                    if (material?.Shader == null || material.Shader.Ubershader != family) continue;
                    Permutation p;
                    if (found.TryGetValue(material.Shader.UbershaderFeatureFlags, out p))
                        p.MaterialUses++;
                }
            }

            List<Permutation> result = new List<Permutation>(found.Values);
            result.Sort((a, b) =>
            {
                if (a.MaterialUses != b.MaterialUses) return b.MaterialUses.CompareTo(a.MaterialUses);
                if (a.Source != b.Source) return a.Source == PermutationSource.LevelPool ? -1 : 1;
                return a.Mask.CompareTo(b.Mask);
            });
            return result;
        }

        private static HashSet<long> LevelMasks(Shaders levelShaders, SHADER_LIST family)
        {
            HashSet<long> masks = new HashSet<long>();
            if (levelShaders != null)
            {
                foreach (Shaders.Shader shader in levelShaders.Entries)
                    if (shader != null && shader.Ubershader == family)
                        masks.Add(shader.UbershaderFeatureFlags);
            }
            return masks;
        }

        /* True when every bit that differs between the two masks is being moved into a state no
         * shipped shader of this family was ever seen in. */
        private static bool AllChangesUnobserved(Shaders levelShaders, SHADER_LIST family, string gameRoot, long from, long to)
        {
            long everSet = 0, everClear = 0;
            foreach (long m in LevelMasks(levelShaders, family)) { everSet |= m; everClear |= ~m; }
            IUbershaderCatalogue db = Database(gameRoot);
            if (db != null)
                foreach (long m in db.FamilyMasks(family)) { everSet |= m; everClear |= ~m; }

            long diff = from ^ to;
            for (int b = 0; b < 64; b++)
            {
                long bit = 1L << b;
                if ((diff & bit) == 0) continue;
                if (((((to & bit) != 0) ? everSet : everClear) & bit) != 0) return false;
            }
            return true;
        }

        /// <summary>
        /// Resolve a shader entry for the mask, adding it to the level's shader pool if it wasn't
        /// already there. Returns null with an error message when the combination is unobtainable.
        /// </summary>
        public static Shaders.Shader Resolve(Shaders levelShaders, SHADER_LIST family, long mask, Shaders.Shader current, string gameRoot, out PermutationSource source, out string error)
        {
            error = null;
            source = PermutationSource.None;
            List<int> carry = current != null ? new List<int>(current.SamplerRemaps) : new List<int>();

            //1. The level already ships this permutation
            Shaders.Shader levelMatch = FindInLevel(levelShaders, family, mask);
            if (levelMatch != null)
            {
                source = PermutationSource.LevelPool;
                List<int> resized = ResizeCarry(carry, levelMatch.SamplerRemaps.Count);
                if (ListsEqual(levelMatch.SamplerRemaps, resized))
                    return levelMatch;

                //Same permutation, but its sampler remaps describe another material's texture
                //layout - clone so the rebind doesn't stomp whatever uses the original
                Shaders.Shader clone = levelMatch.Copy();
                ShareBlobs(clone, levelMatch);
                clone.SamplerRemaps.Clear();
                clone.SamplerRemaps.AddRange(resized);
                levelShaders.Entries.Add(clone);
                return clone;
            }

            //2. In the database and data
            IUbershaderCatalogue db = Database(gameRoot);
            Shaders.Shader shader;
            if (db != null && db.TryGet(family, mask, out shader))
            {
                source = PermutationSource.Database;
                List<int> resized = ResizeCarry(carry, shader.SamplerRemaps.Count);
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(resized);
                DedupeBlobsAgainstLevel(levelShaders, shader);
                levelShaders.Entries.Add(shader);
                return shader;
            }

            //3. In the database and not in data
            if (!UberShaderRecompiler.CanCompile(family))
            {
                error = "This feature combination isn't in your game data and " + family + " can't be recompiled yet.";
                return null;
            }

            /* The bits that differ do not change the compiled shader, so a recompile would hand back the
             * bytecode the material already has. That is not a reason to refuse: the mask is data the
             * engine reads off the entry in its own right - LOW_RES, BILLBOARD, EARLY_ALPHA on a fog
             * plane are render-state flags, and retail ships them as masks with identical blobs. So
             * relabel: the current entry's bytecode and metadata under the requested mask. Refusing
             * here made instancing keep a fog volume's template material after its LOW_RES was ticked,
             * which silently dropped the setting (BSP_LV426_Pt01, CA_FOGPLANE 0xA2F). */
            if (current != null && !UberShaderRecompiler.MastersDiffer(family, current.UbershaderFeatureFlags, mask))
            {
                Shaders.Shader relabelled = current.Copy();
                ShareBlobs(relabelled, current);
                relabelled.UbershaderFeatureFlags = mask;
                relabelled.PermutationHash = SynthesizePermutationHash(family, mask);
                relabelled.SamplerRemaps.Clear();
                relabelled.SamplerRemaps.AddRange(ResizeCarry(carry, current.SamplerRemaps.Count));
                levelShaders.Entries.Add(relabelled);
                source = PermutationSource.Relabelled;
                return relabelled;
            }

            byte[] vertexShader, pixelShader, hullShader, domainShader;
            if (!UberShaderRecompiler.Compile(family, mask, out vertexShader, out pixelShader, out hullShader, out domainShader, out error))
                return null;

            Shaders.Shader donor = PickMetadataDonor(levelShaders, family, mask, gameRoot);
            if (donor == null) donor = current;
            if (donor == null)
            {
                error = "No donor entry available for " + family + " metadata.";
                return null;
            }

            Shaders.Shader compiled = donor.Copy();
            compiled.UbershaderFeatureFlags = mask;
            compiled.VertexShader = vertexShader;
            compiled.PixelShader = pixelShader;
            //Non-null only on a tessellated mask; the donor's stages are never carried, because a
            //hull or domain shader belongs to its own permutation's interpolant layout.
            compiled.HullShader = hullShader;
            compiled.DomainShader = domainShader;
            compiled.GeometryShader = null;
            compiled.ComputeShader = null;
            compiled.PermutationHash = SynthesizePermutationHash(family, mask);

            long modelledReq;
            if (TryModelRequirementFlags(levelShaders, family, mask, gameRoot, out modelledReq))
                compiled.UbershaderRequirementFlags = modelledReq;

            UberShaderRecompiler.SynthesizeRemaps(family, mask, compiled);

            List<int> carryResized = ResizeCarry(carry, compiled.SamplerRemaps.Count);
            compiled.SamplerRemaps.Clear();
            compiled.SamplerRemaps.AddRange(carryResized);

            DedupeBlobsAgainstLevel(levelShaders, compiled);
            levelShaders.Entries.Add(compiled);
            source = PermutationSource.Recompiled;
            return compiled;
        }

        /// <summary>
        /// A shader family a brand new material can be built on, and where its shader would come from.
        /// </summary>
        public class Creatable
        {
            public SHADER_LIST Family;
            public int Permutations;   //how many distinct permutations we could draw on
            public bool InLevel;       //the level already carries shaders of this family
        }

        /// <summary>
        /// The families a new material can be given a working shader for: the ones this level already
        /// carries, plus everything the database holds. Reads the database index only, so no family
        /// file is loaded until one is actually picked.
        /// </summary>
        public static List<Creatable> CreatableFamilies(Shaders levelShaders, string gameRoot)
        {
            Dictionary<SHADER_LIST, Creatable> found = new Dictionary<SHADER_LIST, Creatable>();
            if (levelShaders != null)
            {
                Dictionary<SHADER_LIST, HashSet<long>> levelMasks = new Dictionary<SHADER_LIST, HashSet<long>>();
                foreach (Shaders.Shader shader in levelShaders.Entries)
                {
                    if (shader == null || shader.PixelShader == null) continue;
                    if (!levelMasks.ContainsKey(shader.Ubershader)) levelMasks[shader.Ubershader] = new HashSet<long>();
                    levelMasks[shader.Ubershader].Add(shader.UbershaderFeatureFlags);
                }
                foreach (KeyValuePair<SHADER_LIST, HashSet<long>> kv in levelMasks)
                    found[kv.Key] = new Creatable { Family = kv.Key, Permutations = kv.Value.Count, InLevel = true };
            }

            IUbershaderCatalogue catalogue = Database(gameRoot);
            foreach (KeyValuePair<SHADER_LIST, int> kv in catalogue == null
                     ? new KeyValuePair<SHADER_LIST, int>[0] : catalogue.Families())
            {
                Creatable existing;
                if (found.TryGetValue(kv.Key, out existing))
                    existing.Permutations = Math.Max(existing.Permutations, kv.Value);
                else
                    found[kv.Key] = new Creatable { Family = kv.Key, Permutations = kv.Value, InLevel = false };
            }

            /* A family only makes the list if we can actually pick a permutation to start it on.
             * Anything else would be offered and then refused at the point of creation, which is
             * worse than never showing it. */
            List<Creatable> result = new List<Creatable>();
            foreach (Creatable candidate in found.Values)
            {
                long startingMask;
                if (PickStartingMask(null, levelShaders, candidate.Family, gameRoot, out startingMask))
                    result.Add(candidate);
            }
            result.Sort((a, b) => string.Compare(a.Family.ToString(), b.Family.ToString(), StringComparison.Ordinal));
            return result;
        }

        /* Where a new material of this family should start. Every shipped shader entry carries its
         * own mask - 617 entries, 617 masks for CA_ENVIRONMENT in one level - so the entries say
         * nothing about which permutation is typical. The materials do: counting how many of them
         * use each mask picks out the shader the level is actually built from, which is the most
         * useful thing to hand someone. A family no material here uses falls back to the plainest
         * permutation available, which is the one with the fewest features to unpick. */
        private static bool PickStartingMask(Materials materials, Shaders levelShaders, SHADER_LIST family, string gameRoot, out long mask)
        {
            mask = 0;
            Dictionary<long, int> uses = new Dictionary<long, int>();
            if (materials != null)
            {
                foreach (Materials.Material material in materials.Entries)
                {
                    if (material == null || material.Shader == null || material.Shader.Ubershader != family) continue;
                    long m = material.Shader.UbershaderFeatureFlags;
                    uses[m] = uses.ContainsKey(m) ? uses[m] + 1 : 1;
                }
            }
            if (uses.Count != 0)
            {
                bool first = true;
                int bestUses = 0;
                foreach (KeyValuePair<long, int> kv in uses)
                {
                    if (first || kv.Value > bestUses || (kv.Value == bestUses && kv.Key < mask))
                    {
                        mask = kv.Key;
                        bestUses = kv.Value;
                        first = false;
                    }
                }
                return true;
            }

            List<long> candidates = new List<long>();
            if (levelShaders != null)
            {
                foreach (Shaders.Shader shader in levelShaders.Entries)
                    if (shader != null && shader.Ubershader == family && shader.PixelShader != null)
                        candidates.Add(shader.UbershaderFeatureFlags);
            }
            if (candidates.Count == 0)
            {
                IUbershaderCatalogue db = Database(gameRoot);
                if (db != null)
                    candidates.AddRange(db.FamilyMasks(family));
            }

            bool any = false;
            int bestBits = 0;
            foreach (long m in candidates)
            {
                int bits = PopCount(m);
                if (!any || bits < bestBits || (bits == bestBits && m < mask))
                {
                    mask = m;
                    bestBits = bits;
                    any = true;
                }
            }
            return any;
        }

        private static int PopCount(long value)
        {
            int count = 0;
            ulong v = (ulong)value;
            while (v != 0) { count += (int)(v & 1); v >>= 1; }
            return count;
        }

        /// <summary>
        /// Build a brand new material on the given shader family, added to the level's material list.
        /// It starts with no textures and zeroed shader constants - samplers, features and parameters
        /// are all filled in afterwards through the editor.
        /// </summary>
        /// <param name="permutation">
        /// The feature combination to build on, when the caller already knows which one it wants (the
        /// material generator works one out from an imported model's texture slots). Left null, the
        /// family's most-used combination in this level is used. Going through here rather than
        /// rebinding afterwards matters: the entry is cloned before the caller can touch it, and a
        /// rebind that landed on a shared pool entry would put this material's textures on every other
        /// material using it.
        /// </param>
        public static Materials.Material CreateMaterial(Materials materials, Shaders levelShaders, SHADER_LIST family, string name, string gameRoot, out string error, long? permutation = null)
        {
            error = null;
            if (materials == null || levelShaders == null)
            {
                error = "This level has no material list loaded.";
                return null;
            }

            long mask;
            if (permutation != null)
            {
                mask = permutation.Value;
            }
            else if (!PickStartingMask(materials, levelShaders, family, gameRoot, out mask))
            {
                error = "No shipped " + family + " shader could be found to start from.";
                return null;
            }

            PermutationSource source;
            Shaders.Shader resolved = Resolve(levelShaders, family, mask, null, gameRoot, out source, out error);
            if (resolved == null)
                return null;

            /* The new material gets its own entry even when an identical one exists: sampler remaps
             * live on the shader, so sharing would mean assigning a texture here changed it there. */
            Shaders.Shader shader = resolved.Copy();
            ShareBlobs(shader, resolved);
            shader.SamplerRemaps.Clear();
            shader.SamplerRemaps.AddRange(ResizeCarry(new List<int>(), resolved.SamplerRemaps.Count));
            levelShaders.Entries.Add(shader);

            /* Engine, hull and domain constants are not ubershader-parameter driven, so they are
             * taken from a material that already works - one of the same family where there is one. */
            Materials.Material donor = null;
            foreach (Materials.Material candidate in materials.Entries)
            {
                if (candidate == null || candidate.Shader == null) continue;
                if (donor == null) donor = candidate;
                if (candidate.Shader.Ubershader == family) { donor = candidate; break; }
            }

            Materials.Material material = new Materials.Material
            {
                Name = name,
                Shader = shader,
                EngineConstants = donor != null ? new List<float>(donor.EngineConstants) : new List<float>(),
                HullShaderConstants = donor != null ? new List<float>(donor.HullShaderConstants) : new List<float>(),
                DomainShaderConstants = donor != null ? new List<float>(donor.DomainShaderConstants) : new List<float>(),
                VertexShaderConstants = ZeroedConstants(family, shader.VertexShaderParameterRemaps),
                PixelShaderConstants = ZeroedConstants(family, shader.PixelShaderParameterRemaps),
                PhysicalMaterialIndex = donor != null ? donor.PhysicalMaterialIndex : 255,
                EnvironmentMapIndex = 255,
                Priority = donor != null ? donor.Priority : 0
            };

            materials.Entries.Add(material);
            return material;
        }

        /* A constant block big enough for every parameter the permutation includes, all zero. */
        private static List<float> ZeroedConstants(SHADER_LIST family, List<int> remaps)
        {
            int length = 0;
            for (int id = 0; id < remaps.Count; id++)
            {
                if (remaps[id] == 255) continue;
                int end = remaps[id] + UberShaderRecompiler.ParamWidth(family, id);
                if (end > length) length = end;
            }
            return new List<float>(new float[RegisterAligned(length)]);
        }

        /// <summary>
        /// Round a constant count up to a whole float4 register. The widest parameter in use decides
        /// where the block ends, so a material whose last parameter is a scalar comes out short of a
        /// register - and retail never ships one that way: of 55,336 materials across six pristine
        /// levels, 55,335 have both their vertex and pixel constant counts as multiples of four.
        /// </summary>
        private static int RegisterAligned(int length)
        {
            return (length + 3) / 4 * 4;
        }

        /// <summary>
        /// Rebuild the material's per-stage constants for a new shader entry: values of parameters
        /// present in both keep their slots' contents; newly enabled parameters start at zero.
        /// </summary>
        public static void MigrateConstants(Materials.Material material, Shaders.Shader oldShader, Shaders.Shader newShader)
        {
            SHADER_LIST family = newShader.Ubershader;
            material.VertexShaderConstants = MigrateStage(family, oldShader.VertexShaderParameterRemaps, newShader.VertexShaderParameterRemaps, material.VertexShaderConstants);
            material.PixelShaderConstants = MigrateStage(family, oldShader.PixelShaderParameterRemaps, newShader.PixelShaderParameterRemaps, material.PixelShaderConstants);
            //Engine/hull/domain constants are not ubershader-parameter driven; leave them alone
        }

        private static List<float> MigrateStage(SHADER_LIST family, List<int> oldRemaps, List<int> newRemaps, List<float> oldValues)
        {
            if (ListsEqual(oldRemaps, newRemaps))
                return oldValues;

            int newLength = 0;
            for (int id = 0; id < newRemaps.Count; id++)
            {
                if (newRemaps[id] == 255) continue;
                int end = newRemaps[id] + UberShaderRecompiler.ParamWidth(family, id);
                if (end > newLength) newLength = end;
            }
            newLength = RegisterAligned(newLength);

            List<float> result = new List<float>(new float[newLength]);
            for (int id = 0; id < newRemaps.Count; id++)
            {
                int newSlot = newRemaps[id];
                int oldSlot = id < oldRemaps.Count ? oldRemaps[id] : 255;
                if (newSlot == 255 || oldSlot == 255) continue;
                int width = UberShaderRecompiler.ParamWidth(family, id);
                for (int k = 0; k < width; k++)
                {
                    if (oldSlot + k < oldValues.Count && newSlot + k < newLength)
                        result[newSlot + k] = oldValues[oldSlot + k];
                }
            }
            return result;
        }

        #region DONOR_SELECTION
        /* Metadata for a compiled permutation (render states, sampler blocks, technique) comes from
         * the shipped mask that agrees best with the target: differing on a bit that's ever been
         * seen to change metadata costs 100, a pure-code bit costs 1. Learned per family from
         * one-bit-apart shipped pairs; unobserved bits count as metadata-affecting. */
        private class FamilyKnowledge
        {
            public List<KeyValuePair<long, Shaders.Shader>> Candidates;
            public long MetadataAffectingBits;
            public long ObservedPairBits;
        }

        private static readonly Dictionary<string, FamilyKnowledge> _knowledgeCache = new Dictionary<string, FamilyKnowledge>();

        private static FamilyKnowledge Knowledge(Shaders levelShaders, SHADER_LIST family, string gameRoot)
        {
            //Level entries can change between calls (we add clones); key the cache on the mask set
            List<KeyValuePair<long, Shaders.Shader>> candidates = GatherCandidates(levelShaders, family, gameRoot);
            string key = family + ":" + string.Join(",", candidates.Select(o => o.Key).Distinct().OrderBy(o => o));
            FamilyKnowledge knowledge;
            if (_knowledgeCache.TryGetValue(key, out knowledge))
                return knowledge;

            knowledge = new FamilyKnowledge() { Candidates = candidates };

            //Dedupe to one representative per mask
            Dictionary<long, Shaders.Shader> byMask = new Dictionary<long, Shaders.Shader>();
            foreach (KeyValuePair<long, Shaders.Shader> candidate in candidates)
                if (!byMask.ContainsKey(candidate.Key))
                    byMask[candidate.Key] = candidate.Value;

            List<long> masks = byMask.Keys.ToList();
            for (int i = 0; i < masks.Count; i++)
            {
                for (int j = i + 1; j < masks.Count; j++)
                {
                    long xor = masks[i] ^ masks[j];
                    if ((xor & (xor - 1)) != 0) continue; //not exactly one bit apart
                    int bit = 0;
                    while ((xor >> bit) != 1) bit++;
                    knowledge.ObservedPairBits |= 1L << bit;
                    if (!MetadataEqual(byMask[masks[i]], byMask[masks[j]]))
                        knowledge.MetadataAffectingBits |= 1L << bit;
                }
            }

            _knowledgeCache[key] = knowledge;
            return knowledge;
        }

        private static List<KeyValuePair<long, Shaders.Shader>> GatherCandidates(Shaders levelShaders, SHADER_LIST family, string gameRoot)
        {
            List<KeyValuePair<long, Shaders.Shader>> candidates = new List<KeyValuePair<long, Shaders.Shader>>();
            if (levelShaders != null)
            {
                foreach (Shaders.Shader shader in levelShaders.Entries)
                    if (shader != null && shader.Ubershader == family)
                        candidates.Add(new KeyValuePair<long, Shaders.Shader>(shader.UbershaderFeatureFlags, shader));
            }
            IUbershaderCatalogue db = Database(gameRoot);
            if (db != null)
            {
                foreach (KeyValuePair<long, Shaders.Shader> entry in db.Entries(family))
                    candidates.Add(entry);
            }
            return candidates;
        }

        private static Shaders.Shader PickMetadataDonor(Shaders levelShaders, SHADER_LIST family, long mask, string gameRoot)
        {
            FamilyKnowledge knowledge = Knowledge(levelShaders, family, gameRoot);
            Shaders.Shader best = null;
            long bestScore = long.MaxValue;
            foreach (KeyValuePair<long, Shaders.Shader> candidate in knowledge.Candidates)
            {
                long differing = candidate.Key ^ mask;
                long score = 0;
                for (int bit = 0; bit < 64 && (differing >> bit) != 0; bit++)
                {
                    if (((differing >> bit) & 1) == 0) continue;
                    bool affecting = ((knowledge.MetadataAffectingBits >> bit) & 1) != 0 || ((knowledge.ObservedPairBits >> bit) & 1) == 0;
                    score += affecting ? 100 : 1;
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate.Value;
                }
            }
            return best;
        }

        private static bool MetadataEqual(Shaders.Shader a, Shaders.Shader b)
        {
            if (!a.RenderStates.Equals(b.RenderStates)) return false;
            if (a.Samplers.Count != b.Samplers.Count) return false;
            for (int i = 0; i < a.Samplers.Count; i++)
                if (!a.Samplers[i].Equals(b.Samplers[i])) return false;
            if (!ListsEqual(a.SamplerStageBindings, b.SamplerStageBindings)) return false;
            if (a.SamplerRemaps.Count != b.SamplerRemaps.Count) return false; //values are per-material
            if (!ListsEqual(a.EngineParameterRemaps, b.EngineParameterRemaps)) return false;
            if (!ListsEqual(a.VertexShaderParameterRemaps, b.VertexShaderParameterRemaps)) return false;
            if (!ListsEqual(a.PixelShaderParameterRemaps, b.PixelShaderParameterRemaps)) return false;
            if (!ListsEqual(a.HullShaderParameterRemaps, b.HullShaderParameterRemaps)) return false;
            if (!ListsEqual(a.DomainShaderParameterRemaps, b.DomainShaderParameterRemaps)) return false;
            return true;
        }
        #endregion

        #region REQUIREMENT_FLAGS
        /* Requirement flags are a pure function of the mask (measured game-wide). For non-existant
         * masks, fit an OR-decomposition over the shipped data - base | OR(per-bit contribution) -
         * and only trust it if it reproduces every shipped mask exactly and every bit of the
         * target mask has been observed. Otherwise the donor's flags stand. */
        private static bool TryModelRequirementFlags(Shaders levelShaders, SHADER_LIST family, long mask, string gameRoot, out long requirementFlags)
        {
            requirementFlags = 0;
            FamilyKnowledge knowledge = Knowledge(levelShaders, family, gameRoot);

            Dictionary<long, long> reqByMask = new Dictionary<long, long>();
            foreach (KeyValuePair<long, Shaders.Shader> candidate in knowledge.Candidates)
                if (!reqByMask.ContainsKey(candidate.Key))
                    reqByMask[candidate.Key] = candidate.Value.UbershaderRequirementFlags;
            if (reqByMask.Count == 0)
                return false;

            long baseReq = -1;
            long observedBits = 0;
            Dictionary<int, long> contrib = new Dictionary<int, long>();
            foreach (KeyValuePair<long, long> pair in reqByMask)
            {
                baseReq &= pair.Value;
                for (int bit = 0; bit < 64 && (pair.Key >> bit) != 0; bit++)
                {
                    if (((pair.Key >> bit) & 1) == 0) continue;
                    observedBits |= 1L << bit;
                    long existing;
                    contrib[bit] = contrib.TryGetValue(bit, out existing) ? (existing & pair.Value) : pair.Value;
                }
            }

            long Predict(long m)
            {
                long req = baseReq;
                for (int bit = 0; bit < 64 && (m >> bit) != 0; bit++)
                    if (((m >> bit) & 1) != 0 && contrib.ContainsKey(bit))
                        req |= contrib[bit];
                return req;
            }

            foreach (KeyValuePair<long, long> pair in reqByMask)
                if (Predict(pair.Key) != pair.Value)
                    return false;
            if ((mask & ~observedBits) != 0)
                return false;

            requirementFlags = Predict(mask);
            return true;
        }
        #endregion

        #region HELPERS
        private static Shaders.Shader FindInLevel(Shaders levelShaders, SHADER_LIST family, long mask)
        {
            if (levelShaders == null)
                return null;
            foreach (Shaders.Shader shader in levelShaders.Entries)
                if (shader != null && shader.Ubershader == family && shader.UbershaderFeatureFlags == mask)
                    return shader;
            return null;
        }

        /* The carried sampler assignment, resized to the target entry's remap-table length */
        private static List<int> ResizeCarry(List<int> carry, int targetCount)
        {
            List<int> result = new List<int>();
            for (int i = 0; i < targetCount; i++)
                result.Add(i < carry.Count ? carry[i] : 255);
            return result;
        }

        /* Deep copies waste pool space: bytecode identical to something already in the level
         * should be the same array reference, so the save's first-reference dedupe collapses it */
        private static void ShareBlobs(Shaders.Shader target, Shaders.Shader original)
        {
            target.VertexShader = original.VertexShader;
            target.PixelShader = original.PixelShader;
            target.HullShader = original.HullShader;
            target.DomainShader = original.DomainShader;
            target.GeometryShader = original.GeometryShader;
            target.ComputeShader = original.ComputeShader;
        }

        private static void DedupeBlobsAgainstLevel(Shaders levelShaders, Shaders.Shader shader)
        {
            if (levelShaders == null)
                return;
            Dictionary<string, byte[]> byHash = new Dictionary<string, byte[]>();
            foreach (Shaders.Shader existing in levelShaders.Entries)
            {
                if (existing == null) continue;
                AddBlobHash(byHash, existing.VertexShader);
                AddBlobHash(byHash, existing.PixelShader);
                AddBlobHash(byHash, existing.HullShader);
                AddBlobHash(byHash, existing.DomainShader);
                AddBlobHash(byHash, existing.GeometryShader);
                AddBlobHash(byHash, existing.ComputeShader);
            }
            shader.VertexShader = DedupeBlob(byHash, shader.VertexShader);
            shader.PixelShader = DedupeBlob(byHash, shader.PixelShader);
            shader.HullShader = DedupeBlob(byHash, shader.HullShader);
            shader.DomainShader = DedupeBlob(byHash, shader.DomainShader);
            shader.GeometryShader = DedupeBlob(byHash, shader.GeometryShader);
            shader.ComputeShader = DedupeBlob(byHash, shader.ComputeShader);
        }

        /// <summary>Content key for blob de-duplication - never persisted, so any stable hash does.</summary>
        private static string BlobKey(byte[] blob)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(blob)).Replace("-", "");
        }

        private static void AddBlobHash(Dictionary<string, byte[]> byHash, byte[] blob)
        {
            if (blob == null) return;
            string key = BlobKey(blob);
            if (!byHash.ContainsKey(key))
                byHash[key] = blob;
        }

        private static byte[] DedupeBlob(Dictionary<string, byte[]> byHash, byte[] blob)
        {
            if (blob == null) return null;
            string key = BlobKey(blob);
            byte[] existing;
            return byHash.TryGetValue(key, out existing) ? existing : blob;
        }

        private static uint SynthesizePermutationHash(SHADER_LIST family, long mask)
        {
            //FNV-1a over (family, mask): stable, and never collides with another of our synthesized
            //entries; retail hashes are opaque so a fresh unique value is the safe choice
            uint hash = 2166136261;
            void Mix(byte b) { hash = (hash ^ b) * 16777619; }
            Mix((byte)((int)family & 0xFF));
            Mix((byte)(((int)family >> 8) & 0xFF));
            for (int i = 0; i < 8; i++)
                Mix((byte)((mask >> (i * 8)) & 0xFF));
            return hash;
        }

        private static bool ListsEqual(List<int> a, List<int> b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
        #endregion
    }
}
#endif
