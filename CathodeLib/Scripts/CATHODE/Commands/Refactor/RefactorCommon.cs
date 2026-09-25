#if !GODOT
using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CATHODE.Scripting.Refactor
{
    /// <summary>Something a refactor could not do exactly, or cannot do at all.</summary>
    public sealed class RefactorIssue
    {
        /// <summary>True when the refactor must not go ahead; false when it can, with the result described by <see cref="Message"/>.</summary>
        public bool Blocking;
        public string Message;

        public override string ToString() => (Blocking ? "Cannot: " : "Note: ") + Message;
    }

    /// <summary>What a refactor did, for the editor to follow up on (pages, viewer, undo).</summary>
    public sealed class RefactorResult
    {
        /// <summary>Every script change, committed: revert it to undo, reapply it to redo.</summary>
        public ScriptTransaction Transaction;

        /// <summary>The script pages each composite should now have, for composites whose pages were kept in step with its links.</summary>
        public Dictionary<Composite, List<CompositeFlowgraphTable.FlowgraphMeta>> Pages = new Dictionary<Composite, List<CompositeFlowgraphTable.FlowgraphMeta>>();

        /// <summary>The composite a Create Composite made, or null.</summary>
        public Composite CreatedComposite;

        /// <summary>The instance a Create Composite placed, or null.</summary>
        public FunctionEntity CreatedInstance;

        /// <summary>The entities a De-instance put into the parent, in the order they were added.</summary>
        public List<Entity> PulledEntities = new List<Entity>();

        /// <summary>For a De-instance: each of the instanced composite's entities, and the id its copy (or what the copy was merged into) has in the parent.</summary>
        public Dictionary<ShortGuid, ShortGuid> IdMap = new Dictionary<ShortGuid, ShortGuid>();

        /// <summary>Notes on what could not be carried over exactly.</summary>
        public List<RefactorIssue> Issues = new List<RefactorIssue>();
    }

    /// <summary>
    /// Where a refactor reads a composite's script pages from, and whether it should keep them in step.
    /// </summary>
    /// <remarks>
    /// Pages are editor data (the flowgraph layouts). A composite whose pages draw all of its links has
    /// those links rebuilt from the pages when the editor saves it, so any link a refactor adds has to
    /// be drawn, and any it removes has to go from the page, or the next save undoes the refactor.
    /// </remarks>
    public interface IRefactorPageSource
    {
        /// <summary>The composite's pages. The refactor copies them and never changes these objects.</summary>
        List<CompositeFlowgraphTable.FlowgraphMeta> GetPages(Composite composite);

        /// <summary>
        /// Whether the composite's links are compiled from its pages, so its pages must be kept exactly in
        /// step with its links. When false the links are the truth and the pages are left alone.
        /// </summary>
        bool PagesCarryLinks(Composite composite);
    }

    /// <summary>A step of a resolved path: which element of the stored array it came from, and what it names.</summary>
    internal struct PathStep
    {
        public int Index;
        public Composite Composite;
        public Entity Entity;
    }

    /// <summary>How a stored path was read.</summary>
    internal enum PathReading
    {
        Local,  //From the composite holding the path, element 0 first
        Root,   //From the level's root composite, element 0 first
        Proxy,  //Element 0 names a composite; walked from the root at element 1, repeated hops skipped
    }

    /// <summary>A stored path somewhere in the level, resolved.</summary>
    internal sealed class ResolvedPath
    {
        public Composite Holder;     //The composite holding the entity that stores the path
        public Entity Owner;         //The alias, proxy, TriggerSequence or CAGEAnimation that stores it
        public PathKind Kind;
        public int Slot;             //Which entry or connection, for sequences and animations
        public ShortGuid[] Path;     //The stored array
        public PathReading Reading;
        public List<PathStep> Steps;
    }

    internal enum PathKind
    {
        Alias,
        Proxy,
        ProxySequence,
        TriggerSequence,
        Animation,
    }

    /// <summary>Shared lookups and helpers for the refactors.</summary>
    internal sealed class RefactorContext
    {
        public readonly Commands Commands;
        public readonly CommandsUtils Utils;
        public readonly Dictionary<ShortGuid, Composite> CompositesById = new Dictionary<ShortGuid, Composite>();

        /// <summary>The parameters every composite instance has whatever it instances, by variant.</summary>
        public readonly Dictionary<ShortGuid, ParameterVariant> InstanceInterface = new Dictionary<ShortGuid, ParameterVariant>();
        /// <summary>Relay pin to the method it relays, for the instance interface's methods (show to shown, and so on).</summary>
        public readonly Dictionary<ShortGuid, ShortGuid> InterfaceRelayToMethod = new Dictionary<ShortGuid, ShortGuid>();

        private readonly Dictionary<Composite, List<Composite>> _parents = new Dictionary<Composite, List<Composite>>();

        public RefactorContext(Commands commands)
        {
            Commands = commands;
            Utils = commands.Utils;
            foreach (Composite composite in commands.Entries)
                if (composite != null && !CompositesById.ContainsKey(composite.shortGUID))
                    CompositesById.Add(composite.shortGUID, composite);

            FunctionType? type = FunctionType.CompositeInterface;
            while (type != null)
            {
                foreach ((ShortGuid id, ParameterVariant variant, DataType _) in Utils.GetAllParameters(type.Value))
                {
                    if (InstanceInterface.ContainsKey(id))
                        continue;
                    InstanceInterface.Add(id, variant);
                    if (variant == ParameterVariant.METHOD_PIN)
                    {
                        ShortGuid relay = Utils.GetRelay(id);
                        if (relay != ShortGuid.Invalid && !InterfaceRelayToMethod.ContainsKey(relay))
                            InterfaceRelayToMethod.Add(relay, id);
                    }
                }
                type = Utils.GetInheritedFunction(type.Value);
            }
        }

        public Composite GetComposite(ShortGuid id)
        {
            CompositesById.TryGetValue(id, out Composite composite);
            return composite;
        }

        /// <summary>The composite a function entity instances, or null for a function type (or a missing composite).</summary>
        public Composite InstancedComposite(Entity entity)
        {
            if (!(entity is FunctionEntity function) || function.function.IsFunctionType)
                return null;
            return GetComposite(function.function);
        }

        /// <summary>Every composite with an instance of <paramref name="composite"/>, directly.</summary>
        public List<Composite> ParentsOf(Composite composite)
        {
            if (_parents.Count == 0)
            {
                foreach (Composite candidate in Commands.Entries)
                {
                    if (candidate == null) continue;
                    foreach (FunctionEntity function in candidate.functions_dictionary.Values)
                    {
                        Composite instanced = InstancedComposite(function);
                        if (instanced == null) continue;
                        if (!_parents.TryGetValue(instanced, out List<Composite> list))
                            _parents.Add(instanced, list = new List<Composite>());
                        if (!list.Contains(candidate))
                            list.Add(candidate);
                    }
                }
            }
            return _parents.TryGetValue(composite, out List<Composite> parents) ? parents : new List<Composite>();
        }

        #region Paths
        /// <summary>
        /// Resolve a stored path the way the tools do (<see cref="CommandsUtils.ResolveEntityPath"/>:
        /// local, then from the root, then as a proxy), keeping which array element each step came from.
        /// </summary>
        public ResolvedPath Resolve(ShortGuid[] path, Composite holder, bool proxy)
        {
            if (!proxy)
            {
                List<PathStep> steps = Walk(path, holder, 0, false);
                if (steps != null)
                    return new ResolvedPath() { Path = path, Holder = holder, Reading = PathReading.Local, Steps = steps };
                steps = Walk(path, Commands.EntryPoints?[0], 0, false);
                if (steps != null)
                    return new ResolvedPath() { Path = path, Holder = holder, Reading = PathReading.Root, Steps = steps };
            }
            List<PathStep> proxySteps = WalkProxy(path);
            if (proxySteps != null)
                return new ResolvedPath() { Path = path, Holder = holder, Reading = PathReading.Proxy, Steps = proxySteps };
            return null;
        }

        private List<PathStep> Walk(ShortGuid[] path, Composite start, int first, bool skipRepeats)
        {
            if (path == null || start == null || path.Length - first <= 1)
                return null;
            int end = path.Length - (path[path.Length - 1] == ShortGuid.Invalid ? 1 : 0);
            List<PathStep> steps = new List<PathStep>();
            Composite current = start;
            for (int i = first; i < end; i++)
            {
                if (skipRepeats && i > first && path[i] == path[i - 1])
                    continue;
                Entity entity = current.GetEntityByID(path[i]);
                if (entity == null)
                    return null;
                steps.Add(new PathStep() { Index = i, Composite = current, Entity = entity });
                if (i != end - 1)
                {
                    current = InstancedComposite(entity);
                    if (current == null)
                        return null;
                }
            }
            return steps.Count == 0 ? null : steps;
        }

        private List<PathStep> WalkProxy(ShortGuid[] path)
        {
            if (path == null || path.Length <= 2)
                return null;
            Composite named = GetComposite(path[0]);
            Composite root = Commands.EntryPoints?[0] ?? named;
            if (root == null)
                return null;
            Composite start = root;
            if (root.GetEntityByID(path[1]) == null && named != null && named.GetEntityByID(path[1]) != null)
                start = named;
            //The proxy reading skips an element equal to the one before it, element 0 (a composite id) included
            List<PathStep> steps = new List<PathStep>();
            int end = path.Length - (path[path.Length - 1] == ShortGuid.Invalid ? 1 : 0);
            Composite current = start;
            for (int i = 1; i < end; i++)
            {
                if (path[i] == path[i - 1])
                    continue;
                Entity entity = current.GetEntityByID(path[i]);
                if (entity == null)
                    return null;
                steps.Add(new PathStep() { Index = i, Composite = current, Entity = entity });
                if (i != end - 1)
                {
                    current = InstancedComposite(entity);
                    if (current == null)
                        return null;
                }
            }
            return steps.Count == 0 ? null : steps;
        }

        /// <summary>Every stored path in the level that names <paramref name="anyOf"/> somewhere in its array, resolved.</summary>
        public List<ResolvedPath> FindPaths(HashSet<ShortGuid> anyOf)
        {
            List<ResolvedPath> found = new List<ResolvedPath>();
            foreach (Composite composite in Commands.Entries)
            {
                if (composite == null) continue;
                foreach (AliasEntity alias in composite.aliases_dictionary.Values)
                    Consider(found, anyOf, composite, alias, PathKind.Alias, 0, alias.alias?.path, false);
                foreach (ProxyEntity proxy in composite.proxies_dictionary.Values)
                {
                    Consider(found, anyOf, composite, proxy, PathKind.Proxy, 0, proxy.proxy?.path, true);
                    if (proxy.sequence != null)
                        for (int i = 0; i < proxy.sequence.Count; i++)
                            Consider(found, anyOf, composite, proxy, PathKind.ProxySequence, i, proxy.sequence[i].connectedEntity?.path, false);
                }
                foreach (FunctionEntity function in composite.functions_dictionary.Values)
                {
                    if (function is TriggerSequence trigger)
                    {
                        for (int i = 0; i < trigger.sequence.Count; i++)
                            Consider(found, anyOf, composite, trigger, PathKind.TriggerSequence, i, trigger.sequence[i].connectedEntity?.path, false);
                    }
                    else if (function is CAGEAnimation animation)
                    {
                        for (int i = 0; i < animation.connections.Count; i++)
                            Consider(found, anyOf, composite, animation, PathKind.Animation, i, animation.connections[i].connectedEntity?.path, false);
                    }
                }
            }
            return found;
        }

        private void Consider(List<ResolvedPath> found, HashSet<ShortGuid> anyOf, Composite holder, Entity owner, PathKind kind, int slot, ShortGuid[] path, bool proxy)
        {
            if (path == null)
                return;
            bool mentions = false;
            for (int i = 0; i < path.Length && !mentions; i++)
                mentions = anyOf.Contains(path[i]);
            if (!mentions)
                return;
            ResolvedPath resolved = Resolve(path, holder, proxy);
            if (resolved == null)
                return;
            resolved.Owner = owner;
            resolved.Kind = kind;
            resolved.Slot = slot;
            found.Add(resolved);
        }

        /// <summary>Write a new array into the slot a resolved path was read from.</summary>
        public static void StorePath(ResolvedPath path, ShortGuid[] value)
        {
            switch (path.Kind)
            {
                case PathKind.Alias:
                    ((AliasEntity)path.Owner).alias = new EntityPath() { path = value };
                    break;
                case PathKind.Proxy:
                    ((ProxyEntity)path.Owner).proxy = new EntityPath() { path = value };
                    break;
                case PathKind.ProxySequence:
                    ((ProxyEntity)path.Owner).sequence[path.Slot].connectedEntity = new EntityPath() { path = value };
                    break;
                case PathKind.TriggerSequence:
                    ((TriggerSequence)path.Owner).sequence[path.Slot].connectedEntity = new EntityPath() { path = value };
                    break;
                case PathKind.Animation:
                    ((CAGEAnimation)path.Owner).connections[path.Slot].connectedEntity = new EntityPath() { path = value };
                    break;
            }
        }

        /// <summary>A path array with a terminator, from its elements.</summary>
        public static ShortGuid[] Terminated(IEnumerable<ShortGuid> elements)
        {
            List<ShortGuid> list = elements.Where(o => o != ShortGuid.Invalid).ToList();
            list.Add(ShortGuid.Invalid);
            return list.ToArray();
        }
        #endregion

        #region Parameters
        public static bool Truthy(Entity entity, ShortGuid parameter)
        {
            return Truthy(entity?.GetParameter(parameter)?.content);
        }

        public static bool Truthy(ParameterData data)
        {
            switch (data)
            {
                case cBool b: return b.value;
                case cInteger i: return i.value != 0;
                case cFloat f: return f.value != 0;
                default: return false;
            }
        }

        public static cTransform TransformOf(Entity entity)
        {
            if (entity?.GetParameter(ShortGuids.position)?.content is cTransform transform)
                return transform;
            return null;
        }

        public static bool IsIdentity(cTransform transform)
        {
            return transform == null || (transform.position == Vector3.Zero && transform.rotation == Vector3.Zero);
        }

        /// <summary>Whether an entity type has a <c>position</c> of its own to place it.</summary>
        public bool IsSpatial(Entity entity, Composite composite)
        {
            if (entity == null)
                return false;
            if (entity.GetParameter(ShortGuids.position) != null)
                return true;
            if (!(entity is FunctionEntity))
                return false;
            if (InstancedComposite(entity) != null)
                return true;
            foreach ((ShortGuid id, ParameterVariant _, DataType _) in Utils.GetAllParameters(entity, composite))
                if (id == ShortGuids.position)
                    return true;
            return false;
        }

        /// <summary>Whether the entity has the given parameter, method or relay pin at all.</summary>
        public bool Has(Entity entity, Composite composite, ShortGuid parameter, out ParameterVariant variant)
        {
            variant = ParameterVariant.PARAMETER;
            (ParameterVariant? v, DataType? _, ShortGuid _) = Utils.GetParameterMetadata(entity, parameter, composite);
            if (v == null)
                return false;
            variant = v.Value;
            return true;
        }

        /// <summary>Set a parameter's value, replacing its content object (never writing into the one there).</summary>
        public static void SetParameter(Entity entity, ShortGuid name, ParameterData data)
        {
            Parameter existing = entity.GetParameter(name);
            if (existing != null)
                existing.content = data;
            else
                entity.parameters.Add(new Parameter(name, data, ParameterVariant.PARAMETER));
        }

        public static ParameterData CloneData(ParameterData data)
        {
            return data == null ? null : (ParameterData)data.Clone();
        }
        #endregion

        #region Transforms
        /// <summary>
        /// <paramref name="child"/> placed inside <paramref name="parent"/>: the child's offset turned by
        /// the parent's rotation, then moved out to it. The same composition instancing uses (the child's
        /// matrix times the parent's, row vectors) and OpenCAGE's InstanceTransform.Compose, worked in
        /// double precision: an instance pitched straight up or down leaves yaw and roll to be recovered
        /// from matrix entries near zero, where single precision turned a composed rotation half a degree.
        /// </summary>
        public static cTransform Compose(cTransform parent, cTransform child)
        {
            if (parent == null)
                return child == null ? new cTransform() : new cTransform(child.position, child.rotation);
            if (child == null)
                return new cTransform(parent.position, parent.rotation);
            //Nothing turned: add the offsets and keep the stored angles exactly, rather than recomputing them
            if (parent.rotation == Vector3.Zero)
                return new cTransform(parent.position + child.position, child.rotation);
            double[] p = ToQuaternion(parent.rotation);
            double[] c = ToQuaternion(child.rotation);
            double[] offset = Rotate(p, child.position.X, child.position.Y, child.position.Z);
            return new cTransform(
                new Vector3((float)(parent.position.X + offset[0]), (float)(parent.position.Y + offset[1]), (float)(parent.position.Z + offset[2])),
                ToEulerDegrees(Multiply(p, c)));
        }

        //Quaternions as {x, y, z, w}, following System.Numerics exactly (CreateFromYawPitchRoll, *, Transform, CreateFromQuaternion)
        private static double[] ToQuaternion(Vector3 eulerDegrees)
        {
            const double d = Math.PI / 180.0;
            double yaw = eulerDegrees.Y * d, pitch = eulerDegrees.X * d, roll = eulerDegrees.Z * d;
            double sr = Math.Sin(roll * 0.5), cr = Math.Cos(roll * 0.5);
            double sp = Math.Sin(pitch * 0.5), cp = Math.Cos(pitch * 0.5);
            double sy = Math.Sin(yaw * 0.5), cy = Math.Cos(yaw * 0.5);
            return new[]
            {
                cy * sp * cr + sy * cp * sr,
                sy * cp * cr - cy * sp * sr,
                cy * cp * sr - sy * sp * cr,
                cy * cp * cr + sy * sp * sr,
            };
        }

        private static double[] Multiply(double[] a, double[] b)
        {
            double cx = a[1] * b[2] - a[2] * b[1];
            double cy = a[2] * b[0] - a[0] * b[2];
            double cz = a[0] * b[1] - a[1] * b[0];
            double dot = a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
            return new[]
            {
                a[0] * b[3] + b[0] * a[3] + cx,
                a[1] * b[3] + b[1] * a[3] + cy,
                a[2] * b[3] + b[2] * a[3] + cz,
                a[3] * b[3] - dot,
            };
        }

        private static double[] Rotate(double[] q, double x, double y, double z)
        {
            double x2 = q[0] + q[0], y2 = q[1] + q[1], z2 = q[2] + q[2];
            double wx2 = q[3] * x2, wy2 = q[3] * y2, wz2 = q[3] * z2;
            double xx2 = q[0] * x2, xy2 = q[0] * y2, xz2 = q[0] * z2;
            double yy2 = q[1] * y2, yz2 = q[1] * z2, zz2 = q[2] * z2;
            return new[]
            {
                x * (1.0 - yy2 - zz2) + y * (xy2 - wz2) + z * (xz2 + wy2),
                x * (xy2 + wz2) + y * (1.0 - xx2 - zz2) + z * (yz2 - wx2),
                x * (xz2 - wy2) + y * (yz2 + wx2) + z * (1.0 - xx2 - yy2),
            };
        }

        private static Vector3 ToEulerDegrees(double[] q)
        {
            double n = Math.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
            double x = q[0] / n, y = q[1] / n, z = q[2] / n, w = q[3] / n;
            //The row-vector matrix of the quaternion; only the entries the angles come from
            double m11 = 1.0 - 2.0 * (y * y + z * z), m12 = 2.0 * (x * y + z * w), m13 = 2.0 * (x * z - y * w);
            double m22 = 1.0 - 2.0 * (z * z + x * x);
            double m31 = 2.0 * (x * z + y * w), m32 = 2.0 * (y * z - x * w), m33 = 1.0 - 2.0 * (y * y + x * x);

            double sinPitch = Math.Min(1.0, Math.Max(-1.0, -m32));
            double pitch = Math.Asin(sinPitch);
            double yaw, roll;
            //At a pitch within a millionth of a radian of straight up or down, the entries yaw and roll come from are
            //rounding noise; there the two turn about the same axis anyway, so the whole turn goes to yaw
            if (Math.Sqrt(Math.Max(0.0, 1.0 - sinPitch * sinPitch)) < 1e-6)
            {
                yaw = Math.Atan2(-m13, m11);
                roll = 0.0;
            }
            else
            {
                yaw = Math.Atan2(m31, m33);
                roll = Math.Atan2(m12, m22);
            }
            const double r = 180.0 / Math.PI;
            return new Vector3((float)(pitch * r), (float)(yaw * r), (float)(roll * r));
        }
        #endregion

        #region Resources
        /// <summary>
        /// Every resource id a composite's entities hold - resource parameters and entity-level references.
        /// Ids must be unique within a composite: on load, a composite's references go to the first holder
        /// whose id matches.
        /// </summary>
        public static HashSet<ShortGuid> ResourceIds(Composite composite)
        {
            HashSet<ShortGuid> ids = new HashSet<ShortGuid>();
            foreach (Entity entity in composite.GetEntities())
            {
                foreach (Parameter parameter in entity.parameters)
                    if (parameter?.content is cResource resource && resource.value != null)
                        ids.Add(resource.shortGUID);
                if (entity is FunctionEntity function)
                    foreach (ResourceReference reference in function.resources)
                        if (reference.resource_type != ResourceType.DYNAMIC_PHYSICS_SYSTEM)
                            ids.Add(reference.resource_id);
            }
            return ids;
        }

        /// <summary>
        /// Make <paramref name="copy"/>, a <c>Copy()</c> of <paramref name="source"/> that already has its
        /// own id, share the source's level objects (renderables, collision, physics), as two entities
        /// loaded from disk would. Resource ids are kept where they are free in the destination, so a copy
        /// that keeps its entity id keeps its resource ids too; a clashing one gets a fresh id.
        /// </summary>
        public static void ShareResources(Entity copy, Entity source, HashSet<ShortGuid> takenIds)
        {
            foreach (Parameter parameter in copy.parameters)
            {
                if (!(parameter?.content is cResource resource) || resource.value == null)
                    continue;
                cResource sourceResource = source.GetParameter(parameter.name)?.content as cResource;
                foreach (ResourceReference reference in resource.value)
                    ShareLevelObjects(reference, sourceResource?.GetResource(reference.resource_type));

                ShortGuid id = resource.shortGUID;
                if (id == source.shortGUID)
                    id = copy.shortGUID;
                if (takenIds.Contains(id))
                    id = ShortGuidUtils.GenerateRandom();
                takenIds.Add(id);
                resource.shortGUID = id;
                foreach (ResourceReference reference in resource.value)
                    reference.resource_id = id;
            }

            if (copy is FunctionEntity function)
            {
                FunctionEntity sourceFunction = source as FunctionEntity;
                foreach (ResourceReference reference in function.resources)
                {
                    ShareLevelObjects(reference, sourceFunction?.GetResource(reference.resource_type));
                    if (reference.resource_type != ResourceType.DYNAMIC_PHYSICS_SYSTEM)
                        reference.resource_id = function.shortGUID;
                }
                takenIds.Add(function.shortGUID);
            }
        }

        private static void ShareLevelObjects(ResourceReference reference, ResourceReference original)
        {
            if (reference == null || original == null || ReferenceEquals(reference, original))
                return;
            reference.RenderableInstance = original.RenderableInstance == null ? null : new List<CATHODE.RenderableElements.Element>(original.RenderableInstance);
            reference.PhysicsSystem = original.PhysicsSystem;
            reference.AnimatedModel = original.AnimatedModel;
            reference.CollisionMapping = original.CollisionMapping;
        }
        #endregion

        #region Names
        public string NameOf(Composite composite, Entity entity)
        {
            if (entity == null)
                return "(missing)";
            string name = Utils.GetEntityName(composite, entity);
            return string.IsNullOrEmpty(name) ? entity.shortGUID.ToByteString() : name;
        }

        public static string LeafName(Composite composite)
        {
            string name = composite?.name ?? "";
            int slash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
            return slash < 0 ? name : name.Substring(slash + 1);
        }

        public static string ParamName(ShortGuid id)
        {
            string name = ShortGuidUtils.FindString(id);
            return string.IsNullOrEmpty(name) ? id.ToByteString() : name;
        }
        #endregion
    }

    /// <summary>A link as an owner, the owner's parameter, the linked entity, and the linked parameter.</summary>
    internal struct LinkKey : IEquatable<LinkKey>
    {
        public ShortGuid Owner;
        public ShortGuid Param;
        public ShortGuid Target;
        public ShortGuid TargetParam;

        public LinkKey(ShortGuid owner, ShortGuid param, ShortGuid target, ShortGuid targetParam)
        {
            Owner = owner;
            Param = param;
            Target = target;
            TargetParam = targetParam;
        }

        public LinkKey(Entity owner, EntityConnector link) : this(owner.shortGUID, link.thisParamID, link.linkedEntityID, link.linkedParamID) { }

        public bool Equals(LinkKey other) => Owner == other.Owner && Param == other.Param && Target == other.Target && TargetParam == other.TargetParam;
        public override bool Equals(object obj) => obj is LinkKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Owner.AsUInt32;
                hash = hash * 397 ^ (int)Param.AsUInt32;
                hash = hash * 397 ^ (int)Target.AsUInt32;
                hash = hash * 397 ^ (int)TargetParam.AsUInt32;
                return hash;
            }
        }
        public override string ToString() => Owner.ToByteString() + "." + RefactorContext.ParamName(Param) + " -> " + Target.ToByteString() + "." + RefactorContext.ParamName(TargetParam);
    }
}
#endif
