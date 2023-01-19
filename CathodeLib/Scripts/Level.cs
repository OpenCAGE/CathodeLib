using CATHODE;
using System;
using System.Collections.Generic;
using System.Text;

namespace CathodeLib
{
    /* A helper class that holds all parse-able formats for a level, and saves them safely to update indexes across all */
    public class Level
    {
        public Models AllModels;
        public Textures AllTextures;
        public MaterialDatabase AllMaterials;

        public MoverDatabase AllMovers;
        public Commands AllCommands;
        public RenderableElementsDatabase AllRenderableElements;
        public ResourcesDatabase AllResources;
        public PhysicsMapDatabase AllPhysicsMaps;
        public EnvironmentMapDatabase AllEnvironmentMaps;
        public CollisionMapDatabase AllCollisionMaps;
        public EnvironmentAnimationDatabase AllEnvironmentAnimations;
        public MaterialMappings AllMaterialMappings;

        public NavigationMesh NavMesh;

        /* Load a level in the game's "ENV/PRODUCTION" folder */
        public Level(string path)
        {
            AllModels = new Models(path + "/RENDERABLE/LEVEL_MODELS.PAK"); 
            //AllTextures = new Textures(path + "/RENDERABLE/LEVEL_TEXTURES.ALL.PAK");
            //AllMaterials = new MaterialDatabase(path + "/RENDERABLE/LEVEL_MODELS.MTL");

            //AllMovers = new MoverDatabase(path + "/WORLD/MODELS.MVR");
            //AllCommands = new Commands(path + "/WORLD/COMMANDS.PAK");
            AllRenderableElements = new RenderableElementsDatabase(path + "/WORLD/REDS.BIN");
            //AllResources = new ResourcesDatabase(path + "/WORLD/RESOURCES.BIN");
            //AllPhysicsMaps = new PhysicsMapDatabase(path + "/WORLD/PHYSICS.MAP");
            //AllEnvironmentMaps = new EnvironmentMapDatabase(path + "/WORLD/ENVIRONMENTMAP.BIN");
            //AllCollisionMaps = new CollisionMapDatabase(path + "/WORLD/COLLISION.MAP");
            //AllEnvironmentAnimations = new EnvironmentAnimationDatabase(path + "/WORLD/ENVIRONMENT_ANIMATION.DAT");
            //AllMaterialMappings = new MaterialMappings(path + "/WORLD/MATERIAL_MAPPINGS.PAK");

            //TODO: we can have multiple states!
            //NavMesh = new NavigationMesh(path + "/WORLD/STATE_0/NAV_MESH");
        }

        /* Save all modifications to the level */
        public void Save()
        {
            //Get the REDS database as actual objects
            List<Models.CS2.Submesh> redsModelRefs = new List<Models.CS2.Submesh>();
            for (int i = 0; i < AllRenderableElements.Entries.Count; i++)
            {
                //TODO: we should probs store reds.Entries[i].ModelLODIndex here too, but it goes outside the bounds of our BIN array!?!
                redsModelRefs.Add(AllModels.GetSubmeshForWriteIndex(AllRenderableElements.Entries[i].ModelIndex));
            }

            //Save the files back out 
            AllModels.Save();

            //Update the REDS database with our newly written indexes
            for (int i = 0; i < AllRenderableElements.Entries.Count; i++)
            {
                if (AllRenderableElements.Entries[i].ModelIndex != -1)
                    AllRenderableElements.Entries[i].ModelIndex = AllModels.GetWriteIndexForSubmesh(redsModelRefs[i]);
                //AllRenderableElements.Entries[i].ModelLODIndex = -1;    <- TODO: Can't do this else the game crashes :D
            }
            AllRenderableElements.Save();
        }
    }
}
