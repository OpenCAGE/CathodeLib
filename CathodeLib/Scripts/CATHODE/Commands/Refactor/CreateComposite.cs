#if !GODOT
using CATHODE.Enums;
using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static CathodeLib.CompositeFlowgraphTable;

namespace CATHODE.Scripting.Refactor
{
    /// <summary>
    /// Moving a selection of entities out of a composite into a new composite, placed where they were.
    /// </summary>
    /// <remarks>
    /// The selected entities S of P move, as they are (same objects, same ids), into a new composite N,
    /// and an instance In of N takes their place in P at the origin - so every position, being relative
    /// to its composite, stays where it was.
    ///
    /// <para>Links only join entities of one composite, so every link between S and what stays behind is
    /// routed through a pin of N: the part inside N runs to the pin, the part in P runs from In's pin,
    /// and joined back up (the relay rule De-instance undoes) they make the original link. Which kind of
    /// pin follows from which end owned the link and which side of a node it is drawn on, so the script
    /// pages can draw both halves. Stored paths anywhere that reached into S through P gain the In step.</para>
    /// </remarks>
    public sealed class CreateCompositePlan
    {
        public Composite Parent { get; }
        public IReadOnlyList<Entity> Selection { get; }
        public string Name { get; }
        public List<RefactorIssue> Issues { get; } = new List<RefactorIssue>();
        public bool CanApply => !Issues.Any(o => o.Blocking);

        private readonly RefactorContext _ctx;
        private readonly HashSet<Entity> _moving;
        private readonly HashSet<ShortGuid> _movingIds;
        private readonly List<(ResolvedPath path, int step)> _rewrites = new List<(ResolvedPath, int)>();
        private readonly Dictionary<ShortGuid, ParameterData> _instanceFlags = new Dictionary<ShortGuid, ParameterData>();
        private readonly HashSet<string> _notes = new HashSet<string>();

        private CreateCompositePlan(RefactorContext ctx, Composite parent, List<Entity> selection, string name)
        {
            _ctx = ctx;
            Parent = parent;
            Name = (name ?? "").Trim().Replace('/', '\\');
            //Keep the parent's order: it is the order instancing walks, and the new composite gets the same
            HashSet<Entity> chosen = new HashSet<Entity>(selection.Where(o => o != null));
            Selection = parent.GetEntities().Where(o => chosen.Contains(o)).ToList();
            _moving = new HashSet<Entity>(Selection);
            _movingIds = new HashSet<ShortGuid>(Selection.Select(o => o.shortGUID));
            foreach (Entity entity in chosen)
                if (!_moving.Contains(entity))
                    Block("'" + _ctx.NameOf(parent, entity) + "' is not in " + RefactorContext.LeafName(parent) + ".");
        }

        /// <summary>Work out what moving <paramref name="selection"/> out of <paramref name="parent"/> into a new composite called <paramref name="name"/> would involve. Nothing is changed.</summary>
        public static CreateCompositePlan Plan(Commands commands, Composite parent, IEnumerable<Entity> selection, string name)
        {
            CreateCompositePlan plan = new CreateCompositePlan(new RefactorContext(commands), parent, selection?.ToList() ?? new List<Entity>(), name);
            plan.Analyse();
            return plan;
        }

        private void Block(string message) => Issues.Add(new RefactorIssue() { Blocking = true, Message = message });
        private void Note(string message)
        {
            if (_notes.Add(message))
                Issues.Add(new RefactorIssue() { Blocking = false, Message = message });
        }

