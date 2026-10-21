namespace VikingColonies.Core
{
    /// <summary>
    /// Falling trees and rolling logs hitting settlers: the same hits colony buildings shrug off (see BuildingSafety).
    /// With the SettlersSafeFromTrees setting on, settlers shrug them off too; players are always hit.
    /// </summary>
    public static class SettlerSafety
    {
        /// <summary>The game's console kills (killall, killenemies, killtame, and debug mode's K, which runs killenemies) hit for this.</summary>
        public const float CheatKillDamage = 1E+9f;

        /// <summary>
        /// The console's kill blow, which settlers don't take (1.4.2, a player's report: debug mode's K killed the whole
        /// colony): a huge plain blow with nobody behind it. A real blow, or one from someone, still lands.
        /// </summary>
        public static bool CheatKill(float genericDamage, bool hasAttacker) => !hasAttacker && genericDamage >= CheatKillDamage;

        public static bool IsTreeHit(bool treeHit, bool impactHit, bool hasAttacker) =>
            BuildingSafety.IgnoresHit(treeHit, impactHit, hasAttacker);

        public static bool IgnoresHit(bool safeFromTrees, bool treeHit, bool impactHit, bool hasAttacker) =>
            safeFromTrees && IsTreeHit(treeHit, impactHit, hasAttacker);
    }
}
