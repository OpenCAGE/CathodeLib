using System;
using System.Collections.Generic;
using System.Linq;
using CATHODE;
using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using CathodeLib;

namespace CathodeLib.NavMesh
{
    /// <summary>
    /// Resolves ExclusiveMaster active_objects / inactive_objects into collision instances
    /// to exclude from a per-state Recast soup.
    ///
    /// Pin targets (Aliases, TriggerSequence paths) are relative to the ExclusiveMaster's
    /// host composite. Collision rows store composite_instance_id for the containing
    /// composite placement (Instancing: path.GenerateCompositeInstanceID(false)), so we
    /// prepend the host placement path from root before hashing.
    /// </summary>
    public sealed class ExclusiveMasterNavFilter
    {
        public sealed class MasterSets
        {
            public Entity MasterEntity;
            public Composite HostComposite;
            public ShortGuid CompositeInstanceId = ShortGuid.Invalid;
            public EntityPath HostPath;
            public Resources.Resource Resource;
            public HashSet<uint> ActiveEntityIds = new HashSet<uint>();
            public HashSet<uint> InactiveEntityIds = new HashSet<uint>();
            public HashSet<HavokPackfile.CompoundInstance> ActiveInstances = new HashSet<HavokPackfile.CompoundInstance>();
            public HashSet<HavokPackfile.CompoundInstance> InactiveInstances = new HashSet<HavokPackfile.CompoundInstance>();
            public int ActivePinLinks;
            public int InactivePinLinks;
            public int UnresolvedPins;
            public int InstanceScopedHits;
            public int InstanceScopedMisses;
        }

        public sealed class StateSkipSet
        {
            public int StateIndex;
            public Entity ActiveMaster;
            public HashSet<HavokPackfile.CompoundInstance> Exclude = new HashSet<HavokPackfile.CompoundInstance>();
            public int ExcludeCount => Exclude.Count;
            public string Summary;
        }

        public List<MasterSets> Masters = new List<MasterSets>();
        public List<string> Notes = new List<string>();

        const int TriggerWalkDepth = 4;

        public static ExclusiveMasterNavFilter Build(Level level)
        {
            var filter = new ExclusiveMasterNavFilter();
            if (level?.Commands?.Entries == null)
                return filter;

            var collisionByEntityId = IndexCollisionByEntityId(level);
            var collisionByInstanceId = IndexCollisionByInstanceId(level);
            var utils = level.Commands.Utils;
            var hostPathCache = new Dictionary<uint, EntityPath>();

            foreach (Composite composite in level.Commands.Entries)
            {
                if (composite == null)
                    continue;
                foreach (FunctionEntity master in composite.GetFunctionEntitiesOfType(FunctionType.ExclusiveMaster))
                {
                    if (master == null)
                        continue;

                    var sets = new MasterSets
                    {
                        MasterEntity = master,
                        HostComposite = composite
                    };

                    // Match StateResources metadata when present.
                    if (level.StateResources != null)
                    {
                        for (int i = 1; i < level.StateResources.Count; i++)
                        {
                            Level.State state = level.StateResources[i];
                            if (state?.ExclusiveMaster == master ||
                                (state?.Resource != null && state.Resource.resource_id == master.shortGUID) ||
                                (state?.ExclusiveMaster != null && state.ExclusiveMaster.shortGUID == master.shortGUID))
                            {
                                sets.CompositeInstanceId = state.CompositeInstanceId;
                                sets.Resource = state.Resource;
                                break;
                            }
                        }
                    }

                    sets.HostPath = ResolveHostPath(level, composite, sets.CompositeInstanceId, hostPathCache, filter.Notes);
                    if (sets.HostPath == null)
                    {
                        filter.Notes.Add(string.Format(
                            "WARN: no host path for EM {0} in {1} (inst={2}) — pin→collision matching will be weak",
                            master.shortGUID.ToByteString(),
                            composite.name,
                            sets.CompositeInstanceId.ToByteString()));
                    }

                    CollectPinTargets(
                        level, utils, sets, ShortGuids.active_objects,
                        sets.ActiveEntityIds, sets.ActiveInstances,
                        collisionByEntityId, collisionByInstanceId,
                        ref sets.ActivePinLinks, filter.Notes);
                    CollectPinTargets(
                        level, utils, sets, ShortGuids.inactive_objects,
                        sets.InactiveEntityIds, sets.InactiveInstances,
                        collisionByEntityId, collisionByInstanceId,
                        ref sets.InactivePinLinks, filter.Notes);

                    filter.Masters.Add(sets);
                }
            }

            filter.Notes.Add(string.Format(
                "ExclusiveMasters={0} totalActiveInst={1} totalInactiveInst={2} scopedHits={3} scopedMisses={4}",
                filter.Masters.Count,
                filter.Masters.Sum(m => m.ActiveInstances.Count),
                filter.Masters.Sum(m => m.InactiveInstances.Count),
                filter.Masters.Sum(m => m.InstanceScopedHits),
                filter.Masters.Sum(m => m.InstanceScopedMisses)));
            return filter;
        }

