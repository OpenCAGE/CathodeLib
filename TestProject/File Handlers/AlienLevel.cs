using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

public class AlienModel
{
    public List<AlienModelPart> parts = new List<AlienModelPart>();
}

public class AlienModelPart
{
    public List<int> indicies = new List<int>();

    public List<Vector3> vertices = new List<Vector3>();
    public List<Vector3> normals = new List<Vector3>();
    public List<Vector2> uvs = new List<Vector2>();
    public List<Vector2> uvs1 = new List<Vector2>();
    public List<Vector2> uvs2 = new List<Vector2>();
    public List<Vector2> uvs3 = new List<Vector2>();
    public List<Vector2> uvs7 = new List<Vector2>();
    public List<Vector4> colours = new List<Vector4>();
}

namespace TestProject.File_Handlers
{
    public class AlienLevel
    {
        public static alien_level Load(string LEVEL_NAME)
        {
            alien_level Result = new alien_level();

            string levelPath = @"G:\SteamLibrary\steamapps\common\Alien Isolation\DATA\ENV\PRODUCTION\" + LEVEL_NAME;
            string rootPath = (LEVEL_NAME.ToUpper().Substring(0, 3) == "DLC") ? levelPath + "/.." : levelPath;

            Result.GlobalTextures = CATHODE.Textures.TexturePAK.Load(rootPath + "/../../GLOBAL/WORLD/GLOBAL_TEXTURES.ALL.PAK", rootPath + "/../../GLOBAL/WORLD/GLOBAL_TEXTURES_HEADERS.ALL.BIN");
            Result.LevelTextures = CATHODE.Textures.TexturePAK.Load(levelPath + "/RENDERABLE/LEVEL_TEXTURES.ALL.PAK", levelPath + "/RENDERABLE/LEVEL_TEXTURE_HEADERS.ALL.BIN");
            Result.ModelsCST = File.ReadAllBytes(levelPath + "/RENDERABLE/LEVEL_MODELS.CST");
            Result.ModelsMTL = CATHODE.Models.ModelsMTL.Load(levelPath + "/RENDERABLE/LEVEL_MODELS.MTL");
            Result.ModelsBIN = CATHODE.Models.ModelBIN.Load(levelPath + "/RENDERABLE/MODELS_LEVEL.BIN");
            Result.ModelsPAK = CATHODE.Models.ModelPAK.Load(levelPath + "/RENDERABLE/LEVEL_MODELS.PAK");
            Result.ModelsMVR = new CATHODE.Models.ModelsMVR(levelPath + "/WORLD/MODELS.MVR");
            Result.RenderableREDS = CATHODE.Misc.RenderableElementsBIN.Load(levelPath + "/WORLD/REDS.BIN");
            Result.ShadersPAK = CATHODE.Shaders.ShadersPAK.Load(levelPath + "/RENDERABLE/LEVEL_SHADERS_DX11.PAK");
            //Result.ShadersBIN = TestProject.File_Handlers.Shaders.ShadersBIN.Load(levelPath + "/RENDERABLE/LEVEL_SHADERS_DX11_BIN.PAK");
            Result.ShadersIDXRemap = CATHODE.Shaders.IDXRemap.Load(levelPath + "/RENDERABLE/LEVEL_SHADERS_DX11_IDX_REMAP.PAK");

            Result.CollisionMap = CATHODE.Misc.CollisionMAP.Load(levelPath + "/WORLD/COLLISION.MAP");
            Result.EnvironmentMap = new CATHODE.Misc.EnvironmentMapBIN(levelPath + "/WORLD/ENVIRONMENTMAP.BIN");
            Result.PhysicsMap = CATHODE.Misc.PhysicsMAP.Load(levelPath + "/WORLD/PHYSICS.MAP");

            return Result;
        }
    }
}

public class alien_level
{
    public byte[] ModelsCST;
    public alien_mtl ModelsMTL;
    public alien_pak_model ModelsPAK;
    public alien_model_bin ModelsBIN;
    public CATHODE.Models.ModelsMVR ModelsMVR;

    public alien_textures GlobalTextures;
    public alien_textures LevelTextures;

    public alien_shader_pak ShadersPAK;
    public alien_shader_bin_pak ShadersBIN;
    public alien_shader_idx_remap ShadersIDXRemap;

    public alien_reds_bin RenderableREDS;

    //alien_mvr ModelsMVR;
    //alien_type_infos TypeInfos;
    //alien_commands_bin CommandsBIN;
    //alien_commands_pak CommandsPAK;
    //alien_commands Commands;
    //public alien_reds_bin WorldREDS; - this doesnt exist on win32
    public alien_resources_bin ResourcesBIN;

    public int MeshCount;

    public alien_physics_map PhysicsMap;
    public CATHODE.Misc.EnvironmentMapBIN EnvironmentMap;
    public alien_collision_map CollisionMap;

    //alien_animation_dat EnvironmentAnimation;

    //alien_pak2 GlobalAnimations;
    //alien_anim_string_db GlobalAnimationsStrings;
};