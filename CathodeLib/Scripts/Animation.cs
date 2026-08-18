using CATHODE;
using CATHODE.Animations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

            SkeletonIndex = LoadOne(Find(@"SKELE\DB.BIN"), x => new SkeletonDB(x.Content, Strings, x.Filename));
            ClipIndex = LoadOne(Find(@"ANIM_SYS\ANIM_CLIP_DB.BIN"), x => new GlobalAnimClipDB(x.Content, Strings, x.Filename, StringsDebug));

            foreach (PAK2.File file in _pak.Entries)
            {
                string name = (file.Filename ?? "").ToUpperInvariant();
                if (file.Content == null) continue;

                if (name.Contains("ANIM_CLIP_DB_SEC_"))
                    Add(Sections, file, x => new AnimClipDBSec(x.Content, Strings, x.Filename, StringsDebug));
                else if (name.EndsWith("_ANIM_CLIP_DB.BIN"))
                    Add(ClipDatabases, file, x => new AnimClipDB(x.Content, Strings, x.Filename));
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

        /// <summary>Every clip in the PAK, with the section it lives in.</summary>
        public List<ClipReference> GetClips()
        {
            List<ClipReference> clips = new List<ClipReference>();
            foreach (AnimClipDBSec section in Sections)
            {
                List<HavokPackfile.AnimationClip> animations = section.GetAnimations();
                for (int i = 0; i < animations.Count; i++)
                {
                    ClipReference clip = new ClipReference
                    {
                        Section = section,
                        Animation = animations[i],
                        Metadata = i < section.Metadata.Count ? section.Metadata[i] : null,
                    };
                    clips.Add(clip);
                }
            }
            return clips;
        }

        /// <summary>
        /// Every clip authored against a skeleton. Cheaper than <see cref="GetClips"/> when you only
        /// care about one character, because it skips sections that don't name the skeleton.
        /// </summary>
        public List<ClipReference> GetClips(string skeleton)
        {
            List<ClipReference> clips = new List<ClipReference>();
            foreach (AnimClipDBSec section in Sections)
            {
                if (!section.SkeletonDependencies.Any(x => string.Equals(x, skeleton, StringComparison.OrdinalIgnoreCase)))
                    continue;
                List<HavokPackfile.AnimationClip> animations = section.GetAnimations();
                for (int i = 0; i < animations.Count; i++)
                {
                    if (!string.Equals(animations[i].SkeletonName, skeleton, StringComparison.OrdinalIgnoreCase)) continue;
                    clips.Add(new ClipReference
                    {
                        Section = section,
                        Animation = animations[i],
                        Metadata = i < section.Metadata.Count ? section.Metadata[i] : null,
                    });
                }
            }
            return clips;
        }

        /// <summary>
        /// Find clips by their authored label, e.g. "…\SHOTGUN\RELOAD_OUT". Matches on any part of it.
        /// </summary>
        public List<ClipReference> FindClips(string label)
        {
            List<ClipReference> found = new List<ClipReference>();
            foreach (AnimClipDBSec section in Sections)
            {
                for (int i = 0; i < section.Metadata.Count; i++)
                {
                    string clipLabel = LabelOf(section.Metadata[i]);
                    if (clipLabel == null || clipLabel.IndexOf(label, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    found.Add(new ClipReference { Section = section, Metadata = section.Metadata[i] });
                }
            }
            return found;
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

        /// <summary>One clip, tying the Havok animation to the metadata describing it.</summary>
        public class ClipReference
        {
            public AnimClipDBSec Section;
            public HavokPackfile.AnimationClip Animation;
            public AnimClipDBSec.MetadataSet Metadata;

            public string Label { get { return LabelOf(Metadata); } }

            public override string ToString()
            {
                string label = Label;
                return label ?? (Animation?.ToString() ?? "clip");
            }
        }
        #endregion
    }
}
