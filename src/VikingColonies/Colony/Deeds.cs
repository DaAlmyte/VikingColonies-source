using VikingColonies.Buildings;

namespace VikingColonies.Colony
{
    /// <summary>
    /// Counts one of the colony's deeds (<see cref="Core.ColonyDeeds"/>, 1.5.1), kept with its camp: from any computer, the
    /// camp's owner adds it. With the camp not loaded here nothing is counted (as the Saga: things happen near the colony).
    /// </summary>
    internal static class Deeds
    {
        public static void Count(string colonyId, string kind, int amount = 1)
        {
            if (string.IsNullOrEmpty(colonyId) || amount < 1) return;
            ColonyBuilding hall = ColonyBuilding.FindTownHall(colonyId);
            TownHallModule module = hall != null && hall.IsValid ? hall.GetComponent<TownHallModule>() : null;
            module?.CountDeed(kind, amount);
        }
    }
}
