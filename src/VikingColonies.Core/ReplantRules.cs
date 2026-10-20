namespace VikingColonies.Core
{
    public enum ReplantState { Off, NeedsCultivator, On }

    /// <summary>Replanting follows the base game's bronze age: it needs a Cultivator in the hut (unless the server turns that off).</summary>
    public static class ReplantRules
    {
        public const string CultivatorPrefab = "Cultivator";

        public static ReplantState State(bool replantOn, bool needsCultivator, bool hutHasCultivator) =>
            !replantOn ? ReplantState.Off
            : needsCultivator && !hutHasCultivator ? ReplantState.NeedsCultivator
            : ReplantState.On;
    }
}
