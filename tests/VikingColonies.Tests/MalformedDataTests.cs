using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The security sweep of 1.4.1: whatever a save or another player's computer holds, reading it never throws and never
    /// grows without end. Every decoder of saved or shared data gets broken input: empty, null, separators only, cut short,
    /// huge, numbers that aren't, odd characters.
    /// </summary>
    public class MalformedDataTests
    {
        public static IEnumerable<object?[]> Garbage()
        {
            var rng = new Random(1419);
            var random = new StringBuilder();
            for (int i = 0; i < 4000; i++) random.Append((char)rng.Next(1, 0x2FFF));
            string?[] all =
            {
                null, "", " ", "|", "||||", ";", ";;;", ",", ",,,", ":", "=", "==", "\\", "\\\\\\", "\n\n", "\0", "\u0000|\u0000",
                "1", "-1", "NaN", "Infinity", "1e400", "99999999999999999999", "0x10", "abc", "a|b|c|d|e|f|g|h",
                "1|2|3", "x=1;y=", "=;=;=", "{}", "[]", "\"", "'", "%s%n%x{0}{1}", "\ud800", "€ŝ漢字",
                new string('|', 50000), new string('a', 200000), string.Join(";", Enumerable.Repeat("k=v", 20000)),
                random.ToString(),
            };
            return all.Select(s => new object?[] { s });
        }

        private static void NoThrow(Action read, string what, string input)
        {
            Exception thrown = Record.Exception(read);
            Assert.True(thrown == null, $"{what} threw {thrown?.GetType().Name} on '{(input ?? "null").Substring(0, Math.Min(40, (input ?? "null").Length))}': {thrown?.Message}");
        }

        [Theory]
        [MemberData(nameof(Garbage))]
        public void Records_read_from_saves_never_throw(string text)
        {
            NoThrow(() => BuildOrderRecord.Decode(text), "BuildOrderRecord", text);
            NoThrow(() => CitizenRecord.Decode(text), "CitizenRecord", text);
            NoThrow(() => ColonyRoster.Decode(text), "ColonyRoster", text);
            NoThrow(() => Keepsakes.Decode(text), "Keepsakes", text);
            NoThrow(() => DeathList.Parse(text), "DeathList", text);
            NoThrow(() => MoodLevels.Decode(text), "Mood", text);
            NoThrow(() => SagaLog.Decode(text), "SagaLog", text);
            NoThrow(() => StockList.Decode(text, true), "StockList (hall)", text);
            NoThrow(() => StockList.Decode(text, false), "StockList", text);
            NoThrow(() => TickList.Parse(text), "TickList", text);
            NoThrow(() => ActivityWords.Decode(text, out _, out _, out _), "FreeTime", text);
            NoThrow(() => TownHallLevels.ParseCost(text), "ParseCost", text);
            NoThrow(() => RecordCodec.Decode(text), "RecordCodec", text);
            NoThrow(() => RecordCodec.ParseFloats(text, 3), "ParseFloats", text);
            NoThrow(() => RecordCodec.ParseInt(text, 0), "ParseInt", text);
        }

        [Theory]
        [MemberData(nameof(Garbage))]
        public void A_plan_or_a_design_never_throws(string text)
        {
            NoThrow(() => PlanCodec.Decode(text), "PlanCodec", text);
            NoThrow(() => BlueprintFile.Parse((text ?? "").Split('\n')), "BlueprintFile", text);
        }

        // The security sweep: a building plan is saved gzip-compressed on its hut. A few kilobytes that unpack to gigabytes
        // (a "zip bomb" a modified game could put on a hut) would crash every computer that loads the hut, for good.
        [Fact]
        public void A_plan_that_unpacks_to_too_much_is_refused_not_read()
        {
            byte[] bomb;
            using (var raw = new System.IO.MemoryStream())
            {
                using (var gz = new System.IO.Compression.GZipStream(raw, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
                {
                    var zeros = new byte[1 << 20];
                    for (int i = 0; i < 64; i++) gz.Write(zeros, 0, zeros.Length);   // 64 MB of nothing
                }
                bomb = raw.ToArray();
            }
            Assert.True(bomb.Length < 200000);   // it travels small
            Assert.False(PlanBlob.TryUnpack(bomb, out string text, out string problem));
            Assert.Equal("", text);
            Assert.Contains("too big", problem);
        }

        [Fact]
        public void The_biggest_real_plan_still_unpacks()
        {
            string plan = string.Join("\n", Enumerable.Range(0, 2000).Select(i => $"wood_wall_roof_45|{i}.25|3.5|{i}.75|0|0.7071|0|0.7071|0"));
            Assert.True(PlanBlob.TryUnpack(PlanBlob.Pack(plan), out string again, out _));
            Assert.Equal(plan, again);
        }

        [Fact]
        public void Lists_from_saves_stay_bounded()
        {
            string many = string.Join(",", Enumerable.Range(0, 100000).Select(i => "id" + i));
            Assert.True(DeathList.Parse(many).Count <= DeathList.MaxRemembered);
            string sagaLines = string.Join("\n", Enumerable.Range(0, 5000).Select(i => $"{i}|came|Name{i}"));
            Assert.True(SagaLog.Decode(sagaLines).Count <= SagaLog.MaxEntries);
        }
    }
}
