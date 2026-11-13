using System;
using System.Collections.Generic;
using System.Linq;
using CATHODE;
using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using CathodeLib.ObjectExtensions;
using static CATHODE.HavokPackfile;
using ReferenceEqualityComparer = CathodeLib.ObjectExtensions.ReferenceEqualityComparer; // net5+ also has System.Collections.Generic.ReferenceEqualityComparer

namespace CathodeLib.Havok
{
    /* Collision garbage collection for COLLISION.HKX / HKX64: drops the proxies nothing uses any more and reclaims the dead bytes
       edits leave (old copies of moved arrays, freed boxes and meshes), renumbering every reference.

       A proxy (hkpStaticCompoundShape) is known by its ORDINAL, its rank among the file's compounds in object order; the proxy list
       (the hkpListShape on the first rigid body) holds compound i in slot i. Dropping one renumbers every compound after it, so every
       holder of an ordinal (COLLISION.MAP rows, model submeshes, script resources) is renumbered in the same step. The payload is
       rewritten as retail lays it out - each live object followed by its own arrays, every block at the same offset modulo 16 - then
       read back and checked proxy by proxy, or put back as it was. PC packfiles only: a tagfile numbers proxies by list slot, hosts
       first, and is refused. */
    /// <summary>
    /// Prune unreferenced collision proxies and reclaim dead payload in a level's collision packfiles, renumbering every reference.
    /// </summary>
    public static class CollisionCompactor
    {
        #region Public API
        /// <summary>Raised after a level's collision was compacted (not on a dry run): whatever holds proxy ordinals of its own renumbers them by <see cref="LevelCompaction.Remap"/>.</summary>
        public static event Action<Level, LevelCompaction> Compacted;

        /// <summary>
        /// Set by an editor: the proxies something outside the level's files still holds - an undo history that can bring back an entity,
        /// a row or a brush's earlier collision naming them. Every compaction keeps them. Returning null means it cannot tell, and then
        /// nothing is pruned (dead payload is still reclaimed).
        /// </summary>
        public static Func<Level, ICollection<StaticCompoundShape>> HeldElsewhere;

        public sealed class CompactionOptions
        {
            /// <summary>Report only; change nothing (the default).</summary>
            public bool DryRun = true;
            /// <summary>Also prune unreferenced proxies the level shipped with (before its world hosts). Off by default: only what an editor added after the hosts goes.</summary>
            public bool IncludeShippedProxies = false;
            /// <summary>Prune nothing: only reclaim dead payload (moved arrays, unreferenced boxes and meshes).</summary>
            public bool ReclaimOnly = false;
            /// <summary>Ordinals that stay whatever else refers to them (an editor's own records, an undo history).</summary>
            public ICollection<int> Keep = null;
        }

        /// <summary>What a compaction of one packfile did, or would do.</summary>
        public sealed class PackfileCompaction
        {
            public string File = "";
            public bool Changed;
            public int CompoundsBefore, CompoundsAfter;
            public int ObjectsBefore, ObjectsAfter;
            public long PayloadBefore, PayloadAfter;
            public long FileBefore, FileAfter;
            public int ListSlotsBefore, ListSlotsAfter;
            /// <summary>Bytes no live block uses: the tails trimmed off typed blocks (old copies of moved arrays) and unreachable blocks.</summary>
            public long DeadTailBytes, UnreachableBytes;
            public int DroppedCompounds, DroppedMeshes, DroppedBoxes, DroppedOther;
            /// <summary>Old ordinal to new, -1 for a dropped compound.</summary>
            public int[] Remap = new int[0];
            public List<string> Notes = new List<string>();
            public override string ToString() =>
                File + ": compounds " + CompoundsBefore + " -> " + CompoundsAfter + ", objects " + ObjectsBefore + " -> " + ObjectsAfter +
                ", payload " + PayloadBefore + " -> " + PayloadAfter + ", file " + FileBefore + " -> " + FileAfter +
                " (dead tails " + DeadTailBytes + ", unreachable " + UnreachableBytes + "; dropped " + DroppedCompounds + " compound(s), " +
                DroppedMeshes + " mesh(es), " + DroppedBoxes + " box(es)" + (DroppedOther != 0 ? ", " + DroppedOther + " other" : "") + ")" +
                (Changed ? "" : " - unchanged");
        }

        /// <summary>Who refers to each proxy of a level, by ordinal.</summary>
        public sealed class ProxyReferences
        {
            [Flags]
            public enum Holder
            {
                None = 0,
                Host = 1,           //a world host itself (a rigid body's shape): always kept
                Row = 2,            //a COLLISION.MAP row
                Resource = 4,       //a COLLISION_MAPPING resource in the script (its row may not be in the table until the next build)
                Placed = 8,         //an instance of a kept compound places it (the hosts' instances are the placed collision)
                Submesh = 16,       //a model submesh's CollisionProxyIndex
                Keep = 32,          //asked to be kept
                OtherPointer = 64,  //an object other than the list or a compound's instance points at it
                Shipped = 128,      //before the world hosts, and shipped proxies are not being pruned
            }
            public int Count;
            public Holder[] Holders = new Holder[0];
            public int[] HostOrdinals = new int[0];
            public int FirstAppended = -1;
            /// <summary>The editor could not say what its own history holds (<see cref="HeldElsewhere"/> gave null): nothing may be pruned.</summary>
            public bool HoldAll;
            /// <summary>
            /// Rows (or script resources' rows) holding a copy of their compound from an older reading of the packfile - not the listed
            /// object, but at the same payload offset and ordinal - with the listed compound: they count as references to it, and a
            /// compaction rebinds them to it before it renumbers anything.
            /// </summary>
            public List<KeyValuePair<CollisionMaps.COLLISION_MAPPING, StaticCompoundShape>> Stale = new List<KeyValuePair<CollisionMaps.COLLISION_MAPPING, StaticCompoundShape>>();
            /// <summary>Rows naming a compound that is not in the list and cannot be followed to one: nothing is compacted while there are any.</summary>
            public int Unresolved;
            public List<string> Notes = new List<string>();
            public bool IsReferenced(int ordinal) => ordinal >= 0 && ordinal < Holders.Length && Holders[ordinal] != Holder.None;
        }

        /// <summary>What a compaction of a level did, or would do.</summary>
        public sealed class LevelCompaction
        {
            public bool DryRun;
            public string Refused;
            public ProxyReferences References;
            /// <summary>Ordinals pruned (old numbering).</summary>
            public List<int> Pruned = new List<int>();
            /// <summary>Old ordinal to new, -1 for a pruned proxy; identity when nothing was pruned.</summary>
            public int[] Remap = new int[0];
            public List<PackfileCompaction> Files = new List<PackfileCompaction>();
            public int SubmeshesRenumbered, HistoryEntriesRenumbered;
            public List<string> Notes = new List<string>();
            public bool Changed => Files.Any(o => o.Changed);
            public override string ToString()
            {
                if (Refused != null) return "collision compaction refused: " + Refused;
                string head = (DryRun ? "[dry run] " : "") + "proxies " + (References?.Count ?? 0) + ", pruned " + Pruned.Count +
                    (Pruned.Count != 0 ? " (" + Ranges(Pruned) + ")" : "");
                return head + Environment.NewLine + string.Join(Environment.NewLine, Files.Select(o => "  " + o)) +
                    (SubmeshesRenumbered + HistoryEntriesRenumbered != 0 ? Environment.NewLine + "  renumbered " + SubmeshesRenumbered + " submesh(es), " + HistoryEntriesRenumbered + " edit-history entr(ies)" : "") +
                    (Notes.Count != 0 ? Environment.NewLine + string.Join(Environment.NewLine, Notes.Select(o => "  note: " + o)) : "");
            }
        }

        /// <summary>
        /// Compact one packfile: drop <paramref name="drop"/> (template compounds nothing but the proxy list points at) and whatever only they
        /// used, reclaim dead payload, renumber the remaining compounds. With <paramref name="apply"/> false nothing changes and the result
        /// says what would. Throws (leaving the packfile as it was) when the file is not one this understands or the result does not check.
        /// </summary>
        public static PackfileCompaction Compact(this HavokPackfile packfile, ICollection<StaticCompoundShape> drop, bool apply)
        {
            Planner plan = new Planner(packfile, drop ?? new StaticCompoundShape[0]);
            plan.Run();
            if (!apply || !plan.Result.Changed)
                return plan.Result;
            Applied applied = plan.Apply();
            try { applied.Verify(); }
            catch { applied.Rollback(); throw; }
            return plan.Result;
        }