        /// <summary>
        /// STATE_0: exclude every master's active_objects.
        /// STATE_n for master M: exclude M.inactive + other masters' active.
        /// </summary>
        public StateSkipSet GetSkipSetForState(int stateIndex, Level.State state)
        {
            var result = new StateSkipSet { StateIndex = stateIndex };
            if (Masters.Count == 0)
            {
                result.Summary = "no ExclusiveMasters";
                return result;
            }

            MasterSets activeMaster = null;
            if (stateIndex > 0 && state != null)
            {
                activeMaster = FindMasterForState(state);
                result.ActiveMaster = activeMaster?.MasterEntity;
            }

            if (stateIndex == 0 || activeMaster == null)
            {
                foreach (MasterSets m in Masters)
                    UnionInto(result.Exclude, m.ActiveInstances);
                result.Summary = string.Format(
                    "STATE_0 exclude all actives ({0} instances across {1} masters)",
                    result.Exclude.Count, Masters.Count);
                if (stateIndex > 0 && activeMaster == null)
                    result.Summary += " [WARN: could not match ExclusiveMaster — fell back to STATE_0 policy]";
                return result;
            }

            UnionInto(result.Exclude, activeMaster.InactiveInstances);
            foreach (MasterSets m in Masters)
            {
                if (ReferenceEquals(m, activeMaster))
                    continue;
                UnionInto(result.Exclude, m.ActiveInstances);
            }
            result.Summary = string.Format(
                "STATE_{0} master={1} exclude inactive={2} + otherActives -> {3} instances",
                stateIndex,
                activeMaster.MasterEntity?.shortGUID.ToByteString() ?? "?",
                activeMaster.InactiveInstances.Count,
                result.Exclude.Count);
            return result;
        }

        MasterSets FindMasterForState(Level.State state)
        {
            if (state == null)
                return null;
            for (int i = 0; i < Masters.Count; i++)
            {
                MasterSets m = Masters[i];
                if (state.ExclusiveMaster != null &&
                    (ReferenceEquals(state.ExclusiveMaster, m.MasterEntity) ||
                     state.ExclusiveMaster.shortGUID == m.MasterEntity.shortGUID))
                    return m;
                if (state.Resource != null && m.MasterEntity != null &&
                    state.Resource.resource_id == m.MasterEntity.shortGUID)
                    return m;
            }
            return null;
        }

