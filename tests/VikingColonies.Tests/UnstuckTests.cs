using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // A settler whose way is open on the game's paths but who doesn't move (a log behind his hut, another settler in a
    // doorway) pushed on until his job gave up.
    public class UnstuckTests
    {
        private static List<(double At, UnstuckMove Move, int Side)> Walk(Unstuck unstuck, double seconds, System.Func<double, float> x)
        {
            var moves = new List<(double, UnstuckMove, int)>();
            for (double t = 0; t <= seconds + 1e-6; t += 0.25)
            {
                UnstuckMove move = unstuck.Check(x(t), 0f, t);
                if (move != UnstuckMove.None) moves.Add((t, move, unstuck.Side));
            }
            return moves;
        }

        [Fact]
        public void A_settler_who_keeps_moving_needs_nothing()
        {
            Assert.Empty(Walk(new Unstuck(), 20, t => (float)t * 2f));
        }

        [Fact]
        public void A_settler_who_doesnt_move_hops_then_steps_aside_alternating_sides()
        {
            var moves = Walk(new Unstuck(), 10.5, t => 0f);
            Assert.Equal(new[]
            {
                (3.0, UnstuckMove.Hop, 1),
                (5.0, UnstuckMove.Sidestep, 1),
                (8.0, UnstuckMove.Hop, -1),
                (10.0, UnstuckMove.Sidestep, -1),
            }, moves.ToArray());
        }

        // A settler blocked for good (seen: one hopping every few seconds 20 m from her goal) stops trying after two rounds.
        [Fact]
        public void After_two_rounds_he_stops_trying_until_he_moves()
        {
            var unstuck = new Unstuck();
            Assert.Equal(4, Walk(unstuck, 30, t => 0f).Count);
            var later = Walk(unstuck, 6, t => t < 1 ? 5f : 5f);   // he moved 5 m: the rounds start over
            Assert.Equal(UnstuckMove.Hop, later[0].Move);
        }

        // A crowd at the goal isn't stuck: standing there is fine.
        [Fact]
        public void Near_his_goal_he_doesnt_hop_or_step_aside()
        {
            var unstuck = new Unstuck();
            for (double t = 0; t <= 20; t += 0.25)
                Assert.Equal(UnstuckMove.None, unstuck.Check(0f, 0f, t, toGoal: 2f));
        }

        [Fact]
        public void Moving_again_restarts_the_clock()
        {
            // Still for 2.5 s, then a step of a metre, then still: the hop comes 3 s after the step.
            var moves = Walk(new Unstuck(), 6, t => t < 2.5 ? 0f : 1f);
            Assert.Equal(UnstuckMove.Hop, moves[0].Move);
            Assert.Equal(5.5, moves[0].At, 3);
        }

        [Fact]
        public void A_pause_in_walking_restarts_the_clock()
        {
            var unstuck = new Unstuck();
            for (double t = 0; t < 2.6; t += 0.25) unstuck.Check(0f, 0f, t);   // walking, still, for 2.5 s
            var moves = new List<(double, UnstuckMove)>();
            for (double t = 10; t <= 13.01; t += 0.25)                         // he stood idle, then walks again, still
            {
                UnstuckMove move = unstuck.Check(0f, 0f, t);
                if (move != UnstuckMove.None) moves.Add((t, move));
            }
            Assert.Equal(new[] { (13.0, UnstuckMove.Hop) }, moves.ToArray());   // 3 s after walking again, not at once
        }
    }
}
