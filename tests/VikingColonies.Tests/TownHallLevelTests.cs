using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // Town Hall levels (1.4): the Hall of Trophies — each level shown a Forsaken's trophy widens the colony.
    public class TownHallLevelTests
    {
        private static HallSettings On(int maxSettlers = 30) => new HallSettings(true, 50f, 10f, 10, 5, maxSettlers);
        private static HallSettings Off => new HallSettings(false, 50f, 10f, 10, 5, 30);

        [Fact]
        public void Level_one_is_as_before_1_4()
        {
            HallPerks perks = TownHallLevels.Perks(1, On());
            Assert.Equal(50f, perks.Radius);
            Assert.Equal(10, perks.Settlers);
            Assert.Equal(1, perks.HutCap);   // review of 1.4: huts go as high as the hall, level 2 after Eikthyr
        }

        [Fact]
        public void Each_level_adds_area_settlers_and_a_hut_level()
        {
            HallPerks three = TownHallLevels.Perks(3, On());
            Assert.Equal(70f, three.Radius);
            Assert.Equal(20, three.Settlers);
            Assert.Equal(3, three.HutCap);
            HallPerks five = TownHallLevels.Perks(5, On());
            Assert.Equal(90f, five.Radius);
            Assert.Equal(30, five.Settlers);
            Assert.Equal(5, five.HutCap);
            Assert.Equal(five.Radius, TownHallLevels.Perks(9, On()).Radius);   // clamped to the top level
            Assert.Equal(50f, TownHallLevels.Perks(0, On()).Radius);           // an unset level is level 1
        }

        [Fact]
        public void Settlers_never_pass_MaxSettlers()
        {
            Assert.Equal(22, TownHallLevels.Perks(5, On(maxSettlers: 22)).Settlers);
            Assert.Equal(8, TownHallLevels.Perks(1, On(maxSettlers: 8)).Settlers);
        }

        [Fact]
        public void Levels_off_give_the_old_colony()
        {
            HallPerks perks = TownHallLevels.Perks(4, Off);
            Assert.Equal(50f, perks.Radius);
            Assert.Equal(30, perks.Settlers);
            Assert.Equal(HutLevels.MaxLevel, perks.HutCap);
        }

        [Fact]
        public void Each_level_has_the_Forsaken_met_since_the_level_before()
        {
            // Review of 1.4: the levels follow the hut ages; every Forsaken up to the Queen counts.
            Assert.Empty(TownHallLevels.TrophiesFor(1));
            Assert.Equal(new[] { "TrophyEikthyr" }, TownHallLevels.TrophiesFor(2));
            Assert.Equal(new[] { "TrophyTheElder", "TrophyBonemass" }, TownHallLevels.TrophiesFor(3));
            Assert.Equal(new[] { "TrophyDragonQueen", "TrophyGoblinKing" }, TownHallLevels.TrophiesFor(4));
            Assert.Equal(new[] { "TrophySeekerQueen" }, TownHallLevels.TrophiesFor(5));
            Assert.Empty(TownHallLevels.TrophiesFor(6));
            Assert.Equal(6, Enumerable.Range(2, 4).SelectMany(TownHallLevels.TrophiesFor).Distinct().Count());   // Fader raises none
            Assert.Equal(7, TownHallLevels.Trophies.Count);
            Assert.Equal("TrophyFader", TownHallLevels.Trophies.Last());
            Assert.Equal("$enemy_eikthyr", TownHallLevels.BossToken("TrophyEikthyr"));
            Assert.Equal("$enemy_gdking", TownHallLevels.BossToken("TrophyTheElder"));
            Assert.Equal("$enemy_dragon", TownHallLevels.BossToken("TrophyDragonQueen"));
            Assert.Equal("$enemy_seekerqueen", TownHallLevels.BossToken("TrophySeekerQueen"));
            Assert.True(TownHallLevels.Trophies.All(t => TownHallLevels.BossToken(t).StartsWith("$enemy_")));
            Assert.Equal("", TownHallLevels.BossToken("TrophyDeer"));
        }

        [Fact]
        public void Each_trophy_has_the_world_key_its_boss_sets()
        {
            Assert.Equal("defeated_eikthyr", TownHallLevels.BossKey("TrophyEikthyr"));
            Assert.Equal("defeated_gdking", TownHallLevels.BossKey("TrophyTheElder"));
            Assert.Equal("defeated_dragon", TownHallLevels.BossKey("TrophyDragonQueen"));
            Assert.Equal("defeated_goblinking", TownHallLevels.BossKey("TrophyGoblinKing"));
            Assert.Equal("defeated_queen", TownHallLevels.BossKey("TrophySeekerQueen"));
            Assert.True(TownHallLevels.Trophies.All(t => TownHallLevels.BossKey(t).StartsWith("defeated_")));
            Assert.Equal("", TownHallLevels.BossKey("TrophyDeer"));
        }

        [Fact]
        public void The_first_trophy_not_shown_is_the_one_asked_for()
        {
            Assert.Equal("TrophyTheElder", TownHallLevels.MissingTrophy(3, t => false));
            Assert.Equal("TrophyBonemass", TownHallLevels.MissingTrophy(3, t => t == "TrophyTheElder"));
            Assert.Null(TownHallLevels.MissingTrophy(3, t => t == "TrophyTheElder" || t == "TrophyBonemass"));
            Assert.Null(TownHallLevels.MissingTrophy(1, t => false));   // nothing to show
        }

        [Fact]
        public void A_level_honours_its_bosses_by_name_tokens()
        {
            Assert.Equal("$enemy_eikthyr", TownHallLevels.BossesToken(2));
            Assert.Equal("$enemy_gdking $vc_and $enemy_bonemass", TownHallLevels.BossesToken(3));
            Assert.Equal("", TownHallLevels.BossesToken(1));
        }

        [Fact]
        public void Cost_text_is_read_leniently()
        {
            List<KeyValuePair<string, int>> cost = TownHallLevels.ParseCost("Wood:40, Stone:20, DeerHide:10");
            Assert.Equal(new[] { "Wood", "Stone", "DeerHide" }, cost.Select(c => c.Key).ToArray());
            Assert.Equal(new[] { 40, 20, 10 }, cost.Select(c => c.Value).ToArray());
            List<KeyValuePair<string, int>> twice = TownHallLevels.ParseCost(" Wood : 5 ,Wood:5");
            Assert.Single(twice);
            Assert.Equal(10, twice[0].Value);
            Assert.Empty(TownHallLevels.ParseCost("Wood:x, :5, Stone, Iron:0, Iron:-2, Bronze:3:4"));
            Assert.Empty(TownHallLevels.ParseCost(""));
            Assert.Empty(TownHallLevels.ParseCost(null));
        }

        [Fact]
        public void Default_costs_parse()
        {
            for (int level = 2; level <= TownHallLevels.MaxLevel; level++)
            {
                Assert.NotEmpty(TownHallLevels.ParseCost(TownHallLevels.DefaultCost(level)));
                Assert.Empty(TownHallLevels.UnreadableParts(TownHallLevels.DefaultCost(level)));
            }
            Assert.Equal("", TownHallLevels.DefaultCost(1));
            Assert.Equal("Wood:40, Stone:20, DeerHide:10", TownHallLevels.DefaultCost(2));
        }

        [Fact]
        public void Each_level_costs_what_its_age_gives_once_its_trophies_are_won()
        {
            // Review of 1.4: no material from an age after the one the level's last boss opens (EraTable's eras).
            Era[] opened = { Era.None, Era.None, Era.BlackForest, Era.Mountains, Era.Mistlands, Era.Ashlands };
            for (int level = 2; level <= TownHallLevels.MaxLevel; level++)
                foreach (KeyValuePair<string, int> item in TownHallLevels.ParseCost(TownHallLevels.DefaultCost(level)))
                    Assert.True((int)EraTable.OfItem(item.Key) <= (int)opened[level] && EraTable.OfItem(item.Key) != Era.None,
                        $"level {level}: {item.Key} is from {EraTable.OfItem(item.Key)}");
            Assert.Contains(TownHallLevels.ParseCost(TownHallLevels.DefaultCost(3)), c => c.Key == "Iron" && c.Value == 20);
        }

        private static readonly List<KeyValuePair<string, int>> s_cost = TownHallLevels.ParseCost("Wood:40, Stone:20");

        private static RaiseLock Raise(int level, bool trophy, int wood, int stone, bool enabled = true, bool known = true) =>
            TownHallLevels.CanRaise(enabled, level, trophy, s_cost, item => item == "Wood" ? wood : item == "Stone" ? stone : 0, item => known);

        [Fact]
        public void Raise_waits_for_the_trophy_then_materials()
        {
            Assert.Equal(RaiseLock.NoTrophy, Raise(1, trophy: false, wood: 40, stone: 20));
            Assert.Equal(RaiseLock.Materials, Raise(1, trophy: true, wood: 39, stone: 20));
            Assert.Equal(RaiseLock.None, Raise(1, trophy: true, wood: 40, stone: 25));
            Assert.Equal(RaiseLock.MaxLevel, Raise(5, trophy: true, wood: 40, stone: 20));
            Assert.Equal(RaiseLock.Off, Raise(1, trophy: true, wood: 40, stone: 20, enabled: false));
            Assert.Equal(RaiseLock.UnknownItem, Raise(1, trophy: true, wood: 40, stone: 20, known: false));
            // An empty cost: only the trophy.
            Assert.Equal(RaiseLock.None, TownHallLevels.CanRaise(true, 2, true, new List<KeyValuePair<string, int>>(), item => 0, item => true));
        }

        [Fact]
        public void A_hut_waits_for_the_Town_Hall()
        {
            Assert.Equal(UpgradeLock.TownHallTooLow, HutLevels.CanUpgrade(2, true, false, 5, false, hutCap: 2));
            Assert.Equal(UpgradeLock.None, HutLevels.CanUpgrade(2, true, false, 5, false, hutCap: 3));
            Assert.Equal(UpgradeLock.TownHallTooLow, HutLevels.CanUpgrade(3, true, false, 5, false, hutCap: 2));   // kept, not raised
            Assert.Equal(UpgradeLock.TownHallTooLow, HutLevels.CanUpgrade(2, true, false, 2, true, hutCap: 2));    // the Builder's Hut too
            Assert.Equal(UpgradeLock.NotFinished, HutLevels.CanUpgrade(2, false, false, 5, false, hutCap: 2));     // finish it first
            Assert.Equal(UpgradeLock.MaxLevel, HutLevels.CanUpgrade(5, true, false, 5, false, hutCap: 2));
        }

        [Fact]   // review: a Town Hall loading at level 3 must not tell everyone it "is now level 3"
        public void Only_a_fresh_raise_is_celebrated()
        {
            Assert.True(TownHallLevels.JustRaised(seenLevel: 1, level: 2, raisedAt: 100, now: 103));
            Assert.False(TownHallLevels.JustRaised(seenLevel: -1, level: 2, raisedAt: 100, now: 103));   // the first look
            Assert.False(TownHallLevels.JustRaised(seenLevel: 1, level: 3, raisedAt: 100, now: 400));    // raised long ago
            Assert.False(TownHallLevels.JustRaised(seenLevel: 2, level: 2, raisedAt: 100, now: 101));
            Assert.False(TownHallLevels.JustRaised(seenLevel: 1, level: 2, raisedAt: 0, now: 5));        // never raised by a player
            // Review: the raise time is saved as a float (and other computers' clocks differ a little): just "after" now is now.
            Assert.True(TownHallLevels.JustRaised(seenLevel: 1, level: 2, raisedAt: 100.5, now: 100));
            Assert.False(TownHallLevels.JustRaised(seenLevel: 1, level: 2, raisedAt: 110, now: 100));
        }

        [Fact]   // review: a double click, or two players at once, raised two levels and paid for both
        public void A_raise_is_for_the_level_the_player_saw()
        {
            List<KeyValuePair<string, int>> none = new List<KeyValuePair<string, int>>();
            Assert.Equal(RaiseLock.Already, TownHallLevels.CanRaise(true, 2, true, none, i => 0, i => true, seenLevel: 1));
            Assert.Equal(RaiseLock.None, TownHallLevels.CanRaise(true, 2, true, none, i => 0, i => true, seenLevel: 2));
            Assert.Equal(RaiseLock.None, TownHallLevels.CanRaise(true, 2, true, none, i => 0, i => true));   // not given: as seen
        }

        [Fact]   // review: "Wood=40, Stone:20" made the level cost only stone, and nobody was told
        public void A_cost_part_that_cannot_be_read_blocks_the_raise()
        {
            Assert.Equal(new[] { "Wood=40" }, TownHallLevels.UnreadableParts("Wood=40, Stone:20").ToArray());
            Assert.Equal(new[] { "Wood:40; Stone:20" }, TownHallLevels.UnreadableParts("Wood:40; Stone:20").ToArray());
            Assert.Equal(new[] { "Iron:0", "Wood 40" }, TownHallLevels.UnreadableParts("Iron:0, Wood 40,  , Stone:2").ToArray());
            Assert.Empty(TownHallLevels.UnreadableParts(""));
            Assert.Empty(TownHallLevels.UnreadableParts(TownHallLevels.DefaultCost(3)));
            List<KeyValuePair<string, int>> stone = TownHallLevels.ParseCost("Stone:20");
            Assert.Equal(RaiseLock.UnknownItem, TownHallLevels.CanRaise(true, 1, true, stone, i => 99, i => true, unreadable: 1));
            Assert.Equal(RaiseLock.None, TownHallLevels.CanRaise(true, 1, true, stone, i => 99, i => true, unreadable: 0));
        }

        private static ColonyNeeds Colony(int settlers) => new ColonyNeeds(settlers, beds: 40, food: 50, lastArrivalDay: 1, today: 5, night: false);

        [Fact]
        public void Newcomers_wait_for_a_bigger_hall()
        {
            var rules = new NewcomerRules(perDay: 2, foodEach: 5, maxSettlers: 30, hallSettlers: 10);
            Assert.Equal(NewcomerWait.HallFull, Newcomers.WaitsFor(Colony(10), rules));
            Assert.Equal(0, Newcomers.HowMany(Colony(10), rules));
            Assert.Equal(1, Newcomers.HowMany(Colony(9), rules));   // two a day, room for one
            Assert.Equal(NewcomerWait.Full, Newcomers.WaitsFor(Colony(30), rules));
            Assert.Equal(NewcomerWait.None, Newcomers.WaitsFor(Colony(10), new NewcomerRules(2, 5, 30)));   // no hall limit given
        }
    }
}
