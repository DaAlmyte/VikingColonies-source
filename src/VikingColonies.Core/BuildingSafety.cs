namespace VikingColonies.Core
{
    /// <summary>Which hits a colony building shrugs off: falling trees (Tree hits) and rolling logs (Impact hits with no attacker).</summary>
    public static class BuildingSafety
    {
        public static bool IgnoresHit(bool treeHit, bool impactHit, bool hasAttacker) => treeHit || (impactHit && !hasAttacker);

        /// <summary>
        /// A settler's tool swing never damages any building (the builder stands right next to the walls and his hammer
        /// swing can touch them). Players and monsters still do damage as usual.
        /// </summary>
        public static bool IgnoresSettlerHit(bool attackerIsSettler) => attackerIsSettler;
    }
}
