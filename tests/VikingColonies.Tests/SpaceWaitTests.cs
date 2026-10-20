using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The real builders' runs (2026-10-18/19): the Town Hall camp is where the colony gathers, and five settlers stood where
    /// its bonfire goes; in the whole run its level 5 never finished. The builder waited, silently and for good, for them
    /// to move. Now he waits a little, then works on other pieces and tries that one again later.
    /// </summary>
    public class SpaceWaitTests
    {
        [Fact]
        public void He_waits_a_little_for_a_player_or_an_animal_to_move_then_puts_the_piece_aside()
        {
            Assert.Equal(SpaceStep.Wait, SpaceWait.Next(0f, 0, settlers: false));
            Assert.Equal(SpaceStep.Wait, SpaceWait.Next(SpaceWait.WaitSeconds - 0.1f, 0, settlers: false));
            Assert.Equal(SpaceStep.PutAside, SpaceWait.Next(SpaceWait.WaitSeconds, 0, settlers: false));
        }

        [Fact]
        public void A_piece_put_aside_is_tried_again_after_a_while()
        {
            Assert.True(SpaceWait.AsideSeconds > SpaceWait.WaitSeconds);
            Assert.True(SpaceWait.AsideSeconds <= 60f);   // a gathering breaks up; he comes back to it soon
        }

        // The old huts' third rerun (2026-10-19): the forager stayed in his own hut while it was upgraded, working, resting
        // and sitting where a roof piece went; the builder put it aside and came back to it for 30 minutes. A settler is
        // asked to make way; one still there after that is moved out by the door; then the piece waits as before.
        [Fact]
        public void A_settler_in_the_way_is_asked_to_make_way_then_moved_out_then_the_piece_waits()
        {
            Assert.Equal(SpaceStep.Wait, SpaceWait.Next(SpaceWait.WaitSeconds - 0.1f, 0, settlers: true));
            Assert.Equal(SpaceStep.AskToMove, SpaceWait.Next(SpaceWait.WaitSeconds, 0, settlers: true));
            Assert.Equal(SpaceStep.Wait, SpaceWait.Next(SpaceWait.WaitSeconds - 0.1f, 1, settlers: true));   // his time to walk out
            Assert.Equal(SpaceStep.MoveOut, SpaceWait.Next(SpaceWait.WaitSeconds, 1, settlers: true));
            Assert.Equal(SpaceStep.PutAside, SpaceWait.Next(SpaceWait.WaitSeconds, 2, settlers: true));
        }

        [Fact]
        public void One_who_made_way_keeps_away_long_enough_for_the_piece_but_not_for_long()
        {
            Assert.True(SpaceWait.KeepAwaySeconds >= SpaceWait.WaitSeconds + 10f);   // walking out, then the builder's swing
            Assert.True(SpaceWait.KeepAwaySeconds <= 60f);                           // then back to his work
        }
    }
}