        /// <summary>Every reference to every proxy of the level, from its rows, script resources, placements and models.</summary>
        public static ProxyReferences FindProxyReferences(Level level, CompactionOptions options = null)
        {
            options = options ?? new CompactionOptions();
            HavokPackfile lead = Lead(level), mirror = Mirror(level);
            ProxyReferences refs = new ProxyReferences();
            if (lead == null)
                return refs;
            int count = lead.StaticCompoundShapes.Count;
            refs.Count = count;
            refs.Holders = new ProxyReferences.Holder[count];
            Dictionary<StaticCompoundShape, int> ordinalOf = new Dictionary<StaticCompoundShape, int>();
            for (int i = 0; i < count; i++)
                ordinalOf[lead.StaticCompoundShapes[i]] = lead.StaticCompoundShapes[i].ProxyIndex;
            void Mark(int ordinal, ProxyReferences.Holder holder) { if (ordinal >= 0 && ordinal < count) refs.Holders[ordinal] |= holder; }
            //A row's proxy is the listed compound object - or a copy of it from an older reading of the packfile, found by its payload
            //offset and ordinal (ProxyReferences.Stale). One that is neither cannot be renumbered, and stops a compaction (Unresolved).
            Dictionary<uint, StaticCompoundShape> listedAt = new Dictionary<uint, StaticCompoundShape>();
            foreach (StaticCompoundShape compound in lead.StaticCompoundShapes)
                if (compound != null && !listedAt.ContainsKey(compound.DataOffset)) listedAt[compound.DataOffset] = compound;
            HashSet<object> staleRows = new HashSet<object>(new ReferenceEqualityComparer());
            HashSet<object> unresolvedRows = new HashSet<object>(new ReferenceEqualityComparer());
            void MarkProxy(CollisionMaps.COLLISION_MAPPING row, ProxyReferences.Holder holder)
            {
                StaticCompoundShape proxy = row?.CollisionProxy;
                if (proxy == null) return;
                if (ordinalOf.TryGetValue(proxy, out int ordinal)) { Mark(ordinal, holder); return; }
                if (proxy.ProxyIndex < 0) return;   //a proxy an earlier compaction removed (an undo brought its entity back): no collision
                if (listedAt.TryGetValue(proxy.DataOffset, out StaticCompoundShape listed) && listed.ProxyIndex == proxy.ProxyIndex)
                {
                    Mark(listed.ProxyIndex, holder);
                    if (staleRows.Add(row)) refs.Stale.Add(new KeyValuePair<CollisionMaps.COLLISION_MAPPING, StaticCompoundShape>(row, listed));
                    return;
                }
                if (unresolvedRows.Add(row)) refs.Unresolved++;
            }

            //The world hosts, and what comes after them
            List<int> hosts = lead.StaticCompoundShapes.Where(o => lead.IsWorldHost(o)).Select(o => o.ProxyIndex).OrderBy(o => o).ToList();
            refs.HostOrdinals = hosts.ToArray();
            foreach (int host in hosts) Mark(host, ProxyReferences.Holder.Host);
            refs.FirstAppended = hosts.Count == 0 ? count : hosts.Max() + 1;
            if (!options.IncludeShippedProxies)
                for (int i = 0; i < Math.Min(count, hosts.Count == 0 ? count : hosts.Min()); i++)
                    Mark(i, ProxyReferences.Holder.Shipped);

            //COLLISION.MAP rows, and the script's collision resources (a row made since the last build is only there)
            foreach ((CollisionMaps.COLLISION_MAPPING row, ProxyReferences.Holder holder) in RowsHoldingProxies(level))
                MarkProxy(row, holder);
            if (refs.Stale.Count != 0)
                refs.Notes.Add(refs.Stale.Count + " row(s) hold a copy of their proxy from an older reading of " + Name(lead) + " (same offset and ordinal): counted, and rebound to the listed proxy before a compaction");
            if (refs.Unresolved != 0)
                refs.Notes.Add(refs.Unresolved + " row(s) name a proxy that is not in " + Name(lead) + " and cannot be followed to one: nothing can be compacted");

            //Models
            if (level.Models?.Entries != null)
                foreach (Models.CS2 model in level.Models.Entries)
                    foreach (Models.CS2.Component component in model?.Components ?? new List<Models.CS2.Component>())
                        foreach (Models.CS2.Component.LOD lod in component?.LODs ?? new List<Models.CS2.Component.LOD>())
                            foreach (Models.CS2.Component.LOD.Submesh submesh in lod?.Submeshes ?? new List<Models.CS2.Component.LOD.Submesh>())
                                if (submesh != null && submesh.CollisionProxyIndex >= 0)
                                    Mark(submesh.CollisionProxyIndex, ProxyReferences.Holder.Submesh);

            if (options.Keep != null)
                foreach (int ordinal in options.Keep)
                    Mark(ordinal, ProxyReferences.Holder.Keep);

            //What the editor still holds (its undo history): kept, or nothing pruned when it cannot say
            Func<Level, ICollection<StaticCompoundShape>> held = HeldElsewhere;
            if (held != null)
            {
                ICollection<StaticCompoundShape> proxies = null;
                try { proxies = held(level); }
                catch (Exception e) { refs.Notes.Add("the editor could not list the proxies its history holds (" + e.Message + ")"); }
                if (proxies == null)
                    refs.HoldAll = true;
                else
                    foreach (StaticCompoundShape proxy in proxies)
                    {
                        if (proxy == null) continue;
                        if (ordinalOf.TryGetValue(proxy, out int heldOrdinal))
                            Mark(heldOrdinal, ProxyReferences.Holder.Keep);
                        else if (proxy.ProxyIndex >= 0 && listedAt.TryGetValue(proxy.DataOffset, out StaticCompoundShape listed) && listed.ProxyIndex == proxy.ProxyIndex)
                            Mark(listed.ProxyIndex, ProxyReferences.Holder.Keep);   //an older reading's copy of it
                    }
            }

            //Pointers to compounds from anything but the list and the compounds' own instances keep them (in either width)
            foreach (HavokPackfile hkx in new[] { lead, mirror })
            {
                if (hkx == null || hkx.IsTagfile) continue;
                foreach (int ordinal in OtherPointerTargets(hkx))
                    Mark(ordinal, ProxyReferences.Holder.OtherPointer);
            }

            //A kept compound keeps what its instances place, in either width
            foreach (HavokPackfile hkx in new[] { lead, mirror })
            {
                if (hkx == null) continue;
                Dictionary<uint, StaticCompoundShape> byOffset = new Dictionary<uint, StaticCompoundShape>();
                foreach (StaticCompoundShape compound in hkx.StaticCompoundShapes)
                    if (!byOffset.ContainsKey(compound.DataOffset)) byOffset[compound.DataOffset] = compound;
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    foreach (StaticCompoundShape compound in hkx.StaticCompoundShapes)
                    {
                        if (compound.ProxyIndex < 0 || compound.ProxyIndex >= count || refs.Holders[compound.ProxyIndex] == ProxyReferences.Holder.None)
                            continue;
                        foreach (CompoundInstance instance in compound.Instances)
                            if (byOffset.TryGetValue(instance.ShapeDataOffset, out StaticCompoundShape child) && child.ProxyIndex >= 0 && child.ProxyIndex < count
                                && (refs.Holders[child.ProxyIndex] & ProxyReferences.Holder.Placed) == 0)
                            {
                                refs.Holders[child.ProxyIndex] |= ProxyReferences.Holder.Placed;
                                grew = true;
                            }
                    }
                }
            }
            return refs;
        }

