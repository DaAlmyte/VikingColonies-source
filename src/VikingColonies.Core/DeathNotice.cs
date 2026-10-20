namespace VikingColonies.Core
{
    /// <summary>How a player is told a settler died (each player's own setting).</summary>
    public enum DeathMessageStyle
    {
        CentreAndChat,
        ChatOnly,
    }

    /// <summary>Who hears about a settler's death: players in the colony's area, or near where it happened.</summary>
    public static class DeathNotice
    {
        public const float NearDeathMeters = 50f;

        public static bool ShouldTell(GroundPoint player, GroundPoint death, bool hallKnown, GroundPoint hall, float colonyRadius) =>
            ColonyRules.Distance(player, death) <= NearDeathMeters || (hallKnown && ColonyRules.Distance(player, hall) <= colonyRadius);

        public static bool ShowsCentre(DeathMessageStyle style) => style == DeathMessageStyle.CentreAndChat;
    }
}
