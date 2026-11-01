using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Core;

namespace VikingColonies.Jobs
{
    /// <summary>
    /// Nothing burns on a station the Mead Hall's cook works (1.6, the spec's anti-goal): while it stands in a hall that
    /// has a cook, the game's own cooking leaves a done piece done (its overcooking is off for that call), so a cook called
    /// away, at night or in a raid, finds it waiting. The game advances a slot on the station's computer only, so the rule
    /// is worked out there; other stations are as the game makes them.
    /// </summary>
    [HarmonyPatch(typeof(CookingStation), "UpdateCooking")]
    internal static class CookingPatches
    {
        private static readonly AccessTools.FieldRef<CookingStation, ZNetView> s_view = AccessTools.FieldRefAccess<CookingStation, ZNetView>("m_nview");
        private static readonly Dictionary<CookingStation, float> s_firstSeen = new Dictionary<CookingStation, float>();

        private static void Prefix(CookingStation __instance, out bool __state)
        {
            __state = __instance.m_canOvercookItems;
            if (__state && Holds(__instance)) __instance.m_canOvercookItems = false;
        }

        /// <summary>The station's own setting back, whatever happened in the call.</summary>
        private static void Finalizer(CookingStation __instance, bool __state) => __instance.m_canOvercookItems = __state;

        /// <summary>
        /// The station holds its done food: it stands in a Mead Hall with a cook (<see cref="CookRules.HoldsDone"/>), kept on
        /// the station for the moment after loading when its hall isn't loaded yet (<see cref="CookRules.HoldsWithoutHall"/>).
        /// </summary>
        internal static bool Holds(CookingStation station)
        {
            ZNetView view = s_view(station);
            if (view == null || !view.IsValid()) return false;
            ZDO zdo = view.GetZDO();
            MeadHallModule hall = MeadHallModule.Of(station);
            if (hall != null)
            {
                bool holds = CookRules.HoldsDone(true, hall.HasCook);
                if (view.IsOwner())
                {
                    if (zdo.GetBool(SaveKeys.CookHolds, false) != holds) zdo.Set(SaveKeys.CookHolds, holds);
                    Vector3 at = hall.transform.position;
                    if ((zdo.GetVec3(SaveKeys.CookHallPos, Vector3.zero) - at).sqrMagnitude > 0.01f) zdo.Set(SaveKeys.CookHallPos, at);
                }
                s_firstSeen.Remove(station);   // a later wait for the hall starts then
                return holds;
            }
            if (!s_firstSeen.TryGetValue(station, out float seen))
            {
                if (s_firstSeen.Count > 64) foreach (CookingStation gone in s_firstSeen.Keys.Where(s => s == null).ToList()) s_firstSeen.Remove(gone);
                seen = Time.time;
            }
            // The hall's chest across an unloaded zone edge can't load here: the wait starts again until its spot loads (minor 9).
            Vector3 hallAt = zdo.GetVec3(SaveKeys.CookHallPos, Vector3.zero);
            // The hall's own zone, not the 3 by 3 round it (that stays unready at the loaded area's edge); its pieces get the 30 s.
            bool hallAreaReady = hallAt == Vector3.zero || ZoneSystem.instance == null || ZoneSystem.instance.IsZoneLoaded(hallAt);
            s_firstSeen[station] = seen = CookRules.HallWaitFrom(seen, hallAreaReady, Time.time);
            return CookRules.HoldsWithoutHall(zdo.GetBool(SaveKeys.CookHolds, false), Time.time - seen);
        }
    }
}
