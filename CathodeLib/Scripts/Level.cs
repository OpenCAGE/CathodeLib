using CATHODE;
using CATHODE.EXPERIMENTAL;
using CATHODE.LEGACY;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CathodeLib
{
    /* A helper class that holds all parse-able formats for a level, and saves them safely to update indexes across all */
    public class Level
    {
        public Textures GlobalTextures;

        public Models Models;
        public Textures Textures;
        public Materials Materials;
        public Shaders Shaders;

        public RenderableElements RenderableElements;
        public Movers Movers;
        public Commands Commands;
        public Resources Resources;
        public PhysicsMaps PhysicsMaps;
        public EnvironmentMaps EnvironmentMaps;
        public CollisionMaps CollisionMaps;
        public EnvironmentAnimations EnvironmentAnimations;
        public MaterialMappings MaterialMappings;
        public PathBarrierResources PathBarrierResources;
        public Lights Lights;
        public Collisions Collisions;

        public class State
        {
            public int Index;
            public NavigationMesh NavMesh;
        }
        public List<State> StateResources = new List<State>();

        /* Load a level in the game's "ENV/PRODUCTION" folder */
        public Level(string path)
        {
            /* GLOBAL */
            string pathGlobal = path.Split(new string[] { "PRODUCTION" }, StringSplitOptions.None)[0] + "GLOBAL/WORLD";
            GlobalTextures = new Textures(pathGlobal + "/GLOBAL_TEXTURES.ALL.PAK");
            //TODO: We don't load GLOBAL_MODELS since it just contains vertex buffers. We should load this to learn about all the vertex formats tho!

            /* RENDERABLE */
            Models = new Models(path + "/RENDERABLE/LEVEL_MODELS.PAK");
            Textures = new Textures(path + "/RENDERABLE/LEVEL_TEXTURES.ALL.PAK");
            Materials = new Materials(path + "/RENDERABLE/LEVEL_MODELS.MTL");
            Shaders = new Shaders(path + "/RENDERABLE/LEVEL_SHADERS_DX11.PAK");
            //Shaders = new ShadersPAK(path + "/RENDERABLE/LEVEL_SHADERS_DX11.PAK");
            //ShadersIDX = new IDXRemap(path + "/RENDERABLE/LEVEL_SHADERS_DX11_REMAP.PAK");

            // RENDERABLE TODO:
            //  - SHADERS

            /* WORLD */
            RenderableElements = new RenderableElements(path + "/WORLD/REDS.BIN");
            Movers = new Movers(path + "/WORLD/MODELS.MVR");
            //Commands = new Commands(path + "/WORLD/COMMANDS.PAK");
            //Resources = new Resources(path + "/WORLD/RESOURCES.BIN");
            //PhysicsMaps = new PhysicsMaps(path + "/WORLD/PHYSICS.MAP");
            EnvironmentMaps = new EnvironmentMaps(path + "/WORLD/ENVIRONMENTMAP.BIN");
            //CollisionMaps = new CollisionMaps(path + "/WORLD/COLLISION.MAP");
            //EnvironmentAnimations = new EnvironmentAnimations(path + "/WORLD/ENVIRONMENT_ANIMATION.DAT");
            //MaterialMappings = new MaterialMappings(path + "/WORLD/MATERIAL_MAPPINGS.PAK");
            //PathBarrierResources = new PathBarrierResources(path + "/WORLD/PATH_BARRIER_RESOURCES");
            Lights = new Lights(path + "/WORLD/LIGHTS.BIN");
            //Collisions = new Collisions(path + "/WORLD/COLLISION.BIN");

            // WORLD TODO: 
            //  - ALPHALIGHT_LEVEL.BIN
            //  - OCCLUDER_TRIANGLE_BVH.BIN
            //  - SND NETWORK FILES

            /* WORLD STATE RESOURCES */
            int stateCount = 1;
            using (BinaryReader reader = new BinaryReader(File.OpenRead(path + "/WORLD/EXCLUSIVE_MASTER_RESOURCE_INDICES")))
            {
                reader.BaseStream.Position = 4;
                stateCount += reader.ReadInt32();
                //TODO: this file also contains indices for each state which seem to be of some relevence, although EXCLUSIVE_MASTER_RESOURCE doesn't point to indices?
            }
            for (int i = 0; i < stateCount; i++)
            {
                State state = new State() { Index = i };
                state.NavMesh = new NavigationMesh(path + "/WORLD/STATE_" + i + "/NAV_MESH");

                // WORLD STATE RESOURCES TODO:
                //  - ASSAULT_POSITIONS
                //  - COVER
                //  - CRAWL_SPACE_SPOTTING_POSITIONS
                //  - SPOTTING_POSITIONS
                //  - TRAVERSAL
            }
        }

        /* Save all modifications to the level - this currently assumes we aren't editing GLOBAL data */
        public void Save()
        {
            /* UPDATE MOVER INDEXES */

            Movers.Entries.RemoveRange(Movers.Entries.Count - 100, 100);

            //Get links to mover entries as actual objects
            List<Movers.MOVER_DESCRIPTOR> lightMovers = new List<Movers.MOVER_DESCRIPTOR>();
            for (int i = 0; i < Lights.Entries.Count; i++)
                lightMovers.Add(Movers.GetAtWriteIndex(Lights.Entries[i].MoverIndex));
            List<Movers.MOVER_DESCRIPTOR> envMapMovers = new List<Movers.MOVER_DESCRIPTOR>();
            for (int i = 0; i < EnvironmentMaps.Entries.Count; i++)
                envMapMovers.Add(Movers.GetAtWriteIndex(EnvironmentMaps.Entries[i].MoverIndex));
            Movers.Save();

            //Update mover indexes for light refs
            List<Lights.Light> lights = new List<Lights.Light>();
            for (int i = 0; i < Lights.Entries.Count; i++)
            {
                Lights.Entries[i].MoverIndex = Movers.GetWriteIndex(lightMovers[i]);
                if (Lights.Entries[i].MoverIndex != -1) lights.Add(Lights.Entries[i]);
            }
            Lights.Entries = lights;
            Lights.Save();

            //Update mover indexes for envmap refs
            List<EnvironmentMaps.Mapping> envMaps = new List<EnvironmentMaps.Mapping>();
            for (int i = 0; i < EnvironmentMaps.Entries.Count; i++)
            {
                EnvironmentMaps.Entries[i].MoverIndex = Movers.GetWriteIndex(envMapMovers[i]);
                if (EnvironmentMaps.Entries[i].MoverIndex != -1) envMaps.Add(EnvironmentMaps.Entries[i]);
            }
            EnvironmentMaps.Entries = envMaps;
            EnvironmentMaps.Save();


            /* UPDATE MATERIAL/MODEL INDEXES */

            //Get REDS links as actual objects
            List<Models.CS2.Submesh> redsModels = new List<Models.CS2.Submesh>();
            List<Materials.Material> redsMaterials = new List<Materials.Material>();
            for (int i = 0; i < RenderableElements.Entries.Count; i++)
            {
                redsModels.Add(Models.GetAtWriteIndex(RenderableElements.Entries[i].ModelIndex));
                redsMaterials.Add(Materials.GetAtWriteIndex(RenderableElements.Entries[i].MaterialIndex));
            }

            //Get model links as actual objects
            List<Materials.Material> modelMaterials = new List<Materials.Material>();
            for (int i = 0; i < Models.Entries.Count; i++)
                for (int x = 0; x < Models.Entries[i].Submeshes.Count; x++)
                    modelMaterials.Add(Materials.GetAtWriteIndex(Models.Entries[i].Submeshes[x].MaterialLibraryIndex));
            Models.Save();

            //Get material links as actual objects
            List<Textures.TEX4> materialTextures = new List<Textures.TEX4>();
            for (int i = 0; i < Materials.Entries.Count; i++)
            {
                for (int x = 0; x < Materials.Entries[i].TextureReferences.Count; x++)
                {
                    switch (Materials.Entries[i].TextureReferences[x].Source)
                    {
                        case Materials.Material.Texture.TextureSource.LEVEL:
                            materialTextures.Add(Textures.GetAtWriteIndex(Materials.Entries[i].TextureReferences[x].BinIndex));
                            break;
                        case Materials.Material.Texture.TextureSource.GLOBAL:
                            materialTextures.Add(GlobalTextures.GetAtWriteIndex(Materials.Entries[i].TextureReferences[x].BinIndex));
                            break;
                    }
                }
            }
            Materials.Save();

            //Update the REDS links
            for (int i = 0; i < RenderableElements.Entries.Count; i++)
            {
                if (RenderableElements.Entries[i].ModelIndex != -1)
                    RenderableElements.Entries[i].ModelIndex = Models.GetWriteIndex(redsModels[i]);
                RenderableElements.Entries[i].ModelLODIndex = -1;
                RenderableElements.Entries[i].ModelLODPrimitiveCount = 0;
                if (RenderableElements.Entries[i].MaterialIndex != -1)
                    RenderableElements.Entries[i].MaterialIndex = Materials.GetWriteIndex(redsMaterials[i]);
            }
            RenderableElements.Save();

            //Update the model links
            int y = 0;
            for (int i = 0; i < Models.Entries.Count; i++)
            {
                for (int x = 0; x < Models.Entries[i].Submeshes.Count; x++)
                {
                    Models.Entries[i].Submeshes[x].MaterialLibraryIndex = Materials.GetWriteIndex(modelMaterials[y]);
                    y++;
                }
            }
            Models.Save();

            //Update the material links
            y = 0;
            for (int i = 0; i < Materials.Entries.Count; i++)
            {
                for (int x = 0; x < Materials.Entries[i].TextureReferences.Count; x++)
                {
                    switch (Materials.Entries[i].TextureReferences[x].Source)
                    {
                        case Materials.Material.Texture.TextureSource.LEVEL:
                            Materials.Entries[i].TextureReferences[x].BinIndex = Textures.GetWriteIndex(materialTextures[y]);
                            break;
                        case Materials.Material.Texture.TextureSource.GLOBAL:
                            Materials.Entries[i].TextureReferences[x].BinIndex = GlobalTextures.GetWriteIndex(materialTextures[y]);
                            break;
                    }
                    y++;
                }
            }
            Materials.Save();
        }
    }
}
