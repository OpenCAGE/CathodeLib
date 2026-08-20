using CATHODE;
using CATHODE.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#elif GODOT
using Godot;
using System.Numerics;
using Matrix4x4 = System.Numerics.Matrix4x4;
#else
using System.Numerics;
#endif

namespace CathodeLib
{
    /// <summary>
    /// The other half of the animation system: clips played on static geometry rather than on a
    /// skinned character. Doors, levers, weapons and the like are built as a set of separate meshes
    /// plus a Havok skeleton with a bone for each, and the game moves each mesh rigidly with its
    /// bone.
    ///
    /// Each of those meshes is authored about its own origin and put in place by the entity that
    /// draws it, so a prop only looks like itself once every part has been moved to where the level
    /// says it goes. ENVIRONMENT_ANIMATION.DAT carries that placement per bone as an inverse bind
    /// pose, and names the part each bone drives by the id of its RENDERABLE_INSTANCE resource -
    /// which is what ties a bone to geometry, not the part's name.
    /// </summary>
    public static class EnvironmentRigs
    {
        /* Walking a level's composites isn't cheap and the answer never changes, so hold onto it
         * for as long as the level is alive and no longer. */
        private static readonly ConditionalWeakTable<Level, List<Prop>> _cache = new ConditionalWeakTable<Level, List<Prop>>();

        /// <summary>
        /// One piece of the world an environment animation drives: the entry that describes it, the
        /// composite that draws it, and where each of its parts sits.
        /// </summary>
        public class Prop
        {
            /// <summary>The level's record of this prop, naming the rig and holding its bind pose.</summary>
            public EnvironmentAnimations.EnvironmentAnimation Entry;

            /// <summary>The composite whose entities draw it.</summary>
            public Composite Composite;

            /// <summary>The rig that animates it.</summary>
            public string Rig { get { return Entry?.SkeletonName; } }

            /// <summary>Every piece of geometry the composite draws, in the order it draws them.</summary>
            public List<Part> Parts = new List<Part>();

            /// <summary>The meshes those parts belong to, the one contributing most parts first.</summary>
            public List<Models.CS2> Models = new List<Models.CS2>();

            /// <summary>How many parts the rig actually moves.</summary>
            public int Driven { get { return Parts.Count(x => x.Bone >= 0); } }

            public override string ToString() { return Rig + " (" + Driven + " of " + Parts.Count + " parts)"; }
        }

        /// <summary>One mesh of a prop, and the bone that moves it.</summary>
        public class Part
        {
            /// <summary>The geometry itself.</summary>
            public Models.CS2.Component.LOD.Submesh Submesh;

            /// <summary>Where the level puts it when nothing is playing.</summary>
            public Matrix4x4 Rest;

            /// <summary>The bone that moves it, or -1 for a part that stays put.</summary>
            public int Bone = -1;
        }

        /// <summary>Every prop this level animates.</summary>
        public static List<Prop> Props(Level level)
        {
            if (level?.Commands == null || level.Models == null || level.EnvironmentAnimations == null)
                return new List<Prop>();

            return _cache.GetValue(level, Build);
        }

        /// <summary>
        /// The prop a rig drives, or the one of them that draws the given mesh. Null if this level
        /// doesn't animate anything with that rig.
        /// </summary>
        public static Prop PropFor(Level level, string rigName, Models.CS2 model = null)
        {
            if (string.IsNullOrEmpty(rigName)) return null;

            Prop best = null;
            foreach (Prop prop in Props(level))
            {
                if (!string.Equals(prop.Rig, rigName, StringComparison.OrdinalIgnoreCase)) continue;
                if (model == null) { if (best == null || prop.Driven > best.Driven) best = prop; continue; }
                if (!Holds(prop.Models, model)) continue;
                if (best == null || prop.Driven > best.Driven) best = prop;
            }
            return best;
        }

        /// <summary>
        /// The meshes this level animates with a rig, the one whose parts it drives most first.
        /// </summary>
        public static List<Models.CS2> ModelsFor(Level level, string rigName, Skeleton rig = null)
        {
            List<Models.CS2> models = new List<Models.CS2>();
            if (string.IsNullOrEmpty(rigName)) return models;

            foreach (Prop prop in Props(level).Where(x => string.Equals(x.Rig, rigName, StringComparison.OrdinalIgnoreCase))
                                              .OrderByDescending(x => x.Driven))
                foreach (Models.CS2 model in prop.Models)
                    if (!Holds(models, model)) models.Add(model);
            return models;
        }

        /// <summary>Whether this level drives anything with the named rig.</summary>
        public static bool IsUsedBy(Level level, string rigName)
        {
            return PropFor(level, rigName) != null;
        }

