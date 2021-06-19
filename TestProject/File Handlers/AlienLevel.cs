using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace TestProject.File_Handlers
{
    public class AlienLevel
    {
        public static alien_level Load(string LEVEL_NAME)
        {
            alien_level Result = new alien_level();

            string levelPath = @"G:\SteamLibrary\steamapps\common\Alien Isolation\DATA\ENV\PRODUCTION\" + LEVEL_NAME;
            string rootPath = (LEVEL_NAME.ToUpper().Substring(0, 3) == "DLC") ? levelPath + "/.." : levelPath;

            Result.ModelsCST = File.ReadAllBytes(levelPath + "/RENDERABLE/LEVEL_MODELS.CST");
            Result.ModelsMTL = CATHODE.Models.ModelsMTL.Load(levelPath + "/RENDERABLE/LEVEL_MODELS.MTL");
            Result.ModelsPAK = CATHODE.Models.ModelPAK.Load(levelPath + "/RENDERABLE/LEVEL_MODELS.PAK");
            Result.ModelsBIN = CATHODE.Models.ModelBIN.Load(levelPath + "/RENDERABLE/MODELS_LEVEL.BIN");
            Result.ModelsMVR = new CATHODE.Models.ModelsMVR(levelPath + "/WORLD/MODELS.MVR");

            Result.GlobalTextures = CATHODE.Textures.TexturePAK.Load(rootPath + "/../../GLOBAL/WORLD/GLOBAL_TEXTURES.ALL.PAK", rootPath + "/../../GLOBAL/WORLD/GLOBAL_TEXTURES_HEADERS.ALL.BIN");
            Result.LevelTextures = CATHODE.Textures.TexturePAK.Load(levelPath + "/RENDERABLE/LEVEL_TEXTURES.ALL.PAK", levelPath + "/RENDERABLE/LEVEL_TEXTURE_HEADERS.ALL.BIN");

            Result.ShadersPAK = CATHODE.Shaders.ShadersPAK.Load(levelPath + "/RENDERABLE/LEVEL_SHADERS_DX11.PAK");
            //Result.ShadersBIN = TestProject.File_Handlers.Shaders.ShadersBIN.Load(levelPath + "/RENDERABLE/LEVEL_SHADERS_DX11_BIN.PAK");
            Result.ShadersIDXRemap = CATHODE.Shaders.IDXRemap.Load(levelPath + "/RENDERABLE/LEVEL_SHADERS_DX11_IDX_REMAP.PAK");

            Result.RenderableREDS = new CATHODE.Misc.RenderableElementsBIN(levelPath + "/WORLD/REDS.BIN");
            Result.ResourcesBIN = new CATHODE.Misc.ResourcesBIN(levelPath + "/WORLD/RESOURCES.BIN");
            Result.PhysicsMap = new CATHODE.Misc.PhysicsMAP(levelPath + "/WORLD/PHYSICS.MAP");
            Result.EnvironmentMap = new CATHODE.Misc.EnvironmentMapBIN(levelPath + "/WORLD/ENVIRONMENTMAP.BIN");
            Result.CollisionMap = new CATHODE.Misc.CollisionMAP(levelPath + "/WORLD/COLLISION.MAP");

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

    public CATHODE.Misc.RenderableElementsBIN RenderableREDS;
    public CATHODE.Misc.ResourcesBIN ResourcesBIN;
    public CATHODE.Misc.PhysicsMAP PhysicsMap;
    public CATHODE.Misc.EnvironmentMapBIN EnvironmentMap;
    public CATHODE.Misc.CollisionMAP CollisionMap;

    //alien_mvr ModelsMVR;
    //alien_type_infos TypeInfos;
    //alien_commands_bin CommandsBIN;
    //alien_commands_pak CommandsPAK;
    //alien_commands Commands;
    //public alien_reds_bin WorldREDS; - this doesnt exist on win32

    //alien_animation_dat EnvironmentAnimation;

    //alien_pak2 GlobalAnimations;
    //alien_anim_string_db GlobalAnimationsStrings;
};