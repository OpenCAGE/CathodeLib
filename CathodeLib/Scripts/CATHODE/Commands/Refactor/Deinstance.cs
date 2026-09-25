#if !GODOT
using CATHODE.Enums;
using CATHODE.Scripting.Internal;
using CathodeLib;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using static CathodeLib.CompositeFlowgraphTable;

namespace CATHODE.Scripting.Refactor
{
    /// <summary>
    /// Pulling a composite instance's contents out into the composite that holds it.
    /// </summary>
    /// <remarks>
    /// Instance I of composite C sits in P. De-instancing I copies C's entities into P, placed where I put
    /// them, and removes I. C itself is left alone (other instances still use it).
    ///
    /// <para>What makes this more than a copy is everything that talked to C through I:</para>
    /// <list type="bullet">
    /// <item>C's pins (its VariableEntities) are relays. A record that ends at a pin, from inside C or
    /// from P through I, is joined with every record that starts from it, into a direct record from the
    /// first owner to the final target. A data pin nothing feeds becomes the value I (or the pin) held.</item>
    /// <item>Aliases in P on <c>[I, x]</c> become overrides written straight onto x's copy; deeper ones
    /// lose the I step. Aliases further up that pass through I lose that step too.</item>
    /// <item>Aliases that override I itself (its position, a pin value, a link into a pin) are spread
    /// onto the copies, as aliases on each copy at that level.</item>
    /// <item>Links to I's own interface (show, hide, deleted and so on) are spread onto the copies that
    /// have that method or flag, which is how the engine applies them to an instance's contents anyway.</item>
    /// <item>Stored paths (proxies, trigger sequences, animations) that step through I lose that step.</item>
    /// </list>
    /// <para>Anything that cannot be carried over is reported by <see cref="Plan"/> before anything is
    /// changed: a blocking issue refuses the refactor, a note describes where the result will differ.</para>
    /// </remarks>
    public sealed class DeinstancePlan
    {
        public Composite Parent { get; }
        public FunctionEntity Instance { get; }
        public Composite Content { get; }
        public List<RefactorIssue> Issues { get; } = new List<RefactorIssue>();
        public bool CanApply => Content != null && !Issues.Any(o => o.Blocking);

        private readonly RefactorContext _ctx;

        //What the analysis found, which Apply acts on
        private readonly Dictionary<ShortGuid, VariableEntity> _pinsByName = new Dictionary<ShortGuid, VariableEntity>();
        private readonly Dictionary<VariableEntity, bool> _pinIsEvent = new Dictionary<VariableEntity, bool>();
        private readonly List<Entity> _contents = new List<Entity>();
        private readonly List<(AliasEntity alias, Entity target)> _localMerges = new List<(AliasEntity, Entity)>();
        private readonly List<ResolvedPath> _localRewrites = new List<ResolvedPath>();
        private readonly List<(ResolvedPath path, int step)> _throughRewrites = new List<(ResolvedPath, int)>();
        private readonly List<(ResolvedPath path, int step)> _sequenceFanouts = new List<(ResolvedPath, int)>();
        private readonly Dictionary<string, OverrideLevel> _levels = new Dictionary<string, OverrideLevel>();
        private readonly Dictionary<string, AliasEntity> _aliasesByPath = new Dictionary<string, AliasEntity>();

        private readonly HashSet<string> _notes = new HashSet<string>();

        //Owner (function type or instance) and parameter pairs whose reference links list entities as a set
        private static readonly HashSet<(FunctionType, string)> _collectionReferences = new HashSet<(FunctionType, string)>()
        {
            (FunctionType.Zone, "composites"),
            (FunctionType.EnvironmentMap, "Entities"),
            (FunctionType.RadiosityIsland, "composites"),
            (FunctionType.Master, "objects"),
            (FunctionType.PhysicsApplyImpulse, "objects"),
            (FunctionType.PhysicsApplyVelocity, "objects"),
            (FunctionType.PhysicsModifyGravity, "objects"),
            (FunctionType.LightReference, "exclude_shadow_entities"),
        };

        /// <summary>One level (the parent P, or a composite above it) holding aliases that override I or one of its pins.</summary>
        private sealed class OverrideLevel
        {
            public Composite Holder;
            public ShortGuid[] Prefix;          //Stored path elements before I (empty at P)
            public List<AliasEntity> OnInstance = new List<AliasEntity>();
            public List<(AliasEntity alias, VariableEntity pin)> OnPins = new List<(AliasEntity, VariableEntity)>();
            public bool IsParent;
        }

        private DeinstancePlan(RefactorContext ctx, Composite parent, FunctionEntity instance)
        {
            _ctx = ctx;
            Parent = parent;
            Instance = instance;
            Content = ctx.InstancedComposite(instance);
        }

        /// <summary>Work out what de-instancing <paramref name="instance"/> in <paramref name="parent"/> would involve. Nothing is changed.</summary>
        public static DeinstancePlan Plan(Commands commands, Composite parent, FunctionEntity instance)
        {
            DeinstancePlan plan = new DeinstancePlan(new RefactorContext(commands), parent, instance);
            plan.Analyse();
            return plan;
        }

        private void Block(string message) => Issues.Add(new RefactorIssue() { Blocking = true, Message = message });
        private void Note(string message)
        {
            if (_notes.Add(message))
                Issues.Add(new RefactorIssue() { Blocking = false, Message = message });
        }

        private string InstanceName => _ctx.NameOf(Parent, Instance);
        private string Describe(Composite composite, Entity entity) => "'" + _ctx.NameOf(composite, entity) + "' in " + RefactorContext.LeafName(composite);

        #region Analysis
        private void Analyse()
        {
            if (Parent == null || Instance == null || Parent.GetEntityByID(Instance.shortGUID) != Instance)
            {
                Block("The entity is not in the composite being edited.");
                return;
            }
            if (Content == null)
            {
                Block("'" + InstanceName + "' is not an instance of a composite in this level.");
                return;
            }

            foreach (VariableEntity pin in Content.variables_dictionary.Values)
            {
                if (!_pinsByName.ContainsKey(pin.name))
                    _pinsByName.Add(pin.name, pin);
                CompositePinInfoTable.PinInfo info = _ctx.Utils.GetPinInfo(Content, pin);
                bool isEvent = false;
                if (info != null)
                {
                    ParameterVariant variant = _ctx.Utils.PinTypeToParameterVariant(info.PinTypeGUID);
                    isEvent = variant == ParameterVariant.METHOD_PIN || variant == ParameterVariant.TARGET_PIN;
                }
                _pinIsEvent[pin] = isEvent;
            }
            _contents.AddRange(Content.functions_dictionary.Values);
            _contents.AddRange(Content.aliases_dictionary.Values);
            _contents.AddRange(Content.proxies_dictionary.Values);

            foreach (FunctionEntity function in Content.functions_dictionary.Values)
            {
                if (function.function == FunctionType.PhysicsSystem)
                    Block(RefactorContext.LeafName(Content) + " has a PhysicsSystem, which belongs to the instance that places it and cannot be pulled out of it.");
                if (function.function == FunctionType.EnvironmentModelReference)
                    Block(RefactorContext.LeafName(Content) + " has an EnvironmentModelReference, whose animation data is tied to the composite it is in.");
            }

            if (RefactorContext.Truthy(Instance, ShortGuids.is_template))
                Block("'" + InstanceName + "' is a template (is_template): it is copied at runtime, not placed.");
            if (RefactorContext.Truthy(Instance, ShortGuids.is_shared))
                Block("'" + InstanceName + "' is shared (is_shared): one copy of it serves every placement.");
            if (Instance.childLinks.Any(o => o.thisParamID == ShortGuids.position))
                Block("The position of '" + InstanceName + "' is linked, so where its contents end up is only known at runtime.");

            AnalyseContentPaths();
            AnalysePaths();
            AnalyseInstanceLinks(Parent, Instance, true);
            foreach (OverrideLevel level in _levels.Values)
                foreach (AliasEntity alias in level.OnInstance)
                    AnalyseInstanceLinks(level.Holder, alias, false);
            AnalyseLinksInto(Parent, Instance);
            foreach (OverrideLevel level in _levels.Values)
                foreach (AliasEntity alias in level.OnInstance)
                    AnalyseLinksInto(level.Holder, alias);
            AnalyseFlags(Instance);
            foreach (OverrideLevel level in _levels.Values)
                foreach (AliasEntity alias in level.OnInstance)
                {
                    if (RefactorContext.Truthy(alias, ShortGuids.is_template) || RefactorContext.Truthy(alias, ShortGuids.is_shared))
                        Block(Describe(level.Holder, alias) + " makes '" + InstanceName + "' a template or shared.");
                    AnalyseFlags(alias);
                    if (alias.childLinks.Any(o => o.thisParamID == ShortGuids.position))
                        Block(Describe(level.Holder, alias) + " links the position of '" + InstanceName + "'.");
                }

            AnalyseParentPlacements();

            cTransform placement = RefactorContext.TransformOf(Instance);
            if (!RefactorContext.IsIdentity(placement))
            {
                foreach (Entity entity in _contents)
                    if (entity.childLinks.Any(o => o.thisParamID == ShortGuids.position))
                        Note("'" + _ctx.NameOf(Content, entity) + "' has a linked position, which is taken relative to where it is placed: it will be relative to " + RefactorContext.LeafName(Parent) + " instead of '" + InstanceName + "'.");
            }
        }

