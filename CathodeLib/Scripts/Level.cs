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
            for (int i = 0; i < StateResources.Count; i++)
            {
                string statePath = world + "STATE_" + i + "/";

                StateResources[i].Cover = new Cover(statePath + "COVER");
                StateResources[i].NavMesh = new NavigationMesh(statePath + "NAV_MESH");
                StateResources[i].SpottingPositions = new SpottingPositions(statePath + "SPOTTING_POSITIONS");
                StateResources[i].CrawlSpaceSpottingPositions = new SpottingPositions(statePath + "CRAWL_SPACE_SPOTTING_POSITIONS");
                StateResources[i].AssaultPositions = new AssaultPositions(statePath + "ASSAULT_POSITIONS");
            }
            OnLoadTick?.Invoke();

            string pathDATA = _filepath.Replace('\\', '/').Split(new string[] { "/DATA/ENV" }, StringSplitOptions.None)[0] + "/DATA";
            Strings = new Dictionary<string, Dictionary<string, TextDB>>();
            if (File.Exists(pathDATA + "/LEVEL_TEXT_DATABASES.XML"))
            {
                string levelName = Directory.GetParent(_filepath).Name;
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(File.ReadAllText(pathDATA + "/LEVEL_TEXT_DATABASES.XML"));
                XmlNodeList textDBsGlobal = doc.SelectNodes("//level_text_databases/level");
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
            if (radiositySettings == null)
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
        private int ImportFromGlobal()
        {
            if (_global?.Textures == null || Textures == null)
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