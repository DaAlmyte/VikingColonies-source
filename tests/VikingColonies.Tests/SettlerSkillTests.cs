using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class SettlerSkillTests
    {
        [Theory]
        [InlineData(0f, 0f, 0.25f)]    // a new character: 25-55 % of the axe's damage
        [InlineData(0f, 1f, 0.55f)]
        [InlineData(50f, 0.5f, 0.7f)]
        [InlineData(100f, 0f, 0.85f)]  // a master: 85-100 %
        [InlineData(100f, 1f, 1f)]
        public void Damage_grows_with_skill_like_the_players(float level, float random, float expected)
        {
            Assert.Equal(expected, SettlerSkill.DamageFactor(level, random), 3);
        }

        [Theory]
        [InlineData(0f, 1f)]
        [InlineData(1f, 1.914f)]
        [InlineData(10f, 18.742f)]
        public void Each_level_needs_the_games_own_amount_of_practice(float level, float expected)
        {
            Assert.Equal(expected, SettlerSkill.NextLevelRequirement(level), 2);
        }

        [Fact]
        public void Practice_builds_up_and_raises_one_level_at_a_time()
        {
            float level = 1f, progress = 0f;
            Assert.False(SettlerSkill.Raise(ref level, ref progress, 1f));
            Assert.Equal(1f, level);
            Assert.Equal(1f, progress);
            Assert.True(SettlerSkill.Raise(ref level, ref progress, 1f));
            Assert.Equal(2f, level);
            Assert.Equal(0f, progress);
        }

        [Fact]
        public void At_a_quarter_learning_rate_it_takes_four_hits_to_learn_what_a_player_learns_from_one()
        {
            float level = 0f, progress = 0f;
            float perHit = SettlerSkill.PracticePerHit(0.25f);
            for (int hit = 1; hit <= 3; hit++) Assert.False(SettlerSkill.Raise(ref level, ref progress, perHit));
            Assert.True(SettlerSkill.Raise(ref level, ref progress, perHit));
            Assert.Equal(1f, level);
        }

        [Fact]
        public void A_master_stops_improving()
        {
            float level = 100f, progress = 0f;
            Assert.False(SettlerSkill.Raise(ref level, ref progress, 50f));
            Assert.Equal(100f, level);
        }
    
        [Fact]   // 0.22: a skill of 100 works twice as fast as 0
        public void Speed_doubles_at_100()
        {
            Assert.Equal(1f, SettlerSkill.SpeedFactor(0f), 3);
            Assert.Equal(1.5f, SettlerSkill.SpeedFactor(50f), 3);
            Assert.Equal(2f, SettlerSkill.SpeedFactor(100f), 3);
            Assert.Equal(2f, SettlerSkill.SpeedFactor(150f), 3);
            Assert.Equal(1f, SettlerSkill.SpeedFactor(-5f), 3);
        }
}
}
