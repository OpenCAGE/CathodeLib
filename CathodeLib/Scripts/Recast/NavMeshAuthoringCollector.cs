#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Numerics;
using CATHODE;
using CATHODE.Enums;
using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using CathodeLib;

namespace CathodeLib.NavMesh
{
    internal static class NavMeshAuthoringCollector
    {
        const float SeedQuantize = 1f / 32f;

        public sealed class CollectStats
        {
            public int CharacterSeeds;
            public int SplineSeeds;
            public int PathfindingNodeSeeds;
            public int CommandSeeds;
            public int NavMeshSeedPoints;
            public int OffMeshLinks;
        }

        /// <param name="placement">
        /// An existing instancing run to read placements from. When null a placement-only run is
        /// made just for this collection.
        /// </param>
        public static CollectStats Collect(Level level, CollisionNavMeshSoup soup, Instancing placement)
        {
            if (level == null || soup == null)
                return new CollectStats();

            soup.OffMeshLinks ??= new List<CollisionNavMeshSoup.OffMeshLinkDraft>();
            soup.OffMeshLinks.Clear();

            var stats = new CollectStats();
            var seen = new HashSet<(int, int, int)>();

            foreach (InstancedEntity entity in placement.GeneratedEntities)
            {
                if (entity?.Entity == null || entity.Entity.variant != EntityVariant.FUNCTION)
                    continue;
                if (IsSkippedEntity(entity))
                    continue;

                FunctionEntity function = (FunctionEntity)entity.Entity;
                if (!function.function.IsFunctionType)
                    continue;

                switch (function.function.AsFunctionType)
                {
                    case FunctionType.NavMeshReachabilitySeedPoint:
                        if (TryAddSeed(seen, soup.ReachabilitySeeds, EntityWorldPosition(entity)))
                            stats.NavMeshSeedPoints++;
                        break;
                    case FunctionType.Character:
                        if (TryAddSeed(seen, soup.ReachabilitySeeds, EntityWorldPosition(entity)))
                            stats.CharacterSeeds++;
                        break;
                    case FunctionType.TRAV_1ShotSpline:
                        if (TryGetFirstSplinePointWorld(entity, ShortGuids.EntrancePath, out Vector3 splineStart)
                            && TryAddSeed(seen, soup.ReachabilitySeeds, splineStart))
                            stats.SplineSeeds++;
                        break;
                    case FunctionType.PathfindingTeleportNode:
                        CollectPathfindingNode(entity, seen, soup, stats,
                            NavigationMesh.OffMeshLinkType.Teleport);
                        break;
                    case FunctionType.PathfindingWaitNode:
                        CollectPathfindingNode(entity, seen, soup, stats,
                            NavigationMesh.OffMeshLinkType.Wait);
                        break;
                    case FunctionType.PathfindingManualNode:
                        CollectPathfindingNode(entity, seen, soup, stats,
                            NavigationMesh.OffMeshLinkType.Manual);
                        break;
                    case FunctionType.PathfindingAlienBackstageNode:
                        CollectPathfindingBackstage(entity, seen, soup, stats);
                        break;
                    case FunctionType.CMD_GoTo:
                        if (!GetBool(entity, ShortGuids.DestinationIsBackstage, false)
                            && TryGetLinkedTransformWorld(entity, ShortGuids.Waypoint, out Vector3 waypoint)
                            && TryAddSeed(seen, soup.ReachabilitySeeds, waypoint))
                            stats.CommandSeeds++;
                        break;
                    case FunctionType.CMD_MoveTowards:
                        if (TryGetLinkedTransformWorld(entity, ShortGuids.MoveTarget, out Vector3 moveTarget)
                            && TryAddSeed(seen, soup.ReachabilitySeeds, moveTarget))
                            stats.CommandSeeds++;
                        break;
                    case FunctionType.CMD_GoToCover:
                        if (TryGetLinkedTransformWorld(entity, ShortGuids.CoverPoint, out Vector3 cover)
                            && TryAddSeed(seen, soup.ReachabilitySeeds, cover))
                            stats.CommandSeeds++;
                        break;
                }
            }

            stats.OffMeshLinks = soup.OffMeshLinks.Count;
            return stats;
        }

