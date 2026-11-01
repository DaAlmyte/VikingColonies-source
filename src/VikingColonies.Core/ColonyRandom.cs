using System;
using TestKit;

namespace VikingColonies.Core
{
    /// <summary>
    /// Every random choice the mod makes (the flake hunt and replays, 26 Oct 2026), through TestKit's seeded streams. Without a
    /// seed, every player's game: one unseeded generator, as random as before. With one (vc_seed, test worlds only), a stream
    /// of its own per purpose and settler name, so a test run's colony and choices come back with its seed. Names, not ids,
    /// key the streams: a settler's name comes from the seed, and so does a new settler's id (<see cref="NewId"/>).
    /// </summary>
    public static class ColonyRandom
    {
        private static readonly SeededStreams s_streams = new SeededStreams();

        public static int? Seed => s_streams.Seed;

        /// <summary>A seed (null: unseeded again); every stream starts over.</summary>
        public static void SetSeed(int? seed) => s_streams.SetSeed(seed);

        /// <summary>The generator for this purpose (e.g. "pastime") and settler's name ("" for the colony's own).</summary>
        public static Random For(string purpose, string who) => s_streams.For(purpose, who);

        /// <summary>0 to 1, as Unity's Random.value.</summary>
        public static float Value(string purpose, string who) => (float)For(purpose, who).NextDouble();

        /// <summary>min to max, as Unity's Random.Range for floats.</summary>
        public static float Range(string purpose, string who, float min, float max) => min + (max - min) * Value(purpose, who);

        /// <summary>min to max - 1, as Unity's Random.Range for ints (min when the range is empty).</summary>
        public static int Range(string purpose, string who, int min, int maxExclusive) =>
            maxExclusive <= min ? min : For(purpose, who).Next(min, maxExclusive);

        /// <summary>A new settler's id from the seed (so their trait and every choice keyed on it come back); null without one.</summary>
        public static string NewId() => s_streams.NewId();

        public static uint Fnv1a(string text) => SeededStreams.Fnv1a(text);
    }
}
