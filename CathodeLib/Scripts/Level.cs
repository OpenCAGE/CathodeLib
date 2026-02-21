using CATHODE;
using CATHODE.EXPERIMENTAL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace CathodeLib
{
    public class Global
    {
        public Textures Textures;

        public AnimationStrings AnimationStrings;
        public AnimationStrings AnimationStrings_Debug;

        public Global(string path, PAK2 animPAK)
        {
            Textures = new Textures(path + "\\WORLD\\GLOBAL_TEXTURES.ALL.PAK");
            AnimationStrings = new AnimationStrings(animPAK.Entries.FirstOrDefault(o => o.Filename.Contains("ANIM_STRING_DB.BIN")).Content);
            AnimationStrings_Debug = new AnimationStrings(animPAK.Entries.FirstOrDefault(o => o.Filename.Contains("ANIM_STRING_DB_DEBUG.BIN")).Content);
        }
    }

    /// <summary>
    /// A helper class that holds all parse-able formats for a level, and saves them safely to update indexes across all
    /// </summary>
    public class Level
    {
        public Textures Textures;
        public Shaders Shaders;
        public Collisions WeightedCollisions;
        public MorphTargets MorphTargetDB;
        public Resources Resources;
        public MaterialMappings MaterialMaps;
        public Materials Materials;
        public Models Models;
        public RenderableElements RenderableElements;
        public Movers Movers;
        public EnvironmentMaps EnvironmentMaps;
        public PathBarrierResources PathBarrierResources;
        public CollisionMaps CollisionMaps;
        public RadiosityInstanceMap RadInstanceMap;
        public AlphaLightLevel AlphaLight;
        public CharacterAccessorySets AccessorySets;
        public Commands Commands;
        public EnvironmentAnimations EnvironmentAnimations;
        public Lights Lights;
        public MaterialMappings MaterialMappings;
        public PhysicsMaps PhysicsMaps;
        public SoundNodeNetwork SoundNodeNetwork;
        public SoundBankData SoundBankData;
        public SoundDialogueLookups SoundDialogueLookups;
        public SoundEnvironmentData SoundEnvironmentData;
        public SoundEventData SoundEventData;
        public BehaviorTreeDB BehaviorTreeDB;
        public GalaxyItems GalaxyItems;
        public GalaxyDefinition GalaxyDefinition;

        public class State
        {
            public int Index;
            public NavigationMesh NavMesh;
            public Traversals Traversals;
        }
        public List<State> StateResources = new List<State>(); //State 0 loaded by default

        public Dictionary<string, Dictionary<string, TextDB>> Strings;

        public Global Global => _global;
        private Global _global;

        public string Filepath => _filepath;
        private string _filepath = "";

        public string Name => _name;
        private string _name = "";

        public bool Patched => _patched;
        private bool _patched = false;

        /// <summary>
        /// Triggered every time one of the files within the level loads.
        /// Keep a count of this and divide it by NumberOfTicks to get a loading percentage.
        /// </summary>
        public Action OnLoadTick;

        /// <summary>
        /// Triggered every time one of the files within the level saves.
        /// Keep a count of this and divide it by NumberOfTicks to get a saving percentage.
        /// </summary>
        public Action OnSaveTick;

        public const int NumberOfTicks = 31;

        /// <summary>
        /// A container for data related to a level in the game's "ENV" folder
        /// </summary>
        public Level(string path, Global global, bool loadImmediately = true)
        {
            _global = global;
            _filepath = path.Replace("\\", "/").TrimEnd('/').ToUpper();
            _name = _filepath.Split(new string[] { "DATA/ENV/" }, StringSplitOptions.None)[1];
            _patched = (Name == "PRODUCTION/DLC/BSPNOSTROMO_RIPLEY" || Name == "PRODUCTION/DLC/BSPNOSTROMO_TWOTEAMS") && Directory.Exists(_filepath + "_PATCH");

            if (loadImmediately)
                Load();
        }

        /// <summary>
        /// Load all data for the level
        /// </summary>
        public void Load()
        {
            if (_global?.Textures == null)
                throw new Exception("Missing Global Textures");
            if (_global?.AnimationStrings_Debug == null)
                throw new Exception("Missing Global Animation Strings");

            string renderable = _filepath + "/RENDERABLE/";
            string world = _filepath + (_patched ? "_PATCH" : "") + "/WORLD/";

            Parallel.Invoke(
                () => { Textures = new Textures(renderable + "LEVEL_TEXTURES.ALL.PAK"); OnLoadTick?.Invoke(); },
                () => { Shaders = new Shaders(renderable + "LEVEL_SHADERS_DX11.PAK"); OnLoadTick?.Invoke(); },
                () => { WeightedCollisions = new Collisions(world + "COLLISION.BIN"); OnLoadTick?.Invoke(); },
                () => { MorphTargetDB = new MorphTargets(world + "MORPH_TARGET_DB.BIN"); OnLoadTick?.Invoke(); },
                () => { Resources = new Resources(world + "RESOURCES.BIN"); OnLoadTick?.Invoke(); },
                () => { MaterialMaps = new MaterialMappings(world + "MATERIAL_MAPPINGS.PAK"); OnLoadTick?.Invoke(); }
            );

            Materials = new Materials(renderable + "LEVEL_MODELS.MTL", _global.Textures, Textures, Shaders); OnLoadTick?.Invoke();
            Models = new Models(renderable + "LEVEL_MODELS.PAK", Materials, WeightedCollisions, MorphTargetDB); OnLoadTick?.Invoke();
            RenderableElements = new RenderableElements(world + "REDS.BIN", Models, Materials); OnLoadTick?.Invoke();
            Movers = new Movers(world + "MODELS.MVR", RenderableElements, Resources); OnLoadTick?.Invoke();

            Parallel.Invoke(
                () => { EnvironmentMaps = new EnvironmentMaps(world + "ENVIRONMENTMAP.BIN", Movers); OnLoadTick?.Invoke(); },
                () => { PathBarrierResources = new PathBarrierResources(world + "PATH_BARRIER_RESOURCES", Resources); OnLoadTick?.Invoke(); },
                () => { CollisionMaps = new CollisionMaps(world + "COLLISION.MAP", Materials, MaterialMaps); OnLoadTick?.Invoke(); }
            );

            Parallel.Invoke(
                () => { RadInstanceMap = new RadiosityInstanceMap(renderable + "RADIOSITY_INSTANCE_MAP.TXT"); OnLoadTick?.Invoke(); },
                () => { AlphaLight = new AlphaLightLevel(world + "ALPHALIGHT_LEVEL.BIN"); OnLoadTick?.Invoke(); },
                () => { AccessorySets = new CharacterAccessorySets(world + "CHARACTERACCESSORYSETS.BIN"); OnLoadTick?.Invoke(); },
                () => { EnvironmentAnimations = new EnvironmentAnimations(world + "ENVIRONMENT_ANIMATION.DAT", _global.AnimationStrings_Debug); OnLoadTick?.Invoke(); },
                () => { Lights = new Lights(world + "LIGHTS.BIN"); OnLoadTick?.Invoke(); },
                () => { MaterialMappings = new MaterialMappings(world + "MATERIAL_MAPPINGS.PAK"); OnLoadTick?.Invoke(); },
                () => { PhysicsMaps = new PhysicsMaps(world + "PHYSICS.MAP"); OnLoadTick?.Invoke(); },
                () => { SoundNodeNetwork = new SoundNodeNetwork(world + "SNDNODENETWORK.DAT"); OnLoadTick?.Invoke(); },
                () => { SoundBankData = new SoundBankData(world + "SOUNDBANKDATA.DAT"); OnLoadTick?.Invoke(); },
                () => { SoundDialogueLookups = new SoundDialogueLookups(world + "SOUNDDIALOGUELOOKUPS.DAT"); OnLoadTick?.Invoke(); },
                () => { SoundEnvironmentData = new SoundEnvironmentData(world + "SOUNDENVIRONMENTDATA.DAT"); OnLoadTick?.Invoke(); },
                () => { SoundEventData = new SoundEventData(world + "SOUNDEVENTDATA.DAT"); OnLoadTick?.Invoke(); },
                () => { BehaviorTreeDB = new BehaviorTreeDB(world + "BEHAVIOR_TREE.DB"); OnLoadTick?.Invoke(); }
            );

            Parallel.Invoke(
                () => { GalaxyItems = new GalaxyItems(renderable + "GALAXY/GALAXY.ITEMS_BIN"); OnLoadTick?.Invoke(); },
                () => { GalaxyDefinition = new GalaxyDefinition(renderable + "GALAXY/GALAXY.DEFINITION_BIN"); OnLoadTick?.Invoke(); } //Not used at runtime, but useful to regenerate GalaxyItems.
            );

            Commands = new Commands(world + "COMMANDS" + (File.Exists(world + "COMMANDS.PAK") ? ".PAK" : ".BIN"), EnvironmentAnimations, CollisionMaps, RenderableElements); OnLoadTick?.Invoke();

            //The following files are used by the game, but not handled yet:
            // - RENDERABLE/RADIOSITY_RUNTIME.BIN
            // - WORLD/RADIOSITY_COLLISION_MAPPING.BIN
            // - WORLD/COLLISION.HKX / HKX64
            // - WORLD/PHYSICS.HKX / HKX64
            // - WORLD/OCCLUDER_TRIANGLE_BVH.BIN

            int stateCount = 1; // we always implicitly have one state (the default state: state zero)
            using (BinaryReader reader = new BinaryReader(File.OpenRead(world + "EXCLUSIVE_MASTER_RESOURCE_INDICES")))
            {
                reader.BaseStream.Position = 4;  // version: 1
                int states = reader.ReadInt32(); // number of changeable states
                stateCount += states;
                for (int x = 0; x < states; x++)
                {
                    int resourceIndex = reader.ReadInt32();
                    Resources.Resource resource = Resources.Entries[resourceIndex]; //TODO: this gives you the instance of the ExclusiveMaster entity - use it
                }
            }
            for (int i = 0; i < stateCount; i++)
            {
                string statePath = world + "STATE_" + i + "/";

                State state = new State() { Index = i };
                //ASSAULT_POSITIONS
                //COVER
                //CRAWL_SPACE_SPOTTING_POSITIONS
                state.NavMesh = new NavigationMesh(statePath + "NAV_MESH");
                //SPOTTING_POSITIONS
                state.Traversals = new Traversals(statePath + "TRAVERSAL");
            }
            OnLoadTick?.Invoke();

            string pathDATA = _filepath.Replace('\\', '/').Split(new string[] { "/DATA/ENV" }, StringSplitOptions.None)[0] + "/DATA";
            string levelName = Directory.GetParent(_filepath).Name;
            XmlNodeList textDBsGlobal = new BML(pathDATA + "/LEVEL_TEXT_DATABASES.BML").Content.SelectNodes("//level_text_databases/level");
            List<string> globalDBs = new List<string>();
            for (int i = 0; i < textDBsGlobal.Count; i++)
                if (textDBsGlobal[i].Attributes["name"].Value.ToUpper() == levelName.ToUpper() || textDBsGlobal[i].Attributes["name"].Value == "globals")
                    for (int x = 0; x < textDBsGlobal[i].ChildNodes.Count; x++)
                        globalDBs.Add(textDBsGlobal[i].ChildNodes[x].Attributes["name"].Value);
            List<string> textList = Directory.GetFiles(pathDATA + "/TEXT/", "*.TXT", SearchOption.AllDirectories).ToList<string>();
            List<string> levelDBs = new List<string>();
            if (File.Exists(_filepath + "/TEXT/TEXT_DB_LIST.TXT"))
            {
                string[] textDBsLevel = File.ReadAllLines(_filepath + "/TEXT/TEXT_DB_LIST.TXT");
                for (int i = 0; i < textDBsLevel.Length; i++)
                    levelDBs.Add(textDBsLevel[i]);
                textList.AddRange(Directory.GetFiles(_filepath + "/TEXT/", "*.TXT", SearchOption.AllDirectories));
            }
            textList.Reverse();
            Strings = new Dictionary<string, Dictionary<string, TextDB>>();
            foreach (string textDB in textList)
            {
                string lang = Path.GetFileName(Path.GetDirectoryName(textDB)).ToUpper();
                string db = Path.GetFileNameWithoutExtension(textDB).ToUpper();
                if (!globalDBs.Contains(db) && !levelDBs.Contains(db)) continue;
                if (!Strings.ContainsKey(lang)) Strings.Add(lang, new Dictionary<string, TextDB>());
                if (Strings[lang].ContainsKey(db)) continue;
                Strings[lang].Add(db, new TextDB(textDB));
            }
            OnLoadTick?.Invoke();
        }

        /// <summary>
        /// Save all data for the level
        /// </summary>
        public void Save()
        {
            //TODO: if we haven't pulled GLOBAL texture data into our texture pak, do so, and update sources.

            Parallel.Invoke(
                () => { Textures.Save(); OnSaveTick?.Invoke(); },
                () => { Shaders.Save(); OnSaveTick?.Invoke(); },
                () => { WeightedCollisions.Save(); OnSaveTick?.Invoke(); },
                () => { MorphTargetDB.Save(); OnSaveTick?.Invoke(); },
                () => { MaterialMaps.Save(); OnSaveTick?.Invoke(); }
            );

            Materials.Save(); OnSaveTick?.Invoke();

            Parallel.Invoke(
                () => { Models.Save(); OnSaveTick?.Invoke(); },
                () => { Resources.Save(); OnSaveTick?.Invoke(); }
            );

            RenderableElements.Save(); OnSaveTick?.Invoke();
            Movers.Save(); OnSaveTick?.Invoke();

            Parallel.Invoke(
                () => { EnvironmentMaps.Save(); OnSaveTick?.Invoke(); },
                () => { PathBarrierResources.Save(); OnSaveTick?.Invoke(); },
                () => { CollisionMaps.Save(); OnSaveTick?.Invoke(); },
                () => { RadInstanceMap.Save(); OnSaveTick?.Invoke(); },
                () => { AlphaLight.Save(); OnSaveTick?.Invoke(); },
                () => { AccessorySets.Save(); OnSaveTick?.Invoke(); },
                () => { EnvironmentAnimations.Save(); OnSaveTick?.Invoke(); },
                () => { Lights.Save(); OnSaveTick?.Invoke(); },
                () => { MaterialMappings.Save(); OnSaveTick?.Invoke(); },
                () => { PhysicsMaps.Save(); OnSaveTick?.Invoke(); },
                () => { SoundNodeNetwork.Save(); OnSaveTick?.Invoke(); },
                () => { SoundBankData.Save(); OnSaveTick?.Invoke(); },
                () => { SoundDialogueLookups.Save(); OnSaveTick?.Invoke(); },
                () => { SoundEnvironmentData.Save(); OnSaveTick?.Invoke(); },
                () => { SoundEventData.Save(); OnSaveTick?.Invoke(); },
                () => { BehaviorTreeDB.Save(); OnSaveTick?.Invoke(); }
            );

            Commands.Save(); OnSaveTick?.Invoke();

            Parallel.Invoke(
                () => { GalaxyItems.Save(); OnSaveTick?.Invoke(); },
                () => { GalaxyDefinition.Save(); OnSaveTick?.Invoke(); }
            );

            //TODO: save states
            OnSaveTick?.Invoke();

            //TODO: save strings (?)
            OnSaveTick?.Invoke();
        }

        /// <summary>
        /// Imports resources to the level from Global, making it easier to share around
        /// </summary>
        public void ImportFromGlobal()
        {
            //NOTE: REDS supports referencing models/materials in GLOBAL, but never seems to, so not bothering with that
            //      However, some levels, E.G. Torrens, fail to render after doing this and then clearing global, so something is still pointing there somewhere

            //Import global textures referenced by materials
            for (int i = 0; i < Materials.Entries.Count; i++)
            {
                Materials.Material material = Materials.Entries[i];
                for (int x = 0; x < material.TextureReferences.Count; x++)
                    material.TextureReferences[x]?.RemapToLevel(this);
            }

            //Import global textures referenced by sound flash models
            //for (int i = 0; i < SoundFlashModels.Entries.Count; i++)
            //    SoundFlashModels.Entries[i].Texture?.RemapToLevel(this);
        }

        /// <summary>
        /// Get all levels available within the ENV folder. Pass the path to the folder that contains AI.exe.
        /// </summary>
        public static List<string> GetLevels(string gameDirectory)
        {
            string[] galaxyBins = Directory.GetFiles(gameDirectory + "/DATA/ENV/", "GALAXY.DEFINITION_BIN", SearchOption.AllDirectories);
            List<string> mapList = new List<string>();
            for (int i = 0; i < galaxyBins.Length; i++)
            {
                int extraLength = ("/RENDERABLE/GALAXY/GALAXY.DEFINITION_BIN").Length;
                string mapPath = galaxyBins[i].Substring(0, galaxyBins[i].Length - extraLength);

                //Try match a few files outside of the GALAXY definition, to ensure we are actually a map.
                if (!File.Exists(mapPath + "/WORLD/COMMANDS.PAK") && !File.Exists(mapPath + "/WORLD/COMMANDS.BIN")) continue;
                if (!File.Exists(mapPath + "/WORLD/MODELS.MVR")) continue;
                if (!File.Exists(mapPath + "/RENDERABLE/LEVEL_MODELS.PAK")) continue;
                if (!File.Exists(mapPath + "/RENDERABLE/MODELS_LEVEL.BIN")) continue;

                string[] split = galaxyBins[i].Replace("\\", "/").Split(new[] { "/DATA/ENV/" }, StringSplitOptions.None);
                string file = split[split.Length - 1];
                int length = file.Length - extraLength;
                if (length <= 0) continue;

                mapList.Add(file.Substring(0, length).ToUpper());
            }
            return mapList;
        }
    }
}