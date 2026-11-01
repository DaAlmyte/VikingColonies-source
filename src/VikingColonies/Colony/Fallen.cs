using VikingColonies.Buildings;
using VikingColonies.Core;

namespace VikingColonies.Colony
{
    /// <summary>
    /// Puts a death on the colony's fallen list (<see cref="FallenList"/>, 1.5.1), kept with its camp: from any computer, the
    /// camp's owner adds it. With the camp not loaded here, the death waits until it is (while the game runs).
    /// </summary>
    internal static class Fallen
    {
        private static readonly FallenWaiting s_waiting = new FallenWaiting();

        public static void Record(string colonyId, FallenLine line)
        {
            if (string.IsNullOrEmpty(colonyId) || line == null) return;
            ColonyBuilding hall = ColonyBuilding.FindTownHall(colonyId);
            TownHallModule module = hall != null && hall.IsValid ? hall.GetComponent<TownHallModule>() : null;
            if (module != null)
            {
                module.RecordFallen(line);
                return;
            }
            // The camp isn't loaded here (a guard far out on a chase, its review): told once it is, while the game runs.
            s_waiting.Add(colonyId, line);
            Log.Info($"{line.Name}'s death waits to be told to their camp (colony {colonyId}): it isn't loaded here");
        }

        /// <summary>Deaths that waited for this colony's camp, now loaded here (taken once).</summary>
        public static System.Collections.Generic.IReadOnlyList<FallenLine> TakeWaiting(string colonyId) => s_waiting.Take(colonyId);
    }
}
