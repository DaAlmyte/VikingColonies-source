using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class ConfigNamesTests
    {
        [Fact]
        public void No_section_or_key_uses_a_character_BepInEx_refuses()
        {
            char[] refused = { '=', '\n', '\t', '\\', '"', '\'', '[', ']' };
            Assert.All(ConfigNames.All, name => Assert.True(name.IndexOfAny(refused) < 0, $"'{name}' has a refused character"));
        }

        [Fact]
        public void The_builder_settings_have_names_in_the_list()
        {
            Assert.Contains("Builder Hut", ConfigNames.All);
            Assert.Contains("Builder", ConfigNames.All);
            Assert.Contains("Reach", ConfigNames.All);
            Assert.Contains("SecondsPerPiece", ConfigNames.All);
        }

        [Fact]
        public void The_creative_settings_have_names_in_the_list()
        {
            Assert.Contains("Creative", ConfigNames.All);
            Assert.Contains("Storehouse", ConfigNames.All);   // 1.5
            Assert.Contains("Courier", ConfigNames.All);
            Assert.Contains("CarryPerLevel", ConfigNames.All);
            Assert.Contains("CreativeMode", ConfigNames.All);
            Assert.Contains("LevelUpKey", ConfigNames.All);
            Assert.Contains("LevelDownKey", ConfigNames.All);
        }

        [Fact]
        public void The_death_settings_have_names_in_the_list()
        {
            Assert.Contains(ConfigNames.MessagesSection, ConfigNames.All);
            Assert.Contains(ConfigNames.SettlersSafeFromTrees, ConfigNames.All);
            Assert.Contains(ConfigNames.DeathMessage, ConfigNames.All);
        }

        [Fact]
        public void The_raise_and_lower_keys_have_names_in_the_list()
        {
            Assert.Contains(ConfigNames.PlacingSection, ConfigNames.All);
            Assert.Contains(ConfigNames.RaiseKey, ConfigNames.All);
            Assert.Contains(ConfigNames.LowerKey, ConfigNames.All);
        }

        [Fact]
        public void The_free_time_settings_have_names_in_the_list()
        {
            Assert.Contains("FreeTime", ConfigNames.All);
            Assert.Contains("SmallTalk", ConfigNames.All);
        }

        [Fact]
        public void The_day_settings_have_names_in_the_list()
        {
            Assert.Contains("EveningGathering", ConfigNames.All);
            Assert.Contains("SleepInBeds", ConfigNames.All);
            Assert.Contains("TendFires", ConfigNames.All);
        }

        [Fact]
        public void Carrying_to_stock_has_its_name_in_the_list()
        {
            Assert.Equal("CarryToStock", ConfigNames.CarryToStock);
            Assert.Contains("CarryToStock", ConfigNames.All);
        }

        [Fact]
        public void The_survival_settings_have_names_in_the_list()
        {
            Assert.Contains("SettlersGoIndoorsAtNight", ConfigNames.All);
            Assert.Contains("SettlersShelterInRaids", ConfigNames.All);
        }
    
        [Fact]   // 0.22
        public void The_lumberjack_tree_setting_has_a_name_in_the_list() =>
            Assert.Contains(ConfigNames.LeaveTreesNearBuildings, ConfigNames.All);
}
}
