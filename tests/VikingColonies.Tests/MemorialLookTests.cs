using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The memorial stone's look (1.6.0): which of the game's runestone places it takes its stone from, and how it stands.</summary>
    public class MemorialLookTests
    {
        [Fact]
        public void The_meadows_runestone_comes_first_then_the_other_runestones_by_name()
        {
            var names = new[] { "Dolmen01", "Runestone_Swamps", "RuneStone_Boars", "Runestone_Meadows", "Crypt2", "Runestone_BlackForest" };
            Assert.Equal(new[] { "Runestone_Meadows", "Runestone_BlackForest", "RuneStone_Boars", "Runestone_Swamps" }, MemorialLook.Candidates(names));
        }

        [Fact]
        public void A_world_without_runestones_has_no_candidates_and_a_name_is_taken_once()
        {
            Assert.Empty(MemorialLook.Candidates(new[] { "StartTemple", "Dolmen01", null, "" }));
            Assert.Equal(new[] { "Runestone_Meadows" }, MemorialLook.Candidates(new[] { "Runestone_Meadows", "Runestone_Meadows" }));
            Assert.Empty(MemorialLook.Candidates(null));
        }

        [Fact]
        public void The_stone_is_lifted_so_its_foot_sits_a_little_in_the_ground()
        {
            // A stone whose model reaches 1.2 below its pivot (buried in its own place) is lifted so only its foot is under.
            Assert.Equal(1.2f - MemorialLook.Sink, MemorialLook.Lift(-1.2f), 3);
            // One whose foot is above its pivot comes down to the ground.
            Assert.Equal(-0.3f - MemorialLook.Sink, MemorialLook.Lift(0.3f), 3);
        }
    }
}
