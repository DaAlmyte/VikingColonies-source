using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The earth under a building's roof (the player, 2026-10-21: "I think the dirt floors are fine ... maybe we can polish it
    /// a bit"; the review's pictures: the Longhouse's earth floor was meadow grass and shrubs, as the builder only paints the
    /// ground its levelling moves).
    /// </summary>
    public class EarthFloorTests
    {
        private static PlannedPiece P(string prefab, float x, float y, float z) =>
            new PlannedPiece(0, prefab, new Vec3(x, y, z), Quat.Identity);

        [Fact]
        public void Roofs_are_known_by_their_names()
        {
            Assert.True(EarthFloor.IsRoof("wood_roof_45"));
            Assert.True(EarthFloor.IsRoof("darkwood_roof_top_45"));
            Assert.True(EarthFloor.IsRoof("piece_grausten_roof_45"));
            Assert.False(EarthFloor.IsRoof("wood_wall_roof_45"));       // a gable's triangle is a wall
            Assert.False(EarthFloor.IsRoof("ashwood_wall_roof_26"));
        }

        [Fact]
        public void The_ground_under_a_roof_is_painted_within_the_roof()
        {
            List<GroundPoint> spots = EarthFloor.Spots(new[] { P("wood_roof_45", 0f, 3f, 0f), P("wood_roof_45", 2f, 3f, 0f) }, 2f);
            Assert.NotEmpty(spots);
            Assert.All(spots, s => Assert.InRange(s.X, -1f, 3f));
            Assert.All(spots, s => Assert.InRange(s.Z, -1f, 1f));
        }

        [Fact]
        public void Nothing_is_painted_without_a_roof()
        {
            Assert.Empty(EarthFloor.Spots(new[] { P("wood_wall_roof_45", 0f, 3f, 0f), P("woodwall", 0f, 0f, 0f) }, 2f));
            Assert.Empty(EarthFloor.Spots(new[] { P("wood_floor", 0f, 0f, 0f) }, 2f));
        }

        [Fact]
        public void The_earth_under_a_floor_is_painted_too()
        {
            // The game's grass stands up through a floor resting on the earth (the in-game check, 2026-10-21: through the
            // Longhouse's sleeping platforms, 13 cm high): under a roof the earth is painted, floor or not.
            Assert.NotEmpty(EarthFloor.Spots(new[] { P("wood_roof_45", 0f, 3f, 0f), P("wood_floor", 0f, 0.03f, 0f) }, 2f));
        }

        [Fact]
        public void One_spot_a_block_of_the_paint_s_reach()
        {
            // An 8 x 8 m roof, painted 2 m apart: one spot in each 2 x 2 m block, 16 in all.
            var roof = new List<PlannedPiece>();
            for (int x = -3; x <= 3; x += 2)
                for (int z = -3; z <= 3; z += 2)
                    roof.Add(P("wood_roof_45", x, 3f, z));
            List<GroundPoint> spots = EarthFloor.Spots(roof, 2f);
            Assert.Equal(16, spots.Count);
            Assert.Equal(16, spots.Select(s => (int)System.Math.Floor(s.X / 2f) * 100 + (int)System.Math.Floor(s.Z / 2f)).Distinct().Count());
        }
    }
}