        /// <summary>
        /// Find EntityPath from level root to a placement of <paramref name="hostComposite"/>
        /// whose GenerateCompositeInstanceID(false) equals <paramref name="compositeInstanceId"/>.
        /// Falls back to the first placement of that composite when the id is missing.
        /// </summary>
        static EntityPath ResolveHostPath(
            Level level,
            Composite hostComposite,
            ShortGuid compositeInstanceId,
            Dictionary<uint, EntityPath> cache,
            List<string> notes)
        {
            if (hostComposite == null || level?.Commands?.EntryPoints == null || level.Commands.EntryPoints.Length == 0)
                return null;

            uint cacheKey = compositeInstanceId != ShortGuid.Invalid
                ? compositeInstanceId.AsUInt32
                : unchecked(hostComposite.shortGUID.AsUInt32 ^ 0x48534F54u); // 'HSOT' — first-placement fallback key
            if (cache.TryGetValue(cacheKey, out EntityPath cached))
                return cached;

            Composite root = level.Commands.EntryPoints[0];
            if (ReferenceEquals(hostComposite, root))
            {
                var rootPath = new EntityPath(new[] { ShortGuid.Invalid });
                cache[cacheKey] = rootPath;
                return rootPath;
            }

            EntityPath matchById = null;
            EntityPath firstPlacement = null;

            var queue = new Queue<(Composite comp, EntityPath path)>();
            queue.Enqueue((root, new EntityPath(new[] { ShortGuid.Invalid })));
            var enqueued = new HashSet<ulong>();
            int visited = 0;
            const int visitCap = 250000;

            while (queue.Count > 0 && visited < visitCap)
            {
                (Composite comp, EntityPath path) = queue.Dequeue();
                visited++;
                if (comp?.functions == null)
                    continue;

                foreach (FunctionEntity fe in comp.functions)
                {
                    if (fe == null || fe.function.IsFunctionType)
                        continue;
                    Composite child = level.Commands.GetComposite(fe.function);
                    if (child == null)
                        continue;

                    EntityPath childPath = path.Copy();
                    childPath.AddNextStep(fe);
                    ShortGuid instanceId = childPath.GenerateCompositeInstanceID(false);

                    if (ReferenceEquals(child, hostComposite))
                    {
                        if (firstPlacement == null)
                            firstPlacement = childPath;
                        if (compositeInstanceId != ShortGuid.Invalid && instanceId == compositeInstanceId)
                        {
                            matchById = childPath;
                            goto done;
                        }
                    }

                    ulong key = ((ulong)child.shortGUID.AsUInt32 << 32) ^ instanceId.AsUInt32;
                    if (enqueued.Add(key))
                        queue.Enqueue((child, childPath));
                }
            }

        done:
            EntityPath result = matchById ?? firstPlacement;
            if (result != null)
                cache[cacheKey] = result;
            else if (notes.Count < 32)
                notes.Add(string.Format("host path BFS miss for {0} (visited={1})", hostComposite.name, visited));
            return result;
        }

        static void CollectPinTargets(
            Level level,
            CommandsUtils utils,
            MasterSets sets,
            ShortGuid pin,
            HashSet<uint> entityIds,
            HashSet<HavokPackfile.CompoundInstance> instances,
            Dictionary<uint, List<(ShortGuid instanceId, HavokPackfile.CompoundInstance inst)>> collisionByEntityId,
            Dictionary<uint, List<HavokPackfile.CompoundInstance>> collisionByInstanceId,
            ref int pinLinks,
            List<string> notes)
        {
            FunctionEntity master = sets.MasterEntity as FunctionEntity;
            if (master == null)
                return;

            List<EntityConnector> links = master.GetLinksOut(pin);
            pinLinks += links.Count;
            foreach (EntityConnector link in links)
            {
                Entity linked = sets.HostComposite.GetEntityByID(link.linkedEntityID);
                if (linked == null)
                {
                    sets.UnresolvedPins++;
                    if (notes.Count < 24)
                        notes.Add(string.Format("unresolved pin target {0} on EM {1}",
                            link.linkedEntityID.ToByteString(), master.shortGUID.ToByteString()));
                    continue;
                }

                CollectFromEntity(
                    level, utils, sets, sets.HostComposite, sets.HostPath, linked,
                    entityIds, instances, collisionByEntityId, collisionByInstanceId,
                    depth: 0, visited: new HashSet<uint>(), notes);
            }
        }