        /// <summary>
        /// Prune the level's unreferenced proxies (what an editor added after the world hosts, unless <see cref="CompactionOptions.IncludeShippedProxies"/>)
        /// and reclaim dead payload, in both widths at once, then renumber the model submeshes and edit history that hold ordinals. A dry
        /// run (the default) only reports. Never half-done: a width that does not check puts both back.
        /// </summary>
        public static LevelCompaction CompactCollision(this Level level, CompactionOptions options = null)
        {
            options = options ?? new CompactionOptions();
            LevelCompaction report = new LevelCompaction { DryRun = options.DryRun };
            HavokPackfile lead = Lead(level), mirror = Mirror(level);
            if (lead == null) { report.Refused = "the level has no collision packfile loaded"; return report; }
            if (lead.IsTagfile || (mirror != null && mirror.IsTagfile)) { report.Refused = "a Havok tagfile (mobile / Switch) - only PC packfiles are compacted"; return report; }
            if (mirror != null && mirror.StaticCompoundShapes.Count != lead.StaticCompoundShapes.Count)
            {
                report.Refused = "COLLISION.HKX and HKX64 hold different numbers of compounds (" + lead.StaticCompoundShapes.Count + " / " + mirror.StaticCompoundShapes.Count + ")";
                return report;
            }
            if (lead.InstanceRebuildPending || (mirror != null && mirror.InstanceRebuildPending)) { report.Refused = "an instance rebuild is under way"; return report; }

            ProxyReferences refs = FindProxyReferences(level, options);
            report.References = refs;
            report.Notes.AddRange(refs.Notes);
            if (refs.Unresolved != 0)
            {
                //Its ordinal could not be renumbered: the save would write a number that names another proxy by then
                report.Refused = refs.Unresolved + " row(s) name a collision proxy that is not in " + Name(lead) + " and cannot be followed to one";
                return report;
            }
            int count = refs.Count;
            if (mirror != null)
            {
                int[] mirrorHosts = mirror.StaticCompoundShapes.Where(o => mirror.IsWorldHost(o)).Select(o => o.ProxyIndex).OrderBy(o => o).ToArray();
                if (!mirrorHosts.SequenceEqual(refs.HostOrdinals))
                {
                    report.Refused = "the two widths' world hosts are at different ordinals (" + string.Join(",", refs.HostOrdinals) + " / " + string.Join(",", mirrorHosts) + ")";
                    return report;
                }
            }

            List<int> pruned = new List<int>();
            if (refs.HoldAll && !options.ReclaimOnly)
                report.Notes.Add("nothing pruned: the editor could not say which proxies its undo history holds (dead payload is still reclaimed)");
            if (!options.ReclaimOnly && !refs.HoldAll)
                for (int i = 0; i < count; i++)
                    if (refs.Holders[i] == ProxyReferences.Holder.None)
                        pruned.Add(i);
            report.Pruned = pruned;

            HashSet<int> prunedSet = new HashSet<int>(pruned);
            List<StaticCompoundShape> DropIn(HavokPackfile hkx) => hkx.StaticCompoundShapes.Where(o => prunedSet.Contains(o.ProxyIndex)).ToList();

            Planner leadPlan = new Planner(lead, DropIn(lead));
            leadPlan.Run();
            Planner mirrorPlan = null;
            if (mirror != null)
            {
                mirrorPlan = new Planner(mirror, DropIn(mirror));
                mirrorPlan.Run();
                if (!leadPlan.Result.Remap.SequenceEqual(mirrorPlan.Result.Remap))
                {
                    report.Refused = "the two widths would renumber their proxies differently";
                    return report;
                }
            }
            report.Files.Add(leadPlan.Result);
            if (mirrorPlan != null) report.Files.Add(mirrorPlan.Result);
            report.Remap = leadPlan.Result.Remap;
            if (options.DryRun || !report.Changed)
                return report;

            //Rows holding an older reading's copy of their proxy follow the listed one from here, so the renumbering reaches them
            foreach (KeyValuePair<CollisionMaps.COLLISION_MAPPING, StaticCompoundShape> stale in refs.Stale)
                stale.Key.CollisionProxy = stale.Value;
            if (refs.Stale.Count != 0)
                report.Notes.Add("rebound " + refs.Stale.Count + " row(s) to the listed proxy their copy stood for");

            //Both widths, or neither
            Applied leadApplied = leadPlan.Apply();
            try { leadApplied.Verify(); }
            catch { leadApplied.Rollback(); throw; }
            Applied mirrorApplied = null;
            if (mirrorPlan != null && mirrorPlan.Result.Changed)
            {
                try { mirrorApplied = mirrorPlan.Apply(); }
                catch { leadApplied.Rollback(); throw; }
                try { mirrorApplied.Verify(); }
                catch { mirrorApplied.Rollback(); leadApplied.Rollback(); throw; }
            }

            //Every row must now name a listed compound, whose ordinal is the one the save writes - else none of it is kept
            int offList = RowsOffTheList(level, lead);
            if (offList != 0)
            {
                mirrorApplied?.Rollback();
                leadApplied.Rollback();
                report.Refused = offList + " row(s) would name a proxy that is not in " + Name(lead) + " after the compaction - both files put back as they were";
                return report;
            }

            //What holds an ordinal outside the packfiles: the rows hold the compound objects, which kept their identity
            int[] remap = report.Remap;
            bool renumbered = pruned.Count != 0 && remap.Select((n, o) => n != o).Any(o => o);
            if (renumbered)
            {
                if (level.Models?.Entries != null)
                    foreach (Models.CS2 model in level.Models.Entries)
                        foreach (Models.CS2.Component component in model?.Components ?? new List<Models.CS2.Component>())
                            foreach (Models.CS2.Component.LOD lod in component?.LODs ?? new List<Models.CS2.Component.LOD>())
                                foreach (Models.CS2.Component.LOD.Submesh submesh in lod?.Submeshes ?? new List<Models.CS2.Component.LOD.Submesh>())
                                {
                                    if (submesh == null || submesh.CollisionProxyIndex < 0 || submesh.CollisionProxyIndex >= remap.Length) continue;
                                    int to = remap[submesh.CollisionProxyIndex];
                                    if (to < 0 || to == submesh.CollisionProxyIndex) continue;
                                    submesh.CollisionProxyIndex = to;
                                    report.SubmeshesRenumbered++;
                                }
                IReadOnlyList<AssetModificationInfoTable.ModificationInfo> history = level.Commands?.Utils?.AllAssetModificationInfo;
                if (history != null)
                    foreach (AssetModificationInfoTable.ModificationInfo entry in history)
                    {
                        if (entry == null || entry.kind != AssetModificationInfoTable.AssetKind.COLLISION || !int.TryParse(entry.name, out int was) || was < 0 || was >= remap.Length)
                            continue;
                        string now = remap[was] >= 0 ? remap[was].ToString() : "removed (was " + was + ")";
                        if (now == entry.name) continue;
                        entry.name = now;
                        report.HistoryEntriesRenumbered++;
                    }
            }
            Console.WriteLine("Collision compaction: " + report.ToString().Replace(Environment.NewLine, Environment.NewLine + "  "));
            Compacted?.Invoke(level, report);
            return report;
        }
        #endregion

        #region Helpers
        static HavokPackfile Lead(Level level)
        {
            if (level?.CollisionHKX != null && level.CollisionHKX.Loaded) return level.CollisionHKX;
            return level?.CollisionHKX64 != null && level.CollisionHKX64.Loaded ? level.CollisionHKX64 : null;
        }

        static HavokPackfile Mirror(Level level)
        {
            HavokPackfile lead = Lead(level);
            return lead != null && lead == level.CollisionHKX && level.CollisionHKX64 != null && level.CollisionHKX64.Loaded ? level.CollisionHKX64 : null;
        }

        static string Name(HavokPackfile hkx) => hkx == null ? "?" : hkx.Header.PointerSize == 8 ? "COLLISION.HKX64" : "COLLISION.HKX";

        /// <summary>Every COLLISION.MAP row, then every row a script COLLISION_MAPPING resource holds (a row made since the last build is only there).</summary>
        static IEnumerable<(CollisionMaps.COLLISION_MAPPING row, ProxyReferences.Holder holder)> RowsHoldingProxies(Level level)
        {
            if (level.CollisionMaps?.Entries != null)
                foreach (CollisionMaps.COLLISION_MAPPING row in level.CollisionMaps.Entries)
                    if (row != null) yield return (row, ProxyReferences.Holder.Row);
            if (level.Commands?.Entries == null)
                yield break;
            foreach (Composite composite in level.Commands.Entries)
            {
                if (composite == null) continue;
                foreach (Entity entity in composite.GetEntities())
                {
                    if (entity is FunctionEntity function)
                        foreach (ResourceReference reference in function.resources)
                            if (reference?.CollisionMapping != null) yield return (reference.CollisionMapping, ProxyReferences.Holder.Resource);
                    foreach (Parameter parameter in entity.parameters)
                        if (parameter?.content is cResource resource && resource.value != null)
                            foreach (ResourceReference reference in resource.value)
                                if (reference?.CollisionMapping != null) yield return (reference.CollisionMapping, ProxyReferences.Holder.Resource);
                }
            }
        }

        /// <summary>Rows (and resources' rows) naming a proxy that is not a listed compound object - what a save would write a wrong ordinal for.</summary>
        static int RowsOffTheList(Level level, HavokPackfile lead)
        {
            HashSet<object> listed = new HashSet<object>(lead.StaticCompoundShapes, new ReferenceEqualityComparer());
            int off = 0;
            foreach ((CollisionMaps.COLLISION_MAPPING row, ProxyReferences.Holder _) in RowsHoldingProxies(level))
                if (row.CollisionProxy != null && row.CollisionProxy.ProxyIndex >= 0 && !listed.Contains(row.CollisionProxy))
                    off++;
            return off;
        }