        /// <summary>
        /// Why a composite name cannot be used, or null if it can. Instancing hides everything under a
        /// composite whose path says REQUIRED_ASSETS, TEMPLATE or PHYSICS, so those are refused too.
        /// </summary>
        public static string CheckName(Commands commands, string name)
        {
            string path = (name ?? "").Trim().Replace('/', '\\');
            if (path.Length == 0)
                return "Give the new composite a name.";
            if (path.EndsWith("\\"))
                return "The name ends in a folder separator; give the composite itself a name.";
            if (path.Split('\\').Any(o => o.Trim().Length == 0))
                return "The name has an empty folder in it.";
            string upper = path.ToUpperInvariant();
            if (upper.Contains("REQUIRED_ASSETS") || upper.Contains("TEMPLATE") || upper.Contains("\\PHYSICS\\") || upper.StartsWith("PHYSICS\\"))
                return "Composites named with REQUIRED_ASSETS, TEMPLATE or a PHYSICS folder are never shown in the level; pick another name.";
            if (commands.Entries.Any(o => o != null && string.Equals((o.name ?? "").Replace('/', '\\'), path, StringComparison.OrdinalIgnoreCase)))
                return "A composite called '" + path + "' already exists.";
            return null;
        }

        #region Analysis
        private void Analyse()
        {
            if (Parent == null)
            {
                Block("There is no composite being edited.");
                return;
            }
            if (Selection.Count == 0)
            {
                Block("Select the entities to put in the new composite.");
                return;
            }
            string nameProblem = CheckName(_ctx.Commands, Name);
            if (nameProblem != null)
                Block(nameProblem);

            foreach (Entity entity in Selection)
            {
                string what = "'" + _ctx.NameOf(Parent, entity) + "'";
                switch (entity)
                {
                    case VariableEntity _:
                        Block(what + " is one of " + RefactorContext.LeafName(Parent) + "'s own parameters, which stay with it.");
                        break;
                    case FunctionEntity function when function.function == FunctionType.PhysicsSystem:
                        Block(what + " is a PhysicsSystem, which belongs to the instance that places its composite.");
                        break;
                    case FunctionEntity function when function.function == FunctionType.EnvironmentModelReference:
                        Block(what + " is an EnvironmentModelReference, whose animation data is tied to the composite it is in.");
                        break;
                }
            }
            if (Issues.Any(o => o.Blocking))
                return;

            AnalyseMovingPaths();
            AnalysePathsInto();
            AnalyseAnimationKeys();
            AnalyseLinks();
            AnalyseParentPlacements();
        }

        /// <summary>Paths held by moving entities, read from P: they can only reach what moves with them.</summary>
        private void AnalyseMovingPaths()
        {
            foreach (Entity entity in Selection)
            {
                List<ShortGuid[]> paths = new List<ShortGuid[]>();
                switch (entity)
                {
                    case AliasEntity alias: paths.Add(alias.alias?.path); break;
                    case TriggerSequence trigger: paths.AddRange(trigger.sequence.Select(o => o.connectedEntity?.path)); break;
                    case CAGEAnimation animation: paths.AddRange(animation.connections.Select(o => o.connectedEntity?.path)); break;
                    case ProxyEntity proxy: paths.AddRange(proxy.sequence.Select(o => o.connectedEntity?.path)); break;
                }
                foreach (ShortGuid[] path in paths)
                {
                    ResolvedPath resolved = _ctx.Resolve(path, Parent, false);
                    if (resolved?.Reading != PathReading.Local)
                        continue;
                    Entity first = resolved.Steps[0].Entity;
                    if (!_moving.Contains(first))
                        Block("'" + _ctx.NameOf(Parent, entity) + "' points at '" + _ctx.NameOf(Parent, first) + "', which is not selected: select it too, or leave '" + _ctx.NameOf(Parent, entity) + "' where it is.");
                }
            }
        }

        /// <summary>Paths anywhere that reach into the selection through P gain the step through the new instance.</summary>
        private void AnalysePathsInto()
        {
            foreach (ResolvedPath path in _ctx.FindPaths(_movingIds))
            {
                int step = path.Steps.FindIndex(o => o.Composite == Parent && _moving.Contains(o.Entity));
                if (step < 0)
                    continue;
                //A moving entity's own path read from P moves with it and still reads the same from N
                if (_moving.Contains(path.Owner) && path.Holder == Parent && path.Reading == PathReading.Local && step == 0)
                    continue;
                _rewrites.Add((path, step));
                if (path.Kind == PathKind.TriggerSequence && path.Holder == Parent && path.Reading == PathReading.Local)
                {
                    foreach (Entity owner in Parent.GetEntities())
                        foreach (EntityConnector link in owner.childLinks)
                            if (link.linkedEntityID == path.Owner.shortGUID && link.linkedParamID == ShortGuids.reference && IsZoneLike(owner))
                                Note("The zone or environment map '" + _ctx.NameOf(Parent, owner) + "' lists moved entities through a trigger sequence; they are still listed individually, one step deeper.");
                }
            }
        }

