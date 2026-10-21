using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>1.4.2: asking players what they think, once, at a good moment (the feedback design of 2026-10-20).</summary>
    public class FeedbackAskTests
    {
        [Fact]
        public void A_hut_seen_going_from_level_1_to_2_is_the_moment()
        {
            var ask = new FeedbackAsk();
            ask.SawHut("hut", 1);
            Assert.False(ask.HutReachedLevel2);
            ask.SawHut("hut", 2);
            Assert.True(ask.HutReachedLevel2);
        }

        [Fact]
        public void A_hut_first_seen_at_level_2_or_more_is_not()
        {
            var ask = new FeedbackAsk();
            ask.SawHut("old", 2);
            ask.SawHut("old", 2);
            ask.SawHut("older", 3);
            Assert.False(ask.HutReachedLevel2);
        }

        [Fact]
        public void Later_upgrades_and_other_huts_staying_at_1_are_not()
        {
            var ask = new FeedbackAsk();
            ask.SawHut("a", 2);
            ask.SawHut("a", 3);
            ask.SawHut("b", 1);
            ask.SawHut("b", 1);
            Assert.False(ask.HutReachedLevel2);
            ask.SawHut("b", 2);
            Assert.True(ask.HutReachedLevel2);
        }

        [Fact]
        public void A_hut_without_an_id_is_not_counted()
        {
            var ask = new FeedbackAsk();
            ask.SawHut(null, 1);
            ask.SawHut(null, 2);
            ask.SawHut("", 1);
            ask.SawHut("", 2);
            Assert.False(ask.HutReachedLevel2);
        }

        [Fact]
        public void Due_at_the_first_level_2_or_after_two_hours_with_a_colony()
        {
            Assert.Equal(7200, FeedbackAsk.PlaySeconds);
            Assert.False(FeedbackAsk.Due(false, 7199));
            Assert.True(FeedbackAsk.Due(false, 7200));
            Assert.True(FeedbackAsk.Due(true, 0));
        }

        [Fact]
        public void Shown_when_due_on_and_not_asked_with_no_enemy_window_or_raid()
        {
            Assert.True(FeedbackAsk.ShowNow(due: true, enabled: true, asked: false, enemyNear: false, inWindow: false, raid: false));
        }

        [Fact]
        public void Never_twice_never_when_off_and_never_before_it_is_due()
        {
            Assert.False(FeedbackAsk.ShowNow(due: true, enabled: true, asked: true, enemyNear: false, inWindow: false, raid: false));
            Assert.False(FeedbackAsk.ShowNow(due: true, enabled: false, asked: false, enemyNear: false, inWindow: false, raid: false));
            Assert.False(FeedbackAsk.ShowNow(due: false, enabled: true, asked: false, enemyNear: false, inWindow: false, raid: false));
        }

        [Fact]
        public void It_waits_while_an_enemy_is_near_a_window_is_open_or_a_raid_is_on()
        {
            Assert.False(FeedbackAsk.ShowNow(due: true, enabled: true, asked: false, enemyNear: true, inWindow: false, raid: false));
            Assert.False(FeedbackAsk.ShowNow(due: true, enabled: true, asked: false, enemyNear: false, inWindow: true, raid: false));
            Assert.False(FeedbackAsk.ShowNow(due: true, enabled: true, asked: false, enemyNear: false, inWindow: false, raid: true));
        }
    }

    /// <summary>1.4.2, the review's fix pass: what holds the ask back, and which worlds are ours to test in.</summary>
    public class FeedbackMomentTests
    {
        [Fact]
        public void A_monster_within_30_m_holds_it_back()
        {
            Assert.True(FeedbackAsk.Threat(29f, hostile: true, monster: true, animal: false, targetsPlayer: false));
            Assert.False(FeedbackAsk.Threat(31f, hostile: true, monster: true, animal: false, targetsPlayer: false));
        }

        [Fact]
        public void Deer_boars_and_settlers_minding_their_own_business_dont()
        {
            Assert.False(FeedbackAsk.Threat(5f, hostile: true, monster: true, animal: true, targetsPlayer: false));    // a boar
            Assert.False(FeedbackAsk.Threat(5f, hostile: true, monster: false, animal: true, targetsPlayer: false));   // a deer
            Assert.False(FeedbackAsk.Threat(5f, hostile: false, monster: false, animal: false, targetsPlayer: false)); // a settler
        }

        [Fact]
        public void Anything_after_the_player_within_60_m_does_an_archer_or_a_charging_boar()
        {
            Assert.True(FeedbackAsk.Threat(55f, hostile: true, monster: true, animal: false, targetsPlayer: true));
            Assert.True(FeedbackAsk.Threat(10f, hostile: true, monster: true, animal: true, targetsPlayer: true));
            Assert.False(FeedbackAsk.Threat(61f, hostile: true, monster: true, animal: false, targetsPlayer: true));
        }

        [Fact]
        public void Only_our_own_test_worlds_wait_to_be_armed_not_a_players_world_that_says_test()
        {
            Assert.True(FeedbackAsk.OurTestWorld("VCTest"));
            Assert.True(FeedbackAsk.OurTestWorld("vctestflat"));
            Assert.True(FeedbackAsk.OurTestWorld("VCTestRun-20261020-140255"));
            foreach (string? world in new[] { "Greatest", "Latest", "Contest", "Fastest", "MyTestWorld", "", null })
                Assert.False(FeedbackAsk.OurTestWorld(world));
        }
    }

    /// <summary>1.4.2: the small file next to the mod's config that makes the ask once per computer.</summary>
    public class FeedbackMemoryTests
    {
        [Fact]
        public void Written_and_read_back()
        {
            var memory = new FeedbackMemory { Asked = true, PlayedSeconds = 3600 };
            FeedbackMemory back = FeedbackMemory.Read(memory.Write());
            Assert.True(back.Asked);
            Assert.Equal(3600, back.PlayedSeconds);
        }

        [Fact]
        public void Nothing_or_garbage_reads_as_not_asked_and_nothing_played()
        {
            foreach (string? text in new[] { null, "", "nonsense", "asked=maybe\nplayed=lots", "\0\0" })
            {
                FeedbackMemory memory = FeedbackMemory.Read(text);
                Assert.False(memory.Asked);
                Assert.Equal(0, memory.PlayedSeconds);
            }
        }

        [Fact]
        public void Played_time_that_makes_no_sense_reads_as_none()
        {
            foreach (string played in new[] { "-5", "NaN", "Infinity", "1e400" })
                Assert.Equal(0, FeedbackMemory.Read("played=" + played).PlayedSeconds);
        }

        [Fact]
        public void Unknown_lines_and_windows_line_ends_are_fine()
        {
            FeedbackMemory memory = FeedbackMemory.Read("# VikingColonies\r\nsomething=else\r\nasked=1\r\nplayed=120\r\n");
            Assert.True(memory.Asked);
            Assert.Equal(120, memory.PlayedSeconds);
        }

        [Fact]
        public void Play_time_adds_up_but_not_a_negative_or_a_long_hitch()
        {
            var memory = new FeedbackMemory();
            memory.AddPlayed(2);
            memory.AddPlayed(-3);
            memory.AddPlayed(double.NaN);
            memory.AddPlayed(FeedbackMemory.MaxStep + 1);
            Assert.Equal(2, memory.PlayedSeconds);
        }
    }

    /// <summary>1.4.2: where the Feedback window's two buttons go. Nothing is sent from the game; they open a page.</summary>
    public class FeedbackLinksTests
    {
        [Fact]
        public void Discord_is_the_servers_invite()
        {
            Assert.Equal("https://discord.gg/yCzDUPQA3D", FeedbackLinks.Discord);
        }

        [Fact]
        public void GitHub_is_the_feedback_form_with_the_versions_filled_in()
        {
            Assert.Equal("https://github.com/DaAlmyte/VikingColonies-issues/issues/new?template=feedback.yml&versions=VikingColonies%201.4.2%2C%20Valheim%200.220.5",
                FeedbackLinks.GitHub("1.4.2", "0.220.5"));
        }

        [Fact]
        public void Odd_versions_are_escaped_and_cannot_add_fields()
        {
            string address = FeedbackLinks.GitHub("1.4.2&title=x", null);
            Assert.DoesNotContain("&title=", address);
            Assert.StartsWith("https://github.com/DaAlmyte/VikingColonies-issues/issues/new?template=feedback.yml&versions=", address);
        }
    }
}
