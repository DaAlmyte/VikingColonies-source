using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>How a settler feels (0.17), from his mood's number.</summary>
    public enum MoodLevel { Miserable, Unhappy, Content, Joyful }

    /// <summary>What his mood is worked out from (the owner gathers it once a minute).</summary>
    public struct MoodInputs
    {
        /// <summary>"Not known" for the bed's comfort (an unloaded Longhouse, no home).</summary>
        public const int Unknown = -1;

        public bool HasHome;
        /// <summary>He has a colony and no bed in it (the two are kept apart so "neither" adds nothing).</summary>
        public bool Homeless;
        /// <summary>The game's comfort count at his bed, or <see cref="Unknown"/>.</summary>
        public int BedComfort;
        public bool AteToday;
        public bool Hungry;
        /// <summary>How many different foods in his last 5 meals.</summary>
        public int DistinctFoods;
        /// <summary>The same food 5 meals running.</summary>
        public bool SameFoodRunning;
        public bool EveningByFire;
        /// <summary>With at least two others at the fire (counts only with <see cref="EveningByFire"/>).</summary>
        public bool EveningCompany;
        public bool InCalling;
        /// <summary>Whole days without a job (0 when he has one).</summary>
        public int JoblessDays;
        /// <summary>The name of a settler of the colony who died in the last 3 days, or null/empty.</summary>
        public string Death;
        public bool HitYesterday;
        /// <summary>A raid at the colony ended with nobody dead, in the last 2 days.</summary>
        public bool RaidRepelled;
        /// <summary>The colony has a guard on duty (1.1).</summary>
        public bool Guarded;
        public Trait? Trait;
    }

    /// <summary>One reason for his mood: a translation token, how much it adds or takes away, and a name for "Ingrid died".</summary>
    public struct MoodReason
    {
        public MoodReason(string token, int change, string name = "")
        {
            Token = token ?? "";
            Change = change;
            Name = name ?? "";
        }

        public string Token { get; }
        public int Change { get; }
        public string Name { get; }
    }

    public sealed class MoodResult
    {
        public MoodResult(int value, IReadOnlyList<MoodReason> reasons)
        {
            Value = value;
            Reasons = reasons;
        }

        public int Value { get; }
        public MoodLevel Level => MoodLevels.Of(Value);
        public IReadOnlyList<MoodReason> Reasons { get; }
    }

    /// <summary>
    /// A settler's mood (0.17): 50 to start, and each reason adds or takes away (the spec's table, in its order), kept
    /// between 0 and 100. Nobody leaves over it; it speeds or slows his work and decides whether newcomers come.
    /// </summary>
    public static class Mood
    {
        public const int Start = 50;
        /// <summary>Not worked out yet (a settler from before 0.17): he counts as content.</summary>
        public const int Unknown = -1;

        /// <summary>Every reason's token, in the table's order.</summary>
        public static readonly IReadOnlyList<string> ReasonTokens = new[]
        {
            "$vc_mood_home", "$vc_mood_nohome", "$vc_mood_cosy", "$vc_mood_ate", "$vc_mood_hungry", "$vc_mood_varied", "$vc_mood_samefood",
            "$vc_mood_evening", "$vc_mood_company", "$vc_mood_calling", "$vc_mood_jobless", "$vc_mood_death", "$vc_mood_hit",
            "$vc_mood_repelled", "$vc_mood_guarded", "$vc_mood_cheerful", "$vc_mood_grumpy",
        };

        private static readonly HashSet<string> s_takesAway = new HashSet<string>
            { "$vc_mood_nohome", "$vc_mood_hungry", "$vc_mood_samefood", "$vc_mood_jobless", "$vc_mood_death", "$vc_mood_hit", "$vc_mood_grumpy" };

        /// <summary>The reason takes away (it has words for above his head).</summary>
        public static bool TakesAway(string token) => token != null && s_takesAway.Contains(token);

        public static MoodResult Compute(MoodInputs i)
        {
            var reasons = new List<MoodReason>();
            void Add(bool when, string token, int change, string name = "")
            {
                if (when) reasons.Add(new MoodReason(token, change, name));
            }

            Add(i.HasHome, "$vc_mood_home", 5);
            Add(i.Homeless && !i.HasHome, "$vc_mood_nohome", -15);
            if (i.HasHome && i.BedComfort >= 7) Add(true, "$vc_mood_cosy", 10);
            else Add(i.HasHome && i.BedComfort >= 4, "$vc_mood_cosy", 5);
            Add(i.AteToday && !i.Hungry, "$vc_mood_ate", 5);
            Add(i.Hungry, "$vc_mood_hungry", -20);
            if (i.DistinctFoods >= 4) Add(true, "$vc_mood_varied", 10);
            else Add(i.DistinctFoods >= 3, "$vc_mood_varied", 5);
            Add(i.SameFoodRunning, "$vc_mood_samefood", -5);
            Add(i.EveningByFire, "$vc_mood_evening", 5);
            Add(i.EveningByFire && i.EveningCompany, "$vc_mood_company", 5);
            Add(i.InCalling, "$vc_mood_calling", 5);
            Add(i.JoblessDays >= 2, "$vc_mood_jobless", -5);
            Add(!string.IsNullOrEmpty(i.Death), "$vc_mood_death", -10, i.Death);
            Add(i.HitYesterday, "$vc_mood_hit", -5);
            Add(i.RaidRepelled, "$vc_mood_repelled", 5);
            Add(i.Guarded, "$vc_mood_guarded", 3);
            Add(i.Trait == Trait.Cheerful, "$vc_mood_cheerful", 10);
            Add(i.Trait == Trait.Grumpy, "$vc_mood_grumpy", -10);

            int value = Math.Max(0, Math.Min(100, Start + reasons.Sum(r => r.Change)));
            return new MoodResult(value, reasons);
        }
    }

    public static class MoodLevels
    {
        public static MoodLevel Of(int value) =>
            value == Mood.Unknown ? MoodLevel.Content
            : value >= 75 ? MoodLevel.Joyful : value >= 40 ? MoodLevel.Content : value >= 20 ? MoodLevel.Unhappy : MoodLevel.Miserable;

        public static float WorkFactor(MoodLevel level)
        {
            switch (level)
            {
                case MoodLevel.Joyful: return 1.15f;
                case MoodLevel.Unhappy: return 0.85f;
                case MoodLevel.Miserable: return 0.7f;
                default: return 1f;
            }
        }

        /// <summary>
        /// The bed's comfort his last reasons imply (7 for "+10 cosy bed", 4 for "+5"), for a working-out with the bed's area
        /// not loaded (0.19: it dropped the cosy reason for a minute); unknown when they had none.
        /// </summary>
        public static int LastComfort(string whyMood)
        {
            foreach (MoodReason r in Decode(whyMood))
                if (r.Token == "$vc_mood_cosy") return r.Change >= 10 ? 7 : 4;
            return MoodInputs.Unknown;
        }

        /// <summary>An unhappy or miserable settler says his worst reason above his head.</summary>
        public static bool ShowsWords(MoodLevel level) => level <= MoodLevel.Unhappy;

        /// <summary>What he says about a reason: "$vc_mood_words_hungry" ("My belly's empty.").</summary>
        public static string WordsToken(MoodReason reason)
        {
            const string prefix = "$vc_mood_";
            return "$vc_mood_words_" + (reason.Token.StartsWith(prefix, StringComparison.Ordinal) ? reason.Token.Substring(prefix.Length) : reason.Token);
        }

        /// <summary>"+5 home, -10 Ingrid died": each reason's change and its words (<paramref name="words"/> gives a token's, with {0} for a name).</summary>
        public static string ReasonsLine(IEnumerable<MoodReason> reasons, Func<string, string> words) =>
            string.Join(", ", (reasons ?? Enumerable.Empty<MoodReason>()).Select(r =>
                (r.Change >= 0 ? "+" : "-") + Math.Abs(r.Change).ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + Named(words(r.Token) ?? "", r.Name)));

        private static string Named(string text, string name)
        {
            if (text.IndexOf("{0}", StringComparison.Ordinal) < 0) return text;
            try { return string.Format(text, name); }
            catch (FormatException) { return text; }
        }

        /// <summary>The translation token for the level: "$vc_mood_level_content".</summary>
        public static string Token(MoodLevel level) => "$vc_mood_level_" + level.ToString().ToLowerInvariant();

        /// <summary>His worst reason (the words above his head when unhappy), or null when none takes anything away.</summary>
        public static MoodReason? Worst(IEnumerable<MoodReason> reasons)
        {
            MoodReason? worst = null;
            foreach (MoodReason r in reasons ?? Enumerable.Empty<MoodReason>())
                if (r.Change < 0 && (worst == null || r.Change < worst.Value.Change)) worst = r;
            return worst;
        }

        /// <summary>"token:change[:name],…" (the saved "whymood").</summary>
        public static string Encode(IEnumerable<MoodReason> reasons) =>
            string.Join(",", (reasons ?? Enumerable.Empty<MoodReason>()).Select(r =>
                r.Token + ":" + RecordCodec.FormatInt(r.Change) + (r.Name.Length > 0 ? ":" + Clean(r.Name) : "")));

        /// <summary>Never throws; malformed parts are skipped.</summary>
        public static IReadOnlyList<MoodReason> Decode(string text)
        {
            var reasons = new List<MoodReason>();
            if (string.IsNullOrEmpty(text)) return reasons;
            foreach (string part in text.Split(','))
            {
                string[] bits = part.Split(':');
                if (bits.Length < 2 || bits[0].Length == 0 || !int.TryParse(bits[1], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int change)) continue;
                reasons.Add(new MoodReason(bits[0], change, bits.Length > 2 ? bits[2] : ""));
            }
            return reasons;
        }

        private static string Clean(string name) => name.Replace(':', ' ').Replace(',', ' ');
    }

    /// <summary>The colony's mood: its settlers' average, those not worked out yet left out; content with none.</summary>
    public static class ColonyMood
    {
        public static int Of(IEnumerable<int> moods)
        {
            List<int> known = (moods ?? Enumerable.Empty<int>()).Where(m => m >= 0).ToList();
            return known.Count == 0 ? Mood.Start : (int)Math.Round(known.Average(), MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>Days for mood: "within the last N days" counts today and the N - 1 before it; a later day (the clock put back) never.</summary>
    public static class MoodDays
    {
        public static bool Within(int day, int today, int days) => day >= 0 && day <= today && today - day < days;

        /// <summary>Whole days since <paramref name="day"/>; 0 when it isn't set.</summary>
        public static int Since(int day, int today) => day >= 0 && day <= today ? today - day : 0;
    }

    /// <summary>
    /// When a settler works out his mood (0.17): a few seconds after he loads (his bed and his "came fed" are in place by
    /// then), at a moment of his own within the minute, so a colony's settlers don't all do it on the same frame.
    /// </summary>
    public static class MoodClock
    {
        public const float Settle = 6f;

        public static float FirstDelay(string id)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in id ?? "") hash = (hash ^ c) * 16777619;
                return Settle + hash % 600 / 10f;
            }
        }
    }

    /// <summary>
    /// A raid at the colony, watched on the Town Hall's owner (0.17): when one it saw begin ends with no settler of the
    /// colony dead since, it was held off. One already on when the watch began (a load, a new owner) counts for nothing:
    /// its earlier deaths can't be told apart (review I3).
    /// </summary>
    public sealed class RaidWatch
    {
        private bool _seen, _on, _late;
        private int _startDay, _deathsAtStart;

        /// <summary>Watching begins again (the Town Hall loaded, or this computer took it over).</summary>
        public void Restart() => _seen = _on = _late = false;

        /// <summary>True once, when a raid it saw begin has just ended with nobody lost.</summary>
        public bool Update(bool raidNow, int today, SagaLog saga)
        {
            bool first = !_seen;
            _seen = true;
            if (raidNow && !_on)
            {
                _on = true;
                _late = first;
                _startDay = today;
                _deathsAtStart = MoodSaga.DeathsSince(saga, today);
                return false;
            }
            if (raidNow || !_on) return false;
            _on = false;
            return !_late && MoodSaga.DeathsSince(saga, _startDay) == _deathsAtStart;
        }
    }

    /// <summary>What the colony's Saga says that touches everyone's mood.</summary>
    public static class MoodSaga
    {
        public const int DeathDays = 3;
        public const int RepelledDays = 2;

        /// <summary>The name of the latest settler who died in the last <see cref="DeathDays"/> days, or null.</summary>
        public static string RecentDeath(SagaLog saga, int today)
        {
            if (saga == null) return null;
            foreach (SagaEntry e in saga.Page(0, SagaLog.MaxEntries))
                if (e.Kind == "died" && e.Args.Count > 0 && e.Args[0].Length > 0 && MoodDays.Within(e.Day, today, DeathDays)) return e.Args[0];
            return null;
        }

        /// <summary>How many settlers the Saga says died on <paramref name="day"/> or after (a raid's deaths: its start and end compared).</summary>
        public static int DeathsSince(SagaLog saga, int day)
        {
            if (saga == null) return 0;
            int deaths = 0;
            foreach (SagaEntry e in saga.Page(0, SagaLog.MaxEntries))
                if (e.Kind == "died" && e.Day >= day) deaths++;
            return deaths;
        }

        /// <summary>A raid at the colony ended with nobody dead in the last <see cref="RepelledDays"/> days.</summary>
        public static bool Repelled(SagaLog saga, int today)
        {
            if (saga == null) return false;
            foreach (SagaEntry e in saga.Page(0, SagaLog.MaxEntries))
                if (e.Kind == "repelled" && MoodDays.Within(e.Day, today, RepelledDays)) return true;
            return false;
        }
    }

    /// <summary>His last meals (at most <see cref="Remembered"/>, newest last), for varied food.</summary>
    public static class MealMemory
    {
        public const int Remembered = 5;

        public static void Add(List<string> meals, string item)
        {
            if (meals == null || string.IsNullOrEmpty(item)) return;
            meals.Add(item);
            while (meals.Count > Remembered) meals.RemoveAt(0);
        }

        public static string Last(IReadOnlyList<string> meals) => meals != null && meals.Count > 0 ? meals[meals.Count - 1] : null;

        public static int Distinct(IEnumerable<string> meals) => meals == null ? 0 : meals.Distinct(StringComparer.Ordinal).Count();

        /// <summary>The same food <see cref="Remembered"/> meals running.</summary>
        public static bool SameRunning(IReadOnlyList<string> meals) => meals != null && meals.Count >= Remembered && Distinct(meals) == 1;
    }
}
