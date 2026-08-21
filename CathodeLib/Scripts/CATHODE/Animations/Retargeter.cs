using CATHODE;
using System;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#elif GODOT
using Godot;
using System.Numerics;
using Quaternion = System.Numerics.Quaternion;
using Vector3 = Godot.Vector3;
#else
using System.Numerics;
#endif

namespace CathodeLib
{
    /// <summary>
    /// Plays a clip authored for one skeleton on another one.
    ///
    /// Almost nothing in the game is animated on the rig it ends up on. A character's clips are
    /// authored on a shared reference rig - MALE, FEMALE, FEMALENPC - and the engine moves them onto
    /// the character's own rig at runtime. TAYLOR is 155 bones where MALE is 72, so playing a MALE
    /// clip on TAYLOR bone-for-bone puts an elbow's rotation on a finger; the result is the mesh
    /// tearing itself apart.
    ///
    /// The data to do it properly is in the PAK, under SKELE/MAPS: for each pair of rigs, which bone
    /// answers to which, and how far the two rigs disagree about where that bone rests.
    /// </summary>
    public class Retargeter
    {
        /// <summary>The rig the clip was authored on.</summary>
        public Skeleton From { get; private set; }

        /// <summary>The rig it is being played on. For a route through a shared rig, the last one.</summary>
        public Skeleton To { get { return _next == null ? _to : _next.To; } }

        /// <summary>The rig this hop alone produces.</summary>
        private readonly Skeleton _to;

        /// <summary>Source bone, target bone, and the difference between their rest poses.</summary>
        public List<HavokPackfile.BoneMapping> Pairs { get; private set; }

        /// <summary>The rigs this went through, for anything that wants to explain itself.</summary>
        public List<string> Route { get; private set; }

        private readonly Retargeter _next;

        private Retargeter(Skeleton from, Skeleton to, List<HavokPackfile.BoneMapping> pairs, Retargeter next, List<string> route)
        {
            From = from;
            _to = to;
            Pairs = pairs;
            _next = next;
            Route = route;
        }

        /// <summary>
        /// The pairs of the last hop, whose target indices are bones of <see cref="To"/>. Anything
        /// asking which of the target.s bones end up driven wants these, not <see cref="Pairs"/>,
        /// which for a route through a shared rig indexes the rig in the middle.
        /// </summary>
        public List<HavokPackfile.BoneMapping> TargetPairs { get { return _next == null ? Pairs : _next.TargetPairs; } }

        /// <summary>How many of the target.s bones the mapping actually drives.</summary>
        public int MappedBones { get { return TargetPairs.Count; } }

        public override string ToString() { return string.Join(" -> ", Route); }

        /// <summary>
        /// Move one frame of a pose from the source rig onto the target.
        ///
        /// Only rotation comes across. The target keeps its own bone offsets, which is what stops a
        /// tall character's limbs being squashed to a short one's proportions - and means a bone can
        /// never change length, whatever the clip does. The exception is the root, which is where a
        /// clip carries the character about; that travel comes through, scaled by how much bigger
        /// the target is.
        /// </summary>
        public List<HavokPackfile.SampledTransform> Apply(List<HavokPackfile.SampledTransform> pose)
        {
            if (pose == null) return null;

            List<HavokPackfile.SampledTransform> result = new List<HavokPackfile.SampledTransform>(_to.Bones.Count);
            for (int i = 0; i < _to.Bones.Count; i++)
                result.Add(new HavokPackfile.SampledTransform
                {
                    Translation = _to.Bones[i].Position,
                    Rotation = _to.Bones[i].Rotation,
                    Scale = _to.Bones[i].ScaleXYZ,
                    HasTranslation = true,
                    HasRotation = true,
                    HasScale = true,
                });

            foreach (HavokPackfile.BoneMapping pair in Pairs)
            {
                int source = pair.BoneA, target = pair.BoneB;
                if (source < 0 || source >= pose.Count || target < 0 || target >= result.Count) continue;

                Vector3 travel = Vector3.Zero;
                if (_to.Bones[target].ParentIndex < 0 && source < From.Bones.Count)
                    travel = (pose[source].Translation - From.Bones[source].Position) * pair.Scale;

                result[target] = new HavokPackfile.SampledTransform
                {
                    Translation = _to.Bones[target].Position + travel,
                    Rotation = pair.Rotation * pose[source].Rotation,
                    Scale = _to.Bones[target].ScaleXYZ,
                    HasTranslation = true,
                    HasRotation = true,
                    HasScale = true,
                };
            }

            //a hop through a shared reference rig is just two of these back to back
            return _next == null ? result : _next.Apply(result);
        }

