using CATHODE;
using CATHODE.EXPERIMENTAL; 
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CathodeLib
{
    /* A helper class that holds all parse-able formats for a level, and saves them safely to update indexes across all */
    public class Level
    {
        public Models Models;
        public Textures Textures;
        public Materials Materials;
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

        public List<State> StateResources = new List<State>();

        /* Load a level in the game's "ENV/PRODUCTION" folder */
        public Level(string path)
        {
            /* RENDERABLE */
            Models = new Models(path + "/RENDERABLE/LEVEL_MODELS.PAK"); //TODO: parse CST data here too?
            Textures = new Textures(path + "/RENDERABLE/LEVEL_TEXTURES.ALL.PAK");
            Materials = new Materials(path + "/RENDERABLE/LEVEL_MODELS.MTL");
            RenderableElements = new RenderableElements(path + "/RENDERABLE/REDS.BIN");
            
            // RENDERABLE TODO:
            //  - SHADERS

            /* WORLD */
            Movers = new Movers(path + "/WORLD/MODELS.MVR");
            Commands = new Commands(path + "/WORLD/COMMANDS.PAK");
            Resources = new Resources(path + "/WORLD/RESOURCES.BIN");
            PhysicsMaps = new PhysicsMaps(path + "/WORLD/PHYSICS.MAP");
            EnvironmentMaps = new EnvironmentMaps(path + "/WORLD/ENVIRONMENTMAP.BIN");
            CollisionMaps = new CollisionMaps(path + "/WORLD/COLLISION.MAP");
            EnvironmentAnimations = new EnvironmentAnimations(path + "/WORLD/ENVIRONMENT_ANIMATION.DAT");
            MaterialMappings = new MaterialMappings(path + "/WORLD/MATERIAL_MAPPINGS.PAK");
            PathBarrierResources = new PathBarrierResources(path + "/WORLD/PATH_BARRIER_RESOURCES");

            // WORLD TODO: 
            //  - COLLISION.BIN
            //  - ALPHALIGHT_LEVEL.BIN
            //  - LIGHTS.BIN
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

        /* Save all modifications to the level */
        public void Save()
        {
            //Get the REDS database as actual objects
            List<Models.CS2.Submesh> redsModelRefs = new List<Models.CS2.Submesh>();
            for (int i = 0; i < RenderableElements.Entries.Count; i++)
            {
                //TODO: we should probs store reds.Entries[i].ModelLODIndex here too, but it goes outside the bounds of our BIN array!?!
                redsModelRefs.Add(Models.GetSubmeshForWriteIndex(RenderableElements.Entries[i].ModelIndex));
            }

            //Save the files back out 
            Models.Save();

            //Update the REDS database with our newly written indexes
            for (int i = 0; i < RenderableElements.Entries.Count; i++)
            {
                if (RenderableElements.Entries[i].ModelIndex != -1)
                    RenderableElements.Entries[i].ModelIndex = Models.GetWriteIndexForSubmesh(redsModelRefs[i]);
                //AllRenderableElements.Entries[i].ModelLODIndex = -1;    <- TODO: Can't do this else the game crashes :D
            }
            RenderableElements.Save();
        }

        public class State
        {
            public int Index;

            public NavigationMesh NavMesh;
        }
    }
}
