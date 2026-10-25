using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The review round of 23 October 2026, the Core review's findings: saved data from a tampered save or another player's
    /// computer can't hang or break the game, and two player-facing slips.
    /// </summary>
    public class ReviewRoundCoreTests
    {
        [Fact]   // Important: total=2000000000 decoded fine, then the hut's owner looped two billion times on every load
        public void A_huge_saved_piece_count_is_capped()
        {
            BuildOrderRecord order = BuildOrderRecord.Decode("v=1|total=2000000000|take=2000000000");
            Assert.NotNull(order);
            Assert.True(order!.Total <= BuildOrderRecord.MaxPieces, $"total {order.Total}");
            Assert.True(order.TakeAwayTotal <= BuildOrderRecord.MaxPieces, $"take {order.TakeAwayTotal}");
            var watch = Stopwatch.StartNew();
            order.Encode();
            bool handled = order.AllHandled;
            Assert.True(watch.ElapsedMilliseconds < 2000, $"{watch.ElapsedMilliseconds} ms");
        }

        [Fact]   // Important: each entry was checked against every one before it, so 100,000 names took billions of steps
        public void A_crafted_keep_list_decodes_quickly_and_is_capped()
        {
            var text = new StringBuilder("1|");
            for (int i = 0; i < 100000; i++) text.Append("Item").Append(i).Append("=5;");
            var watch = Stopwatch.StartNew();
            StockList list = StockList.Decode(text.ToString(), false);
            Assert.True(watch.ElapsedMilliseconds < 2000, $"{watch.ElapsedMilliseconds} ms");
            Assert.True(list.Entries.Count <= StockList.MaxDecoded, $"{list.Entries.Count} entries");
        }

        [Fact]
        public void A_crafted_roster_decodes_quickly_and_is_capped()
        {
            var text = new StringBuilder();
            for (int i = 0; i < 100000; i++) text.Append("id=c").Append(i).Append("|name=N\n");
            var watch = Stopwatch.StartNew();
            ColonyRoster roster = ColonyRoster.Decode(text.ToString());
            Assert.True(watch.ElapsedMilliseconds < 2000, $"{watch.ElapsedMilliseconds} ms");
            Assert.True(roster.Entries.Count <= ColonyRoster.MaxDecoded, $"{roster.Entries.Count} entries");
        }

        [Fact]   // re-graded: "NaN" and "Infinity" were read as numbers (a piece at NaN, the ground levelled towards infinity)
        public void Saved_numbers_must_be_finite()
        {
            Assert.Null(RecordCodec.ParseFloats("NaN,1,2", 3));
            Assert.Null(RecordCodec.ParseFloats("1,Infinity,2", 3));
            Assert.NotNull(RecordCodec.ParseFloats("1,2.5,-3", 3));
            Assert.False(PlanCodec.Decode("wood_wall;NaN;0;0;0;0;0;1")[0].IsValid);
            Assert.True(PlanCodec.Decode("wood_wall;1;0;0;0;0;0;1")[0].IsValid);
            BuildOrderRecord order = BuildOrderRecord.Decode("v=1|total=3|ground=Infinity|claim=NaN|created=-Infinity");
            Assert.True(float.IsNaN(order!.Ground));   // not set
            Assert.Equal(0, order.ClaimTime);
            Assert.Equal(0, order.Created);
            Assert.Empty(BlueprintFile.Parse(new[] { "#Pieces", "wood_wall;Building;NaN;0;0;0;0;0;1;\"\";1;1;1" }).Pieces);
            Assert.Single(BlueprintFile.Parse(new[] { "#Pieces", "wood_wall;Building;1;0;0;0;0;0;1;\"\";1;1;1" }).Pieces);
        }

        [Fact]   // re-graded: "Wood:2147483647,Wood:1" wrapped to a negative count, and a full load made every Total read throw
        public void A_carried_load_never_overflows()
        {
            CarryLoad same = CarryLoad.Deserialize("Wood:2147483647,Wood:1");
            Assert.True(same.Count("Wood") > 0 && same.Count("Wood") <= CarryLoad.MaxCount, $"{same.Count("Wood")}");
            CarryLoad two = CarryLoad.Deserialize("Wood:2147483647,Stone:1");
            Assert.True(two.Total > 0);
            var load = new CarryLoad();
            load.Add("Wood", int.MaxValue);
            load.Add("Wood", int.MaxValue);
            Assert.Equal(CarryLoad.MaxCount, load.Count("Wood"));
        }

        [Fact]   // re-graded: an old copy's keepsake (a lower life) replaced the new one, and then neither body counted as old
        public void A_keepsake_never_goes_back_to_an_older_life()
        {
            var keep = new Keepsakes();
            var newer = new CitizenRecord { Id = "a1", Name = "Sigrid", Life = 2 };
            var older = new CitizenRecord { Id = "a1", Name = "Sigrid", Life = 1 };
            Assert.True(keep.Set("a1", Keepsake.Of(newer)));
            Assert.False(keep.Set("a1", Keepsake.Of(older)));
            Assert.Equal(2, CitizenRecord.Decode(keep.Get("a1"))!.Life);
            var changed = new CitizenRecord { Id = "a1", Name = "Sigrid", Life = 2, Job = "forager" };
            Assert.True(keep.Set("a1", Keepsake.Of(changed)));   // the same life, changed: kept as before
        }

        [Fact]   // re-graded: two Farms finished the same day showed as one line
        public void Two_buildings_built_the_same_day_are_both_told()
        {
            var log = new SagaLog();
            log.Add(new SagaEntry(5, "built", "$vc_hut_farm"));
            log.Add(new SagaEntry(5, "built", "$vc_hut_farm"));
            Assert.Equal(2, log.Count);
            log.Add(new SagaEntry(5, "rescued", "Ivar"));
            log.Add(new SagaEntry(5, "rescued", "Ivar"));   // the spam the rule was for: still once
            Assert.Equal(3, log.Count);
        }

        [Fact]   // cheap safeguard: a modded item's name with '|' or a tab made a claim unreadable, and its task went out twice
        public void A_courier_task_with_odd_characters_survives_saving()
        {
            var task = new CourierTask { Kind = CourierTaskKind.Deliver, From = "a", To = "b", Item = "Mod|Item\tX", Need = "Mod|Item\tX", Amount = 4 };
            CourierTask copy = CourierTask.Decode(task.Encode())!;
            Assert.Equal(task.Key, copy.Key);
            Assert.Equal("Mod|Item\tX", copy.Item);
            var claims = new CourierClaims();
            claims.Assign(new List<CourierTask> { task }, "c1", 10);
            Assert.Equal("Mod|Item\tX", CourierClaims.Decode(claims.Encode()).For("c1")!.Item);
        }

        [Fact]   // cheap safeguard: 1.5's saved formats pinned before it ships
        public void The_saved_formats_of_1_5_never_change()
        {
            Assert.Equal(3, (int)OrderKind.Remove);
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 },
                new[] { CourierTaskKind.Deliver, CourierTaskKind.UrgentPickup, CourierTaskKind.DropOff, CourierTaskKind.Pickup, CourierTaskKind.Sort, CourierTaskKind.Clear }.Select(k => (int)k));
            Assert.Equal("0|store|farm|Wood|Wood|50", new CourierTask { Kind = CourierTaskKind.Deliver, From = "store", To = "farm", Item = "Wood", Need = "Wood", Amount = 50 }.Encode());
            var list = new StockList();
            list.Add("Wood", 20, 5);
            list.Add(StockList.AnyFood, 10, 5);
            Assert.Equal("1|Wood=20;" + StockList.AnyFood + "=10", list.Encode());
        }
        [Fact]   // Critical (building): another computer's hut sent its plan anew with each update, the same bytes in a new array,
                 // so the builder took the site afresh every time and never placed a piece
        public void The_same_plan_in_a_new_array_is_kept_not_decoded_again()
        {
            var cache = new ContentCache<List<string>>();
            int decoded = 0;
            List<string> Decode(byte[] b) { decoded++; return new List<string> { b.Length.ToString() }; }
            List<string> first = cache.Get(new byte[] { 1, 2, 3 }, Decode);
            List<string> same = cache.Get(new byte[] { 1, 2, 3 }, Decode);
            Assert.Same(first, same);
            Assert.Equal(1, decoded);
            List<string> changed = cache.Get(new byte[] { 1, 2, 4 }, Decode);
            Assert.NotSame(first, changed);
            Assert.Equal(2, decoded);
            Assert.Null(cache.Get(null, Decode));
        }
    }
}
