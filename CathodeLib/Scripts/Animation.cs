using CATHODE;
using CATHODE.Animations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml;

namespace CathodeLib
{
    /// <summary>
    /// A helper class that holds every parseable format in DATA/GLOBAL/ANIMATION.PAK, resolves the
    /// references between them, and writes them all back into the PAK together.
    ///
    /// The PAK holds each skeleton, mapping and ragdoll twice - once for the 32 bit build and once
    /// for the 64 bit one - so those are paired up into a single entry with both halves on it.
    /// </summary>
    public class Animation
    {
        /// <summary>Names and hashes shared by every file in the PAK.</summary>
        public AnimationStrings Strings;

        /// <summary>The larger string table CA shipped alongside it - argument and clip names live here.</summary>
        public AnimationStrings StringsDebug;

        /// <summary>Every skeleton in the game, and which pairs of them can be retargeted.</summary>
        public SkeletonDB SkeletonIndex;

        /// <summary>The blend sets and the dependencies between the per-character clip DBs.</summary>
        public GlobalAnimClipDB ClipIndex;

        /// <summary>One per character: the clips it owns and the sections holding them.</summary>
        public List<AnimClipDB> ClipDatabases = new List<AnimClipDB>();

        /// <summary>The loadable chunks of animation - Havok clips plus their metadata.</summary>
        public List<AnimClipDBSec> Sections = new List<AnimClipDBSec>();

        /// <summary>The animation trees that decide which clip plays when. These name themselves out
        /// of the debug string table rather than the plain one.</summary>
        public List<AnimTreeDB> Trees = new List<AnimTreeDB>();

        public List<SkeletonAsset> Skeletons = new List<SkeletonAsset>();
        public List<MappingAsset> Mappings = new List<MappingAsset>();
        public List<RagdollAsset> Ragdolls = new List<RagdollAsset>();

        /// <summary>The PAK everything came out of. Entry content is refreshed by <see cref="Save"/>.</summary>
        public PAK2 PAK { get { return _pak; } }

        /// <summary>Whether every file in the PAK was parsed. See <see cref="Failures"/> if not.</summary>
        public bool Loaded { get { return _loaded; } }

        /// <summary>Files that didn't parse, by PAK path.</summary>
        public List<string> Failures = new List<string>();

        private PAK2 _pak;
        private bool _loaded;

        public Animation(string path) : this(new PAK2(path)) { }

        public Animation(PAK2 pak)
        {
            _pak = pak;
            _loaded = Load();
        }

        #region LOAD_SAVE
        private bool Load()
        {
            if (_pak == null || !_pak.Loaded || _pak.Entries == null) return false;

            Strings = LoadOne(Find("ANIM_STRING_DB.BIN"), x => new AnimationStrings(x.Content, x.Filename));
            StringsDebug = LoadOne(Find("ANIM_STRING_DB_DEBUG.BIN"), x => new AnimationStrings(x.Content, x.Filename));
            if (Strings == null) return false;

            /* Most names in the PAK - clips, contexts, arguments - only exist in the debug table,
             * so let every parser reach it through the plain one rather than threading both around. */
            if (StringsDebug != null) Strings.Fallback = StringsDebug;

            SkeletonIndex = LoadOne(Find(@"SKELE\DB.BIN"), x => new SkeletonDB(x.Content, Strings, x.Filename));
            ClipIndex = LoadOne(Find(@"ANIM_SYS\ANIM_CLIP_DB.BIN"), x => new GlobalAnimClipDB(x.Content, Strings, x.Filename, StringsDebug));

            foreach (PAK2.File file in _pak.Entries)
            {
                string name = (file.Filename ?? "").ToUpperInvariant();
                if (file.Content == null) continue;

                if (name.Contains("ANIM_CLIP_DB_SEC_"))
                    Add(Sections, file, x => new AnimClipDBSec(x.Content, Strings, x.Filename, StringsDebug));
                else if (name.EndsWith("_ANIM_CLIP_DB.BIN"))
                    Add(ClipDatabases, file, x => new AnimClipDB(x.Content, Strings, x.Filename, StringsDebug));
                else if (name.EndsWith("_ANIM_TREE_DB.BIN"))
                    Add(Trees, file, x => new AnimTreeDB(x.Content, StringsDebug, x.Filename));
            }

            LoadPaired(@"SKELE\SK\", @"SKELE\SK64\", Skeletons,
                (file, sixtyFour) => new SkeletonAsset { Name = SkeletonName(file.Filename) },
                (asset, file, sixtyFour) =>
                {
                    Skeleton skeleton = new Skeleton(file.Content, Strings, file.Filename);
                    if (!skeleton.Loaded) return false;
                    if (sixtyFour) asset.Skeleton64 = skeleton; else asset.Skeleton = skeleton;
                    return true;
                });

            LoadPaired(@"SKELE\MAPS\", @"SKELE\MAPS64\", Mappings,
                (file, sixtyFour) => new MappingAsset(),
                (asset, file, sixtyFour) =>
                {
                    SkeletonMapping mapping = new SkeletonMapping(file.Content, Strings, file.Filename);
                    if (!mapping.Loaded) return false;
                    if (sixtyFour) asset.Mapping64 = mapping; else asset.Mapping = mapping;
                    return true;
                });

            LoadPaired(@"SKELE\RAGS\", @"SKELE\RAGS64\", Ragdolls,
                (file, sixtyFour) => new RagdollAsset { Name = SkeletonName(file.Filename) },
                (asset, file, sixtyFour) =>
                {
                    Ragdoll ragdoll = new Ragdoll(file.Content, Strings, file.Filename);
                    if (!ragdoll.Loaded) return false;
                    if (sixtyFour) asset.Ragdoll64 = ragdoll; else asset.Ragdoll = ragdoll;
                    return true;
                });

            //name the mappings now the skeletons are in, since the files are named by hash
            foreach (MappingAsset mapping in Mappings)
            {
                SkeletonMapping source = mapping.Mapping ?? mapping.Mapping64;
                if (source == null) continue;
                mapping.SkeletonA = source.SkeletonA;
                mapping.SkeletonB = source.SkeletonB;
            }

            BuildSets();
            return Failures.Count == 0;
        }

