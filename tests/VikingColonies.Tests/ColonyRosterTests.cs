using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class ColonyRosterTests
    {
        private static readonly float[] Here = { 1f, 2f, 3f };

        [Fact]
        public void A_members_home_is_kept_on_the_roster()
        {
            var roster = new ColonyRoster();
            roster.Seen("c1", "Bjorn", "", "", "", Here, 1, home: "long1#30");
            string before = roster.Signature();
            Assert.Equal("long1#30", ColonyRoster.Decode(roster.Encode()).Get("c1").Home);
            roster.Seen("c1", "Bjorn", "", "", "", Here, 1, home: "long1#31");
            Assert.NotEqual(before, roster.Signature());   // a new home is saved at once
            Assert.Equal("", ColonyRoster.Decode("id=c2|name=Old").Get("c2").Home);   // a roster from before homes
        }

        [Fact]
        public void Seen_adds_then_updates_without_duplicates()
        {
            var roster = new ColonyRoster();
            roster.Seen("c1", "Bjorn", "", "", "$vc_status_jobless", Here, 5);
            roster.Seen("c1", "Bjorn", "lumberjack", "hut1", "$vc_status_working", Here, 6);
            Assert.Single(roster.Entries);
            RosterEntry entry = roster.Get("c1");
            Assert.Equal("lumberjack", entry.Job);
            Assert.Equal("hut1", entry.Workplace);
            Assert.Equal(6, entry.LastSeenDay);
        }

        [Fact]
        public void Seen_clears_missing()
        {
            var roster = new ColonyRoster();
            roster.Seen("c1", "Bjorn", "", "", "", Here, 1);
            roster.MarkMissing("c1", 2);
            Assert.True(roster.Get("c1").IsMissing);
            roster.Seen("c1", "Bjorn", "", "", "", Here, 3);
            Assert.False(roster.Get("c1").IsMissing);
        }

        [Fact]
        public void MarkMissing_keeps_the_first_day()
        {
            var roster = new ColonyRoster();
            roster.Seen("c1", "Bjorn", "", "", "", Here, 1);
            roster.MarkMissing("c1", 4);
            roster.MarkMissing("c1", 6);
            Assert.Equal(4, roster.Get("c1").MissingSinceDay);
        }

        [Fact]
        public void Missing_entries_are_pruned_after_the_limit()
        {
            var roster = new ColonyRoster();
            roster.Seen("c1", "Bjorn", "", "", "", Here, 1);
            roster.Seen("c2", "Sigrid", "", "", "", Here, 1);
            roster.MarkMissing("c1", 10);
            Assert.Equal(0, roster.PruneMissing(12, 3));
            Assert.Equal(1, roster.PruneMissing(13, 3));
            Assert.Null(roster.Get("c1"));
            Assert.NotNull(roster.Get("c2"));
        }

        [Fact]
        public void WorkerOf_finds_the_member_working_at_a_building()
        {
            var roster = new ColonyRoster();
            roster.Seen("c1", "Bjorn", "lumberjack", "hut1", "", Here, 1);
            roster.Seen("c2", "Sigrid", "", "", "", Here, 1);
            Assert.Equal("c1", roster.WorkerOf("hut1").CitizenId);
            Assert.Null(roster.WorkerOf("hut2"));
            Assert.Null(roster.WorkerOf(""));
        }

        [Fact]
        public void Remove_drops_a_member()
        {
            var roster = new ColonyRoster();
            roster.Seen("c1", "Bjorn", "", "", "", Here, 1);
            Assert.True(roster.Remove("c1"));
            Assert.False(roster.Remove("c1"));
            Assert.Empty(roster.Entries);
        }

        [Fact]
        public void Round_trip_keeps_every_member()
        {
            var roster = new ColonyRoster();
            roster.Seen("c1", "Bjorn", "lumberjack", "hut1", "$vc_status_working", new[] { 1.5f, 2f, -3f }, 7);
            roster.Seen("c2", "Si|grid", "", "", "", Here, 7);
            roster.MarkMissing("c2", 8);
            ColonyRoster copy = ColonyRoster.Decode(roster.Encode());
            Assert.Equal(2, copy.Entries.Count);
            Assert.Equal("hut1", copy.Get("c1").Workplace);
            Assert.Equal(new[] { 1.5f, 2f, -3f }, copy.Get("c1").LastPos);
            Assert.Equal("Si|grid", copy.Get("c2").Name);
            Assert.Equal(8, copy.Get("c2").MissingSinceDay);
        }

        // Review focus 3
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("junk\n|=\nid=|name=x\nv=zz")]
        public void Decoding_garbage_never_throws(string? text)
        {
            Assert.Empty(ColonyRoster.Decode(text).Entries);
        }

        // Review focus 5
        [Fact]
        public void Position_only_changes_keep_the_signature()
        {
            var roster = new ColonyRoster();
            roster.Seen("c1", "Bjorn", "lumberjack", "hut1", "$vc_status_working", Here, 1);
            string before = roster.Signature();
            roster.Seen("c1", "Bjorn", "lumberjack", "hut1", "$vc_status_working", new[] { 9f, 9f, 9f }, 2);
            Assert.Equal(before, roster.Signature());
            roster.Seen("c1", "Bjorn", "lumberjack", "hut1", "$vc_status_delivering", new[] { 9f, 9f, 9f }, 2);
            Assert.NotEqual(before, roster.Signature());
        }
    }
}
