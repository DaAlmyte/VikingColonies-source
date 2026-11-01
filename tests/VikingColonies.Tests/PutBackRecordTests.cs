using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>An upgrade's put-backs, saved with its order (1.6, the own-pieces design's point 4).</summary>
    public class PutBackRecordTests
    {
        private static PutBackRecord Torch() => new PutBackRecord("piece_walltorch", new Vec3(1.5f, 31.25f, -2.75f),
            new Quat(0f, 0.3826834f, 0f, 0.9238795f), 1234567890123L, "$piece_walltorch");

        [Fact]
        public void Put_backs_are_saved_with_the_order()
        {
            var order = new BuildOrderRecord { Kind = OrderKind.Upgrade, Total = 3 };
            order.PutBacks.Add(Torch());
            PutBackRecord chest = Torch();
            chest.State = PutBackState.Down;
            chest.Failures = 2;
            order.PutBacks.Add(chest);

            BuildOrderRecord back = BuildOrderRecord.Decode(order.Encode());
            Assert.Equal(2, back.PutBacks.Count);
            PutBackRecord torch = back.PutBacks[0];
            Assert.Equal("piece_walltorch", torch.Prefab);
            Assert.Equal(31.25f, torch.Position.Y);
            Assert.Equal(0.3826834f, torch.Rotation.Y);
            Assert.Equal(1234567890123L, torch.Creator);
            Assert.Equal("$piece_walltorch", torch.Name);
            Assert.Equal(PutBackState.Standing, torch.State);
            Assert.Equal(PutBackState.Down, back.PutBacks[1].State);
            Assert.Equal(2, back.PutBacks[1].Failures);
        }

        [Fact]
        public void An_order_from_before_has_no_put_backs_and_a_damaged_one_is_left_out()
        {
            var order = new BuildOrderRecord { Kind = OrderKind.Upgrade, Total = 1 };
            Assert.DoesNotContain("back=", order.Encode());   // without put-backs it is written as before
            order.PutBacks.Add(Torch());
            Assert.Empty(BuildOrderRecord.Decode(order.Encode().Replace("|back=", "|old=")).PutBacks);   // a save from before

            order.PutBacks.Add(Torch());
            string damaged = order.Encode().Replace("1.5,31.25,-2.75", "NaN,31.25,-2.75");
            Assert.Empty(BuildOrderRecord.Decode(damaged).PutBacks);   // both carried the same place: both left out
            Assert.Empty(BuildOrderRecord.Decode(order.Encode().Replace("piece_walltorch", "")).PutBacks);   // no kind
        }

        [Fact]
        public void An_upgrade_isnt_done_until_its_put_backs_are_back()
        {
            var order = new BuildOrderRecord { Kind = OrderKind.Upgrade, Total = 1 };
            order.MarkDone(0);
            order.PutBacks.Add(Torch());
            Assert.False(order.AllHandled);
            Assert.Equal(1, order.PutBacksLeft);
            order.PutBacks[0].State = PutBackState.Down;
            Assert.False(order.AllHandled);
            order.PutBacks[0].State = PutBackState.Back;
            Assert.True(order.AllHandled);
            order.PutBacks[0].State = PutBackState.Skipped;   // it fell three times: left out, its materials kept
            Assert.True(order.AllHandled);
        }

        [Fact]
        public void A_put_back_that_keeps_failing_is_skipped()
        {
            PutBackRecord torch = Torch();
            Assert.False(torch.MarkFailed());
            Assert.False(torch.MarkFailed());
            Assert.True(torch.MarkFailed());
            Assert.Equal(PutBackState.Skipped, torch.State);
        }

        [Fact]
        public void A_put_back_only_moves_on_and_one_taken_down_ends_the_cancel()
        {
            var order = new BuildOrderRecord { Kind = OrderKind.Upgrade, Total = 1 };
            order.PutBacks.Add(Torch());
            order.PutBacks.Add(Torch());
            Assert.False(order.MovePutBack(2, PutBackState.Down));   // no such put-back
            Assert.False(order.MovePutBack(-1, PutBackState.Down));

            Assert.True(order.MovePutBack(1, PutBackState.Skipped));   // filled since the order: left standing
            Assert.False(order.Changed);   // nothing of the hut's site changed: the upgrade can still be cancelled

            Assert.True(order.MovePutBack(0, PutBackState.Down));
            Assert.True(order.Changed);   // a cancel would lose it: the upgrade can't be cancelled now
            Assert.False(order.MovePutBack(0, PutBackState.Standing));   // never back to an earlier state
            Assert.False(order.MovePutBack(0, PutBackState.Down));
            Assert.True(order.MovePutBack(0, PutBackState.Back));
            Assert.False(order.MovePutBack(0, PutBackState.Skipped));   // back stays back
            Assert.Equal(PutBackState.Back, order.PutBacks[0].State);
            Assert.False(order.MovePutBack(1, PutBackState.Back));   // skipped stays skipped
            order.PutBacks.Add(Torch());
            Assert.False(order.MovePutBack(2, (PutBackState)7));   // a state from a newer version: ignored
            Assert.Equal(PutBackState.Standing, order.PutBacks[2].State);
        }

        [Fact]
        public void A_put_back_taken_down_keeps_what_it_held_through_a_save()
        {
            // A sign's text, a portal's tag, a torch's fuel (the review's finding 1): saved as it comes down, set on it as
            // it goes back. Whatever the text, it comes back the same; a save from before has none.
            var order = new BuildOrderRecord { Kind = OrderKind.Upgrade, Total = 1 };
            order.PutBacks.Add(Torch());
            order.PutBacks.Add(Torch());
            const string kept = "text=Mead | here; a=b\nnext \u00e5\u00e4\u00f6|back=x";
            Assert.True(order.MovePutBack(0, PutBackState.Down, kept));
            BuildOrderRecord back = BuildOrderRecord.Decode(order.Encode());
            Assert.Equal(kept, back.PutBacks[0].Kept);
            Assert.Equal(PutBackState.Down, back.PutBacks[0].State);
            Assert.Equal("", back.PutBacks[1].Kept);
            Assert.Equal(2, back.PutBacks.Count);
            Assert.True(back.MovePutBack(0, PutBackState.Back, "text=other"));   // only the take-down's values count
            Assert.Equal(kept, back.PutBacks[0].Kept);

            var plain = new BuildOrderRecord { Kind = OrderKind.Upgrade, Total = 1 };
            plain.PutBacks.Add(Torch());
            Assert.Contains(@"pk\\", order.Encode());          // the put-back's own "k=", escaped in the order's list
            Assert.DoesNotContain(@"pk\\", plain.Encode());   // none kept: written as before
            Assert.Equal("", BuildOrderRecord.Decode(plain.Encode()).PutBacks[0].Kept);
        }

        [Fact]
        public void One_never_taken_down_is_left_out_but_one_another_computer_took_down_isnt()
        {
            // A builder elsewhere whose copy still says standing can't find it: "left out" only while it is standing here
            // too (the review's finding 5), never overruling "down" (its materials would drop while it waits to go back).
            var order = new BuildOrderRecord { Kind = OrderKind.Upgrade, Total = 1 };
            order.PutBacks.Add(Torch());
            order.PutBacks.Add(Torch());
            Assert.True(order.LeaveOut(0));
            Assert.Equal(PutBackState.Skipped, order.PutBacks[0].State);
            Assert.False(order.Changed);
            Assert.True(order.MovePutBack(1, PutBackState.Down));
            Assert.False(order.LeaveOut(1));
            Assert.Equal(PutBackState.Down, order.PutBacks[1].State);
            Assert.False(order.LeaveOut(0));   // already left out
            Assert.False(order.LeaveOut(5));
        }

        [Fact]
        public void A_report_counts_only_for_the_upgrade_it_was_made_for()
        {
            // A late report after a cancel and a new upgrade would hit another order's put-back (the review's finding 5).
            var order = new BuildOrderRecord { Kind = OrderKind.Upgrade, Total = 1, Created = 52713.123456789 };
            long stamp = order.Stamp;
            Assert.True(BuildOrderRecord.Decode(order.Encode()).IsStamped(stamp));   // the same order on another computer
            Assert.False(new BuildOrderRecord { Kind = OrderKind.Upgrade, Total = 1, Created = 52716.5 }.IsStamped(stamp));
            Assert.False(new BuildOrderRecord { Kind = OrderKind.Upgrade, Total = 1, Created = 52713.2 }.IsStamped(stamp));
        }

        [Fact]
        public void A_repair_and_a_take_down_have_none()
        {
            var order = new BuildOrderRecord { Kind = OrderKind.Upgrade, Total = 1, State = BuildState.Done };
            order.MarkDone(0);
            order.PutBacks.Add(Torch());
            Assert.True(order.ReopenForRepair(new[] { 0 }, 0));
            Assert.Empty(order.PutBacks);
            Assert.Empty(BuildOrderRecord.ForRemove(10, 2, 0).PutBacks);
        }

        [Fact]
        public void A_saved_list_of_put_backs_is_cut_to_the_most_an_order_counts()
        {
            var order = new BuildOrderRecord { Kind = OrderKind.Upgrade };
            for (int i = 0; i < BuildOrderRecord.MaxPutBacks + 5; i++)
                order.PutBacks.Add(new PutBackRecord("piece_bench01", new Vec3(i, 0f, 0f), Quat.Identity, 1L, "$piece_bench"));
            Assert.Equal(BuildOrderRecord.MaxPutBacks, BuildOrderRecord.Decode(order.Encode()).PutBacks.Count);
            Assert.Equal(BuildOrderRecord.MaxPutBacks, BuildOrderRecord.Decode(order.Encode()).PutBacks.Select(p => p.Position.X).Distinct().Count());
        }
    }
}