        /// <summary>
        /// Push every parsed file back into the PAK and write it out. Pass a path to save elsewhere.
        /// </summary>
        public bool Save(string path = "")
        {
            if (_pak == null) return false;

            Write(Strings, x => x.ToBytes());
            Write(StringsDebug, x => x.ToBytes());
            Write(SkeletonIndex, x => x.ToBytes());
            Write(ClipIndex, x => x.ToBytes());
            foreach (AnimClipDB clips in ClipDatabases) Write(clips, x => x.ToBytes());
            foreach (AnimClipDBSec section in Sections) Write(section, x => x.ToBytes());
            foreach (AnimTreeDB tree in Trees) Write(tree, x => x.ToBytes());

            foreach (SkeletonAsset skeleton in Skeletons)
            {
                Write(skeleton.Skeleton, x => x.ToBytes());
                Write(skeleton.Skeleton64, x => x.ToBytes());
            }
            foreach (MappingAsset mapping in Mappings)
            {
                Write(mapping.Mapping, x => x.ToBytes());
                Write(mapping.Mapping64, x => x.ToBytes());
            }
            foreach (RagdollAsset ragdoll in Ragdolls)
            {
                Write(ragdoll.Ragdoll, x => x.ToBytes());
                Write(ragdoll.Ragdoll64, x => x.ToBytes());
            }

            return path.Length == 0 ? _pak.Save() : _pak.Save(path);
        }