        /// <summary>
        /// Planar reflections, material mapping and disable_collision are read from an entity's direct parent
        /// instance. The entities directly inside C read them from I now, and from P's placements afterwards:
        /// if anything places P with one of those set, C's contents pick it up.
        /// </summary>
        private void AnalyseParentPlacements()
        {
            if (!_contents.Any(o => o is FunctionEntity && _ctx.InstancedComposite(o) == null))
                return;
            List<(Composite, Entity)> placements = new List<(Composite, Entity)>();
            HashSet<ShortGuid> ids = new HashSet<ShortGuid>();
            foreach (Composite composite in _ctx.Commands.Entries)
            {
                if (composite == null) continue;
                foreach (FunctionEntity function in composite.functions_dictionary.Values)
                {
                    if (function.function != Parent.shortGUID) continue;
                    placements.Add((composite, function));
                    ids.Add(function.shortGUID);
                }
            }
            if (placements.Count == 0)
                return;
            List<Entity> setters = placements.Select(o => o.Item2).ToList();
            foreach (ResolvedPath path in _ctx.FindPaths(ids))
            {
                if (path.Kind != PathKind.Alias) continue;
                PathStep last = path.Steps[path.Steps.Count - 1];
                if (placements.Contains((last.Composite, last.Entity)))
                    setters.Add(path.Owner);
            }
            string parent = RefactorContext.LeafName(Parent);
            string content = RefactorContext.LeafName(Content);
            if (setters.Any(o => RefactorContext.Truthy(o, ShortGuids.include_in_planar_reflections) || o.childLinks.Any(l => l.thisParamID == ShortGuids.include_in_planar_reflections)))
                Note(parent + " is placed with planar reflections on somewhere: the entities directly inside " + content + " will be reflected there too.");
            if (setters.Any(o => o.GetParameter(ShortGuids.mapping)?.content is cResource mapping && mapping.shortGUID != ShortGuid.Invalid))
                Note(parent + " is placed with a material remapping somewhere: it will also remap the models directly inside " + content + ".");
            if (setters.Any(o => RefactorContext.Truthy(o, ShortGuids.disable_collision) || o.childLinks.Any(l => l.thisParamID == ShortGuids.disable_collision)))
                Note(parent + " is placed with disable_collision somewhere, which will now also apply to the entities directly inside " + content + ".");
        }

        /// <summary>Paths stored inside C that name one of C's pins cannot be carried over, as the pins are not copied.</summary>
        private void AnalyseContentPaths()
        {
            foreach (Entity entity in _contents)
            {
                IEnumerable<ShortGuid[]> paths = Enumerable.Empty<ShortGuid[]>();
                switch (entity)
                {
                    case AliasEntity alias: paths = new[] { alias.alias?.path }; break;
                    case TriggerSequence trigger: paths = trigger.sequence.Select(o => o.connectedEntity?.path); break;
                    case CAGEAnimation animation: paths = animation.connections.Select(o => o.connectedEntity?.path); break;
                    case ProxyEntity proxy: paths = proxy.sequence.Select(o => o.connectedEntity?.path); break;
                }
                foreach (ShortGuid[] path in paths)
                {
                    ResolvedPath resolved = _ctx.Resolve(path, Content, false);
                    if (resolved?.Reading == PathReading.Local && resolved.Steps[0].Entity is VariableEntity)
                        Block("'" + _ctx.NameOf(Content, entity) + "' in " + RefactorContext.LeafName(Content) + " points at one of its pins, which are not copied.");
                }
            }
        }

        private void AnalysePaths()
        {
            List<ResolvedPath> paths = _ctx.FindPaths(new HashSet<ShortGuid>() { Instance.shortGUID });
            cTransform placement = RefactorContext.TransformOf(Instance);

            foreach (ResolvedPath path in paths)
            {
                int step = path.Steps.FindIndex(o => o.Composite == Parent && o.Entity == Instance);
                if (step < 0)
                    continue;
                bool endsOnInstance = step == path.Steps.Count - 1;
                bool endsOnPin = !endsOnInstance && step + 1 == path.Steps.Count - 1 && path.Steps[step + 1].Entity is VariableEntity;
                bool nextIsLast = !endsOnInstance && step + 1 == path.Steps.Count - 1;
                string where = Describe(path.Holder, path.Owner);

                if (path.Kind == PathKind.Alias)
                    _aliasesByPath[PathKey(path.Holder, path.Path)] = (AliasEntity)path.Owner;

                switch (path.Kind)
                {
                    case PathKind.Alias:
                        {
                            AliasEntity alias = (AliasEntity)path.Owner;
                            bool local = path.Holder == Parent && path.Reading == PathReading.Local && step == 0;
                            if (endsOnInstance || endsOnPin)
                            {
                                OverrideLevel level = Level(path.Holder, path.Path.Take(path.Steps[step].Index).ToArray(), local);
                                if (endsOnInstance) level.OnInstance.Add(alias);
                                else level.OnPins.Add((alias, (VariableEntity)path.Steps[step + 1].Entity));
                            }
                            else if (local && nextIsLast)
                                _localMerges.Add((alias, path.Steps[step + 1].Entity));
                            else if (local)
                                _localRewrites.Add(path);
                            else
                                _throughRewrites.Add((path, step));
                            break;
                        }
                    case PathKind.Proxy:
                        if (endsOnInstance || endsOnPin)
                            Block("The proxy " + where + " points at '" + InstanceName + "'" + (endsOnPin ? "'s pin" : "") + " itself, which will no longer exist.");
                        else
                            _throughRewrites.Add((path, step));
                        break;
                    case PathKind.TriggerSequence:
                    case PathKind.ProxySequence:
                        if (endsOnPin)
                            Block("The trigger sequence " + where + " has an entry on one of the pins of '" + InstanceName + "'.");
                        else if (endsOnInstance)
                        {
                            if (IsEntityList(path))
                            {
                                _sequenceFanouts.Add((path, step));
                                Note("The trigger sequence " + where + " lists '" + InstanceName + "': it will list each of its contents instead.");
                            }
                            else
                                Block("The trigger sequence " + where + " calls methods on '" + InstanceName + "' itself, which cannot be spread over its contents.");
                        }
                        else
                            _throughRewrites.Add((path, step));
                        break;
                    case PathKind.Animation:
                        if (endsOnInstance || endsOnPin)
                            Block("The animation " + where + " animates '" + InstanceName + "' itself.");
                        else
                        {
                            CAGEAnimation.Connection connection = ((CAGEAnimation)path.Owner).connections[path.Slot];
                            if (nextIsLast && connection.target_param == ShortGuids.position && !RefactorContext.IsIdentity(placement))
                                Block("The animation " + where + " moves '" + _ctx.NameOf(Content, path.Steps[step + 1].Entity) + "' within '" + InstanceName + "', whose keys are relative to where the instance places it.");
                            _throughRewrites.Add((path, step));
                        }
                        break;
                }
            }

        }

        private OverrideLevel Level(Composite holder, ShortGuid[] prefix, bool isParent)
        {
            string key = PathKey(holder, prefix);
            if (!_levels.TryGetValue(key, out OverrideLevel level))
                _levels.Add(key, level = new OverrideLevel() { Holder = holder, Prefix = prefix, IsParent = isParent && prefix.Length == 0 });
            return level;
        }

        private static string PathKey(Composite holder, IEnumerable<ShortGuid> path)
        {
            return holder.shortGUID.ToByteString() + ":" + string.Join(",", path.Where(o => o != ShortGuid.Invalid).Select(o => o.AsUInt32));
        }

        /// <summary>
        /// A trigger sequence used only as a list of entities (a zone's composites, say), which can list
        /// the contents instead of the instance. One that is triggered calls its methods on every entry,
        /// and an instance's pins do not map onto its contents' methods.
        /// </summary>
        private bool IsEntityList(ResolvedPath path)
        {
            if (path.Kind == PathKind.ProxySequence)
                return ((ProxyEntity)path.Owner).methods.Count == 0;
            TriggerSequence trigger = (TriggerSequence)path.Owner;
            if (trigger.methods.Count != 0)
                return false;
            foreach (Entity entity in path.Holder.GetEntities())
                foreach (EntityConnector link in entity.childLinks)
                    if (link.linkedEntityID == trigger.shortGUID && link.linkedParamID != ShortGuids.reference)
                        return false;
            return true;
        }

        /// <summary>Links owned by I (or an alias overriding it): into its pins, or from its interface.</summary>
        private void AnalyseInstanceLinks(Composite holder, Entity owner, bool isInstance)
        {
            string who = isInstance ? "'" + InstanceName + "'" : Describe(holder, owner);
            foreach (EntityConnector link in owner.childLinks)
            {
                if (_pinsByName.ContainsKey(link.thisParamID))
                    continue;
                if (_ctx.InterfaceRelayToMethod.TryGetValue(link.thisParamID, out ShortGuid method))
                {
                    if (!_contents.Any(o => o is FunctionEntity && _ctx.Has(o, Content, method, out ParameterVariant v) && v == ParameterVariant.METHOD_PIN))
                        Block(who + " sends '" + RefactorContext.ParamName(link.thisParamID) + "', but nothing in " + RefactorContext.LeafName(Content) + " has '" + RefactorContext.ParamName(method) + "' to send it instead.");
                    else
                        Note(who + " sends '" + RefactorContext.ParamName(link.thisParamID) + "': one of its contents will send it instead.");
                    continue;
                }
                if (link.thisParamID == ShortGuids.deleted || link.thisParamID == ShortGuids.include_in_planar_reflections)
                {
                    Note("The linked '" + RefactorContext.ParamName(link.thisParamID) + "' on " + who + " will be linked on each of its contents instead.");
                    continue;
                }
                if (_ctx.InstanceInterface.ContainsKey(link.thisParamID))
                {
                    Block("'" + RefactorContext.ParamName(link.thisParamID) + "' on " + who + " is linked, and cannot be linked on its contents instead.");
                    continue;
                }
                Note(who + " has a link from '" + RefactorContext.ParamName(link.thisParamID) + "', which " + RefactorContext.LeafName(Content) + " has no pin for; it does nothing and will be dropped.");
            }
        }

