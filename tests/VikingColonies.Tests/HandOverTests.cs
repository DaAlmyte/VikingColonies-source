using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Worker polish part 4 (0.23): what Hand over moves from the player's bag (MineColonies' arrow).</summary>
    public class HandOverTests
    {
        [Fact]   // Review Focus: exactly the shortfall, capped by the bag
        public void Moves_the_shortfall_capped_by_the_bag()
        {
            var needs = new Dictionary<string, int> { ["Wood"] = 30, ["Stone"] = 10 };
            var stores = new Dictionary<string, int> { ["Wood"] = 12 };
            var bag = new Dictionary<string, int> { ["Wood"] = 50, ["Stone"] = 4, ["Resin"] = 9 };
            Dictionary<string, int> moved = HandOver.Amounts(needs, stores, bag);
            Assert.Equal(2, moved.Count);
            Assert.Equal(18, moved["Wood"]);
            Assert.Equal(4, moved["Stone"]);
        }

        [Fact]   // 0.26 (review of 0.23): what the builder already carries for his order counts
        public void Builder_load_counts()
        {
            var needs = new Dictionary<string, int> { ["Wood"] = 30 };
            var stores = new Dictionary<string, int> { ["Wood"] = 12 };
            var carried = new Dictionary<string, int> { ["Wood"] = 10 };
            var bag = new Dictionary<string, int> { ["Wood"] = 50 };
            Assert.Equal(8, HandOver.Amounts(needs, stores, bag, carried, null)["Wood"]);
            Assert.Empty(HandOver.Amounts(needs, stores, bag, new Dictionary<string, int> { ["Wood"] = 18 }, null));
        }

        [Fact]   // 0.26 (review of 0.23): a chest keeping the item gets its keep topped up first, so the builder can take the rest
        public void Keep_in_stock_is_topped_up_first()
        {
            var needs = new Dictionary<string, int> { ["Wood"] = 30 };
            var stores = new Dictionary<string, int> { ["Wood"] = 12 };
            var deficit = new Dictionary<string, int> { ["Wood"] = 5 };
            Assert.Equal(23, HandOver.Amounts(needs, stores, new Dictionary<string, int> { ["Wood"] = 50 }, null, deficit)["Wood"]);
            Assert.Equal(20, HandOver.Amounts(needs, stores, new Dictionary<string, int> { ["Wood"] = 20 }, null, deficit)["Wood"]);   // never more than the bag
            Assert.Empty(HandOver.Amounts(needs, new Dictionary<string, int> { ["Wood"] = 40 }, new Dictionary<string, int> { ["Wood"] = 50 }, null, deficit));   // nothing short: nothing for the keep either
        }

        [Fact]
        public void Old_overload_unchanged()
        {
            var needs = new Dictionary<string, int> { ["Wood"] = 30 };
            var stores = new Dictionary<string, int> { ["Wood"] = 12 };
            var bag = new Dictionary<string, int> { ["Wood"] = 50 };
            Assert.Equal(18, HandOver.Amounts(needs, stores, bag)["Wood"]);
            Assert.Equal(18, HandOver.Amounts(needs, stores, bag, null, null)["Wood"]);
        }

        [Fact]   // review of 0.26: the builder's load counts in "nothing needed" and "Still to bring" too
        public void Available_adds_the_builders_load()
        {
            var stores = new Dictionary<string, int> { ["Wood"] = 12, ["Stone"] = 4 };
            var carried = new Dictionary<string, int> { ["Wood"] = 18, ["Resin"] = 2 };
            Dictionary<string, int> all = HandOver.Available(stores, carried);
            Assert.Equal(30, all["Wood"]);
            Assert.Equal(4, all["Stone"]);
            Assert.Equal(2, all["Resin"]);
            Assert.Empty(NeedsTally.Shortfall(new Dictionary<string, int> { ["Wood"] = 30 }, all));
            Assert.Equal(2, HandOver.Available(null, new Dictionary<string, int> { ["Wood"] = 2 })["Wood"]);
        }

        [Fact]   // Review Focus: the colony's chests hold it already
        public void Nothing_when_the_chests_hold_it()
        {
            var needs = new Dictionary<string, int> { ["Wood"] = 10 };
            Assert.Empty(HandOver.Amounts(needs, new Dictionary<string, int> { ["Wood"] = 15 }, new Dictionary<string, int> { ["Wood"] = 20 }));
        }

        [Fact]
        public void Nothing_from_an_empty_bag() =>
            Assert.Empty(HandOver.Amounts(new Dictionary<string, int> { ["Wood"] = 10 }, new Dictionary<string, int>(), new Dictionary<string, int>()));
    }

    /// <summary>Take down (0.23): which buildings may be taken down, and the order for it.</summary>
    public class TakeDownRulesTests
    {
        [Fact]   // Review Focus: only a finished hut
        public void Only_a_finished_hut()
        {
            Assert.True(TakeDownRules.MayTakeDown(isTownHall: false, hasOrder: true, orderDone: true));
            Assert.True(TakeDownRules.MayTakeDown(isTownHall: false, hasOrder: false, orderDone: false));
            Assert.False(TakeDownRules.MayTakeDown(isTownHall: false, hasOrder: true, orderDone: false));
            Assert.False(TakeDownRules.MayTakeDown(isTownHall: true, hasOrder: true, orderDone: true));
        }

        [Fact]   // the storehouse (1.5)
        public void A_storehouse_is_taken_down_only_once_its_chests_are_empty()
        {
            Assert.False(TakeDownRules.MayTakeDown(isTownHall: false, hasOrder: false, orderDone: false, storeEmpty: false));
            Assert.True(TakeDownRules.MayTakeDown(false, false, false, storeEmpty: true));
            Assert.True(TakeDownRules.MayTakeDown(false, false, false));   // every other hut, as before
        }

        [Fact]   // part 3: couriers empty it first; gear they can't carry is the player's
        public void A_full_storehouse_is_emptied_by_its_couriers_or_by_the_player()
        {
            Assert.Equal(StoreTakeDown.Now, TakeDownRules.ForStore(storeEmpty: true, hasCourier: false, onlyGearLeft: false));
            Assert.Equal(StoreTakeDown.CouriersEmptyIt, TakeDownRules.ForStore(storeEmpty: false, hasCourier: true, onlyGearLeft: false));
            Assert.Equal(StoreTakeDown.EmptyItYourself, TakeDownRules.ForStore(storeEmpty: false, hasCourier: false, onlyGearLeft: false));
            Assert.Equal(StoreTakeDown.EmptyItYourself, TakeDownRules.ForStore(storeEmpty: false, hasCourier: true, onlyGearLeft: true));
        }

        [Fact]   // its review (part 3): a repair while it empties silently reopened the store
        public void A_closing_store_waits_through_a_repair_and_is_taken_down_once_empty()
        {
            Assert.Equal(ClosingStep.Wait, TakeDownRules.WhileClosing(storeEmpty: false, orderDone: true, repairing: false, takingDown: false));
            Assert.Equal(ClosingStep.TakeDown, TakeDownRules.WhileClosing(storeEmpty: true, orderDone: true, repairing: false, takingDown: false));
            Assert.Equal(ClosingStep.Wait, TakeDownRules.WhileClosing(storeEmpty: true, orderDone: false, repairing: true, takingDown: false));
            Assert.Equal(ClosingStep.Reopen, TakeDownRules.WhileClosing(storeEmpty: true, orderDone: false, repairing: false, takingDown: false));
            Assert.Equal(ClosingStep.Reopen, TakeDownRules.WhileClosing(storeEmpty: true, orderDone: false, repairing: false, takingDown: true));
        }

        [Fact]   // Review Focus: a Remove order keeps its kind through saving
        public void Remove_order_round_trips()
        {
            BuildOrderRecord order = BuildOrderRecord.ForRemove(pieces: 30, level: 2, now: 123.0);
            BuildOrderRecord copy = BuildOrderRecord.Decode(order.Encode());
            Assert.Equal(OrderKind.Remove, copy.Kind);
            Assert.Equal(0, copy.Total);
            Assert.Equal(30, copy.TakeAwayTotal);
            Assert.Equal(2, copy.TargetLevel);
            Assert.Equal(BuildState.Waiting, copy.State);
            Assert.False(copy.AllHandled);
            for (int i = 0; i < 30; i++) copy.MarkTaken(i);
            Assert.True(copy.AllHandled);
            Assert.Equal("$vc_order_take_down", OrderWords.KindToken(OrderKind.Remove));
        }

        [Fact]   // review of 0.23 (I1): no upgrade while it is being taken down (Cancel upgrade then left the builder stuck)
        public void No_upgrade_while_taken_down()
        {
            Assert.Equal(UpgradeLock.TakingDown, HutLevels.CanUpgrade(1, finished: true, upgrading: false, highestBuilderHutLevel: 3, isBuilderHut: false, takingDown: true));
            Assert.Equal(UpgradeLock.None, HutLevels.CanUpgrade(1, finished: true, upgrading: false, highestBuilderHutLevel: 3, isBuilderHut: false, takingDown: false));
        }
    }
}
