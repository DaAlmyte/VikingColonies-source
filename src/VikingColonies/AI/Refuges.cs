using System.Collections.Generic;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.AI
{
    /// <summary>
    /// The colony's huts settlers can go into: a finished building with its door standing (the chest stands inside).
    /// Looked over at most every few seconds per colony, so many settlers share one look.
    /// </summary>
    internal static class Refuges
    {
        private const float MaxAgeSeconds = 3f;
        private static readonly Dictionary<string, KeyValuePair<float, List<Refuge>>> s_byColony = new Dictionary<string, KeyValuePair<float, List<Refuge>>>();
        private static readonly List<Refuge> s_none = new List<Refuge>();

        public static List<Refuge> For(string colonyId)
        {
            if (string.IsNullOrEmpty(colonyId)) return s_none;
            if (s_byColony.TryGetValue(colonyId, out KeyValuePair<float, List<Refuge>> cached) && Time.time - cached.Key < MaxAgeSeconds)
                return cached.Value;
            var huts = new List<Refuge>();
            foreach (ColonyBuilding building in ColonyBuilding.All)
            {
                if (building == null || building.ColonyId != colonyId) continue;
                ConstructionModule site = building.GetComponent<ConstructionModule>();
                Door door = site != null && site.IsValid && site.Order != null && site.IsBuilt ? HutDoors.DoorOf(site) : null;
                Vector3 chest = building.transform.position;
                Vector3 doorAt = door != null ? door.transform.position : chest;
                Vector3 inward = door != null ? door.transform.forward : Vector3.zero;
                inward.y = 0f;
                inward = inward.normalized;
                if (Vector3.Dot(inward, chest - doorAt) < 0f) inward = -inward;   // the side the chest is on
                huts.Add(new Refuge(building.Id, new GroundPoint(chest.x, chest.z), door != null, new GroundPoint(doorAt.x, doorAt.z),
                    new GroundPoint(inward.x, inward.z)));
            }
            s_byColony[colonyId] = new KeyValuePair<float, List<Refuge>>(Time.time, huts);
            return huts;
        }
    }
}