        static void CollectPathfindingNode(
            InstancedEntity entity,
            HashSet<(int, int, int)> seen,
            CollisionNavMeshSoup soup,
            CollectStats stats,
            NavigationMesh.OffMeshLinkType linkType)
        {
            Vector3 start = EntityWorldPosition(entity);
            if (!GetBool(entity, ShortGuids.build_into_navmesh, false))
                return;

            if (TryAddSeed(seen, soup.ReachabilitySeeds, start))
                stats.PathfindingNodeSeeds++;

            if (!TryGetLinkedTransformWorld(entity, ShortGuids.destination, out Vector3 end))
                return;

            if (TryAddSeed(seen, soup.ReachabilitySeeds, end))
                stats.PathfindingNodeSeeds++;

            soup.OffMeshLinks.Add(new CollisionNavMeshSoup.OffMeshLinkDraft
            {
                Start = start,
                End = end,
                LinkType = linkType,
                ExtraCost = GetFloat(entity, ShortGuids.extra_cost, 1f),
                CharacterClasses = GetCharacterClasses(entity),
                OpenOnReset = GetBool(entity, ShortGuids.open_on_reset, true),
                Entity = entity.Handle,
            });
        }

        static void CollectPathfindingBackstage(
            InstancedEntity entity,
            HashSet<(int, int, int)> seen,
            CollisionNavMeshSoup soup,
            CollectStats stats)
        {
            if (!GetBool(entity, ShortGuids.build_into_navmesh, false))
                return;

            Vector3 start = EntityWorldPosition(entity);
            if (TryAddSeed(seen, soup.ReachabilitySeeds, start))
                stats.PathfindingNodeSeeds++;

            if (TryGetLinkedTransformWorld(entity, ShortGuids.destination, out Vector3 end)
                && TryAddSeed(seen, soup.ReachabilitySeeds, end))
                stats.PathfindingNodeSeeds++;
        }

        static bool IsSkippedEntity(InstancedEntity entity)
        {
            if (GetBool(entity, ShortGuids.deleted, false))
                return true;
            if (GetBool(entity, ShortGuids.delete_me, false))
                return true;
            return false;
        }

        static Vector3 EntityWorldPosition(InstancedEntity entity)
        {
            return entity.CalculateWorldPositionRotation().position;
        }

        static bool TryGetLinkedTransformWorld(InstancedEntity entity, ShortGuid pin, out Vector3 world)
        {
            world = default;
            if (entity.Transforms.Links != null
                && entity.Transforms.Links.TryGetValue(pin, out List<Tuple<ShortGuid, InstancedEntity>> links)
                && links != null
                && links.Count > 0)
            {
                InstancedEntity target = links[0].Item2;
                ShortGuid targetParam = links[0].Item1;
                if (target == null)
                    return false;
                return TryGetInstancedTransformWorld(target, targetParam, out world);
            }
            return TryGetFirstChildLinkTransformWorld(entity, pin, out world);
        }

        static bool TryGetFirstChildLinkTransformWorld(InstancedEntity entity, ShortGuid pin, out Vector3 world)
        {
            world = default;
            if (!(entity.Entity is FunctionEntity function) || function.childLinks == null)
                return false;

            foreach (EntityConnector link in function.childLinks)
            {
                if (link.thisParamID != pin)
                    continue;
                InstancedEntity target = FindSiblingEntity(entity, link.linkedEntityID);
                if (target == null)
                    continue;
                return TryGetInstancedTransformWorld(target, link.linkedParamID, out world);
            }
            return false;
        }

        /// <summary>Resolve a transform/vector parameter on an instanced entity to world space.</summary>
        static bool TryGetInstancedTransformWorld(InstancedEntity entity, ShortGuid paramId, out Vector3 world)
        {
            world = default;
            if (entity == null)
                return false;

            if (paramId == ShortGuids.position || paramId == ShortGuid.Invalid)
            {
                world = entity.CalculateWorldPositionRotation().position;
                return true;
            }

            if (entity.Transforms.Has(paramId) || HasTransformLink(entity, paramId))
            {
                if (paramId == ShortGuids.position)
                {
                    world = entity.CalculateWorldPositionRotation().position;
                    return true;
                }
                InstancedEntity.Transform t = entity.Transforms.Get(paramId);
                world = Vector3.Transform(t.Position, entity.CalculateWorldTransformMatrix());
                return true;
            }

            if (entity.Vectors.Has(paramId) || HasVectorLink(entity, paramId))
            {
                Vector3 local = entity.Vectors.Get(paramId);
                world = Vector3.Transform(local, entity.CalculateWorldTransformMatrix());
                return true;
            }

            if (paramId == ShortGuids.x || paramId == ShortGuids.y || paramId == ShortGuids.z)
            {
                InstancedEntity.Transform root = entity.Transforms.Get(ShortGuids.position);
                Vector3 local = root.Position;
                if (paramId == ShortGuids.x) local = new Vector3(local.X, 0, 0);
                else if (paramId == ShortGuids.y) local = new Vector3(0, local.Y, 0);
                else local = new Vector3(0, 0, local.Z);
                world = Vector3.Transform(local, entity.CalculateWorldTransformMatrix());
                return true;
            }

            world = entity.CalculateWorldPositionRotation().position;
            return true;
        }

        static bool HasTransformLink(InstancedEntity entity, ShortGuid guid)
        {
            return entity.Transforms.Links != null && entity.Transforms.Links.ContainsKey(guid);
        }

