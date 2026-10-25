using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The storehouse's rules (1.5): docs/superpowers/specs/2026-10-21-vikingcolonies-storehouse-design.md.</summary>
    public class StorehouseTests
    {
        [Fact]
        public void A_second_storehouse_in_a_colony_is_refused_first()
        {
            Assert.Equal(RedReason.OnlyOneStorehouse, StorehouseRules.PlaceRed(otherStorehousesInColony: 1, hasBuilderHut: false));
            Assert.Equal(RedReason.NeedsBuilderHut, StorehouseRules.PlaceRed(0, false));
            Assert.Equal(RedReason.None, StorehouseRules.PlaceRed(0, true));
        }

        [Fact]   // Review Focus 4: a cart or a ship's hold never joins
        public void Only_plain_chests_standing_inside_join_the_store()
        {
            Assert.True(StorehouseRules.Joins(insideRoom: true, isPiece: true, isColonyBuilding: false, isPersonal: false, onVehicle: false));
            Assert.False(StorehouseRules.Joins(false, true, false, false, false));   // outside its walls
            Assert.False(StorehouseRules.Joins(true, true, true, false, false));     // a hut's own chest
            Assert.False(StorehouseRules.Joins(true, true, false, true, false));     // a personal chest
            Assert.False(StorehouseRules.Joins(true, true, false, false, true));     // a cart or a ship's hold
            Assert.False(StorehouseRules.Joins(true, false, false, false, false));   // not a placed piece (a tombstone)
        }

        [Fact]   // the follow-ups' review: a chest on the ground under the raised floor joined, and goods went through the floor
        public void A_chest_under_the_raised_floor_doesnt_join()
        {
            Assert.False(StorehouseRules.Joins(true, true, false, false, false, aboveFloor: -0.72f));   // on the ground under it
            Assert.True(StorehouseRules.Joins(true, true, false, false, false, aboveFloor: 0f));        // on the floor
            Assert.True(StorehouseRules.Joins(true, true, false, false, false, aboveFloor: -0.2f));     // a little sunk in it
        }

        [Fact]
        public void The_storehouse_comes_after_a_workers_own_hut_and_before_the_Town_Hall()
        {
            Assert.True(StorehouseRules.Rank(true, false, false) < StorehouseRules.Rank(false, true, false));
            Assert.True(StorehouseRules.Rank(false, true, false) < StorehouseRules.Rank(false, false, true));
            Assert.True(StorehouseRules.Rank(false, false, true) < StorehouseRules.Rank(false, false, false));
        }

        [Fact]
        public void Stock_totals_run_most_first_and_say_where_most_lies()
        {
            var lines = ColonyStock.Totals(new[] {
                new ChestCount("Storehouse", new Dictionary<string, int> { { "Wood", 30 }, { "Stone", 5 } }),
                new ChestCount("Lumberjack's Hut", new Dictionary<string, int> { { "Wood", 40 } }) });
            Assert.Equal(new[] { "Wood", "Stone" }, lines.Select(l => l.Item));
            Assert.Equal(70, lines[0].Total);
            Assert.Equal("Lumberjack's Hut", lines[0].MostAt);
            Assert.Equal(40, lines[0].MostCount);
        }

        private static PieceBox Box(float x0, float y0, float z0, float x1, float y1, float z1) =>
            new PieceBox(new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));

        [Fact]   // its review (1.5): a player's chest inside blocked every upgrade, against the spec
        public void A_players_chest_inside_stays_through_an_upgrade_unless_the_new_level_needs_its_place()
        {
            PieceBox chest = Box(0, 0.5f, 0, 1.5f, 1.5f, 0.8f);
            PieceBox floor = Box(-1, 0.3f, -1, 2, 0.5f, 2);
            PieceBox wallThrough = Box(0.5f, 0.5f, -1, 0.7f, 3f, 2);
            PieceBox wallBeside = Box(-1, 0.5f, 0.9f, 2, 3f, 1.1f);
            PieceBox floorElsewhere = Box(5, 0.3f, 5, 7, 0.5f, 7);
            Assert.True(StorehouseRules.ChestStaysThroughUpgrade(chest, new[] { floor, wallBeside }, onGround: false));
            Assert.False(StorehouseRules.ChestStaysThroughUpgrade(chest, new[] { floor, wallThrough }, onGround: false));   // a wall goes there
            Assert.False(StorehouseRules.ChestStaysThroughUpgrade(chest, new[] { floorElsewhere }, onGround: false));      // nothing holds it up
            Assert.True(StorehouseRules.ChestStaysThroughUpgrade(chest, new[] { floorElsewhere }, onGround: true));
            Assert.Equal(1, StorehouseRules.InTheWayOf(chest, new[] { floor, wallThrough }));   // which one, for the log
            Assert.Equal(-1, StorehouseRules.InTheWayOf(chest, new[] { floor, wallBeside }));
        }

        [Fact]
        public void Stock_totals_tie_by_name_and_where_by_the_first_given()
        {
            var lines = ColonyStock.Totals(new[] {
                new ChestCount("Storehouse", new Dictionary<string, int> { { "Stone", 10 }, { "Flint", 10 } }),
                new ChestCount("Town Hall", new Dictionary<string, int> { { "Stone", 10 }, { "Resin", 0 } }) });
            Assert.Equal(new[] { "Stone", "Flint" }, lines.Select(l => l.Item));   // 20 before 10; nothing for none
            Assert.Equal("Storehouse", lines[0].MostAt);
            Assert.Empty(ColonyStock.Totals(new ChestCount[0]));
        }
    }
}