        /// <summary>Animation events keyed to an entity id name it within the animation's own composite.</summary>
        private void AnalyseAnimationKeys()
        {
            foreach (FunctionEntity function in Parent.functions_dictionary.Values)
            {
                if (!(function is CAGEAnimation animation)) continue;
                bool moving = _moving.Contains(animation);
                foreach (CAGEAnimation.EventTrack track in animation.eventTracks)
                    foreach (CAGEAnimation.EventTrack.Keyframe key in track.keyframes)
                    {
                        if (key.track_type != ANIM_TRACK_TYPE.T_GUID) continue;
                        Entity keyed = Parent.GetEntityByID(key.forward);
                        if (keyed == null) continue;
                        if (moving != _moving.Contains(keyed))
                            Block("The animation '" + _ctx.NameOf(Parent, animation) + "' has an event on '" + _ctx.NameOf(Parent, keyed) + "', and only one of them is selected.");
                    }
            }
        }

        private void AnalyseLinks()
        {
            foreach (Entity owner in Parent.GetEntities())
            {
                bool inside = _moving.Contains(owner);
                foreach (EntityConnector link in owner.childLinks)
                {
                    Entity target = Parent.GetEntityByID(link.linkedEntityID);
                    if (target == null || inside == _moving.Contains(target)) continue;
                    if (!inside && IsZoneLike(owner))
                        Note("'" + _ctx.NameOf(Parent, owner) + "' lists '" + _ctx.NameOf(Parent, target) + "', which will be reached through the new instance: zones and environment maps apply to all of an instance, so it may cover the rest of the new composite too.");
                }
            }
        }

        private bool IsZoneLike(Entity entity)
        {
            return entity is FunctionEntity function && (function.function == FunctionType.Zone || function.function == FunctionType.EnvironmentMap);
        }

        /// <summary>
        /// Planar reflections, material mapping and disable_collision are read from an entity's direct parent
        /// instance. The selection reads them from P's placements now, and from the new instance afterwards:
        /// where every placement of P agrees, the new instance is given the same value, otherwise it is noted.
        /// </summary>
        private void AnalyseParentPlacements()
        {
            if (!Selection.Any(o => o is FunctionEntity && _ctx.InstancedComposite(o) == null))
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
            List<Entity> aliases = new List<Entity>();
            foreach (ResolvedPath path in _ctx.FindPaths(ids))
            {
                if (path.Kind != PathKind.Alias) continue;
                PathStep last = path.Steps[path.Steps.Count - 1];
                if (placements.Contains((last.Composite, last.Entity)))
                    aliases.Add(path.Owner);
            }

            foreach (ShortGuid flag in new[] { ShortGuids.include_in_planar_reflections, ShortGuids.mapping, ShortGuids.disable_collision })
            {
                List<Entity> setters = placements.Select(o => o.Item2).Concat(aliases).ToList();
                bool linked = setters.Any(o => o.childLinks.Any(l => l.thisParamID == flag));
                List<string> values = placements.Select(o => Describe(o.Item2.GetParameter(flag)?.content)).Distinct().ToList();
                bool aliasSets = aliases.Any(o => o.GetParameter(flag) != null);
                string current = values.Count == 1 ? values[0] : null;
                if (current == "" && !linked && !aliasSets)
                    continue;
                if (current != null && current != "" && !linked && !aliasSets)
                {
                    _instanceFlags[flag] = RefactorContext.CloneData(placements[0].Item2.GetParameter(flag).content);
                    continue;
                }
                Note(RefactorContext.LeafName(Parent) + " is placed with differing '" + RefactorContext.ParamName(flag) + "' (or it is linked): the new composite's contents will read it from the new instance instead.");
            }
        }

