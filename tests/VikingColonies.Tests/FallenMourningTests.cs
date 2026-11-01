using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The fallen return (1.5.1): a settler who returns is no longer mourned.</summary>
    public class FallenMourningTests
    {
        [Fact]
        public void The_saga_knows_a_return() => Assert.True(SagaLog.IsKnownKind("fallenback"));

        [Fact]
        public void A_return_ends_mourning()
        {
            var saga = new SagaLog();
            saga.Add(new SagaEntry(39, "died", "Ulf", "$vc_cause_killed_by", "Greydwarf"));
            saga.Add(new SagaEntry(40, "fallenback", "Ulf"));
            Assert.Null(MoodSaga.RecentDeath(saga, 40));
            Assert.Equal("", SmallTalk.FromSaga(saga, 40, "Arne").DeadName);
        }

        [Fact]
        public void A_return_ends_mourning_only_for_the_death_before_it()
        {
            var saga = new SagaLog();
            saga.Add(new SagaEntry(39, "died", "Ulf", "$vc_cause_killed_by", "Greydwarf"));
            saga.Add(new SagaEntry(40, "fallenback", "Ulf"));
            saga.Add(new SagaEntry(41, "died", "Ulf", "$vc_cause_drowned", ""));
            Assert.Equal("Ulf", MoodSaga.RecentDeath(saga, 41));
            Assert.Equal("Ulf", SmallTalk.FromSaga(saga, 41, "Arne").DeadName);
        }

        [Fact]
        public void Another_settlers_return_leaves_the_mourning()
        {
            var saga = new SagaLog();
            saga.Add(new SagaEntry(39, "died", "Ulf", "$vc_cause_killed_by", "Greydwarf"));
            saga.Add(new SagaEntry(40, "fallenback", "Gudrun"));
            Assert.Equal("Ulf", MoodSaga.RecentDeath(saga, 40));
        }
    }
}
