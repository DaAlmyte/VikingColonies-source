using System.Globalization;

namespace VikingColonies.Core
{
    /// <summary>What the log says about a settler's death. Texts are already in words (the cause, the attacker).</summary>
    public sealed class DeathFacts
    {
        public string Name = "";
        /// <summary>The job id ("lumberjack"); "" when jobless.</summary>
        public string Job = "";
        /// <summary>For example "lumberjack_hut b1e10ec6 (level 1)"; "" when jobless.</summary>
        public string Hut = "";
        /// <summary>The colony's short id; "" for none.</summary>
        public string Colony = "";
        public Vec3 Position;
        public string Cause = "";
        /// <summary>The last hit's kind; "" when there was none.</summary>
        public string HitType = "";
        /// <summary>Who dealt it, in words; "" for nobody.</summary>
        public string Attacker = "";
        public float Damage;
        /// <summary>The hit's main damage type ("Blunt").</summary>
        public string DamageType = "";
        /// <summary>Seconds between the last hit and the death; negative when unknown.</summary>
        public double SecondsSinceHit = -1;
    }

    /// <summary>The log lines for settler deaths and for falling trees and logs that hit settlers.</summary>
    public static class DeathLine
    {
        private static readonly CultureInfo Dots = CultureInfo.InvariantCulture;

        public static string Format(DeathFacts f)
        {
            string who = string.IsNullOrEmpty(f.Job)
                ? f.Name + ", jobless"
                : f.Name + ", " + f.Job + (string.IsNullOrEmpty(f.Hut) ? "" : " at " + f.Hut);
            string colony = string.IsNullOrEmpty(f.Colony) ? "no colony" : "colony " + f.Colony;
            string at = string.Format(Dots, "({0:0.0}, {1:0.0}, {2:0.0})", f.Position.X, f.Position.Y, f.Position.Z);
            return "Settler died: " + who + ", " + colony + ", at " + at + ": " + f.Cause + " [" + LastHit(f) + "]";
        }

        private static string LastHit(DeathFacts f)
        {
            if (string.IsNullOrEmpty(f.HitType)) return "no last hit";
            string when = f.SecondsSinceHit >= 0 ? string.Format(Dots, "last hit {0:0.0} s before", f.SecondsSinceHit) : "last hit";
            string by = string.IsNullOrEmpty(f.Attacker) ? "no attacker" : "by " + f.Attacker;
            string damage = string.Format(Dots, "{0:0} damage", f.Damage);
            string type = string.IsNullOrEmpty(f.DamageType) ? "" : ", mostly " + f.DamageType.ToLowerInvariant();
            return when + ": " + f.HitType + ", " + by + ", " + damage + type;
        }

        /// <summary>A falling tree or rolling log hit a settler: blocked by the setting, or how much it hurt.</summary>
        public static string CloseCall(string name, float damage, bool blocked, float healthLeft)
        {
            string what = string.Format(Dots, "{0} was hit by a falling tree or log: {1:0} damage", name, damage);
            return blocked
                ? what + " (blocked: SettlersSafeFromTrees)"
                : what + string.Format(Dots, ", {0:0} health left", healthLeft);
        }
    }
}