        /// <summary>Links into I (or an alias overriding it) from its own composite: into pins, onto its interface, or referencing it.</summary>
        private void AnalyseLinksInto(Composite holder, Entity target)
        {
            string who = target == Instance ? "'" + InstanceName + "'" : Describe(holder, target);
            foreach (Entity owner in holder.GetEntities())
            {
                foreach (EntityConnector link in owner.childLinks)
                {
                    if (link.linkedEntityID != target.shortGUID || _pinsByName.ContainsKey(link.linkedParamID))
                        continue;
                    if (_ctx.InstanceInterface.TryGetValue(link.linkedParamID, out ParameterVariant variant) && variant == ParameterVariant.METHOD_PIN)
                    {
                        Note("'" + _ctx.NameOf(holder, owner) + "' calls '" + RefactorContext.ParamName(link.linkedParamID) + "' on " + who + ": it will call it on each of its contents that has it.");
                        continue;
                    }
                    if (link.linkedParamID == ShortGuids.reference)
                    {
                        if (IsCollectionReference(holder, owner, link.thisParamID))
                            Note("'" + _ctx.NameOf(holder, owner) + "' lists " + who + " in '" + RefactorContext.ParamName(link.thisParamID) + "': it will list each of its contents instead.");
                        else
                            Block("'" + _ctx.NameOf(holder, owner) + "' refers to " + who + " itself (its '" + RefactorContext.ParamName(link.thisParamID) + "'), and that cannot be pointed at its contents.");
                        continue;
                    }
                    if (_ctx.InstanceInterface.ContainsKey(link.linkedParamID))
                    {
                        Block("'" + _ctx.NameOf(holder, owner) + "' reads '" + RefactorContext.ParamName(link.linkedParamID) + "' from " + who + ".");
                        continue;
                    }
                    Note("'" + _ctx.NameOf(holder, owner) + "' links to '" + RefactorContext.ParamName(link.linkedParamID) + "' on " + who + ", which " + RefactorContext.LeafName(Content) + " has no pin for; it does nothing and will be dropped.");
                }
            }
        }

        private bool IsCollectionReference(Composite holder, Entity owner, ShortGuid param)
        {
            FunctionType? type = OwnerFunctionType(holder, owner);
            if (type == null)
                return false;
            string name = RefactorContext.ParamName(param);
            FunctionType? current = type;
            while (current != null)
            {
                if (_collectionReferences.Contains((current.Value, name)))
                    return true;
                current = _ctx.Utils.GetInheritedFunction(current.Value);
            }
            return false;
        }

        private FunctionType? OwnerFunctionType(Composite holder, Entity owner)
        {
            Entity resolved = owner;
            if (owner is AliasEntity || owner is ProxyEntity)
                resolved = _ctx.Utils.GetResolvedTarget(_ctx.Utils.ResolveAliasOrProxy(owner, holder)).Item2;
            if (resolved is FunctionEntity function && function.function.IsFunctionType)
                return function.function.AsFunctionType;
            return null;
        }

        /// <summary>Flags set on I (or an alias overriding it) that its contents cannot all carry.</summary>
        private void AnalyseFlags(Entity entity)
        {
            bool hasLeaves = _contents.Any(o => o is FunctionEntity && _ctx.InstancedComposite(o) == null);
            if (!hasLeaves)
                return;
            if (RefactorContext.Truthy(entity, ShortGuids.disable_display))
                Note("'" + InstanceName + "' has disable_display set: nested instances will carry it, but the entities directly inside " + RefactorContext.LeafName(Content) + " have no such flag and will display.");
            if (RefactorContext.Truthy(entity, ShortGuids.delete_standard_collision) || RefactorContext.Truthy(entity, ShortGuids.delete_ballistic_collision))
                Note("'" + InstanceName + "' deletes collision: nested instances will carry that, the models directly inside " + RefactorContext.LeafName(Content) + " will keep theirs.");
            if (RefactorContext.Truthy(entity, ShortGuids.disable_collision))
                Note("'" + InstanceName + "' has disable_collision set, which only its direct contents read; they will read their new parent's instead.");
            if (entity.GetParameter(ShortGuids.mapping)?.content is cResource mapping && mapping.shortGUID != ShortGuid.Invalid)
                Note("'" + InstanceName + "' remaps its models' materials (mapping), which only its direct contents read; they will read their new parent's instead.");
        }
        #endregion

        #region Apply
        /// <summary>
        /// Carry out the refactor. Throws if the plan has a blocking issue. <paramref name="pages"/> may be
        /// null, in which case no script pages are produced.
        /// </summary>
        public RefactorResult Apply(IRefactorPageSource pages)
        {
            if (!CanApply)
                throw new InvalidOperationException("This instance cannot be de-instanced: " + string.Join(" ", Issues.Where(o => o.Blocking).Select(o => o.Message)));
            return new Applier(this, pages).Run();
        }

        /// <summary>The working state of one application of a plan.</summary>
        private sealed class Applier
        {
            private readonly DeinstancePlan _plan;
            private readonly RefactorContext _ctx;
            private readonly IRefactorPageSource _pageSource;
            private readonly ScriptTransaction _tx;
            private readonly Composite P, C;
            private readonly FunctionEntity I;

            private readonly Dictionary<ShortGuid, ShortGuid> _idMap = new Dictionary<ShortGuid, ShortGuid>();
            private readonly Dictionary<Entity, Entity> _copies = new Dictionary<Entity, Entity>();
            private readonly List<Entity> _copyOrder = new List<Entity>();
            private readonly HashSet<Entity> _removedFromParent = new HashSet<Entity>();
            private readonly Dictionary<ShortGuid, ShortGuid> _parentNodeRemap = new Dictionary<ShortGuid, ShortGuid>();

            //Ending records at each pin in P's terms, and readers left with a value, for the levels above to extend
            private readonly Dictionary<VariableEntity, List<(Entity owner, ShortGuid param)>> _endings = new Dictionary<VariableEntity, List<(Entity, ShortGuid)>>();
            private readonly Dictionary<VariableEntity, List<(Entity owner, ShortGuid param)>> _staticReaders = new Dictionary<VariableEntity, List<(Entity, ShortGuid)>>();

            private readonly Dictionary<Composite, List<PageHint>> _hints = new Dictionary<Composite, List<PageHint>>();
            private readonly Dictionary<Composite, HashSet<ShortGuid>> _removedNodes = new Dictionary<Composite, HashSet<ShortGuid>>();
            private readonly RefactorResult _result = new RefactorResult();

            //The P-level entities that stand for I: I itself and any alias in P on [I]
            private readonly List<Entity> _parentOverrides = new List<Entity>();
            private cTransform _placement;

            public Applier(DeinstancePlan plan, IRefactorPageSource pages)
            {
                _plan = plan;
                _ctx = plan._ctx;
                _pageSource = pages;
                _tx = new ScriptTransaction(_ctx.Commands);
                P = plan.Parent;
                C = plan.Content;
                I = plan.Instance;
            }

            /// <summary>Do it; if anything fails part way, put back everything touched so far and pass the failure on.</summary>
            public RefactorResult Run()
            {
                try
                {
                    return RunCore();
                }
                catch
                {
                    _tx.Revert();
                    throw;
                }
            }

            private RefactorResult RunCore()
            {
                _tx.Touch(P);
                _parentOverrides.Add(I);
                OverrideLevel parentLevel = _plan._levels.Values.FirstOrDefault(o => o.IsParent);
                if (parentLevel != null)
                    _parentOverrides.AddRange(parentLevel.OnInstance);

                _placement = EffectivePlacement(parentLevel);

                MakeCopies();
                RemapCopiedPaths();
                PlaceCopies();
                SpliceContentLinks();
                SpliceParentLinks();
                PushDownFlags(P, _parentOverrides, e => e);
                MergeLocalAliases();
                RewriteLocalAliases();
                RewriteThroughPaths();
                FanOutSequences();
                foreach (OverrideLevel level in _plan._levels.Values)
                    if (!level.IsParent)
                        SpreadOverrides(level);

                //I goes, and anything in P that stood for it; the copies go in (the first takes I's slot)
                RemoveFromParent(I);
                if (parentLevel != null)
                {
                    foreach (AliasEntity alias in parentLevel.OnInstance) RemoveFromParent(alias);
                    foreach ((AliasEntity alias, VariableEntity _) in parentLevel.OnPins) RemoveFromParent(alias);
                }
                foreach (Entity copy in _copyOrder)
                {
                    if (_removedFromParent.Contains(copy)) continue;
                    AddToComposite(P, copy);
                    _result.PulledEntities.Add(copy);
                }

                TakeShadowedValuesOffAliases();

                foreach (KeyValuePair<ShortGuid, ShortGuid> pair in _idMap)
                    _result.IdMap[pair.Key] = _parentNodeRemap.TryGetValue(pair.Value, out ShortGuid merged) ? merged : pair.Value;

                BuildPages();
                _tx.Commit();
                _result.Transaction = _tx;
                _result.Issues.AddRange(_plan.Issues);
                return _result;
            }

