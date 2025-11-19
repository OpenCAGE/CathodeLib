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
        public string Name;

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
        public SoundFlashModels SoundFlashModels;
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
        public SoundLoadZones SoundLoadZones;

        public class State
        {
            public int Index;
            public NavigationMesh NavMesh;
            public Traversals Traversals;
        }
        public List<State> StateResources = new List<State>(); //State 0 loaded by default

        public Dictionary<string, Dictionary<string, TextDB>> Strings;

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

        public const int NumberOfTicks = 30;

        private Global _global;
        private string _path;

        /// <summary>
        /// A container for data related to a level in the game's "ENV" folder
        /// </summary>
        public Level(string path, Global global, bool loadImmediately = true)
        {
            _path = path;
            _global = global;

            Name = _path.ToUpper().Replace("\\", "/").Split(new string[] { "DATA/ENV/" }, StringSplitOptions.None)[1].TrimEnd('/');

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

            Parallel.Invoke(
                () => { Textures = new Textures(_path + "/RENDERABLE/LEVEL_TEXTURES.ALL.PAK"); OnLoadTick?.Invoke(); },
                () => { Shaders = new Shaders(_path + "/RENDERABLE/LEVEL_SHADERS_DX11.PAK"); OnLoadTick?.Invoke(); },
                () => { WeightedCollisions = new Collisions(_path + "/WORLD/COLLISION.BIN"); OnLoadTick?.Invoke(); },
                () => { MorphTargetDB = new MorphTargets(_path + "/WORLD/MORPH_TARGET_DB.BIN"); OnLoadTick?.Invoke(); },
                () => { Resources = new Resources(_path + "/WORLD/RESOURCES.BIN"); OnLoadTick?.Invoke(); },
                () => { MaterialMaps = new MaterialMappings(_path + "/WORLD/MATERIAL_MAPPINGS.PAK"); OnLoadTick?.Invoke(); }
            );

            Materials = new Materials(_path + "/RENDERABLE/LEVEL_MODELS.MTL", _global.Textures, Textures, Shaders); OnLoadTick?.Invoke();
            Models = new Models(_path + "/RENDERABLE/LEVEL_MODELS.PAK", Materials, WeightedCollisions, MorphTargetDB); OnLoadTick?.Invoke();
            RenderableElements = new RenderableElements(_path + "/WORLD/REDS.BIN", Models, Materials); OnLoadTick?.Invoke();
            Movers = new Movers(_path + "/WORLD/MODELS.MVR", RenderableElements, Resources); OnLoadTick?.Invoke();

            Parallel.Invoke(
                () => { EnvironmentMaps = new EnvironmentMaps(_path + "/WORLD/ENVIRONMENTMAP.BIN", Movers); OnLoadTick?.Invoke(); },
                () => { PathBarrierResources = new PathBarrierResources(_path + "/WORLD/PATH_BARRIER_RESOURCES", Resources); OnLoadTick?.Invoke(); },
                () => { SoundFlashModels = new SoundFlashModels(_path + "/WORLD/SOUNDFLASHMODELS.DAT", _global.Textures, Textures); OnLoadTick?.Invoke(); },
                () => { CollisionMaps = new CollisionMaps(_path + "/WORLD/COLLISION.MAP", Materials, MaterialMaps); OnLoadTick?.Invoke(); }
            );

            Parallel.Invoke(
                () => { RadInstanceMap = new RadiosityInstanceMap(_path + "/RENDERABLE/RADIOSITY_INSTANCE_MAP.TXT"); OnLoadTick?.Invoke(); },
                () => { AlphaLight = new AlphaLightLevel(_path + "/WORLD/ALPHALIGHT_LEVEL.BIN"); OnLoadTick?.Invoke(); },
                () => { AccessorySets = new CharacterAccessorySets(_path + "/WORLD/CHARACTERACCESSORYSETS.BIN"); OnLoadTick?.Invoke(); },
                () => { EnvironmentAnimations = new EnvironmentAnimations(_path + "/WORLD/ENVIRONMENT_ANIMATION.DAT", _global.AnimationStrings_Debug); OnLoadTick?.Invoke(); },
                () => { Lights = new Lights(_path + "/WORLD/LIGHTS.BIN"); OnLoadTick?.Invoke(); },
                () => { MaterialMappings = new MaterialMappings(_path + "/WORLD/MATERIAL_MAPPINGS.PAK"); OnLoadTick?.Invoke(); },
                () => { PhysicsMaps = new PhysicsMaps(_path + "/WORLD/PHYSICS.MAP"); OnLoadTick?.Invoke(); },
                () => { SoundNodeNetwork = new SoundNodeNetwork(_path + "/WORLD/SNDNODENETWORK.DAT"); OnLoadTick?.Invoke(); },
                () => { SoundBankData = new SoundBankData(_path + "/WORLD/SOUNDBANKDATA.DAT"); OnLoadTick?.Invoke(); },
                () => { SoundDialogueLookups = new SoundDialogueLookups(_path + "/WORLD/SOUNDDIALOGUELOOKUPS.DAT"); OnLoadTick?.Invoke(); },
                () => { SoundEnvironmentData = new SoundEnvironmentData(_path + "/WORLD/SOUNDENVIRONMENTDATA.DAT"); OnLoadTick?.Invoke(); },
                () => { SoundEventData = new SoundEventData(_path + "/WORLD/SOUNDEVENTDATA.DAT"); OnLoadTick?.Invoke(); },
                () => { SoundLoadZones = new SoundLoadZones(_path + "/WORLD/SOUNDLOADZONES.DAT"); OnLoadTick?.Invoke(); }
            );

            Commands = new Commands(_path + "/WORLD/COMMANDS" + (File.Exists(_path + "/WORLD/COMMANDS.PAK") ? ".PAK" : ".BIN"), EnvironmentAnimations, CollisionMaps, PhysicsMaps, RenderableElements); OnLoadTick?.Invoke();

            //RENDERABLE/DAMAGE/*
            //RENDERABLE/GALAXY/*
            //RENDERABLE/RADIOSITY_RUNTIME.BIN
            //WORLD/BEHAVIOR_TREE.DB
            //WORLD/COLLISION.HKX
            //WORLD/COLLISION.HKX64
            //WORLD/CUTSCENE_DIRECTOR_DATA.BIN
            //WORLD/EXCLUSIVE_MASTER_RESOURCE_INDICES (loaded below)
            //WORLD/LEVEL.STR
            //WORLD/OCCLUDER_TRIANGLE_BVH.BIN
            //WORLD/PHYSICS.HKX
            //WORLD/PHYSICS.HKX64
            //WORLD/RADIOSITY_COLLISION_MAPPING.BIN

            int stateCount = 1; // we always implicitly have one state (the default state: state zero)
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_path + "/WORLD/EXCLUSIVE_MASTER_RESOURCE_INDICES")))
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
                string statePath = _path + "/WORLD/STATE_" + i + "/";

                State state = new State() { Index = i };
                //ASSAULT_POSITIONS
                //COVER
                //CRAWL_SPACE_SPOTTING_POSITIONS
                state.NavMesh = new NavigationMesh(statePath + "NAV_MESH");
                //SPOTTING_POSITIONS
                state.Traversals = new Traversals(statePath + "TRAVERSAL");
            }
            OnLoadTick?.Invoke();

            string pathDATA = _path.Replace('\\', '/').Split(new string[] { "/DATA/ENV" }, StringSplitOptions.None)[0] + "/DATA";
            string levelName = Directory.GetParent(_path).Name;
            XmlNodeList textDBsGlobal = new BML(pathDATA + "/LEVEL_TEXT_DATABASES.BML").Content.SelectNodes("//level_text_databases/level");
            List<string> globalDBs = new List<string>();
            for (int i = 0; i < textDBsGlobal.Count; i++)
                if (textDBsGlobal[i].Attributes["name"].Value.ToUpper() == levelName.ToUpper() || textDBsGlobal[i].Attributes["name"].Value == "globals")
                    for (int x = 0; x < textDBsGlobal[i].ChildNodes.Count; x++)
                        globalDBs.Add(textDBsGlobal[i].ChildNodes[x].Attributes["name"].Value);
            List<string> textList = Directory.GetFiles(pathDATA + "/TEXT/", "*.TXT", SearchOption.AllDirectories).ToList<string>();
            List<string> levelDBs = new List<string>();
            if (File.Exists(_path + "/TEXT/TEXT_DB_LIST.TXT"))
            {
                string[] textDBsLevel = File.ReadAllLines(_path + "/TEXT/TEXT_DB_LIST.TXT");
                for (int i = 0; i < textDBsLevel.Length; i++)
                    levelDBs.Add(textDBsLevel[i]);
                textList.AddRange(Directory.GetFiles(_path + "/TEXT/", "*.TXT", SearchOption.AllDirectories));
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
                () => { SoundFlashModels.Save(); OnSaveTick?.Invoke(); },
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
                () => { SoundLoadZones.Save(); OnSaveTick?.Invoke(); }
            );

            Commands.Save(); OnSaveTick?.Invoke();

            //TODO: save states
            OnSaveTick?.Invoke();

            //TODO: save strings (?)
            OnSaveTick?.Invoke();
        }

        /// <summary>
        /// Get all levels available within the ENV folder. Pass the path to the folder that contains AI.exe.
        /// </summary>
        public static List<string> GetLevels(string gameDirectory, bool swapNostromoForPatch = false)
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

                string mapName = file.Substring(0, length).ToUpper();
                if (swapNostromoForPatch && (mapName == "DLC/BSPNOSTROMO_RIPLEY" || mapName == "DLC/BSPNOSTROMO_TWOTEAMS")) mapName += "_PATCH";
                mapList.Add(mapName.ToUpper());
            }
            return mapList;
        }
    }
}