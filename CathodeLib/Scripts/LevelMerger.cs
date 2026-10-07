using CATHODE;
using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib
{
    /// <summary>
    /// Combines several edited copies of one level into one. Each copy's changes against the unmodified level -
    /// composites added or removed; entities added, removed or edited (parameters, links, aliases and proxies,
    /// keyframes, trigger sequences, resources); textures and models replaced; level sound and galaxy data - are
    /// applied in turn to a destination level that starts out unmodified. Apply them in list order: where two
    /// copies change the same thing, the later one is kept and the clash is recorded.
    /// </summary>
    /// <remarks>
    /// Only what each copy changed travels, so edits to different composites, different entities, or different
    /// parameters and links of one entity all survive together. New assets come over with the entities that use
    /// them (through <see cref="CompositePorter"/>). Save the destination with <see cref="Level.SaveInstanced()"/>:
    /// movers, collision, navigation and lighting are rebuilt from the combined scripts.
    /// </remarks>
    public class LevelMerger
    {
        public class Conflict
        {
            public string Where;
            public string Kept;
            public string Lost;
            public string Detail;
        }

        public List<Conflict> Conflicts { get; } = new List<Conflict>();
        public List<string> Notes { get; } = new List<string>();

        /// <summary>Each composite a copy changed, with the copies that changed it in order - for merging editor data kept beside the scripts.</summary>
        public Dictionary<ShortGuid, List<string>> ChangedComposites { get; } = new Dictionary<ShortGuid, List<string>>();

        private readonly Level _vanilla;
        private readonly Level _dest;

        private Level _mod;
        private string _modName;
        private CompositePorter _porter;
        private readonly Dictionary<string, string> _claims = new Dictionary<string, string>();

        public LevelMerger(Level vanilla, Level destination)
        {
            _vanilla = vanilla ?? throw new ArgumentNullException(nameof(vanilla));
            _dest = destination ?? throw new ArgumentNullException(nameof(destination));
            if (vanilla.Commands == null || destination.Commands == null)
                throw new ArgumentException("Both levels must be loaded.");
        }

        /// <summary>Apply one edited copy's changes. Call once per copy, in list order.</summary>
        public void Apply(Level mod, string modName)
        {
            if (mod?.Commands == null) throw new ArgumentException("The copy must be loaded.");
            _mod = mod;
            _modName = modName;
            _porter = new CompositePorter(mod, _dest) { OverwriteComposites = false, OverwriteAssets = false, Recurse = false };

            MergeTextures();
            MergeModels();
            MergeComposites();
            MergeStandaloneData();
        }

        /// <summary>
        /// After the last copy: tidy what combining can leave behind - links to entities another copy removed -
        /// and record it.
        /// </summary>
        public void Finish()
        {
            foreach (ShortGuid id in ChangedComposites.Keys)
            {
                Composite composite = _dest.Commands.GetComposite(id);
                if (composite == null) continue;
                Composite original = _vanilla.Commands.GetComposite(id);
                foreach (Entity entity in composite.GetEntities())
                {
                    int before = entity.childLinks.Count;
                    entity.childLinks.RemoveAll(o => composite.GetEntityByID(o.linkedEntityID) == null && original?.GetEntityByID(o.linkedEntityID) != null);
                    if (entity.childLinks.Count != before)
                        Notes.Add(Describe(composite, entity, _dest) + ": " + (before - entity.childLinks.Count) + " link(s) to entities another mod removed were dropped");
                }
            }
        }

        #region CLAIMS
        /* Where a change was made: the key tells things apart by id (names repeat - two folders can each hold a
           composite of one name), the text is how it's shown */
        private struct Spot
        {
            public string Key, Text;
            public Spot(string key, string text) { Key = key; Text = text; }
            public Spot Sub(string key, string text) { return new Spot(Key + "/" + key, Text + text); }
        }

        private static Spot At(Composite composite)
        {
            return new Spot(composite.shortGUID.ToByteString(), "composite " + composite.name);
        }

        /* Named as the unmodified level names it, so every copy's changes to it - a rename included - meet in one place */
        private static Spot At(Composite composite, Entity entity, Level level)
        {
            return new Spot(composite.shortGUID.ToByteString() + "/" + entity.shortGUID.ToByteString(), Describe(composite, entity, level));
        }

        private static Spot Named(string text)
        {
            return new Spot("~" + text, text);
        }

        /* A change at 'where' by the current copy: if an earlier copy changed the same thing to something else,
           that's a clash the current copy wins */
        private void Claim(Spot where, bool differsFromEarlier, string detail = null)
        {
            if (_claims.TryGetValue(where.Key, out string earlier) && earlier != _modName && differsFromEarlier)
                Conflicts.Add(new Conflict() { Where = where.Text, Kept = _modName, Lost = earlier, Detail = detail });
            _claims[where.Key] = _modName;
        }

        /* A removal or a replacement by the current copy - the thing taken whole: a clash with any earlier copy that
           changed the thing itself or anything in it */
        private void ClaimRemoval(Spot where, string detail, bool differsFromEarlier = true)
        {
            string earlier = null;
            string inside = where.Key + "/";
            if (differsFromEarlier)
                foreach (KeyValuePair<string, string> claim in _claims)
                    if (claim.Value != _modName && (claim.Key == where.Key || claim.Key.StartsWith(inside, StringComparison.Ordinal)))
                        earlier = claim.Value;
            if (earlier != null)
                Conflicts.Add(new Conflict() { Where = where.Text, Kept = _modName, Lost = earlier, Detail = detail });
            _claims[where.Key] = _modName;
        }

        private void Touched(Composite composite)
        {
            if (!ChangedComposites.TryGetValue(composite.shortGUID, out List<string> mods))
                ChangedComposites[composite.shortGUID] = mods = new List<string>();
            if (!mods.Contains(_modName))
                mods.Add(_modName);
        }

        private static string Describe(Composite composite, Entity entity, Level level)
        {
            string name = entity == null ? null : level.Commands.Utils.GetEntityName(composite, entity);
            string leaf = composite.name.Replace('\\', '/');
            leaf = leaf.Substring(leaf.LastIndexOf('/') + 1);
            return entity == null ? leaf : leaf + " › " + (string.IsNullOrEmpty(name) ? entity.shortGUID.ToByteString() : name);
        }
        #endregion

        #region COMPOSITES
        private void MergeComposites()
        {
            Dictionary<ShortGuid, Composite> vanilla = new Dictionary<ShortGuid, Composite>();
            foreach (Composite c in _vanilla.Commands.Entries)
                if (c != null) vanilla[c.shortGUID] = c;
            HashSet<ShortGuid> inMod = new HashSet<ShortGuid>();
            List<Composite> broughtBack = new List<Composite>();

            foreach (Composite mc in _mod.Commands.Entries.ToList())
            {
                if (mc == null) continue;
                inMod.Add(mc.shortGUID);
                Composite dc = _dest.Commands.GetComposite(mc.shortGUID);
                if (!vanilla.TryGetValue(mc.shortGUID, out Composite vc))
                {
                    //New in this copy: the whole composite comes over, with what it uses
                    Spot where = At(mc);
                    if (dc != null)
                    {
                        //The same composite added by an earlier copy (both ported it in): the later copy's is kept
                        Claim(where, true, "both add it");
                        _dest.Commands.Entries.Remove(dc);
                    }
                    else
                        Claim(where, false);
                    _porter.Port(mc);
                    Touched(mc);
                    continue;
                }
                if (dc == null)
                {
                    //An earlier copy removed it, this one keeps it - and it may be changed: it comes back
                    if (!SameComposite(vc, mc))
                    {
                        Claim(At(vc), true, "removed by one, changed by the other");
                        _porter.Port(mc);
                        Touched(mc);
                        broughtBack.Add(mc);
                    }
                    continue;
                }
                MergeComposite(vc, mc, dc);
            }

            //...along with the instances of it the earlier copy took out, so it doesn't come back unused
            foreach (Composite back in broughtBack)
                foreach (Composite mp in _mod.Commands.Entries)
                {
                    if (mp == null) continue;
                    Composite vp = _vanilla.Commands.GetComposite(mp.shortGUID), dp = _dest.Commands.GetComposite(mp.shortGUID);
                    if (vp == null || dp == null) continue;
                    List<Entity> instances = mp.functions.Where(f => f.function == back.shortGUID && vp.GetEntityByID(f.shortGUID) != null && dp.GetEntityByID(f.shortGUID) == null).Cast<Entity>().ToList();
                    if (instances.Count == 0) continue;
                    foreach (Entity instance in instances)
                    {
                        Entity copy = (Entity)instance.Copy();
                        AddEntity(dp, copy);
                        _porter.PortEntityResources(new[] { copy });
                    }
                    Relink(vp, mp, dp, instances.Select(o => o.shortGUID));
                    Touched(dp);
                }

            //Removed by this copy
            foreach (Composite vc in vanilla.Values)
            {
                if (inMod.Contains(vc.shortGUID)) continue;
                Composite dc = _dest.Commands.GetComposite(vc.shortGUID);
                if (dc == null) continue;
                if (_dest.Commands.EntryPoints.Contains(dc))
                {
                    Notes.Add("'" + _modName + "' removes " + vc.name + ", which the level starts from - it was kept");
                    continue;
                }
                bool stillUsed = _dest.Commands.Entries.Any(c => c != null && c != dc && c.functions.Any(f => f.function == dc.shortGUID));
                if (stillUsed)
                {
                    Notes.Add("'" + _modName + "' removes " + vc.name + ", but another mod still uses it - it was kept");
                    continue;
                }
                ClaimRemoval(At(vc), "'" + _modName + "' removes it");
                _dest.Commands.Entries.Remove(dc);
                Touched(dc);
            }

            //Removed by an earlier copy but used by this one - an instance it kept, changed or added: it comes back, or
            //the level would hold an instance of nothing
            bool more = true;
            while (more)
            {
                more = false;
                HashSet<ShortGuid> used = new HashSet<ShortGuid>();
                foreach (Composite c in _dest.Commands.Entries)
                    if (c != null)
                        foreach (FunctionEntity f in c.functions)
                            used.Add(f.function);
                foreach (Composite mc in _mod.Commands.Entries)
                {
                    if (mc == null || !used.Contains(mc.shortGUID) || _dest.Commands.GetComposite(mc.shortGUID) != null) continue;
                    Notes.Add("'" + _modName + "' uses " + mc.name + ", which another mod removes - it was kept");
                    _porter.Port(mc);
                    Touched(mc);
                    more = true;
                }
            }
        }

        private bool SameComposite(Composite a, Composite b)
        {
            if (a.name != b.name) return false;
            List<Entity> ea = a.GetEntities(), eb = b.GetEntities();
            if (ea.Count != eb.Count) return false;
            foreach (Entity x in ea)
            {
                Entity y = b.GetEntityByID(x.shortGUID);
                if (y == null || !SameEntity(x, y, _vanilla, _mod, a, b)) return false;
            }
            return true;
        }

        private void MergeComposite(Composite vc, Composite mc, Composite dc)
        {
            bool touched = false;
            if (mc.name != vc.name)
            {
                Claim(At(vc).Sub("name", " (its name)"), dc.name != vc.name && dc.name != mc.name);
                dc.name = mc.name;
                touched = true;
            }

            List<Entity> added = new List<Entity>();
            List<Entity> resourcesToPort = new List<Entity>();
            List<ShortGuid> broughtBack = new List<ShortGuid>();

            //Added and changed
            foreach (Entity me in mc.GetEntities())
            {
                Entity ve = vc.GetEntityByID(me.shortGUID);
                Entity de = dc.GetEntityByID(me.shortGUID);
                if (ve == null)
                {
                    Spot where = At(mc, me, _mod);
                    if (de != null)
                    {
                        Claim(where, !SameEntity(de, me, _dest, _mod, dc, mc), "both add it");
                        dc.RemoveEntity(de);
                    }
                    else
                        Claim(where, false);
                    Entity copy = (Entity)me.Copy();
                    AddEntity(dc, copy);
                    added.Add(copy);
                    CopyPinInfo(mc, dc, me);
                    touched = true;
                    continue;
                }
                if (SameEntity(ve, me, _vanilla, _mod, vc, mc))
                    continue;
                touched = true;
                if (de == null)
                {
                    //An earlier copy removed it; this one changed it - it comes back as this copy has it
                    Claim(At(vc, ve, _vanilla), true, "removed by one, changed by the other");
                    Entity copy = (Entity)me.Copy();
                    AddEntity(dc, copy);
                    added.Add(copy);
                    CopyPinInfo(mc, dc, me);
                    broughtBack.Add(me.shortGUID);
                    continue;
                }
                MergeEntity(vc, mc, dc, ve, me, de, resourcesToPort);
            }

            //Removed
            foreach (Entity ve in vc.GetEntities())
            {
                if (mc.GetEntityByID(ve.shortGUID) != null) continue;
                Entity de = dc.GetEntityByID(ve.shortGUID);
                if (de == null) continue;
                ClaimRemoval(At(vc, ve, _vanilla), "'" + _modName + "' removes it");
                dc.RemoveEntity(de);
                touched = true;
            }

            if (broughtBack.Count != 0)
                Relink(vc, mc, dc, broughtBack);

            if (added.Count != 0)
                _porter.PortEntityResources(added);

            if (touched)
            {
                Touched(dc);
                CompositeModificationInfoTable.ModificationInfo info = _mod.Commands.Utils.GetModificationInfo(mc);
                if (info != null)
                    _dest.Commands.Utils.SetModificationInfo(info);
            }
        }

        /* Entities brought back after an earlier copy removed them get the links into them that the original had and
           this copy still has. Those links belong to other entities, and went when the entity was removed - without them
           it would be back but cut off from whatever starts it */
        private static void Relink(Composite vc, Composite mc, Composite dc, IEnumerable<ShortGuid> broughtBack)
        {
            HashSet<ShortGuid> back = new HashSet<ShortGuid>(broughtBack);
            foreach (Entity mz in mc.GetEntities())
            {
                Entity vz = vc.GetEntityByID(mz.shortGUID), dz = dc.GetEntityByID(mz.shortGUID);
                if (vz == null || dz == null) continue;
                foreach (EntityConnector link in mz.childLinks)
                    if (back.Contains(link.linkedEntityID) && vz.childLinks.Contains(link) && !dz.childLinks.Contains(link))
                        dz.childLinks.Add(link);
            }
        }

        private static void AddEntity(Composite composite, Entity entity)
        {
            switch (entity)
            {
                case VariableEntity variable: composite.AddVariable(variable); break;
                case FunctionEntity function: composite.AddFunction(function); break;
                case AliasEntity alias: composite.AddAlias(alias); break;
                case ProxyEntity proxy: composite.AddProxy(proxy); break;
            }
        }

        private void CopyPinInfo(Composite source, Composite destination, Entity entity)
        {
            if (!(entity is VariableEntity variable)) return;
            CompositePinInfoTable.PinInfo info = _mod.Commands.Utils.GetPinInfo(source, variable);
            if (info != null)
                _dest.Commands.Utils.SetPinInfo(destination, info);
        }
        #endregion

        #region ENTITIES
        private void MergeEntity(Composite vc, Composite mc, Composite dc, Entity ve, Entity me, Entity de, List<Entity> resourcesToPort)
        {
            Spot where = At(vc, ve, _vanilla);

            //A different kind of entity, or a function swapped for another: it's replaced whole
            bool replaced = ve.variant != me.variant
                || (ve is FunctionEntity vf && me is FunctionEntity mf && vf.function != mf.function)
                || (ve is ProxyEntity && me is ProxyEntity && ((ProxyEntity)ve).function != ((ProxyEntity)me).function);
            if (replaced)
            {
                //Every earlier change to it goes with it
                ClaimRemoval(where, "replaced with a different kind of entity", !SameEntity(de, me, _dest, _mod, dc, mc));
                dc.RemoveEntity(de);
                Entity copy = (Entity)me.Copy();
                AddEntity(dc, copy);
                _porter.PortEntityResources(new[] { copy });
                CopyPinInfo(mc, dc, me);
                return;
            }

            //Parameters, one by one
            HashSet<ShortGuid> ids = new HashSet<ShortGuid>(ve.parameters.Select(o => o.name).Concat(me.parameters.Select(o => o.name)));
            foreach (ShortGuid id in ids)
            {
                Parameter vp = ve.GetParameter(id), mp = me.GetParameter(id), dp = de.GetParameter(id);
                if (mp == null)
                {
                    if (vp == null || dp == null) continue;
                    Claim(where.Sub("p" + id.ToByteString(), " › " + id), !SameParameter(vp, _vanilla, dp, _dest), "'" + _modName + "' removes it");
                    de.parameters.Remove(dp);
                    continue;
                }
                if (vp != null && SameParameter(vp, _vanilla, mp, _mod))
                    continue;
                //(An earlier copy that removed it changed it too)
                Claim(where.Sub("p" + id.ToByteString(), " › " + id), !SameParameter(vp, _vanilla, dp, _dest) && !SameParameter(dp, _dest, mp, _mod),
                    "set to " + Show(mp.content));
                ParameterData content = (ParameterData)mp.content.Clone();
                if (content is cResource resource && resource.value != null)
                {
                    //Its own references, pointed at this level's copies of what they use
                    resource.value = resource.value.Select(o => (ResourceReference)o.Clone()).ToList();
                    _porter.PortResourceReferences(resource.value);
                }
                if (dp != null)
                {
                    dp.content = content;
                    dp.variant = mp.variant;
                }
                else
                    de.parameters.Add(new Parameter(mp.name, content, mp.variant));
            }

            //Links, as a set
            HashSet<(ShortGuid, ShortGuid, ShortGuid)> vLinks = Links(ve, vc), mLinks = Links(me, mc);
            foreach (EntityConnector link in me.childLinks)
            {
                var key = (link.thisParamID, link.linkedEntityID, link.linkedParamID);
                if (vLinks.Contains(key) || !mLinks.Contains(key)) continue;
                Claim(where.Sub("l" + link.thisParamID.ToByteString() + link.linkedEntityID.ToByteString() + link.linkedParamID.ToByteString(), " › link " + link.thisParamID + " → " + link.linkedParamID), false);
                if (!de.childLinks.Any(o => o.thisParamID == key.Item1 && o.linkedEntityID == key.Item2 && o.linkedParamID == key.Item3))
                    de.childLinks.Add(link);
            }
            foreach (EntityConnector link in ve.childLinks)
            {
                var key = (link.thisParamID, link.linkedEntityID, link.linkedParamID);
                if (mLinks.Contains(key) || !vLinks.Contains(key)) continue;
                de.childLinks.RemoveAll(o => o.thisParamID == key.Item1 && o.linkedEntityID == key.Item2 && o.linkedParamID == key.Item3);
            }

            //What each kind carries beyond those, each taken whole
            switch (me)
            {
                case VariableEntity mv:
                    {
                        VariableEntity vv = (VariableEntity)ve, dv = (VariableEntity)de;
                        if (mv.name != vv.name || mv.type != vv.type)
                        {
                            Claim(where.Sub("pin", " (its pin)"), (dv.name != vv.name || dv.type != vv.type) && (dv.name != mv.name || dv.type != mv.type));
                            dv.name = mv.name;
                            dv.type = mv.type;
                        }
                        CompositePinInfoTable.PinInfo mInfo = _mod.Commands.Utils.GetPinInfo(mc, mv), vInfo = _vanilla.Commands.Utils.GetPinInfo(vc, vv);
                        if (mInfo != null && (vInfo == null || mInfo.PinTypeGUID != vInfo.PinTypeGUID || mInfo.PinEnumTypeGUID != vInfo.PinEnumTypeGUID))
                            _dest.Commands.Utils.SetPinInfo(dc, mInfo);
                        break;
                    }
                case AliasEntity ma:
                    {
                        AliasEntity va = (AliasEntity)ve, da = (AliasEntity)de;
                        if (ma.alias != va.alias)
                        {
                            Claim(where.Sub("target", " (what it points at)"), da.alias != va.alias && da.alias != ma.alias);
                            da.alias = ma.alias.Copy();
                        }
                        break;
                    }
                case ProxyEntity mp:
                    {
                        ProxyEntity vp = (ProxyEntity)ve, dp = (ProxyEntity)de;
                        if (mp.proxy != vp.proxy)
                        {
                            Claim(where.Sub("target", " (what it points at)"), dp.proxy != vp.proxy && dp.proxy != mp.proxy);
                            dp.proxy = mp.proxy.Copy();
                        }
                        string ms = SequenceSignature(mp.sequence, mp.methods), vs = SequenceSignature(vp.sequence, vp.methods), ds = SequenceSignature(dp.sequence, dp.methods);
                        if (ms != vs)
                        {
                            Claim(where.Sub("sequence", " (its trigger sequence)"), ds != vs && ds != ms);
                            dp.sequence = (List<TriggerSequence.SequenceEntry>)mp.sequence.Copy();
                            dp.methods = (List<TriggerSequence.MethodEntry>)mp.methods.Copy();
                        }
                        break;
                    }
                case TriggerSequence mt:
                    {
                        TriggerSequence vt = (TriggerSequence)ve, dt = (TriggerSequence)de;
                        string ms = SequenceSignature(mt.sequence, mt.methods), vs = SequenceSignature(vt.sequence, vt.methods), ds = SequenceSignature(dt.sequence, dt.methods);
                        if (ms != vs)
                        {
                            Claim(where.Sub("sequence", " (its trigger sequence)"), ds != vs && ds != ms);
                            dt.sequence = (List<TriggerSequence.SequenceEntry>)mt.sequence.Copy();
                            dt.methods = (List<TriggerSequence.MethodEntry>)mt.methods.Copy();
                        }
                        break;
                    }
                case CAGEAnimation mn:
                    {
                        CAGEAnimation vn = (CAGEAnimation)ve, dn = (CAGEAnimation)de;
                        string ma = AnimationSignature(mn), va = AnimationSignature(vn), da = AnimationSignature(dn);
                        if (ma != va)
                        {
                            Claim(where.Sub("keyframes", " (its keyframes)"), da != va && da != ma);
                            dn.connections = (List<CAGEAnimation.Connection>)mn.connections.Copy();
                            dn.floatTracks = (List<CAGEAnimation.FloatTrack>)mn.floatTracks.Copy();
                            dn.eventTracks = (List<CAGEAnimation.EventTrack>)mn.eventTracks.Copy();
                        }
                        break;
                    }
            }

            //A function's own resources (its model, collision, physics...)
            if (me is FunctionEntity mfn && de is FunctionEntity dfn && ve is FunctionEntity vfn)
            {
                string mr = ResourcesSignature(mfn.resources, _mod), vr = ResourcesSignature(vfn.resources, _vanilla), dr = ResourcesSignature(dfn.resources, _dest);
                if (mr != vr)
                {
                    Claim(where.Sub("resources", " (its resources)"), dr != vr && dr != mr);
                    dfn.resources = mfn.resources.Select(o => (ResourceReference)o.Clone()).ToList();
                    _porter.PortResourceReferences(dfn.resources);
                }
            }
        }

        /* An entity's links to entities of its own composite. A link to nothing - retail ships some, and a save drops
           them - is no difference between two copies */
        private static HashSet<(ShortGuid, ShortGuid, ShortGuid)> Links(Entity entity, Composite composite)
        {
            return new HashSet<(ShortGuid, ShortGuid, ShortGuid)>(entity.childLinks
                .Where(o => composite == null || composite.GetEntityByID(o.linkedEntityID) != null)
                .Select(o => (o.thisParamID, o.linkedEntityID, o.linkedParamID)));
        }

        /// <summary>Whether two entities - each read with its own level - say the same thing.</summary>
        private bool SameEntity(Entity a, Entity b, Level la, Level lb, Composite compA = null, Composite compB = null)
        {
            if (a.variant != b.variant) return false;
            if (a.parameters.Count != b.parameters.Count) return false;
            foreach (Parameter pa in a.parameters)
            {
                Parameter pb = b.GetParameter(pa.name);
                if (pb == null || !SameParameter(pa, la, pb, lb)) return false;
            }
            if (!Links(a, compA).SetEquals(Links(b, compB))) return false;
            switch (a)
            {
                case FunctionEntity fa:
                    {
                        FunctionEntity fb = (FunctionEntity)b;
                        if (fa.function != fb.function) return false;
                        if (ResourcesSignature(fa.resources, la) != ResourcesSignature(fb.resources, lb)) return false;
                        if (fa is TriggerSequence ta && SequenceSignature(ta.sequence, ta.methods) != SequenceSignature(((TriggerSequence)fb).sequence, ((TriggerSequence)fb).methods)) return false;
                        if (fa is CAGEAnimation ca && AnimationSignature(ca) != AnimationSignature((CAGEAnimation)fb)) return false;
                        return true;
                    }
                case VariableEntity va:
                    return va.name == ((VariableEntity)b).name && va.type == ((VariableEntity)b).type;
                case AliasEntity aa:
                    return aa.alias == ((AliasEntity)b).alias;
                case ProxyEntity pa:
                    {
                        ProxyEntity pb = (ProxyEntity)b;
                        return pa.proxy == pb.proxy && pa.function == pb.function && SequenceSignature(pa.sequence, pa.methods) == SequenceSignature(pb.sequence, pb.methods);
                    }
            }
            return true;
        }

        private bool SameParameter(Parameter a, Level la, Parameter b, Level lb)
        {
            if (a == null || b == null) return a == b;
            if (a.variant != b.variant) return false;
            ParameterData x = a.content, y = b.content;
            if (x == null || y == null) return x == y;
            if (x.dataType != y.dataType) return false;
            switch (x.dataType)
            {
                case DataType.RESOURCE:
                    return ResourcesSignature(((cResource)x).value, la) == ResourcesSignature(((cResource)y).value, lb);
                case DataType.SPLINE:
                    {
                        List<cTransform> sx = ((cSpline)x).splinePoints ?? new List<cTransform>(), sy = ((cSpline)y).splinePoints ?? new List<cTransform>();
                        if (sx.Count != sy.Count) return false;
                        for (int i = 0; i < sx.Count; i++)
                            if (sx[i].position != sy[i].position || sx[i].rotation != sy[i].rotation) return false;
                        return true;
                    }
                case DataType.FLOAT:
                    return ((cFloat)x).value.Equals(((cFloat)y).value);
                default:
                    return x == y;
            }
        }

        private static string Show(ParameterData data)
        {
            switch (data)
            {
                case cFloat f: return f.value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case cInteger i: return i.value.ToString();
                case cBool b: return b.value ? "true" : "false";
                case cString s: return "'" + s.value + "'";
                case cTransform t: return "a new position";
                case cVector3 v: return "a new vector";
                default: return "a new value";
            }
        }
        #endregion

        #region SIGNATURES
        /* What a set of resource references points at, in terms that mean the same in two separately loaded copies of
           a level: models by name and place within the model, materials by name, shader and textures. Transforms are
           left out - every save works them out from the entity again. */
        private readonly Dictionary<Level, Dictionary<object, string>> _submeshNames = new Dictionary<Level, Dictionary<object, string>>();

        private string ResourcesSignature(List<ResourceReference> references, Level level)
        {
            if (references == null || references.Count == 0) return "";
            StringBuilder sig = new StringBuilder();
            foreach (ResourceReference reference in references.OrderBy(o => (int)o.resource_type))
            {
                sig.Append((int)reference.resource_type).Append('{');
                switch (reference.resource_type)
                {
                    case ResourceType.RENDERABLE_INSTANCE:
                        foreach (RenderableElements.Element element in reference.RenderableInstance ?? new List<RenderableElements.Element>())
                            AppendElement(sig, element, level);
                        break;
                    case ResourceType.COLLISION_MAPPING:
                        if (reference.CollisionMapping != null)
                        {
                            CollisionMaps.COLLISION_MAPPING map = reference.CollisionMapping;
                            sig.Append(map.Flags).Append(',').Append(map.Material?.Name).Append(',').Append(map.MaterialMapping?.ToString()).Append(',').Append(map.CollisionProxy?.ProxyIndex);
                        }
                        break;
                    case ResourceType.DYNAMIC_PHYSICS_SYSTEM:
                        sig.Append(reference.PhysicsSystemIndex);
                        break;
                    case ResourceType.ANIMATED_MODEL:
                        sig.Append(reference.AnimatedModel?.ToString());
                        break;
                }
                sig.Append('}');
            }
            return sig.ToString();
        }

        private void AppendElement(StringBuilder sig, RenderableElements.Element element, Level level)
        {
            if (element == null) { sig.Append("null;"); return; }
            sig.Append(SubmeshName(element.Model, level)).Append('|');
            Materials.Material material = element.Material;
            if (material != null)
            {
                sig.Append(material.Name).Append('/').Append(material.Shader?.Ubershader).Append(':').Append(material.Shader?.UbershaderFeatureFlags);
                foreach (TexturePtr texture in material.TextureReferences)
                    sig.Append('/').Append(texture?.Texture?.Name);
            }
            if (element.LODs != null)
                foreach (RenderableElements.Element lod in element.LODs)
                {
                    sig.Append('<');
                    AppendElement(sig, lod, level);
                    sig.Append('>');
                }
            sig.Append(';');
        }

        private string SubmeshName(Models.CS2.Component.LOD.Submesh submesh, Level level)
        {
            if (submesh == null) return "";
            if (!_submeshNames.TryGetValue(level, out Dictionary<object, string> names))
            {
                names = new Dictionary<object, string>(new ReferenceEqualityComparer());
                foreach (Models.CS2 cs2 in level.Models?.Entries ?? new List<Models.CS2>())
                    for (int c = 0; c < cs2.Components.Count; c++)
                        for (int l = 0; l < cs2.Components[c].LODs.Count; l++)
                            for (int s = 0; s < cs2.Components[c].LODs[l].Submeshes.Count; s++)
                            {
                                object key = cs2.Components[c].LODs[l].Submeshes[s];
                                if (key != null && !names.ContainsKey(key))
                                    names[key] = cs2.Name + "#" + c + "." + l + "." + s;
                            }
                _submeshNames[level] = names;
            }
            return names.TryGetValue(submesh, out string name) ? name : "?" + submesh.VertexCount + "/" + submesh.IndexCount;
        }

        private static string SequenceSignature(List<TriggerSequence.SequenceEntry> sequence, List<TriggerSequence.MethodEntry> methods)
        {
            StringBuilder sig = new StringBuilder();
            foreach (TriggerSequence.SequenceEntry entry in sequence ?? new List<TriggerSequence.SequenceEntry>())
                sig.Append(entry.timing.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('@').Append(Path(entry.connectedEntity)).Append(';');
            sig.Append('|');
            foreach (TriggerSequence.MethodEntry method in methods ?? new List<TriggerSequence.MethodEntry>())
                sig.Append(method.method.ToByteString()).Append(',').Append(method.relay.ToByteString()).Append(',').Append(method.finished.ToByteString()).Append(';');
            return sig.ToString();
        }

        private static string AnimationSignature(CAGEAnimation animation)
        {
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            StringBuilder sig = new StringBuilder();
            foreach (CAGEAnimation.Connection c in animation.connections)
                sig.Append(c.binding_guid.ToByteString()).Append(c.target_track.ToByteString()).Append((int)c.binding_type).Append(c.target_param.ToByteString())
                   .Append((int)c.target_param_type).Append(c.target_sub_param.ToByteString()).Append(Path(c.connectedEntity)).Append(';');
            sig.Append('|');
            foreach (CAGEAnimation.FloatTrack t in animation.floatTracks)
            {
                sig.Append(t.shortGUID.ToByteString()).Append(':');
                foreach (CAGEAnimation.FloatTrack.Keyframe k in t.keyframes)
                    sig.Append((int)k.mode).Append(',').Append(k.time.ToString("R", inv)).Append(',').Append(k.value).Append(',').Append(k.tan_in).Append(',').Append(k.tan_out).Append(';');
            }
            sig.Append('|');
            foreach (CAGEAnimation.EventTrack t in animation.eventTracks)
            {
                sig.Append(t.shortGUID.ToByteString()).Append((int)t.track_type).Append(':');
                foreach (CAGEAnimation.EventTrack.Keyframe k in t.keyframes)
                    sig.Append(k.time.ToString("R", inv)).Append(',').Append(k.forward.ToByteString()).Append(',').Append(k.reverse.ToByteString()).Append(',').Append((int)k.track_type).Append(',').Append(k.duration.ToString("R", inv)).Append(';');
            }
            return sig.ToString();
        }

        private static string Path(EntityPath path)
        {
            return path == null ? "" : string.Join(".", path.path.Select(o => o.ToByteString()));
        }
        #endregion

        #region ASSETS
        /* A texture of the unmodified level that the copy replaced (same name, different pixels): replaced in the
           destination too, in place, so everything already using it sees the new one */
        private void MergeTextures()
        {
            if (_mod.Textures == null || _vanilla.Textures == null || _dest.Textures == null) return;
            foreach (Textures.TEX4 mt in _mod.Textures.Entries)
            {
                Textures.TEX4 vt = _vanilla.Textures.FindByName(mt.Name);
                if (vt == null || SameTexture(vt, mt)) continue;
                Textures.TEX4 dt = _dest.Textures.FindByName(mt.Name);
                Claim(Named("texture " + mt.Name), dt != null && !SameTexture(dt, vt) && !SameTexture(dt, mt));
                _dest.Textures.ImportEntry(mt, true);
            }
        }

        private static bool SameTexture(Textures.TEX4 a, Textures.TEX4 b)
        {
            return a.Format == b.Format && a.StateFlags == b.StateFlags && a.UsageFlags == b.UsageFlags
                && SamePart(a.TexturePersistent, b.TexturePersistent) && SamePart(a.TextureStreamed, b.TextureStreamed);
        }

        private static bool SamePart(Textures.TEX4.Texture a, Textures.TEX4.Texture b)
        {
            if (a == null || b == null) return a == b;
            if (a.Width != b.Width || a.Height != b.Height || a.Depth != b.Depth || a.MipLevels != b.MipLevels) return false;
            if (a.Content == null || b.Content == null) return a.Content == b.Content;
            return a.Content.AsSpan().SequenceEqual(b.Content);
        }

        /* A model of the unmodified level the copy reshaped (same name, different geometry): replaced in place */
        private void MergeModels()
        {
            if (_mod.Models == null || _vanilla.Models == null || _dest.Models == null) return;
            Dictionary<string, Models.CS2> vanilla = new Dictionary<string, Models.CS2>();
            foreach (Models.CS2 m in _vanilla.Models.Entries)
                if (m?.Name != null && !vanilla.ContainsKey(m.Name)) vanilla[m.Name] = m;
            foreach (Models.CS2 mm in _mod.Models.Entries)
            {
                if (mm?.Name == null || !vanilla.TryGetValue(mm.Name, out Models.CS2 vm) || SameGeometry(vm, mm)) continue;
                Models.CS2 dm = _dest.Models.Entries.FirstOrDefault(o => o.Name == mm.Name);
                Claim(Named("model " + mm.Name), dm != null && !SameGeometry(dm, vm) && !SameGeometry(dm, mm));
                _dest.Models.ImportEntry(mm, true);
            }
        }

        private static bool SameGeometry(Models.CS2 a, Models.CS2 b)
        {
            if (a.Components.Count != b.Components.Count) return false;
            for (int c = 0; c < a.Components.Count; c++)
            {
                if (a.Components[c].LODs.Count != b.Components[c].LODs.Count) return false;
                for (int l = 0; l < a.Components[c].LODs.Count; l++)
                {
                    List<Models.CS2.Component.LOD.Submesh> sa = a.Components[c].LODs[l].Submeshes, sb = b.Components[c].LODs[l].Submeshes;
                    if (sa.Count != sb.Count) return false;
                    for (int s = 0; s < sa.Count; s++)
                    {
                        if (sa[s].VertexCount != sb[s].VertexCount || sa[s].IndexCount != sb[s].IndexCount) return false;
                        if (!(sa[s].Data ?? new byte[0]).AsSpan().SequenceEqual(sb[s].Data ?? new byte[0])) return false;
                        if (sa[s].Material?.Name != sb[s].Material?.Name) return false;
                    }
                }
            }
            return true;
        }
        #endregion

        #region STANDALONE DATA
        /* Level files that stand on their own - the level's sound tables, galaxy, character accessories - taken whole
           from a copy that changed them. Compared as each would be written, so a re-save that changed nothing isn't
           taken for a change. */
        private void MergeStandaloneData()
        {
            Swap("the level's sound banks", l => l.SoundBankData, (l, o) => l.SoundBankData = (SoundBankData)o);
            Swap("the level's dialogue lookups", l => l.SoundDialogueLookups, (l, o) => l.SoundDialogueLookups = (SoundDialogueLookups)o);
            Swap("the level's sound environments", l => l.SoundEnvironmentData, (l, o) => l.SoundEnvironmentData = (SoundEnvironmentData)o);
            Swap("the level's sound events", l => l.SoundEventData, (l, o) => l.SoundEventData = (SoundEventData)o);
            Swap("the level's character accessories", l => l.AccessorySets, (l, o) => l.AccessorySets = (CharacterAccessorySets)o);
            Swap("the galaxy", l => l.GalaxyItems, (l, o) => l.GalaxyItems = (GalaxyItems)o);
            Swap("the galaxy definition", l => l.GalaxyDefinition, (l, o) => l.GalaxyDefinition = (GalaxyDefinition)o);
        }

        private void Swap(string what, Func<Level, CathodeFile> get, Action<Level, CathodeFile> set)
        {
            CathodeFile v = get(_vanilla), m = get(_mod), d = get(_dest);
            if (v == null || m == null || d == null) return;
            byte[] vb = Written(v), mb = Written(m);
            if (vb == null || mb == null || vb.AsSpan().SequenceEqual(mb)) return;
            byte[] db = Written(d);
            Claim(Named(what), db != null && !db.AsSpan().SequenceEqual(vb) && !db.AsSpan().SequenceEqual(mb));
            string destPath = d.Filepath;
            m.Save(destPath);
            set(_dest, m);
        }

        private static byte[] Written(CathodeFile file)
        {
            string temp = System.IO.Path.GetTempFileName();
            try
            {
                if (!file.Save(temp, false)) return null;
                return File.ReadAllBytes(temp);
            }
            catch
            {
                return null;
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        }
        #endregion
    }
}
#endif