        static void CollectFromEntity(
            Level level,
            CommandsUtils utils,
            MasterSets sets,
            Composite hostComposite,
            EntityPath scopePath,
            Entity entity,
            HashSet<uint> entityIds,
            HashSet<HavokPackfile.CompoundInstance> instances,
            Dictionary<uint, List<(ShortGuid instanceId, HavokPackfile.CompoundInstance inst)>> collisionByEntityId,
            Dictionary<uint, List<HavokPackfile.CompoundInstance>> collisionByInstanceId,
            int depth,
            HashSet<uint> visited,
            List<string> notes)
        {
            if (entity == null || depth > TriggerWalkDepth + 2)
                return;
            if (!visited.Add(entity.shortGUID.AsUInt32))
                return;

            switch (entity.variant)
            {
                case EntityVariant.ALIAS:
                {
                    var alias = (AliasEntity)entity;
                    EntityPath fullPath = AppendPath(scopePath, alias.alias);
                    var resolved = utils.ResolveAlias(alias, hostComposite);
                    if (!utils.CouldResolve(resolved))
                    {
                        sets.UnresolvedPins++;
                        return;
                    }
                    (Composite targetComp, Entity target) = utils.GetResolvedTarget(resolved);
                    CollectResolvedTarget(
                        level, utils, sets, hostComposite, fullPath, targetComp, target,
                        entityIds, instances, collisionByEntityId, collisionByInstanceId,
                        depth + 1, visited, notes);
                    return;
                }
                case EntityVariant.PROXY:
                {
                    var resolved = utils.ResolveProxy((ProxyEntity)entity);
                    if (!utils.CouldResolve(resolved))
                    {
                        sets.UnresolvedPins++;
                        if (notes.Count < 24)
                            notes.Add(string.Format("unresolved proxy {0}", entity.shortGUID.ToByteString()));
                        return;
                    }
                    (Composite targetComp, Entity target) = utils.GetResolvedTarget(resolved);
                    CollectFromEntity(level, utils, sets, targetComp ?? hostComposite, scopePath, target,
                        entityIds, instances, collisionByEntityId, collisionByInstanceId,
                        depth + 1, visited, notes);
                    return;
                }
                case EntityVariant.FUNCTION:
                {
                    var fe = (FunctionEntity)entity;
                    entityIds.Add(fe.shortGUID.AsUInt32);

                    if (!fe.function.IsFunctionType)
                    {
                        // Composite instance placed in the current scope.
                        EntityPath childPath = AppendEntity(scopePath, fe.shortGUID);
                        ShortGuid instanceId = childPath != null
                            ? childPath.GenerateCompositeInstanceID(false)
                            : ShortGuid.Invalid;
                        AddCollisionForInstance(instanceId, collisionByInstanceId, instances, sets);
                        // Nested collision hosts under this placement use the child instance as scope.
                        Composite child = level.Commands.GetComposite(fe.function);
                        if (child != null && instanceId == ShortGuid.Invalid)
                        {
                            // No host path — best-effort template walk without instance scope (logged).
                            CollectCompositeCollisionHosts(
                                level, sets, child, ShortGuid.Invalid,
                                entityIds, instances, collisionByEntityId, collisionByInstanceId,
                                depth + 1, notes, allowUnscopedFallback: true);
                        }
                        return;
                    }

                    FunctionType type = fe.function.AsFunctionType;
                    if (IsCollisionHost(type))
                    {
                        ShortGuid containingInstance = scopePath != null
                            ? scopePath.GenerateCompositeInstanceID(false)
                            : ShortGuid.Invalid;
                        AddCollisionForEntity(fe.shortGUID, containingInstance, collisionByEntityId, instances, sets, allowUnscopedFallback: containingInstance == ShortGuid.Invalid);
                        return;
                    }

                    if (fe is TriggerSequence trig)
                    {
                        CollectTriggerSequenceTargets(
                            level, utils, sets, hostComposite, scopePath, trig,
                            entityIds, instances, collisionByEntityId, collisionByInstanceId,
                            depth + 1, visited, notes);
                        return;
                    }

                    if (type == FunctionType.TriggerSequence || IsLogicRelay(type))
                    {
                        if (depth >= TriggerWalkDepth)
                            return;
                        WalkLogicLinks(level, utils, sets, hostComposite, scopePath, fe,
                            entityIds, instances, collisionByEntityId, collisionByInstanceId,
                            depth + 1, visited, notes);
                    }
                    return;
                }
                default:
                    entityIds.Add(entity.shortGUID.AsUInt32);
                    AddCollisionForEntity(entity.shortGUID, ShortGuid.Invalid, collisionByEntityId, instances, sets, allowUnscopedFallback: true);
                    return;
            }
        }

