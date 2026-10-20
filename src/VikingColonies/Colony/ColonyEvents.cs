using System;
using VikingColonies.Buildings;
using VikingColonies.Citizens;

namespace VikingColonies.Colony
{
    /// <summary>Things that happen in a colony. Fired on the computer where they happen.</summary>
    public static class ColonyEvents
    {
        public static event Action<ColonyBuilding> BuildingPlaced;
        public static event Action<ColonyBuilding> BuildingDestroyed;
        public static event Action<Citizen> CitizenSpawned;
        public static event Action<Citizen> CitizenHired;
        public static event Action<Citizen> CitizenFired;
        public static event Action<Citizen> CitizenDied;

        internal static void RaiseBuildingPlaced(ColonyBuilding b) => Raise(BuildingPlaced, b);
        internal static void RaiseBuildingDestroyed(ColonyBuilding b) => Raise(BuildingDestroyed, b);
        internal static void RaiseCitizenSpawned(Citizen c) => Raise(CitizenSpawned, c);
        internal static void RaiseCitizenHired(Citizen c) => Raise(CitizenHired, c);
        internal static void RaiseCitizenFired(Citizen c) => Raise(CitizenFired, c);
        internal static void RaiseCitizenDied(Citizen c) => Raise(CitizenDied, c);

        /// <summary>One failing listener must not stop the others.</summary>
        private static void Raise<T>(Action<T> handlers, T arg)
        {
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try { ((Action<T>)d)(arg); }
                catch (Exception e) { Log.Error($"Colony event listener failed: {e}"); }
            }
        }
    }
}
