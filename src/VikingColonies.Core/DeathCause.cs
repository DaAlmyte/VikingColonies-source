namespace VikingColonies.Core
{
    /// <summary>Why a settler died: a translation token and its argument (the attacker's name for "killed by").</summary>
    public struct DeathCause
    {
        public DeathCause(string token, string arg = "")
        {
            Token = token;
            Arg = arg ?? "";
        }

        public string Token { get; }
        public string Arg { get; }
    }

    /// <summary>
    /// Turns the game's last hit on a settler into a cause. The inputs are names, so Core needs no game types: the hit's
    /// kind (HitData.HitType; "" when there was no hit), the attacker's name ("" when there is none or it is gone) and the
    /// hit's main damage type (HitData.DamageType).
    /// </summary>
    public static class DeathCauses
    {
        public const string KilledBy = "$vc_death_killed_by";
        public const string Attacked = "$vc_death_attacked";
        public const string Tree = "$vc_death_tree";
        public const string Crushed = "$vc_death_crushed";
        public const string Fall = "$vc_death_fall";
        public const string Drowned = "$vc_death_drowned";
        public const string Burned = "$vc_death_burned";
        public const string Froze = "$vc_death_froze";
        public const string Poisoned = "$vc_death_poisoned";
        public const string Smoke = "$vc_death_smoke";
        public const string Shot = "$vc_death_shot";
        public const string Edge = "$vc_death_edge";
        public const string Lightning = "$vc_death_lightning";
        public const string Unknown = "$vc_death_unknown";
        /// <summary>A guard killed by a monster (1.1): "fell defending the colony against {0}".</summary>
        public const string Defending = "$vc_death_defending";

        public static readonly string[] All =
        {
            KilledBy, Attacked, Tree, Crushed, Fall, Drowned, Burned, Froze, Poisoned, Smoke, Shot, Edge, Lightning, Unknown, Defending,
        };

        public static DeathCause From(string hitType, string attackerName, string mainDamageType)
        {
            if (!string.IsNullOrEmpty(attackerName)) return new DeathCause(KilledBy, attackerName);
            switch (hitType ?? "")
            {
                case "":
                    return new DeathCause(Unknown);
                case "EnemyHit":
                case "PlayerHit":
                    return new DeathCause(Attacked);
                case "Tree":
                    return new DeathCause(Tree);
                case "Impact":
                case "Structural":
                case "Cart":
                case "Boat":
                case "Stalagtite":
                case "DrawBridge":
                    return new DeathCause(Crushed);
                case "Fall":
                    return new DeathCause(Fall);
                case "Drowning":
                    return new DeathCause(Drowned);
                case "Burning":
                case "CinderFire":
                case "AshlandsLava":
                case "Incinerator":
                    return new DeathCause(Burned);
                case "Freezing":
                    return new DeathCause(Froze);
                case "Poisoned":
                    return new DeathCause(Poisoned);
                case "Smoke":
                    return new DeathCause(Smoke);
                case "Turret":
                case "Catapult":
                    return new DeathCause(Shot);
                case "EdgeOfWorld":
                    return new DeathCause(Edge);
            }
            switch (mainDamageType ?? "")
            {
                case "Fire":
                    return new DeathCause(Burned);
                case "Frost":
                    return new DeathCause(Froze);
                case "Poison":
                    return new DeathCause(Poisoned);
                case "Lightning":
                    return new DeathCause(Lightning);
                default:
                    return new DeathCause(Unknown);
            }
        }
    }
}
