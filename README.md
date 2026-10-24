<img src="https://github.com/user-attachments/assets/212954ff-1cb3-4ab0-942b-2e21ea86755f" width="100%" />

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

## Parsers available in CathodeLib:
- `CATHODE.AlphaLightLevel` handles level `WORLD/ALPHALIGHT_LEVEL.BIN` files
	- This is a A16B16G16R16F image with a specified resolution, baking alpha light data for the level
- `CATHODE.AnimationStrings` handles `ANIM_STRING_DB.BIN` and `ANIM_STRING_DB_DEBUG.BIN` files within `ANIMATION.PAK`
	- This is a database of hashed animation-related strings, and their associated hashes
- `CATHODE.AnimClipDB` handles `ANIM_CLIP_DB.BIN` files within `ANIMATION.PAK`
	- This is a database of animation clips and contexts available by character set
- `CATHODE.AnimClipDBSec` handles `ANIM_CLIP_DB_SEC_*.BIN` files within `ANIMATION.PAK`
	- This is a secondary database of animation clips and metadata per character set
- `CATHODE.AnimTreeDB` handles `ANIM_TREE_DB.BIN` files within `ANIMATION.PAK`
	- This is a database of animation blend trees available by character type
- `CATHODE.AssaultPositions` handles level `WORLD/STATE_*/ASSAULT_POSITIONS` files
	- This contains the spatial grid of assault positions for a level state used by NPCs in combat
- `CATHODE.BehaviorTreeDB` handles level `WORLD/BEHAVIOR_TREE.DB` files
	- This is a database of top-level behaviour trees used by characters
- `CATHODE.BML` handles any `.BML` files
	- Get/set content as an `XmlDocument` via `BML.Content`
- `CATHODE.CharacterAccessorySets` handles level `WORLD/CHARACTERACCESSORYSETS.BIN` files
	- This is a collection of metadata for Character entities within the level (e.g. gender, build, assets, etc)
- `CATHODE.CollisionMaps` handles level `WORLD/COLLISION.MAP` files
	- This defines `COLLISION_MAPPING` resource data for collision barriers, including zone metadata
- `CATHODE.Collisions` handles level `WORLD/COLLISION.BIN` files
	- This defines weighted collision data for character hitboxes
- `CATHODE.Commands` handles level `WORLD/COMMANDS.PAK` and/or `WORLD/COMMANDS.BIN` files
	- This consists of `Composite` scripts which hold various `Entity` types for logic
		- `FunctionEntity` = functions which execute functionality, with parameters and links to child `Entity` objects
		- `VariableEntity` = variables which can be used externally as parameters on an instanced `Composite` via a `FunctionEntity`
		- `ProxyEntity` = a proxy of a `FunctionEntity` within another `Composite`, useful for acting on events in another composite
		- `OverrideEntity` = an override of a parameter value on an entity within an instanced `Composite` in this `Composite`
	- A `Utils` member provides various utilities for working with Commands data via data tables appended to the saved file, as well as composite creation, duplication, de-instancing, and refactoring helpers
- `CATHODE.Cover` handles level `WORLD/STATE_*/COVER` files
	- This contains the cover grid for a level state used by NPCs
- `CATHODE.CustomCharacterAssetData` handles the `CHR_INFO/CUSTOMCHARACTERASSETDATA.BIN` file
	- This defines the tints and decals that character asset types use
- `CATHODE.CustomCharacterConstrainedComponents` handles the `CHR_INFO/CUSTOMCHARACTERCONSTRAINEDCOMPONENTS.BIN` file
	- This defines the character parameters that character component types use
- `CATHODE.CustomCharacterInfo` handles the `CHR_INFO/CUSTOMCHARACTERINFO.BIN` file
	- This defines base character presets, including skeletons and accessories
- `CATHODE.EnvironmentAnimations` handles level `WORLD/ENVIRONMENT_ANIMATION.DAT` files
	- This defines environmental animation data, inverse bind poses, and `RENDERABLE_INSTANCE` bindings for animated world props
- `CATHODE.GalaxyDefinition` handles level `WORLD/GALAXY/GALAXY.DEFINITION_BIN` files
	- This is a definition file used to define the generation of `GalaxyItems`
- `CATHODE.GalaxyItems` handles level `WORLD/GALAXY/GALAXY.ITEMS_BIN` files
	- This is a collection of items to form the stars in the galaxy, defined by `GalaxyDefinition`
- `CATHODE.GlobalAnimClipDB` handles the global `ANIM_CLIP_DB.BIN` file within `ANIMATION.PAK`
	- This is the global database of Havok animation clips and spline-compressed tracks
- `CATHODE.HavokPackfile` handles level `WORLD/COLLISION.HKX`, `COLLISION.HKX64`, `PHYSICS.HKX`, and `PHYSICS.HKX64` files (as well as Switch/mobile `TAG0` tagfiles)
	- This parses and writes Havok packfiles and tagfiles, supporting static compound shape editing, physics system import/export, and BVH rebuilding