        private static string Describe(ParameterData data)
        {
            switch (data)
            {
                case null: return "";
                case cBool b: return b.value ? "true" : "";
                case cResource r: return r.shortGUID == ShortGuid.Invalid ? "" : "r" + r.shortGUID.AsUInt32;
                default: return data.ToString();
            }
        }
        #endregion

        #region Apply
        /// <summary>Carry out the refactor. Throws if the plan has a blocking issue. <paramref name="pages"/> may be null.</summary>
        public RefactorResult Apply(IRefactorPageSource pages)
        {
            if (!CanApply)
                throw new InvalidOperationException("The composite cannot be created: " + string.Join(" ", Issues.Where(o => o.Blocking).Select(o => o.Message)));
            return new Applier(this, pages).Run();
        }

        private sealed class Applier
        {
            private readonly CreateCompositePlan _plan;
            private readonly RefactorContext _ctx;
            private readonly IRefactorPageSource _pageSource;
            private readonly ScriptTransaction _tx;
            private readonly Composite P;
            private Composite N;
            private FunctionEntity In;
            private readonly RefactorResult _result = new RefactorResult();
            private readonly List<PageHint> _parentHints = new List<PageHint>();
            private readonly List<PageHint> _newHints = new List<PageHint>();
            private readonly HashSet<string> _pinNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public Applier(CreateCompositePlan plan, IRefactorPageSource pages)
            {
                _plan = plan;
                _ctx = plan._ctx;
                _pageSource = pages;
                _tx = new ScriptTransaction(_ctx.Commands);
                P = plan.Parent;
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
                //Pages first, while P still holds everything they draw
                List<FlowgraphMeta> parentPages = _pageSource != null && _pageSource.PagesCarryLinks(P) ? _pageSource.GetPages(P) : null;
                List<FlowgraphMeta> sourcePages = _pageSource != null ? _pageSource.GetPages(P) : new List<FlowgraphMeta>();

                _tx.TouchEntries();
                _tx.Touch(P);
                N = new Composite(_plan.Name);
                _ctx.Commands.Entries.Add(N);
                _tx.Touch(N);
                _tx.TouchPins(N);

                HashSet<ShortGuid> taken = new HashSet<ShortGuid>(P.GetEntities().Select(o => o.shortGUID));
                ShortGuid instanceId = ShortGuidUtils.GenerateRandom();
                while (taken.Contains(instanceId)) instanceId = ShortGuidUtils.GenerateRandom();
                In = new FunctionEntity(instanceId, N);
                _ctx.Utils.SetEntityName(In, UniqueEntityName(RefactorContext.LeafName(N)));
                In.parameters.Add(new Parameter(ShortGuids.position, new cTransform(), ParameterVariant.PARAMETER));
                foreach (KeyValuePair<ShortGuid, ParameterData> flag in _plan._instanceFlags)
                    In.parameters.Add(new Parameter(flag.Key, flag.Value, ParameterVariant.PARAMETER));

                //Links are rewired before anything moves: which side of the boundary an end is on is still P's to say
                RewireLinks();
                RewritePaths();

                foreach (Entity entity in _plan.Selection)
                {
                    P.RemoveEntity(entity);
                    AddTo(N, entity);
                }
                P.AddFunction(In);

                BuildPages(parentPages, sourcePages);

                _tx.Commit();
                _result.Transaction = _tx;
                _result.CreatedComposite = N;
                _result.CreatedInstance = In;
                _result.Issues.AddRange(_plan.Issues);
                return _result;
            }

            private static void AddTo(Composite composite, Entity entity)
            {
                switch (entity)
                {
                    case VariableEntity variable: composite.AddVariable(variable); break;
                    case FunctionEntity function: composite.AddFunction(function); break;
                    case AliasEntity alias: composite.AddAlias(alias); break;
                    case ProxyEntity proxy: composite.AddProxy(proxy); break;
                }
            }

            private string UniqueEntityName(string baseName)
            {
                HashSet<string> names = new HashSet<string>(P.GetEntities().Select(o => _ctx.Utils.GetEntityName(P, o) ?? ""), StringComparer.OrdinalIgnoreCase);
                for (int i = 1; ; i++)
                {
                    string candidate = baseName + "_" + i;
                    if (!names.Contains(candidate))
                        return candidate;
                }
            }

