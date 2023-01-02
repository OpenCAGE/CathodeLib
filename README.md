<img src="https://i.imgur.com/TZNGZVA.jpg" width="100%">

# CathodeLib - Alien: Isolation C# Library

### CathodeLib is an open source library providing functionality to parse and write various formats from the Cathode game engine, for modding Alien: Isolation. Used to power [OpenCAGE](https://github.com/MattFiler/OpenCAGE)!

Available as a [NuGet package](https://www.nuget.org/packages/CathodeLib/), or alternatively just include this repo as a submodule in your project!

---

## For scripting:
- `CATHODE.Commands` handles `COMMANDS.PAK` files (for scripting)
    - The file consists of `Composite` scripts which hold various `Entity` types for logic
	    - `FunctionEntity` = functions which execute functionality, with parameters and links to child `Entity` objects
		- `VariableEntity` = variables which can be used externally as parameters on an instanced `Composite` via a `FunctionEntity`
		- `ProxyEntity` = a proxy of a `FunctionEntity` within another `Composite`, useful for acting on events in another composite
		- `OverrideEntity` = an override of a parameter value on an entity within an instanced `Composite` in this `Composite`
    
## For assets:
- `CATHODE.Assets.PAK2` handles `UI.PAK` and `ANIMATIONS.PAK` files
- `CATHODE.Assets.Models` handles `LEVEL_MODELS.PAK` files, paired with a `MODELS_LEVEL.BIN`
- `CATHODE.Assets.Textures` handles `LEVEL_TEXTURES.ALL.PAK` files, paired with a `LEVEL_TEXTURE_HEADERS.ALL.BIN`
- `CATHODE.Assets.MaterialMapping` handles `MATERIAL_MAPPINGS.PAK` files
- `CATHODE.Assets.Shaders` handles various `SHADERS` `PAK` files

## For level data and mappings:
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

## For configurations:
- `CATHODE.BML` handles any `.BML` files
    - Get/set content as an `XmlDocument` via `BML.Content`

## For saves:
- `CATHODE.ProgressionSave` handles `PROGRESSION.AIS` files
- `CATHODE.MissionSave` handles `PROGRESSION.AIS` files
