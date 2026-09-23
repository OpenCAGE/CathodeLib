using CATHODE;
using CATHODE.EXPERIMENTAL;
using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace CathodeLib
{
    public class Global
    {
        public Textures Textures;
        public Animation Animations;

        public AnimationStrings AnimationStrings { get { return Animations?.Strings; } }
        public AnimationStrings AnimationStrings_Debug { get { return Animations?.StringsDebug; } }
        public SkeletonDB Skeletons { get { return Animations?.SkeletonIndex; } }

        public Global(string path)
        {
            string root = (path ?? "").TrimEnd('\\', '/') + "\\";
            Textures = new Textures(root + "WORLD\\GLOBAL_TEXTURES.ALL.PAK");
            if (File.Exists(root + "..\\..\\GLOBAL\\ANIMATION_SWITCH.PAK"))
                Animations = new Animation(new PAK2(root + "..\\..\\GLOBAL\\ANIMATION_SWITCH.PAK"), false);
            else if (File.Exists(root + "..\\..\\GLOBAL\\ANIMATION.PAK"))
                Animations = new Animation(new PAK2(root + "..\\..\\GLOBAL\\ANIMATION.PAK"), false);
        }

        ~Global()
        {
            Textures = null;
            Animations = null;
        }

        /// <summary>
        /// A skeleton's bones, from the animation PAK. Returns null if it isn't there.
        /// </summary>
        public Skeleton GetSkeleton(SkeletonDB.SkeletonEntry skeleton)
        {
            Skeleton loaded = Animations?.GetSkeleton(skeleton)?.Skeleton;
            return loaded != null && loaded.Loaded ? loaded : null;
        }

        /// <summary>
        /// Load a skeleton by name, e.g. "MALE" or "ALIEN".
        /// </summary>
        public Skeleton GetSkeleton(string name)
        {
            Skeleton loaded = Animations?.GetSkeleton(name)?.Skeleton;
            return loaded != null && loaded.Loaded ? loaded : null;
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
        public Materials Materials;
        public Models Models;
        public RenderableElements RenderableElements;
        public Movers Movers;
        public PathBarrierResources PathBarrierResources;
        public HavokPackfile CollisionHKX;
        public HavokPackfile CollisionHKX64;
        public HavokPackfile PhysicsHKX;
        public HavokPackfile PhysicsHKX64;
        public CollisionMaps CollisionMaps;
#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
        public RadiosityInstanceMap RadiosityInstanceMap;
        public RadiosityCollisionMap RadiosityCollisionMap;
        public RadiosityRuntime RadiosityRuntime;
#endif
        public AlphaLightLevel AlphaLight;
        public CharacterAccessorySets AccessorySets;
        public Commands Commands;
        public EnvironmentAnimations EnvironmentAnimations;
        public Lights Lights;
        public OccluderTriangleBVH OccluderTriangleBVH;
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

        //Helpful accessors: default to 32-bit Havok data, fall back to 64-bit if not present
        public HavokPackfile Collision => CollisionHKX ?? CollisionHKX64;
        public HavokPackfile Physics => PhysicsHKX ?? PhysicsHKX64;

        public class State
        {
            //The entity that defines this state (invalid if state 0, as that's the default)
            public Entity ExclusiveMaster = null;
            public ShortGuid CompositeInstanceId = ShortGuid.Invalid;
            public Resources.Resource Resource = null;

            //Generated resources within this state
            public Cover Cover;
            public NavigationMesh NavMesh;
            public SpottingPositions SpottingPositions;
            public SpottingPositions CrawlSpaceSpottingPositions;
            public AssaultPositions AssaultPositions;

            ~State()
            {
                ExclusiveMaster = null;
                Resource = null;

                Cover = null;
                NavMesh = null;
                SpottingPositions = null;
                CrawlSpaceSpottingPositions = null;
                AssaultPositions = null;
            }
        }
        public List<State> StateResources = new List<State>(); //State 0 loaded by default

        public Dictionary<string, Dictionary<string, TextDB>> Strings;

        public Global Global => _global;

        /// <summary>
        /// Whether <see cref="Load"/> and <see cref="Save"/> copy the global textures this level's
        /// materials reference into its own texture pak (see <see cref="ImportFromGlobal"/>). On by
        /// default, and that is what makes a level self-contained. A level that exists only as a
        /// container for a few composites - the scratch level a composite archive is built in - turns
        /// it off, because absorbing the global set puts ~80MB of textures nothing in it references
        /// into a pak that is meant to carry only what was ported.
        /// </summary>
        public bool AbsorbGlobalTextures { get; set; } = true;
        private Global _global;

        public string Filepath => _filepath;
        private string _filepath = "";

        public string Name => _name;
        private string _name = "";

        public bool Patched => _patched;
        private bool _patched = false;

        /// <summary>
        /// The script file <see cref="Load"/> will read for this level, resolved without loading anything:
        /// the PAK where one ships, else the BIN (gzipped on builds that compress their level data).
        /// </summary>
        public string CommandsFilepath
        {
            get
            {
                string world = _filepath + (_patched ? "_PATCH" : "") + "/WORLD/";
                if (File.Exists(_filepath + "/RENDERABLE/LEVEL_TEXTURES.ALL.PAK.FZIP"))
                    return world + "COMMANDS.BIN.GZ";
                return File.Exists(world + "COMMANDS.PAK") ? world + "COMMANDS.PAK" : world + "COMMANDS.BIN";
            }
        }

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

        public const int NumberOfTicks = 35;

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

        ~Level()
        {
            Materials?.ClearReferences();
            Models?.ClearReferences();
            RenderableElements?.ClearReferences();
            Movers?.ClearReferences();
            PathBarrierResources?.ClearReferences();
            CollisionMaps?.ClearReferences();
            PhysicsMaps?.ClearReferences();
            Commands?.ClearReferences();
            EnvironmentAnimations?.ClearReferences();

            Textures = null;
            Shaders = null;
            WeightedCollisions = null;
            MorphTargetDB = null;
            Resources = null;
            Materials = null;
            Models = null;
            RenderableElements = null;
            Movers = null;
            PathBarrierResources = null;
            CollisionHKX = null;
            CollisionHKX64 = null;
            PhysicsHKX = null;
            PhysicsHKX64 = null;
            CollisionMaps = null;
#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
            RadiosityInstanceMap = null;
            RadiosityCollisionMap = null;
            RadiosityRuntime = null;
#endif
            AlphaLight = null;
            AccessorySets = null;
            Commands = null;
            EnvironmentAnimations = null;
            Lights = null;
            OccluderTriangleBVH = null;
            MaterialMappings = null;
            PhysicsMaps = null;
            SoundNodeNetwork = null;
            SoundBankData = null;
            SoundDialogueLookups = null;
            SoundEnvironmentData = null;
            SoundEventData = null;
            BehaviorTreeDB = null;
            GalaxyItems = null;
            GalaxyDefinition = null;

            _global = null;

            StateResources?.Clear();
            StateResources = null;
            Strings?.Clear();
            Strings = null;
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

            bool compressed = File.Exists(renderable + "LEVEL_TEXTURES.ALL.PAK.FZIP");

            Parallel.Invoke(
                () => { Textures = new Textures(renderable + "LEVEL_TEXTURES.ALL.PAK" + (compressed ? ".FZIP" : "")); OnLoadTick?.Invoke(); },
                () => { Shaders = new Shaders(renderable + "LEVEL_SHADERS_DX11.PAK" + (compressed ? ".GZ" : "")); OnLoadTick?.Invoke(); },
                () => { WeightedCollisions = new Collisions(world + "COLLISION.BIN" + (compressed ? ".GZ" : "")); OnLoadTick?.Invoke(); },
                () => { MorphTargetDB = new MorphTargets(world + "MORPH_TARGET_DB.BIN"); OnLoadTick?.Invoke(); },
                () => { Resources = new Resources(world + "RESOURCES.BIN"); OnLoadTick?.Invoke(); },
                () => { MaterialMappings = new MaterialMappings(world + "MATERIAL_MAPPINGS.PAK"); OnLoadTick?.Invoke(); }
            );

            Materials = new Materials(renderable + "LEVEL_MODELS.MTL" + (compressed ? ".GZ" : ""), _global.Textures, Textures, Shaders); OnLoadTick?.Invoke();
            Models = new Models(renderable + "LEVEL_MODELS.PAK" + (compressed ? ".FZIP" : ""), Materials, WeightedCollisions, MorphTargetDB); OnLoadTick?.Invoke();
            RenderableElements = new RenderableElements(world + "REDS.BIN" + (compressed ? ".GZ" : ""), Models, Materials); OnLoadTick?.Invoke();
            Movers = new Movers(world + "MODELS.MVR" + (compressed ? ".GZ" : ""), RenderableElements, Resources, Textures); OnLoadTick?.Invoke();

            Parallel.Invoke(
                () =>
                {                    
                    if (File.Exists(world + "COLLISION.HKX"))
                    {
                        CollisionHKX = new HavokPackfile(world + "COLLISION.HKX");
                        if (!CollisionHKX.Loaded)
                            CollisionHKX = null;
                    }
                    OnLoadTick?.Invoke();
                },
                () =>
                {
                    string path = Havok64Path(world, "COLLISION");
                    if (path != null)
                    {
                        CollisionHKX64 = new HavokPackfile(path);
                        if (!CollisionHKX64.Loaded)
                            CollisionHKX64 = null;
                    }
                    OnLoadTick?.Invoke();
                },
                () =>
                {
                    if (File.Exists(world + "PHYSICS.HKX"))
                    {
                        PhysicsHKX = new HavokPackfile(world + "PHYSICS.HKX");
                        if (!PhysicsHKX.Loaded)
                            PhysicsHKX = null;
                    }
                    OnLoadTick?.Invoke();
                },
                () =>
                {
                    string path = Havok64Path(world, "PHYSICS");
                    if (path != null)
                    {
                        PhysicsHKX64 = new HavokPackfile(path);
                        if (!PhysicsHKX64.Loaded)
                            PhysicsHKX64 = null;
                    }
                    OnLoadTick?.Invoke();
                }
            );

            Parallel.Invoke(
                () => { PathBarrierResources = new PathBarrierResources(world + "PATH_BARRIER_RESOURCES", Resources); OnLoadTick?.Invoke(); },
                () => { CollisionMaps = new CollisionMaps(world + "COLLISION.MAP" + (compressed ? ".GZ" : ""), Materials, MaterialMappings, CollisionHKX ?? CollisionHKX64); OnLoadTick?.Invoke(); }
            );

            Parallel.Invoke(
#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
                () => { RadiosityRuntime = new RadiosityRuntime(File.Exists(renderable + "RADIOSITY_RUNTIME.BIN.GZ") ? renderable + "RADIOSITY_RUNTIME.BIN.GZ" : renderable + "RADIOSITY_RUNTIME.BIN", Resources); OnLoadTick?.Invoke(); },
                () => { RadiosityInstanceMap = new RadiosityInstanceMap(renderable + "RADIOSITY_INSTANCE_MAP.TXT", Resources); OnLoadTick?.Invoke(); },
                () => { RadiosityCollisionMap = new RadiosityCollisionMap(world + "RADIOSITY_COLLISION_MAPPING.BIN"); OnLoadTick?.Invoke(); },
#endif
                () => { AlphaLight = new AlphaLightLevel(world + "ALPHALIGHT_LEVEL.BIN"); OnLoadTick?.Invoke(); },
                () => { AccessorySets = new CharacterAccessorySets(world + "CHARACTERACCESSORYSETS.BIN"); OnLoadTick?.Invoke(); },
                () => { EnvironmentAnimations = new EnvironmentAnimations(world + "ENVIRONMENT_ANIMATION.DAT", _global.AnimationStrings_Debug); OnLoadTick?.Invoke(); },
                () => { Lights = new Lights(world + "LIGHTS.BIN"); OnLoadTick?.Invoke(); },
                () => { OccluderTriangleBVH = new OccluderTriangleBVH(world + "OCCLUDER_TRIANGLE_BVH.BIN"); OnLoadTick?.Invoke(); },
                () => { PhysicsMaps = new PhysicsMaps(world + "PHYSICS.MAP", PhysicsHKX); OnLoadTick?.Invoke(); },
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

            Commands = new Commands(world + "COMMANDS" + (compressed ? ".BIN.GZ" : File.Exists(world + "COMMANDS.PAK") ? ".PAK" : ".BIN"), EnvironmentAnimations, CollisionMaps, RenderableElements, Physics, Textures, _global?.Textures); OnLoadTick?.Invoke();
            RefreshEnvironmentMapIndexing();

            StateResources.Add(new State());
            using (BinaryReader reader = new BinaryReader(File.OpenRead(world + "EXCLUSIVE_MASTER_RESOURCE_INDICES")))
            {
                reader.BaseStream.Position = 4;
                int states = reader.ReadInt32(); 
                for (int i = 0; i < states; i++)
                {
                    int resourceIndex = reader.ReadInt32();
                    Resources.Resource resource = Resources.Entries[resourceIndex];
                    StateResources.Add(new State()
                    {
                        Resource = resource,
                        CompositeInstanceId = resource.composite_instance_id,
                        ExclusiveMaster = FindExclusiveMaster(resource.resource_id)
                    });
                }
            }
            ReloadStateResources();
            OnLoadTick?.Invoke();

            string pathDATA = _filepath.Replace('\\', '/').Split(new string[] { "/DATA/ENV" }, StringSplitOptions.None)[0] + "/DATA";
            Strings = new Dictionary<string, Dictionary<string, TextDB>>();
            if (File.Exists(pathDATA + "/LEVEL_TEXT_DATABASES.XML"))
            {
                //The config names a level by its own folder ("BSP_Torrens"), not the folder that holds it -
                //taking the parent asked for a level called "PRODUCTION" and matched nothing. The name and
                //the database names are both compared case-insensitively: the config's casing
                //("globals", "Tutorials") is not the files' ("GLOBALS.TXT"), and an exact compare against
                //the upper-cased filename let through only the databases the config happens to spell in
                //capitals - BSP_Torrens loaded 1 of its 24 databases (T0001) before this.
                string levelName = Path.GetFileName(_filepath);
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(File.ReadAllText(pathDATA + "/LEVEL_TEXT_DATABASES.XML"));
                XmlNodeList textDBsGlobal = doc.SelectNodes("//level_text_databases/level");
                HashSet<string> globalDBs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < textDBsGlobal.Count; i++)
                    if (string.Equals(textDBsGlobal[i].Attributes["name"]?.Value, levelName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(textDBsGlobal[i].Attributes["name"]?.Value, "globals", StringComparison.OrdinalIgnoreCase))
                        for (int x = 0; x < textDBsGlobal[i].ChildNodes.Count; x++)
                            if (textDBsGlobal[i].ChildNodes[x].Attributes?["name"]?.Value != null)
                                globalDBs.Add(textDBsGlobal[i].ChildNodes[x].Attributes["name"].Value);
                List<string> textList = Directory.GetFiles(pathDATA + "/TEXT/", "*.TXT", SearchOption.AllDirectories).ToList<string>();
                HashSet<string> levelDBs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(_filepath + "/TEXT/TEXT_DB_LIST.TXT"))
                {
                    string[] textDBsLevel = File.ReadAllLines(_filepath + "/TEXT/TEXT_DB_LIST.TXT");
                    for (int i = 0; i < textDBsLevel.Length; i++)
                        if (!string.IsNullOrWhiteSpace(textDBsLevel[i]))
                            levelDBs.Add(textDBsLevel[i].Trim());
                    textList.AddRange(Directory.GetFiles(_filepath + "/TEXT/", "*.TXT", SearchOption.AllDirectories));
                }
                textList.Reverse();
                foreach (string textDB in textList)
                {
                    string lang = Path.GetFileName(Path.GetDirectoryName(textDB)).ToUpper();
                    string db = Path.GetFileNameWithoutExtension(textDB).ToUpper();
                    if (!globalDBs.Contains(db) && !levelDBs.Contains(db)) continue;
                    if (!Strings.ContainsKey(lang)) Strings.Add(lang, new Dictionary<string, TextDB>());
                    if (Strings[lang].ContainsKey(db)) continue;
                    Strings[lang].Add(db, new TextDB(textDB));
                }
            }

            ImportFromGlobal();
            OnLoadTick?.Invoke();
        }

#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
        /// <summary>
        /// Perform a full instanced save, complete with radiosity, cover, navmesh, etc.
        /// </summary>
        public Instancing SaveInstanced()
        {
            return SaveInstanced(new NavMesh.NavMeshBakeSettings(), new NavMesh.CoverBakeSettings(), new Radiosity.RadiosityBakeSettings(), new NavMesh.JobPositionBakeSettings(), new Alphalight.AlphalightBakeSettings(), new Sound.SoundNetworkBakeSettings());
        }

        /// <summary>
        /// Generate instanced structures for the level, and save.
        /// Pass settings here for the various bakers - leaving them null will skip.
        /// </summary>
        /// <returns>The pass that ran, so a caller can read <see cref="Instancing.BakeWarnings"/>.</returns>
        public Instancing SaveInstanced(NavMesh.NavMeshBakeSettings navMeshSettings, NavMesh.CoverBakeSettings coverSettings, Radiosity.RadiosityBakeSettings radiositySettings, NavMesh.JobPositionBakeSettings jobPositionSettings, Alphalight.AlphalightBakeSettings alphalightSettings, Sound.SoundNetworkBakeSettings soundSettings)
        {
            //Generate instancing data with the given settings
            Instancing instancing = new Instancing(this, navMeshSettings, coverSettings, radiositySettings, jobPositionSettings, alphalightSettings, soundSettings);
            Save();

            //If the user didn't enable radiosity, we should clear it out, else it'll point to the wrong movers.
            //The same when the bake found nothing to light: the files were blanked, and the objects in memory
            //describe nothing.
            if (radiositySettings == null || instancing.RadiosityCleared)
            {
                Utilities.ClearRadiosityOnDisk(this);
            }
            else
            {
                Parallel.Invoke(
                    () => { RadiosityInstanceMap.Save(); OnSaveTick?.Invoke(); },
                    () => { RadiosityCollisionMap.Save(); OnSaveTick?.Invoke(); },
                    () => { RadiosityRuntime?.Save(); OnSaveTick?.Invoke(); }
                );
            }

            return instancing;
        }
#endif

        /// <summary>
        /// Give Movers the script's EnvironmentMap ranking, which is what WORLD/ENVIRONMENTMAP.BIN
        /// indexes and which only Commands can derive. Run once the script has loaded, to resolve
        /// the loaded rows to cubemaps, and again just before the movers are written, so the rows
        /// reflect the texture table as it is being saved (ImportFromGlobal can grow it).
        /// </summary>
        private void RefreshEnvironmentMapIndexing()
        {
            if (Commands == null || Movers == null)
                return;
            Commands.BuildEnvironmentMapIndexing(out List<Textures.TEX4> indexToTexture, out Dictionary<Textures.TEX4, int> textureToIndex);
            Movers.SetEnvironmentMapIndexing(indexToTexture, textureToIndex);
        }

        /// <summary>
        /// Re-read every state's generated navigation data - cover, navmesh, spotting and assault
        /// positions - from disk. A save rewrites those files (an instanced one regenerates them), so
        /// anything still holding a Level that was loaded before it is looking at the old ones until
        /// this is called. The states themselves are not re-read: their number and what defines them
        /// comes from the commands, which a caller in that position has its own copy of.
        /// </summary>
        public void ReloadStateResources()
        {
            string world = _filepath + (_patched ? "_PATCH" : "") + "/WORLD/";
            for (int i = 0; i < StateResources.Count; i++)
            {
                string statePath = world + "STATE_" + i + "/";

                StateResources[i].Cover = new Cover(statePath + "COVER");
                StateResources[i].NavMesh = new NavigationMesh(statePath + "NAV_MESH");
                StateResources[i].SpottingPositions = new SpottingPositions(statePath + "SPOTTING_POSITIONS");
                StateResources[i].CrawlSpaceSpottingPositions = new SpottingPositions(statePath + "CRAWL_SPACE_SPOTTING_POSITIONS");
                StateResources[i].AssaultPositions = new AssaultPositions(statePath + "ASSAULT_POSITIONS");
            }
        }

        /// <summary>
        /// Save all data for the level
        /// </summary>
        public void Save()
        {
            //OpenCAGE never modifies Global - but since people might, re-run the global importer again.
            ImportFromGlobal();

            string renderable = _filepath + "/RENDERABLE/";
            string world = _filepath + (_patched ? "_PATCH" : "") + "/WORLD/";

            Parallel.Invoke(
                () => { Textures.Save(); OnSaveTick?.Invoke(); },
                () => { Shaders.Save(); OnSaveTick?.Invoke(); },
                () => { WeightedCollisions.Save(); OnSaveTick?.Invoke(); },
                () => { MorphTargetDB.Save(); OnSaveTick?.Invoke(); },
                () => { MaterialMappings.Save(); OnSaveTick?.Invoke(); }
            );

            Materials.Save(); OnSaveTick?.Invoke();

            Parallel.Invoke(
                () => { Models.Save(); OnSaveTick?.Invoke(); },
                () => { Resources.Save(); OnSaveTick?.Invoke(); }
            );

            RenderableElements.Save(); OnSaveTick?.Invoke();
            RefreshEnvironmentMapIndexing();
            Movers.Save(); OnSaveTick?.Invoke();

            Parallel.Invoke(
                () => { PathBarrierResources.Save(); OnSaveTick?.Invoke(); },
                () => { CollisionHKX?.Save(); OnSaveTick?.Invoke(); },
                () => { CollisionHKX64?.Save(); OnSaveTick?.Invoke(); },
                () => { PhysicsHKX?.Save(); OnSaveTick?.Invoke(); },
                () => { PhysicsHKX64?.Save(); OnSaveTick?.Invoke(); },
                () => { CollisionMaps.Save(); OnSaveTick?.Invoke(); },
                () => { AlphaLight.Save(); OnSaveTick?.Invoke(); },
                () => { AccessorySets.Save(); OnSaveTick?.Invoke(); },
                () => { EnvironmentAnimations.Save(); OnSaveTick?.Invoke(); },
                () => { Lights.Save(); OnSaveTick?.Invoke(); },
                () => { OccluderTriangleBVH?.Save(); OnSaveTick?.Invoke(); },
                () => { PhysicsMaps.Save(); OnSaveTick?.Invoke(); },
                () => { SoundNodeNetwork.Save(); OnSaveTick?.Invoke(); },
                () => { SoundBankData.Save(); OnSaveTick?.Invoke(); },
                () => { SoundDialogueLookups.Save(); OnSaveTick?.Invoke(); },
                () => { SoundEnvironmentData.Save(); OnSaveTick?.Invoke(); },
                () => { SoundEventData.Save(); OnSaveTick?.Invoke(); },
                () => { BehaviorTreeDB.Save(); OnSaveTick?.Invoke(); }
            );

            Commands.Save(); OnSaveTick?.Invoke();

            //NOTE - We do not re-save radiosity here. Radiosity is only handled when using SaveInstanced.

            Parallel.Invoke(
                () => { GalaxyItems.Save(); OnSaveTick?.Invoke(); },
                () => { GalaxyDefinition.Save(); OnSaveTick?.Invoke(); }
            );

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(world + "EXCLUSIVE_MASTER_RESOURCE_INDICES")))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(1);
                writer.Write(StateResources.Count - 1);
                for (int i = 1; i < StateResources.Count; i++)
                {
                    writer.Write(Resources.GetWriteIndex(StateResources[i].Resource));
                }
            }
            Parallel.For(0, StateResources.Count, (i) =>
            {
                string statePath = world + "STATE_" + i + "/";

                StateResources[i].Cover.Save(statePath + "COVER");
                StateResources[i].NavMesh?.Save(statePath + "NAV_MESH");
                StateResources[i].SpottingPositions.Save(statePath + "SPOTTING_POSITIONS");
                StateResources[i].CrawlSpaceSpottingPositions.Save(statePath + "CRAWL_SPACE_SPOTTING_POSITIONS");
                StateResources[i].AssaultPositions.Save(statePath + "ASSAULT_POSITIONS");

                File.WriteAllBytes(statePath + "TRAVERSAL", new byte[] { 0x74, 0x72, 0x61, 0x76, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x3F, 0x00, 0x00 });
            });
            OnSaveTick?.Invoke();

            //TODO: save strings (?)
            OnSaveTick?.Invoke();
        }

        /// <summary>
        /// Imports resources to the level from Global, making it easier to share around
        /// </summary>
        /// <summary>
        /// Copy into this level's pak only the global textures its materials actually reference, and
        /// point those references at the copies. The same end as <see cref="ImportFromGlobal"/> - no
        /// material left pointing into global - without bringing the whole global set along, which is
        /// what a level built to carry a few composites elsewhere wants: it has
        /// <see cref="AbsorbGlobalTextures"/> off so Load and Save leave global alone, and calls this
        /// once before it is saved. Returns how many references were remapped.
        /// </summary>
        public int AbsorbReferencedGlobalTextures()
        {
            if (Textures == null || Materials?.Entries == null)
                return 0;

            Dictionary<Textures.TEX4, Textures.TEX4> imported = new Dictionary<Textures.TEX4, Textures.TEX4>();
            int remapped = 0;
            foreach (Materials.Material material in Materials.Entries)
            {
                foreach (TexturePtr reference in material.TextureReferences)
                {
                    if (reference == null || reference.Location != TexturePtr.Source.GLOBAL || reference.Texture == null)
                        continue;

                    if (!imported.TryGetValue(reference.Texture, out Textures.TEX4 levelCopy))
                    {
                        //Imported as a level texture (the flags travel with the copy), then the global's own put back
                        Textures.TEX4 globalTexture = reference.Texture;
                        globalTexture.UsageFlags &= ~Textures.TextureUsageFlag.IS_GLOBAL_PACK;
                        globalTexture.UsageFlags |= Textures.TextureUsageFlag.IS_LEVEL_PACK;
                        levelCopy = Textures.ImportEntry(globalTexture);
                        globalTexture.UsageFlags |= Textures.TextureUsageFlag.IS_GLOBAL_PACK;
                        globalTexture.UsageFlags &= ~Textures.TextureUsageFlag.IS_LEVEL_PACK;
                        imported[globalTexture] = levelCopy;
                    }

                    reference.Texture = levelCopy;
                    reference.Location = TexturePtr.Source.LEVEL;
                    remapped++;
                }
            }
            return remapped;
        }

        private int ImportFromGlobal()
        {
            if (!AbsorbGlobalTextures || _global?.Textures == null || Textures == null)
                return 0;

            Dictionary<Textures.TEX4, Textures.TEX4> imported = new Dictionary<Textures.TEX4, Textures.TEX4>();
            foreach (Textures.TEX4 globalTexture in _global.Textures.Entries)
            {
                globalTexture.UsageFlags &= ~Textures.TextureUsageFlag.IS_GLOBAL_PACK;
                globalTexture.UsageFlags |= Textures.TextureUsageFlag.IS_LEVEL_PACK;
                Textures.TEX4 levelCopy = Textures.ImportEntry(globalTexture);
                globalTexture.UsageFlags |= Textures.TextureUsageFlag.IS_GLOBAL_PACK;
                globalTexture.UsageFlags &= ~Textures.TextureUsageFlag.IS_LEVEL_PACK;
                imported[globalTexture] = levelCopy;
            }

            int remapped = 0;
            if (Materials?.Entries != null)
            {
                foreach (Materials.Material material in Materials.Entries)
                {
                    foreach (TexturePtr reference in material.TextureReferences)
                    {
                        if (reference == null || reference.Location != TexturePtr.Source.GLOBAL || reference.Texture == null)
                            continue;
                        if (!imported.TryGetValue(reference.Texture, out Textures.TEX4 levelTexture))
                            continue;

                        reference.Texture = levelTexture;
                        reference.Location = TexturePtr.Source.LEVEL;
                        remapped++;
                    }
                }
            }
            return remapped;
        }

        /// <summary>
        /// Find an ExclusiveMaster by ShortGuid
        /// </summary>
        private Entity FindExclusiveMaster(ShortGuid entityId)
        {
            if (Commands?.Entries == null)
                return null;

            foreach (Composite composite in Commands.Entries)
            {
                if (composite?.functions == null)
                    continue;

                foreach (FunctionEntity function in composite.GetFunctionEntitiesOfType(FunctionType.ExclusiveMaster))
                {
                    if (function.shortGUID == entityId)
                        return function;
                }
            }
            return null;
        }

        /// <summary>
        /// Gets the Havok 64 filepath (on Switch it's named differently)
        /// </summary>
        private static string Havok64Path(string world, string name)
        {
            if (File.Exists(world + name + ".HKX64_SWITCH.GZ"))
                return world + name + ".HKX64_SWITCH.GZ";
            if (File.Exists(world + name + ".HKX64"))
                return world + name + ".HKX64";
            return null;
        }

