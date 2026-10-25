using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Who carries what (1.5): the claims the storehouse's owner keeps, saved on the storehouse.</summary>
    public class CourierClaimsTests
    {
        private static readonly CourierTask T1 = new CourierTask { Kind = CourierTaskKind.Deliver, From = "store", To = "hall", Item = "Raspberry", Need = StockList.AnyFood, Amount = 10 };
        private static readonly CourierTask T2 = new CourierTask { Kind = CourierTaskKind.Pickup, From = "lumber", To = "store", Amount = 50 };

        [Fact]   // Review Focus 1: two couriers, two computers, never one task
        public void Two_couriers_never_get_one_task()
        {
            var claims = new CourierClaims();
            var plan = new List<CourierTask> { T1, T2 };
            Assert.Equal(T1.Key, claims.Assign(plan, "a", 0).Key);
            Assert.Equal(T2.Key, claims.Assign(plan, "b", 0).Key);
            Assert.Null(claims.Assign(plan, "c", 0));
        }

        [Fact]
        public void A_courier_keeps_its_task_until_released()
        {
            var claims = new CourierClaims();
            claims.Assign(new List<CourierTask> { T1, T2 }, "a", 0);
            Assert.Equal(T1.Key, claims.Assign(new List<CourierTask> { T2, T1 }, "a", 5).Key);
            claims.Release("a");
            Assert.Null(claims.For("a"));
            Assert.Equal(T2.Key, claims.Assign(new List<CourierTask> { T2, T1 }, "a", 6).Key);
        }

        [Fact]
        public void A_task_held_too_long_is_freed()
        {
            var claims = new CourierClaims();
            claims.Assign(new List<CourierTask> { T1 }, "a", 0);
            Assert.Equal(0, claims.Expire(CourierClaims.HoldSeconds - 1));
            Assert.Equal(1, claims.Expire(CourierClaims.HoldSeconds + 1));
            Assert.Equal(T1.Key, claims.Assign(new List<CourierTask> { T1 }, "b", CourierClaims.HoldSeconds + 2).Key);
        }

        [Fact]
        public void Claims_survive_saving()
        {
            var claims = new CourierClaims();
            claims.Assign(new List<CourierTask> { T1, T2 }, "a", 12.5);
            claims.Assign(new List<CourierTask> { T1, T2 }, "b", 13);
            CourierClaims copy = CourierClaims.Decode(claims.Encode());
            Assert.Equal(T1.Key, copy.For("a").Key);
            Assert.Equal(T2.Key, copy.For("b").Key);
            Assert.Equal(1, copy.Expire(12.5 + CourierClaims.HoldSeconds + 0.1));   // when it was given survives too
            Assert.Null(CourierClaims.Decode("rubbish\tnot\ta task").For("rubbish"));
            Assert.Equal("", new CourierClaims().Encode());
        }

        [Fact]   // I6: a Done lost when the storehouse changed computers gave the courier its finished task again
        public void A_task_the_courier_says_it_finished_is_let_go_and_not_given_again()
        {
            var claims = new CourierClaims();
            var plan = new List<CourierTask> { T1, T2 };
            claims.Assign(plan, "a", 0);
            Assert.Equal(T2.Key, claims.Assign(plan, "a", 5, lastDone: T1.Key).Key);   // the plan is old: T1 is skipped this once
            Assert.Equal(5, claims.Since("a"));
            Assert.Equal(T1.Key, claims.Assign(plan, "b", 6, lastDone: T1.Key).Key);   // another courier's last isn't this one's
        }

        [Fact]
        public void A_fired_courier_lets_its_task_go()
        {
            var claims = new CourierClaims();
            claims.Assign(new List<CourierTask> { T1 }, "a", 0);
            claims.Forget(id => id != "a");
            Assert.Null(claims.For("a"));
            Assert.Equal(T1.Key, claims.Assign(new List<CourierTask> { T1 }, "b", 1).Key);
        }
    }
}
