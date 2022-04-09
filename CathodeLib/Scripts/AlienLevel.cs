using System.IO;

/*
namespace CathodeLib
{
    public class AlienLevel
    {
        public static alien_level Load(string LEVEL_NAME, string ENV_PATH)
        {
            alien_level Result = new alien_level();
            string levelPath = ENV_PATH + "/PRODUCTION/" + LEVEL_NAME;

            /*** WORLD ***/

/*
            Result.ModelsMVR = new CATHODE.Models.CathodeMovers(levelPath + "/WORLD/MODELS.MVR");
            Result.CommandsPAK = new CATHODE.Commands.CommandsPAK(levelPath + "/WORLD/COMMANDS.PAK");

            Result.RenderableREDS = new CATHODE.Misc.CathodeRenderableElements(levelPath + "/WORLD/REDS.BIN");
            Result.ResourcesBIN = new CATHODE.Misc.CathodeResources(levelPath + "/WORLD/RESOURCES.BIN");
            Result.PhysicsMap = new CATHODE.Misc.CathodePhysicsMap(levelPath + "/WORLD/PHYSICS.MAP");
            Result.EnvironmentMap = new CATHODE.Misc.CathodeEnvironmentMap(levelPath + "/WORLD/ENVIRONMENTMAP.BIN");
            Result.CollisionMap = new CATHODE.Misc.CathodeCollisionMap(levelPath + "/WORLD/COLLISION.MAP");

            Result.EnvironmentAnimation = CATHODE.Misc.CathodeEnvironmentAnimation.Load(levelPath + "/WORLD/ENVIRONMENT_ANIMATION.DAT");

            //ALPHALIGHT_LEVEL.BIN
            //BEHAVIOR_TREE.DB
            //CHARACTERACCESSORYSETS.BIN
            //COLLISION.BIN
            //COLLISION.HKX
            //COLLISION.HKX64
            //CUTSCENE_DIRECTOR_DATA.BIN
            //EXCLUSIVE_MASTER_RESOURCE_INDICES
            //LEVEL.STR
            //LIGHTS.BIN
            //MATERIAL_MAPPINGS.PAK
            //MORPH_TARGET_DB.BIN
            //OCCLUDER_TRIANGLE_BVH.BIN
            //PATH_BARRIER_RESOURCES
            //PHYSICS.HKX
            //PHYSICS.HKX64
            //RADIOSITY_COLLISION_MAPPING.BIN
            //SNDNODENETWORK.DAT
            //SOUNDBANKDATA.DAT
            //SOUNDDIALOGUELOOKUPS.DAT
            //SOUNDENVIRONMENTDATA.DAT
            //SOUNDEVENTDATA.DAT
            //SOUNDFLASHMODELS.DAT
            //SOUNDLOADZONES.DAT
            //STATE_x/ASSAULT_POSITIONS
            //STATE_x/COVER
            //STATE_x/CRAWL_SPACE_SPOTTING_POSITIONS
            //STATE_x/NAV_MESH
            //STATE_x/SPOTTING_POSITIONS
            //STATE_x/TRAVERSAL

            /*** RENDERABLE ***/

/*
Result.ModelsCST = File.ReadAllBytes(levelPath + "/RENDERABLE/LEVEL_MODELS.CST");
            Result.ModelsMTL = CATHODE.Models.CathodeMaterials.Load(levelPath + "/RENDERABLE/LEVEL_MODELS.MTL");
            Result.ModelsPAK = CATHODE.Models.ModelPAK.Load(levelPath + "/RENDERABLE/LEVEL_MODELS.PAK");
            Result.ModelsBIN = CATHODE.Models.ModelBIN.Load(levelPath + "/RENDERABLE/MODELS_LEVEL.BIN");

            Result.ShadersPAK = CATHODE.Shaders.ShadersPAK.Load(levelPath + "/RENDERABLE/LEVEL_SHADERS_DX11.PAK");
            //Result.ShadersBIN = CATHODE.Shaders.ShadersBIN.Load(levelPath + "/RENDERABLE/LEVEL_SHADERS_DX11_BIN.PAK");
            Result.ShadersIDXRemap = CATHODE.Shaders.IDXRemap.Load(levelPath + "/RENDERABLE/LEVEL_SHADERS_DX11_IDX_REMAP.PAK");

            Result.LevelTextures = CATHODE.Textures.CathodeTextures.Load(levelPath + "/RENDERABLE/LEVEL_TEXTURES.ALL.PAK", levelPath + "/RENDERABLE/LEVEL_TEXTURE_HEADERS.ALL.BIN");

            //LEVEL_TEXTURES.DX11.PAK
            //RADIOSITY_INSTANCE_MAP.TXT
            //RADIOSITY_RUNTIME.BIN
            //DAMAGE/DAMAGE_MAPPING_INFO.BIN
            //GALAXY/GALAXY.DEFINITION_BIN
            //GALAXY/GALAXY.ITEMS_BIN

            return Result;
        }
    }
}

public class alien_level
{
    public CATHODE.Models.CathodeMovers ModelsMVR;
    public CATHODE.Commands.CommandsPAK CommandsPAK;

    public CATHODE.Misc.CathodeRenderableElements RenderableREDS;
    public CATHODE.Misc.CathodeResources ResourcesBIN;
    public CATHODE.Misc.CathodePhysicsMap PhysicsMap;
    public CATHODE.Misc.CathodeEnvironmentMap EnvironmentMap;
    public CATHODE.Misc.CathodeCollisionMap CollisionMap;

    public alien_animation_dat EnvironmentAnimation;

    public byte[] ModelsCST;
    public alien_mtl ModelsMTL;
    public alien_pak_model ModelsPAK;
    public alien_model_bin ModelsBIN;

    public alien_textures LevelTextures;

    public alien_shader_pak ShadersPAK;
    public alien_shader_bin_pak ShadersBIN;
    public alien_shader_idx_remap ShadersIDXRemap;
};
*/