- `CATHODE.Lights` handles level `WORLD/LIGHTS.BIN` files
	- This defines compiled lighting data and shadow/light properties for instanced light entities
- `CATHODE.MaterialMappings` handles level `WORLD/MATERIAL_MAPPINGS.PAK` files
	- This defines material remappings, used for modifying materials on instanced entities
- `CATHODE.Materials` handles level `RENDERABLE/LEVEL_MODELS.MTL` & `LEVEL_MODELS.CST` files
	- This defines material data, including texture pointers, shader references, and constant buffers
- `CATHODE.EXPERIMENTAL.MissionSave` handles `*.AIS` files
	- This handles mission save file data (WIP)
- `CATHODE.Models` handles level `RENDERABLE/LEVEL_MODELS.PAK` & `MODELS_LEVEL.BIN` files
	- This contains model mesh data and submesh headers in CS2 format
- `CATHODE.MorphTargets` handles level `WORLD/MORPH_TARGET_DB.BIN` files
	- This defines facial morph target data for character models
- `CATHODE.Movers` handles level `WORLD/MODELS.MVR` files
	- This defines instance-specific info for world entities, including transforms, constant buffers, lighting links, and renderables
- `CATHODE.NavigationMesh` handles level `WORLD/STATE_*/NAV_MESH` files
	- This contains the Detour navigation mesh, off-mesh links, and traversal barriers for the level state
- `CATHODE.OccluderTriangleBVH` handles level `WORLD/OCCLUDER_TRIANGLE_BVH.BIN` files
	- This defines the bounding volume hierarchy of occluder triangles used for visibility culling
- `CATHODE.PAK2` handles `UI.PAK` and `ANIMATION.PAK` files
	- This allows parsing and saving of the PAK2 archive format, containing embedded files
- `CATHODE.PathBarrierResources` handles level `WORLD/PATH_BARRIER_RESOURCES` files
	- This defines the navigation mesh barrier resources
- `CATHODE.PhysicsMaps` handles level `WORLD/PHYSICS.MAP` files
	- This defines `DYNAMIC_PHYSICS_SYSTEM` resource data for composite instances with physics systems
- `CATHODE.EXPERIMENTAL.ProgressionSave` handles `PROGRESSION.AIS` files
	- This handles progression save file data (WIP)
- `CATHODE.RadiosityCollisionMap` handles level `WORLD/RADIOSITY_COLLISION_MAPPING.BIN` files
	- This maps radiosity collision instances to level resource indices
- `CATHODE.RadiosityInstanceMap` handles level `RENDERABLE/RADIOSITY_INSTANCE_MAP.TXT` files
	- This contains mappings between lightmap transforms in `RADIOSITY_RUNTIME.BIN` and resource indices
- `CATHODE.RadiosityRuntime` handles level `RENDERABLE/RADIOSITY_RUNTIME.BIN` files
	- This contains runtime radiosity probe visibility slices, neighbour tables, lightmap fixups, and directional probe data
- `CATHODE.Ragdoll` handles `SKELE/RAGS` and `SKELE/RAGS64` files within `ANIMATION.PAK`
	- This parses and writes Havok ragdoll physics rigs (`hkaRagdollInstance`) for characters
- `CATHODE.RenderableElements` handles level `WORLD/REDS.BIN` files
	- This defines all renderable elements in a level - a model index with associated material index and count
- `CATHODE.Resources` handles level `WORLD/RESOURCES.BIN` files
	- This defines all available resources within the level
- `CATHODE.SelectionOverlayParams` handles the `SELECTIONOVERLAYPARAMS.BIN` file
	- This defines various properties for the overlay effect applied to highlighted models
- `CATHODE.Shaders` handles level `RENDERABLE/LEVEL_SHADERS_DX11.PAK`, `LEVEL_SHADERS_DX11_BIN.PAK`, & `LEVEL_SHADERS_DX11_IDX_REMAP.PAK` files
	- This defines all compiled DX11 vertex, pixel, and hull/domain shaders within the level and their ubershader metadata
- `CATHODE.Skeleton` handles `SKELE/SK` and `SKELE/SK64` files within `ANIMATION.PAK`
	- This defines character and prop skeletons (`hkaSkeleton`), bone/float hash maps, IK setups, and retargeting targets
- `CATHODE.SkeletonDB` handles the `ANIM_SYS/SKELE/DB.BIN` file within `ANIMATION.PAK`
	- This defines the master index of all available skeletons and retargeting mappings
- `CATHODE.SkeletonMapping` handles `SKELE/MAPS` and `SKELE/MAPS64` files within `ANIMATION.PAK`
	- This defines retargeting mappings (`hkaSkeletonMapper` pairs and bone index tables) between two skeletons
