#if !GODOT
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
    /// A copy of a composite, under a new name, that can stand in for it - and, optionally, some of the original's
    /// instances switched over to the copy, so it can be changed for just those.
    /// </summary>
    /// <remarks>
    /// The copy holds the same entities under the same ids, so anything that reaches into an instance by path (an
    /// alias overriding something inside it, a proxy, a trigger sequence, an animation track) reaches the same things in
    /// an instance of the copy, and an instance can be switched from one to the other with nothing else changing. Ids
    /// only have to be unique within a composite: the game tells placements apart by the path of instances to them, and
    /// a composite's resources by their id within it. The copy's resources point at the level's own renderables,
    /// collision and physics, as two placements of one composite share them; the pins keep their types; the script
    /// pages are the original's, under the copy. Its entities are in the original's order - the order instancing walks.
    /// </remarks>
    public sealed class DuplicateCompositePlan
    {
        public Composite Source { get; }
        public string Name { get; }
        /// <summary>Instances of <see cref="Source"/> to switch to the copy, each with the composite it is in.</summary>
        public IReadOnlyList<(Composite Holder, FunctionEntity Instance)> Switch { get; }
        public List<RefactorIssue> Issues { get; } = new List<RefactorIssue>();
        public bool CanApply => !Issues.Any(o => o.Blocking);

        private readonly RefactorContext _ctx;

        private DuplicateCompositePlan(RefactorContext ctx, Composite source, string name, List<(Composite, FunctionEntity)> switching)
        {
            _ctx = ctx;
            Source = source;
            Name = (name ?? "").Trim().Replace('/', '\\');
            Switch = switching;
        }

        /// <summary>Work out what duplicating <paramref name="source"/> as <paramref name="name"/> (and switching <paramref name="switchInstances"/> to the copy) involves. Nothing is changed.</summary>
        public static DuplicateCompositePlan Plan(Commands commands, Composite source, string name, IEnumerable<(Composite Holder, FunctionEntity Instance)> switchInstances = null)
        {
            DuplicateCompositePlan plan = new DuplicateCompositePlan(new RefactorContext(commands), source, name, switchInstances?.Distinct().ToList() ?? new List<(Composite, FunctionEntity)>());
            plan.Analyse();
            return plan;
        }

        /// <summary>
        /// A free name for a copy of <paramref name="source"/>: beside it, with "_Copy" (then "_Copy_2" and on) after its
        /// name - or, under a folder no composite may be named in (REQUIRED_ASSETS, TEMPLATE, PHYSICS), its name alone.
        /// </summary>
        public static string DefaultName(Commands commands, Composite source)
        {
            string path = (source?.name ?? "").Replace('/', '\\').Trim('\\');
            string leaf = RefactorContext.LeafName(new Composite() { name = path });
            if (string.IsNullOrEmpty(leaf))
                leaf = "Composite";
            foreach (string stem in new[] { path + "_Copy", leaf + "_Copy" })
            {
                for (int i = 1; i <= 10000; i++)
                {
                    string candidate = i == 1 ? stem : stem + "_" + i;
                    if (CreateCompositePlan.CheckName(commands, candidate) == null)
                        return candidate;
                    if (!commands.Entries.Any(o => o != null && string.Equals((o.name ?? "").Replace('/', '\\'), candidate, StringComparison.OrdinalIgnoreCase)))
                        break; //refused for its folder, not because it is taken: try the plainer stem
                }
            }
            return leaf + "_Copy_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private void Block(string message) => Issues.Add(new RefactorIssue() { Blocking = true, Message = message });
        private void Note(string message) => Issues.Add(new RefactorIssue() { Blocking = false, Message = message });

        private void Analyse()
        {
            if (Source == null || !_ctx.Commands.Entries.Contains(Source))
            {
                Block("There is no composite to duplicate.");
                return;
            }
            string problem = CreateCompositePlan.CheckName(_ctx.Commands, Name);
            if (problem != null)
                Block(problem);

            foreach ((Composite holder, FunctionEntity instance) in Switch)
            {
                if (holder == null || instance == null || !_ctx.Commands.Entries.Contains(holder) || holder.GetEntityByID(instance.shortGUID) != instance)
                    Block("An instance to switch to the copy is not in the composite given for it.");
                else if (instance.function != Source.shortGUID)
                    Block("'" + _ctx.NameOf(holder, instance) + "' in " + RefactorContext.LeafName(holder) + " is not an instance of " + RefactorContext.LeafName(Source) + ".");
            }

            //Proxies hold a path from the level's root: the copy's lead where the original's do, not into the copy's placements
            if (Source.proxies_dictionary.Count != 0)
                Note(RefactorContext.LeafName(Source) + " has " + Source.proxies_dictionary.Count + " prox" + (Source.proxies_dictionary.Count == 1 ? "y" : "ies") + ": the copy's point at the same entities the original's do.");
        }

        /// <summary>
        /// Make the copy and switch the instances, as one committed transaction; the copy's pages are the original's
        /// (as <paramref name="pages"/> has them). Throws, with nothing changed, if the plan cannot be applied.
        /// </summary>
        public RefactorResult Apply(IRefactorPageSource pages)
        {
            if (!CanApply)
                throw new InvalidOperationException(string.Join(" ", Issues.Where(o => o.Blocking).Select(o => o.Message)));

            ScriptTransaction tx = new ScriptTransaction(_ctx.Commands);
            try
            {
                tx.TouchEntries();
                Composite copy = new Composite(Name);
                while (_ctx.GetComposite(copy.shortGUID) != null || copy.shortGUID == Source.shortGUID)
                    copy.shortGUID = ShortGuidUtils.GenerateRandom();
                _ctx.Commands.Entries.Add(copy);
                tx.Touch(copy);
                tx.TouchPins(copy);

                HashSet<ShortGuid> resourceIds = new HashSet<ShortGuid>();
                foreach (VariableEntity entity in Source.variables_dictionary.Values)
                    copy.variables_dictionary.Add(entity.shortGUID, (VariableEntity)CopyOf(entity, resourceIds));
                foreach (FunctionEntity entity in Source.functions_dictionary.Values)
                    copy.functions_dictionary.Add(entity.shortGUID, (FunctionEntity)CopyOf(entity, resourceIds));
                foreach (AliasEntity entity in Source.aliases_dictionary.Values)
                    copy.aliases_dictionary.Add(entity.shortGUID, (AliasEntity)CopyOf(entity, resourceIds));
                foreach (ProxyEntity entity in Source.proxies_dictionary.Values)
                    copy.proxies_dictionary.Add(entity.shortGUID, (ProxyEntity)CopyOf(entity, resourceIds));

                //A variable's pin type is kept by composite and variable id - in the level's own table, or for a vanilla
                //composite the shipped one, which knows nothing of the copy: without it a pin draws as a plain parameter,
                //and the flowgraph drops the links on its other sides when it next compiles
                List<CompositePinInfoTable.PinInfo> pins = new List<CompositePinInfoTable.PinInfo>();
                foreach (VariableEntity variable in Source.variables_dictionary.Values)
                {
                    CompositePinInfoTable.PinInfo info = _ctx.Utils.GetPinInfo(Source, variable);
                    if (info != null)
                        pins.Add(new CompositePinInfoTable.PinInfo()
                        {
                            VariableGUID = info.VariableGUID,
                            PinTypeGUID = info.PinTypeGUID,
                            PinEnumTypeGUID = info.PinEnumTypeGUID,
                        });
                }
                _ctx.Utils.AddCustomPinInfos(copy, pins);

                foreach ((Composite holder, FunctionEntity instance) in Switch)
                {
                    tx.Touch(holder);
                    tx.Touch(instance);
                    instance.function = copy.shortGUID;
                }
                tx.Commit();

                RefactorResult result = new RefactorResult() { Transaction = tx, CreatedComposite = copy };
                result.Pages[copy] = (pages?.GetPages(Source) ?? new List<FlowgraphMeta>()).Select(o =>
                {
                    FlowgraphMeta page = PageRewriter.Copy(o, copy.shortGUID);
                    page.AlwaysUse = true;
                    return page;
                }).ToList();
                foreach (Entity entity in copy.GetEntities())
                    result.IdMap[entity.shortGUID] = entity.shortGUID;
                result.Issues.AddRange(Issues);
                return result;
            }
            catch
            {
                tx.Revert();
                throw;
            }
        }

        /// <summary>An entity of the original, copied with its id, links, parameters and paths, its resources sharing the level's objects.</summary>
        private static Entity CopyOf(Entity entity, HashSet<ShortGuid> resourceIds)
        {
            Entity copy = entity.Copy();
            copy.shortGUID = entity.shortGUID;
            RefactorContext.ShareResources(copy, entity, resourceIds);
            return copy;
        }
    }
}
#endif
