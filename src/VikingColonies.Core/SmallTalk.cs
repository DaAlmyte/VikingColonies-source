using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>What a settler talks about (0.14), in the order they're tried.</summary>
    public enum TalkTopic { Mourn, Hungry, Weather, NewHere, Newcomer, Built, Wait, Jobless, General, Evening, Cheerful, Grumpy }

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
    /// Small talk above settlers' heads (0.14): a topic from what is going on (a death first, then hunger, the weather, a
    /// newcomer, a new building, his job's wait, having no job; anything else otherwise), each that applies taken half the
    /// time, and a line of it picked by a seed that is the same on every computer.
    /// </summary>
    public static class SmallTalk
    {
        /// <summary>How long ago a death is still talked about (days).</summary>
        public const int MournDays = 2;

        private static readonly Dictionary<string, int> s_counts = new Dictionary<string, int>
        {
            ["mourn"] = 4, ["hungry"] = 4, ["weather"] = 4, ["newhere"] = 4, ["newcomer"] = 4, ["built"] = 4,
            ["wait_no_trees"] = 4, ["wait_crops_growing"] = 4, ["wait_nothing_to_pick"] = 4, ["wait_no_work"] = 4,
            ["jobless"] = 4, ["general"] = 12, ["evening"] = 4, ["cheerful"] = 4, ["grumpy"] = 4,
        };

        /// <summary>The topic: each that applies, in order, is taken when <paramref name="roll"/> gives under one half.</summary>
        public static TalkTopic Topic(TalkContext context, Func<double> roll)
        {
            foreach (TalkTopic topic in new[] { TalkTopic.Mourn, TalkTopic.Hungry, TalkTopic.Weather, TalkTopic.NewHere, TalkTopic.Newcomer,
                         TalkTopic.Built, TalkTopic.Evening, TalkTopic.Wait, TalkTopic.Jobless, TalkTopic.Cheerful, TalkTopic.Grumpy })
                if (Applies(topic, context) && roll() < 0.5) return topic;
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
                default: return true;
            }
        }

        /// <summary>How many lines a topic has (a wait: that wait's lines; 0 for a wait without its own).</summary>
        public static int Count(TalkTopic topic, string waitStatus) =>
            s_counts.TryGetValue(Key(topic, waitStatus), out int n) ? n : 0;

        /// <summary>The line the seed picks (it wraps round), naming whoever or whatever the topic is about.</summary>
        public static TalkLine Line(TalkTopic topic, string waitStatus, int seed, TalkContext context = default(TalkContext))
        {
            string key = Key(topic, waitStatus);
            int count = Count(topic, waitStatus);
            if (count == 0)
            {
                key = "general";
                count = s_counts[key];
            }
            int n = ((seed % count) + count) % count + 1;
            string arg = topic == TalkTopic.Mourn ? context.DeadName
                : topic == TalkTopic.Newcomer ? context.NewcomerName
                : topic == TalkTopic.Built ? context.BuiltToken
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
            var context = new TalkContext { DeadName = "", NewcomerName = "", BuiltToken = "", WaitStatus = "" };
            if (saga == null) return context;
            foreach (SagaEntry entry in saga.Page(0, SagaLog.MaxEntries))   // newest first
            {
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

        private static string Key(TalkTopic topic, string waitStatus)
        {
            switch (topic)
            {
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
