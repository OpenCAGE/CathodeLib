using CATHODE;
using CATHODE.EXPERIMENTAL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using static CATHODE.Resources;
using static CATHODE.Textures.TEX4;

namespace CathodeLib
{
    public class Global
    {
        public Textures Textures;

        public AnimationStrings AnimationStrings;
        public AnimationStrings AnimationStrings_Debug;
    }

    /// <summary>
    /// A helper class that holds all parse-able formats for a level, and saves them safely to update indexes across all
    /// </summary>
    public class Level
    {
        public Textures Textures;
        public Shaders Shaders;
        public Materials Materials;
        public CollisionMaps CollisionMaps;
        public Collisions WeightedCollisions;
        public MorphTargets MorphTargetDB;
        public Models Models;
        public RenderableElements RenderableElements;
        public Resources Resources;
        public PathBarrierResources PathBarrierResources;
        public Movers Movers;
        public EnvironmentMaps EnvironmentMaps;

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
        public SoundFlashModels SoundFlashModels;
        public SoundLoadZones SoundLoadZones;

        public class State
        {
            public int Index;
            public NavigationMesh NavMesh;
            public Traversals Traversals;
        }
        public List<State> StateResources = new List<State>(); //State 0 loaded by default

        public Dictionary<string, Dictionary<string, TextDB>> Strings;

        private Global Global;

        /// <summary>
        /// Load a level in the game's "ENV" folder
        /// </summary>
        public Level(string path, Global global)
        {
            if (global.Textures == null)
                throw new Exception("Missing Global Textures");
            if (global.AnimationStrings_Debug == null)
                throw new Exception("Missing Global Animation Strings");
            Global = global;

            Textures = new Textures(path + "/RENDERABLE/LEVEL_TEXTURES.ALL.PAK");
            SoundFlashModels = new SoundFlashModels(path + "/WORLD/SOUNDFLASHMODELS.DAT", Global.Textures, Textures);
            Shaders = new Shaders(path + "/RENDERABLE/LEVEL_SHADERS_DX11.PAK");
            Materials = new Materials(path + "/RENDERABLE/LEVEL_MODELS.MTL", Global.Textures, Textures, Shaders);
            CollisionMaps = new CollisionMaps(path + "/WORLD/COLLISION.MAP", Materials);
            WeightedCollisions = new Collisions(path + "/WORLD/COLLISION.BIN");
            MorphTargetDB = new MorphTargets(path + "/WORLD/MORPH_TARGET_DB.BIN");
            Models = new Models(path + "/RENDERABLE/LEVEL_MODELS.PAK", Materials, WeightedCollisions, MorphTargetDB);
            RenderableElements = new RenderableElements(path + "/WORLD/REDS.BIN", Models, Materials);
            Resources = new Resources(path + "/WORLD/RESOURCES.BIN");
            PathBarrierResources = new PathBarrierResources(path + "/WORLD/PATH_BARRIER_RESOURCES", Resources);
            Movers = new Movers(path + "/WORLD/MODELS.MVR", RenderableElements, Resources, Materials);
            EnvironmentMaps = new EnvironmentMaps(path + "/WORLD/ENVIRONMENTMAP.BIN", Movers);

            RadInstanceMap = new RadiosityInstanceMap(path + "/RENDERABLE/RADIOSITY_INSTANCE_MAP.TXT");
            AlphaLight = new AlphaLightLevel(path + "/WORLD/ALPHALIGHT_LEVEL.BIN");
            AccessorySets = new CharacterAccessorySets(path + "/WORLD/CHARACTERACCESSORYSETS.BIN");
            Commands = new Commands(path + "/WORLD/COMMANDS" + (File.Exists(path + "/WORLD/COMMANDS.PAK") ? ".PAK" : ".BIN")); //todo - this also references indexes!
            EnvironmentAnimations = new EnvironmentAnimations(path + "/WORLD/ENVIRONMENT_ANIMATION.DAT", global.AnimationStrings_Debug);
            Lights = new Lights(path + "/WORLD/LIGHTS.BIN");
            MaterialMappings = new MaterialMappings(path + "/WORLD/MATERIAL_MAPPINGS.PAK");
            PhysicsMaps = new PhysicsMaps(path + "/WORLD/PHYSICS.MAP");
            SoundNodeNetwork = new SoundNodeNetwork(path + "/WORLD/SNDNODENETWORK.DAT");
            SoundBankData = new SoundBankData(path + "/WORLD/SOUNDBANKDATA.DAT");
            SoundDialogueLookups = new SoundDialogueLookups(path + "/WORLD/SOUNDDIALOGUELOOKUPS.DAT");
            SoundEnvironmentData = new SoundEnvironmentData(path + "/WORLD/SOUNDENVIRONMENTDATA.DAT");
            SoundEventData = new SoundEventData(path + "/WORLD/SOUNDEVENTDATA.DAT");
            SoundLoadZones = new SoundLoadZones(path + "/WORLD/SOUNDLOADZONES.DAT");

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
            using (BinaryReader reader = new BinaryReader(File.OpenRead(path + "/WORLD/EXCLUSIVE_MASTER_RESOURCE_INDICES")))
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
                string statePath = path + "/WORLD/STATE_" + i + "/";

                State state = new State() { Index = i };
                //ASSAULT_POSITIONS
                //COVER
                //CRAWL_SPACE_SPOTTING_POSITIONS
                state.NavMesh = new NavigationMesh(statePath + "NAV_MESH");
                //SPOTTING_POSITIONS
                state.Traversals = new Traversals(statePath + "TRAVERSAL");
            }