            #region Links
            /// <summary>
            /// Every link that crosses the boundary becomes two, joined by a pin of N.
            /// <list type="bullet">
            /// <item>Owned inside (X.a to T.t outside): X.a to the pin inside N, and In's pin to T.t in P. Consecutive
            /// links from one parameter share a pin, so the order they are read in (the last one wins) is kept.</item>
            /// <item>Owned outside (O.q to X.x inside): O.q to In's pin in P, and the pin to X.x inside N. Every
            /// outside link to the same X.x shares a pin.</item>
            /// </list>
            /// </summary>
            private void RewireLinks()
            {
                //Owned inside
                foreach (Entity owner in _plan.Selection)
                {
                    if (!owner.childLinks.Any(o => Outside(o.linkedEntityID)))
                        continue;
                    _tx.Touch(owner);
                    List<EntityConnector> links = new List<EntityConnector>();
                    VariableEntity run = null;
                    ShortGuid runParam = ShortGuid.Invalid;
                    foreach (EntityConnector link in owner.childLinks)
                    {
                        if (!Outside(link.linkedEntityID))
                        {
                            links.Add(link);
                            run = null;
                            continue;
                        }
                        Entity target = P.GetEntityByID(link.linkedEntityID);
                        if (run == null || runParam != link.thisParamID)
                        {
                            run = NewPin(owner, link.thisParamID, target, link.linkedParamID, ownedInside: true);
                            runParam = link.thisParamID;
                            links.Add(new EntityConnector(run.shortGUID, link.thisParamID, run.name));
                            _newHints.Add(new PageHint() { Link = new LinkKey(owner.shortGUID, link.thisParamID, run.shortGUID, run.name), From = new LinkKey(owner, link) });
                        }
                        In.childLinks.Add(new EntityConnector(link.linkedEntityID, run.name, link.linkedParamID));
                        _parentHints.Add(new PageHint() { Link = new LinkKey(In.shortGUID, run.name, link.linkedEntityID, link.linkedParamID), From = new LinkKey(owner, link) });
                    }
                    owner.childLinks = links;
                }

                //Owned outside
                Dictionary<(ShortGuid, ShortGuid), VariableEntity> inward = new Dictionary<(ShortGuid, ShortGuid), VariableEntity>();
                foreach (Entity owner in P.GetEntities())
                {
                    if (_plan._moving.Contains(owner) || !owner.childLinks.Any(o => _plan._movingIds.Contains(o.linkedEntityID)))
                        continue;
                    _tx.Touch(owner);
                    List<EntityConnector> links = new List<EntityConnector>();
                    foreach (EntityConnector link in owner.childLinks)
                    {
                        if (!_plan._movingIds.Contains(link.linkedEntityID))
                        {
                            links.Add(link);
                            continue;
                        }
                        Entity target = P.GetEntityByID(link.linkedEntityID);
                        if (!inward.TryGetValue((link.linkedEntityID, link.linkedParamID), out VariableEntity pin))
                        {
                            pin = NewPin(owner, link.thisParamID, target, link.linkedParamID, ownedInside: false);
                            pin.childLinks.Add(new EntityConnector(link.linkedEntityID, pin.name, link.linkedParamID));
                            _newHints.Add(new PageHint() { Link = new LinkKey(pin.shortGUID, pin.name, link.linkedEntityID, link.linkedParamID), From = new LinkKey(owner, link) });
                            inward.Add((link.linkedEntityID, link.linkedParamID), pin);
                        }
                        links.Add(new EntityConnector(In.shortGUID, link.thisParamID, pin.name));
                        _parentHints.Add(new PageHint() { Link = new LinkKey(owner.shortGUID, link.thisParamID, In.shortGUID, pin.name), From = new LinkKey(owner, link) });
                    }
                    owner.childLinks = links;
                }
            }

            private bool Outside(ShortGuid id) => !_plan._movingIds.Contains(id) && P.GetEntityByID(id) != null;