        static void CollectResolvedTarget(
            Level level,
            CommandsUtils utils,
            MasterSets sets,
            Composite resolveHost,
            EntityPath fullPath,
            Composite targetComp,
            Entity target,
            HashSet<uint> entityIds,
            HashSet<HavokPackfile.CompoundInstance> instances,
            Dictionary<uint, List<(ShortGuid instanceId, HavokPackfile.CompoundInstance inst)>> collisionByEntityId,
            Dictionary<uint, List<HavokPackfile.CompoundInstance>> collisionByInstanceId,
            int depth,
            HashSet<uint> visited,
            List<string> notes)
        {
            if (target == null)
            {
                sets.UnresolvedPins++;
                return;
            }

            entityIds.Add(target.shortGUID.AsUInt32);

            if (target is FunctionEntity tfe && !tfe.function.IsFunctionType)
            {
                // Alias/sequence pointed at a composite instance entity.
                ShortGuid instanceId = fullPath != null
                    ? fullPath.GenerateCompositeInstanceID(false)
                    : ShortGuid.Invalid;
                int before = instances.Count;
                AddCollisionForInstance(instanceId, collisionByInstanceId, instances, sets);
                if (instances.Count == before && instanceId != ShortGuid.Invalid)
                {
                    // Rare: try Gen(true) in case the path included an internal entity id.
                    ShortGuid alt = fullPath.GenerateCompositeInstanceID(true);
                    AddCollisionForInstance(alt, collisionByInstanceId, instances, sets);
                }
                return;
            }

            if (target is FunctionEntity hostFe && hostFe.function.IsFunctionType && IsCollisionHost(hostFe.function.AsFunctionType))
            {
                // Path points at a ModelReference (etc.): containing instance is Gen(true).
                ShortGuid containing = fullPath != null
                    ? fullPath.GenerateCompositeInstanceID(true)
                    : ShortGuid.Invalid;
                AddCollisionForEntity(target.shortGUID, containing, collisionByEntityId, instances, sets, allowUnscopedFallback: containing == ShortGuid.Invalid);
                return;
            }

            // Fallback: continue walk from resolved entity within its composite.
            CollectFromEntity(level, utils, sets, targetComp ?? resolveHost, fullPath, target,
                entityIds, instances, collisionByEntityId, collisionByInstanceId,
                depth, visited, notes);
        }

        static void CollectTriggerSequenceTargets(
            Level level,
            CommandsUtils utils,
            MasterSets sets,
            Composite hostComposite,
            EntityPath hostPath,
            TriggerSequence trig,
            HashSet<uint> entityIds,
            HashSet<HavokPackfile.CompoundInstance> instances,
            Dictionary<uint, List<(ShortGuid instanceId, HavokPackfile.CompoundInstance inst)>> collisionByEntityId,
            Dictionary<uint, List<HavokPackfile.CompoundInstance>> collisionByInstanceId,
            int depth,
            HashSet<uint> visited,
            List<string> notes)
        {
            if (trig?.sequence == null || depth > TriggerWalkDepth + 2)
                return;

            foreach (TriggerSequence.SequenceEntry entry in trig.sequence)
            {
                if (entry?.connectedEntity == null)
                    continue;

                EntityPath fullPath = AppendPath(hostPath, entry.connectedEntity);
                var resolved = utils.ResolveAliasOrProxy(entry.connectedEntity, hostComposite);
                if (!utils.CouldResolve(resolved))
                    resolved = utils.ResolveAlias(entry.connectedEntity, hostComposite);
                if (!utils.CouldResolve(resolved))
                {
                    sets.UnresolvedPins++;
                    sets.InstanceScopedMisses++;
                    continue;
                }

                (Composite targetComp, Entity target) = utils.GetResolvedTarget(resolved);
                CollectResolvedTarget(
                    level, utils, sets, hostComposite, fullPath, targetComp, target,
                    entityIds, instances, collisionByEntityId, collisionByInstanceId,
                    depth, visited, notes);
            }

            WalkLogicLinks(level, utils, sets, hostComposite, hostPath, trig,
                entityIds, instances, collisionByEntityId, collisionByInstanceId,
                depth, visited, notes);
        }

