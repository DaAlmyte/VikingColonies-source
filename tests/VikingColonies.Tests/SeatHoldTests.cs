using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class SeatHoldTests
    {
        [Fact]
        public void Nobody_is_held_before_sitting_down()
        {
            var hold = new SeatHold();
            Assert.False(hold.Holding);
            Assert.Equal(SeatStep.Nothing, hold.Step(owner: true, seatThere: true, distance: 0f));
        }

        [Fact]
        public void A_settler_on_a_seat_that_stands_is_kept_on_it()
        {
            var hold = new SeatHold();
            hold.Start();
            Assert.Equal(SeatStep.Keep, hold.Step(owner: true, seatThere: true, distance: 0.05f));
        }

        [Fact]
        public void A_seat_taken_away_under_them_lets_them_go_and_they_are_still_held_until_let_go()
        {
            // The builders' test of 27 Oct: the camp's log bench taken down under three settlers, who stayed held as seated
            // (no gravity, "attached" for the game) and were found 12-17 m under the ground.
            var hold = new SeatHold();
            hold.Start();
            Assert.Equal(SeatStep.LetGo, hold.Step(owner: true, seatThere: false, distance: float.NaN));
            Assert.True(hold.Holding);   // until the caller lets go: whether they're held never depends on the seat still being there
            hold.Stop();
            Assert.False(hold.Holding);
            Assert.Equal(SeatStep.Nothing, hold.Step(owner: true, seatThere: false, distance: float.NaN));
        }

        [Fact]
        public void Pushed_off_their_seat_they_stand_up()
        {
            var hold = new SeatHold();
            hold.Start();
            Assert.Equal(SeatStep.LetGo, hold.Step(owner: true, seatThere: true, distance: SeatHold.SlipDistance + 0.01f));
        }

        [Fact]
        public void Another_computer_running_them_lets_them_go()
        {
            var hold = new SeatHold();
            hold.Start();
            Assert.Equal(SeatStep.Release, hold.Step(owner: false, seatThere: true, distance: 0f));
        }
    }
}