            /// <summary>
            /// A pin of N for one side of a crossing link. Its kind is chosen so both halves can be drawn: a
            /// link's owner is the node side it leaves from (right, or top), so inside-owned links need a pin
            /// the instance drives out (a target pin for an event, an input or output pin for data) and
            /// outside-owned ones a pin the instance takes in (a method pin for an event, a reference pin for
            /// data, which is a reference to the entity inside).
            /// </summary>
            private VariableEntity NewPin(Entity owner, ShortGuid ownerParam, Entity target, ShortGuid targetParam, bool ownedInside)
            {
                (ParameterVariant? ownerVariant, DataType? ownerType, ShortGuid _) = _ctx.Utils.GetParameterMetadata(owner, ownerParam, P);
                (ParameterVariant? targetVariant, DataType? targetType, ShortGuid _) = target == null ? (null, null, ShortGuid.Invalid) : _ctx.Utils.GetParameterMetadata(target, targetParam, P);
                bool isEvent = ownerVariant == ParameterVariant.TARGET_PIN || ownerVariant == ParameterVariant.METHOD_PIN || targetVariant == ParameterVariant.METHOD_PIN;
                DataType type = (ownedInside ? ownerType : targetType) ?? ownerType ?? targetType ?? DataType.FLOAT;

                CompositePinType kind;
                if (ownedInside)
                    kind = isEvent ? CompositePinType.CompositeTargetPin : DataPin(type, output: ownerVariant == ParameterVariant.OUTPUT_PIN);
                else
                    kind = isEvent ? CompositePinType.CompositeMethodPin : CompositePinType.CompositeReferencePin;

                Entity inside = ownedInside ? owner : target;
                ShortGuid insideParam = ownedInside ? ownerParam : targetParam;
                string name = PinName(_ctx.Utils.GetEntityName(P, inside) + "_" + RefactorContext.ParamName(insideParam));
                VariableEntity pin = new VariableEntity(ShortGuidUtils.GenerateRandom(), ShortGuidUtils.Generate(name), isEvent ? DataType.FLOAT : type);
                N.AddVariable(pin);

                CompositePinInfoTable.PinInfo info = new CompositePinInfoTable.PinInfo() { VariableGUID = pin.shortGUID, PinTypeGUID = new ShortGuid((uint)kind) };
                ParameterData current = inside.GetParameter(insideParam)?.content;
                if (current is cEnum enumValue)
                    info.PinEnumTypeGUID = enumValue.enumID;
                _ctx.Utils.SetPinInfo(N.shortGUID, info);
                if (!isEvent && kind != CompositePinType.CompositeReferencePin)
                {
                    ParameterData value = current != null ? RefactorContext.CloneData(current) : _ctx.Utils.CreateDefaultParameterData(pin, N, pin.name);
                    if (value != null)
                        pin.parameters.Add(new Parameter(pin.name, value, ParameterVariant.PARAMETER));
                }
                return pin;
            }

            private static CompositePinType DataPin(DataType type, bool output)
            {
                switch (type)
                {
                    case DataType.BOOL: return output ? CompositePinType.CompositeOutputBoolVariablePin : CompositePinType.CompositeInputBoolVariablePin;
                    case DataType.INTEGER: return output ? CompositePinType.CompositeOutputIntVariablePin : CompositePinType.CompositeInputIntVariablePin;
                    case DataType.FLOAT: return output ? CompositePinType.CompositeOutputFloatVariablePin : CompositePinType.CompositeInputFloatVariablePin;
                    case DataType.STRING: return output ? CompositePinType.CompositeOutputStringVariablePin : CompositePinType.CompositeInputStringVariablePin;
                    case DataType.TRANSFORM: return output ? CompositePinType.CompositeOutputPositionVariablePin : CompositePinType.CompositeInputPositionVariablePin;
                    case DataType.VECTOR: return output ? CompositePinType.CompositeOutputDirectionVariablePin : CompositePinType.CompositeInputDirectionVariablePin;
                    case DataType.ENUM: return output ? CompositePinType.CompositeOutputEnumVariablePin : CompositePinType.CompositeInputEnumVariablePin;
                    case DataType.ENUM_STRING: return output ? CompositePinType.CompositeOutputEnumStringVariablePin : CompositePinType.CompositeInputEnumStringVariablePin;
                    case DataType.OBJECT: return output ? CompositePinType.CompositeOutputObjectVariablePin : CompositePinType.CompositeInputObjectVariablePin;
                    case DataType.ZONE: return output ? CompositePinType.CompositeOutputZonePtrVariablePin : CompositePinType.CompositeInputZonePtrVariablePin;
                    case DataType.ZONE_LINK: return output ? CompositePinType.CompositeOutputZoneLinkPtrVariablePin : CompositePinType.CompositeInputZoneLinkPtrVariablePin;
                    case DataType.ANIMATION_INFO: return output ? CompositePinType.CompositeOutputAnimationInfoVariablePin : CompositePinType.CompositeInputAnimationInfoVariablePin;
                    default: return output ? CompositePinType.CompositeOutputVariablePin : CompositePinType.CompositeInputVariablePin;
                }
            }

