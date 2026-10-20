using System.IO;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Polish sweep part 1 (0.18): what the settler-life reviews set aside.</summary>
    public class PolishSettlerTests
    {
        [Fact]
        public void The_new_words_are_translated()
        {
            DirectoryInfo? dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VikingColonies.slnx"))) dir = dir.Parent;
            Assert.NotNull(dir);
            string text = File.ReadAllText(Path.Combine(dir.FullName, "src", "VikingColonies", "Config", "Translations.cs"));
            foreach (string token in new[] { "vc_here_day", "vc_here_days", "vc_came_today", "vc_home_bed_n" }) Assert.Contains("\"" + token + "\"", text);
        }

        [Fact]
        public void One_day_reads_as_one_day()
        {
            Assert.Equal("$vc_came_today", CitizenLine.DaysToken(0));
            Assert.Equal("$vc_came_today", CitizenLine.DaysToken(-2));   // the clock put back
            Assert.Equal("$vc_here_day", CitizenLine.DaysToken(1));
            Assert.Equal("$vc_here_days", CitizenLine.DaysToken(2));
        }

        [Fact]
        public void His_bed_has_a_number_in_its_Longhouse()
        {
            var beds = new[] { "long1#114", "long1#120", "long1#131" };
            Assert.Equal(2, Homes.BedNumber("long1#120", beds));
            Assert.Equal(1, Homes.BedNumber("long1#114", beds));
            Assert.Equal(0, Homes.BedNumber("long1#999", beds));
            Assert.Equal(0, Homes.BedNumber("", beds));
            Assert.Equal(0, Homes.BedNumber("long1#114", null));
        }

        [Fact]
        public void A_lazy_settler_takes_the_fire_tending_chore_half_as_often()
        {
            Assert.Equal(2, Traits.LeisureWeight(Trait.Lazy, Leisure.TendFire, 5));
            Assert.Equal(5, Traits.LeisureWeight(Trait.Hardworking, Leisure.TendFire, 5));
        }

        [Fact]
        public void A_cheerful_settler_is_cheerful_only_when_he_feels_it()
        {
            Assert.Equal(TalkTopic.Cheerful, SmallTalk.Topic(new TalkContext { Trait = Trait.Cheerful, Mood = MoodLevel.Content }, () => 0.1));
            Assert.Equal(TalkTopic.Cheerful, SmallTalk.Topic(new TalkContext { Trait = Trait.Cheerful }, () => 0.1));   // mood off
            Assert.Equal(TalkTopic.General, SmallTalk.Topic(new TalkContext { Trait = Trait.Cheerful, Mood = MoodLevel.Miserable }, () => 0.1));
            Assert.Equal(TalkTopic.Grumpy, SmallTalk.Topic(new TalkContext { Trait = Trait.Grumpy, Mood = MoodLevel.Joyful }, () => 0.1));
        }

        [Fact]
        public void A_damaged_mood_outside_0_to_100_reads_as_not_known()
        {
            const string bare = "v=2|id=abc|name=Ivar|model=0|hair=|beard=|skin=1,1,1|haircolor=1,1,1|colony=c|colonypos=|work=|workpos=|job=|carry=|" +
                                "status=|woodcutting=0,0|meal=-1|hungry=|home=|homepos=|halllost=|life=";
            Assert.Equal(Mood.Unknown, CitizenRecord.Decode(bare + "|mood=150").Mood);
            Assert.Equal(Mood.Unknown, CitizenRecord.Decode(bare + "|mood=-3").Mood);
            Assert.Equal(0, CitizenRecord.Decode(bare + "|mood=0").Mood);
            Assert.Equal(100, CitizenRecord.Decode(bare + "|mood=100").Mood);
        }

        [Fact]
        public void A_chat_partner_is_known_by_his_id_too()
        {
            // Two settlers with the same name in an old colony took the same turns (0.14 review).
            string saved = ActivityWords.Encode("$vc_doing_chat", "Ulf", "id-b");
            Assert.True(ActivityWords.Decode(saved, out string token, out string partner, out string partnerId));
            Assert.Equal("$vc_doing_chat", token);
            Assert.Equal("Ulf", partner);
            Assert.Equal("id-b", partnerId);
            Assert.True(ActivityWords.Decode("$vc_doing_chat|Ulf", out _, out string oldPartner, out string noId));   // as 0.14-0.17 wrote it
            Assert.Equal("Ulf", oldPartner);
            Assert.Equal("", noId);
            Assert.True(ActivityWords.Decode(saved, out _, out string twoArgPartner));
            Assert.Equal("Ulf", twoArgPartner);
            Assert.Equal("$vc_doing_seat", ActivityWords.Encode("$vc_doing_seat", "", ""));
        }

        [Fact]
        public void The_evening_ring_counts_the_whole_colony_the_same_on_every_computer()
        {
            // Two computers each saw only the settlers loaded there, and sent two to one spot (0.15 review).
            var roster = new[] { "c", "a", "e" };
            Assert.Equal(new[] { "a", "b", "c", "e" }, Gathering.Members(roster, new[] { "b", "a" }));
            Assert.Equal(new[] { "a", "c", "e" }, Gathering.Members(roster, new string[0]));
            Assert.Equal(new[] { "b" }, Gathering.Members(null, new[] { "b" }));
        }

        [Fact]
        public void Each_settler_goes_to_breakfast_at_his_own_moment()
        {
            // Twenty settlers reached one chest on the same tick at dawn (0.15 review).
            float a = Breakfast.Delay("a1"), b = Breakfast.Delay("b2"), c = Breakfast.Delay("c3");
            foreach (float d in new[] { a, b, c }) Assert.InRange(d, 0f, Breakfast.Spread);
            Assert.False(a == b && b == c);
            Assert.Equal(a, Breakfast.Delay("a1"));
        }

        [Fact]
        public void Hunger_shows_above_his_head_now_and_then_not_all_the_time()
        {
            // The player saw "Hungry" stuck over a settler's head all day, through his chats (0.18).
            int shown = 0;
            for (int second = 0; second < 300; second++) if (HelpWords.HungerShows(1000.0 + second, "a1")) shown++;
            Assert.Equal(300 * HelpWords.HungerFor / HelpWords.HungerEvery, shown, 0);
            Assert.Equal(HelpWords.HungerShows(1234.5, "a1"), HelpWords.HungerShows(1234.5, "a1"));
            bool differ = false;
            for (int second = 0; second < 30; second++) differ |= HelpWords.HungerShows(second, "a1") != HelpWords.HungerShows(second, "zz9");
            Assert.True(differ);
        }
    }
}