#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
        /// <summary>
        /// Create a new, empty level on disk at <paramref name="path"/> (a folder under DATA/ENV, e.g.
        /// DATA/ENV/PRODUCTION/MYLEVEL) and return it loaded.
        ///
        /// Most of a level's files can be written from nothing by their parsers, and are. A few hold
        /// authored data that is the same on every shipped level and that nothing can regenerate - the
        /// sound bank, event and dialogue tables, the behaviour tree database, the material mapping
        /// table, the galaxy, the morph target name table - and the Havok collision and physics files are
        /// scaffolds every level's rigid bodies are built on. Those are copied from <paramref name="baseLevel"/>,
        /// which must already be loaded. FRONTEND is the natural base: its Havok files are the smallest
        /// the game ships (one instance per world host, which the format needs), and every table it
        /// carries is the campaign variant.
        ///
        /// The new level's script holds a copy of the base's GLOBAL and PAUSEMENU composites (the engine
        /// instances both, and they are identical on every shipped level), its REQUIRED_ASSETS composites
        /// (which the engine also instances on every level), everything those instance, and an empty root
        /// composite named after the level. Its model pak holds the required FX/light models instancing
        /// needs plus whatever the required assets render; textures are the global set every level
        /// absorbs on load. The state files are valid empties, so the level loads in the game before
        /// anything is built into it.
        /// </summary>
        /// <summary>
        /// A level with nothing in it, at <paramref name="path"/>, loaded and ready to have content
        /// ported in - but not saved, and not made runnable. Only the files a level cannot produce for
        /// itself are copied from <paramref name="baseLevel"/> (authored tables, Havok scaffolds, the
        /// DX11 stubs); everything else is left for the parsers to write from an empty state, and the
        /// files the parsers cannot write from a never-loaded state are given valid empties. Nothing is
        /// seeded into the script and no required models are imported: that is what
        /// <see cref="MakeNewLevelFrom"/> adds on top to make a level the game can run, and a level
        /// that only exists to carry composites to another install has no use for any of it.
        /// </summary>
        /// <param name="absorbGlobalTextures">Sets <see cref="AbsorbGlobalTextures"/> before the first load.</param>
        public static Level MakeBlankLevel(string path, Level baseLevel, bool absorbGlobalTextures = true)
        {
            if (baseLevel == null || baseLevel.Commands == null || !baseLevel.Commands.Loaded)
                throw new ArgumentException("The base level must be loaded.", nameof(baseLevel));

            //Which files come from the base is decided by what it has loaded, so a base that lacks one
            //(no 64-bit Havok, say) simply contributes nothing for it
            List<KeyValuePair<string, string>> copies = new List<KeyValuePair<string, string>>();
            void FromBase(CathodeFile file, string destination) { if (file != null) copies.Add(new KeyValuePair<string, string>(file.Filepath, destination)); }
            FromBase(baseLevel.BehaviorTreeDB, "WORLD");
            FromBase(baseLevel.SoundBankData, "WORLD");
            FromBase(baseLevel.SoundDialogueLookups, "WORLD");
            FromBase(baseLevel.SoundEventData, "WORLD");
            FromBase(baseLevel.MaterialMappings, "WORLD");
            FromBase(baseLevel.MorphTargetDB, "WORLD");
            FromBase(baseLevel.CollisionHKX, "WORLD");
            FromBase(baseLevel.CollisionHKX64, "WORLD");
            FromBase(baseLevel.PhysicsHKX, "WORLD");
            FromBase(baseLevel.PhysicsHKX64, "WORLD");
            FromBase(baseLevel.GalaxyItems, "RENDERABLE/GALAXY");
            FromBase(baseLevel.GalaxyDefinition, "RENDERABLE/GALAXY");
            string baseRenderable = Path.GetDirectoryName(baseLevel.Textures.Filepath);
            copies.Add(new KeyValuePair<string, string>(Path.Combine(baseRenderable, "LEVEL_TEXTURES.DX11.PAK"), "RENDERABLE"));
            copies.Add(new KeyValuePair<string, string>(Path.Combine(baseRenderable, "LEVEL_TEXTURE_HEADERS.DX11.BIN"), "RENDERABLE"));

            return MakeBlankLevel(path, copies, baseLevel.Global, absorbGlobalTextures);
        }

        /// <summary>
        /// A blank level that only ever has to load and save - never run - scaffolded from a level's
        /// folder on disk without loading that level. It takes the least the parsers need: the Havok
        /// packfiles (a valid file to import compounds and physics systems into, which nothing here can
        /// write from nothing), MATERIAL_MAPPINGS (collision maps resolve against it), MORPH_TARGET_DB
        /// (models want its name table), GALAXY.DEFINITION_BIN (its parser cannot write from a
        /// never-loaded state), and the two DX11 stubs. The sound, behaviour-tree and galaxy-items
        /// tables a runnable level copies as well are left out: they are dead weight in a level that
        /// exists to carry composites somewhere else, and their parsers write an empty one on save.
        /// The folder is a retail-style one (RENDERABLE and WORLD beneath it); the level's global is
        /// passed separately, since a folder has no idea which install it belongs to.
        /// </summary>
        public static Level MakeBlankLevel(string path, string baseLevelFolder, Global global, bool absorbGlobalTextures = true)
        {
            if (string.IsNullOrEmpty(baseLevelFolder) || !Directory.Exists(baseLevelFolder))
                throw new ArgumentException("The base level folder does not exist: " + baseLevelFolder, nameof(baseLevelFolder));
            if (global == null)
                throw new ArgumentNullException(nameof(global));

            string baseWorld = Path.Combine(baseLevelFolder, "WORLD");
            string baseRenderable = Path.Combine(baseLevelFolder, "RENDERABLE");
            List<KeyValuePair<string, string>> copies = new List<KeyValuePair<string, string>>();
            foreach (string file in new[] { "MATERIAL_MAPPINGS.PAK", "MORPH_TARGET_DB.BIN", "COLLISION.HKX", "COLLISION.HKX64", "PHYSICS.HKX", "PHYSICS.HKX64" })
                copies.Add(new KeyValuePair<string, string>(Path.Combine(baseWorld, file), "WORLD"));
            copies.Add(new KeyValuePair<string, string>(Path.Combine(baseRenderable, "GALAXY", "GALAXY.DEFINITION_BIN"), "RENDERABLE/GALAXY"));
            foreach (string file in new[] { "LEVEL_TEXTURES.DX11.PAK", "LEVEL_TEXTURE_HEADERS.DX11.BIN" })
                copies.Add(new KeyValuePair<string, string>(Path.Combine(baseRenderable, file), "RENDERABLE"));

            return MakeBlankLevel(path, copies, global, absorbGlobalTextures);
        }

        /* copies: source file -> folder beneath the new level root (missing sources are skipped, the way
           CopyBaseFile always has) */
        private static Level MakeBlankLevel(string path, List<KeyValuePair<string, string>> copies, Global global, bool absorbGlobalTextures)
        {
            string root = path.Replace("\\", "/").TrimEnd('/');
            if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
                throw new IOException("A level already exists at " + root);
            if (root.ToUpper().Split(new string[] { "DATA/ENV/" }, StringSplitOptions.None).Length < 2)
                throw new ArgumentException("A level has to live under DATA/ENV.", nameof(path));

            string renderable = root + "/RENDERABLE/";
            string world = root + "/WORLD/";
            Directory.CreateDirectory(renderable + "GALAXY/");
            Directory.CreateDirectory(world + "STATE_0/");

            //Authored tables and Havok scaffolds we cannot produce ourselves, and the two DX11 stubs the
            //PC build ships identically on every level and nothing parses (32 and 8 bytes): the texture
            //streamer opens the header file on load and crashes without it, so they travel with the
            //base files even though the ALL pak is the only real texture container.
            foreach (KeyValuePair<string, string> copy in copies)
                CopyBaseFile(copy.Key, root + "/" + copy.Value.Trim('/') + "/");

            //Load reads these two unconditionally: no exclusive master states, and an empty script so
            //the loader picks the PAK filename (retail ships the PAK; the BIN is written alongside it).
            using (BinaryWriter writer = new BinaryWriter(File.Create(world + "EXCLUSIVE_MASTER_RESOURCE_INDICES")))
            {
                writer.Write(1);
                writer.Write(0);
            }
            File.WriteAllBytes(world + "COMMANDS.PAK", new byte[0]);

            Level level = new Level(root, global, false);
            level.AbsorbGlobalTextures = absorbGlobalTextures;
            Utilities.ClearRadiosityOnDisk(level); //the "no radiosity" state a build without radiosity leaves behind
            level.Load();

            //The morph target file was copied for its name table; the targets themselves belong to the base's models
            level.MorphTargetDB.Entries.Clear();

            //Valid empties for the files whose parsers cannot write from a never-loaded state
            State state = level.StateResources[0];
            state.NavMesh.SetTileData(EmptyNavMeshHeader(), null, null, null, null, null, null, null, null);
            SetEmptyGrid(state.SpottingPositions);
            SetEmptyGrid(state.CrawlSpaceSpottingPositions);
            SetEmptyGrid(state.AssaultPositions);
            state.Cover.Traversal = new Cover.TraversalGrid() { XCells = 1, ZCells = 1, UnitSize = 1.0f, Cells = new List<List<short>>() { new List<short>() } };
            level.AlphaLight.Resolution = new System.Numerics.Vector2(64, 64);
            level.AlphaLight.ImageData = new byte[64 * 64 * 8];

            return level;
        }

        public static Level MakeNewLevelFrom(string path, Level baseLevel)
        {
            if (baseLevel == null || baseLevel.Commands == null || !baseLevel.Commands.Loaded)
                throw new ArgumentException("The base level must be loaded.", nameof(baseLevel));

            /* Checked before anything is written: a base without the whole REQUIRED_MODEL_* block makes a
             * level that can never build a light, a particle or a fog volume, and finding that out after
             * the folder exists is worse than not starting. */
            List<RequiredModels.Model> absent = new List<RequiredModels.Model>();
            foreach (RequiredModels.Model model in Enum.GetValues(typeof(RequiredModels.Model)))
                if (RequiredModels.Resolve(baseLevel.Models, model) == null)
                    absent.Add(model);
            if (absent.Count != 0)
                throw new ArgumentException("The base level is missing required models: " + string.Join(", ", absent), nameof(baseLevel));
            List<string> absentMaterials = RequiredMaterials.Missing(baseLevel.Materials);
            if (absentMaterials.Count != 0)
                throw new ArgumentException("The base level is missing required materials: " + string.Join(", ", absentMaterials), nameof(baseLevel));

            //The empty, loadable shell; the rest of this makes it a level the game can run
            Level level = MakeBlankLevel(path, baseLevel);
            string name = Path.GetFileName(path.Replace("\\", "/").TrimEnd('/'));

            /* The engine's own materials at the head of every level's table - the post-processing
             * composite above all, then bloom, distortion, lens flare, the fog and surface-effect volumes,
             * water, refraction, caustics, the deferred light, the particle cube, FALLBACK at 0. No model
             * references most of them, so no port ever brought them, and a level without
             * POST_PROCESSING_MATERIAL drew its HUD over a black frame. First, onto the empty table, so
             * they take 0-14 and their shaders 0-14 of the pak, exactly as retail ships them. */
            RequiredMaterials.Import(level.Materials, baseLevel.Materials);

            /* The REQUIRED_MODEL_* block at the head of every model pak: instancing swaps lights, particles,
             * fog and decals onto these, so a level without them cannot build any FX. Imported before the
             * porter runs, because the porter pulls several of them in itself as dependencies of the
             * required-asset composites - and once one is in the pak by name, importing it again is a no-op,
             * so doing this afterwards left them wherever the porter happened to put them. */
            foreach (Models.CS2 model in baseLevel.Models.Entries)
            {
                if (RequiredModels.IsRequiredEntry(baseLevel.Models, model))
                    level.Models.ImportEntry(model);
            }

            /* And the twelve movers drawing them, at the head of the MVR where instancing keeps them.
               Without these instancing used to keep the first twelve movers of whatever was placed
               instead - see RequiredMovers. */
            List<Movers.MOVER_DESCRIPTOR> requiredMovers = RequiredMovers.ImportFrom(level, baseLevel);
            if (requiredMovers.Count != RequiredMovers.Count)
                throw new ArgumentException("The base level has no required-asset movers at the head of its MVR.", nameof(baseLevel));
            level.Movers.Entries.InsertRange(0, requiredMovers);

            //Script: GLOBAL and PAUSEMENU from the base, the REQUIRED_ASSETS composites the engine instances on
            //every level without a script referencing them (weapons, gadgets, the jobs the AI needs) - each
            //with everything it instances, via the porter - then an empty root for the level itself
            Composite baseGlobal = baseLevel.Commands.EntryPoints?[1];
            Composite basePauseMenu = baseLevel.Commands.EntryPoints?[2];
            CompositePorter porter = new CompositePorter(baseLevel, level) { OverwriteComposites = true, Recurse = true };
            if (baseGlobal != null) porter.Port(baseGlobal);
            if (basePauseMenu != null) porter.Port(basePauseMenu);
            foreach (Composite composite in baseLevel.Commands.Entries)
            {
                if (composite != null && IsRequiredAssetComposite(composite))
                    porter.Port(composite);
            }
            Composite levelRoot = level.Commands.AddComposite(name);
            level.Commands.SetEntryPoints(
                levelRoot,
                baseGlobal == null ? null : level.Commands.GetComposite(baseGlobal.shortGUID),
                basePauseMenu == null ? null : level.Commands.GetComposite(basePauseMenu.shortGUID));

            /* The engine reads the required block positionally, and this level is saved rather than
             * instanced, so the ordering instancing normally restores has to be right before the save. */
            List<RequiredModels.Model> stillMissing;
            RequiredModels.EnsureOrdered(level.Models, out stillMissing);
            List<string> stillMissingMaterials;
            RequiredMaterials.EnsureOrdered(level.Materials, level.Shaders, out stillMissingMaterials);

            level.Save();
            return level;
        }

        //The same rule instancing uses to pick the composites the engine loads on every level
        private static bool IsRequiredAssetComposite(Composite composite)
        {
            return composite.name != null && composite.name.ToUpper().Replace("/", "\\").StartsWith("REQUIRED_ASSETS\\");
        }

        private static void CopyBaseFile(CathodeFile file, string destinationFolder)
        {
            if (file != null)
                CopyBaseFile(file.Filepath, destinationFolder);
        }
        private static void CopyBaseFile(string sourcePath, string destinationFolder)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                return;
            File.Copy(sourcePath, destinationFolder + Path.GetFileName(sourcePath), true);
        }

        /// <summary>
        /// A Detour tile header for a mesh with no polygons, in the shape the navmesh baker writes.
        /// </summary>
        private static NavigationMesh.dtMeshHeader EmptyNavMeshHeader()
        {
            return new NavigationMesh.dtMeshHeader
            {
                FourCC = new fourcc { V = new[] { 'V', 'A', 'N', 'D' } },
                version = 7,
                walkableHeight = 0.5f,
                walkableRadius = 0.3125f,
                walkableClimb = 0.3125f,
                bMin = new[] { -1.0f, -1.0f, -1.0f },
                bMax = new[] { 1.0f, 1.0f, 1.0f },
                bvQuantFactor = 16.0f,
            };
        }

        //Retail's empty job grids are one cell of one unit holding nothing, not a zero-sized grid
        private static void SetEmptyGrid(SpottingPositions positions)
        {
            positions.XCells = 1;
            positions.ZCells = 1;
            positions.UnitSize = 1.0f;
            positions.Cells.Clear();
            positions.Cells.Add(new List<SpottingPositions.JobInfo>());
        }
        private static void SetEmptyGrid(AssaultPositions positions)
        {
            positions.XCells = 1;
            positions.ZCells = 1;
            positions.UnitSize = 1.0f;
            positions.Cells.Clear();
            positions.Cells.Add(new List<AssaultPositions.JobInfo>());
        }