        /// <summary>
        /// Where each bone of an environment rig puts the part hanging off it, at one frame of a
        /// clip. Pass a null clip for the rest pose.
        ///
        /// A part is stuck to its bone, so what carries it is the bone's rest placement followed by
        /// however far the clip has moved that bone since. The rest placement is the level's, not
        /// the rig's: the two nearly always agree, and where they don't it is the level that draws
        /// the prop.
        /// </summary>
        public static Matrix4x4[] Pose(Prop prop, Skeleton rig, Animation.ClipReference clip, int frame,
                                       Animation.RootMotion root = Animation.RootMotion.Ignore)
        {
            if (prop?.Entry == null || rig == null) return new Matrix4x4[0];

            Matrix4x4[] pose = new Matrix4x4[rig.Bones.Count];
            Matrix4x4[] offset = Offsets(prop, rig);

            List<Matrix4x4> animated = clip == null ? null : Animation.SampleRigPose(clip, rig, frame, root);
            if (animated == null) animated = rig.GetModelSpacePose();

            for (int i = 0; i < pose.Length; i++)
                pose[i] = i < animated.Count ? offset[i] * animated[i] : Matrix4x4.Identity;
            return pose;
        }

        /// <summary>
        /// How far each part sits from the bone that carries it - constant, whatever the clip does.
        /// A part modelled about its own origin needs this in front of the bone's transform before
        /// it lands where the prop wants it.
        /// </summary>
        public static Matrix4x4[] Offsets(Prop prop, Skeleton rig)
        {
            if (prop?.Entry == null || rig == null) return new Matrix4x4[0];

            List<Matrix4x4> rest = rig.GetModelSpacePose();
            Matrix4x4[] offset = new Matrix4x4[rig.Bones.Count];
            for (int i = 0; i < offset.Length; i++)
            {
                offset[i] = Matrix4x4.Identity;
                if (i >= prop.Entry.InverseBindPoses.Count) continue;
                if (!Matrix4x4.Invert(rest[i], out Matrix4x4 unposed)) continue;
                if (!Matrix4x4.Invert(prop.Entry.InverseBindPoses[i], out Matrix4x4 bind)) continue;
                offset[i] = bind * unposed;
            }
            return offset;
        }

        /// <summary>
        /// The bone that moves a part of a model, found by name, or -1 if the rig doesn't drive it.
        /// The last resort for a mesh the level has no record for - a bone and the part it drives
        /// usually, but not always, share a name.
        /// </summary>
        public static int BoneFor(Skeleton rig, string partName)
        {
            if (rig == null || string.IsNullOrEmpty(partName)) return -1;
            return rig.IndexOf(LastSegment(partName));
        }

        /// <summary>
        /// The bone that moves each part of a model, as [component][LOD], with -1 where the rig
        /// doesn't drive it. Only for a mesh outside the level's own records - see <see cref="Props"/>.
        ///
        /// Mostly this is <see cref="BoneFor"/> on the part's name, but some levels ship a model
        /// with its top LOD unnamed while the LODs below it keep theirs - the door frames in
        /// BSP_Torrens, for one. Those lower LODs are the same piece of geometry at lower detail and
        /// hang off the same bone, so a named one speaks for the unnamed ones in its component.
        /// </summary>
        public static int[][] Bind(Models.CS2 model, Skeleton rig)
        {
            if (model == null) return new int[0][];

            int[][] bound = new int[model.Components.Count][];
            for (int c = 0; c < model.Components.Count; c++)
            {
                List<Models.CS2.Component.LOD> lods = model.Components[c].LODs;
                bound[c] = new int[lods.Count];

                int spare = -1;
                for (int l = 0; l < lods.Count; l++)
                {
                    bound[c][l] = BoneFor(rig, lods[l].Name);
                    if (bound[c][l] >= 0 && spare < 0) spare = bound[c][l];
                }

                /* A named part the rig doesn't mention is scenery and stays put - only stand in for
                 * the ones with no name to go on. */
                for (int l = 0; l < lods.Count; l++)
                    if (bound[c][l] < 0 && string.IsNullOrEmpty(lods[l].Name)) bound[c][l] = spare;
            }
            return bound;
        }

        /// <summary>How many of a model's parts a rig moves.</summary>
        public static int DrivenParts(Models.CS2 model, Skeleton rig)
        {
            int driven = 0;
            foreach (int[] component in Bind(model, rig))
                foreach (int bone in component)
                    if (bone >= 0) driven++;
            return driven;
        }

        /// <summary>How many parts a model has, driven or not.</summary>
        public static int PartCount(Models.CS2 model)
        {
            int count = 0;
            foreach (string part in PartNames(model)) count++;
            return count;
        }