            #region Copies
            private void MakeCopies()
            {
                HashSet<ShortGuid> taken = new HashSet<ShortGuid>(P.GetEntities().Select(o => o.shortGUID));
                taken.Add(I.shortGUID);
                foreach (Entity entity in _plan._contents)
                {
                    ShortGuid id = entity.shortGUID;
                    while (taken.Contains(id))
                        id = ShortGuidUtils.GenerateRandom();
                    taken.Add(id);
                    _idMap.Add(entity.shortGUID, id);
                }

                HashSet<ShortGuid> resourceIds = RefactorContext.ResourceIds(P);
                foreach (Entity entity in _plan._contents)
                {
                    Entity copy = entity.Copy();
                    copy.shortGUID = _idMap[entity.shortGUID];
                    copy.childLinks = new List<EntityConnector>();
                    RefactorContext.ShareResources(copy, entity, resourceIds);
                    if (copy.GetParameter(ShortGuids.name) == null && !(copy is VariableEntity))
                        _ctx.Utils.SetEntityName(copy, _ctx.NameOf(C, entity));
                    _copies.Add(entity, copy);
                    _copyOrder.Add(copy);
                }
            }

            private Entity CopyOf(Entity contentEntity) => contentEntity != null && _copies.TryGetValue(contentEntity, out Entity copy) ? copy : null;

            /// <summary>Paths inside the copies that were read from C now read from P, where C's entities have new ids.</summary>
            private void RemapCopiedPaths()
            {
                foreach (Entity entity in _plan._contents)
                {
                    Entity copy = _copies[entity];
                    switch (entity)
                    {
                        case AliasEntity alias:
                            ((AliasEntity)copy).alias = new EntityPath() { path = RemapLocal(alias.alias?.path) };
                            break;
                        case ProxyEntity proxy:
                            ProxyEntity proxyCopy = (ProxyEntity)copy;
                            for (int i = 0; i < proxy.sequence.Count; i++)
                                proxyCopy.sequence[i].connectedEntity = new EntityPath() { path = RemapLocal(proxy.sequence[i].connectedEntity?.path) };
                            break;
                        case TriggerSequence trigger:
                            TriggerSequence triggerCopy = (TriggerSequence)copy;
                            for (int i = 0; i < trigger.sequence.Count; i++)
                                triggerCopy.sequence[i].connectedEntity = new EntityPath() { path = RemapLocal(trigger.sequence[i].connectedEntity?.path) };
                            break;
                        case CAGEAnimation animation:
                            CAGEAnimation animationCopy = (CAGEAnimation)copy;
                            for (int i = 0; i < animation.connections.Count; i++)
                                animationCopy.connections[i].connectedEntity = new EntityPath() { path = RemapLocal(animation.connections[i].connectedEntity?.path) };
                            foreach (CAGEAnimation.EventTrack track in animationCopy.eventTracks)
                                foreach (CAGEAnimation.EventTrack.Keyframe key in track.keyframes)
                                    if (key.track_type == ANIM_TRACK_TYPE.T_GUID && _idMap.TryGetValue(key.forward, out ShortGuid moved))
                                        key.forward = moved;
                            break;
                    }
                }
            }

            private ShortGuid[] RemapLocal(ShortGuid[] path)
            {
                if (path == null)
                    return new ShortGuid[0];
                ShortGuid[] copy = (ShortGuid[])path.Clone();
                ResolvedPath resolved = _ctx.Resolve(path, C, false);
                if (resolved?.Reading == PathReading.Local && _idMap.TryGetValue(copy[resolved.Steps[0].Index], out ShortGuid moved))
                    copy[resolved.Steps[0].Index] = moved;
                return copy;
            }

            /// <summary>Where I places C's contents: its position, or an alias in P overriding it (the last one, as they apply in turn).</summary>
            private cTransform EffectivePlacement(OverrideLevel parentLevel)
            {
                cTransform placement = RefactorContext.TransformOf(I);
                if (parentLevel != null)
                    foreach (AliasEntity alias in parentLevel.OnInstance)
                        placement = RefactorContext.TransformOf(alias) ?? placement;
                return placement;
            }

            /// <summary>
            /// An entity's position within C, as it stands at P: its own, or what an alias in P on [I, x]
            /// (the last one), or failing that one of C's own aliases on it, sets.
            /// </summary>
            private cTransform LocalPosition(Entity contentEntity)
            {
                cTransform local = null;
                foreach ((AliasEntity alias, Entity target) in _plan._localMerges)
                    if (target == contentEntity && RefactorContext.TransformOf(alias) != null)
                        local = RefactorContext.TransformOf(alias);
                if (local != null)
                    return local;
                foreach (AliasEntity alias in C.aliases_dictionary.Values)
                {
                    ResolvedPath resolved = _ctx.Resolve(alias.alias?.path, C, false);
                    if (resolved?.Reading == PathReading.Local && resolved.Steps.Count == 1 && resolved.Steps[0].Entity == contentEntity && RefactorContext.TransformOf(alias) != null)
                        local = RefactorContext.TransformOf(alias);
                }
                return local ?? RefactorContext.TransformOf(contentEntity);
            }

            private void PlaceCopies()
            {
                foreach (Entity entity in _plan._contents)
                {
                    if (!(entity is FunctionEntity) || !_ctx.IsSpatial(entity, C))
                        continue;
                    cTransform local = LocalPosition(entity);
                    if (local == null && RefactorContext.IsIdentity(_placement))
                        continue;
                    RefactorContext.SetParameter(_copies[entity], ShortGuids.position, RefactorContext.Compose(_placement, local));
                }

                //One of C's own aliases on a direct child moves that child within C: its copy moves the child's copy within P
                foreach (AliasEntity alias in C.aliases_dictionary.Values)
                {
                    cTransform local = RefactorContext.TransformOf(alias);
                    if (local == null) continue;
                    ResolvedPath resolved = _ctx.Resolve(alias.alias?.path, C, false);
                    if (resolved?.Reading == PathReading.Local && resolved.Steps.Count == 1)
                        RefactorContext.SetParameter(_copies[alias], ShortGuids.position, RefactorContext.Compose(_placement, local));
                }
            }
            #endregion

            #region Pins
            /// <summary>Where a pin's value or event comes from or goes to, in P's terms.</summary>
            private struct Source
            {
                public Entity Target;
                public ShortGuid Param;
                public LinkKey From;           //The record this came from, for drawing it
            }

            /// <summary>What replaces one record that ended at a pin (or at I itself).</summary>
            private sealed class Replacement
            {
                public List<EntityConnector> Links = new List<EntityConnector>();
                public bool IsValue;                 //A data pin nothing feeds: the owner reads its value instead
                public ParameterData Value;
                public HashSet<VariableEntity> Pins = new HashSet<VariableEntity>();
            }

            /// <summary>
            /// The records that start from a pin, followed through any pin they lead to: the pin's own (in C),
            /// then I's, then those of aliases in P on I or on the pin - in the order they are applied, so the
            /// last one still wins for a value. Empty for a data pin means the value is all there is.
            /// </summary>
            private List<Source> Starts(VariableEntity pin, HashSet<VariableEntity> visited)
            {
                List<Source> sources = new List<Source>();
                if (!visited.Add(pin))
                    return sources;
                foreach (EntityConnector link in pin.childLinks)
                {
                    Entity target = C.GetEntityByID(link.linkedEntityID);
                    if (target is VariableEntity next)
                        sources.AddRange(Starts(next, visited));
                    else if (target != null)
                        sources.Add(new Source() { Target = CopyOf(target), Param = link.linkedParamID, From = new LinkKey(pin.shortGUID, link.thisParamID, _idMap[target.shortGUID], link.linkedParamID) });
                }
                foreach (Entity overrider in ParentOverridesFor(pin))
                {
                    foreach (EntityConnector link in overrider.childLinks)
                    {
                        if (link.thisParamID != pin.name) continue;
                        if (link.linkedEntityID == I.shortGUID && _plan._pinsByName.TryGetValue(link.linkedParamID, out VariableEntity next))
                        {
                            sources.AddRange(Starts(next, visited));
                            continue;
                        }
                        Entity target = P.GetEntityByID(link.linkedEntityID);
                        if (target != null && target != I)
                            sources.Add(new Source() { Target = target, Param = link.linkedParamID, From = new LinkKey(overrider, link) });
                    }
                }
                return sources;
            }

            /// <summary>I, then the aliases in P overriding it, then aliases in P on this one pin - the P-level holders of a pin's records and value.</summary>
            private IEnumerable<Entity> ParentOverridesFor(VariableEntity pin)
            {
                foreach (Entity entity in _parentOverrides)
                    yield return entity;
                OverrideLevel parentLevel = _plan._levels.Values.FirstOrDefault(o => o.IsParent);
                if (parentLevel != null)
                    foreach ((AliasEntity alias, VariableEntity onPin) in parentLevel.OnPins)
                        if (onPin == pin)
                            yield return alias;
            }

