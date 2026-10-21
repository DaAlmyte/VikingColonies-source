using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>What a settler talks about (0.14); <see cref="SmallTalk.Topic"/> says the order they're tried in.</summary>
    public enum TalkTopic { Mourn, Hungry, Weather, NewHere, Newcomer, Built, Wait, Jobless, General, Evening, Cheerful, Grumpy, Trade, Repelled, Boss }

    /// <summary>What is going on around a settler that small talk can be about.</summary>
    public struct TalkContext
    {
        /// <summary>A settler of the colony who died in the last 2 days ("" for none).</summary>
        public string DeadName;
        public bool Hungry;
        /// <summary>Rain or snow.</summary>
        public bool Wet;
        /// <summary>He came to the colony today.</summary>
        public bool IsNewToday;
        /// <summary>Someone else who came today ("" for none).</summary>
        public string NewcomerName;
        /// <summary>A building finished today: its name's translation token ("" for none).</summary>
        public string BuiltToken;
        /// <summary>His status (a job's wait gives its own lines).</summary>
        public string WaitStatus;
        public bool Jobless;
        /// <summary>The evening by the fire (0.15).</summary>
        public bool Evening;
        /// <summary>His trait (0.17: the cheerful say cheerful things, the grumpy grumble).</summary>
        public Trait? Trait;
        /// <summary>How he feels (null with mood off): the cheerful are cheerful only when content or better (0.18).</summary>
        public MoodLevel? Mood;
        /// <summary>Their trade ("lumberjack", "builder"…; "" for none): they talk about it (1.4.2).</summary>
        public string Job;
        /// <summary>The colony held off a raid in the last <see cref="SmallTalk.MournDays"/> days (1.4.2).</summary>
        public bool RepelledLately;
        /// <summary>A boss whose fall the colony heard of in the last <see cref="SmallTalk.MournDays"/> days, its name's token ("" for none) (1.4.2).</summary>
        public string BossToken;
    }

    /// <summary>A line of small talk: its translation token and the name or word it is about ("" for none).</summary>
    public struct TalkLine
    {
        public TalkLine(string token, string arg)
        {
            Token = token ?? "";
            Arg = arg ?? "";
        }

        public string Token { get; }
        public string Arg { get; }
    }

    /// <summary>
    /// Small talk above settlers' heads (0.14): a topic from what is going on (the colony's news first: a death, hunger, the
    /// weather, being new, a newcomer, a new building, a raid held off, the evening; then a job's wait, having no job, the
    /// trade, the trait and a boss's fall; anything else otherwise), each that applies taken half the time (the trade a
    /// third), and a line of it picked by a seed that is the same on every computer.
    /// </summary>
    public static class SmallTalk
    {
        /// <summary>How long ago a death is still talked about (days).</summary>
        public const int MournDays = 2;

        private static readonly Dictionary<string, int> s_counts = new Dictionary<string, int>
        {
            ["mourn"] = 6, ["hungry"] = 6, ["weather"] = 6, ["newhere"] = 6, ["newcomer"] = 6, ["built"] = 6,
            ["wait_no_trees"] = 4, ["wait_crops_growing"] = 4, ["wait_nothing_to_pick"] = 4, ["wait_no_work"] = 4,
            ["jobless"] = 6, ["general"] = 16, ["evening"] = 6, ["cheerful"] = 6, ["grumpy"] = 6,
            // 1.4.2: their trade, a raid held off, the latest boss to fall.
            ["trade_lumberjack"] = 4, ["trade_builder"] = 4, ["trade_farmer"] = 4, ["trade_forager"] = 4, ["trade_guard"] = 4,
            ["repelled"] = 4, ["boss"] = 4,
        };

        /// <summary>Every line's token (a unit test checks each has words).</summary>
        public static IEnumerable<string> AllTokens()
        {
            foreach (KeyValuePair<string, int> topic in s_counts)
                for (int n = 1; n <= topic.Value; n++)
                    yield return "$vc_talk_" + topic.Key + "_" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A worker's trade is taken this often (the review of polish round 2: at one half it took half of every worker's
        /// lines from the trait and general ones).
        /// </summary>
        public const double TradeChance = 1.0 / 3.0;

        /// <summary>The topic: each that applies, in order, is taken when <paramref name="roll"/> gives under one half (the trade: <see cref="TradeChance"/>).</summary>
        public static TalkTopic Topic(TalkContext context, Func<double> roll)
        {
            // The colony's own news first, then the trade, the trait and the wider world (1.4.2).
            foreach (TalkTopic topic in new[] { TalkTopic.Mourn, TalkTopic.Hungry, TalkTopic.Weather, TalkTopic.NewHere, TalkTopic.Newcomer,
                         TalkTopic.Built, TalkTopic.Repelled, TalkTopic.Evening, TalkTopic.Wait, TalkTopic.Jobless, TalkTopic.Trade,
                         TalkTopic.Cheerful, TalkTopic.Grumpy, TalkTopic.Boss })
                if (Applies(topic, context) && roll() < (topic == TalkTopic.Trade ? TradeChance : 0.5)) return topic;
            return TalkTopic.General;
        }

        private static bool Applies(TalkTopic topic, TalkContext c)
        {
            switch (topic)
            {
                case TalkTopic.Mourn: return !string.IsNullOrEmpty(c.DeadName);
                case TalkTopic.Hungry: return c.Hungry;
                case TalkTopic.Weather: return c.Wet;
                case TalkTopic.NewHere: return c.IsNewToday;
                case TalkTopic.Newcomer: return !string.IsNullOrEmpty(c.NewcomerName);
                case TalkTopic.Built: return !string.IsNullOrEmpty(c.BuiltToken);
                case TalkTopic.Wait: return Count(TalkTopic.Wait, c.WaitStatus) > 0;
                case TalkTopic.Jobless: return c.Jobless;
                case TalkTopic.Evening: return c.Evening;
                case TalkTopic.Cheerful: return c.Trait == Trait.Cheerful && (c.Mood == null || c.Mood >= MoodLevel.Content);
                case TalkTopic.Grumpy: return c.Trait == Trait.Grumpy;
                case TalkTopic.Trade: return !string.IsNullOrEmpty(c.Job) && Count(TalkTopic.Trade, "", c.Job) > 0;
                case TalkTopic.Repelled: return c.RepelledLately;
                case TalkTopic.Boss: return !string.IsNullOrEmpty(c.BossToken);
                default: return true;
            }
        }

        /// <summary>How many lines a topic has (a wait: that wait's lines; 0 for a wait without its own).</summary>
        public static int Count(TalkTopic topic, string waitStatus, string job = "") =>
            s_counts.TryGetValue(Key(topic, waitStatus, job), out int n) ? n : 0;

        /// <summary>The line the seed picks (it wraps round), naming whoever or whatever the topic is about.</summary>
        public static TalkLine Line(TalkTopic topic, string waitStatus, int seed, TalkContext context = default(TalkContext))
        {
            string key = Key(topic, waitStatus, context.Job);
            int count = Count(topic, waitStatus, context.Job);
            if (count == 0)
            {
                key = "general";
                count = s_counts[key];
            }
            int n = ((seed % count) + count) % count + 1;
            string arg = topic == TalkTopic.Mourn ? context.DeadName
                : topic == TalkTopic.Newcomer ? context.NewcomerName
                : topic == TalkTopic.Built ? context.BuiltToken
                : topic == TalkTopic.Boss ? context.BossToken
                : "";
            return new TalkLine("$vc_talk_" + key + "_" + n.ToString(System.Globalization.CultureInfo.InvariantCulture), arg);
        }

        /// <summary>The same for every computer: from the settler's id and the hour.</summary>
        public static int Seed(string settlerId, int hour)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char ch in settlerId ?? "")
                {
                    hash ^= ch;
                    hash *= 16777619;
                }
                hash ^= (uint)hour;
                hash *= 16777619;
                hash ^= hash >> 15;
                return (int)(hash & 0x7fffffff);
            }
        }

        /// <summary>From the colony's saga: who died lately, who came today (and whether it was him), what was built today.</summary>
        public static TalkContext FromSaga(SagaLog saga, int today, string myName)
        {
            var context = new TalkContext { DeadName = "", NewcomerName = "", BuiltToken = "", WaitStatus = "", Job = "", BossToken = "" };
            if (saga == null) return context;
            foreach (SagaEntry entry in saga.Page(0, SagaLog.MaxEntries))   // newest first
            {
                if (entry.Kind == "repelled" && today - entry.Day >= 0 && today - entry.Day <= MournDays) context.RepelledLately = true;
                // A boss's fall, the newest, is news for as long as a death is (the review of polish round 2: it never ended).
                if (entry.Kind == "bossfell" && entry.Args.Count > 0 && context.BossToken.Length == 0 && today - entry.Day >= 0 && today - entry.Day <= MournDays)
                    context.BossToken = entry.Args[0] ?? "";
                string first = entry.Args.Count > 0 ? entry.Args[0] : "";
                if (first.Length == 0) continue;
                if (entry.Kind == "died" && context.DeadName.Length == 0 && today - entry.Day >= 0 && today - entry.Day <= MournDays)
                    context.DeadName = first;
                else if ((entry.Kind == "arrived" || entry.Kind == "came") && entry.Day == today)
                {
                    if (first == myName) context.IsNewToday = true;
                    else if (context.NewcomerName.Length == 0) context.NewcomerName = first;
                }
                else if (entry.Kind == "built" && entry.Day == today && context.BuiltToken.Length == 0)
                    context.BuiltToken = first;
            }
            return context;
        }

        private static string Key(TalkTopic topic, string waitStatus, string job = "")
        {
            switch (topic)
            {
                case TalkTopic.Trade: return "trade_" + (job ?? "");
                case TalkTopic.Repelled: return "repelled";
                case TalkTopic.Boss: return "boss";
                case TalkTopic.Mourn: return "mourn";
                case TalkTopic.Hungry: return "hungry";
                case TalkTopic.Weather: return "weather";
                case TalkTopic.NewHere: return "newhere";
                case TalkTopic.Newcomer: return "newcomer";
                case TalkTopic.Built: return "built";
                case TalkTopic.Jobless: return "jobless";
                case TalkTopic.Evening: return "evening";
                case TalkTopic.Cheerful: return "cheerful";
                case TalkTopic.Grumpy: return "grumpy";
                case TalkTopic.Wait:
                    const string prefix = "$vc_status_";
                    string s = waitStatus ?? "";
                    return s.StartsWith(prefix, StringComparison.Ordinal) ? "wait_" + s.Substring(prefix.Length) : "wait_";
                default: return "general";
            }
        }
    }
}
