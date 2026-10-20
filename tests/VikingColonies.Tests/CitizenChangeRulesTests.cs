using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// Checks the computer running a citizen makes before applying a change another computer asked for, so two
    /// players acting at once can't double-hire, fire someone from a newer job, or move a citizen between colonies.
    /// </summary>
    public class CitizenChangeRulesTests
    {
        private static CitizenRecord Jobless(string colony) => new CitizenRecord { Id = "c1", ColonyId = colony };

        private static CitizenRecord Working(string colony, string workplace) =>
            new CitizenRecord { Id = "c1", ColonyId = colony, Workplace = workplace, Job = "lumberjack" };

        [Fact]
        public void A_jobless_member_of_the_huts_colony_can_be_employed()
        {
            Assert.True(ColonyRules.CanEmploy(Jobless("colA"), "colA"));
        }

        [Fact]
        public void Someone_already_working_cannot_be_employed_again()
        {
            Assert.False(ColonyRules.CanEmploy(Working("colA", "hut1"), "colA"));
        }

        [Fact]
        public void A_citizen_of_another_colony_or_none_cannot_be_employed()
        {
            Assert.False(ColonyRules.CanEmploy(Jobless("colB"), "colA"));
            Assert.False(ColonyRules.CanEmploy(Jobless(""), "colA"));
            Assert.False(ColonyRules.CanEmploy(Jobless("colA"), ""));
        }

        [Fact]
        public void Firing_only_applies_to_the_workplace_that_asked()
        {
            Assert.True(ColonyRules.CanUnemploy(Working("colA", "hut1"), "hut1"));
            Assert.False(ColonyRules.CanUnemploy(Working("colA", "hut2"), "hut1"));
            Assert.False(ColonyRules.CanUnemploy(Jobless("colA"), "hut1"));
        }

        [Fact]
        public void Only_a_citizen_without_a_colony_can_join_one()
        {
            Assert.True(ColonyRules.CanJoinColony(Jobless(""), "colA"));
            Assert.False(ColonyRules.CanJoinColony(Jobless("colB"), "colA"));
            Assert.False(ColonyRules.CanJoinColony(Jobless(""), ""));
        }
    }
}