- `CATHODE.SoundBankData` handles level `WORLD/SOUNDBANKDATA.DAT` files
	- This defines all soundbanks available to be utilised within the level
- `CATHODE.SoundDialogueLookups` handles level `WORLD/SOUNDDIALOGUELOOKUPS.DAT` files
	- This defines a lookup table for dialogue animation/sound hashes within soundbanks
- `CATHODE.SoundEnvironmentData` handles level `WORLD/SOUNDENVIRONMENTDATA.DAT` files
	- This defines all reverb and acoustic environment types used within the level
- `CATHODE.SoundEventData` handles level `WORLD/SOUNDEVENTDATA.DAT` files
	- This defines all sound events available within the level's soundbanks
- `CATHODE.SoundNodeNetwork` handles level `WORLD/SOUNDNODENETWORK.DAT` files
	- This defines the layout of the sound node network within the level, used for acoustic propagation
- `CATHODE.SpottingPositions` handles level `WORLD/STATE_*/SPOTTING_POSITIONS` and `CRAWL_SPACE_SPOTTING_POSITIONS` files
	- This contains the spatial grid of standing and crawl-space spotting positions per level state used by NPCs
- `CATHODE.TextDB` handles `*.TXT` files within `TEXT` directories
	- This defines a database of localised strings and subtitles, saved per language
- `CATHODE.Textures` handles level `RENDERABLE/LEVEL_TEXTURES.ALL.PAK` & `WORLD/LEVEL_TEXTURE_HEADERS.ALL.BIN` files
	- This contains all textures used within the level with header and mipmap metadata

## High-Level Managers, Baking & Utilities

Beyond individual file parsers, `CathodeLib` provides high-level classes for loading, instancing, baking, porting, and merging entire levels:

- **`Level` & `Global`**: Centralised containers for loading and saving all files associated with a level (`DATA/ENV/*`) and global archives (`GLOBAL_TEXTURES.ALL.PAK` and `ANIMATION.PAK`), automatically keeping cross-file indices (models, materials, textures, shaders, and resources) synchronised on save.
- **`Animation` & `EnvironmentRigs`**: Loads, resolves cross-references across, and saves all formats inside `DATA/GLOBAL/ANIMATION.PAK` (pairing 32-bit and 64-bit skeletons, mappings, and ragdolls, plus spline encoding/decoding and retargeting), as well as multi-mesh environmental prop rigs (`EnvironmentRigs`).
- **`Instancing` & Level Bakers**: Evaluates a level's `Commands` composite hierarchy into resolved `InstancedComposite` and `InstancedEntity` trees (handling parameter inheritance, links, aliases, zones, and world transforms) and rebuilds the level's compiled runtime files:
	- **Navigation & AI Baking (`CathodeLib.NavMesh`)**: `NavMeshBaker`, `CoverBaker`, and `JobPositionBaker` generate per-state `NAV_MESH`, `COVER`, `SPOTTING_POSITIONS`, `CRAWL_SPACE_SPOTTING_POSITIONS`, and `ASSAULT_POSITIONS` files using an embedded port of Recast/Detour (`DotRecast`).
	- **Radiosity & AlphaLight Baking (`CathodeLib.Radiosity` & `CathodeLib.Alphalight`)**: `RadiosityBaker` raytraces level geometry and lights (via an embedded `NanoRT` BVH raytracer) to bake lightmap atlases, volume probes, visibility slices, `RADIOSITY_RUNTIME.BIN`, `RADIOSITY_INSTANCE_MAP.TXT`, and `RADIOSITY_COLLISION_MAPPING.BIN`. `AlphalightBaker` bakes `ALPHALIGHT_LEVEL.BIN`.
	- **Sound Network Generation (`CathodeLib.Sound`)**: `SoundNodeNetworkGenerator` builds `SOUNDNODENETWORK.DAT` propagation graphs across zones and portals.
	- **Ubershader Permutation Service (`CATHODE.Ubershaders`)**: `ShaderPermutationService` and `UberShaderRecompiler` generate and compile shader permutations when material or lighting feature flags change.
- **`CompositePorter` & `LevelMerger`**:
	- `CompositePorter` deep-copies composites from one `Level` into another, bringing along all referenced nested composites, models, materials, textures, shaders, environment animations, and Havok collision/physics shapes.
	- `LevelMerger` merges multiple modified copies of a level against a base level into a single unified level, tracking any conflicting edits.
- **`PatchManager` & `Utilities`**: Helpers for applying executable and configuration patches across game platforms (`PatchManager`), loading levels (`Utilities.LoadLevel`), and working with `ShortGuid` hashes (`ShortGuidUtils`).
 
---

<p align="center">CathodeLib is in no way related to (or endorsed by) Creative Assembly or SEGA.</p>