            /// <summary>The value a data pin has when nothing feeds it: the pin's default, then I's, then P's aliases', last one set winning.</summary>
            private ParameterData StaticValue(VariableEntity pin)
            {
                ParameterData value = pin.GetParameter(pin.name)?.content;
                foreach (Entity overrider in ParentOverridesFor(pin))
                    value = overrider.GetParameter(pin.name)?.content ?? value;
                return RefactorContext.CloneData(value);
            }

            /// <summary>
            /// A record at P that ends at a pin, replaced: by a record to each thing the pin leads to, or, for a
            /// data pin that nothing feeds, by the pin's value.
            /// </summary>
            private Replacement Splice(Entity owner, ShortGuid param, VariableEntity pin, LinkKey from)
            {
                Replacement replacement = new Replacement();
                List<Source> sources = Starts(pin, replacement.Pins);
                foreach (VariableEntity reached in replacement.Pins)
                {
                    if (!_endings.TryGetValue(reached, out var list)) _endings.Add(reached, list = new List<(Entity, ShortGuid)>());
                    list.Add((owner, param));
                }
                if (sources.Count == 0)
                {
                    if (!_plan._pinIsEvent[pin])
                    {
                        replacement.IsValue = true;
                        replacement.Value = StaticValue(pin);
                    }
                    return replacement;
                }
                foreach (Source source in sources)
                {
                    replacement.Links.Add(new EntityConnector(source.Target.shortGUID, param, source.Param));
                    LinkKey key = new LinkKey(owner.shortGUID, param, source.Target.shortGUID, source.Param);
                    Hint(P, key, from);
                    Hint(P, key, source.From);
                }
                return replacement;
            }

            /// <summary>
            /// Put a replacement in place of the record at <paramref name="index"/> of <paramref name="original"/>
            /// (the owner's links before the refactor), adding to <paramref name="result"/> (its links being rebuilt).
            /// A value only matters if no later record drives the same parameter (the last one wins); when it does,
            /// any earlier record for the parameter was only ever overridden by this one, so it goes, and the value
            /// is written onto the owner. Aliases that set that parameter on the owner used to lose to the record
            /// (a link beats an alias's value), so they have it taken off them once all is in place.
            /// </summary>
            private void Put(Composite holder, Entity owner, List<EntityConnector> original, int index, List<EntityConnector> result, Replacement replacement)
            {
                if (!replacement.IsValue)
                {
                    result.AddRange(replacement.Links);
                    return;
                }
                ShortGuid param = original[index].thisParamID;
                for (int i = index + 1; i < original.Count; i++)
                    if (original[i].thisParamID == param)
                        return;
                result.RemoveAll(o => o.thisParamID == param);
                if (replacement.Value != null)
                {
                    _tx.Touch(owner);
                    RefactorContext.SetParameter(owner, param, replacement.Value);
                }
                _linkBecameValue.Add((holder, owner, param));
                if (holder == P)
                {
                    foreach (VariableEntity reached in replacement.Pins)
                    {
                        if (!_staticReaders.TryGetValue(reached, out var list)) _staticReaders.Add(reached, list = new List<(Entity, ShortGuid)>());
                        list.Add((owner, param));
                    }
                }
            }

            private void Hint(Composite composite, LinkKey link, LinkKey from)
            {
                if (!_hints.TryGetValue(composite, out List<PageHint> hints))
                    _hints.Add(composite, hints = new List<PageHint>());
                hints.Add(new PageHint() { Link = link, From = from });
            }

            /// <summary>C's links, copied between the copies, with every record that ends at a pin spliced.</summary>
            private void SpliceContentLinks()
            {
                foreach (Entity entity in _plan._contents)
                {
                    Entity copy = _copies[entity];
                    List<EntityConnector> original = entity.childLinks;
                    List<EntityConnector> result = new List<EntityConnector>();
                    for (int i = 0; i < original.Count; i++)
                    {
                        EntityConnector link = original[i];
                        Entity target = C.GetEntityByID(link.linkedEntityID);
                        if (target is VariableEntity pin)
                            Put(P, copy, original, i, result, Splice(copy, link.thisParamID, pin, new LinkKey(copy.shortGUID, link.thisParamID, pin.shortGUID, link.linkedParamID)));
                        else if (target != null)
                            result.Add(new EntityConnector(_idMap[target.shortGUID], link.thisParamID, link.linkedParamID));
                    }
                    copy.childLinks = result;
                }
            }

            /// <summary>P's links into I (and into aliases in P standing for it or its pins), each replaced where it stood.</summary>
            private void SpliceParentLinks()
            {
                OverrideLevel parentLevel = _plan._levels.Values.FirstOrDefault(o => o.IsParent);
                Dictionary<ShortGuid, VariableEntity> pinAliases = new Dictionary<ShortGuid, VariableEntity>();
                if (parentLevel != null)
                    foreach ((AliasEntity alias, VariableEntity pin) in parentLevel.OnPins)
                        pinAliases[alias.shortGUID] = pin;
                HashSet<ShortGuid> standsForInstance = new HashSet<ShortGuid>(_parentOverrides.Select(o => o.shortGUID));

                foreach (Entity owner in P.GetEntities())
                {
                    if (standsForInstance.Contains(owner.shortGUID) || pinAliases.ContainsKey(owner.shortGUID))
                        continue;
                    if (!owner.childLinks.Any(o => standsForInstance.Contains(o.linkedEntityID) || pinAliases.ContainsKey(o.linkedEntityID)))
                        continue;
                    _tx.Touch(owner);
                    List<EntityConnector> original = owner.childLinks;
                    List<EntityConnector> links = new List<EntityConnector>();
                    for (int i = 0; i < original.Count; i++)
                    {
                        EntityConnector link = original[i];
                        LinkKey from = new LinkKey(owner, link);
                        if (pinAliases.TryGetValue(link.linkedEntityID, out VariableEntity aliasedPin))
                        {
                            if (link.linkedParamID == aliasedPin.name)
                                Put(P, owner, original, i, links, Splice(owner, link.thisParamID, aliasedPin, from));
                            continue;
                        }
                        if (!standsForInstance.Contains(link.linkedEntityID))
                        {
                            links.Add(link);
                            continue;
                        }
                        Put(P, owner, original, i, links, ReplaceLinkIntoInstance(P, owner, link, from, e => e, null));
                    }
                    owner.childLinks = links;
                }

                //Links I (and the aliases standing for it) own from its interface move onto its contents
                foreach (Entity overrider in _parentOverrides)
                    MoveInterfaceLinks(P, overrider, e => e);
            }

            /// <summary>
            /// A link into I (or an alias standing for it, at any level): into a pin, onto its interface, or a
            /// reference to it. Returns what replaces it, with each copy expressed through <paramref name="standIn"/>
            /// (the copy itself in P, an alias on it further up).
            /// </summary>
            private Replacement ReplaceLinkIntoInstance(Composite holder, Entity owner, EntityConnector link, LinkKey from, Func<Entity, Entity> standIn, OverrideLevel level)
            {
                if (_plan._pinsByName.TryGetValue(link.linkedParamID, out VariableEntity pin))
                {
                    if (holder == P)
                        return Splice(owner, link.thisParamID, pin, from);
                    return SpliceAbove(holder, owner, link.thisParamID, pin, from, standIn, level);
                }
                Replacement replacement = new Replacement();
                bool isMethod = _ctx.InstanceInterface.TryGetValue(link.linkedParamID, out ParameterVariant variant) && variant == ParameterVariant.METHOD_PIN;
                bool isListing = link.linkedParamID == ShortGuids.reference;
                if (!isMethod && !isListing)
                    return replacement;
                foreach (Entity entity in _plan._contents)
                {
                    if (!(entity is FunctionEntity)) continue;
                    if (isMethod && !(_ctx.Has(entity, C, link.linkedParamID, out ParameterVariant v) && v == ParameterVariant.METHOD_PIN)) continue;
                    Entity target = standIn(_copies[entity]);
                    replacement.Links.Add(new EntityConnector(target.shortGUID, link.thisParamID, link.linkedParamID));
                    Hint(holder, new LinkKey(owner.shortGUID, link.thisParamID, target.shortGUID, link.linkedParamID), from);
                }
                return replacement;
            }

