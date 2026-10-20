using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Buildings
{
    /// <summary>
    /// The colonies' areas as this computer sees them (1.1): each Town Hall's circle (<c>Colony.ColonyRadius</c>) and its
    /// counting Watchtowers' (<see cref="WatchtowerModule.Counts"/>). A colony whose Town Hall isn't loaded here is known by
    /// its buildings, which keep where it stands; one whose Town Hall is gone is not.
    /// </summary>
    internal static class ColonyAreas
    {
        private static int s_frame = -1;
        private static List<ColonyAreaInfo> s_current;

        /// <summary>Worked out once a frame (1.2: placing a hut asked for it several times a frame); callers only read it.</summary>
        public static List<ColonyAreaInfo> Current()
        {
            if (s_current != null && s_frame == Time.frameCount) return s_current;
            s_current = Work();
            s_frame = Time.frameCount;
            return s_current;
        }

        private static List<ColonyAreaInfo> Work()
        {
            var byId = new Dictionary<string, ColonyAreaInfo>();
            foreach (ColonyBuilding b in ColonyBuilding.All)
            {
                if (b == null || !b.IsValid) continue;
                string colony = b.ColonyId;
                if (colony.Length == 0) continue;
                if (!b.IsTownHall && (!b.HasColonyPos || ColonyGrace.IsLost(b.HallLostAt))) continue;
                Vector3 hallAt = b.IsTownHall ? b.transform.position : b.ColonyPos;
                float hallRadius = HallRules.PerksAt(b.IsTownHall ? b.Level : b.ColonyLevel).Radius;   // 1.4: by the Town Hall's level
                if (!byId.TryGetValue(colony, out ColonyAreaInfo info))
                    byId[colony] = new ColonyAreaInfo { ColonyId = colony, Hall = new AreaCircle(Ground(hallAt), hallRadius) };
                else if (b.IsTownHall || (!info.HallLoaded && hallRadius > info.Hall.Radius))
                    info.Hall = new AreaCircle(Ground(hallAt), hallRadius);   // the Town Hall's own; else the widest a hut remembers
                if (b.IsTownHall) byId[colony].HallLoaded = true;
            }
            foreach (WatchtowerModule tower in WatchtowerModule.All)
            {
                AreaCircle? circle = tower != null ? tower.Circle : null;
                if (circle != null && byId.TryGetValue(tower.Building.ColonyId, out ColonyAreaInfo info)) info.Towers.Add(circle.Value);
            }
            return byId.Values.ToList();
        }

        /// <summary>The colony whose area holds this spot (its Town Hall's circle first, then the nearest tower's); null for none.</summary>
        public static string ColonyAt(Vector3 at) => ColonyArea.ChooseColony(Ground(at), Current());

        /// <summary>The colony's circles; none when it isn't known here.</summary>
        public static List<AreaCircle> CirclesOf(string colonyId)
        {
            var circles = new List<AreaCircle>();
            ColonyAreaInfo info = Current().FirstOrDefault(c => c.ColonyId == colonyId);
            if (info == null) return circles;
            circles.Add(info.Hall);
            circles.AddRange(info.Towers);
            return circles;
        }

        /// <summary>Where the colony's Town Hall stands, as it or its buildings know; null when it isn't known here.</summary>
        public static Vector3? HallPosition(string colonyId)
        {
            if (string.IsNullOrEmpty(colonyId)) return null;
            ColonyBuilding hall = ColonyBuilding.FindTownHall(colonyId);
            if (hall != null) return hall.transform.position;
            ColonyBuilding member = ColonyBuilding.All.FirstOrDefault(b => b != null && b.IsValid && b.ColonyId == colonyId && b.HasColonyPos
                                                                         && !ColonyGrace.IsLost(b.HallLostAt));
            return member != null ? member.ColonyPos : (Vector3?)null;
        }

        /// <summary>
        /// The colony a hut at this spot joins, as a building of it: its Town Hall, or while that isn't loaded here the colony's
        /// building nearest the spot (who may build there is the colony's matter, see ColonyPermissions); null for none.
        /// </summary>
        public static ColonyBuilding BuildingAt(Vector3 at)
        {
            string colony = ColonyAt(at);
            if (colony == null) return null;
            return ColonyBuilding.FindTownHall(colony)
                   ?? ColonyBuilding.All.Where(b => b != null && b.IsValid && b.ColonyId == colony)
                       .OrderBy(b => Utils.DistanceXZ(b.transform.position, at)).FirstOrDefault();
        }

        public static GroundPoint Ground(Vector3 v) => new GroundPoint(v.x, v.z);
    }
}