        static bool HasVectorLink(InstancedEntity entity, ShortGuid guid)
        {
            return entity.Vectors.Links != null && entity.Vectors.Links.ContainsKey(guid);
        }

        static InstancedEntity FindSiblingEntity(InstancedEntity entity, ShortGuid entityId)
        {
            if (entity.ThisCompositeInstance?.Entities == null)
                return null;
            foreach (InstancedEntity sibling in entity.ThisCompositeInstance.Entities)
            {
                if (sibling?.Entity != null && sibling.Entity.shortGUID == entityId)
                    return sibling;
            }
            return null;
        }

        static bool TryGetFirstSplinePointWorld(InstancedEntity entity, ShortGuid splinePin, out Vector3 world)
        {
            world = default;
            cSpline spline = null;
            if (TryGetLinkedSpline(entity, splinePin, out spline))
            {
                // linked spline resolved
            }
            else if (TryGetInstancedSpline(entity, splinePin, out spline))
            {
                // local spline on this instance
            }
            if (spline == null || spline.splinePoints == null || spline.splinePoints.Count == 0)
                return false;

            Vector3 local = spline.splinePoints[0].position;
            world = Vector3.Transform(local, entity.CalculateWorldTransformMatrix());
            return true;
        }

        static bool TryGetLinkedSpline(InstancedEntity entity, ShortGuid splinePin, out cSpline spline)
        {
            spline = null;
            if (entity.Transforms.Links != null
                && entity.Transforms.Links.TryGetValue(splinePin, out List<Tuple<ShortGuid, InstancedEntity>> links)
                && links != null
                && links.Count > 0)
            {
                InstancedEntity target = links[0].Item2;
                ShortGuid targetParam = links[0].Item1;
                if (target != null && TryGetInstancedSpline(target, targetParam, out spline))
                    return true;
            }

            if (!(entity.Entity is FunctionEntity function) || function.childLinks == null)
                return false;

            foreach (EntityConnector link in function.childLinks)
            {
                if (link.thisParamID != splinePin)
                    continue;
                InstancedEntity target = FindSiblingEntity(entity, link.linkedEntityID);
                if (target != null && TryGetInstancedSpline(target, link.linkedParamID, out spline))
                    return true;
                break;
            }
            return spline != null;
        }

        /// <summary>Spline data lives on the instanced entity's parameter bag (post-alias).</summary>
        static bool TryGetInstancedSpline(InstancedEntity entity, ShortGuid paramId, out cSpline spline)
        {
            spline = null;
            if (entity?.Entity == null)
                return false;

            Parameter p = entity.Entity.GetParameter(paramId);
            if (p?.content is cSpline s && s.splinePoints != null && s.splinePoints.Count > 0)
            {
                spline = s;
                return true;
            }
            return false;
        }

        static bool TryAddSeed(HashSet<(int, int, int)> seen, List<Vector3> seeds, Vector3 position)
        {
            if (float.IsNaN(position.X) || float.IsNaN(position.Y) || float.IsNaN(position.Z))
                return false;

            var key = (
                (int)MathF.Round(position.X / SeedQuantize),
                (int)MathF.Round(position.Y / SeedQuantize),
                (int)MathF.Round(position.Z / SeedQuantize));
            if (!seen.Add(key))
                return false;

            seeds.Add(position);
            return true;
        }

        static bool GetBool(InstancedEntity entity, ShortGuid name, bool fallback)
        {
            if (entity.Bools.Has(name) || (entity.Bools.Links != null && entity.Bools.Links.ContainsKey(name)))
                return entity.Bools.Get(name);
            return fallback;
        }

        static float GetFloat(InstancedEntity entity, ShortGuid name, float fallback)
        {
            if (entity.Floats.Has(name) || (entity.Floats.Links != null && entity.Floats.Links.ContainsKey(name)))
                return entity.Floats.Get(name);
            return fallback;
        }

        static NAVIGATION_CHARACTER_CLASS_COMBINATION GetCharacterClasses(InstancedEntity entity)
        {
            if (!entity.EnumIndexes.Has(ShortGuids.character_classes)
                && (entity.EnumIndexes.Links == null || !entity.EnumIndexes.Links.ContainsKey(ShortGuids.character_classes)))
                return NAVIGATION_CHARACTER_CLASS_COMBINATION.ALL;

            int idx = entity.EnumIndexes.Get(ShortGuids.character_classes);
            if (Enum.IsDefined(typeof(NAVIGATION_CHARACTER_CLASS_COMBINATION), idx))
                return (NAVIGATION_CHARACTER_CLASS_COMBINATION)idx;
            return NAVIGATION_CHARACTER_CLASS_COMBINATION.ALL;
        }
    }
}
#endif