        /// <summary>
        /// Whether the model names its parts at all. Some levels ship a prop with every part name
        /// stripped, and then nothing can be matched to it by name however right the rig is.
        /// </summary>
        public static bool HasPartNames(Models.CS2 model)
        {
            foreach (string part in PartNames(model))
                if (!string.IsNullOrEmpty(part)) return true;
            return false;
        }

        /// <summary>
        /// Whether a rig looks like it belongs to a model: the level animates that mesh with it, or
        /// failing that at least one named part matches a bone. Either way the model has to be
        /// static - a skinned mesh is a character, and moves the other way.
        /// </summary>
        public static bool Drives(Skeleton rig, Models.CS2 model, Level level = null)
        {
            if (rig == null || model == null || Skeleton.RequiredBoneCount(model) != 0) return false;

            Prop prop = level == null ? null : PropFor(level, rig.Name, model);
            if (prop != null) return prop.Driven > 0;
            return DrivenParts(model, rig) > 0;
        }

        private static IEnumerable<string> PartNames(Models.CS2 model)
        {
            if (model == null) yield break;
            foreach (Models.CS2.Component component in model.Components)
                foreach (Models.CS2.Component.LOD lod in component.LODs)
                    yield return lod.Name;
        }

        private static string LastSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            return path.Substring(path.LastIndexOfAny(new[] { '\\', '/' }) + 1);
        }

        private static List<Prop> Build(Level level)
        {
            List<Prop> props = new List<Prop>();

            ShortGuid positionParameter = ShortGuidUtils.Generate("position");
            Dictionary<Models.CS2.Component.LOD.Submesh, Models.CS2> owners = null;

            foreach (Composite composite in level.Commands.Entries)
            {
                /* Two passes, because resolving a renderable instance back to its model is the
                 * expensive part and the great majority of composites never animate anything.
                 * Finding the entries first means only the handful that do pay for it. */
                List<EnvironmentAnimations.EnvironmentAnimation> entries = EntriesIn(composite);
                if (entries == null) continue;

                if (owners == null) owners = IndexSubmeshes(level.Models);
                Dictionary<ShortGuid, Placement> drawn = Drawn(composite, positionParameter);
                if (drawn.Count == 0) continue;

                foreach (EnvironmentAnimations.EnvironmentAnimation entry in entries)
                {
                    Prop prop = new Prop { Entry = entry, Composite = composite };

                    /* The entry names one renderable per bone. Anything else the composite draws is
                     * part of the same prop but never moves - a door frame, a weapon housing. */
                    Dictionary<ShortGuid, int> boneOf = new Dictionary<ShortGuid, int>();
                    for (int i = 0; i < entry.BoneMappings.Count; i++)
                        if (!boneOf.ContainsKey(entry.BoneMappings[i])) boneOf[entry.BoneMappings[i]] = i;

                    Dictionary<Models.CS2, int> parts = new Dictionary<Models.CS2, int>(SameModel.Instance);
                    foreach (KeyValuePair<ShortGuid, Placement> renderable in drawn)
                    {
                        int bone = boneOf.TryGetValue(renderable.Key, out int found) ? found : -1;
                        Matrix4x4 rest = renderable.Value.Transform;
                        if (bone >= 0 && bone < entry.InverseBindPoses.Count
                            && Matrix4x4.Invert(entry.InverseBindPoses[bone], out Matrix4x4 bind)) rest = bind;

                        foreach (Models.CS2.Component.LOD.Submesh submesh in renderable.Value.Submeshes)
                        {
                            prop.Parts.Add(new Part { Submesh = submesh, Rest = rest, Bone = bone });
                            if (!owners.TryGetValue(submesh, out Models.CS2 model)) continue;
                            parts[model] = (parts.TryGetValue(model, out int count) ? count : 0) + 1;
                        }
                    }

                    prop.Models = parts.OrderByDescending(x => x.Value).Select(x => x.Key).ToList();
                    if (prop.Parts.Count != 0) props.Add(prop);
                }
            }
            return props;
        }

        private static List<EnvironmentAnimations.EnvironmentAnimation> EntriesIn(Composite composite)
        {
            List<EnvironmentAnimations.EnvironmentAnimation> entries = null;
            foreach (FunctionEntity entity in composite.functions)
                foreach (Parameter parameter in entity.parameters)
                {
                    if (!(parameter?.content is cResource resource) || resource.value == null) continue;
                    foreach (ResourceReference reference in resource.value)
                    {
                        if (reference.resource_type != ResourceType.ANIMATED_MODEL || reference.AnimatedModel == null) continue;
                        if (entries == null) entries = new List<EnvironmentAnimations.EnvironmentAnimation>();
                        if (!entries.Contains(reference.AnimatedModel)) entries.Add(reference.AnimatedModel);
                    }
                }
            return entries;
        }