        static string Ranges(List<int> values)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < values.Count;)
            {
                int j = i;
                while (j + 1 < values.Count && values[j + 1] == values[j] + 1) j++;
                parts.Add(i == j ? values[i].ToString() : values[i] + "-" + values[j]);
                i = j + 1;
            }
            return parts.Count > 12 ? string.Join(",", parts.Take(12)) + ",..." : string.Join(",", parts);
        }

        static uint AlignUp(uint value, uint to) => (value + to - 1) & ~(to - 1);

        /// <summary>Ordinals of compounds a global fixup points at from anywhere but the proxy list's slots and the compounds' own instance records.</summary>
        static IEnumerable<int> OtherPointerTargets(HavokPackfile hkx)
        {
            Dictionary<uint, int> ordinalAt = new Dictionary<uint, int>();
            foreach (StaticCompoundShape compound in hkx.StaticCompoundShapes)
                if (!ordinalAt.ContainsKey(compound.DataOffset)) ordinalAt[compound.DataOffset] = compound.ProxyIndex;
            //An instance record's shape pointer follows its translation, rotation and scale (48 bytes, both widths)
            HashSet<uint> instanceShapeFields = new HashSet<uint>();
            foreach (StaticCompoundShape compound in hkx.StaticCompoundShapes)
                foreach (CompoundInstance instance in compound.Instances)
                    instanceShapeFields.Add(instance.DataOffset + 48u);
            //A rigid body's collidable shape (the hosts, kept as hosts)
            uint bodyShape = (uint)(hkx.Header.PointerSize == 8 ? 32 : 16);
            HashSet<uint> bodyShapeFields = new HashSet<uint>(hkx.Objects.Where(o => o.ClassName == "hkpRigidBody").Select(o => o.DataOffset + bodyShape));
            ProxyList list = ProxyList.Read(hkx);
            HashSet<int> found = new HashSet<int>();
            foreach (GlobalFixup fixup in hkx.GlobalFixups)
            {
                if (!ordinalAt.TryGetValue(fixup.Dst, out int ordinal)) continue;
                if (instanceShapeFields.Contains(fixup.Src) || bodyShapeFields.Contains(fixup.Src)) continue;
                if (list != null && list.IsSlot(fixup.Src)) continue;
                found.Add(ordinal);
            }
            return found;
        }

        /// <summary>The proxy list: the hkpListShape a rigid body carries, its childInfo array and its slots.</summary>
        sealed class ProxyList
        {
            public PackfileObject Object;
            public uint Field;          //the childInfo hkArray field
            public uint Children;       //where its slots are (0 when it has no local fixup)
            public int Count;
            public uint CapacityAndFlags;
            public int Stride;
            public bool HasChildren;

            public bool IsSlot(uint src) => HasChildren && src >= Children && src < Children + (uint)(Count * Stride) && (src - Children) % (uint)Stride == 0;

            public static ProxyList Read(HavokPackfile hkx)
            {
                int ptr = hkx.Header.PointerSize;
                Dictionary<uint, PackfileObject> objAt = new Dictionary<uint, PackfileObject>();
                foreach (PackfileObject o in hkx.Objects) if (!objAt.ContainsKey(o.DataOffset)) objAt[o.DataOffset] = o;
                uint bodyShape = (uint)(ptr == 8 ? 32 : 16);
                PackfileObject list = null;
                foreach (PackfileObject body in hkx.Objects.Where(o => o.ClassName == "hkpRigidBody").OrderBy(o => o.DataOffset))
                {
                    foreach (GlobalFixup fixup in hkx.GlobalFixups)
                        if (fixup.Src == body.DataOffset + bodyShape && objAt.TryGetValue(fixup.Dst, out PackfileObject shape) && shape.ClassName == "hkpListShape")
                        { list = shape; break; }
                    if (list != null) break;
                }
                if (list == null) return null;
                ProxyList result = new ProxyList { Object = list, Field = list.DataOffset + (uint)(ptr == 8 ? 0x30 : 0x18), Stride = ptr == 8 ? 32 : 16 };
                if (result.Field + ptr + 8 > hkx.DataPayload.Length) return null;
                result.Count = BitConverter.ToInt32(hkx.DataPayload, (int)result.Field + ptr);
                result.CapacityAndFlags = BitConverter.ToUInt32(hkx.DataPayload, (int)result.Field + ptr + 4);
                foreach (LocalFixup fixup in hkx.LocalFixups)
                    if (fixup.Src == result.Field) { result.Children = fixup.Dst; result.HasChildren = true; break; }
                return result;
            }
        }
        #endregion

        #region Layout knowledge
        enum ArrayKind { Plain, MeshSections, ListChildren }

        sealed class ArrayField
        {
            public int Field32, Field64, Stride32, Stride64;
            public ArrayKind Kind;
            public ArrayField(int field32, int field64, int stride32, int stride64, ArrayKind kind = ArrayKind.Plain)
            { Field32 = field32; Field64 = field64; Stride32 = stride32; Stride64 = stride64; Kind = kind; }
        }

        sealed class ClassLayout
        {
            public int Size32, Size64;
            public ArrayField[] Arrays;
        }

        /* The 2012 packfile classes a level's collision is made of: object sizes and array element strides, as every retail PC level has
           them (one block length per class and width, every typed array exactly count x stride rounded up to 16). A file
           whose blocks do not fit these is refused rather than trimmed. */
        static readonly Dictionary<string, ClassLayout> Layouts = new Dictionary<string, ClassLayout>
        {
            ["hkpStaticCompoundShape"] = new ClassLayout { Size32 = 112, Size64 = 160, Arrays = new[] {
                new ArrayField(32, 56, 64, 80),         //instances
                new ArrayField(64, 112, 6, 6) } },      //Storage6 tree nodes
            ["hkpBvCompressedMeshShape"] = new ClassLayout { Size32 = 224, Size64 = 272, Arrays = new[] {
                new ArrayField(80, 112, 5, 5),          //tree nodes over sections (Codec3Axis5)
                new ArrayField(140, 176, 96, 96, ArrayKind.MeshSections),
                new ArrayField(152, 192, 4, 4),         //primitives
                new ArrayField(164, 208, 2, 2),         //sharedVerticesIndex
                new ArrayField(176, 224, 4, 4),         //packedVertices
                new ArrayField(188, 240, 8, 8),         //sharedVertices
                new ArrayField(200, 256, 8, 8) } },     //primitiveDataRuns
            ["hkpListShape"] = new ClassLayout { Size32 = 112, Size64 = 144, Arrays = new[] {
                new ArrayField(24, 48, 16, 32, ArrayKind.ListChildren) } },
            ["hkpBoxShape"] = new ClassLayout { Size32 = 48, Size64 = 64, Arrays = new ArrayField[0] },
        };

        /// <summary>A section record's own node array (Codec3Axis4, 4 bytes a node) is its first field.</summary>
        const int SectionStride = 96, SectionNodeStride = 4;

        /// <summary>Objects of these classes live only while something reaches them; every other object is kept whatever points at it.</summary>
        static bool Collectable(string className) =>
            className == "hkpStaticCompoundShape" || className == "hkpBvCompressedMeshShape" || className == "hkpBoxShape";
        #endregion

        #region Planner
        sealed class Block
        {
            public int Index;
            public uint Start, End;          //[Start, End): to the next boundary
            public PackfileObject Object;    //set on an object's block
            public ClassLayout Layout;
            public bool Untyped;             //some array pointing here has no known stride
            public long TypedLength = -1;    //count x stride, the most any typed array pointing here needs
            public ArrayKind Kind = ArrayKind.Plain;
            public uint KeepEnd;             //end of what is kept
            public bool Live;
            public int Owner = -1;
            public uint NewStart;
            public bool Placed;
            public bool IsObject => Object != null;
        }

        sealed class Planner
        {
            readonly HavokPackfile _hk;
            readonly HashSet<StaticCompoundShape> _drop;
            byte[] _d;
            uint _len;
            int _ptr;
            uint[] _bounds;
            Block[] _blocks;
            Dictionary<uint, PackfileObject> _objAt;
            List<LocalFixup>[] _localBySrc;
            List<GlobalFixup>[] _globalBySrc;
            ProxyList _list;
            int _listChildrenBlock = -1;
            List<StaticCompoundShape> _compounds;      //by old ordinal
            List<StaticCompoundShape> _survivors;      //by new ordinal
            Dictionary<StaticCompoundShape, int> _oldOrdinal;
            int _newListCount;
            byte[] _payload;
            List<LocalFixup> _local;
            List<GlobalFixup> _global;
            List<VirtualFixup> _virtual;
            uint _newListChildren;
            uint _newLength;
            byte[] _before;                            //the file as it stood, for Verify

            public readonly PackfileCompaction Result = new PackfileCompaction();

            public Planner(HavokPackfile hk, ICollection<StaticCompoundShape> drop)
            {
                _hk = hk;
                _drop = new HashSet<StaticCompoundShape>(drop);
            }

            static InvalidOperationException Refuse(string why) => new InvalidOperationException("Collision compaction refused: " + why);

            public void Run()
            {
                if (_hk == null || !_hk.Loaded) throw Refuse("no packfile loaded");
                if (_hk.IsTagfile) throw Refuse("a Havok tagfile (mobile / Switch)");
                if (!(_hk.Header.ContentsVersion ?? "").StartsWith("hk_2012")) throw Refuse("contents version " + _hk.Header.ContentsVersion + " is not hk_2012");
                if (_hk.InstanceRebuildPending) throw Refuse("an instance rebuild is under way");
                Result.File = Name(_hk);

                //The file as a save would write it now: the instances' in-memory fields go into the payload first
                _before = _hk.ToBytes();
                _d = _hk.DataPayload;
                _len = (uint)_d.Length;
                _ptr = _hk.Header.PointerSize;
                Result.PayloadBefore = _len;
                Result.FileBefore = _before.Length;
                Result.ObjectsBefore = _hk.Objects.Count;

                CheckOrdinals();
                BuildBlocks();
                TypeBlocks();
                FindList();
                ChooseSurvivors();
                Mark();
                Order();
                Emit();
                Fixups();
                Measure();
            }

            void CheckOrdinals()
            {
                _compounds = _hk.StaticCompoundShapes.ToList();
                _oldOrdinal = new Dictionary<StaticCompoundShape, int>();
                for (int i = 0; i < _compounds.Count; i++)
                {
                    if (_compounds[i].ProxyIndex != i) throw Refuse("compound " + i + " carries ordinal " + _compounds[i].ProxyIndex);
                    if (i > 0 && _compounds[i].DataOffset <= _compounds[i - 1].DataOffset) throw Refuse("compound order is not payload order at " + i);
                    _oldOrdinal[_compounds[i]] = i;
                }
                foreach (StaticCompoundShape compound in _drop)
                {
                    if (!_oldOrdinal.ContainsKey(compound)) throw Refuse("a compound to drop is not in " + Name(_hk));
                    if (_hk.IsWorldHost(compound)) throw Refuse("compound " + compound.ProxyIndex + " is a world host");
                }
                Result.CompoundsBefore = _compounds.Count;
                for (int i = 1; i < _hk.Objects.Count; i++)
                    if (_hk.Objects[i].DataOffset <= _hk.Objects[i - 1].DataOffset) throw Refuse("objects are not in payload order");
            }

            void BuildBlocks()
            {
                _objAt = new Dictionary<uint, PackfileObject>();
                foreach (PackfileObject o in _hk.Objects)
                {
                    if (o.DataOffset >= _len || (o.DataOffset & 15) != 0) throw Refuse("object at " + o.DataOffset + " is outside the payload or unaligned");
                    if (_objAt.ContainsKey(o.DataOffset)) throw Refuse("two objects at " + o.DataOffset);
                    _objAt[o.DataOffset] = o;
                }
                if (_hk.VirtualFixups.Count != _hk.Objects.Count || _hk.VirtualFixups.Any(v => !_objAt.ContainsKey(v.Src)))
                    throw Refuse("the virtual fixups and the object list disagree");

                SortedSet<uint> bounds = new SortedSet<uint>(_objAt.Keys);
                foreach (LocalFixup fixup in _hk.LocalFixups)
                {
                    if (fixup.Src + _ptr > _len || fixup.Dst > _len) throw Refuse("a local fixup points outside the payload");
                    bounds.Add(fixup.Dst);
                }
                foreach (GlobalFixup fixup in _hk.GlobalFixups)
                {
                    if (fixup.Src + _ptr > _len) throw Refuse("a global fixup starts outside the payload");
                    if (!_objAt.ContainsKey(fixup.Dst)) throw Refuse("a global fixup points at " + fixup.Dst + ", which is no object");
                }
                bounds.Add(_len);
                if (!_objAt.ContainsKey(0) || (uint)_hk.Header.ContentsSectionOffset != 0) throw Refuse("the root object is not at the start of the payload");
                _bounds = bounds.ToArray();
                _blocks = new Block[_bounds.Length - 1];
                for (int i = 0; i < _blocks.Length; i++)
                {
                    Block b = new Block { Index = i, Start = _bounds[i], End = _bounds[i + 1] };
                    if (_objAt.TryGetValue(b.Start, out PackfileObject o))
                    {
                        b.Object = o;
                        Layouts.TryGetValue(o.ClassName ?? "", out b.Layout);
                    }
                    _blocks[i] = b;
                }
                if (!_blocks[0].IsObject) throw Refuse("the payload does not start with an object");

                _localBySrc = new List<LocalFixup>[_blocks.Length];
                _globalBySrc = new List<GlobalFixup>[_blocks.Length];
                foreach (LocalFixup fixup in _hk.LocalFixups)
                {
                    int b = BlockOf(fixup.Src);
                    (_localBySrc[b] ?? (_localBySrc[b] = new List<LocalFixup>())).Add(fixup);
                }
                foreach (GlobalFixup fixup in _hk.GlobalFixups)
                {
                    int b = BlockOf(fixup.Src);
                    (_globalBySrc[b] ?? (_globalBySrc[b] = new List<GlobalFixup>())).Add(fixup);
                }
            }

            /// <summary>The block holding a payload position (positions are always below the payload end here).</summary>
            int BlockOf(uint at)
            {
                int i = Array.BinarySearch(_bounds, at);
                if (i < 0) i = ~i - 1;
                return Math.Min(Math.Max(i, 0), _blocks.Length - 1);
            }

            /// <summary>The block a local fixup's target starts, or -1 for the payload end.</summary>
            int TargetBlock(uint dst) => dst >= _len ? -1 : BlockOf(dst);

            void TypeBlocks()
            {
                //What each array pointing at a block says it holds: object fields first, so a section array is typed before its records are read
                foreach (Block src in _blocks.Where(b => b.IsObject))
                    TypeTargets(src);
                foreach (Block src in _blocks.Where(b => !b.IsObject))
                    TypeTargets(src);

                foreach (Block b in _blocks)
                {
                    uint length = b.End - b.Start;
                    if (b.IsObject)
                    {
                        if (b.Layout == null) { b.KeepEnd = b.End; continue; }
                        uint size = (uint)(_ptr == 8 ? b.Layout.Size64 : b.Layout.Size32);
                        if (size > length) throw Refuse(b.Object.ClassName + " at " + b.Start + " is " + length + " bytes, less than its class");
                        b.KeepEnd = Math.Min(b.End, AlignUp(b.Start + size, 16));
                    }
                    else if (b.Untyped || b.TypedLength < 0)
                        b.KeepEnd = b.End;
                    else
                    {
                        if (b.TypedLength > length) throw Refuse("an array at " + b.Start + " needs " + b.TypedLength + " bytes where " + length + " are left before the next block");
                        b.KeepEnd = Math.Min(b.End, AlignUp(b.Start + (uint)b.TypedLength, 16));
                    }
                }
            }

            void TypeTargets(Block src)
            {
                List<LocalFixup> fixups = _localBySrc[src.Index];
                if (fixups == null) return;
                foreach (LocalFixup fixup in fixups)
                {
                    int t = TargetBlock(fixup.Dst);
                    if (t < 0) continue;
                    Block target = _blocks[t];
                    if (target.IsObject)
                    {
                        //Only an empty array may point at an object (OpenCAGE points them at its append cursor)
                        if (ArrayCount(fixup.Src) != 0) throw Refuse("an array at " + fixup.Src + " with elements points at the object at " + fixup.Dst);
                        continue;
                    }
                    if (IsEmptyTypedArray(src, fixup))
                        continue;
                    int stride = 0;
                    ArrayKind kind = ArrayKind.Plain;
                    if (src.IsObject && src.Layout != null)
                    {
                        uint rel = fixup.Src - src.Start;
                        foreach (ArrayField field in src.Layout.Arrays)
                            if (rel == (uint)(_ptr == 8 ? field.Field64 : field.Field32)) { stride = _ptr == 8 ? field.Stride64 : field.Stride32; kind = field.Kind; break; }
                    }
                    else if (!src.IsObject && src.Kind == ArrayKind.MeshSections && (fixup.Src - src.Start) % SectionStride == 0)
                        stride = SectionNodeStride;

                    if (stride == 0) { target.Untyped = true; continue; }
                    int count = ArrayCount(fixup.Src);
                    if (count < 0) throw Refuse("an array at " + fixup.Src + " has a negative count");
                    target.TypedLength = Math.Max(target.TypedLength, (long)count * stride);
                    if (kind != ArrayKind.Plain) target.Kind = kind;
                }
            }

            int ArrayCount(uint field) => (int)field + _ptr + 4 <= _d.Length ? BitConverter.ToInt32(_d, (int)field + _ptr) : -1;

            /// <summary>
            /// A known array field with no elements: its pointer says nothing about what it points at (OpenCAGE aims empty arrays at its
            /// append cursor, which later holds someone else's data), so it neither keeps that alive nor claims it.
            /// </summary>
            bool IsEmptyTypedArray(Block src, LocalFixup fixup)
            {
                if (src.IsObject && src.Layout != null)
                {
                    uint rel = fixup.Src - src.Start;
                    if (src.Layout.Arrays.Any(f => rel == (uint)(_ptr == 8 ? f.Field64 : f.Field32)))
                        return ArrayCount(fixup.Src) == 0;
                }
                else if (!src.IsObject && src.Kind == ArrayKind.MeshSections && (fixup.Src - src.Start) % SectionStride == 0)
                    return ArrayCount(fixup.Src) == 0;
                return false;
            }

            void FindList()
            {
                _list = ProxyList.Read(_hk);
                string why = _list == null ? "no proxy list" : RegistryProblem(_list);
                if (why == null)
                {
                    Result.ListSlotsBefore = _list.Count;
                    if (_list.HasChildren) _listChildrenBlock = TargetBlock(_list.Children);
                    return;
                }
                //Not a registry of compounds by ordinal (a PHYSICS.HKX list shape is a rigid body's real shape): an ordinary object then,
                //which only a compaction that drops nothing may go through
                if (_drop.Count != 0) throw Refuse(why);
                if (_list != null) Result.Notes.Add("the rigid body's list shape is not a proxy list (" + why + "); left as it is");
                _list = null;
            }

            /// <summary>Why the list is not the proxy registry this rewrites (slot i holds compound i), or null when it is.</summary>
            string RegistryProblem(ProxyList list)
            {
                if (list.Count < 0) return "the proxy list has a negative count";
                if (list.Count > 0 && !list.HasChildren) return "the proxy list has slots but no array";
                if (!list.HasChildren) return null;
                int block = TargetBlock(list.Children);
                if (block < 0 || _blocks[block].IsObject || _blocks[block].Start != list.Children) return "the proxy list's array is not a block of its own";
                if (_localBySrc[block] != null) return "the proxy list's slots point at arrays";
                //Slot i is compound i: the rule a row's ordinal is read by. Anything else is not a file this understands.
                Dictionary<uint, int> ordinalAt = _compounds.ToDictionary(o => o.DataOffset, o => _oldOrdinal[o]);
                foreach (GlobalFixup fixup in _globalBySrc[block] ?? new List<GlobalFixup>())
                {
                    if (!list.IsSlot(fixup.Src)) return "a pointer in the proxy list's array is not a slot";
                    int slot = (int)((fixup.Src - list.Children) / (uint)list.Stride);
                    if (!ordinalAt.TryGetValue(fixup.Dst, out int ordinal) || ordinal != slot) return "proxy list slot " + slot + " does not hold compound " + slot;
                }
                return null;
            }

            void ChooseSurvivors()
            {
                _survivors = _compounds.Where(o => !_drop.Contains(o)).ToList();
                Result.Remap = new int[_compounds.Count];
                for (int i = 0; i < _compounds.Count; i++) Result.Remap[i] = -1;
                for (int i = 0; i < _survivors.Count; i++) Result.Remap[_oldOrdinal[_survivors[i]]] = i;
                Result.CompoundsAfter = _survivors.Count;

                //One slot per remaining ordinal up to the last compound that is not a host (the hosts are unlisted in retail and only
                //fill the slots between the templates and what was appended after them). Nothing dropped: the list stays as long as it is.
                int needed = 0;
                for (int i = 0; i < _survivors.Count; i++)
                    if (!_hk.IsWorldHost(_survivors[i])) needed = i + 1;
                _newListCount = _list == null ? 0 : _drop.Count == 0 ? Math.Max(_list.Count, needed) : needed;
                if (_list != null && _drop.Count == 0 && needed > _list.Count)
                    Result.Notes.Add("the proxy list was " + (needed - _list.Count) + " slot(s) short of its compounds; they are listed now");
                if (_newListCount > 0 && (_list == null || !_list.HasChildren || _list.Count <= 0))
                    throw Refuse("the proxy list has no array to write its slots in");
                Result.ListSlotsAfter = _newListCount;
            }

            void Mark()
            {
                Queue<Block> queue = new Queue<Block>();
                void Reach(Block b) { if (!b.Live) { b.Live = true; queue.Enqueue(b); } }
                HashSet<uint> survivorAt = new HashSet<uint>(_survivors.Select(o => o.DataOffset));
                foreach (Block b in _blocks.Where(o => o.IsObject))
                    if (!Collectable(b.Object.ClassName) || survivorAt.Contains(b.Start))
                        Reach(b);
                while (queue.Count != 0)
                {
                    Block b = queue.Dequeue();
                    if (_localBySrc[b.Index] != null)
                        foreach (LocalFixup fixup in _localBySrc[b.Index])
                        {
                            if (fixup.Src >= b.KeepEnd || IsEmptyTypedArray(b, fixup)) continue;
                            int t = TargetBlock(fixup.Dst);
                            if (t >= 0 && !_blocks[t].IsObject) Reach(_blocks[t]);
                        }
                    if (_globalBySrc[b.Index] != null && b.Index != _listChildrenBlock)
                        foreach (GlobalFixup fixup in _globalBySrc[b.Index])
                            if (fixup.Src < b.KeepEnd)
                                Reach(_blocks[BlockOf(fixup.Dst)]);
                }

                foreach (StaticCompoundShape compound in _drop)
                    if (_blocks[BlockOf(compound.DataOffset)].Live)
                        throw Refuse("proxy " + compound.ProxyIndex + " is still pointed at by something that stays (a placement, or an object this does not know)");

                //A pointer left in a trimmed tail would be cut off from what it points at: a tail must be dead space
                foreach (Block b in _blocks.Where(o => o.Live && o.KeepEnd < o.End && o.Index != _listChildrenBlock))
                {
                    if (_localBySrc[b.Index]?.Any(f => f.Src >= b.KeepEnd) == true || _globalBySrc[b.Index]?.Any(f => f.Src >= b.KeepEnd) == true)
                        throw Refuse("a pointer sits past the end of the " + (b.IsObject ? b.Object.ClassName : "array") + " at " + b.Start + " as its type has it");
                }
            }

            List<Block> _order;

            void Order()
            {
                //Each live object, then the arrays it owns (through its own fields, or its arrays' fields) in the order they are in now
                _order = new List<Block>();
                foreach (Block obj in _blocks.Where(o => o.IsObject && o.Live))
                {
                    obj.Owner = obj.Index;
                    List<Block> owned = new List<Block>();
                    Stack<Block> stack = new Stack<Block>();
                    stack.Push(obj);
                    while (stack.Count != 0)
                    {
                        Block b = stack.Pop();
                        if (_localBySrc[b.Index] == null) continue;
                        foreach (LocalFixup fixup in _localBySrc[b.Index])
                        {
                            if (fixup.Src >= b.KeepEnd || IsEmptyTypedArray(b, fixup)) continue;
                            int t = TargetBlock(fixup.Dst);
                            if (t < 0 || _blocks[t].IsObject || !_blocks[t].Live || _blocks[t].Owner >= 0) continue;
                            _blocks[t].Owner = obj.Index;
                            owned.Add(_blocks[t]);
                            stack.Push(_blocks[t]);
                        }
                    }
                    _order.Add(obj);
                    _order.AddRange(owned.OrderBy(o => o.Start));
                }
                Block stray = _blocks.FirstOrDefault(o => o.Live && o.Owner < 0);
                if (stray != null) throw Refuse("the block at " + stray.Start + " belongs to no object");
            }

            void Emit()
            {
                //The new list's slots: each remaining compound's old slot as it was (pointer word and all), a new one zeroed
                byte[] slots = null;
                if (_list != null)
                {
                    slots = new byte[_newListCount * _list.Stride];
                    for (int i = 0; i < _newListCount && i < _survivors.Count; i++)
                    {
                        int was = _oldOrdinal[_survivors[i]];
                        if (_list.HasChildren && was < _list.Count)
                            Buffer.BlockCopy(_d, (int)(_list.Children + was * _list.Stride), slots, i * _list.Stride, _list.Stride);
                    }
                    if (_newListCount > _survivors.Count) throw Refuse("the proxy list would need more slots than there are compounds");
                }

                long cursor = 0;
                List<(Block block, uint at, uint length)> placed = new List<(Block, uint, uint)>();
                foreach (Block b in _order)
                {
                    uint length = b.Index == _listChildrenBlock ? (uint)slots.Length : b.KeepEnd - b.Start;
                    //Same offset modulo 16 as before: what was aligned stays aligned, and a payload with nothing to drop lands where it was
                    long at = cursor + ((b.Start - cursor) & 15);
                    b.NewStart = (uint)at;
                    b.Placed = true;
                    placed.Add((b, (uint)at, length));
                    cursor = at + length;
                }
                _newLength = AlignUp((uint)cursor, 16);
                _payload = new byte[_newLength];
                foreach ((Block b, uint at, uint length) in placed)
                {
                    if (b.Index == _listChildrenBlock) { Buffer.BlockCopy(slots, 0, _payload, (int)at, slots.Length); _newListChildren = at; }
                    else Buffer.BlockCopy(_d, (int)b.Start, _payload, (int)at, (int)length);
                }

                //The list's own count: the capacity keeps its flag bits (retail: DONT_DEALLOCATE)
                if (_list != null)
                {
                    Block listBlock = _blocks[BlockOf(_list.Object.DataOffset)];
                    int field = (int)(listBlock.NewStart + (_list.Field - listBlock.Start));
                    HavokPackfile.WriteUInt32(_payload, field + _ptr, (uint)_newListCount);
                    HavokPackfile.WriteUInt32(_payload, field + _ptr + 4, (_list.CapacityAndFlags & 0xC0000000u) | (uint)_newListCount);
                }
            }

            /// <summary>Where a position in a kept block went (positions at a kept block's end included), or uint.MaxValue.</summary>
            public uint Move(uint at)
            {
                if (at >= _len) return _newLength;
                Block b = _blocks[BlockOf(at)];
                if (!b.Live || at > b.KeepEnd) return uint.MaxValue;
                return b.NewStart + (at - b.Start);
            }

            /// <summary>Where an object went, or uint.MaxValue for one that did not stay.</summary>
            public uint MoveObject(uint at)
            {
                if (!_objAt.ContainsKey(at)) return uint.MaxValue;
                Block b = _blocks[BlockOf(at)];
                return b.Live ? b.NewStart : uint.MaxValue;
            }

            /// <summary>For an empty array's pointer at something that went: the next kept position after it.</summary>
            uint MoveLoose(uint at)
            {
                if (at >= _len) return _newLength;
                for (int i = BlockOf(at); i < _blocks.Length; i++)
                    if (_blocks[i].Live) return _blocks[i].NewStart;
                return _newLength;
            }

            void Fixups()
            {
                _local = new List<LocalFixup>();
                _global = new List<GlobalFixup>();
                _virtual = new List<VirtualFixup>();
                foreach (Block b in _order)
                {
                    if (_localBySrc[b.Index] != null)
                        foreach (LocalFixup fixup in _localBySrc[b.Index])
                        {
                            if (fixup.Src >= b.KeepEnd) continue;
                            uint src = b.NewStart + (fixup.Src - b.Start);
                            uint dst;
                            if (_list != null && fixup.Src == _list.Field) dst = _newListChildren;
                            else
                            {
                                int t = TargetBlock(fixup.Dst);
                                if (t < 0) dst = _newLength;
                                else dst = _blocks[t].Live ? _blocks[t].NewStart : MoveLoose(fixup.Dst);
                            }
                            _local.Add(new LocalFixup { Src = src, Dst = dst });
                        }
                    if (_globalBySrc[b.Index] != null && b.Index != _listChildrenBlock)
                        foreach (GlobalFixup fixup in _globalBySrc[b.Index])
                        {
                            if (fixup.Src >= b.KeepEnd) continue;
                            uint dst = MoveObject(fixup.Dst);
                            if (dst == uint.MaxValue) throw Refuse("a kept pointer at " + fixup.Src + " leads to an object that went");
                            _global.Add(new GlobalFixup { Src = b.NewStart + (fixup.Src - b.Start), DstSectionIndex = fixup.DstSectionIndex, Dst = dst });
                        }
                }
                if (_list != null && _newListCount > 0)
                    for (int i = 0; i < _newListCount; i++)
                        _global.Add(new GlobalFixup { Src = _newListChildren + (uint)(i * _list.Stride), DstSectionIndex = 2, Dst = _blocks[BlockOf(_survivors[i].DataOffset)].NewStart });
                foreach (VirtualFixup fixup in _hk.VirtualFixups)
                {
                    uint at = MoveObject(fixup.Src);
                    if (at != uint.MaxValue)
                        _virtual.Add(new VirtualFixup { Src = at, SectionIndex = fixup.SectionIndex, NameOffset = fixup.NameOffset });
                }
                _local.Sort((a, b) => a.Dst != b.Dst ? a.Dst.CompareTo(b.Dst) : a.Src.CompareTo(b.Src));
                _global.Sort((a, b) => a.Src.CompareTo(b.Src));
                _virtual.Sort((a, b) => a.Src.CompareTo(b.Src));
            }

            void Measure()
            {
                Result.PayloadAfter = _newLength;
                Result.ObjectsAfter = _blocks.Count(o => o.IsObject && o.Live);
                Result.FileAfter = Result.FileBefore - Result.PayloadBefore + Result.PayloadAfter
                    - (_hk.LocalFixups.Count - _local.Count) * 8L - (_hk.GlobalFixups.Count - _global.Count) * 12L - (_hk.VirtualFixups.Count - _virtual.Count) * 12L;
                foreach (Block b in _blocks)
                {
                    if (b.Live && b.Index != _listChildrenBlock) Result.DeadTailBytes += b.End - b.KeepEnd;
                    else if (b.Live) Result.DeadTailBytes += Math.Max(0, (long)(b.End - b.Start) - _newListCount * _list.Stride);
                    else
                    {
                        Result.UnreachableBytes += b.End - b.Start;
                        if (!b.IsObject) continue;
                        switch (b.Object.ClassName)
                        {
                            case "hkpStaticCompoundShape": Result.DroppedCompounds++; break;
                            case "hkpBvCompressedMeshShape": Result.DroppedMeshes++; break;
                            case "hkpBoxShape": Result.DroppedBoxes++; break;
                            default: Result.DroppedOther++; break;
                        }
                    }
                }
                if (Result.DroppedCompounds != _drop.Count)
                    Result.Notes.Add((Result.DroppedCompounds - _drop.Count) + " compound(s) nothing reached went too");

                Result.Changed = _newLength != _len || !SameBytes(_payload, _d) || !SameLocal(_local, _hk.LocalFixups) || !SameGlobal(_global, _hk.GlobalFixups)
                    || _virtual.Count != _hk.VirtualFixups.Count || _virtual.Where((v, i) => v.Src != _hk.VirtualFixups[i].Src).Any();
            }

            static bool SameBytes(byte[] a, byte[] b) => a.Length == b.Length && a.AsSpan().SequenceEqual(b);
            static bool SameLocal(List<LocalFixup> a, List<LocalFixup> b)
            {
                if (a.Count != b.Count) return false;
                HashSet<(uint, uint)> set = new HashSet<(uint, uint)>(b.Select(o => (o.Src, o.Dst)));
                return a.All(o => set.Contains((o.Src, o.Dst)));
            }
            static bool SameGlobal(List<GlobalFixup> a, List<GlobalFixup> b)
            {
                if (a.Count != b.Count) return false;
                HashSet<(uint, uint, uint)> set = new HashSet<(uint, uint, uint)>(b.Select(o => (o.Src, o.DstSectionIndex, o.Dst)));
                return a.All(o => set.Contains((o.Src, o.DstSectionIndex, o.Dst)));
            }

            /// <summary>Put the plan into the packfile; anything that throws part-way puts it back as it was first.</summary>
            public Applied Apply()
            {
                Applied applied = new Applied(this, _hk, _before);
                applied.Snapshot();
                try { ApplyChanges(); }
                catch { applied.Rollback(); throw; }
                return applied;
            }

            void ApplyChanges()
            {
                //The payload and its tables
                _hk.DataPayload = _payload;
                _hk.LocalFixups = _local;
                _hk.GlobalFixups = _global;
                _hk.VirtualFixups = _virtual;

                //The objects that stayed, same objects at their new offsets; compounds renumbered
                List<PackfileObject> objects = new List<PackfileObject>();
                int ordinal = 0;
                foreach (PackfileObject o in _hk.Objects)
                {
                    Block b = _blocks[BlockOf(o.DataOffset)];
                    if (!b.Live) continue;
                    o.DataOffset = b.NewStart;
                    if (o.Class == ObjectClass.StaticCompoundShape) o.ProxyIndex = ordinal++;
                    objects.Add(o);
                }
                _hk.Objects = objects;

                //The typed views keep their identity (rows, pickers and caches hold them): moved and renumbered in place
                Dictionary<uint, uint> compoundMoved = new Dictionary<uint, uint>();
                foreach (StaticCompoundShape compound in _compounds)
                {
                    uint was = compound.DataOffset;
                    if (_drop.Contains(compound) || MoveObject(was) == uint.MaxValue)
                    {
                        compoundMoved[was] = uint.MaxValue;
                        continue;
                    }
                    compoundMoved[was] = MoveObject(was);
                }
                foreach (StaticCompoundShape compound in _compounds)
                {
                    if (compoundMoved[compound.DataOffset] == uint.MaxValue)
                    {
                        //Gone: an undo that brings back an entity naming it gets no collision rather than someone else's (ordinal -1)
                        compound.ProxyIndex = -1;
                        compound.DataOffset = uint.MaxValue;
                        continue;
                    }
                    foreach (CompoundInstance instance in compound.Instances)
                    {
                        uint record = Move(instance.DataOffset);
                        uint shape = MoveObject(instance.ShapeDataOffset);
                        if (record == uint.MaxValue || shape == uint.MaxValue)
                            throw Refuse("an instance of proxy " + compound.ProxyIndex + " could not be followed");
                        instance.DataOffset = record;
                        instance.ShapeDataOffset = shape;
                    }
                    compound.DataOffset = compoundMoved[compound.DataOffset];
                }
                for (int i = 0; i < _survivors.Count; i++) _survivors[i].ProxyIndex = i;
                _hk.StaticCompoundShapes = _survivors.ToList();
                foreach (PhysicsSystem system in _hk.PhysicsSystems)
                    system.DataOffset = system.Object?.DataOffset ?? MoveObject(system.DataOffset);
                _hk.AfterCompaction(at => compoundMoved.TryGetValue(at, out uint to) ? to : uint.MaxValue);
            }

            public int SurvivorCount => _survivors.Count;
            public int NewListCount => _newListCount;
            public ProxyList List => _list;
            public List<StaticCompoundShape> Compounds => _compounds;
            public HashSet<StaticCompoundShape> Dropped => _drop;
        }

        /// <summary>A compaction applied to a packfile: checked by reading the result back, or put back as it was.</summary>
        sealed class Applied
        {
            readonly Planner _plan;
            readonly HavokPackfile _hk;
            readonly byte[] _before;
            byte[] _payload;
            List<LocalFixup> _local;
            List<GlobalFixup> _global;
            List<VirtualFixup> _virtual;
            List<PackfileObject> _objects;
            (PackfileObject o, uint at, int ordinal)[] _objectState;
            List<StaticCompoundShape> _compounds;
            (StaticCompoundShape c, uint at, int ordinal, (CompoundInstance i, uint at, uint shape)[] instances)[] _compoundState;
            (PhysicsSystem s, uint at)[] _systemState;

            public Applied(Planner plan, HavokPackfile hk, byte[] before) { _plan = plan; _hk = hk; _before = before; }

            public void Snapshot()
            {
                _payload = _hk.DataPayload;
                _local = _hk.LocalFixups;
                _global = _hk.GlobalFixups;
                _virtual = _hk.VirtualFixups;
                _objects = _hk.Objects;
                _objectState = _hk.Objects.Select(o => (o, o.DataOffset, o.ProxyIndex)).ToArray();
                _compounds = _hk.StaticCompoundShapes;
                _compoundState = _plan.Compounds.Select(c => (c, c.DataOffset, c.ProxyIndex, c.Instances.Select(i => (i, i.DataOffset, i.ShapeDataOffset)).ToArray())).ToArray();
                _systemState = _hk.PhysicsSystems.Select(s => (s, s.DataOffset)).ToArray();
            }

            public void Rollback()
            {
                Dictionary<uint, uint> back = new Dictionary<uint, uint>();
                foreach (var c in _compoundState) if (c.c.DataOffset != uint.MaxValue) back[c.c.DataOffset] = c.at;
                _hk.DataPayload = _payload;
                _hk.LocalFixups = _local;
                _hk.GlobalFixups = _global;
                _hk.VirtualFixups = _virtual;
                _hk.Objects = _objects;
                foreach (var o in _objectState) { o.o.DataOffset = o.at; o.o.ProxyIndex = o.ordinal; }
                _hk.StaticCompoundShapes = _compounds;
                foreach (var c in _compoundState)
                {
                    c.c.DataOffset = c.at;
                    c.c.ProxyIndex = c.ordinal;
                    foreach (var i in c.instances) { i.i.DataOffset = i.at; i.i.ShapeDataOffset = i.shape; }
                }
                foreach (var s in _systemState) s.s.DataOffset = s.at;
                _hk.AfterCompaction(at => back.TryGetValue(at, out uint to) ? to : uint.MaxValue);
            }

            /// <summary>
            /// Write the compacted file, read it back, and hold every remaining proxy to what it was: the same instances (fields, and the
            /// shape each places, followed through the move), domain, tree bytes and triangles under its new ordinal; the list's slot i on
            /// compound i; the world hosts on the same rigid bodies.
            /// </summary>
            public void Verify()
            {
                HavokPackfile was = new HavokPackfile(_before);
                HavokPackfile now = new HavokPackfile(_hk.ToBytes());
                if (!was.Loaded || !now.Loaded) throw new InvalidOperationException("Collision compaction check: the file did not read back.");
                void Fail(string why) => throw new InvalidOperationException("Collision compaction check failed (" + Name(_hk) + "): " + why);

                int[] remap = _plan.Result.Remap;
                if (now.StaticCompoundShapes.Count != _plan.SurvivorCount) Fail("it reads back " + now.StaticCompoundShapes.Count + " compounds where " + _plan.SurvivorCount + " stayed");
                if (now.Objects.Count != _plan.Result.ObjectsAfter) Fail("object count");

                //Old payload offsets of objects to new, by object order (what stayed keeps its order), and each class the same
                Dictionary<uint, uint> objectMoved = new Dictionary<uint, uint>();
                {
                    HashSet<PackfileObject> kept = new HashSet<PackfileObject>(_hk.Objects);
                    var keptOld = _objectState.Where(o => kept.Contains(o.o)).ToList();
                    if (keptOld.Count != now.Objects.Count) Fail("kept objects " + keptOld.Count + " / read back " + now.Objects.Count);
                    for (int i = 0; i < keptOld.Count; i++)
                    {
                        if (keptOld[i].o.ClassName != now.Objects[i].ClassName) Fail("object " + i + " reads back as a " + now.Objects[i].ClassName + ", not a " + keptOld[i].o.ClassName);
                        objectMoved[keptOld[i].at] = now.Objects[i].DataOffset;
                    }
                }

                for (int old = 0; old < was.StaticCompoundShapes.Count; old++)
                {
                    int to = remap[old];
                    if (to < 0) continue;
                    StaticCompoundShape a = was.StaticCompoundShapes[old], b = now.StaticCompoundShapes[to];
                    if (b.ProxyIndex != to) Fail("compound " + to + " reads back as " + b.ProxyIndex);
                    if (a.Instances.Count != b.Instances.Count) Fail("proxy " + old + " -> " + to + " has " + b.Instances.Count + " instances, was " + a.Instances.Count);
                    if (!Same(a.DomainMin, b.DomainMin) || !Same(a.DomainMax, b.DomainMax)) Fail("proxy " + old + " -> " + to + " domain changed");
                    for (int n = 0; n < a.Instances.Count; n++)
                    {
                        CompoundInstance x = a.Instances[n], y = b.Instances[n];
                        if (!Same(x.Translation, y.Translation) || !Same(x.Scale, y.Scale) || !Same(x.Rotation, y.Rotation) || x.FilterInfo != y.FilterInfo
                            || x.ChildFilterInfoMask != y.ChildFilterInfoMask || x.UserData != y.UserData || x.ShapeClassName != y.ShapeClassName)
                            Fail("proxy " + old + " -> " + to + " instance " + n + " changed");
                        if (!objectMoved.TryGetValue(x.ShapeDataOffset, out uint shape) || shape != y.ShapeDataOffset)
                            Fail("proxy " + old + " -> " + to + " instance " + n + " places a different shape");
                    }
                    if (!was.IsWorldHost(a))
                    {
                        PreviewMesh ma = was.BuildBakeMesh(a), mb = now.BuildBakeMesh(b);
                        if (ma.Positions.Count != mb.Positions.Count || !ma.Indices.SequenceEqual(mb.Indices) || !ma.Positions.SequenceEqual(mb.Positions))
                            Fail("proxy " + old + " -> " + to + " decodes to different triangles");
                    }
                    else if (now.IsWorldHost(b) == false)
                        Fail("world host " + old + " -> " + to + " is no longer a rigid body's shape");
                }
                foreach (string role in new[] { "primary", "secondary" })
                {
                    StaticCompoundShape ha = role == "primary" ? was.WorldHostPrimary : was.WorldHostSecondary;
                    StaticCompoundShape hb = role == "primary" ? now.WorldHostPrimary : now.WorldHostSecondary;
                    if ((ha == null) != (hb == null) || (ha != null && remap[ha.ProxyIndex] != hb.ProxyIndex)) Fail("the " + role + " world host moved");
                }

                ProxyList list = ProxyList.Read(now);
                if (_plan.List != null)
                {
                    if (list == null || list.Count != _plan.NewListCount) Fail("the proxy list reads back with " + (list?.Count ?? -1) + " slots, not " + _plan.NewListCount);
                    Dictionary<uint, int> ordinalAt = now.StaticCompoundShapes.ToDictionary(o => o.DataOffset, o => o.ProxyIndex);
                    int slots = 0;
                    foreach (GlobalFixup fixup in now.GlobalFixups)
                    {
                        if (!list.IsSlot(fixup.Src)) continue;
                        int slot = (int)((fixup.Src - list.Children) / (uint)list.Stride);
                        if (!ordinalAt.TryGetValue(fixup.Dst, out int ordinal) || ordinal != slot) Fail("proxy list slot " + slot + " does not hold compound " + slot);
                        slots++;
                    }
                    if (slots != list.Count) Fail("the proxy list has " + slots + " filled slots of " + list.Count);
                }
            }

            static bool Same(System.Numerics.Vector4 a, System.Numerics.Vector4 b) => Bits(a.X) == Bits(b.X) && Bits(a.Y) == Bits(b.Y) && Bits(a.Z) == Bits(b.Z) && Bits(a.W) == Bits(b.W);
            static bool Same(System.Numerics.Quaternion a, System.Numerics.Quaternion b) => Bits(a.X) == Bits(b.X) && Bits(a.Y) == Bits(b.Y) && Bits(a.Z) == Bits(b.Z) && Bits(a.W) == Bits(b.W);
            static int Bits(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
        }
        #endregion
    }
}