            /// <summary>
            /// Links owned by I (or an alias standing for it) from its interface: a relay goes to one copy
            /// with the method it relays (they are all called together), deleted onto every copy, planar
            /// reflections onto the direct contents that have it (it is read from the direct parent only).
            /// Records from pins are handled by the splice.
            /// </summary>
            private void MoveInterfaceLinks(Composite holder, Entity overrider, Func<Entity, Entity> standIn)
            {
                foreach (EntityConnector link in overrider.childLinks)
                {
                    if (_plan._pinsByName.ContainsKey(link.thisParamID))
                        continue;
                    LinkKey from = new LinkKey(overrider, link);
                    if (_ctx.InterfaceRelayToMethod.TryGetValue(link.thisParamID, out ShortGuid method))
                    {
                        Entity sender = _plan._contents.FirstOrDefault(o => o is FunctionEntity && _ctx.Has(o, C, method, out ParameterVariant v) && v == ParameterVariant.METHOD_PIN);
                        if (sender != null)
                            AddLink(holder, standIn(_copies[sender]), link.thisParamID, link.linkedEntityID, link.linkedParamID, from);
                        continue;
                    }
                    bool planar = link.thisParamID == ShortGuids.include_in_planar_reflections;
                    if (link.thisParamID != ShortGuids.deleted && !planar)
                        continue;
                    ShortGuid param = planar ? link.thisParamID : ShortGuids.delete_me;
                    foreach (Entity entity in _plan._contents)
                    {
                        if (!(entity is FunctionEntity)) continue;
                        if (planar && (_ctx.InstancedComposite(entity) != null || !_ctx.Has(entity, C, param, out ParameterVariant _))) continue;
                        //The flag is the entity's own OR its parent's: one already set stays set, whatever the link says,
                        //and a link written onto it would replace its own value rather than join it
                        Entity copy = _copies[entity];
                        if (planar ? RefactorContext.Truthy(copy, param) : (RefactorContext.Truthy(copy, ShortGuids.delete_me) || RefactorContext.Truthy(copy, ShortGuids.deleted))) continue;
                        AddLink(holder, standIn(copy), param, link.linkedEntityID, link.linkedParamID, from);
                    }
                }
            }

            private void AddLink(Composite holder, Entity owner, ShortGuid param, ShortGuid target, ShortGuid targetParam, LinkKey from)
            {
                _tx.Touch(owner);
                owner.childLinks.Add(new EntityConnector(target, param, targetParam));
                Hint(holder, new LinkKey(owner.shortGUID, param, target, targetParam), from);
            }

            /// <summary>
            /// Parameters whose record was replaced by the value it read, per owner: an alias anywhere that sets
            /// one of them used to lose to the record, and would now win - so it no longer sets it. The aliases
            /// this refactor wrote a pin's value onto are left, since that value is what the record would have read.
            /// </summary>
            private void TakeShadowedValuesOffAliases()
            {
                if (_linkBecameValue.Count == 0)
                    return;
                Dictionary<(Composite, Entity), HashSet<ShortGuid>> byOwner = new Dictionary<(Composite, Entity), HashSet<ShortGuid>>();
                foreach ((Composite holder, Entity owner, ShortGuid param) in _linkBecameValue)
                {
                    if (!byOwner.TryGetValue((holder, owner), out HashSet<ShortGuid> set)) byOwner.Add((holder, owner), set = new HashSet<ShortGuid>());
                    set.Add(param);
                }
                HashSet<ShortGuid> ids = new HashSet<ShortGuid>(byOwner.Keys.Select(o => o.Item2.shortGUID));
                foreach (ResolvedPath path in _ctx.FindPaths(ids))
                {
                    if (path.Kind != PathKind.Alias) continue;
                    PathStep last = path.Steps[path.Steps.Count - 1];
                    if (!byOwner.TryGetValue((last.Composite, last.Entity), out HashSet<ShortGuid> parameters)) continue;
                    AliasEntity alias = (AliasEntity)path.Owner;
                    foreach (ShortGuid param in parameters)
                    {
                        if (_pinValuesWritten.Contains((alias, param)) || alias.GetParameter(param) == null) continue;
                        _tx.Touch(alias);
                        alias.parameters.RemoveAll(o => o.name == param);
                    }
                }
            }
            #endregion

            #region Flags
            /// <summary>
            /// Flags on I (or the aliases standing for it) that the engine applies to an instance's contents,
            /// written onto the copies that can carry them: deleted onto everything, planar reflections onto
            /// the direct contents that have it, display and collision deletion onto nested instances.
            /// </summary>
            private void PushDownFlags(Composite holder, IEnumerable<Entity> overriders, Func<Entity, Entity> standIn)
            {
                Dictionary<ShortGuid, ParameterData> flags = new Dictionary<ShortGuid, ParameterData>();
                foreach (Entity overrider in overriders)
                    foreach (ShortGuid flag in new[] { ShortGuids.deleted, ShortGuids.include_in_planar_reflections, ShortGuids.disable_display, ShortGuids.delete_standard_collision, ShortGuids.delete_ballistic_collision })
                    {
                        ParameterData value = overrider.GetParameter(flag)?.content;
                        if (value != null) flags[flag] = value;
                    }

                foreach (KeyValuePair<ShortGuid, ParameterData> flag in flags)
                {
                    bool set = RefactorContext.Truthy(flag.Value);
                    //At P a flag that is off changes nothing; above it an alias can switch one off again, so it is always written there
                    if (!set && holder == P)
                        continue;
                    foreach (Entity entity in _plan._contents)
                    {
                        if (!(entity is FunctionEntity)) continue;
                        bool nested = _ctx.InstancedComposite(entity) != null;
                        ShortGuid param = flag.Key;
                        if (flag.Key == ShortGuids.deleted)
                            param = ShortGuids.delete_me;
                        else if (flag.Key == ShortGuids.include_in_planar_reflections)
                        {
                            if (nested || !_ctx.Has(entity, C, param, out ParameterVariant _)) continue;
                        }
                        else if (!nested)
                            continue;
                        Entity target = standIn(_copies[entity]);
                        _tx.Touch(target);
                        RefactorContext.SetParameter(target, param, new cBool(set));
                    }
                }
            }
            #endregion

            #region Aliases in P
            /// <summary>Aliases in P on [I, x]: their overrides are written onto x's copy, and anything that pointed at them now points at it.</summary>
            private void MergeLocalAliases()
            {
                foreach ((AliasEntity alias, Entity target) in _plan._localMerges)
                {
                    Entity copy = _copies[target];
                    MergeAliasInto(P, alias, copy, skipPosition: true);
                }
            }

            private void MergeAliasInto(Composite holder, AliasEntity alias, Entity into, bool skipPosition)
            {
                _tx.Touch(into);
                foreach (Parameter parameter in alias.parameters)
                {
                    if (parameter.name == ShortGuids.name) continue;
                    if (skipPosition && parameter.name == ShortGuids.position) continue;
                    //The alias's value lost to the record that has now become a value itself
                    if (_linkBecameValue.Contains((holder, into, parameter.name))) continue;
                    RefactorContext.SetParameter(into, parameter.name, RefactorContext.CloneData(parameter.content));
                }
                into.childLinks.AddRange(alias.childLinks);
                _mergedInto[alias] = into;
                foreach (Entity owner in holder.GetEntities().Concat(holder == P ? _copyOrder : Enumerable.Empty<Entity>()))
                {
                    if (!owner.childLinks.Any(o => o.linkedEntityID == alias.shortGUID)) continue;
                    _tx.Touch(owner);
                    owner.childLinks = owner.childLinks.Select(o => o.linkedEntityID == alias.shortGUID ? new EntityConnector(into.shortGUID, o.thisParamID, o.linkedParamID) : o).ToList();
                }
                if (holder == P)
                    _parentNodeRemap[alias.shortGUID] = into.shortGUID;
                RemoveFromComposite(holder, alias);
            }

            /// <summary>
            /// Aliases in P on [I, J, ...] lose the I step. If one of C's own aliases (copied) now has the same
            /// path, the two become one, with P's overrides winning - the order instancing applied them in.
            /// </summary>
            private void RewriteLocalAliases()
            {
                Dictionary<string, AliasEntity> copiedByPath = new Dictionary<string, AliasEntity>();
                foreach (Entity copy in _copyOrder)
                    if (copy is AliasEntity alias)
                        copiedByPath[PathKey(P, alias.alias.path)] = alias;

                foreach (ResolvedPath path in _plan._localRewrites)
                {
                    AliasEntity alias = (AliasEntity)path.Owner;
                    int step = path.Steps.FindIndex(o => o.Composite == P && o.Entity == I);
                    ShortGuid[] rewritten = DropInstanceStep(path, step);
                    _tx.Touch(alias);
                    alias.alias = new EntityPath() { path = rewritten };

                    if (copiedByPath.TryGetValue(PathKey(P, rewritten), out AliasEntity copied) && !_removedFromParent.Contains(copied))
                    {
                        //C's overrides first, then P's on top
                        foreach (Parameter parameter in copied.parameters)
                            if (alias.GetParameter(parameter.name) == null)
                                alias.parameters.Add(new Parameter(parameter.name, RefactorContext.CloneData(parameter.content), parameter.variant));
                        alias.childLinks = copied.childLinks.Concat(alias.childLinks).ToList();
                        foreach (Entity copy in _copyOrder)
                        {
                            if (!copy.childLinks.Any(o => o.linkedEntityID == copied.shortGUID)) continue;
                            copy.childLinks = copy.childLinks.Select(o => o.linkedEntityID == copied.shortGUID ? new EntityConnector(alias.shortGUID, o.thisParamID, o.linkedParamID) : o).ToList();
                        }
                        _parentNodeRemap[copied.shortGUID] = alias.shortGUID;
                        _removedFromParent.Add(copied);
                    }
                }
            }

            /// <summary>The stored path with I's element (and any repeats of it) taken out and the next element given its copy's id.</summary>
            private ShortGuid[] DropInstanceStep(ResolvedPath path, int step)
            {
                int instanceIndex = path.Steps[step].Index;
                int nextIndex = path.Steps[step + 1].Index;
                List<ShortGuid> elements = new List<ShortGuid>();
                for (int i = 0; i < path.Path.Length; i++)
                {
                    if (i >= instanceIndex && i < nextIndex) continue;
                    if (i == nextIndex && _idMap.TryGetValue(path.Path[i], out ShortGuid moved))
                        elements.Add(moved);
                    else
                        elements.Add(path.Path[i]);
                }
                return elements.ToArray();
            }
            #endregion