        static void CollectCompositeCollisionHosts(
            Level level,
            MasterSets sets,
            Composite composite,
            ShortGuid instanceHint,
            HashSet<uint> entityIds,
            HashSet<HavokPackfile.CompoundInstance> instances,
            Dictionary<uint, List<(ShortGuid instanceId, HavokPackfile.CompoundInstance inst)>> collisionByEntityId,
            Dictionary<uint, List<HavokPackfile.CompoundInstance>> collisionByInstanceId,
            int depth,
            List<string> notes,
            bool allowUnscopedFallback)
        {
            if (composite?.functions == null || depth > TriggerWalkDepth + 4)
                return;

            if (instanceHint != ShortGuid.Invalid)
            {
                AddCollisionForInstance(instanceHint, collisionByInstanceId, instances, sets);
                return;
            }

            if (!allowUnscopedFallback)
                return;

            foreach (FunctionEntity fe in composite.functions)
            {
                if (fe == null)
                    continue;
                if (!fe.function.IsFunctionType)
                {
                    Composite nested = level.Commands.GetComposite(fe.function);
                    if (nested != null)
                        CollectCompositeCollisionHosts(
                            level, sets, nested, ShortGuid.Invalid,
                            entityIds, instances, collisionByEntityId, collisionByInstanceId,
                            depth + 1, notes, allowUnscopedFallback: true);
                    continue;
                }

                FunctionType type = fe.function.AsFunctionType;
                if (IsCollisionHost(type))
                {
                    entityIds.Add(fe.shortGUID.AsUInt32);
                    AddCollisionForEntity(fe.shortGUID, ShortGuid.Invalid, collisionByEntityId, instances, sets, allowUnscopedFallback: true);
                }
            }
        }

        static void WalkLogicLinks(
            Level level,
            CommandsUtils utils,
            MasterSets sets,
            Composite hostComposite,
            EntityPath scopePath,
            FunctionEntity logic,
            HashSet<uint> entityIds,
            HashSet<HavokPackfile.CompoundInstance> instances,
            Dictionary<uint, List<(ShortGuid instanceId, HavokPackfile.CompoundInstance inst)>> collisionByEntityId,
            Dictionary<uint, List<HavokPackfile.CompoundInstance>> collisionByInstanceId,
            int depth,
            HashSet<uint> visited,
            List<string> notes)
        {
            if (logic?.childLinks == null)
                return;

            foreach (EntityConnector link in logic.childLinks)
            {
                if (link.thisParamID != ShortGuids.trigger &&
                    link.thisParamID != ShortGuids.reference &&
                    link.thisParamID != ShortGuids.active_objects &&
                    link.thisParamID != ShortGuids.inactive_objects)
                {
                    if (depth > 1 && link.linkedParamID == ShortGuid.Invalid)
                        continue;
                }

                Entity target = hostComposite.GetEntityByID(link.linkedEntityID);
                if (target == null)
                    continue;
                CollectFromEntity(level, utils, sets, hostComposite, scopePath, target,
                    entityIds, instances, collisionByEntityId, collisionByInstanceId,
                    depth, visited, notes);
            }
        }

        static bool IsCollisionHost(FunctionType type)
        {
            switch (type)
            {
                case FunctionType.ModelReference:
                case FunctionType.EnvironmentModelReference:
                case FunctionType.PhysicsSystem:
                case FunctionType.CollisionBarrier:
                case FunctionType.NavMeshBarrier:
                    return true;
                default:
                    return false;
            }
        }

        static bool IsLogicRelay(FunctionType type)
        {
            switch (type)
            {
                case FunctionType.LogicGate:
                case FunctionType.LogicGateAnd:
                case FunctionType.LogicGateOr:
                case FunctionType.LogicDelay:
                case FunctionType.LogicOnce:
                case FunctionType.LogicSwitch:
                    return true;
                default:
                    return false;
            }
        }

        static EntityPath AppendPath(EntityPath hostPath, EntityPath relative)
        {
            if (relative == null)
                return hostPath?.Copy();
            if (hostPath == null)
                return relative.Copy();

            EntityPath full = hostPath.Copy();
            ShortGuid[] steps = relative.path;
            if (steps == null)
                return full;
            for (int i = 0; i < steps.Length; i++)
            {
                if (steps[i] == ShortGuid.Invalid)
                    break;
                full.AddNextStep(steps[i]);
            }
            return full;
        }

        static EntityPath AppendEntity(EntityPath hostPath, ShortGuid entityId)
        {
            if (entityId == ShortGuid.Invalid)
                return hostPath?.Copy();
            if (hostPath == null)
                return new EntityPath(new[] { entityId, ShortGuid.Invalid });
            EntityPath full = hostPath.Copy();
            full.AddNextStep(entityId);
            return full;
        }

