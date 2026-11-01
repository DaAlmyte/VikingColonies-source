using UnityEngine;
using VikingColonies.Core;

namespace VikingColonies
{
    /// <summary>
    /// Unity's shapes from <see cref="ColonyRandom"/> (the flake hunt and replays, 26 Oct 2026): the mod never draws from
    /// UnityEngine.Random, so a test run's seed (vc_seed) brings its choices back. Unseeded in every player's game.
    /// </summary>
    internal static class ModRandom
    {
        public static float Value(string purpose, string who) => ColonyRandom.Value(purpose, who);

        public static float Range(string purpose, string who, float min, float max) => ColonyRandom.Range(purpose, who, min, max);

        public static int Range(string purpose, string who, int min, int maxExclusive) => ColonyRandom.Range(purpose, who, min, maxExclusive);

        /// <summary>A point in the unit circle, evenly spread, as Random.insideUnitCircle.</summary>
        public static Vector2 InsideUnitCircle(string purpose, string who)
        {
            float angle = Range(purpose, who, 0f, Mathf.PI * 2f);
            float r = Mathf.Sqrt(Value(purpose, who));
            return new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
        }

        /// <summary>A point in the unit sphere, as Random.insideUnitSphere (tried until inside: about 2 draws of 3).</summary>
        public static Vector3 InsideUnitSphere(string purpose, string who)
        {
            for (int i = 0; i < 16; i++)
            {
                var p = new Vector3(Range(purpose, who, -1f, 1f), Range(purpose, who, -1f, 1f), Range(purpose, who, -1f, 1f));
                if (p.sqrMagnitude <= 1f) return p;
            }
            return Vector3.zero;
        }

        /// <summary>A turn about the up axis, anywhere round (saplings, crops, cairns).</summary>
        public static Quaternion Turn(string purpose, string who) => Quaternion.Euler(0f, Range(purpose, who, 0f, 360f), 0f);
    }
}