            #region Paths elsewhere
            /// <summary>
            /// Every other stored path through I loses the I step. An alias above P on [.., I, x] that moves
            /// x is now moving x's copy, which sits in P's space: its position is composed with where I puts
            /// things at that level.
            /// </summary>
            private void RewriteThroughPaths()
            {
                foreach ((ResolvedPath path, int step) in _plan._throughRewrites)
                {
                    ShortGuid[] rewritten = DropInstanceStep(path, step);
                    _tx.Touch(path.Owner);
                    RefactorContext.StorePath(path, rewritten);
                    if (path.Holder == C && _copies.TryGetValue(path.Owner, out Entity copy))
                        RefactorContext.StorePath(new ResolvedPath() { Owner = copy, Kind = path.Kind, Slot = path.Slot }, (ShortGuid[])rewritten.Clone());

                    if (path.Kind == PathKind.Alias && step + 1 == path.Steps.Count - 1)
                    {
                        cTransform local = RefactorContext.TransformOf(path.Owner);
                        if (local != null)
                        {
                            _originalLocalPositions[(AliasEntity)path.Owner] = local;
                            ShortGuid[] prefix = path.Path.Take(path.Steps[step].Index).ToArray();
                            RefactorContext.SetParameter(path.Owner, ShortGuids.position, RefactorContext.Compose(PlacementAt(path.Holder, prefix), local));
                        }
                    }
                }
            }

            /// <summary>Where I places things as seen from an alias level: an alias there on I overrides it, otherwise it is as at P.</summary>
            private cTransform PlacementAt(Composite holder, ShortGuid[] prefix)
            {
                if (_plan._levels.TryGetValue(PathKey(holder, prefix), out OverrideLevel level))
                {
                    cTransform placement = null;
                    foreach (AliasEntity alias in level.OnInstance)
                        placement = RefactorContext.TransformOf(alias) ?? placement;
                    if (placement != null)
                        return placement;
                }
                return _placement;
            }

            /// <summary>A trigger sequence used as a list that lists I lists each of the copies instead, at the same time.</summary>
            private void FanOutSequences()
            {
                foreach (IGrouping<Entity, (ResolvedPath path, int step)> group in _plan._sequenceFanouts.GroupBy(o => o.path.Owner))
                {
                    Entity owner = group.Key;
                    _tx.Touch(owner);
                    List<TriggerSequence.SequenceEntry> sequence = owner is TriggerSequence trigger ? trigger.sequence : ((ProxyEntity)owner).sequence;
                    Dictionary<int, (ResolvedPath path, int step)> bySlot = group.ToDictionary(o => o.path.Slot, o => o);
                    List<TriggerSequence.SequenceEntry> expanded = new List<TriggerSequence.SequenceEntry>();
                    for (int i = 0; i < sequence.Count; i++)
                    {
                        if (!bySlot.TryGetValue(i, out var entry))
                        {
                            expanded.Add(sequence[i]);
                            continue;
                        }
                        ShortGuid[] prefix = entry.path.Path.Take(entry.path.Steps[entry.step].Index).ToArray();
                        foreach (Entity entity in _plan._contents)
                        {
                            if (!(entity is FunctionEntity)) continue;
                            expanded.Add(new TriggerSequence.SequenceEntry()
                            {
                                timing = sequence[i].timing,
                                connectedEntity = new EntityPath() { path = RefactorContext.Terminated(prefix.Concat(new[] { _copies[entity].shortGUID })) },
                            });
                        }
                    }
                    if (owner is TriggerSequence triggerSequence) triggerSequence.sequence = expanded;
                    else ((ProxyEntity)owner).sequence = expanded;
                }
            }
            #endregion

            #region Overrides above P
            /// <summary>
            /// Aliases above P that override I, or one of its pins, have nothing to override once I is gone.
            /// What they did is spread onto the copies at the same level, as aliases on each (reusing any
            /// alias already there with that path), and the overriding aliases are removed.
            /// </summary>
            private void SpreadOverrides(OverrideLevel level)
            {
                Composite Q = level.Holder;
                Dictionary<Entity, AliasEntity> standIns = new Dictionary<Entity, AliasEntity>();
                Entity StandIn(Entity parentEntity)
                {
                    parentEntity = Merged(parentEntity);
                    if (standIns.TryGetValue(parentEntity, out AliasEntity existing))
                        return existing;
                    ShortGuid[] path = RefactorContext.Terminated(level.Prefix.Concat(new[] { parentEntity.shortGUID }));
                    string key = PathKey(Q, path);
                    AliasEntity alias = Q.aliases_dictionary.Values.FirstOrDefault(o => PathKey(Q, o.alias.path) == key);
                    if (alias == null)
                    {
                        alias = new AliasEntity(ShortGuidUtils.GenerateRandom()) { alias = new EntityPath() { path = path } };
                        AddToComposite(Q, alias);
                    }
                    else
                        _tx.Touch(alias);
                    standIns.Add(parentEntity, alias);
                    return alias;
                }

                //Positions: each placed copy, where this level's override on I puts it
                cTransform placement = null;
                foreach (AliasEntity alias in level.OnInstance)
                    placement = RefactorContext.TransformOf(alias) ?? placement;
                if (placement != null)
                {
                    foreach (Entity entity in _plan._contents)
                    {
                        if (!(entity is FunctionEntity) || !_ctx.IsSpatial(entity, C)) continue;
                        //An alias at this level on [.., I, x] (now rewritten) already says where x goes within C
                        cTransform local = null;
                        if (_plan._aliasesByPath.TryGetValue(PathKey(Q, level.Prefix.Concat(new[] { I.shortGUID, entity.shortGUID })), out AliasEntity onChild))
                            local = _originalLocalPositions.TryGetValue(onChild, out cTransform original) ? original : RefactorContext.TransformOf(onChild);
                        local = local ?? LocalPosition(entity);
                        Entity standIn = StandIn(_copies[entity]);
                        _tx.Touch(standIn);
                        RefactorContext.SetParameter(standIn, ShortGuids.position, RefactorContext.Compose(placement, local));
                    }
                }

                //Flags, as at P
                PushDownFlags(Q, level.OnInstance, StandIn);

                //Pins: values and records these aliases add, joined with every record at the pin in P
                foreach (VariableEntity pin in _plan._pinsByName.Values)
                {
                    List<AliasEntity> overriders = level.OnInstance.Concat(level.OnPins.Where(o => o.pin == pin).Select(o => o.alias)).ToList();
                    List<(AliasEntity alias, EntityConnector link)> startOwners = new List<(AliasEntity, EntityConnector)>();
                    ParameterData value = null;
                    foreach (AliasEntity alias in overriders)
                    {
                        value = alias.GetParameter(pin.name)?.content ?? value;
                        foreach (EntityConnector link in alias.childLinks)
                            if (link.thisParamID == pin.name && Q.GetEntityByID(link.linkedEntityID) != null && !overriders.Any(o => o.shortGUID == link.linkedEntityID))
                                startOwners.Add((alias, link));
                    }

                    if (startOwners.Count != 0 && _endings.TryGetValue(pin, out var endings))
                    {
                        foreach ((Entity owner, ShortGuid param) in endings)
                            foreach ((AliasEntity alias, EntityConnector link) in startOwners)
                                AddLink(Q, StandIn(owner), param, link.linkedEntityID, link.linkedParamID, new LinkKey(alias, link));
                    }
                    else if (value != null && !_plan._pinIsEvent[pin] && _staticReaders.TryGetValue(pin, out var readers))
                    {
                        foreach ((Entity owner, ShortGuid param) in readers)
                        {
                            AliasEntity standIn = (AliasEntity)StandIn(owner);
                            _tx.Touch(standIn);
                            RefactorContext.SetParameter(standIn, param, RefactorContext.CloneData(value));
                            _pinValuesWritten.Add((standIn, param));
                        }
                    }
                }

                //Links into these aliases, and their interface links
                HashSet<ShortGuid> overriding = new HashSet<ShortGuid>(level.OnInstance.Select(o => o.shortGUID).Concat(level.OnPins.Select(o => o.alias.shortGUID)));
                Dictionary<ShortGuid, VariableEntity> pinAliases = level.OnPins.ToDictionary(o => o.alias.shortGUID, o => o.pin);
                foreach (Entity owner in Q.GetEntities())
                {
                    if (overriding.Contains(owner.shortGUID) || !owner.childLinks.Any(o => overriding.Contains(o.linkedEntityID)))
                        continue;
                    _tx.Touch(owner);
                    List<EntityConnector> original = owner.childLinks;
                    List<EntityConnector> links = new List<EntityConnector>();
                    for (int i = 0; i < original.Count; i++)
                    {
                        EntityConnector link = original[i];
                        if (!overriding.Contains(link.linkedEntityID))
                        {
                            links.Add(link);
                            continue;
                        }
                        LinkKey from = new LinkKey(owner, link);
                        if (pinAliases.TryGetValue(link.linkedEntityID, out VariableEntity aliasedPin))
                        {
                            if (link.linkedParamID == aliasedPin.name)
                                Put(Q, owner, original, i, links, SpliceAbove(Q, owner, link.thisParamID, aliasedPin, from, StandIn, level));
                            continue;
                        }
                        Put(Q, owner, original, i, links, ReplaceLinkIntoInstance(Q, owner, link, from, StandIn, level));
                    }
                    owner.childLinks = links;
                }
                foreach (AliasEntity alias in level.OnInstance)
                    MoveInterfaceLinks(Q, alias, StandIn);

                foreach (AliasEntity alias in level.OnInstance) RemoveFromComposite(Q, alias);
                foreach ((AliasEntity alias, VariableEntity _) in level.OnPins) RemoveFromComposite(Q, alias);
            }

