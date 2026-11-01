using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>A raw food the colony's chests can spare: how many, what the cooking station makes of it, and that food's value.</summary>
    public sealed class RawFood
    {
        public string Item = "";
        public int Spare;
        public string Cooked = "";
        /// <summary>The cooked food's value (as the game counts it for players): the plainest is roasted first.</summary>
        public float CookedValue;
    }

    /// <summary>Hot food (1.6): an item a cooking station, a cauldron or an oven makes.</summary>
    public static class HotFood
    {
        /// <summary>The item is hot food; <paramref name="cooked"/> is every item the game's stations make (the plugin reads it).</summary>
        public static bool Is(string item, ICollection<string> cooked) => !string.IsNullOrEmpty(item) && cooked != null && cooked.Contains(item);

        /// <summary>How much hot food there is (pass a chest's spare, so what it keeps doesn't count): edible items only.</summary>
        public static int Count(IEnumerable<KeyValuePair<string, int>> contents, ICollection<string> cooked, Func<string, bool> isFood) =>
            contents == null ? 0 : contents.Where(kv => kv.Value > 0 && Is(kv.Key, cooked) && (isFood == null || isFood(kv.Key))).Sum(kv => kv.Value);

        /// <summary>
        /// The food that comes first at a meal (<see cref="Meals.Choose(IEnumerable{FoodStock}, IReadOnlyList{string}, Func{string, bool})"/>):
        /// hot food the Mead Hall's chest can spare (<paramref name="hallSpare"/>; null without a hall). Hot food elsewhere, the
        /// player's stew in the camp's chest, is just food, so the best is still left for the player (the review of 1.6's cook).
        /// </summary>
        public static Func<string, bool> FirstAt(ICollection<string> cooked, IReadOnlyDictionary<string, int> hallSpare) =>
            item => Is(item, cooked) && hallSpare != null && hallSpare.TryGetValue(item, out int n) && n > 0;
    }

    /// <summary>The Mead Hall (1.6): one per colony, one kitchen and one table, so the stock isn't split.</summary>
    public static class MeadHallRules
    {
        /// <summary>How often the hall looks for the cooking stations standing in it.</summary>
        public const float ScanSeconds = 5f;

        /// <summary>A hall without a plan counts the stations this near its chest as its own.</summary>
        public const float StationsNear = 8f;

        public static RedReason PlaceRed(int otherMeadHallsInColony) => otherMeadHallsInColony > 0 ? RedReason.OnlyOneMeadHall : RedReason.None;
    }

    /// <summary>
    /// The Mead Hall's cook (1.6, part 1): roasts the colony's spare raw food on the hall's cooking stations, never burnt, so
    /// the settlers have a hot breakfast (docs/superpowers/specs/2026-10-24-vikingcolonies-cook-and-mead-hall-design.md).
    /// </summary>
    public static class CookRules
    {
        /// <summary>The cook varies what is roasted: a cooked food among the last this many roasted waits its turn.</summary>
        public const int VaryLast = 3;

        /// <summary>Hot meals the cook keeps in the hall's chest: one for each settler, and two more.</summary>
        public static int HotTarget(int settlers) => Math.Max(0, settlers) + 2;

        public static bool ShouldRoast(int hotInHall, int settlers) => hotInHall < HotTarget(settlers);

        /// <summary>
        /// What to roast next: of the raw foods the station takes with something spare, the plainest (its cooked food's
        /// value), preferring one whose cooked food isn't among the last <see cref="VaryLast"/> roasted, so the colony gets
        /// as many kinds as the spare raw food allows; ties by name. Null when there's nothing to roast.
        /// </summary>
        public static RawFood NextRaw(IEnumerable<RawFood> spare, ICollection<string> stationTakes, IReadOnlyList<string> cookedLately)
        {
            if (spare == null || stationTakes == null) return null;
            List<RawFood> plainestFirst = spare.Where(r => r != null && r.Spare > 0 && stationTakes.Contains(r.Item))
                .OrderBy(r => r.CookedValue).ThenBy(r => r.Item, StringComparer.Ordinal).ToList();
            if (plainestFirst.Count == 0) return null;
            List<string> lately = cookedLately == null ? new List<string>() : cookedLately.Skip(Math.Max(0, cookedLately.Count - VaryLast)).ToList();
            return plainestFirst.FirstOrDefault(r => !lately.Contains(r.Cooked)) ?? plainestFirst[0];
        }

        /// <summary>A done item on a station stays done (never coal) while the station stands in a Mead Hall that has a cook.</summary>
        public static bool HoldsDone(bool stationInMeadHall, bool hallHasCook) => stationInMeadHall && hallHasCook;

        /// <summary>A station just loaded waits this long for its hall's pieces to load before it stops holding by its own word.</summary>
        public const float HallLoadSeconds = 30f;

        /// <summary>
        /// A station with no hall loaded round it: just loaded, it holds by what it last knew (its hall's pieces may load a
        /// moment later, and a done piece left while nobody was near would burn at once); after that it doesn't.
        /// </summary>
        public static bool HoldsWithoutHall(bool lastKnown, float secondsSinceLoaded) => lastKnown && secondsSinceLoaded < HallLoadSeconds;

        /// <summary>
        /// When a station began waiting for its hall (<see cref="HoldsWithoutHall"/>): again now while the spot where its hall's
        /// chest stood isn't loaded here (across a zone edge from the station, the hall can't load, and isn't gone).
        /// </summary>
        public static float HallWaitFrom(float waitingSince, bool hallAreaReady, float now) => hallAreaReady ? waitingSince : now;

        /// <summary>
        /// A station's fire burns: the game says so and the hearth under it has fuel (the game's own check counted a hearth
        /// with none as lit, in the cook's game check, and meat went on a cold station). A fire the cook can't find: the
        /// game's word.
        /// </summary>
        public static bool FireBurns(bool gameSaysLit, bool hasHearth, bool hearthHasFuel) => gameSaysLit && (!hasHearth || hearthHasFuel);

        /// <summary>
        /// What a station's fire needs: nothing (it burns), fuel (it has none), or what wood can't give, when it has fuel and
        /// still doesn't burn (rain on the hearth, the station not over its flames) or there's no fire to feed under it.
        /// </summary>
        public static FireNeed Need(bool lit, bool fuelKnown, float fuelHave, float sinceFed = float.PositiveInfinity) =>
            lit ? FireNeed.Burns : fuelKnown && fuelHave <= 0f ? FireNeed.Fuel : fuelKnown && sinceFed < CatchSeconds ? FireNeed.Catching : FireNeed.WontBurn;

        /// <summary>
        /// A fire the cook just fed is given this long to catch before "won't burn": from another computer the wood shows a
        /// moment before the flames (the fire looks again every 2 s).
        /// </summary>
        public const float CatchSeconds = 5f;

        /// <summary>Which station raw food in hand goes on: a burning one first, then one out of fuel (fed first); never one wood can't help (-1).</summary>
        public static int PutOnRank(FireNeed need) => need == FireNeed.Burns ? 0 : need == FireNeed.Fuel || need == FireNeed.Catching ? 1 : -1;

        /// <summary>A station's fire needs feeding: it's out, and there's food on it to cook or room for more.</summary>
        public static bool NeedsFuel(bool lit, int freeSlots, int uncooked) => !lit && (freeSlots > 0 || uncooked > 0);

        /// <summary>After a plan that found nothing to do, the cook waits this long before planning again.</summary>
        public const float PlanAgainSeconds = 3f;

        public static bool MayPlan(float now, float failedAt) => now - failedAt >= PlanAgainSeconds;

        /// <summary>A trip for raw food or wood not done in this long is given up (a chest out of reach).</summary>
        public const float TripSeconds = 90f;

        public static bool TripOver(float sinceStarted) => sinceStarted >= TripSeconds;

        /// <summary>Pieces sent to a station show in its slots within this long, or the station's own word stands.</summary>
        public const float SlotSyncTimeout = 10f;

        /// <summary>
        /// More pieces may go on once the last ones show in the station's slots (from another computer they come later), so
        /// none is sent to a slot that's taken and lost.
        /// </summary>
        public static bool MayAddMore(int filledNow, int filledExpected, float sinceSent) => filledNow >= filledExpected || sinceSent >= SlotSyncTimeout;

        /// <summary>How long after the last take-off the cook looks for pieces still owed (dropped late on another computer).</summary>
        public const float GatherSeconds = 30f;

        public static bool KeepGathering(int owed, float sinceTakeOff) => owed > 0 && sinceTakeOff < GatherSeconds;

        /// <summary>A trip's raw food: what the cook carries, the station's free slots and what's spare, the smallest.</summary>
        public static int Load(int carryCapacity, int freeSlots, int spare) => Math.Max(0, Math.Min(carryCapacity, Math.Min(freeSlots, spare)));

        /// <summary>With cooked food in hand, another done piece comes off first while the hands have room: one walk to the chest for all.</summary>
        /// <remarks>Only from a station close by (<paramref name="distance"/>): one out of reach would keep the hot food from the chest.</remarks>
        public static bool TakeMoreOff(int carried, int carryCapacity, bool anotherDone, float distance) =>
            anotherDone && carried < carryCapacity && distance <= MoveRules.StuckDeliveryRange;

        /// <summary>
        /// The walk to a piece a station dropped: picked up there; with the walk stuck, from close by (a piece by the hearth,
        /// as a chest is worked from close by), or given up farther off.
        /// </summary>
        public static GatherStep Gather(bool there, bool stuck, float distance) =>
            there ? GatherStep.PickUp : !stuck ? GatherStep.Walk : MoveRules.DeliverFromHere(true, distance) ? GatherStep.PickUp : GatherStep.GiveUp;
    }

    /// <summary>What a cooking station's fire needs (<see cref="CookRules.Need"/>).</summary>
    public enum FireNeed { Burns, Fuel, WontBurn, Catching }

    /// <summary>The cook's next step towards a piece a station dropped (<see cref="CookRules.Gather"/>).</summary>
    public enum GatherStep { Walk, PickUp, GiveUp }

    /// <summary>
    /// The cook's look at the floor (1.6): what the stations drop as pieces come off is the cook's to pick up, what lay
    /// there before isn't, and nor is a piece by a station nothing was taken off in this look (a player's, say).
    /// </summary>
    public sealed class CookDrops
    {
        /// <summary>What a station drops when a piece comes off lands this near it.</summary>
        public const float Radius = 4f;

        private readonly List<Vec3> _stations = new List<Vec3>();
        private readonly HashSet<string> _before = new HashSet<string>();

        /// <summary>Nothing owed any more: the next take-off starts a new look.</summary>
        public void NewLook()
        {
            _stations.Clear();
            _before.Clear();
        }

        /// <summary>
        /// A piece comes off the station at <paramref name="at"/>: the first time in this look, what lies by it now isn't the
        /// cook's, unless it lies by a station already taken off from (a piece still owed may lie there).
        /// </summary>
        public void TakingOffAt(Vec3 at, IEnumerable<KeyValuePair<string, Vec3>> lying)
        {
            if (HasLookedAt(at)) return;
            if (lying != null)
                foreach (KeyValuePair<string, Vec3> drop in lying)
                    if (Vec3.Distance(drop.Value, at) <= Radius && !NearTakenOff(drop.Value)) _before.Add(drop.Key);
            _stations.Add(at);
        }

        /// <summary>The floor by the station at <paramref name="at"/> was looked at this look (no need to look again).</summary>
        public bool HasLookedAt(Vec3 at) => _stations.Any(s => Vec3.Distance(s, at) < 0.01f);

        /// <summary>A drop is the cook's: by a station taken off from in this look, and not lying there before.</summary>
        public bool IsOwed(string id, Vec3 at) => !_before.Contains(id ?? "") && NearTakenOff(at);

        private bool NearTakenOff(Vec3 at) => _stations.Any(s => Vec3.Distance(s, at) <= Radius);
    }
}
