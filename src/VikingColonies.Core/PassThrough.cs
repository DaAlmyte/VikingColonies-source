namespace VikingColonies.Core
{
    /// <summary>
    /// Which bodies don't push each other (1.4.1, the player: "villagers keep pushing each other and me"): a settler and a
    /// settler, a settler and a player. Only their bodies: blows, arrows and the game's searches still find them.
    /// </summary>
    public static class PassThrough
    {
        public static bool Between(bool aIsSettler, bool bIsSettler, bool bIsPlayer, bool aIsPlayer = false) =>
            (aIsSettler && (bIsSettler || bIsPlayer)) || (bIsSettler && aIsPlayer);
    }
}