            //Positions aliases above P held before their paths lost the I step (within C, rather than P)
            private readonly Dictionary<AliasEntity, cTransform> _originalLocalPositions = new Dictionary<AliasEntity, cTransform>();
            //Aliases in P whose overrides were written onto a copy, and that copy
            private readonly Dictionary<Entity, Entity> _mergedInto = new Dictionary<Entity, Entity>();
            private Entity Merged(Entity entity) => entity != null && _mergedInto.TryGetValue(entity, out Entity into) ? into : entity;
            //Records replaced by the value they read, where that value is what the owner ends up with
            private readonly HashSet<(Composite holder, Entity owner, ShortGuid param)> _linkBecameValue = new HashSet<(Composite, Entity, ShortGuid)>();
            //Pin values written onto stand-in aliases above P, which are meant to be there
            private readonly HashSet<(AliasEntity, ShortGuid)> _pinValuesWritten = new HashSet<(AliasEntity, ShortGuid)>();

            /// <summary>
            /// A record in a level above P that ends at a pin (through an alias there on I or on the pin):
            /// joined with everything the pin leads to at P (as aliases on those entities at this level), then
            /// with the records this level's aliases add. With nothing at all, the value that reaches it.
            /// </summary>
            private Replacement SpliceAbove(Composite holder, Entity owner, ShortGuid param, VariableEntity pin, LinkKey from, Func<Entity, Entity> standIn, OverrideLevel level)
            {
                Replacement replacement = new Replacement();
                List<Source> sources = Starts(pin, replacement.Pins);
                foreach (Source source in sources)
                {
                    Entity target = standIn(source.Target);
                    replacement.Links.Add(new EntityConnector(target.shortGUID, param, source.Param));
                    Hint(holder, new LinkKey(owner.shortGUID, param, target.shortGUID, source.Param), from);
                }
                ParameterData value = null;
                if (level != null)
                {
                    foreach (AliasEntity alias in level.OnInstance.Concat(level.OnPins.Where(o => o.pin == pin).Select(o => o.alias)))
                    {
                        value = alias.GetParameter(pin.name)?.content ?? value;
                        foreach (EntityConnector link in alias.childLinks)
                        {
                            if (link.thisParamID != pin.name || holder.GetEntityByID(link.linkedEntityID) == null) continue;
                            replacement.Links.Add(new EntityConnector(link.linkedEntityID, param, link.linkedParamID));
                            Hint(holder, new LinkKey(owner.shortGUID, param, link.linkedEntityID, link.linkedParamID), from);
                        }
                    }
                }
                if (replacement.Links.Count == 0 && !_plan._pinIsEvent[pin])
                {
                    replacement.IsValue = true;
                    replacement.Value = value != null ? RefactorContext.CloneData(value) : StaticValue(pin);
                }
                return replacement;
            }
            #endregion

            #region Composite membership
            private void AddToComposite(Composite composite, Entity entity)
            {
                _tx.Touch(composite);
                switch (entity)
                {
                    case VariableEntity variable: composite.AddVariable(variable); break;
                    case FunctionEntity function: composite.AddFunction(function); break;
                    case AliasEntity alias: composite.AddAlias(alias); break;
                    case ProxyEntity proxy: composite.AddProxy(proxy); break;
                }
            }

            private void RemoveFromComposite(Composite composite, Entity entity)
            {
                _tx.Touch(composite);
                composite.RemoveEntity(entity);
                if (composite == P)
                    _removedFromParent.Add(entity);
                if (!_removedNodes.TryGetValue(composite, out HashSet<ShortGuid> removed))
                    _removedNodes.Add(composite, removed = new HashSet<ShortGuid>());
                removed.Add(entity.shortGUID);
            }

            private void RemoveFromParent(Entity entity)
            {
                if (P.GetEntityByID(entity.shortGUID) == entity)
                    RemoveFromComposite(P, entity);
            }
            #endregion

            #region Pages
            private void BuildPages()
            {
                if (_pageSource == null)
                    return;

                //P: its own pages, plus C's pages (for its copies), then drawn to match P's links
                if (_pageSource.PagesCarryLinks(P))
                {
                    PageRewriter pages = new PageRewriter(P, _pageSource.GetPages(P));
                    List<FlowgraphMeta> contentPages = _pageSource.PagesCarryLinks(C) ? _pageSource.GetPages(C) : new List<FlowgraphMeta>();
                    string name = _ctx.NameOf(P, I);
                    HashSet<ShortGuid> pins = new HashSet<ShortGuid>(C.variables_dictionary.Keys);
                    List<FlowgraphMeta> imported = new List<FlowgraphMeta>();
                    foreach (FlowgraphMeta page in contentPages)
                    {
                        FlowgraphMeta added = pages.AddPage(page, contentPages.Count == 1 ? name : name + " - " + page.Name);
                        //Only C's entities are renamed here: a pin's id is left alone so the records into it can be found
                        Dictionary<ShortGuid, ShortGuid> remap = new Dictionary<ShortGuid, ShortGuid>();
                        foreach (KeyValuePair<ShortGuid, ShortGuid> pair in _idMap)
                            if (!pins.Contains(pair.Key)) remap[pair.Key] = pair.Value;
                        pages.RemapEntities(remap, added);
                        imported.Add(added);
                    }
                    pages.RemapEntities(_parentNodeRemap);
                    pages.IndexDrawnConnections();
                    HashSet<ShortGuid> removed = _removedNodes.TryGetValue(P, out HashSet<ShortGuid> r) ? new HashSet<ShortGuid>(r) : new HashSet<ShortGuid>();
                    removed.ExceptWith(_parentNodeRemap.Values);
                    pages.RemoveNodes(removed);
                    RemovePinNodes(pages, imported, pins);
                    pages.Reconcile(RemappedHints(P), name);
                    _result.Pages[P] = pages.Pages;
                }

                //Levels above: aliases were added, removed and relinked
                foreach (KeyValuePair<Composite, List<PageHint>> entry in _hints)
                {
                    if (entry.Key == P || !_pageSource.PagesCarryLinks(entry.Key)) continue;
                    AboveLevelPages(entry.Key);
                }
                foreach (Composite composite in _removedNodes.Keys)
                    if (composite != P && !_result.Pages.ContainsKey(composite) && _pageSource.PagesCarryLinks(composite))
                        AboveLevelPages(composite);
                foreach (OverrideLevel level in _plan._levels.Values)
                    if (!level.IsParent && !_result.Pages.ContainsKey(level.Holder) && _pageSource.PagesCarryLinks(level.Holder))
                        AboveLevelPages(level.Holder);
            }

            private void AboveLevelPages(Composite composite)
            {
                PageRewriter pages = new PageRewriter(composite, _pageSource.GetPages(composite));
                pages.IndexDrawnConnections();
                if (_removedNodes.TryGetValue(composite, out HashSet<ShortGuid> removed))
                    pages.RemoveNodes(removed);
                pages.Reconcile(_hints.TryGetValue(composite, out List<PageHint> hints) ? hints : new List<PageHint>(), RefactorContext.LeafName(composite));
                _result.Pages[composite] = pages.Pages;
            }

            /// <summary>C's pin nodes are not copied: they go from the imported pages only (a P entity could share an id with one).</summary>
            private static void RemovePinNodes(PageRewriter pages, List<FlowgraphMeta> imported, HashSet<ShortGuid> pins)
            {
                foreach (FlowgraphMeta page in imported)
                {
                    HashSet<int> removed = new HashSet<int>(page.Nodes.Where(o => pins.Contains(o.EntityGUID)).Select(o => o.NodeID));
                    page.Nodes.RemoveAll(o => removed.Contains(o.NodeID));
                    foreach (FlowgraphMeta.NodeMeta node in page.Nodes)
                        node.ConnectionsOut.RemoveAll(o => removed.Contains(o.ConnectedNodeID));
                }
            }

            /// <summary>P's hints with merged aliases named as what they merged into, matching the pages and links.</summary>
            private List<PageHint> RemappedHints(Composite composite)
            {
                List<PageHint> hints = _hints.TryGetValue(composite, out List<PageHint> h) ? h : new List<PageHint>();
                ShortGuid Map(ShortGuid id) => _parentNodeRemap.TryGetValue(id, out ShortGuid to) ? to : id;
                return hints.Select(o => new PageHint()
                {
                    Link = new LinkKey(Map(o.Link.Owner), o.Link.Param, Map(o.Link.Target), o.Link.TargetParam),
                    From = new LinkKey(Map(o.From.Owner), o.From.Param, Map(o.From.Target), o.From.TargetParam),
                }).ToList();
            }
            #endregion
        }
        #endregion
    }
}
#endif
