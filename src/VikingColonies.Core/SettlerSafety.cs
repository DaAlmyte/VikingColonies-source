namespace VikingColonies.Core
{
    /// <summary>
    /// Falling trees and rolling logs hitting settlers: the same hits colony buildings shrug off (see BuildingSafety).
    /// With the SettlersSafeFromTrees setting on, settlers shrug them off too; players are always hit.
    /// </summary>
    public static class SettlerSafety
    {
        public static bool IsTreeHit(bool treeHit, bool impactHit, bool hasAttacker) =>
            BuildingSafety.IgnoresHit(treeHit, impactHit, hasAttacker);

        public static bool IgnoresHit(bool safeFromTrees, bool treeHit, bool impactHit, bool hasAttacker) =>
            safeFromTrees && IsTreeHit(treeHit, impactHit, hasAttacker);
    }
}
