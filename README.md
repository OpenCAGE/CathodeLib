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

## Parsers currently available in CathodeLib:
- `CATHODE.AlphaLightLevel` handles level `WORLD/ALPHALIGHT_LEVEL.BIN` files
	- This is a A16B16G16R16F image with a specified resolution, baking alpha light data for the level
- `CATHODE.AnimationStrings` handles `ANIM_STRING_DB.BIN` and `ANIM_STRING_DB_DEBUG.BIN` files within `ANIMATION.PAK
	- This is a database of hashed animation-related strings, and their associated hashes
- `CATHODE.AnimClipDB` handles `ANIM_CLIP_DB.BIN` files within `ANIMATION.PAK`
	- This is a database of animation clips available by character type (WIP)
- `CATHODE.BML` handles any `.BML` files
    - Get/set content as an `XmlDocument` via `BML.Content`
- `CATHODE.CharacterAccessorySets` handles level `WORLD/CHARACTERACCESSORYSETS.BIN` files
	- This is a collection of metadata for Character entities within the level (e.g. gender, build, assets, etc)
- `CATHODE.CollisionMaps` handles level `WORLD/COLLISION.MAP` files
	- This defines COLLISION_MAPPING resource data for collision barriers, including zone metadata
- `CATHODE.Collisions` handles level `WORLD/COLLISION.BIN` files
	- This defines weighted collision data for character hitboxes
- `CATHODE.Commands` handles level `WORLD/COMMANDS.PAK` and/or `WORLD/COMMANDS.BIN` files
    - This consists of `Composite` scripts which hold various `Entity` types for logic
	    - `FunctionEntity` = functions which execute functionality, with parameters and links to child `Entity` objects
		- `VariableEntity` = variables which can be used externally as parameters on an instanced `Composite` via a `FunctionEntity`
		- `ProxyEntity` = a proxy of a `FunctionEntity` within another `Composite`, useful for acting on events in another composite
		- `OverrideEntity` = an override of a parameter value on an entity within an instanced `Composite` in this `Composite`
	- A `Utils` member provides various utilities for working with Commands data via data tables appended to the saved file
- `CATHODE.CustomCharacterAssetData` handles the `CHR_INFO/CUSTOMCHARACTERASSETDATA.BIN` file
	- This defines the tints and decals that character asset types use
- `CATHODE.CustomCharacterConstrainedComponents` handles the `CHR_INFO/CUSTOMCHARACTERCONSTRAINEDCOMPONENTS.BIN` file
	- This defines the character parameters that character component types use
- `CATHODE.CustomCharacterInfo` handles the `CHR_INFO/CUSTOMCHARACTERINFO.BIN` file
	- This defines base character presets, including skeletons and accessories
- `CATHODE.EnvironmentAnimations` handles level `WORLD/ENVIRONMENT_ANIMATION.DAT` files
	- This defines environmental animation data (WIP)
- `CATHODE.EnvironmentMaps` handles level `WORLD/ENVIRONMENTMAP.BIN` files
	- This maps instanced entities with environment map textures 
- `CATHODE.Lights` handles level `WORLD/LIGHTS.BIN` files
	- This defines various lighting data for instanced entities (although, modifying it appears to have no visual impact)
- `CATHODE.MaterialMappings` handles level `WORLD/MATERIAL_MAPPINGS.PAK` files
	- This defines material remappings, used for modifying materials on instanced entities (WIP)
- `CATHODE.Materials` handles level `RENDERABLE/LEVEL_MODELS.MTL` & `LEVEL_MODELS.CST` files
	- This defines material data, including texture pointers and constant buffers (WIP)
- `CATHODE.EXPERIMENTAL.MissionSave` handles `*.AIS` files
	- This handles save file data (WIP)
- `CATHODE.Models` handles level `RENDERABLE/LEVEL_MODELS.PAK` & `MODELS_LEVEL.BIN` files
	- This contains model mesh data in a CS2 format
- `CATHODE.MorphTargets` handles level `WORLD/MORPH_TARGET_DB.BIN` files
	- This defines morph data for faces (WIP)
- `CATHODE.Movers` handles level `WORLD/MODELS.MVR` files
	- This defines instance specific info for entities, including constant buffers and renderables (WIP)
- `CATHODE.EXPERIMENTAL.NavigationMesh` handles level `WORLD/STATE_*/NAV_MESH` files
	- This contains the navmesh for the level state, with associated properties (WIP)
- `CATHODE.PAK2` handles `UI.PAK` and `ANIMATIONS.PAK` files
	- This allows parsing of the PAK2 archive format, containing embedded files
- `CATHODE.PathBarrierResources` handles level `WORLD/PATH_BARRIER_RESOURCES` files
	- This defines the navmesh barrier resources (WIP)
- `CATHODE.PhysicsMaps` handles level `WORLD/PHYSICS.MAP` files
	- This defines DYNAMIC_PHYSICS_SYSTEM resource data for composite instances with physics systems
- `CATHODE.EXPERIMENTAL.ProgressionSave` handles `PROGRESSION.AIS` files
	- This handles progression save file data (WIP)
- `CATHODE.RadiosityInstanceMap` handles level `RENDERABLE/RADIOSITY_INSTANCE_MAP.TXT` files
	- This contains mappings between lightmap transforms in RADIOSITY_RUNTIME to resource indexes
- `CATHODE.RenderableElements` handles level `WORLD/REDS.BIN` files
	- This defines all renderable elements in a level - a model index with associated material index and count
- `CATHODE.Resources` handles level `WORLD/RESOURCES.BIN` files
	- This defines all available resources within the level
- `CATHODE.Shaders` handles level `RENDERABLE/LEVEL_SHADERS_DX11.PAK` & `LEVEL_SHADERS_DX11_BIN.PAK` & `LEVEL_SHADERS_DX11_IDX_REMAP.PAK` files
	- This defines all shaders within the level and additional metadata (WIP)
- `CATHODE.SkeleDB` handles the `ANIM_SYS/SKELE/DB.BIN` file within `ANIMATION.PAK`
	- This defines all available skeletons and mappings
- `CATHODE.SoundBankData` handles level `WORLD/SOUNDBANKDATA.DAT` files
	- This defines all soundbanks available to be utilised within the game
- `CATHODE.SoundDialogueLookups` handles level `WORLD/SOUNDDIALOGUELOOKUPS.DAT` files
	- This defines a lookup table for animation/sound hashes within soundbanks
- `CATHODE.SoundEnvironmentData` handles level `WORLD/SOUNDENVIRONMENTDATA.DAT` files
	- This defines all reverb types that are used within the level
- `CATHODE.SoundEventData` handles level `WORLD/SOUNDEVENTDATA.DAT` files
	- This defines all sound events available to be utilised within the game
- `CATHODE.SoundFlashModels` handles level `WORLD/SOUNDFLASHMODELS.DAT` files
	- This maps flash texture resource IDs with the model IDs that use them within the level
- `CATHODE.SoundLoadZones` handles level `WORLD/SOUNDLOADZONES.DAT` files
	- This specifies all soundbanks that are used within the level
- `CATHODE.SoundNodeNetwork` handles level `WORLD/SOUNDNODENETWORK.DAT` files
	- This defines the layout of the sound node network within the level, used for sound propagation (WIP)
- `CATHODE.TextDB` handles `*.TXT` files within `TEXT` directories
	- This defines a database of subtitles, which are saved per language
- `CATHODE.Textures` handles level `WORLD/LEVEL_TEXTURES.ALL.PAK` & `LEVEL_TEXTURE_HEADERS.ALL.BIN` files
	- This contains all textures used within the level with additional metadata
- `CATHODE.EXPERIMENTAL.Traversals` handles level `WORLD/STATE_*/TRAVERSAL` files
	- This contains traversal metadata for the level (WIP)

Check out a full overview of the Commands structure [on the Wiki](https://github.com/OpenCAGE/CathodeLib/wiki/Cathode-scripting-overview), and follow [this handy guide](https://github.com/OpenCAGE/CathodeLib/wiki/Creating-your-first-Cathode-script) to create your first script!
 
---

<p align="center">CathodeLib is in no way related to (or endorsed by) Creative Assembly or SEGA.</p>