        private struct Placement
        {
            public Matrix4x4 Transform;
            public List<Models.CS2.Component.LOD.Submesh> Submeshes;
        }

        /* Every piece of geometry a composite draws, by the id of the resource that draws it - the
         * same id the animation entry uses to name the part a bone moves. */
        private static Dictionary<ShortGuid, Placement> Drawn(Composite composite, ShortGuid positionParameter)
        {
            Dictionary<ShortGuid, Placement> drawn = new Dictionary<ShortGuid, Placement>();
            foreach (FunctionEntity entity in composite.functions)
            {
                Matrix4x4 place = Matrix4x4.Identity;
                foreach (Parameter parameter in entity.parameters)
                    if (parameter.name == positionParameter && parameter.content is cTransform transform)
                        place = ToMatrix(transform);

                foreach (Parameter parameter in entity.parameters)
                {
                    if (!(parameter?.content is cResource resource) || resource.value == null) continue;
                    foreach (ResourceReference reference in resource.value)
                    {
                        if (reference.resource_type != ResourceType.RENDERABLE_INSTANCE) continue;
                        if (reference.RenderableInstance == null || reference.RenderableInstance.Count == 0) continue;

                        if (!drawn.TryGetValue(reference.resource_id, out Placement placement))
                            placement = new Placement { Transform = place, Submeshes = new List<Models.CS2.Component.LOD.Submesh>() };

                        foreach (RenderableElements.Element element in reference.RenderableInstance)
                        {
                            AddOnce(placement.Submeshes, element.Model);
                            foreach (RenderableElements.Element lod in element.LODs) AddOnce(placement.Submeshes, lod.Model);
                        }
                        drawn[reference.resource_id] = placement;
                    }
                }
            }
            return drawn;
        }

        /* Models and submeshes compare by value, which walks their whole contents - and two copies
         * of the same geometry are still two separate things here. Identity is both faster and the
         * question actually being asked. */
        private static bool Holds(List<Models.CS2> models, Models.CS2 model)
        {
            foreach (Models.CS2 held in models)
                if (ReferenceEquals(held, model)) return true;
            return false;
        }

        private static void AddOnce(List<Models.CS2.Component.LOD.Submesh> submeshes, Models.CS2.Component.LOD.Submesh submesh)
        {
            if (submesh == null) return;
            foreach (Models.CS2.Component.LOD.Submesh held in submeshes)
                if (ReferenceEquals(held, submesh)) return;
            submeshes.Add(submesh);
        }

        /* COMMANDS stores a placement as degrees about each axis, applied the way the rest of
         * OpenCAGE reads them. */
        private static Matrix4x4 ToMatrix(cTransform transform)
        {
            const float radians = (float)(Math.PI / 180.0);
            Quaternion rotation = Quaternion.CreateFromYawPitchRoll(
                transform.rotation.Y * radians, transform.rotation.X * radians, transform.rotation.Z * radians);
            return Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(transform.position);
        }

        /* Models.FindModel walks every model in the level and compares submeshes by value, which is
         * far too slow to call once per renderable element - a big level has over a hundred thousand
         * of them. One pass by reference up front answers the same question. */
        private static Dictionary<Models.CS2.Component.LOD.Submesh, Models.CS2> IndexSubmeshes(Models models)
        {
            Dictionary<Models.CS2.Component.LOD.Submesh, Models.CS2> owners
                = new Dictionary<Models.CS2.Component.LOD.Submesh, Models.CS2>(SameObject.Instance);
            if (models == null) return owners;

            foreach (Models.CS2 model in models.Entries)
                foreach (Models.CS2.Component component in model.Components)
                    foreach (Models.CS2.Component.LOD lod in component.LODs)
                        foreach (Models.CS2.Component.LOD.Submesh submesh in lod.Submeshes)
                            owners[submesh] = model;
            return owners;
        }

        /// <summary>Identity, not equality - two submeshes with the same contents are still two submeshes.</summary>
        private class SameObject : IEqualityComparer<Models.CS2.Component.LOD.Submesh>
        {
            public static readonly SameObject Instance = new SameObject();

            public bool Equals(Models.CS2.Component.LOD.Submesh x, Models.CS2.Component.LOD.Submesh y) { return ReferenceEquals(x, y); }
            public int GetHashCode(Models.CS2.Component.LOD.Submesh obj) { return RuntimeHelpers.GetHashCode(obj); }
        }

        /// <summary>The same, for whole models.</summary>
        private class SameModel : IEqualityComparer<Models.CS2>
        {
            public static readonly SameModel Instance = new SameModel();

            public bool Equals(Models.CS2 x, Models.CS2 y) { return ReferenceEquals(x, y); }
            public int GetHashCode(Models.CS2 obj) { return RuntimeHelpers.GetHashCode(obj); }
        }
    }
}
