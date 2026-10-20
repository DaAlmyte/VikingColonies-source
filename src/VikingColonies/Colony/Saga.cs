using VikingColonies.Buildings;

namespace VikingColonies.Colony
{
    /// <summary>
    /// Writes a line of a colony's saga (<see cref="Core.SagaLog"/>), kept with its Town Hall: from any computer, the Town
    /// Hall's owner appends it. With the Town Hall not loaded here nothing is written (things happen near the colony).
    /// </summary>
    internal static class Saga
    {
        public static void Record(string colonyId, string kind, params string[] args)
        {
            if (string.IsNullOrEmpty(colonyId)) return;
            ColonyBuilding hall = ColonyBuilding.FindTownHall(colonyId);
            TownHallModule module = hall != null ? hall.GetComponent<TownHallModule>() : null;
            module?.Record(kind, args);
        }

        /// <summary>A hut's name for the saga: its translation token, so each player reads it in their own language.</summary>
        public static string HutWord(ColonyBuilding building) =>
            building == null ? "" : building.Type != null ? building.Type.NameToken : building.TypeId;
    }
}