            /// <summary>
            /// A pin name no other pin of N has, and that is not one of an instance's own parameters or a
            /// method with a relay (either would turn the instance's node pin into something else).
            /// </summary>
            private string PinName(string wanted)
            {
                StringBuilder sb = new StringBuilder();
                foreach (char c in wanted)
                    sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
                string baseName = sb.Length == 0 ? "pin" : sb.ToString();
                for (int i = 1; ; i++)
                {
                    string candidate = i == 1 ? baseName : baseName + "_" + i;
                    if (_pinNames.Contains(candidate)) continue;
                    ShortGuid id = ShortGuidUtils.Generate(candidate, false);
                    if (_ctx.InstanceInterface.ContainsKey(id) || _ctx.InterfaceRelayToMethod.ContainsKey(id) || _ctx.Utils.GetRelay(id) != ShortGuid.Invalid)
                        continue;
                    _pinNames.Add(candidate);
                    return candidate;
                }
            }
            #endregion

            #region Paths
            /// <summary>Stored paths that reached a moving entity through P now step through the new instance first.</summary>
            private void RewritePaths()
            {
                foreach ((ResolvedPath path, int step) in _plan._rewrites)
                {
                    int index = path.Steps[step].Index;
                    List<ShortGuid> elements = path.Path.ToList();
                    elements.Insert(index, In.shortGUID);
                    _tx.Touch(path.Owner);
                    RefactorContext.StorePath(path, elements.ToArray());
                }
            }
            #endregion

            #region Pages
            /// <summary>
            /// N's pages: each of P's pages that held moved nodes, with only those nodes, and the halves of
            /// crossing links drawn to pin nodes where the other end was. P's pages: the moved nodes gone, and
            /// the other halves drawn to a node for the new instance where the moved nodes were.
            /// </summary>
            private void BuildPages(List<FlowgraphMeta> parentPages, List<FlowgraphMeta> sourcePages)
            {
                if (_pageSource == null)
                    return;

                HashSet<ShortGuid> staying = new HashSet<ShortGuid>(P.GetEntities().Select(o => o.shortGUID));
                staying.Remove(In.shortGUID);
                List<FlowgraphMeta> withMoved = _pageSource.PagesCarryLinks(P) ? sourcePages.Where(o => o.Nodes.Any(n => _plan._movingIds.Contains(n.EntityGUID))).ToList() : new List<FlowgraphMeta>();
                PageRewriter newPages = new PageRewriter(N, withMoved);
                newPages.IndexDrawnConnections();
                newPages.RemoveNodes(staying);
                newPages.Reconcile(_newHints, RefactorContext.LeafName(N));
                _result.Pages[N] = newPages.Pages.Where(o => o.Nodes.Count != 0 || newPages.Pages.Count == 1).ToList();

                if (parentPages != null)
                {
                    PageRewriter pages = new PageRewriter(P, parentPages);
                    pages.IndexDrawnConnections();
                    pages.RemoveNodes(_plan._movingIds);
                    pages.Reconcile(_parentHints, RefactorContext.LeafName(P));
                    _result.Pages[P] = pages.Pages;
                }
            }
            #endregion
        }
        #endregion
    }
}
#endif
