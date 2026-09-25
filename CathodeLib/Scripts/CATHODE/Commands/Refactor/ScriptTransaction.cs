#if !GODOT
using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CATHODE.Scripting.Refactor
{
    /// <summary>
    /// The script as it was before a refactor and as it is after, for everything the refactor touched,
    /// so the whole change can be taken back and put back on the same objects.
    /// </summary>
    /// <remarks>
    /// A refactor edits many composites at once: entities come and go, links are rewired, stored paths
    /// are rewritten. Recording each of those as its own undo step would need an edit type per kind of
    /// change, and those would have to replay in exactly the right order. Instead, anything about to be
    /// changed is snapshotted the first time it is touched, and again once the refactor is done. Undo
    /// writes the first snapshot back, redo the second.
    ///
    /// <para>The objects themselves are never replaced. An entity keeps its identity across an undo, so
    /// anything holding it (a selection, an open editor, the viewer's registry) still holds the right
    /// thing. Snapshots never share a list with the live object: <c>~Entity</c> clears
    /// <c>childLinks</c> and <c>parameters</c> in place, so a shared list would be emptied behind our back.</para>
    ///
    /// <para>A composite's entities are snapshotted in dictionary order, and a restore rebuilds the
    /// dictionaries in that order. The order is not cosmetic: it is the order instancing walks, which
    /// decides zone arbitration, environment map ties and which placement of a shared composite claims it.</para>
    /// </remarks>
    public sealed class ScriptTransaction
    {
        private readonly Commands _commands;

        private readonly Dictionary<Entity, EntityState> _entitiesBefore = new Dictionary<Entity, EntityState>();
        private readonly Dictionary<Entity, EntityState> _entitiesAfter = new Dictionary<Entity, EntityState>();
        private readonly List<Entity> _entityOrder = new List<Entity>();

        private readonly Dictionary<Composite, CompositeContents> _compositesBefore = new Dictionary<Composite, CompositeContents>();
        private readonly Dictionary<Composite, CompositeContents> _compositesAfter = new Dictionary<Composite, CompositeContents>();
        private readonly List<Composite> _compositeOrder = new List<Composite>();

        private readonly Dictionary<ShortGuid, List<CompositePinInfoTable.PinInfo>> _pinsBefore = new Dictionary<ShortGuid, List<CompositePinInfoTable.PinInfo>>();
        private readonly Dictionary<ShortGuid, List<CompositePinInfoTable.PinInfo>> _pinsAfter = new Dictionary<ShortGuid, List<CompositePinInfoTable.PinInfo>>();

        private List<Composite> _entriesBefore = null;
        private List<Composite> _entriesAfter = null;

        public bool Committed { get; private set; }

        public ScriptTransaction(Commands commands)
        {
            _commands = commands;
        }

        /// <summary>Snapshot an entity before it is first changed.</summary>
        public void Touch(Entity entity)
        {
            if (entity == null || Committed || _entitiesBefore.ContainsKey(entity))
                return;
            _entitiesBefore.Add(entity, EntityState.Capture(entity));
            _entityOrder.Add(entity);
        }

        /// <summary>Snapshot which entities a composite holds, and in what order, before any are added or removed.</summary>
        public void Touch(Composite composite)
        {
            if (composite == null || Committed || _compositesBefore.ContainsKey(composite))
                return;
            _compositesBefore.Add(composite, CompositeContents.Capture(composite));
            _compositeOrder.Add(composite);
        }

        /// <summary>Snapshot a composite's pin types before a pin is added, removed or retyped.</summary>
        public void TouchPins(Composite composite)
        {
            if (composite == null || Committed || _pinsBefore.ContainsKey(composite.shortGUID))
                return;
            _pinsBefore.Add(composite.shortGUID, CopyPins(_commands.Utils.GetAllCustomPinInfo(composite.shortGUID)));
        }

        /// <summary>Snapshot the level's composite list before a composite is added or removed.</summary>
        public void TouchEntries()
        {
            if (Committed || _entriesBefore != null)
                return;
            _entriesBefore = new List<Composite>(_commands.Entries);
        }

        /// <summary>Take the after-state of everything touched. Call once the refactor is complete.</summary>
        public void Commit()
        {
            if (Committed)
                return;
            foreach (Entity entity in _entityOrder)
                _entitiesAfter[entity] = EntityState.Capture(entity);
            foreach (Composite composite in _compositeOrder)
                _compositesAfter[composite] = CompositeContents.Capture(composite);
            foreach (ShortGuid composite in _pinsBefore.Keys)
                _pinsAfter[composite] = CopyPins(_commands.Utils.GetAllCustomPinInfo(composite));
            if (_entriesBefore != null)
                _entriesAfter = new List<Composite>(_commands.Entries);
            Committed = true;
        }

        /// <summary>Put everything touched back the way it was before the refactor.</summary>
        public void Revert()
        {
            Restore(_entitiesBefore, _compositesBefore, _pinsBefore, _entriesBefore);
        }

        /// <summary>Put everything touched back the way the refactor left it.</summary>
        public void Reapply()
        {
            if (!Committed)
                throw new InvalidOperationException("A transaction has to be committed before it can be reapplied");
            Restore(_entitiesAfter, _compositesAfter, _pinsAfter, _entriesAfter);
        }

        private void Restore(Dictionary<Entity, EntityState> entities, Dictionary<Composite, CompositeContents> composites, Dictionary<ShortGuid, List<CompositePinInfoTable.PinInfo>> pins, List<Composite> entries)
        {
            if (entries != null)
            {
                _commands.Entries.Clear();
                _commands.Entries.AddRange(entries);
            }
            foreach (Composite composite in _compositeOrder)
                composites[composite].Apply(composite);
            foreach (Entity entity in _entityOrder)
                entities[entity].Apply(entity);
            foreach (KeyValuePair<ShortGuid, List<CompositePinInfoTable.PinInfo>> row in pins)
                _commands.Utils.ReplaceCustomPinInfos(row.Key, CopyPins(row.Value));
        }

        private static List<CompositePinInfoTable.PinInfo> CopyPins(List<CompositePinInfoTable.PinInfo> pins)
        {
            List<CompositePinInfoTable.PinInfo> copy = new List<CompositePinInfoTable.PinInfo>();
            if (pins == null)
                return copy;
            foreach (CompositePinInfoTable.PinInfo pin in pins)
            {
                if (pin == null) continue;
                copy.Add(new CompositePinInfoTable.PinInfo() { VariableGUID = pin.VariableGUID, PinTypeGUID = pin.PinTypeGUID, PinEnumTypeGUID = pin.PinEnumTypeGUID });
            }
            return copy;
        }

        #region What changed
        /// <summary>The composites whose entity set was snapshotted.</summary>
        public IReadOnlyList<Composite> TouchedComposites => _compositeOrder;

        /// <summary>The entities that were snapshotted.</summary>
        public IReadOnlyList<Entity> TouchedEntities => _entityOrder;

        /// <summary>Composites the refactor added to the level (or, <paramref name="reverted"/>, the ones undoing it removes).</summary>
        public List<Composite> AddedComposites(bool reverted = false)
        {
            if (_entriesBefore == null || _entriesAfter == null)
                return new List<Composite>();
            HashSet<Composite> from = new HashSet<Composite>(reverted ? _entriesAfter : _entriesBefore);
            return (reverted ? _entriesBefore : _entriesAfter).Where(o => !from.Contains(o)).ToList();
        }

        /// <summary>Composites the refactor took out of the level (or, <paramref name="reverted"/>, the ones undoing it brings back).</summary>
        public List<Composite> RemovedComposites(bool reverted = false)
        {
            return AddedComposites(!reverted);
        }

        /// <summary>Entities in <paramref name="composite"/> after the refactor that were not there before it, in composite order (or the reverse, <paramref name="reverted"/>).</summary>
        public List<Entity> AddedEntities(Composite composite, bool reverted = false)
        {
            if (!_compositesBefore.TryGetValue(composite, out CompositeContents before) || !_compositesAfter.TryGetValue(composite, out CompositeContents after))
                return new List<Entity>();
            CompositeContents from = reverted ? after : before;
            CompositeContents to = reverted ? before : after;
            HashSet<Entity> had = new HashSet<Entity>(from.All());
            return to.All().Where(o => !had.Contains(o)).ToList();
        }

        /// <summary>Entities in <paramref name="composite"/> before the refactor that are gone after it (or the reverse, <paramref name="reverted"/>).</summary>
        public List<Entity> RemovedEntities(Composite composite, bool reverted = false)
        {
            return AddedEntities(composite, !reverted);
        }

        /// <summary>
        /// Entities that are in the same composite before and after, whose parameters or stored path
        /// changed - what a copy of the script that ignores links (the viewer's) has to be told again.
        /// </summary>
        public List<Entity> EntitiesWithChangedState()
        {
            List<Entity> changed = new List<Entity>();
            foreach (Entity entity in _entityOrder)
            {
                if (!_entitiesAfter.TryGetValue(entity, out EntityState after))
                    continue;
                if (!_entitiesBefore[entity].SameParametersAndPaths(after))
                    changed.Add(entity);
            }
            return changed;
        }
        #endregion

        /// <summary>One entity's links, parameters and stored paths.</summary>
        private sealed class EntityState
        {
            private List<EntityConnector> _links;
            private List<(Parameter parameter, ShortGuid name, ParameterData content, ParameterVariant variant)> _parameters;
            private ShortGuid[] _path;
            private ShortGuid _proxyFunction;
            private List<TriggerSequence.SequenceEntry> _sequence;
            private List<TriggerSequence.MethodEntry> _methods;
            private List<CAGEAnimation.Connection> _connections;
            private List<CAGEAnimation.EventTrack> _eventTracks;

            public static EntityState Capture(Entity entity)
            {
                EntityState state = new EntityState();
                state._links = new List<EntityConnector>(entity.childLinks);
                //The Parameter objects are kept, not copied: the inspector holds them, and a restore should leave it holding live ones
                state._parameters = entity.parameters.Select(o => (o, o.name, o.content, o.variant)).ToList();
                switch (entity)
                {
                    case AliasEntity alias:
                        state._path = CopyPath(alias.alias?.path);
                        break;
                    case ProxyEntity proxy:
                        state._path = CopyPath(proxy.proxy?.path);
                        state._proxyFunction = proxy.function;
                        state._sequence = CopySequence(proxy.sequence);
                        state._methods = CopyMethods(proxy.methods);
                        break;
                    case TriggerSequence trigger:
                        state._sequence = CopySequence(trigger.sequence);
                        state._methods = CopyMethods(trigger.methods);
                        break;
                    case CAGEAnimation animation:
                        state._connections = CopyConnections(animation.connections);
                        state._eventTracks = CopyEventTracks(animation.eventTracks);
                        break;
                }
                return state;
            }

            public void Apply(Entity entity)
            {
                entity.childLinks = new List<EntityConnector>(_links);
                entity.parameters = new List<Parameter>(_parameters.Count);
                foreach (var saved in _parameters)
                {
                    saved.parameter.name = saved.name;
                    saved.parameter.content = saved.content;
                    saved.parameter.variant = saved.variant;
                    entity.parameters.Add(saved.parameter);
                }
                switch (entity)
                {
                    case AliasEntity alias:
                        alias.alias = new EntityPath() { path = CopyPath(_path) };
                        break;
                    case ProxyEntity proxy:
                        proxy.proxy = new EntityPath() { path = CopyPath(_path) };
                        proxy.function = _proxyFunction;
                        proxy.sequence = CopySequence(_sequence);
                        proxy.methods = CopyMethods(_methods);
                        break;
                    case TriggerSequence trigger:
                        trigger.sequence = CopySequence(_sequence);
                        trigger.methods = CopyMethods(_methods);
                        break;
                    case CAGEAnimation animation:
                        animation.connections = CopyConnections(_connections);
                        animation.eventTracks = CopyEventTracks(_eventTracks);
                        break;
                }
            }

            public bool SameParametersAndPaths(EntityState other)
            {
                if (_parameters.Count != other._parameters.Count)
                    return false;
                for (int i = 0; i < _parameters.Count; i++)
                {
                    if (!ReferenceEquals(_parameters[i].parameter, other._parameters[i].parameter)) return false;
                    if (_parameters[i].name != other._parameters[i].name) return false;
                    if (!ReferenceEquals(_parameters[i].content, other._parameters[i].content)) return false;
                }
                if (!SamePath(_path, other._path))
                    return false;
                return true;
            }

            private static bool SamePath(ShortGuid[] a, ShortGuid[] b)
            {
                if (a == null || b == null) return a == b;
                if (a.Length != b.Length) return false;
                for (int i = 0; i < a.Length; i++)
                    if (a[i] != b[i]) return false;
                return true;
            }

            private static ShortGuid[] CopyPath(ShortGuid[] path)
            {
                if (path == null) return new ShortGuid[0];
                ShortGuid[] copy = new ShortGuid[path.Length];
                Array.Copy(path, copy, path.Length);
                return copy;
            }

            private static List<TriggerSequence.SequenceEntry> CopySequence(List<TriggerSequence.SequenceEntry> sequence)
            {
                if (sequence == null) return new List<TriggerSequence.SequenceEntry>();
                return sequence.Select(o => new TriggerSequence.SequenceEntry()
                {
                    timing = o.timing,
                    connectedEntity = new EntityPath() { path = CopyPath(o.connectedEntity?.path) },
                }).ToList();
            }

            private static List<TriggerSequence.MethodEntry> CopyMethods(List<TriggerSequence.MethodEntry> methods)
            {
                if (methods == null) return new List<TriggerSequence.MethodEntry>();
                return methods.Select(o => new TriggerSequence.MethodEntry(o.method, o.relay, o.finished)).ToList();
            }

            private static List<CAGEAnimation.Connection> CopyConnections(List<CAGEAnimation.Connection> connections)
            {
                if (connections == null) return new List<CAGEAnimation.Connection>();
                return connections.Select(o => new CAGEAnimation.Connection()
                {
                    binding_guid = o.binding_guid,
                    target_track = o.target_track,
                    binding_type = o.binding_type,
                    target_param = o.target_param,
                    target_param_type = o.target_param_type,
                    target_sub_param = o.target_sub_param,
                    connectedEntity = new EntityPath() { path = CopyPath(o.connectedEntity?.path) },
                }).ToList();
            }

            private static List<CAGEAnimation.EventTrack> CopyEventTracks(List<CAGEAnimation.EventTrack> tracks)
            {
                if (tracks == null) return new List<CAGEAnimation.EventTrack>();
                return tracks.Select(o => new CAGEAnimation.EventTrack()
                {
                    shortGUID = o.shortGUID,
                    track_type = o.track_type,
                    keyframes = o.keyframes?.Select(k => new CAGEAnimation.EventTrack.Keyframe()
                    {
                        time = k.time,
                        forward = k.forward,
                        reverse = k.reverse,
                        track_type = k.track_type,
                        duration = k.duration,
                    }).ToList() ?? new List<CAGEAnimation.EventTrack.Keyframe>(),
                }).ToList();
            }
        }

        /// <summary>Which entities a composite holds, in dictionary order.</summary>
        private sealed class CompositeContents
        {
            private List<VariableEntity> _variables;
            private List<FunctionEntity> _functions;
            private List<AliasEntity> _aliases;
            private List<ProxyEntity> _proxies;

            public static CompositeContents Capture(Composite composite)
            {
                return new CompositeContents()
                {
                    _variables = composite.variables_dictionary.Values.ToList(),
                    _functions = composite.functions_dictionary.Values.ToList(),
                    _aliases = composite.aliases_dictionary.Values.ToList(),
                    _proxies = composite.proxies_dictionary.Values.ToList(),
                };
            }

            public IEnumerable<Entity> All()
            {
                foreach (Entity entity in _variables) yield return entity;
                foreach (Entity entity in _functions) yield return entity;
                foreach (Entity entity in _aliases) yield return entity;
                foreach (Entity entity in _proxies) yield return entity;
            }

            public void Apply(Composite composite)
            {
                //Clear then add: a cleared Dictionary hands out its slots from the start again, so enumeration comes back in this order
                composite.variables_dictionary.Clear();
                foreach (VariableEntity entity in _variables) composite.variables_dictionary.Add(entity.shortGUID, entity);
                composite.functions_dictionary.Clear();
                foreach (FunctionEntity entity in _functions) composite.functions_dictionary.Add(entity.shortGUID, entity);
                composite.aliases_dictionary.Clear();
                foreach (AliasEntity entity in _aliases) composite.aliases_dictionary.Add(entity.shortGUID, entity);
                composite.proxies_dictionary.Clear();
                foreach (ProxyEntity entity in _proxies) composite.proxies_dictionary.Add(entity.shortGUID, entity);
            }
        }
    }
}
#endif
