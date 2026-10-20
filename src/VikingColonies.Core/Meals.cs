using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>Food in the colony's chests: an item, how many, and its food value (as the game counts it for players).</summary>
    public struct FoodStock
    {
        public FoodStock(string item, int count, float value)
        {
            Item = item ?? "";
            Count = count;
            Value = value;
        }

        public string Item { get; }
        public int Count { get; }
        public float Value { get; }
    }

    /// <summary>
    /// A settler eats once a day, at dawn, from the colony's chests: the plainest food there (the least food value), so
    /// the player's best is left for the player. One who finds nothing is hungry till his next meal and works slower.
    /// </summary>
    public static class Meals
    {
        /// <summary>"He hasn't eaten yet" (a new settler, or one from before meals).</summary>
        public const int NeverAte = -1;
        /// <summary>A hungry settler never slows below this.</summary>
        public const float SlowestWork = 0.1f;

        /// <summary>He'd rather not eat again what he had at his last this many meals (0.17).</summary>
        public const int AvoidLast = 3;

        /// <summary>The item he eats, or null when there's nothing to eat.</summary>
        public static string Choose(IEnumerable<FoodStock> stock) => Choose(stock, null);

        /// <summary>
        /// The plainest food he hasn't had at his last <see cref="AvoidLast"/> meals; when he has had every kind there lately,
        /// the one he had longest ago (the plainest on a tie). So four kinds or more give four different foods in any five
        /// meals, and the best food in a well-stocked colony is left for you (0.17 review I2: avoiding only the last meal,
        /// he alternated the two plainest for ever). Null when there's nothing to eat.
        /// </summary>
        public static string Choose(IEnumerable<FoodStock> stock, IReadOnlyList<string> recent)
        {
            List<string> plainestFirst = stock.Where(f => f.Count > 0 && f.Value > 0f).OrderBy(f => f.Value).ThenByDescending(f => f.Count)
                .ThenBy(f => f.Item, StringComparer.Ordinal).Select(f => f.Item).ToList();
            if (plainestFirst.Count == 0) return null;
            if (recent == null || recent.Count == 0) return plainestFirst[0];
            List<string> lately = recent.Skip(Math.Max(0, recent.Count - AvoidLast)).ToList();
            string fresh = plainestFirst.FirstOrDefault(i => !lately.Contains(i));
            if (fresh != null) return fresh;
            List<string> eaten = recent.ToList();
            return plainestFirst.OrderBy(i => eaten.LastIndexOf(i)).First();   // a stable sort: the plainest on a tie
        }

        /// <summary>
        /// He counts as fed (mood's "ate", 0.17): not hungry, and his meal was today or yesterday, so he stays fed through
        /// the night until breakfast (review I1: it dropped off at midnight); a new settler comes fed; with meals off,
        /// nobody goes without.
        /// </summary>
        public static bool Fed(int lastMealDay, bool hungry, int today, bool eating) =>
            !eating || FirstDay(lastMealDay) || !hungry && lastMealDay <= today && today - lastMealDay <= 1;

        /// <summary>His meal is due: a new day has dawned since his last (not at night). One who never ate has his
        /// first day on the house (<see cref="FirstDay"/>).</summary>
        public static bool Due(int lastMealDay, int today, bool night) => !FirstDay(lastMealDay) && today != lastMealDay && !night;   // a later day: the clock was put back

        /// <summary>He never ate: a new settler, or one from before meals. He counts as fed today.</summary>
        public static bool FirstDay(int lastMealDay) => lastMealDay == NeverAte;

        public static float WorkSpeed(bool hungry, float hungrySpeed) => hungry ? Math.Max(SlowestWork, Math.Min(1f, hungrySpeed)) : 1f;
    }
}