            string pathDATA = path.Replace('\\', '/').Split(new string[] { "/DATA/ENV" }, StringSplitOptions.None)[0] + "/DATA";
            string levelName = Directory.GetParent(path).Name;
            XmlNodeList textDBsGlobal = new BML(pathDATA + "/LEVEL_TEXT_DATABASES.BML").Content.SelectNodes("//level_text_databases/level");
            List<string> globalDBs = new List<string>();
            for (int i = 0; i < textDBsGlobal.Count; i++)
                if (textDBsGlobal[i].Attributes["name"].Value.ToUpper() == levelName.ToUpper() || textDBsGlobal[i].Attributes["name"].Value == "globals")
                    for (int x = 0; x < textDBsGlobal[i].ChildNodes.Count; x++)
                        globalDBs.Add(textDBsGlobal[i].ChildNodes[x].Attributes["name"].Value);
            List<string> textList = Directory.GetFiles(pathDATA + "/TEXT/", "*.TXT", SearchOption.AllDirectories).ToList<string>();
            List<string> levelDBs = new List<string>();
            if (File.Exists(path + "/TEXT/TEXT_DB_LIST.TXT"))
            {
                string[] textDBsLevel = File.ReadAllLines(path + "/TEXT/TEXT_DB_LIST.TXT");
                for (int i = 0; i < textDBsLevel.Length; i++)
                    levelDBs.Add(textDBsLevel[i]);
                textList.AddRange(Directory.GetFiles(path + "/TEXT/", "*.TXT", SearchOption.AllDirectories));
            }
            textList.Reverse();
            Strings = new Dictionary<string, Dictionary<string, TextDB>>();
            foreach (string textDB in textList)
            {
                string lang = Path.GetFileName(Path.GetDirectoryName(textDB));
                string db = Path.GetFileNameWithoutExtension(textDB);
                if (!globalDBs.Contains(db) && !levelDBs.Contains(db)) continue;
                if (!Strings.ContainsKey(lang)) Strings.Add(lang, new Dictionary<string, TextDB>());
                if (Strings[lang].ContainsKey(db)) continue;
                Strings[lang].Add(db, new TextDB(textDB));
            }
        }

        /* Save all modifications to the level - this currently assumes we aren't editing GLOBAL data */
        public void Save()
        {
            Textures.Save();
            SoundFlashModels.Save();
            Shaders.Save();
            Materials.Save();
            CollisionMaps.Save();
            WeightedCollisions.Save();
            MorphTargetDB.Save();
            Models.Save();
            RenderableElements.Save();
            Resources.Save();
            //PathBarrierResources.Save();
            Movers.Save();
            EnvironmentMaps.Save();

            RadInstanceMap.Save();
            AlphaLight.Save();
            AccessorySets.Save();
            Commands.Save();
            EnvironmentAnimations.Save();
            Lights.Save();
            MaterialMappings.Save();
            PhysicsMaps.Save();
            SoundNodeNetwork.Save();
            SoundBankData.Save();
            SoundDialogueLookups.Save();
            SoundEnvironmentData.Save();
            SoundEventData.Save();
            SoundLoadZones.Save();
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
