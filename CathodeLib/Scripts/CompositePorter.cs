using CATHODE;
using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.Linq;

#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib
{
    /// <summary>
    /// Copies composites from one loaded level into another, bringing the level data they reference
    /// (renderables with their models, materials and textures; collision mappings with their Havok
    /// proxies; dynamic physics systems; animated models; the material mappings their instances select
    /// and the materials they name; the characters they spawn, with their accessory sets) and the editor
    /// metadata that travels in the COMMANDS custom tables (custom pin info, modification info, the purge
    /// list).
    ///
    /// One instance is one source-to-destination pairing. It remembers what it has already visited, so
    /// a composite reachable down several paths is copied once and a cyclic reference terminates, and it
    /// shares the Havok remap caches, so a proxy or physics system referenced by several composites is
    /// imported once.
    /// </summary>
    public class CompositePorter
    {
        public Level Source { get; }
        public Level Destination { get; }

        /// <summary>
        /// When a composite with the same ID already exists in the destination, replace it. Off keeps
        /// the destination copy and only walks into it for nested composites.
        /// </summary>
        public bool OverwriteComposites = true;

        /// <summary>
        /// Models, textures, materials and other named assets replace destination entries with the
        /// same name. Off keeps existing destination assets with matching names.
        /// </summary>
        public bool OverwriteAssets = false;

        /// <summary>
        /// Follow composite instances and port the composites they refer to as well - and the composites
        /// the engine finds without an instance: a character's DisplayModel, an NPC's accessory components.
        /// </summary>
        public bool Recurse = true;

        /// <summary>
        /// Composites that recursion stops at: reached as something's instance, they are neither copied
        /// nor walked into, on the understanding that the destination already has its own. Passing one
        /// to <see cref="Port"/> directly still ports it. Meant for GLOBAL and PAUSEMENU, which every
        /// level holds and which one composite's closure can reach through the required-asset scripts -
        /// a port that exists to carry a few composites elsewhere has no business replacing those.
        /// </summary>
        public HashSet<ShortGuid> DoNotDescendInto = new HashSet<ShortGuid>();

        /// <summary>
        /// Copy a ported composite's edit history (when it was last changed) along with it. Off for mods: their
        /// author's history isn't the history of the level they go into.
        /// </summary>
        public bool CarryEditHistory = true;

        /// <summary>
        /// Raised for each composite actually copied into the destination, with the source composite
        /// and its copy. OpenCAGE uses it to carry flowgraph layouts across, which live outside CathodeLib.
        /// </summary>
        public Action<Composite, Composite> OnCompositePorted;

        /// <summary>
        /// Raised frequently while porting, so a UI can pump its message loop.
        /// </summary>
        public Action OnProgress;

        /// <summary>
        /// Composites copied into the destination by this porter, in the order they were copied.
        /// </summary>
        public IReadOnlyList<Composite> PortedComposites => _ported;
        private readonly List<Composite> _ported = new List<Composite>();

        /// <summary>
        /// Composites walked (copied or already present), so the caller can tell how much of the source
        /// graph a port covered.
        /// </summary>
        public int VisitedCount => _visited.Count;
        private readonly HashSet<ShortGuid> _visited = new HashSet<ShortGuid>();

        public int RenderablesPorted { get; private set; }
        public int CollisionMappingsPorted { get; private set; }
        public int PhysicsSystemsPorted { get; private set; }
        public int AnimatedModelsPorted { get; private set; }
        public int EnvironmentMapsPorted { get; private set; }
        public int ColourLutTexturesPorted { get; private set; }
        public int MaterialMappingsPorted { get; private set; }
        public int NamedMaterialsPorted { get; private set; }
        public int CharacterCompositesPorted { get; private set; }
        public int AccessorySetsPorted { get; private set; }
        public int ResourcesSkipped { get; private set; }

        //Source Havok data offset to destination object, so shared proxies/systems are imported once
        private readonly Dictionary<uint, uint> _collisionRemap32 = new Dictionary<uint, uint>();
        private readonly Dictionary<uint, uint> _collisionRemap64 = new Dictionary<uint, uint>();
        private readonly Dictionary<uint, uint> _physicsRemap32 = new Dictionary<uint, uint>();
        private readonly Dictionary<uint, uint> _physicsRemap64 = new Dictionary<uint, uint>();

        /* Submesh.CollisionProxyIndex is persisted in the model file and names a compound by its place in
           COLLISION.HKX. Retail keeps it equal to the proxy the model's own collision mapping uses, on every
           model of every level; a port renumbers the compounds it imports, so the models it imports have
           to follow or they name another model's collision, or nothing (issue 706). Only models this porter
           brought in are touched - one the destination already had keeps the number that is right there. */
        private readonly Dictionary<int, int> _proxyIndexRemap = new Dictionary<int, int>();
        private readonly List<(int SourceIndex, Models.CS2.Component.LOD.Submesh Submesh)> _importedSubmeshes = new List<(int, Models.CS2.Component.LOD.Submesh)>();
        private readonly HashSet<object> _modelsBefore;

        public CompositePorter(Level source, Level destination)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (source.Commands == null || destination.Commands == null)
                throw new ArgumentException("Both levels must be loaded before porting.");
            if (ReferenceEquals(source, destination))
                throw new ArgumentException("Cannot port a level into itself.");

            Source = source;
            Destination = destination;
            _modelsBefore = new HashSet<object>(destination.Models?.Entries ?? new List<Models.CS2>(), new ReferenceEqualityComparer());
        }

        /// <summary>
        /// Port one composite, and (when <see cref="Recurse"/> is set) everything it instances.
        /// </summary>
        public void Port(Composite composite)
        {
            if (composite == null) throw new ArgumentNullException(nameof(composite));
            using (new ImportMemoScope(Source, Destination))
            {
                PortRecursive(composite);
                PortAccessorySets(composite);
                ApplyProxyIndexRemap();
            }
        }

        /// <summary>
        /// Port every composite in the source level.
        /// </summary>
        public void PortAll()
        {
            using (new ImportMemoScope(Source, Destination))
            {
                foreach (Composite composite in Source.Commands.Entries.ToList())
                {
                    if (composite != null)
                        PortRecursive(composite);
                }
                if (Source.Commands.EntryPoints?[0] != null)
                    PortAccessorySets(Source.Commands.EntryPoints[0]);
                ApplyProxyIndexRemap();
            }
        }

        /// <summary>
        /// Bring over the level data some entities copied from the source refer to - renderables with their models,
        /// materials and textures, collision, physics, animated models, an environment map's cubemap, the material
        /// mappings they select and the materials they name - and point their references at the destination's objects.
        /// For merging entity by entity rather than whole composites: the entities are the caller's own copies (Copy()
        /// of the source's), in or bound for a destination composite.
        /// </summary>
        public void PortEntityResources(IEnumerable<Entity> copiedEntities)
        {
            using (new ImportMemoScope(Source, Destination))
            {
                foreach (Entity entity in copiedEntities)
                {
                    if (entity is FunctionEntity function)
                    {
                        if (function.resources != null)
                            CopyResources(function.resources);
                        PortEnvironmentMapTexture(function);
                        PortColourLutTextures(function);
                    }
                    Parameter resources = entity.GetParameter("resource");
                    if (resources?.content is cResource resourceParam && resourceParam.value != null)
                        CopyResources(resourceParam.value);
                    PortMappingsAndNamedMaterials(entity);
                }
                ApplyProxyIndexRemap();
            }
        }

        /// <summary>
        /// Bring over what an entity names rather than references - the material mappings it selects, the materials its
        /// strings name, an environment map's cubemap, a colour grade's LUTs - for an entity whose parameters changed
        /// but whose resources didn't. Its resources aren't touched: they may already point at the destination's objects.
        /// </summary>
        public void PortNamedAssets(Entity entity)
        {
            if (entity == null)
                return;
            using (new ImportMemoScope(Source, Destination))
            {
                if (entity is FunctionEntity function)
                {
                    PortEnvironmentMapTexture(function);
                    PortColourLutTextures(function);
                }
                PortMappingsAndNamedMaterials(entity);
            }
        }

        /// <summary>Port resource references copied from the source (a changed resource parameter, say), in place.</summary>
        public void PortResourceReferences(List<ResourceReference> copiedReferences)
        {
            if (copiedReferences == null)
                return;
            using (new ImportMemoScope(Source, Destination))
            {
                CopyResources(copiedReferences);
                ApplyProxyIndexRemap();
            }
        }

        /* One port's worth of memo on the destination's model, material and texture tables (see Models.PortMemo): a
           source object imported once is not imported again. The source's Havok files keep their object boundaries for
           the port too (see HavokPackfile.CacheRangeBoundaries) - a port only ever reads them. Only what this scope
           switched on is switched off. */
        private sealed class ImportMemoScope : IDisposable
        {
            private readonly Models _models;
            private readonly Materials _materials;
            private readonly Textures _textures;
            private readonly List<HavokPackfile> _havok = new List<HavokPackfile>();

            public ImportMemoScope(Level source, Level destination)
            {
                foreach (HavokPackfile file in new[] { source.CollisionHKX, source.CollisionHKX64, source.PhysicsHKX, source.PhysicsHKX64 })
                {
                    if (file != null && !file.CacheRangeBoundaries && !_havok.Contains(file))
                    {
                        file.CacheRangeBoundaries = true;
                        _havok.Add(file);
                    }
                }

                if (destination.Models != null && destination.Models.PortMemo == null)
                {
                    _models = destination.Models;
                    _models.PortMemo = new System.Runtime.CompilerServices.ConditionalWeakTable<object, object>();
                }
                if (destination.Materials != null && destination.Materials.PortMemo == null)
                {
                    _materials = destination.Materials;
                    _materials.PortMemo = new System.Runtime.CompilerServices.ConditionalWeakTable<object, object>();
                }
                if (destination.Textures != null && destination.Textures.PortMemo == null)
                {
                    _textures = destination.Textures;
                    _textures.PortMemo = new System.Runtime.CompilerServices.ConditionalWeakTable<object, object>();
                }
            }

            public void Dispose()
            {
                foreach (HavokPackfile file in _havok)
                    file.CacheRangeBoundaries = false;
                if (_models != null) _models.PortMemo = null;
                if (_materials != null) _materials.PortMemo = null;
                if (_textures != null) _textures.PortMemo = null;
            }
        }

        private void ApplyProxyIndexRemap()
        {
            if (_importedSubmeshes.Count == 0)
                return;

            //By reference, once: FindModel is a linear scan that compares submeshes by value
            var ownerOf = new Dictionary<object, Models.CS2>(new ReferenceEqualityComparer());
            foreach (Models.CS2 cs2 in Destination.Models?.Entries ?? new List<Models.CS2>())
                foreach (Models.CS2.Component component in cs2.Components)
                    foreach (Models.CS2.Component.LOD lod in component.LODs)
                        foreach (Models.CS2.Component.LOD.Submesh sub in lod.Submeshes)
                            if (sub != null && !ownerOf.ContainsKey(sub))
                                ownerOf[sub] = cs2;

            foreach ((int sourceIndex, Models.CS2.Component.LOD.Submesh submesh) in _importedSubmeshes)
            {
                if (submesh == null || sourceIndex < 0 || !_proxyIndexRemap.TryGetValue(sourceIndex, out int destIndex))
                    continue;
                if (!ownerOf.TryGetValue(submesh, out Models.CS2 owner) || _modelsBefore.Contains(owner))
                    continue;
                submesh.CollisionProxyIndex = destIndex;
            }
            _importedSubmeshes.Clear();
        }

        private void CollectImportedSubmeshes(List<RenderableElements.Element> source, List<RenderableElements.Element> imported)
        {
            if (source == null || imported == null)
                return;
            for (int i = 0; i < source.Count && i < imported.Count; i++)
            {
                if (source[i]?.Model != null && imported[i]?.Model != null)
                    _importedSubmeshes.Add((source[i].Model.CollisionProxyIndex, imported[i].Model));
                CollectImportedSubmeshes(source[i]?.LODs, imported[i]?.LODs);
            }
        }

        private void PortRecursive(Composite composite)
        {
            //Only walk each composite once. Skipping the copy for one already handled is not enough:
            //a composite reachable down several paths would be descended into once per path, so porting
            //MISSIONS_TechHub walked 21,829 composites instead of 339. This also makes a cyclic reference
            //terminate instead of overflowing the stack.
            if (!_visited.Add(composite.shortGUID)) return;

            //Check to see if the composite already exists at our destination
            Composite dest = Destination.Commands.GetComposite(composite.shortGUID);

            //If overwriting and there is an existing matching comp in the destination, delete it
            if (OverwriteComposites)
            {
                if (dest != null)
                    Destination.Commands.Entries.Remove(dest);
                dest = null;
            }

            //Copy composite and bring over the resources referenced by it
            if (dest == null)
            {
                Composite copiedComp = composite.Copy();
                Destination.Commands.Entries.Add(copiedComp);
                OnProgress?.Invoke();

                foreach (FunctionEntity ent in copiedComp.functions)
                {
                    if (ent.resources != null)
                        CopyResources(ent.resources);

                    Parameter resources = ent.GetParameter("resource");
                    if (resources?.content is cResource resourceParam && resourceParam.value != null)
                        CopyResources(resourceParam.value);

                    PortEnvironmentMapTexture(ent);
                    PortColourLutTextures(ent);
                }
                foreach (Entity ent in copiedComp.GetEntities())
                    PortMappingsAndNamedMaterials(ent);

                //Bring over generic metadata
                //NOTE: entity names travel with the entities themselves (as a 'name' parameter)
                Destination.Commands.Utils.AddCustomPinInfos(copiedComp, Source.Commands.Utils.GetAllCustomPinInfo(composite));
                Destination.Commands.Utils.ReplaceColourParameters(copiedComp.shortGUID, Source.Commands.Utils.GetColourParameters(composite));
                if (CarryEditHistory)
                    Destination.Commands.Utils.SetModificationInfo(Source.Commands.Utils.GetModificationInfo(composite));
                Destination.Commands.Utils.PurgedComposites.purged.Remove(copiedComp.shortGUID); //mark for re-purge

                _ported.Add(copiedComp);
                OnCompositePorted?.Invoke(composite, copiedComp);
            }

            //If recursing, follow any composite instances through, and copy those too
            if (!Recurse) return;
            foreach (FunctionEntity ent in composite.functions)
            {
                if (ent.function.IsFunctionType) continue;
                if (DoNotDescendInto.Contains(ent.function)) continue;

                Composite nestedComp = Source.Commands.GetComposite(ent.function);
                if (nestedComp != null)
                    PortRecursive(nestedComp);
            }
            PortCharacters(composite);
        }

        /* Characters are not instanced. The engine builds one from the composite named "DisplayModel:<name>", where <name>
           is a string the script hands it (Character.display_model, RegisterCharacterModel, a variable feeding either). No
           instance leads to one, so porting ChallengeMap3's root into a new level brought none of its 13: the player and the
           androids started spawning and never finished, and the mission waited for them behind a black frame. They are
           followed here like instances - their ModelReferences bring the character models, weighted collisions and animated
           models along. Any string can carry the name on its way to a Character, so any string naming one counts. */
        private void PortCharacters(Composite composite)
        {
            if (_sourceDisplayModels == null)
            {
                _sourceDisplayModels = new Dictionary<string, Composite>(StringComparer.OrdinalIgnoreCase);
                foreach (Composite candidate in Source.Commands.Entries)
                {
                    if (candidate?.name == null || !candidate.name.StartsWith(DisplayModelPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string character = candidate.name.Substring(DisplayModelPrefix.Length);
                    if (!_sourceDisplayModels.ContainsKey(character))
                        _sourceDisplayModels.Add(character, candidate);
                }
            }
            if (_sourceDisplayModels.Count == 0)
                return;

            foreach (Entity entity in composite.GetEntities())
            {
                foreach (Parameter parameter in entity.parameters)
                {
                    if (parameter.content is cString text && !string.IsNullOrEmpty(text.value) &&
                        _sourceDisplayModels.TryGetValue(text.value, out Composite displayModel))
                        PortCharacterComposite(displayModel);
                }
            }
        }
        private const string DisplayModelPrefix = "DisplayModel:";
        private Dictionary<string, Composite> _sourceDisplayModels = null;

        /* A custom-character NPC is dressed from the component composites its WORLD/CHARACTERACCESSORYSETS.BIN row names (12 on
           ChallengeMap3, none of them instanced by anything), and the row is keyed by the character entity and the composite
           instance it sits in - an id hashed from the hop path down from the level root. Every row for an NPC inside the
           ported composite comes across with its components, keyed by the path from that composite down: once the composite
           is placed in its new level, instancing finds the instance whose path ends with that one and completes the key
           (Instancing.RekeyAccessorySets). Rows for NPCs elsewhere in the source stay behind. */
        private void PortAccessorySets(Composite root)
        {
            if (!Recurse || DoNotDescendInto.Contains(root.shortGUID) || Destination.AccessorySets == null || Source.AccessorySets?.Entries == null)
                return;

            //Below the source's own root the keys already are paths from the composite being ported
            bool levelRoot = root.shortGUID == Source.Commands.EntryPoints?[0]?.shortGUID;
            foreach (CharacterAccessorySets.CharacterAttributes row in Source.AccessorySets.Entries)
            {
                if (row?.character == null)
                    continue;

                ShortGuid key = row.character.composite_instance_id;
                if (!levelRoot)
                {
                    List<ShortGuid> below = HopsBelow(root, key);
                    if (below == null)
                        continue;
                    key = below.GenerateCompositeInstanceID(false);
                }

                foreach (CharacterAccessorySets.CharacterAttributes.Components.Component component in new[] {
                    row.components.Torso, row.components.Legs, row.components.Shoes, row.components.Head, row.components.Arms, row.components.Collision })
                {
                    Composite componentComposite = component.Composite == ShortGuid.Invalid ? null : Source.Commands.GetComposite(component.Composite);
                    if (componentComposite != null)
                        PortCharacterComposite(componentComposite);
                }
                CarryAccessorySet(row, key);
            }
        }

        /* The hops below the instance of root on the source path whose composite instance id is instanceId, or null when that
           path does not pass through one. The source's paths are walked once per port, keeping only those a row is keyed by. */
        private List<ShortGuid> HopsBelow(Composite root, ShortGuid instanceId)
        {
            if (_accessorySetPaths == null)
            {
                _accessorySetPaths = new Dictionary<ShortGuid, (List<ShortGuid> Hops, List<ShortGuid> Opened)>();
                HashSet<ShortGuid> wanted = new HashSet<ShortGuid>(Source.AccessorySets.Entries.Where(o => o?.character != null).Select(o => o.character.composite_instance_id));
                Stack<(Composite Composite, List<ShortGuid> Hops, List<ShortGuid> Opened)> todo = new Stack<(Composite, List<ShortGuid>, List<ShortGuid>)>();
                if (Source.Commands.EntryPoints?[0] != null)
                    todo.Push((Source.Commands.EntryPoints[0], new List<ShortGuid>(), new List<ShortGuid>()));
                while (todo.Count != 0 && wanted.Count != 0)
                {
                    (Composite composite, List<ShortGuid> hops, List<ShortGuid> opened) = todo.Pop();
                    if (hops.Count != 0)
                    {
                        ShortGuid id = hops.GenerateCompositeInstanceID(false);
                        if (wanted.Remove(id))
                            _accessorySetPaths.Add(id, (hops, opened));
                    }
                    if (hops.Count >= 64)
                        continue;
                    foreach (FunctionEntity function in composite.functions)
                    {
                        if (function.function.IsFunctionType)
                            continue;
                        Composite child = Source.Commands.GetComposite(function.function);
                        if (child != null)
                            todo.Push((child, new List<ShortGuid>(hops) { function.shortGUID }, new List<ShortGuid>(opened) { child.shortGUID }));
                    }
                }
            }

            if (!_accessorySetPaths.TryGetValue(instanceId, out (List<ShortGuid> Hops, List<ShortGuid> Opened) path))
                return null;
            int at = path.Opened.IndexOf(root.shortGUID);
            return at < 0 ? null : path.Hops.GetRange(at + 1, path.Hops.Count - at - 1);
        }
        private Dictionary<ShortGuid, (List<ShortGuid> Hops, List<ShortGuid> Opened)> _accessorySetPaths = null;

        private void PortCharacterComposite(Composite composite)
        {
            if (DoNotDescendInto.Contains(composite.shortGUID))
                return;
            int before = _ported.Count;
            PortRecursive(composite);
            if (_ported.Count > before && _ported[before].shortGUID == composite.shortGUID)
                CharacterCompositesPorted++;
        }

        private void CarryAccessorySet(CharacterAccessorySets.CharacterAttributes row, ShortGuid compositeInstanceId)
        {
            if (Destination.AccessorySets.Entries.Any(o => o?.character != null && o.character.entity_id == row.character.entity_id &&
                                                           o.character.composite_instance_id == compositeInstanceId))
                return;
            CharacterAccessorySets.CharacterAttributes copy = row.Copy();
            copy.character = new EntityHandle() { entity_id = row.character.entity_id, composite_instance_id = compositeInstanceId };
            Destination.AccessorySets.Entries.Add(copy);
            AccessorySetsPorted++;
        }

        /* A composite instance's 'mapping' resource selects a WORLD/MATERIAL_MAPPINGS.PAK row by its ID, and instancing remaps
           every model below the instance through it (MaterialRemappingUtils.ApplyMapping). The row's targets, a
           ModelReference's 'material' override, and the string a variable feeds into one (ChallengeMap3's
           RandomStaticVDUscreen picks its screen that way) are all looked up by NAME. No resource leads to any of them:
           ported from ChallengeMap3 into a level made from FRONTEND, 11 of its 20 mappings were missing from the campaign
           table and 362 movers rendered on other materials (its orange/grey walls came out white). The row comes across as
           it is, and with it every source material answering to each name it remaps to or a string gives - all of them,
           because FindMaterialByName chooses among same-named twins by radiosity class and vertex format, and the
           destination needs the candidates the source had. */
        private void PortMappingsAndNamedMaterials(Entity entity)
        {
            if (Destination.Materials == null || Source.Materials == null)
                return;

            foreach (Parameter parameter in entity.parameters)
            {
                if (parameter.name == ShortGuids.mapping && parameter.content is cResource mapping)
                    PortMaterialMapping(mapping.shortGUID);
                else if (parameter.name != ShortGuids.name && parameter.content is cString text) //'name' is the entity's own
                    PortMaterialsNamed(text.value);
            }
            OnProgress?.Invoke();
        }

        private void PortMaterialMapping(ShortGuid id)
        {
            if (id == ShortGuid.Invalid || Source.MaterialMappings == null || Destination.MaterialMappings == null || !_mappingsVisited.Add(id))
                return;

            MaterialMappings.MaterialMapping source = Source.MaterialMappings.Entries.FirstOrDefault(o => o.ID == id);
            if (source == null)
                return;

            //A destination row of the same name stays unless assets are overwritten, as any named asset does
            int before = Destination.MaterialMappings.Entries.Count;
            MaterialMappings.MaterialMapping imported = Destination.MaterialMappings.ImportEntry(source, OverwriteAssets);
            if (Destination.MaterialMappings.Entries.Count != before)
                MaterialMappingsPorted++;
            foreach (MaterialMappings.MaterialMapping.Mapping row in imported?.Mappings ?? source.Mappings)
                PortMaterialsNamed(row?.to);
        }
        private readonly HashSet<ShortGuid> _mappingsVisited = new HashSet<ShortGuid>();

        private void PortMaterialsNamed(string name)
        {
            if (string.IsNullOrEmpty(name) || !_materialNamesVisited.Add(name))
                return;

            //Most strings name no material: answer that from the source's names before scanning its table
            if (_sourceMaterialNames == null)
            {
                _sourceMaterialNames = new HashSet<string>();
                foreach (Materials.Material material in Source.Materials.Entries)
                {
                    if (string.IsNullOrEmpty(material?.Name))
                        continue;
                    _sourceMaterialNames.Add(MaterialRemappingUtils.NormalizeMaterialNameForLookup(material.Name));
                    _sourceMaterialNames.Add(MaterialRemappingUtils.NormalizeMaterialNameForLookup(MaterialRemappingUtils.StripAuthoringSlot(material.Name)));
                }
            }
            if (!_sourceMaterialNames.Contains(MaterialRemappingUtils.NormalizeMaterialNameForLookup(name)) &&
                !_sourceMaterialNames.Contains(MaterialRemappingUtils.NormalizeMaterialNameForLookup(MaterialRemappingUtils.StripAuthoringSlot(name))))
                return;

            foreach (Materials.Material material in MaterialRemappingUtils.MaterialsAnsweringTo(Source.Materials, name).ToList())
            {
                int before = Destination.Materials.Entries.Count;
                Destination.Materials.ImportEntry(material, OverwriteAssets);
                if (Destination.Materials.Entries.Count != before)
                    NamedMaterialsPorted++;
            }
            OnProgress?.Invoke();
        }
        private readonly HashSet<string> _materialNamesVisited = new HashSet<string>();
        private HashSet<string> _sourceMaterialNames = null;

        /* An EnvironmentMap names its cubemap by a Texture path parameter rather than by a resource,
           so CopyResources never sees it: Commands.Save resolves the path level-pak first, then global,
           and bakes the answer into Texture_Index. A cubemap that only the source level pak holds
           would resolve to nothing in the destination, so it comes across by name the way a material's
           textures do. Stock cubemaps are already in every level (absorbed from global on load) and
           dedupe by name. */
        private void PortEnvironmentMapTexture(FunctionEntity entity)
        {
            if (entity == null || !entity.function.IsFunctionType || entity.function.AsFunctionType != FunctionType.EnvironmentMap)
                return;
            if (Destination.Textures == null)
                return;

            string path = (entity.GetParameter(ShortGuids.Texture)?.content as cString)?.value;
            if (string.IsNullOrEmpty(path))
                return;

            Textures.TEX4 texture = Source.Textures?.GetEnvironmentMapByPath(path)
                ?? Source.Global?.Textures?.GetEnvironmentMapByPath(path);
            if (texture == null)
                return;

            Destination.Textures.ImportEntry(texture, OverwriteAssets);
            EnvironmentMapsPorted++;
            OnProgress?.Invoke();
        }

        /* A ColourCorrectionTransition names its colour-grading LUTs by path (colour_lut_a / colour_lut_b) and the engine
           grades with the LEVEL texture pak index Commands.Save bakes from them. No material references a LUT, so nothing
           else brings one: a level made from FRONTEND by MakeNewLevelFrom (which ports GLOBAL, and so GlobalPostProcessing,
           through here) had none in its pak, and its baked indices named camera_frost_threshold.tga. They come across from
           the source's level pak by name; one the destination already holds dedupes. */
        private void PortColourLutTextures(FunctionEntity entity)
        {
            if (entity == null || !entity.function.IsFunctionType || entity.function.AsFunctionType != FunctionType.ColourCorrectionTransition)
                return;
            if (Destination.Textures == null || Source.Textures == null)
                return;

            foreach (ShortGuid parameter in new[] { ShortGuids.colour_lut_a, ShortGuids.colour_lut_b })
            {
                string path = (entity.GetParameter(parameter)?.content as cString)?.value;
                if (string.IsNullOrEmpty(path))
                    continue;

                Textures.TEX4 lut = Source.Textures.GetEnvironmentMapByPath(path);
                if (lut == null)
                    continue;

                int before = Destination.Textures.Entries.Count;
                Destination.Textures.ImportEntry(lut, OverwriteAssets);
                if (Destination.Textures.Entries.Count != before)
                    ColourLutTexturesPorted++;
            }
            OnProgress?.Invoke();
        }

        private void CopyResources(List<ResourceReference> resourceRefs)
        {
            for (int i = 0; i < resourceRefs.Count; i++)
            {
                switch (resourceRefs[i].resource_type)
                {
                    case ResourceType.ANIMATED_MODEL:
                        resourceRefs[i].AnimatedModel = Destination.EnvironmentAnimations.ImportEntry(resourceRefs[i].AnimatedModel);
                        AnimatedModelsPorted++;
                        break;
                    case ResourceType.RENDERABLE_INSTANCE:
                        List<RenderableElements.Element> sourceElements = resourceRefs[i].RenderableInstance;
                        resourceRefs[i].RenderableInstance = Destination.RenderableElements.ImportEntry(sourceElements, Source.Models, OverwriteAssets);
                        CollectImportedSubmeshes(sourceElements, resourceRefs[i].RenderableInstance);
                        RenderablesPorted++;
                        break;
                    case ResourceType.COLLISION_MAPPING:
                        PortCollisionMapping(resourceRefs[i]);
                        CollisionMappingsPorted++;
                        CollectIfHavokGarbageGrew();
                        break;
                    case ResourceType.DYNAMIC_PHYSICS_SYSTEM:
                        PortDynamicPhysicsSystem(resourceRefs[i]);
                        PhysicsSystemsPorted++;
                        CollectIfHavokGarbageGrew();
                        break;
                    case ResourceType.TRAVERSAL_SEGMENT:
                    case ResourceType.NAV_MESH_BARRIER_RESOURCE:
                    case ResourceType.EXCLUSIVE_MASTER_STATE_RESOURCE:
                        //Regenerated when the destination is built
                        break;
                    default:
                        ResourcesSkipped++;
                        Console.WriteLine("CompositePorter: skipping resource type " + resourceRefs[i].resource_type);
                        break;
                }
                OnProgress?.Invoke();
            }
        }

        /* Each Havok import grows the destination's payload into a new array, whole: a large-object-heap allocation the
           size of the file so far, once per collision mapping and physics system ported. The GC let that garbage run far
           ahead of the live data - exporting SCI_HospitalUpper's UpperDeck_SectionA (428,000 functions) took OpenCAGE from
           7.5 GB to 19.5 GB in half a minute and, unguarded, to 61 GB and the whole machine's commit limit. A full
           collection once the managed heap has grown 2 GB since the last one keeps it to the live data plus that. */
        private long _heapAtLastCollect = -1;
        private const long HavokGarbageAllowance = 2L << 30;

        private void CollectIfHavokGarbageGrew()
        {
            long now = GC.GetTotalMemory(false);
            if (_heapAtLastCollect < 0)
            {
                _heapAtLastCollect = now;
                return;
            }
            if (now - _heapAtLastCollect < HavokGarbageAllowance)
                return;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
            _heapAtLastCollect = GC.GetTotalMemory(false);
        }

        private void PortCollisionMapping(ResourceReference resource)
        {
            CollisionMaps.COLLISION_MAPPING srcMap = resource.CollisionMapping;
            HavokPackfile.StaticCompoundShape remappedProxy = null;
            if (srcMap?.CollisionProxy != null)
                remappedProxy = ImportCollisionProxyPair(srcMap.CollisionProxy);
            if (srcMap?.CollisionProxy != null && remappedProxy != null)
                _proxyIndexRemap[srcMap.CollisionProxy.ProxyIndex] = remappedProxy.ProxyIndex;
            resource.CollisionMapping = Destination.CollisionMaps.ImportEntry(srcMap, remappedProxy, OverwriteAssets);
        }

        private HavokPackfile.StaticCompoundShape ImportCollisionProxyPair(HavokPackfile.StaticCompoundShape sourceProxy)
        {
            if (sourceProxy == null)
                return null;

            HavokPackfile src32 = Source.CollisionHKX;
            HavokPackfile src64 = Source.CollisionHKX64;
            HavokPackfile dst32 = Destination.CollisionHKX;
            HavokPackfile dst64 = Destination.CollisionHKX64;

            HavokPackfile.StaticCompoundShape imported32 = null, imported64 = null;
            if (src32 != null && dst32 != null)
                imported32 = dst32.ImportStaticCompoundShape(src32, sourceProxy, _collisionRemap32);
            else if (src32 != null && dst32 == null)
                Console.WriteLine("CompositePorter: destination level has no COLLISION.HKX - cannot import collision proxy.");

            if (src64 != null && dst64 != null)
            {
                HavokPackfile.StaticCompoundShape source64 = src64.GetCompound(sourceProxy.ProxyIndex);
                if (source64 != null)
                {
                    try
                    {
                        imported64 = dst64.ImportStaticCompoundShape(src64, source64, _collisionRemap64);
                    }
                    catch (Exception ex) when (dst32 != null)
                    {
                        //Beside a 32-bit file that one stays the answer; alone (iOS, Switch) this failure is the port's
                        Console.WriteLine("CompositePorter: COLLISION.HKX64 import failed: " + ex.Message);
                    }
                }
                else
                {
                    Console.WriteLine("CompositePorter: no matching COLLISION.HKX64 compound for proxy " + sourceProxy.ProxyIndex);
                }
            }
            else if (src64 != null && dst32 == null)
                Console.WriteLine("CompositePorter: destination level has no COLLISION.HKX64 - cannot import collision proxy.");

            //The mobile and Switch builds ship only the 64-bit file, so its proxy is the one the row must name
            return dst32 != null ? imported32 : imported64;
        }

        private void PortDynamicPhysicsSystem(ResourceReference resource)
        {
            HavokPackfile.PhysicsSystem srcSystem = resource.PhysicsSystem;
            if (srcSystem == null && resource.PhysicsSystemIndex >= 0)
                srcSystem = Source.Physics?.GetPhysicsSystem(resource.PhysicsSystemIndex);

            if (srcSystem == null)
            {
                Console.WriteLine("CompositePorter: DYNAMIC_PHYSICS_SYSTEM has no bound PhysicsSystem - leaving as-is.");
                return;
            }

            HavokPackfile.PhysicsSystem imported = ImportPhysicsSystemPair(srcSystem);
            resource.PhysicsSystem = imported;
            resource.PhysicsSystemIndex = imported?.SystemIndex ?? -1;
        }

        private HavokPackfile.PhysicsSystem ImportPhysicsSystemPair(HavokPackfile.PhysicsSystem sourceSystem)
        {
            if (sourceSystem == null)
                return null;

            HavokPackfile src32 = Source.PhysicsHKX;
            HavokPackfile src64 = Source.PhysicsHKX64;
            HavokPackfile dst32 = Destination.PhysicsHKX;
            HavokPackfile dst64 = Destination.PhysicsHKX64;

            HavokPackfile.PhysicsSystem imported32 = null, imported64 = null;
            if (src32 != null && dst32 != null)
                imported32 = dst32.ImportPhysicsSystem(src32, sourceSystem, _physicsRemap32);
            else if (src32 != null && dst32 == null)
                Console.WriteLine("CompositePorter: destination level has no PHYSICS.HKX - cannot import physics system.");

            if (src64 != null && dst64 != null)
            {
                HavokPackfile.PhysicsSystem source64 = src64.GetPhysicsSystem(sourceSystem.SystemIndex);
                if (source64 != null)
                {
                    try
                    {
                        imported64 = dst64.ImportPhysicsSystem(src64, source64, _physicsRemap64);
                    }
                    catch (Exception ex) when (dst32 != null)
                    {
                        Console.WriteLine("CompositePorter: PHYSICS.HKX64 import failed: " + ex.Message);
                    }
                }
                else
                {
                    Console.WriteLine("CompositePorter: no matching PHYSICS.HKX64 system for index " + sourceSystem.SystemIndex);
                }
            }
            else if (src64 != null && dst32 == null)
                Console.WriteLine("CompositePorter: destination level has no PHYSICS.HKX64 - cannot import physics system.");

            return dst32 != null ? imported32 : imported64;
        }
    }
}
#endif