#endif

        /// <summary>
        /// Get all levels available within the ENV folder. Pass the path to the folder that contains AI.exe.
        /// </summary>
        public static List<string> GetLevels(string gameDirectory)
        {
            string envDirectory = gameDirectory + "/DATA/ENV/";
            if (!Directory.Exists(envDirectory))
                return new List<string>();

            string[] galaxyBins = Directory.GetFiles(envDirectory, "GALAXY.DEFINITION_BIN", SearchOption.AllDirectories);
            List<string> mapList = new List<string>();
            for (int i = 0; i < galaxyBins.Length; i++)
            {
                int extraLength = ("/RENDERABLE/GALAXY/GALAXY.DEFINITION_BIN").Length;
                string mapPath = galaxyBins[i].Substring(0, galaxyBins[i].Length - extraLength);

                //Try match a few files outside of the GALAXY definition, to ensure we are actually a map.
                if (!File.Exists(mapPath + "/WORLD/COMMANDS.PAK") && !File.Exists(mapPath + "/WORLD/COMMANDS.BIN") && !File.Exists(mapPath + "/WORLD/COMMANDS.PAK.GZ") && !File.Exists(mapPath + "/WORLD/COMMANDS.BIN.GZ")) continue;
                if (!File.Exists(mapPath + "/WORLD/MODELS.MVR") && !File.Exists(mapPath + "/WORLD/MODELS.MVR.GZ")) continue;
                if (!File.Exists(mapPath + "/RENDERABLE/LEVEL_MODELS.PAK") && !File.Exists(mapPath + "/RENDERABLE/LEVEL_MODELS.PAK.FZIP")) continue;
                if (!File.Exists(mapPath + "/RENDERABLE/MODELS_LEVEL.BIN") && !File.Exists(mapPath + "/RENDERABLE/MODELS_LEVEL.BIN.GZ")) continue;

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