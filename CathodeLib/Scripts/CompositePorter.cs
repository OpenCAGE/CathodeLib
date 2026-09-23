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
    /// proxies; dynamic physics systems; animated models) and the editor metadata that travels in the
    /// COMMANDS custom tables (custom pin info, modification info, the purge list).
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
        /// Follow composite instances and port the composites they refer to as well.
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
            PortRecursive(composite);
            ApplyProxyIndexRemap();
        }

        /// <summary>
        /// Port every composite in the source level.
        /// </summary>
        public void PortAll()
        {
            foreach (Composite composite in Source.Commands.Entries.ToList())
            {
                if (composite != null)
                    PortRecursive(composite);
            }
            ApplyProxyIndexRemap();
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
                }

                //Bring over generic metadata
                //NOTE: entity names travel with the entities themselves (as a 'name' parameter)
                Destination.Commands.Utils.AddCustomPinInfos(copiedComp, Source.Commands.Utils.GetAllCustomPinInfo(composite));
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
        }

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
                        break;
                    case ResourceType.DYNAMIC_PHYSICS_SYSTEM:
                        PortDynamicPhysicsSystem(resourceRefs[i]);
                        PhysicsSystemsPorted++;
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

            HavokPackfile.StaticCompoundShape imported32 = null;
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
                        dst64.ImportStaticCompoundShape(src64, source64, _collisionRemap64);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("CompositePorter: COLLISION.HKX64 import failed: " + ex.Message);
                    }
                }
                else
                {
                    Console.WriteLine("CompositePorter: no matching COLLISION.HKX64 compound for proxy " + sourceProxy.ProxyIndex);
                }
            }

            return imported32;
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

            HavokPackfile.PhysicsSystem imported32 = null;
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
                        dst64.ImportPhysicsSystem(src64, source64, _physicsRemap64);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("CompositePorter: PHYSICS.HKX64 import failed: " + ex.Message);
                    }
                }
                else
                {
                    Console.WriteLine("CompositePorter: no matching PHYSICS.HKX64 system for index " + sourceSystem.SystemIndex);
                }
            }

            return imported32;
        }
    }
}
#endif