        static Dictionary<uint, List<(ShortGuid instanceId, HavokPackfile.CompoundInstance inst)>> IndexCollisionByEntityId(Level level)
        {
            var map = new Dictionary<uint, List<(ShortGuid, HavokPackfile.CompoundInstance)>>();
            if (level.CollisionMaps?.Entries == null)
                return map;

            foreach (CollisionMaps.COLLISION_MAPPING entry in level.CollisionMaps.Entries)
            {
                if (entry?.CollisionInstance == null || entry.Entity == null)
                    continue;
                uint id = entry.Entity.entity_id.AsUInt32;
                if (id == 0)
                    continue;
                if (!map.TryGetValue(id, out List<(ShortGuid, HavokPackfile.CompoundInstance)> list))
                {
                    list = new List<(ShortGuid, HavokPackfile.CompoundInstance)>();
                    map[id] = list;
                }
                list.Add((entry.Entity.composite_instance_id, entry.CollisionInstance));
            }
            return map;
        }

        static Dictionary<uint, List<HavokPackfile.CompoundInstance>> IndexCollisionByInstanceId(Level level)
        {
            var map = new Dictionary<uint, List<HavokPackfile.CompoundInstance>>();
            if (level.CollisionMaps?.Entries == null)
                return map;

            foreach (CollisionMaps.COLLISION_MAPPING entry in level.CollisionMaps.Entries)
            {
                if (entry?.CollisionInstance == null || entry.Entity == null)
                    continue;
                uint id = entry.Entity.composite_instance_id.AsUInt32;
                if (id == 0)
                    continue;
                if (!map.TryGetValue(id, out List<HavokPackfile.CompoundInstance> list))
                {
                    list = new List<HavokPackfile.CompoundInstance>();
                    map[id] = list;
                }
                list.Add(entry.CollisionInstance);
            }
            return map;
        }

        static void AddCollisionForInstance(
            ShortGuid compositeInstanceId,
            Dictionary<uint, List<HavokPackfile.CompoundInstance>> collisionByInstanceId,
            HashSet<HavokPackfile.CompoundInstance> instances,
            MasterSets sets)
        {
            if (compositeInstanceId == ShortGuid.Invalid)
            {
                sets.InstanceScopedMisses++;
                return;
            }

            if (!collisionByInstanceId.TryGetValue(compositeInstanceId.AsUInt32, out List<HavokPackfile.CompoundInstance> list) ||
                list == null || list.Count == 0)
            {
                sets.InstanceScopedMisses++;
                return;
            }

            sets.InstanceScopedHits++;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                    instances.Add(list[i]);
            }
        }

        static void AddCollisionForEntity(
            ShortGuid entityId,
            ShortGuid compositeInstanceHint,
            Dictionary<uint, List<(ShortGuid instanceId, HavokPackfile.CompoundInstance inst)>> collisionByEntityId,
            HashSet<HavokPackfile.CompoundInstance> instances,
            MasterSets sets,
            bool allowUnscopedFallback)
        {
            if (entityId == ShortGuid.Invalid)
                return;
            if (!collisionByEntityId.TryGetValue(entityId.AsUInt32, out List<(ShortGuid instanceId, HavokPackfile.CompoundInstance inst)> list))
            {
                if (compositeInstanceHint != ShortGuid.Invalid)
                    sets.InstanceScopedMisses++;
                return;
            }

            bool haveHint = compositeInstanceHint != ShortGuid.Invalid;
            bool matchedHint = false;
            if (haveHint)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].instanceId == compositeInstanceHint && list[i].inst != null)
                    {
                        instances.Add(list[i].inst);
                        matchedHint = true;
                    }
                }
            }
            if (matchedHint)
            {
                sets.InstanceScopedHits++;
                return;
            }

            if (haveHint && !allowUnscopedFallback)
            {
                sets.InstanceScopedMisses++;
                return;
            }

            // Unscoped fallback: every placement of this entity id (legacy / no host path).
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].inst != null)
                    instances.Add(list[i].inst);
            }
        }

        static void UnionInto(HashSet<HavokPackfile.CompoundInstance> dst, HashSet<HavokPackfile.CompoundInstance> src)
        {
            if (src == null)
                return;
            foreach (HavokPackfile.CompoundInstance inst in src)
                if (inst != null)
                    dst.Add(inst);
        }
    }
}
