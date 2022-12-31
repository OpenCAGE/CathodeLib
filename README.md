# CathodeLib

An open source library providing functionality to parse and write various formats from the Cathode engine, used for modding Alien: Isolation.

- For scripting:
    - `CATHODE.Commands` handles `COMMANDS.PAK` files
    
- For assets:
    - `CATHODE.Assets.PAK2` handles `UI.PAK` and `ANIMATIONS.PAK` files
    - `CATHODE.Assets.Models` handles `LEVEL_MODELS.PAK` files, paired with a `MODELS_LEVEL.BIN`
    - `CATHODE.Assets.Textures` handles `LEVEL_TEXTURES.ALL.PAK` files, paired with a `LEVEL_TEXTURE_HEADERS.ALL.BIN`
    - `CATHODE.Assets.MaterialMapping` handles `MATERIAL_MAPPINGS.PAK` files
    - `CATHODE.Assets.Shaders` handles various `SHADERS` `PAK` files

- For level data and mappings:
    - `CATHODE.MoverDatabase` handles `MODELS.MVR` files
    - `CATHODE.RenderableElementsDatabase` handles `REDS.BIN` files
    - `CATHODE.ResourcesDatabase` handles `RESOURCES.BIN` files
    - `CATHODE.MaterialDatabase` handles `MODELS.MTL` files
    - `CATHODE.EnvironmentMapDatabase` handles `ENVIRONMENTMAP.BIN` files
    - `CATHODE.EnvironmentAnimationDatabase` handles `ENVIRONMENT_ANIMATION.DAT` files
    - `CATHODE.PhysicsMapDatabase` handles `PHYSICS.MAP` files
    - `CATHODE.CollisionMapDatabase` handles `COLLISION.MAP` files
    - `CATHODE.AnimationStringDatabase` handles `ANIM_STRING_DB.BIN` and `ANIM_STRING_DB_DEBUG.BIN` files
    - `CATHODE.NavigationMesh` handles `NAV_MESH` files

- For saves:
    - `CATHODE.ProgressionSave` handles `PROGRESSION.AIS` files
    - `CATHODE.MissionSave` handles `PROGRESSION.AIS` files

- For configurations:
    - `CATHODE.BML.AlienBML` converts any `*.BML` files to XML