        #region BUILDING
        /// <summary>
        /// Work out how to get a clip from one rig to another, or null if it can't be done - or
        /// doesn't need to be, because they're the same rig.
        /// </summary>
        public static Retargeter Between(Animation animations, string from, string to)
        {
            if (animations == null || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return null;
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return null;

            Skeleton source = Rig(animations, from), target = Rig(animations, to);
            if (source == null || target == null) return null;

            Retargeter direct = Hop(animations, source, target, null, new List<string> { from, to });
            if (direct != null) return direct;

            /* No direct mapping. The rigs a character borrows from all reach its lo-res reference
             * skeleton, so go through that - it's the hub the whole scheme is built around. */
            string reference = animations.SkeletonDefs.TryGetValue(to, out Animation.SkeletonDef def) ? def.ReferenceSkeleton : null;
            if (!string.IsNullOrEmpty(reference) && !string.Equals(reference, from, StringComparison.OrdinalIgnoreCase))
            {
                Skeleton middle = Rig(animations, reference);
                if (middle != null)
                {
                    Retargeter second = Hop(animations, middle, target, null, new List<string> { reference, to });
                    if (second != null)
                    {
                        Retargeter through = Hop(animations, source, middle, second, new List<string> { from, reference, to });
                        if (through != null) return through;
                    }
                }
            }

            /* Still nothing. The mappings form a graph and a route can perfectly well exist that
             * neither of those two shapes covers - through the SOURCE's reference skeleton, or
             * through a shared rig like MALE. FEMALEFP -> CAT is one: no direct mapping and CAT's
             * reference doesn't help, but FEMALEFP -> MALE -> CAT is right there. Following the
             * graph properly picks up 18 more rig pairs, 97 clips. */
            return Along(animations, ShortestRoute(animations, from, to));
        }

        /* Chain one hop per step of the route, built from the far end back, so each retargeter hands
         * its result to the next. Null if any step turns out not to be readable after all. */
        private static Retargeter Along(Animation animations, List<string> route)
        {
            if (route == null || route.Count < 2) return null;

            Retargeter chain = null;
            for (int i = route.Count - 2; i >= 0; i--)
            {
                Skeleton a = Rig(animations, route[i]), b = Rig(animations, route[i + 1]);
                if (a == null || b == null) return null;

                chain = Hop(animations, a, b, chain, route.GetRange(i, route.Count - i));
                if (chain == null) return null;
            }
            return chain;
        }

        /* Fewest mappings that get from one rig to the other, ends included, or null. A mapping
         * reads in either direction, so the graph is undirected. */
        private static List<string> ShortestRoute(Animation animations, string from, string to)
        {
            Dictionary<string, List<string>> edges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (Animation.MappingAsset mapping in animations.Mappings)
            {
                Link(edges, mapping.SkeletonA, mapping.SkeletonB);
                Link(edges, mapping.SkeletonB, mapping.SkeletonA);
            }
            if (!edges.ContainsKey(from) || !edges.ContainsKey(to)) return null;

            Dictionary<string, string> cameFrom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { from, null } };
            Queue<string> queue = new Queue<string>();
            queue.Enqueue(from);

            while (queue.Count != 0)
            {
                string at = queue.Dequeue();
                if (string.Equals(at, to, StringComparison.OrdinalIgnoreCase))
                {
                    List<string> route = new List<string>();
                    for (string step = to; step != null; step = cameFrom[step]) route.Add(step);
                    route.Reverse();
                    return route;
                }
                foreach (string next in edges[at])
                {
                    if (cameFrom.ContainsKey(next)) continue;
                    cameFrom[next] = at;
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        private static void Link(Dictionary<string, List<string>> edges, string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return;
            if (!edges.TryGetValue(a, out List<string> list)) edges[a] = list = new List<string>();
            if (!list.Contains(b, StringComparer.OrdinalIgnoreCase)) list.Add(b);
        }

        private static Skeleton Rig(Animation animations, string name)
        {
            Animation.SkeletonAsset asset = animations.GetSkeleton(name);
            return asset?.Skeleton ?? asset?.Skeleton64;
        }

        /* One mapping file, read in whichever direction gets us from source to target. */
        private static Retargeter Hop(Animation animations, Skeleton source, Skeleton target, Retargeter next, List<string> route)
        {
            Animation.MappingAsset asset = animations.GetMapping(source.Name, target.Name)
                                        ?? animations.GetMapping(target.Name, source.Name);
            SkeletonMapping mapping = asset?.Mapping ?? asset?.Mapping64;
            if (mapping == null) return null;

            List<HavokPackfile.BoneMapping> pairs = Orient(mapping, source, target);
            return pairs == null ? null : new Retargeter(source, target, pairs, next, route);
        }

        /// <summary>
        /// Turn a mapping file's bone pairs into source-to-target order.
        ///
        /// A file holds one mapper per direction, and which rig each index belongs to isn't written
        /// down - so it's worked out from the shape: the mapper that produces a rig lists that rig's
        /// spare bones as unmapped, and its "B" side indexes it.
        /// </summary>
        private static List<HavokPackfile.BoneMapping> Orient(SkeletonMapping mapping, Skeleton source, Skeleton target)
        {
            List<HavokPackfile.SkeletonMapper> mappers = mapping.GetMappers();

            foreach (HavokPackfile.SkeletonMapper mapper in mappers)
            {
                if (mapper.Mappings.Count == 0) continue;
                if (!Fits(mapper, source, target, false)) continue;
                return mapper.Mappings;
            }

            //the mapper we want isn't there in that orientation, so invert the one that is
            foreach (HavokPackfile.SkeletonMapper mapper in mappers)
            {
                if (mapper.Mappings.Count == 0) continue;
                if (!Fits(mapper, source, target, true)) continue;
                return mapper.Mappings.Select(Flip).ToList();
            }
            return null;
        }

        private static bool Fits(HavokPackfile.SkeletonMapper mapper, Skeleton source, Skeleton target, bool swapped)
        {
            foreach (HavokPackfile.BoneMapping pair in mapper.Mappings)
            {
                int intoSource = swapped ? pair.BoneB : pair.BoneA;
                int intoTarget = swapped ? pair.BoneA : pair.BoneB;
                if (intoSource < 0 || intoSource >= source.Bones.Count) return false;
                if (intoTarget < 0 || intoTarget >= target.Bones.Count) return false;
            }

            /* Both rigs can be big enough for both readings, so prefer the one whose unmapped list
             * accounts for the target's spare bones - that's the mapper built to produce it. */
            int spare = target.Bones.Count - mapper.Mappings.Count;
            return swapped ? mapper.UnmappedBones.Count != spare || source.Bones.Count == target.Bones.Count
                           : mapper.UnmappedBones.Count == spare;
        }

        private static HavokPackfile.BoneMapping Flip(HavokPackfile.BoneMapping pair)
        {
            Quaternion rotation = Quaternion.Inverse(pair.Rotation);
            Vector3 scale = new Vector3(
                pair.Scale.X == 0 ? 1 : 1 / pair.Scale.X,
                pair.Scale.Y == 0 ? 1 : 1 / pair.Scale.Y,
                pair.Scale.Z == 0 ? 1 : 1 / pair.Scale.Z);

            return new HavokPackfile.BoneMapping
            {
                BoneA = pair.BoneB,
                BoneB = pair.BoneA,
                Rotation = rotation,
                Scale = scale,
                Translation = -Vector3.Transform(pair.Translation, rotation) * scale,
            };
        }
        #endregion
    }
}
