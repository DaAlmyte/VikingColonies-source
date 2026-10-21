using System;
using System.Collections.Generic;
using System.Globalization;

namespace VikingColonies.Core
{
    /// <summary>
    /// When the game asks a player what they think (1.4.2, the feedback design of 2026-10-20): once per computer, at the
    /// first time a hut near the player finishes its upgrade to level 2, or after 2 hours of play with a colony,
    /// whichever comes first; never with an enemy near, over a window or menu, or in a raid.
    /// </summary>
    public sealed class FeedbackAsk
    {
        public const double PlaySeconds = 2 * 60 * 60;
        private const int MomentLevel = 2;
        private readonly Dictionary<string, int> _levels = new Dictionary<string, int>();

        /// <summary>A hut near the player was seen below level 2, and then at level 2 or more.</summary>
        public bool HutReachedLevel2 { get; private set; }

        /// <summary>A hut near the player, at the level it has now. A hut first seen at level 2 or more isn't the moment.</summary>
        public void SawHut(string id, int level)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (_levels.TryGetValue(id, out int was) && was < MomentLevel && level >= MomentLevel) HutReachedLevel2 = true;
            _levels[id] = level;
        }

        public static bool Due(bool hutReachedLevel2, double playedSeconds) => hutReachedLevel2 || playedSeconds >= PlaySeconds;

        public static bool ShowNow(bool due, bool enabled, bool asked, bool enemyNear, bool inWindow, bool raid) =>
            due && enabled && !asked && !enemyNear && !inWindow && !raid;

        /// <summary>A monster this near holds the ask back; anything after the player, from <see cref="TargetReach"/>.</summary>
        public const float EnemyReach = 30f, TargetReach = 60f;

        /// <summary>
        /// Whether a creature holds the ask back: a monster that would fight the player within 30 m (not a deer, a boar or
        /// a settler going about their business), or anything after the player within 60 m (an archer, a charging boar).
        /// </summary>
        public static bool Threat(float distance, bool hostile, bool monster, bool animal, bool targetsPlayer) =>
            targetsPlayer ? distance <= TargetReach : hostile && monster && !animal && distance <= EnemyReach;

        /// <summary>
        /// Our own test worlds (VCTest, VCTestFlat, VCTestRun-…), where the ask waits to be armed by a test command. Only
        /// that prefix: a player's world called "Greatest" or "Contest" is asked as any other (the review of 1.4.2).
        /// </summary>
        public static bool OurTestWorld(string world) =>
            !string.IsNullOrEmpty(world) && world.StartsWith("VCTest", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What this computer remembers about the ask, in a small file next to the mod's config: whether it was shown, and
    /// the play time with a colony so far. Anything it can't read counts as nothing.
    /// </summary>
    public sealed class FeedbackMemory
    {
        /// <summary>The most one step of play time adds: a longer one is a hitch or a clock jump, not play.</summary>
        public const double MaxStep = 60;

        public bool Asked;
        public double PlayedSeconds;

        public void AddPlayed(double seconds)
        {
            if (seconds > 0 && seconds <= MaxStep) PlayedSeconds += seconds;
        }

        public string Write() =>
            "# VikingColonies: whether the game has asked for feedback on this computer, and the play time with a colony so far\n" +
            $"asked={(Asked ? 1 : 0)}\nplayed={Math.Floor(PlayedSeconds).ToString("0", CultureInfo.InvariantCulture)}\n";

        public static FeedbackMemory Read(string text)
        {
            var memory = new FeedbackMemory();
            foreach (string raw in (text ?? "").Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                if (key == "asked") memory.Asked = value == "1";
                else if (key == "played" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double played)
                         && played > 0 && !double.IsInfinity(played))
                    memory.PlayedSeconds = played;
            }
            return memory;
        }
    }

    /// <summary>Where the Feedback window's buttons go. Nothing is sent from the game: they open a page in the browser.</summary>
    public static class FeedbackLinks
    {
        /// <summary>The Discord server's invite (it never expires; checked 20 Oct 2026). Players post in #feedback.</summary>
        public const string Discord = "https://discord.gg/yCzDUPQA3D";

        /// <summary>The issues page's feedback form, with only the mod's and the game's versions filled in.</summary>
        public static string GitHub(string modVersion, string gameVersion) =>
            $"https://github.com/{BugForm.IssuesPage}/issues/new?template=feedback.yml&versions=" +
            Uri.EscapeDataString($"VikingColonies {modVersion}, Valheim {gameVersion}");
    }
}
