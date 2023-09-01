<img src="https://i.imgur.com/TZNGZVA.jpg" width="100%">

# CathodeLib - Alien: Isolation C# Library

### CathodeLib is an open source library providing functionality to handle formats from the Cathode game engine, for modding Alien: Isolation. Used to power [OpenCAGE](https://github.com/MattFiler/OpenCAGE)!

Available as a [NuGet package](https://www.nuget.org/packages/CathodeLib/), or alternatively just include this repo as a submodule in your project!

---

All parsers inherit from a base `CathodeFile` class which provides:
 - A static `Implementation` flag, defining if the parser supports `CREATE`, `LOAD`, and/or `SAVE` functionality for the file. Parsers which support all three have the ability to generate files from scratch.
 - A `Loaded` bool, which is true if the parser has populated its values from a pre-existing file that it has loaded. If false, the parser is creating a new file on save.
 - A `Filepath` string, which is the filepath that the parser is using to either load or save the file.
 - A `Save` function, which will save the file out if the parser has the `SAVE` flag. This function can optionally be given a new filepath to save the file to. Returns false if saving fails.
 - Events for `OnLoadBegin`, `OnLoadSuccess`, `OnSaveBegin`, `OnSaveSuccess` which fire at load/save start and successful completion respectively, with the appropriate filepath as an arg.

Most parsers provide access to the file's content via an `Entries` parameter, however this can vary per implementation. 

Note: in debug mode the parsers will all fail hard, however in release mode all load/save calls are wrapped in try/catch statements.

**Parsers currently available in CathodeLib...**

## For scripting:
- `CATHODE.Commands` handles `COMMANDS.PAK` files
    - The file consists of `Composite` scripts which hold various `Entity` types for logic
	    - `FunctionEntity` = functions which execute functionality, with parameters and links to child `Entity` objects
		- `VariableEntity` = variables which can be used externally as parameters on an instanced `Composite` via a `FunctionEntity`
		- `ProxyEntity` = a proxy of a `FunctionEntity` within another `Composite`, useful for acting on events in another composite
		- `OverrideEntity` = an override of a parameter value on an entity within an instanced `Composite` in this `Composite`
		
Check out a full overview of the Commands structure [on the Wiki](https://github.com/OpenCAGE/CathodeLib/wiki/Cathode-scripting-overview), and follow [this handy guide](https://github.com/OpenCAGE/CathodeLib/wiki/Creating-your-first-Cathode-script) to create your first script!
    
## For assets:
- `CATHODE.PAK2` handles `UI.PAK` and `ANIMATIONS.PAK` files
- `CATHODE.Models` handles `LEVEL_MODELS.PAK` files, paired with a `MODELS_LEVEL.BIN`
- `CATHODE.Textures` handles `LEVEL_TEXTURES.ALL.PAK` files, paired with a `LEVEL_TEXTURE_HEADERS.ALL.BIN`
- `CATHODE.LEGACY.Assets.Shaders` handles various `SHADERS` `PAK` files (WIP)

## For level data and mappings:
- `CATHODE.Movers` handles `MODELS.MVR` files
- `CATHODE.RenderableElements` handles `REDS.BIN` files
- `CATHODE.Resources` handles `RESOURCES.BIN` files
- `CATHODE.Materials` handles `MODELS.MTL` files
- `CATHODE.MaterialMappings` handles `MATERIAL_MAPPINGS.PAK` files
- `CATHODE.EnvironmentMaps` handles `ENVIRONMENTMAP.BIN` files
- `CATHODE.EnvironmentAnimations` handles `ENVIRONMENT_ANIMATION.DAT` files
- `CATHODE.PathBarrierResources` handles `PATH_BARRIER_RESOURCES` files
- `CATHODE.PhysicsMaps` handles `PHYSICS.MAP` files
- `CATHODE.Lights` handles `LIGHTS.BIN` files
- `CATHODE.Collisions` handles `COLLISION.BIN` files
- `CATHODE.CollisionMaps` handles `COLLISION.MAP` files
- `CATHODE.AnimationStrings` handles `ANIM_STRING_DB.BIN` and `ANIM_STRING_DB_DEBUG.BIN` files
- `CATHODE.EXPERIMENTAL.NavigationMesh` handles `NAV_MESH` files (experimental)
- `CATHODE.SoundNodeNetwork` handles `SNDNODENETWORK.DAT` files
- `CATHODE.SoundLoadZones` handles `SOUNDLOADZONES.DAT` files
- `CATHODE.SoundFlashModels` handles `SOUNDFLASHMODELS.DAT` files
- `CATHODE.SoundEventData` handles `SOUNDEVENTDATA.DAT` files
- `CATHODE.SoundEnvironmentData` handles `SOUNDENVIRONMENTDATA.DAT` files
- `CATHODE.SoundDialogueLookups` handles `SOUNDDIALOGUELOOKUPS.DAT` files
- `CATHODE.SoundBankData` handles `SOUNDBANKDATA.DAT` files
- `CATHODE.CharacterAccessorySets` handles `CHARACTERACCESSORYSETS.BIN` files
- `CATHODE.CustomCharacterInfo` handles `CUSTOMCHARACTERINFO.BIN` files
- `CATHODE.CustomCharacterConstrainedComponents` handles `CUSTOMCHARACTERCONSTRAINEDCOMPONENTS.BIN` files
- `CATHODE.Strings` handles `*.TXT` files

## For configurations:
- `CATHODE.BML` handles any `.BML` files
    - Get/set content as an `XmlDocument` via `BML.Content`

## For saves:
- `CATHODE.ProgressionSave` handles `PROGRESSION.AIS` files
- `CATHODE.EXPERIMENTAL.MissionSave` handles `*.AIS` files (experimental)
 
---

<p align="center">CathodeLib is in no way related to (or endorsed by) Creative Assembly or SEGA.</p>
