namespace VikingColonies.Core
{
    /// <summary>Who may manage a colony's huts: hire, fire, auto-hire, the replanting list, building and upgrades.</summary>
    public static class ColonyManagers
    {
        /// <param name="owner">The player who built the colony's Town Hall; 0 when unknown (the colony is then open to everyone).</param>
        /// <param name="player">The player asking; 0 when he can't be identified.</param>
        /// <param name="onOwnersWard">The owner put him on one of the owner's wards that covers the hut (Valheim's "my friends").</param>
        public static bool MayManage(long owner, long player, bool onOwnersWard) =>
            owner == 0L || (player != 0L && (player == owner || onOwnersWard));
    }
}
