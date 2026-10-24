using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Xml;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#elif GODOT
using Godot;
using System.Numerics;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;
using Vector2 = Godot.Vector2;
using Vector3 = Godot.Vector3;
using Vector4 = Godot.Vector4;
using Color = Godot.Color;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/BEHAVIOR_TREE.DB
    /// </summary>
    public class BehaviorTreeDB : CathodeFile
    {
        public List<string> Entries = new List<string>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE | Implementation.CREATE;

        public BehaviorTreeDB(string path) : base(path) { }
        public BehaviorTreeDB(MemoryStream stream, string path = "") : base(stream, path) { }
        public BehaviorTreeDB(byte[] data, string path = "") : base(data, path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int count = reader.ReadInt32();
                reader.BaseStream.Position += (count * 8) + 4;
                for (int i = 0; i < count; i++)
                {
                    Entries.Add(Utilities.ReadString(reader));
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write((Int64)Entries.Count);
                writer.Write(new byte[8 * Entries.Count]);
                List<int> offsets = new List<int>(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    offsets.Add((int)writer.BaseStream.Position);
                    Utilities.WriteString(Entries[i], writer, true);
                }
                writer.BaseStream.Position = 8;
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write((Int64)offsets[i]);
                }
            }
            return true;
        }
        #endregion

        #region GENERATION
        /// <summary>
        /// The engine picks these itself: the local player runs PlayerBehaviour and a remote one NoBehaviour, whatever their
        /// class says. Both are looked up like any other name, so every level lists them.
        /// </summary>
        public static readonly string[] AlwaysRequired = { "PlayerBehaviour", "NoBehaviour" };

        /// <summary>
        /// The longest tree name the engine can take: it copies the name and ".bml" into a 100 byte buffer.
        /// </summary>
        public const int MaxNameLength = 95;

        /// <summary>
        /// What the character attribute configs ask of every level's behaviour tree list. Shared between callers while the
        /// files it was read from are unchanged: don't modify it.
        /// </summary>
        public class Requirements
        {
            /// <summary>False when the class list or the tree directory could not be read: nothing is known, so nothing should change.</summary>
            public bool Readable = false;
            /// <summary>False when some class could not be resolved: the trees found are needed, but they may not be all of them.</summary>
            public bool Complete = false;
            /// <summary>The root trees the configs need, as spelled in the configs, in the order first needed: classes in ATTRIBUTES.BML order, then <see cref="AlwaysRequired"/>.</summary>
            public List<string> Trees = new List<string>();
            /// <summary>The tree each class runs, by class name (null where it names none, or could not be read).</summary>
            public Dictionary<string, string> ClassTrees = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            /// <summary>What is wrong with the configs, in words a user can act on. Characters of a class listed here crash the game when they spawn.</summary>
            public List<string> Problems = new List<string>();

            //Names _DIRECTORY_CONTENTS.BML holds a File for, and those of them the engine can load a tree from - matched
            //ignoring case, as the engine does
            internal HashSet<string> Present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal HashSet<string> Loadable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Whether a level can list this name: the engine finds and parses a tree for it.</summary>
            public bool IsLoadable(string name)
            {
                return !string.IsNullOrEmpty(name) && name.Length <= MaxNameLength && Loadable.Contains(name);
            }
        }

        /// <summary>
        /// Class names the engine loads another class's file for (compared case-sensitively, as it does).
        /// </summary>
        public static readonly Dictionary<string, string> ClassAliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "HUMAN_FACTION_1", "CIVILIAN" },
            { "HUMAN_FACTION_2", "SECURITY_GUARD" },
        };

        private static readonly object _requirementsLock = new object();
        private static Requirements _cachedRequirements = null;
        private static string _cachedDataPath = null;
        private static List<KeyValuePair<string, string>> _cachedStamps = null;

        /// <summary>
        /// Work out which root trees a level needs from the character attribute configs under <paramref name="pathToData"/>
        /// (the game's DATA folder): the tree each class in CHR_INFO/ATTRIBUTES/ATTRIBUTES.BML runs, following its templates
        /// the way the engine does, plus the two the engine always asks for - keeping only those
        /// DATA/BINARY_BEHAVIOR/_DIRECTORY_CONTENTS.BML can load. The result is cached until one of the files it read changes.
        /// This never throws: configs it can't make sense of come back as not readable, with the reason in Problems.
        /// </summary>
        public static Requirements ReadRequirements(string pathToData)
        {
            //Callers spell the same folder differently (a level's path is upper-cased with forward slashes)
            string dataPath;
            try
            {
                dataPath = Path.GetFullPath(pathToData).TrimEnd('\\', '/');
            }
            catch (Exception e)
            {
                Requirements unusable = new Requirements();
                unusable.Problems.Add("The game's DATA folder (" + pathToData + ") could not be used: " + e.Message);
                return unusable;
            }

            lock (_requirementsLock)
            {
                if (_cachedRequirements != null && string.Equals(_cachedDataPath, dataPath, StringComparison.OrdinalIgnoreCase) && _cachedStamps.All(s => s.Value == Stamp(s.Key)))
                    return _cachedRequirements;

                //Reading them must never stop a level saving: a failure is reported, and not cached, so the next call tries again
                List<KeyValuePair<string, string>> stamps = new List<KeyValuePair<string, string>>();
                Requirements requirements;
                try
                {
                    requirements = ComputeRequirements(dataPath, stamps);
                }
                catch (Exception e)
                {
                    requirements = new Requirements();
                    requirements.Problems.Add("The character configs could not be read: " + e.Message);
                    return requirements;
                }
                _cachedRequirements = requirements;
                _cachedDataPath = dataPath;
                _cachedStamps = stamps;
                return requirements;
            }
        }

        /// <summary>
        /// Make the list what the configs ask for. Entries already listed keep their place, so an unchanged config leaves the
        /// file exactly as it was (retail's order included), and new trees go on the end. An entry that can't load is always
        /// dropped - it would shift every entry after it onto the wrong tree - and one no class needs is dropped too, unless a
        /// class could not be read (it may be the one that needs it). Nothing changes when the configs can't be read at all.
        /// Returns true if <see cref="Entries"/> changed.
        /// </summary>
        public bool Regenerate(Requirements requirements)
        {
            if (requirements == null || !requirements.Readable)
                return false;

            HashSet<string> needed = new HashSet<string>(requirements.Trees, StringComparer.Ordinal);
            HashSet<string> listed = new HashSet<string>(StringComparer.Ordinal);
            List<string> entries = new List<string>();
            foreach (string entry in Entries)
            {
                bool keep = requirements.Complete ? needed.Contains(entry) : requirements.IsLoadable(entry);
                if (keep && listed.Add(entry))
                    entries.Add(entry);
            }
            foreach (string tree in requirements.Trees)
            {
                if (listed.Add(tree))
                    entries.Add(tree);
            }

            //The engine corrupts its heap reading an empty list: leave whatever is there rather than write one
            if (entries.Count == 0 || entries.SequenceEqual(Entries, StringComparer.Ordinal))
                return false;
            Entries = entries;
            return true;
        }

        private static Requirements ComputeRequirements(string pathToData, List<KeyValuePair<string, string>> stamps)
        {
            Requirements requirements = new Requirements();
            string chrInfo = Path.Combine(pathToData, "CHR_INFO");
            string directoryPath = Path.Combine(pathToData, "BINARY_BEHAVIOR", "_DIRECTORY_CONTENTS.BML");
            string classListPath = Path.Combine(chrInfo, "ATTRIBUTES", "ATTRIBUTES.BML");

            //Every file consulted is stamped, missing ones included, so the cache notices one appearing as well as changing.
            //A file that can't be loaded or doesn't hold XML comes back null, like a missing one.
            XmlDocument Read(string path)
            {
                stamps.Add(new KeyValuePair<string, string>(path, Stamp(path)));
                try
                {
                    if (!File.Exists(path)) return null;
                    BML bml = new BML(path);
                    return bml.Loaded ? bml.Content : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            //The trees: the engine finds a name's File by comparing "<name>.bml" ignoring case, first match wins, then parses
            //its Behavior's Node - a File without them fails to load
            XmlElement directory = Read(directoryPath)?["DIR"];
            if (directory == null)
            {
                requirements.Problems.Add("The behaviour tree directory (DATA/BINARY_BEHAVIOR/_DIRECTORY_CONTENTS.BML) could not be read.");
                return requirements;
            }
            //A tree loads from its top node (Behavior/Node/Connector/Node): with none - a tree made empty - it doesn't load. A
            //reference to another tree deeper down that isn't there only drops that branch, but one as the top node takes the
            //whole tree with it, so a top-node reference is only as loadable as what it points at.
            Dictionary<string, string> topReferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlNode node in directory.ChildNodes)
            {
                XmlElement file = node as XmlElement;
                if (file == null || file.Name != "File")
                    continue;
                string fileName = file.GetAttribute("name");
                if (!fileName.EndsWith(".bml", StringComparison.OrdinalIgnoreCase))
                    continue;
                string name = fileName.Substring(0, fileName.Length - 4);
                if (!requirements.Present.Add(name))
                    continue;
                XmlElement top = (file["Behavior"]?["Node"]?["Connector"]?.ChildNodes.OfType<XmlElement>().FirstOrDefault(o => o.Name == "Node"));
                if (top == null)
                    continue;
                if (top.GetAttribute("Class") == "Brainiac.Design.Nodes.ReferencedBehavior")
                    topReferences[name] = Path.GetFileNameWithoutExtension(top.GetAttribute("ReferenceFilename"));
                else
                    requirements.Loadable.Add(name);
            }
            for (bool resolved = true; resolved; )
            {
                resolved = false;
                foreach (KeyValuePair<string, string> reference in topReferences.ToList())
                {
                    if (!requirements.Loadable.Contains(reference.Value))
                        continue;
                    requirements.Loadable.Add(reference.Key);
                    topReferences.Remove(reference.Key);
                    resolved = true;
                }
            }

            //The classes: ATTRIBUTES.BML names each one (the classes Character entities choose from)
            XmlElement classList = Read(classListPath)?["Attributes"];
            if (classList == null)
            {
                requirements.Problems.Add("The character class list (DATA/CHR_INFO/ATTRIBUTES/ATTRIBUTES.BML) could not be read.");
                return requirements;
            }
            List<string> classes = new List<string>();
            foreach (XmlNode node in classList.ChildNodes)
            {
                string className = (node as XmlElement)?["Name"]?.InnerText?.Trim();
                if (!string.IsNullOrEmpty(className))
                    classes.Add(className);
            }

            //The engine loads a class, or a template, by name from CHR_INFO/ATTRIBUTES/<name>.bml (the list's Filename is never
            //read). A class's tree is whatever was assigned last reading its file top to bottom: a Template_Name copies the whole
            //template (its tree included) at that point, and a Behavior_Tree after it overrides that. Only a template that is
            //still being read further up the chain is a loop - the engine copies one reached twice without trouble.
            bool TryResolve(string className, List<string> chain, out string tree, out string failure)
            {
                tree = null;
                failure = null;
                string fileName;
                if (!ClassAliases.TryGetValue(className, out fileName))
                    fileName = className;
                if (chain.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    failure = "its templates inherit from each other in a loop";
                    return false;
                }
                chain.Add(fileName);
                try
                {
                    string path = Path.Combine(chrInfo, "ATTRIBUTES", fileName + ".BML");
                    XmlElement attribute = Read(path)?.DocumentElement;
                    if (attribute == null || attribute.Name != "Attribute")
                    {
                        failure = (File.Exists(path) ? "could not read " : "there is no ") + "ATTRIBUTES/" + fileName + ".BML";
                        return false;
                    }
                    foreach (XmlNode node in attribute.ChildNodes)
                    {
                        XmlElement element = node as XmlElement;
                        if (element == null)
                            continue;
                        if (element.Name == "Template_Name" && !string.IsNullOrWhiteSpace(element.InnerText))
                        {
                            if (!TryResolve(element.InnerText.Trim(), chain, out tree, out failure))
                                return false;
                        }
                        else if (element.Name == "Behavior")
                        {
                            foreach (XmlNode setting in element.ChildNodes)
                            {
                                if (setting is XmlElement && setting.Name == "Behavior_Tree")
                                    tree = setting.InnerText;
                            }
                        }
                    }
                    return true;
                }
                catch (Exception e)
                {
                    //A name no file could have (a path character in a Template_Name, say)
                    failure = "the name '" + fileName + "' can't be a file: " + e.Message;
                    return false;
                }
                finally
                {
                    chain.RemoveAt(chain.Count - 1);
                }
            }

            bool complete = true;
            List<string> trees = new List<string>();
            foreach (string className in classes)
            {
                string tree, failure;
                if (!TryResolve(className, new List<string>(), out tree, out failure))
                {
                    complete = false;
                    requirements.ClassTrees[className] = null;
                    requirements.Problems.Add("The character class " + className + " could not be read (" + failure + "), so its behaviour tree is unknown.");
                    continue;
                }
                requirements.ClassTrees[className] = tree;
                if (string.IsNullOrEmpty(tree))
                    requirements.Problems.Add("The character class " + className + " has no behaviour tree: characters of this class will crash the game when they spawn.");
                else if (!requirements.IsLoadable(tree))
                    requirements.Problems.Add("The character class " + className + " uses the behaviour tree '" + tree + "', which " + WhyNotLoadable(requirements, tree) + ": characters of this class will crash the game when they spawn.");
                else if (!trees.Contains(tree))
                    trees.Add(tree);
            }
            foreach (string tree in AlwaysRequired)
            {
                if (requirements.IsLoadable(tree))
                {
                    if (!trees.Contains(tree))
                        trees.Add(tree);
                }
                //A class already reported for the same missing tree says it all
                else if (!requirements.ClassTrees.Values.Contains(tree, StringComparer.Ordinal))
                    requirements.Problems.Add("The game always asks for the behaviour tree '" + tree + "' itself, but it " + WhyNotLoadable(requirements, tree) + ".");
            }

            requirements.Trees = trees;
            requirements.Complete = complete;
            requirements.Readable = true;
            return requirements;
        }

        private static string WhyNotLoadable(Requirements requirements, string tree)
        {
            if (tree.Length > MaxNameLength)
                return "has a name longer than the game can take (" + MaxNameLength + " characters)";
            if (requirements.Present.Contains(tree))
                return "has no nodes for the game to load (an empty tree, or one that is only a reference to a tree that doesn't load)";
            return "is not in DATA/BINARY_BEHAVIOR/_DIRECTORY_CONTENTS.BML";
        }

        private static string Stamp(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                return info.Exists ? info.Length + ":" + info.LastWriteTimeUtc.Ticks : "missing";
            }
            catch (Exception)
            {
                return "unusable";
            }
        }
        #endregion
    }
}