        /* Serialise one file back over the PAK entry it came from. */
        private void Write<T>(T file, Func<T, byte[]> serialise) where T : CathodeFile
        {
            if (file == null) return;
            PAK2.File entry = _pak.Entries.FirstOrDefault(x => string.Equals(x.Filename, file.Filepath, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return;

            byte[] content = serialise(file);
            if (content != null) entry.Content = content;
        }

        private PAK2.File Find(string suffix)
        {
            return _pak.Entries.FirstOrDefault(x => (x.Filename ?? "").EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }

        private T LoadOne<T>(PAK2.File file, Func<PAK2.File, T> make) where T : CathodeFile
        {
            if (file?.Content == null) return null;
            T loaded = make(file);
            if (loaded.Loaded) return loaded;
            Failures.Add(file.Filename);
            return null;
        }

        private void Add<T>(List<T> into, PAK2.File file, Func<PAK2.File, T> make) where T : CathodeFile
        {
            T loaded = make(file);
            if (loaded.Loaded) into.Add(loaded);
            else Failures.Add(file.Filename);
        }

        /* The 32 and 64 bit copies of a skeleton share a filename, so pair them as we go. */
        private void LoadPaired<T>(string folder, string folder64, List<T> into,
                                   Func<PAK2.File, bool, T> make, Func<T, PAK2.File, bool, bool> fill)
        {
            Dictionary<string, T> byName = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            foreach (PAK2.File file in _pak.Entries)
            {
                if (file.Content == null) continue;
                string name = (file.Filename ?? "").ToUpperInvariant();
                bool sixtyFour = name.Contains(folder64.ToUpperInvariant());
                if (!sixtyFour && !name.Contains(folder.ToUpperInvariant())) continue;

                string key = SkeletonName(file.Filename);
                if (!byName.TryGetValue(key, out T asset))
                {
                    asset = make(file, sixtyFour);
                    byName[key] = asset;
                    into.Add(asset);
                }
                if (!fill(asset, file, sixtyFour)) Failures.Add(file.Filename);
            }
        }

        private static string SkeletonName(string path)
        {
            string name = (path ?? "").Replace('/', '\\');
            int slash = name.LastIndexOf('\\');
            return slash < 0 ? name : name.Substring(slash + 1);
        }
        #endregion

        #region LOOKUPS
        /// <summary>Find a skeleton by name, e.g. "MALE" or "ALIEN".</summary>
        public SkeletonAsset GetSkeleton(string name)
        {
            SkeletonDB.SkeletonEntry entry = SkeletonIndex?.GetSkeleton(name);
            if (entry == null) return null;
            string file = SkeletonName(SkeletonIndex.GetSkeletonPath(entry));
            return Skeletons.FirstOrDefault(x => string.Equals(x.Name, file, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Find the retargeting data that plays <paramref name="from"/>'s animation on <paramref name="to"/>.</summary>
        public MappingAsset GetMapping(string from, string to)
        {
            return Mappings.FirstOrDefault(x =>
                string.Equals(x.SkeletonA, from, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.SkeletonB, to, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Every skeleton this one can be retargeted onto.</summary>
        public List<MappingAsset> GetMappingsFrom(string skeleton)
        {
            return Mappings.Where(x => string.Equals(x.SkeletonA, skeleton, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>Every clip every set can play, in set and context order.</summary>
        public List<ClipReference> GetClips()
        {
            return Sets.SelectMany(x => x.Contexts).SelectMany(x => x.Clips).ToList();
        }

        /// <summary>Every clip authored against a skeleton, across all the sets that use it.</summary>
        public List<ClipReference> GetClips(string skeleton)
        {
            return GetClips().Where(x => string.Equals(x.Skeleton, skeleton, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>
        /// Find clips by name or authored path, e.g. "reload" or "SHOTGUN\RELOAD_OUT". Matches on
        /// any part of either.
        /// </summary>
        public List<ClipReference> FindClips(string search)
        {
            if (string.IsNullOrEmpty(search)) return GetClips();
            return GetClips().Where(x =>
                x.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                x.Path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        /// <summary>
        /// Everything tagged on a clip's timeline, flattened out of the metadata into one marker per
        /// occurrence and sorted by time. Audio markers come back with the Wwise event resolved.
        /// </summary>
        public static List<ClipMarker> GetMarkers(ClipReference clip)
        {
            List<ClipMarker> markers = new List<ClipMarker>();
            AnimClipDBSec.MetadataSet set = clip?.Metadata;
            if (set == null) return markers;

            //the common block holds the clip's own tags; each instance is one place it gets used
            CollectMarkers(markers, set.Common, -1);
            for (int i = 0; i < set.Instances.Count; i++) CollectMarkers(markers, set.Instances[i], i);

            markers.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : string.Compare(a.Property, b.Property, StringComparison.OrdinalIgnoreCase));
            return markers;
        }

        private static void CollectMarkers(List<ClipMarker> into, AnimClipDBSec.MetadataBlock block, int instance)
        {
            if (block == null) return;

            foreach (AnimClipDBSec.MetadataProperty property in block.Properties)
            {
                for (int i = 0; i < property.Times.Count; i++)
                {
                    ClipMarker marker = new ClipMarker
                    {
                        Property = property.Name,
                        Time = property.Times[i],
                        Instance = instance,
                        Block = block,
                    };

                    if (i < property.Events.Count)
                    {
                        AnimClipDBSec.MetadataEvent fired = property.Events[i];
                        marker.Event = fired.Name;
                        marker.Type = fired.Type;

                        /* A PROPERTY_REFERENCE names an argument of the same block; anything else
                         * carries its value in that field directly. */
                        if (fired.Type == MetadataValueType.PROPERTY_REFERENCE)
                        {
                            marker.Argument = block.Arguments.FirstOrDefault(x => x.Name == fired.Name);
                            if (marker.Argument != null && marker.Argument.Type == MetadataValueType.AUDIO)
                                marker.Audio = ParseAudioEvent(marker.Argument.Value as string);
                        }
                    }
                    into.Add(marker);
                }
            }
        }

        /// <summary>
        /// Pull an audio argument apart. The value is stored as one string in the form
        /// "[ArgumentList={},Bone={LipsUpper},Event={play_footstep},Offset={0,0,0},UseArguments={No}]".
        /// Returns null if it isn't in that shape.
        /// </summary>
        public static AudioEvent ParseAudioEvent(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf("={", StringComparison.Ordinal) < 0) return null;

            AudioEvent audio = new AudioEvent { Raw = value };
            int at = 0;
            while (at < value.Length)
            {
                int equals = value.IndexOf("={", at, StringComparison.Ordinal);
                if (equals < 0) break;

                //the key runs back to whatever punctuation separated it from the field before
                int start = equals;
                while (start > 0 && value[start - 1] != ',' && value[start - 1] != '[') start--;
                string key = value.Substring(start, equals - start).Trim();

                int close = value.IndexOf('}', equals + 2);
                if (close < 0) break;
                string field = value.Substring(equals + 2, close - equals - 2);
                at = close + 1;

                switch (key)
                {
                    case "Event": audio.Event = field; break;
                    case "Bone": audio.Bone = field; break;
                    case "Offset": audio.Offset = field; break;
                    case "ArgumentList": audio.Arguments = field; break;
                    case "UseArguments": audio.UsesArguments = !string.Equals(field, "No", StringComparison.OrdinalIgnoreCase); break;
                }
            }
            return audio.Event == null && audio.Bone == null ? null : audio;
        }

        /// <summary>The clip label a metadata set carries, or null if it has none.</summary>
        public static string LabelOf(AnimClipDBSec.MetadataSet set)
        {
            return set?.Common?.Arguments
                .FirstOrDefault(x => x.Name == "anim_label" || x.Name == "meta_label")?.Value as string;
        }

        /// <summary>Read a named argument off a metadata block, or null if it isn't there.</summary>
        public static object ArgumentOf(AnimClipDBSec.MetadataBlock block, string name)
        {
            return block?.Arguments.FirstOrDefault(x => x.Name == name)?.Value;
        }

        /// <summary>The animation trees belonging to one set, e.g. "MALE".</summary>
        public List<AnimationTree> GetTrees(string set)
        {
            return Trees.SelectMany(x => x.Entries)
                        .Where(x => string.Equals(x.Set, set, StringComparison.OrdinalIgnoreCase))
                        .ToList();
        }
        #endregion
        #region SETS
        /// <summary>
        /// Every animation set in the PAK, resolved down to the clips it can play. This is the view
        /// the game presents to script: pick a set, pick a context within it, play a clip by name.
        /// </summary>
        public List<AnimationSet> Sets = new List<AnimationSet>();

        /// <summary>The skeleton definitions from DATA/SKELETONDEFS, keyed by skeleton name.</summary>
        public Dictionary<string, SkeletonDef> SkeletonDefs = new Dictionary<string, SkeletonDef>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Look a set up by name, e.g. "MALE" or "DOORS_DOOR_SHOPPING".</summary>
        public AnimationSet GetSet(string name)
        {
            return Sets.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Resolve an authored clip path to the section holding it, and the clip's index inside it.
        /// The global index names the section, and the file is that name hashed - a streamed clip
        /// gets a file to itself, a resident one shares with the rest of its character's set.
        /// </summary>
        public AnimClipDBSec GetSection(string clipPath, out int index)
        {
            index = 0;
            if (clipPath == null || _sectionOfClip == null) return null;
            if (!_sectionOfClip.TryGetValue(clipPath, out GlobalAnimClipDB.ClipDbSection entry)) return null;

            index = entry.SectionIndex < 0 ? 0 : entry.SectionIndex;
            return _sectionByName.TryGetValue("ANIM_CLIP_DB_SEC_" + Utilities.AnimationHashedString(entry.SectionName), out AnimClipDBSec section)
                ? section : null;
        }

        /* Sets and their contexts come straight out of the per-character clip DBs; the work here is
         * pointing each clip at the section that actually holds its animation. */
        private void BuildSets()
        {
            Sets.Clear();
            _sectionOfClip = new Dictionary<string, GlobalAnimClipDB.ClipDbSection>(StringComparer.OrdinalIgnoreCase);
            _sectionByName = new Dictionary<string, AnimClipDBSec>(StringComparer.OrdinalIgnoreCase);

            foreach (AnimClipDBSec section in Sections)
            {
                //the 64 bit copies hold the same clips built for the other pointer size, so index one set
                string folder = (Path.GetDirectoryName(section.Filepath) ?? "").ToUpperInvariant();
                if (folder.EndsWith("64")) continue;
                _sectionByName[Path.GetFileNameWithoutExtension(section.Filepath)] = section;
            }
            if (ClipIndex != null)
                foreach (GlobalAnimClipDB.ClipDbSection entry in ClipIndex.ClipDbSections)
                    _sectionOfClip[entry.Name] = entry;

            ReadSkeletonDefs();

            foreach (AnimClipDB database in ClipDatabases)
            {
                AnimationSet set = new AnimationSet { Name = database.Character, Database = database };
                set.Contexts.Add(MakeContext(set, "", database.Animations));
                foreach (AnimClipDB.Context context in database.Contexts)
                    set.Contexts.Add(MakeContext(set, context.Name, context.Animations));

                set.ClipCount = set.Contexts.Sum(x => x.Clips.Count);
                set.Skeleton = PrimarySkeleton(set);
                set.Kind = Classify(set);
                Sets.Add(set);
            }
        }

        private AnimationContext MakeContext(AnimationSet set, string name, List<AnimClipDB.AnimClip> clips)
        {
            AnimationContext context = new AnimationContext { Name = name, Set = set };
            foreach (AnimClipDB.AnimClip clip in clips)
            {
                AnimClipDBSec section = GetSection(clip.Path, out int index);
                context.Clips.Add(new ClipReference
                {
                    Name = clip.Name,
                    Path = clip.Path,
                    Context = context,
                    Section = section,
                    Index = index,
                });
            }
            return context;
        }

        /* Whichever skeleton the set's clips are actually authored against. Nearly every set has just
         * one; the shared human sections list a hundred, so fall back to whichever comes up most. */
        private string PrimarySkeleton(AnimationSet set)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (AnimationContext context in set.Contexts)
                foreach (ClipReference clip in context.Clips)
                {
                    if (clip.Section == null) continue;
                    foreach (string skeleton in clip.Section.SkeletonDependencies)
                    {
                        counts.TryGetValue(skeleton, out int seen);
                        counts[skeleton] = seen + 1;
                    }
                }

            //a set named after a skeleton is authored against it, however the shared sections are listed
            if (counts.ContainsKey(set.Name)) return set.Name;
            return counts.Count == 0 ? "" : counts.OrderByDescending(x => x.Value).First().Key;
        }

        /* Environment rigs live in a folder tree under ReferenceSkeletons named after the piece of set
         * dressing they drive; character rigs sit loose at the top of it. Nothing else separates the
         * two in the shipped data - though every rig with a ragdoll does fall on the character side.
         *
         * Not every set has a definition, so this falls back twice more. */
        private AnimationKind Classify(AnimationSet set)
        {
            if (SkeletonDefs.TryGetValue(set.Name, out SkeletonDef named))
                return named.IsEnvironment ? AnimationKind.Environment : AnimationKind.Character;
            if (set.Skeleton.Length != 0 && SkeletonDefs.TryGetValue(set.Skeleton, out SkeletonDef used))
                return used.IsEnvironment ? AnimationKind.Environment : AnimationKind.Character;

            /* Go by the company it keeps. ANDROID has no definition of its own, but its 1,528 clips
             * sit in sections alongside MALE, FEMALE and half the named cast, all of which do. */
            int character = 0, environment = 0;
            foreach (string skeleton in SkeletonsUsedBy(set))
            {
                if (!SkeletonDefs.TryGetValue(skeleton, out SkeletonDef def)) continue;
                if (def.IsEnvironment) environment++; else character++;
            }
            if (character != environment)
                return character > environment ? AnimationKind.Character : AnimationKind.Environment;

            /* Last resort, the shape of the rig. Across the 395 sets the definitions do cover, every
             * character rig carries most of these bones and not one environment rig carries any. */
            return LooksHumanoid(GetSkeleton(set.Skeleton)?.Bones) ? AnimationKind.Character : AnimationKind.Unknown;
        }

        /// <summary>Every skeleton the sections holding a set's clips are built against.</summary>
        public IEnumerable<string> SkeletonsUsedBy(AnimationSet set)
        {
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AnimationContext context in set.Contexts)
                foreach (ClipReference clip in context.Clips)
                {
                    if (clip.Section == null) continue;
                    foreach (string skeleton in clip.Section.SkeletonDependencies) used.Add(skeleton);
                }
            return used;
        }

        /* The bones every animated character has and no door, locker or fan does. */
        private static readonly string[] _humanoidBones =
            { "HIPS", "SPINE", "HEAD", "NECK", "LEFTARM", "RIGHTARM", "LEFTLEG", "RIGHTLEG", "PELVIS" };

        private static bool LooksHumanoid(List<Skeleton.Bone> bones)
        {
            if (bones == null) return false;

            int found = 0;
            foreach (string landmark in _humanoidBones)
                foreach (Skeleton.Bone bone in bones)
                    if (bone.Name.EndsWith(":" + landmark, StringComparison.OrdinalIgnoreCase)) { found++; break; }
            return found >= 4;
        }

        private void ReadSkeletonDefs()
        {
            SkeletonDefs.Clear();
            foreach (PAK2.File file in _pak.Entries)
            {
                if (file.Content == null) continue;
                if (!(file.Filename ?? "").StartsWith(@"DATA\SKELETONDEFS", StringComparison.OrdinalIgnoreCase)) continue;

                SkeletonDef def = SkeletonDef.Read(file);
                if (def != null) SkeletonDefs[def.Name] = def;
            }
        }

        private Dictionary<string, GlobalAnimClipDB.ClipDbSection> _sectionOfClip;
        private Dictionary<string, AnimClipDBSec> _sectionByName;
        #endregion

        #region SAMPLING
        /// <summary>
        /// One frame of a clip as parent-relative bone transforms, indexed by skeleton bone. Bones the
        /// clip doesn't drive keep their bind pose, so a clip that only moves an arm still gives back
        /// a complete, posable skeleton.
        ///
        /// Additive clips are layered onto the bind pose rather than replacing it, which is the only
        /// base pose available in isolation - in game they go on top of whatever is already playing.
        /// </summary>
        public static List<Matrix4x4> SampleLocalPose(ClipReference clip, Skeleton skeleton, int frame, RootMotion root = RootMotion.Ignore, Retargeter retarget = null)
        {
            List<HavokPackfile.SampledTransform> bones = SampleBones(clip, skeleton, frame, root, retarget);
            if (bones == null) return null;

            List<Matrix4x4> pose = new List<Matrix4x4>(bones.Count);
            for (int i = 0; i < bones.Count; i++)
                pose.Add(Matrix4x4.CreateScale(bones[i].Scale)
                       * Matrix4x4.CreateFromQuaternion(bones[i].Rotation)
                       * Matrix4x4.CreateTranslation(bones[i].Translation));
            return pose;
        }

        /// <summary>
        /// The same frame as <see cref="SampleLocalPose"/>, but kept as separate translation, rotation
        /// and scale per bone - which is what a keyframe wants, and avoids decomposing a matrix back
        /// into the three parts it was built from.
        /// </summary>
        public static List<HavokPackfile.SampledTransform> SampleBones(ClipReference clip, Skeleton skeleton, int frame,
            RootMotion root = RootMotion.Ignore, Retargeter retarget = null)
        {
            /* With a retargeter the clip is sampled on the rig it was authored for and moved across
             * afterwards. Anchoring happens last either way, because the bone it keys off belongs to
             * the rig being played on. */
            Skeleton authored = retarget == null ? skeleton : retarget.From;

            List<HavokPackfile.SampledTransform> pose = SampleBonesRaw(clip, authored, frame);
            if (pose == null || clip?.Animation == null) return pose;
            if (retarget != null) pose = retarget.Apply(pose);

            List<HavokPackfile.SampledTransform> start = frame == 0 ? pose : null;
            if (start == null)
            {
                start = SampleBonesRaw(clip, authored, 0);
                if (retarget != null && start != null) start = retarget.Apply(start);
            }
            return Anchor(pose, start, skeleton, frame, root);
        }

        /* The clip's own transforms, with nothing done about where the character ends up. */
        private static List<HavokPackfile.SampledTransform> SampleBonesRaw(ClipReference clip, Skeleton skeleton, int frame)
        {
            if (skeleton == null) return null;

            List<HavokPackfile.SampledTransform> pose = new List<HavokPackfile.SampledTransform>(skeleton.Bones.Count);
            for (int i = 0; i < skeleton.Bones.Count; i++)
                pose.Add(new HavokPackfile.SampledTransform
                {
                    Translation = skeleton.Bones[i].Position,
                    Rotation = skeleton.Bones[i].Rotation,
                    Scale = skeleton.Bones[i].ScaleXYZ,
                });

            HavokPackfile.AnimationClip animation = clip?.Animation;
            if (animation == null) return pose;

            List<HavokPackfile.SampledTransform> tracks = clip.Section.Havok.Sample(animation, frame);
            for (int track = 0; track < tracks.Count && track < animation.TrackToBone.Count; track++)
            {
                int bone = animation.TrackToBone[track];
                if (bone < 0 || bone >= pose.Count) continue;

                HavokPackfile.SampledTransform sampled = tracks[track];
                if (!animation.Additive)
                {
                    /* Only take the channels the clip actually stored. A track that leaves one out
                     * isn't saying "put this at zero", it's saying "the rig already has it right" -
                     * which matters for the environment rigs, where a part authored at a scale
                     * other than 1 is animated by clips that never mention scale. */
                    pose[bone] = new HavokPackfile.SampledTransform
                    {
                        Translation = sampled.HasTranslation ? sampled.Translation : skeleton.Bones[bone].Position,
                        Rotation = sampled.HasRotation ? sampled.Rotation : skeleton.Bones[bone].Rotation,
                        Scale = sampled.HasScale ? sampled.Scale : skeleton.Bones[bone].ScaleXYZ,
                    };
                    continue;
                }

                //an additive clip holds a delta, so lay it over the pose that's already there
                Skeleton.Bone rest = skeleton.Bones[bone];
                pose[bone] = new HavokPackfile.SampledTransform
                {
                    Translation = rest.Position + sampled.Translation,
                    Rotation = rest.Rotation * sampled.Rotation,
                    Scale = rest.ScaleXYZ * sampled.Scale,
                };
            }
            return pose;
        }

        /// <summary>
        /// The bones a clip never moves off the rig's rest pose, either because it holds no track
        /// for them or because the track it holds never turns.
        ///
        /// A clip that drives only part of the body is completely normal - the game lays it over
        /// whatever else is playing, so an idle can leave the arms to a weapon animation. Shown on
        /// its own, though, those bones sit in the rest pose and read as a limb left behind, which
        /// is worth saying out loud.
        /// </summary>
        public static List<int> BonesLeftAtRest(ClipReference clip, Skeleton skeleton, Retargeter retarget = null)
        {
            List<int> still = new List<int>();
            if (clip?.Animation == null || skeleton == null) return still;

            int frames = clip.Animation.FrameCount;
            if (frames < 2) return still;

            bool[] moved = new bool[skeleton.Bones.Count];
            int step = Math.Max(1, (frames - 1) / 5);
            for (int frame = 0; frame < frames; frame += step)
            {
                List<HavokPackfile.SampledTransform> pose = SampleBones(clip, skeleton, frame, RootMotion.Ignore, retarget);
                if (pose == null) break;

                for (int bone = 0; bone < moved.Length && bone < pose.Count; bone++)
                {
                    if (moved[bone]) continue;
                    if (Quaternion.Dot(Quaternion.Normalize(pose[bone].Rotation),
                                       Quaternion.Normalize(skeleton.Bones[bone].Rotation)) < 0.99999f
                        || (pose[bone].Translation - skeleton.Bones[bone].Position).LengthSquared() > 1e-8f)
                        moved[bone] = true;
                }
            }

            for (int bone = 0; bone < moved.Length; bone++)
                if (!moved[bone]) still.Add(bone);
            return still;
        }

        /// <summary>The bone a rig marks its own position with, or -1. Every character rig has one.</summary>
        public static int ReferenceBone(Skeleton skeleton)
        {
            if (skeleton == null) return -1;
            for (int i = 0; i < skeleton.Bones.Count; i++)
                if (skeleton.Bones[i].Name.EndsWith("REFERENCE_ROOT", StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private static int RootBone(Skeleton skeleton)
        {
            for (int i = 0; i < skeleton.Bones.Count; i++)
                if (skeleton.Bones[i].ParentIndex < 0) return i;
            return -1;
        }

        /* Move the whole pose so the character stands where we want them.
         *
         * The correction rides entirely on the root bone's local transform: every other bone hangs
         * off it, so folding it in there moves the lot and leaves the sampled data alone. */
        private static List<HavokPackfile.SampledTransform> Anchor(List<HavokPackfile.SampledTransform> pose,
            List<HavokPackfile.SampledTransform> start, Skeleton skeleton, int frame, RootMotion root)
        {
            int rootBone = RootBone(skeleton);
            if (rootBone < 0) return pose;

            int reference = ReferenceBone(skeleton);
            if (reference < 0)
            {
                /* No reference bone - the environment rigs. Their clips are authored in place, so
                 * the only thing to correct is the root orientation, which the clip overwrites with
                 * something that isn't the pose the mesh was built around. */
                pose[rootBone] = Rest(skeleton, rootBone);
                return pose;
            }

            /* Put the reference bone where it started. This is a fixed transform for the whole clip,
             * so it squares the character up without touching a frame of the animation.
             *
             * Taking it per frame instead would be wrong: the body does not travel with this bone.
             * Across the retail clips where it moves more than 25 cm, the hips follow it only 13% of
             * the way in the median case, so pinning it every frame swings the character around it. */
            if (start == null) start = pose;
            Matrix4x4 anchor = ModelSpaceOf(start, skeleton, reference);
            if (!Matrix4x4.Invert(anchor, out Matrix4x4 place)) return pose;

            /* Playing in place is a viewing aid on top of that: take out however far the body itself
             * has drifted along the ground since the first frame, and leave the height alone. */
            if (root == RootMotion.Ignore && frame != 0)
            {
                int body = BodyBone(skeleton, reference);
                if (body >= 0)
                {
                    Vector3 now = (ModelSpaceOf(pose, skeleton, body) * place).Translation;
                    Vector3 was = (ModelSpaceOf(start, skeleton, body) * place).Translation;
                    place = place * Matrix4x4.CreateTranslation(new Vector3(was.X - now.X, 0, was.Z - now.Z));
                }
            }

            /* Everything above is in model space; folding it into the root's local transform means
             * going through the mesh-space rotation and back out again. */
            Matrix4x4 corrected = ToMatrix(pose[rootBone]) * Skeleton.ToMeshSpace * place * _fromMeshSpace;
            if (!Matrix4x4.Decompose(corrected, out Vector3 scale, out Quaternion rotation, out Vector3 translation))
                return pose;

            pose[rootBone] = new HavokPackfile.SampledTransform { Translation = translation, Rotation = rotation, Scale = scale };
            return pose;
        }

        /* Whichever bone the rest of the skeleton hangs off - the pelvis on a character. Found by
         * counting descendants rather than by name, so it works on any rig. */
        private static int BodyBone(Skeleton skeleton, int reference)
        {
            int best = -1, bestCount = 0;
            for (int i = 0; i < skeleton.Bones.Count; i++)
            {
                if (i == reference || skeleton.Bones[i].ParentIndex < 0) continue;

                int count = 0;
                for (int child = 0; child < skeleton.Bones.Count; child++)
                {
                    for (int at = skeleton.Bones[child].ParentIndex; at >= 0; at = skeleton.Bones[at].ParentIndex)
                        if (at == i) { count++; break; }
                }
                if (count > bestCount) { bestCount = count; best = i; }
            }
            return best;
        }

        private static readonly Matrix4x4 _fromMeshSpace = Matrix4x4.Transpose(Skeleton.ToMeshSpace);

        private static HavokPackfile.SampledTransform Rest(Skeleton skeleton, int bone)
        {
            return new HavokPackfile.SampledTransform
            {
                Translation = skeleton.Bones[bone].Position,
                Rotation = skeleton.Bones[bone].Rotation,
                Scale = skeleton.Bones[bone].ScaleXYZ,
            };
        }

        private static Matrix4x4 ToMatrix(HavokPackfile.SampledTransform transform)
        {
            return Matrix4x4.CreateScale(transform.Scale)
                 * Matrix4x4.CreateFromQuaternion(transform.Rotation)
                 * Matrix4x4.CreateTranslation(transform.Translation);
        }

        /* One bone's transform relative to the skeleton root, walking up the parents */
        private static Matrix4x4 ModelSpaceOf(List<HavokPackfile.SampledTransform> pose, Skeleton skeleton, int bone)
        {
            Matrix4x4 result = ToMatrix(pose[bone]);
            for (int at = skeleton.Bones[bone].ParentIndex; at >= 0; at = skeleton.Bones[at].ParentIndex)
                result = result * ToMatrix(pose[at]);
            return result * Skeleton.ToMeshSpace;
        }

        /// <summary>
        /// One frame of a clip as bone transforms relative to the skeleton root, rotated into mesh
        /// space - the same space <see cref="Skeleton.GetBindPose"/> hands back, so the two compose.
        /// </summary>
        public static List<Matrix4x4> SampleModelPose(ClipReference clip, Skeleton skeleton, int frame, RootMotion root = RootMotion.Ignore, Retargeter retarget = null)
        {
            List<Matrix4x4> local = SampleLocalPose(clip, skeleton, frame, root, retarget);
            if (local == null) return null;

            List<Matrix4x4> pose = new List<Matrix4x4>(local.Count);
            for (int i = 0; i < local.Count; i++)
            {
                int parent = skeleton.Bones[i].ParentIndex;
                pose.Add(parent >= 0 && parent < i ? local[i] * pose[parent] : local[i] * Skeleton.ToMeshSpace);
            }
            return pose;
        }

        /// <summary>
        /// One frame of a clip as bone transforms relative to the skeleton root, left in the
        /// skeleton's own space. <see cref="SampleModelPose"/> turns the same sample into the space
        /// a character's mesh is authored in; an environment rig sits in the same space as the prop
        /// it drives already, and wants this one.
        /// </summary>
        public static List<Matrix4x4> SampleRigPose(ClipReference clip, Skeleton skeleton, int frame, RootMotion root = RootMotion.Ignore, Retargeter retarget = null)
        {
            List<Matrix4x4> local = SampleLocalPose(clip, skeleton, frame, root, retarget);
            if (local == null) return null;

            List<Matrix4x4> pose = new List<Matrix4x4>(local.Count);
            for (int i = 0; i < local.Count; i++)
            {
                int parent = skeleton.Bones[i].ParentIndex;
                pose.Add(parent >= 0 && parent < i ? local[i] * pose[parent] : local[i]);
            }
            return pose;
        }

        /// <summary>
        /// One frame of a clip as skinning matrices - the inverse bind pose times the animated pose.
        /// A vertex run through its bones' matrices and weighted lands where the animation puts it.
        /// </summary>
        public static List<Matrix4x4> SampleSkinningPose(ClipReference clip, Skeleton skeleton, int frame, RootMotion root = RootMotion.Ignore, Retargeter retarget = null)
        {
            List<Matrix4x4> animated = SampleModelPose(clip, skeleton, frame, root, retarget);
            if (animated == null) return null;

            List<Matrix4x4> bind = skeleton.GetBindPose();
            List<Matrix4x4> skinning = new List<Matrix4x4>(animated.Count);
            for (int i = 0; i < animated.Count; i++)
                skinning.Add(Matrix4x4.Invert(bind[i], out Matrix4x4 inverse) ? inverse * animated[i] : Matrix4x4.Identity);
            return skinning;
        }
        #endregion

        #region EDITING
        /// <summary>
        /// Register a name so it can be written back out. Every name in the PAK is stored as a hash,
        /// so anything new has to go in the string table first or it won't survive a save.
        /// </summary>
        public uint AddName(string name, bool debug = false)
        {
            AnimationStrings strings = debug ? StringsDebug : Strings;
            if (strings == null) return 0;

            uint id = Utilities.AnimationHashedString(name);
            if (!strings.Entries.ContainsKey(id)) strings.AddString(name);
            return id;
        }

        /// <summary>
        /// Tag a moment in a clip - a footstep sound, a ragdoll trigger. The property is added to the
        /// clip's first instance block, which is where the game looks for per-use events.
        /// </summary>
        public static AnimClipDBSec.MetadataProperty AddProperty(AnimClipDBSec.MetadataSet set, string name, params float[] times)
        {
            if (set == null) return null;
            AnimClipDBSec.MetadataBlock block = set.Instances.Count != 0 ? set.Instances[0] : set.Common;

            AnimClipDBSec.MetadataProperty property = new AnimClipDBSec.MetadataProperty { Name = name };
            property.Times.AddRange(times);
            block.Properties.Add(property);
            block.HasProperties = true;
            return property;
        }

        /// <summary>
        /// Point a property's occurrence at one of its block's arguments - normally the audio event
        /// that should fire at that time.
        /// </summary>
        public static void SetEvent(AnimClipDBSec.MetadataProperty property, int index, string argument)
        {
            if (property == null || index < 0) return;
            while (property.Events.Count <= index) property.Events.Add(new AnimClipDBSec.MetadataEvent());
            property.Events[index].Name = argument;
            property.HasEvents = true;
        }
        #endregion

        #region STRUCTURES
        /// <summary>A skeleton, in both the shapes the game ships it in.</summary>
        public class SkeletonAsset
        {
            /// <summary>The PAK filename, which is the hash of the skeleton's name.</summary>
            public string Name = "";

            public Skeleton Skeleton;
            public Skeleton Skeleton64;

            /// <summary>Bones come from the 32 bit copy, which is the one the tools read.</summary>
            public List<Skeleton.Bone> Bones { get { return (Skeleton ?? Skeleton64)?.Bones; } }

            public override string ToString() => (Skeleton ?? Skeleton64)?.Name ?? Name;
        }

        /// <summary>Retargeting data between two skeletons.</summary>
        public class MappingAsset
        {
            public string SkeletonA = "";
            public string SkeletonB = "";

            public SkeletonMapping Mapping;
            public SkeletonMapping Mapping64;

            public override string ToString() => SkeletonA + " -> " + SkeletonB;
        }

        /// <summary>A character's physics rig.</summary>
        public class RagdollAsset
        {
            /// <summary>The PAK filename, which is the hash of the skeleton's name.</summary>
            public string Name = "";

            public Ragdoll Ragdoll;
            public Ragdoll Ragdoll64;

            public List<string> Bodies { get { return (Ragdoll ?? Ragdoll64)?.GetBodyNames(); } }

            public override string ToString() => Name + " (" + (Bodies?.Count ?? 0) + " bodies)";
        }

        /// <summary>One clip, tying the name a set plays it by to the Havok animation and its metadata.</summary>
        public class ClipReference
        {
            /// <summary>The name the set plays this clip by, e.g. "aim_centre_down".</summary>
            public string Name = "";

            /// <summary>The authored path the clip was built from, which is what the metadata labels it.</summary>
            public string Path = "";

            /// <summary>The context this clip belongs to, or null if it wasn't reached through a set.</summary>
            public AnimationContext Context;

            /// <summary>The section file holding the animation, or null if it couldn't be resolved.</summary>
            public AnimClipDBSec Section;

            /// <summary>Which clip inside that section. Streamed clips have a section to themselves.</summary>
            public int Index;

            /// <summary>The compressed animation itself, or null if the section didn't resolve.</summary>
            public HavokPackfile.AnimationClip Animation
            {
                get
                {
                    if (_animation != null) return _animation;
                    if (Section?.Havok == null) return null;

                    //the clips come back in section order, so the index out of the DB indexes them directly
                    List<HavokPackfile.AnimationClip> clips = Section.GetAnimations();
                    return _animation = Index >= 0 && Index < clips.Count ? clips[Index] : null;
                }
            }

            /// <summary>The tags on the clip - its label, its length, and any events it fires.</summary>
            public AnimClipDBSec.MetadataSet Metadata
            {
                get
                {
                    if (Section == null || Index < 0 || Index >= Section.Metadata.Count) return null;
                    return Section.Metadata[Index];
                }
            }

            /// <summary>The clip's authored label, from its metadata. Usually the same as <see cref="Path"/>.</summary>
            public string Label { get { return LabelOf(Metadata); } }

            /// <summary>Everything tagged on this clip's timeline, sorted by time.</summary>
        public List<ClipMarker> Markers { get { return _markers ?? (_markers = GetMarkers(this)); } }
        private List<ClipMarker> _markers;

        /// <summary>Whether the clip layers deltas onto another pose rather than holding one itself.</summary>
        public bool Additive { get { return Animation?.Additive ?? false; } }

        /// <summary>Whether the animation resolved and can be sampled.</summary>
            public bool Playable { get { return Animation != null && Animation.FrameCount > 0; } }

            /// <summary>How long the clip runs, in seconds.</summary>
            public float Duration { get { return Animation?.Duration ?? 0; } }

            /// <summary>The skeleton the animation was authored against.</summary>
            public string Skeleton { get { return Animation?.SkeletonName ?? Section?.SkeletonDependencies.FirstOrDefault() ?? ""; } }

            private HavokPackfile.AnimationClip _animation;

            public override string ToString() => Name.Length != 0 ? Name : (Label ?? Path);
        }

        /// <summary>
        /// One thing happening at one moment of a clip - a foot striking the floor, a sound firing,
        /// a ragdoll being handed control. This is <see cref="AnimClipDBSec.MetadataProperty"/>
        /// flattened: one marker per time, with whatever it triggers already resolved.
        /// </summary>
        public class ClipMarker
        {
            /// <summary>The property this came from, e.g. "LeftStrike" or "AUDIO_Foley".</summary>
            public string Property = "";

            /// <summary>When it fires, in seconds from the start of the clip.</summary>
            public float Time;

            /// <summary>Which use of the clip tagged it, or -1 for a tag on the clip itself.</summary>
            public int Instance = -1;

            /// <summary>The block it was found on, if you need at the rest of its arguments.</summary>
            public AnimClipDBSec.MetadataBlock Block;

            /// <summary>What fires - an argument name for a reference, otherwise a literal value.</summary>
            public string Event;

            public MetadataValueType Type = MetadataValueType.PROPERTY_REFERENCE;

            /// <summary>The argument <see cref="Event"/> names, when it names one.</summary>
            public AnimClipDBSec.MetadataArgument Argument;

            /// <summary>The sound this fires, when the argument it points at is an audio one.</summary>
            public AudioEvent Audio;

            /// <summary>Whether this marker plays a sound, as opposed to flagging a moment.</summary>
            public bool IsAudio { get { return Audio != null || Argument?.Type == MetadataValueType.AUDIO; } }

            public override string ToString() => Time.ToString("0.###") + "s " + Property + (Event == null ? "" : " -> " + Event);
        }

        /// <summary>A sound an animation fires: which Wwise event, played from which bone.</summary>
        public class AudioEvent
        {
            /// <summary>The Wwise event name, e.g. "play_ladder_clothing_and_rucksack".</summary>
            public string Event;

            /// <summary>The bone the sound plays from, e.g. "LipsUpper".</summary>
            public string Bone;

            /// <summary>Offset from that bone, as it was stored - "0,0,0" in nearly every case.</summary>
            public string Offset;

            /// <summary>Wwise switches/states passed with the event, usually empty.</summary>
            public string Arguments;

            public bool UsesArguments;

            /// <summary>The original value, in case the parse missed a field.</summary>
            public string Raw;

            public override string ToString() => Event ?? Raw ?? "";
        }

        /// <summary>
        /// Where a clip's pose gets placed.
        ///
        /// Every character rig ends with a `REFERENCE_ROOT` bone: a leaf with nothing skinned to it
        /// and no children, which the clip drives with where the character is meant to be standing.
        /// The engine reads it to move the entity through the world and renders the skeleton around
        /// it. Take the clip's transforms at face value instead and the character is carried off
        /// wherever the animator was working - across 3,260 retail clips that leaves them upright
        /// only 1.7% of the time, because most of them are authored lying along an axis.
        ///
        /// Both settings square the character up by putting the reference bone back where it started,
        /// which is a fixed transform and so costs the animation nothing. They differ only in whether
        /// the body is then allowed to walk away from where it began.
        /// </summary>
        public enum RootMotion
        {
            /// <summary>
            /// Hold the character on the spot: whatever ground it covers since the first frame is
            /// taken back out, leaving it stepping in place. Handy for watching a walk cycle.
            /// </summary>
            Ignore,

            /// <summary>
            /// Let the clip carry the character, starting from the origin. This is the animation
            /// exactly as authored.
            /// </summary>
            Follow,
        }

        /// <summary>What an animation set drives: a skinned character, or a piece of set dressing.</summary>
        public enum AnimationKind
        {
            /// <summary>No skeleton definition names this set, so there's nothing to go on.</summary>
            Unknown,

            /// <summary>A skinned character - the rig is one of the shared reference skeletons.</summary>
            Character,

            /// <summary>A piece of environment geometry - a door, a locker, a fan.</summary>
            Environment,
        }

        /// <summary>
        /// One character or prop's animations, grouped the way the game asks for them: a set of clips
        /// that always apply, plus contexts that swap parts of it out (crouched, holding a shotgun).
        /// </summary>
        public class AnimationSet
        {
            public string Name = "";
            public AnimationKind Kind = AnimationKind.Unknown;

            /// <summary>The skeleton this set's clips are authored against, or "" if none was found.</summary>
            public string Skeleton = "";

            /// <summary>The file this came from, if you need at the blend sets or want to edit it.</summary>
            public AnimClipDB Database;

            /// <summary>The set's own clips first, then one entry per named context.</summary>
            public List<AnimationContext> Contexts = new List<AnimationContext>();

            /// <summary>How many clips the set holds across every context.</summary>
            public int ClipCount;

            public override string ToString() => Name + " (" + ClipCount + " clips)";
        }

        /// <summary>
        /// A group of clips within a set. The unnamed context holds the clips that always apply;
        /// the rest override them while the character is in that state.
        /// </summary>
        public class AnimationContext
        {
            /// <summary>Empty for the set's own clips, otherwise e.g. "CROUCHED" or "WEAPON_HANDGUN".</summary>
            public string Name = "";

            public AnimationSet Set;
            public List<ClipReference> Clips = new List<ClipReference>();

            public override string ToString() => (Name.Length == 0 ? "(default)" : Name) + " (" + Clips.Count + ")";
        }

        /// <summary>
        /// A skeleton definition from DATA/SKELETONDEFS - which reference rig a character uses, what
        /// it can be retargeted from, and which ragdoll it falls into.
        /// </summary>
        public class SkeletonDef
        {
            /// <summary>The skeleton this describes, taken from the filename.</summary>
            public string Name = "";

            public string ReferenceSkeleton = "";
            public string ReferenceSkeletonPath = "";
            public string HiResSkeleton = "";
            public string Ragdoll = "";

            /// <summary>Skeletons whose animation can be retargeted onto this one.</summary>
            public List<string> MapsFrom = new List<string>();

            /// <summary>
            /// Whether this rig drives set dressing rather than a character. The reference skeletons
            /// for characters sit loose at the top of the ReferenceSkeletons folder; everything for
            /// the environment is filed away under it by where the prop lives in the world.
            /// </summary>
            public bool IsEnvironment;

            public override string ToString() => Name + (IsEnvironment ? " (environment)" : " (character)");

            internal static SkeletonDef Read(PAK2.File file)
            {
                XmlNode root;
                try { root = new BML(file.Content).Content?.SelectSingleNode("//SkeletonDef"); }
                catch { return null; }
                if (root == null) return null;

                SkeletonDef def = new SkeletonDef
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(file.Filename),
                    ReferenceSkeleton = root.SelectSingleNode("LoResReferenceSkeleton")?.InnerText ?? "",
                    ReferenceSkeletonPath = root.SelectSingleNode("LoResReferenceSkeletonPath")?.InnerText ?? "",
                    HiResSkeleton = root.SelectSingleNode("HiResReferenceSkeleton")?.InnerText ?? "",
                    Ragdoll = root.SelectSingleNode("ragdoll")?.InnerText ?? "",
                };

                XmlNodeList maps = root.SelectNodes("maps_to_lo_res_required/maps_to_lo_res_required");
                if (maps != null)
                    foreach (XmlNode map in maps)
                    {
                        string skeleton = map.Attributes?["skeleton"]?.Value;
                        if (!string.IsNullOrEmpty(skeleton)) def.MapsFrom.Add(skeleton);
                    }

                const string rootFolder = @"ReferenceSkeletons\";
                int at = def.ReferenceSkeletonPath.IndexOf(rootFolder, StringComparison.OrdinalIgnoreCase);
                def.IsEnvironment = at >= 0 && def.ReferenceSkeletonPath.IndexOf('\\', at + rootFolder.Length) >= 0;
                return def;
            }
        }
        #endregion
    }
}
