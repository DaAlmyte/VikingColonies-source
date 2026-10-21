using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>The settings Town Hall levels read (section Town Hall, and the colony's base radius and cap).</summary>
    public struct HallSettings
    {
        public HallSettings(bool enabled, float baseRadius, float radiusPerLevel, int settlersAtLevel1, int settlersPerLevel, int maxSettlers)
        {
            Enabled = enabled;
            BaseRadius = baseRadius;
            RadiusPerLevel = Math.Max(0f, radiusPerLevel);
            SettlersAtLevel1 = Math.Max(0, settlersAtLevel1);
            SettlersPerLevel = Math.Max(0, settlersPerLevel);
            MaxSettlers = Math.Max(0, maxSettlers);
        }

        public bool Enabled { get; }
        public float BaseRadius { get; }
        public float RadiusPerLevel { get; }
        public int SettlersAtLevel1 { get; }
        public int SettlersPerLevel { get; }
        public int MaxSettlers { get; }
    }

    /// <summary>What a Town Hall's level gives the colony: its area's radius, how many settlers it grows to, the highest hut level.</summary>
    public struct HallPerks
    {
        public HallPerks(float radius, int settlers, int hutCap)
        {
            Radius = radius;
            Settlers = settlers;
            HutCap = hutCap;
        }

        public float Radius { get; }
        public int Settlers { get; }
        public int HutCap { get; }
    }

    /// <summary>Why the Town Hall can't be raised a level now (None: it can).</summary>
    public enum RaiseLock { None, Off, MaxLevel, NoTrophy, Materials, UnknownItem, Already }

    /// <summary>
    /// Town Hall levels 1–5 (1.4, the Hall of Trophies): each level above 1 is earned by showing the hall the trophies
    /// of the Forsaken met since the level before (never used up) and paying materials from its chest, and widens the
    /// colony's area, raises how many settlers it grows to and opens the next age of huts. After the research review
    /// (docs/research/town-hall-levels-in-minecolonies-and-valheim.md) the levels follow the hut ages: huts go as high
    /// as the hall, and a level's last boss is the one whose defeat opens that age.
    /// </summary>
    public static class TownHallLevels
    {
        public const int MaxLevel = 5;

        private static readonly string[] s_trophies =
        {
            "TrophyEikthyr", "TrophyTheElder", "TrophyBonemass", "TrophyDragonQueen", "TrophyGoblinKing", "TrophySeekerQueen", "TrophyFader",
        };

        // The world key the game sets when each is beaten (Character.m_defeatSetGlobalKey), in the same order.
        private static readonly string[] s_bossKeys =
        {
            "defeated_eikthyr", "defeated_gdking", "defeated_bonemass", "defeated_dragon", "defeated_goblinking", "defeated_queen", "defeated_fader",
        };

        // The bosses' own names (Character.m_name), so the player reads them in their language.
        private static readonly string[] s_bosses =
        {
            "$enemy_eikthyr", "$enemy_gdking", "$enemy_bonemass", "$enemy_dragon", "$enemy_goblinking", "$enemy_seekerqueen", "$enemy_fader",
        };

        /// <summary>
        /// Whether <paramref name="latest"/> (<see cref="LatestBeaten"/>) is news to a colony that last heard of
        /// <paramref name="heard"/>: a boss further along the way (the review of polish round 2: the boss topic never ended,
        /// "Eikthyr, fallen!" on day 300). The colony's saga tells it, and settlers talk of it for a few days.
        /// </summary>
        public static bool BossNews(string heard, string latest)
        {
            int now = Array.IndexOf(s_bosses, latest ?? "");
            return now >= 0 && now > Array.IndexOf(s_bosses, heard ?? "");
        }

        /// <summary>The name token of the boss furthest along the way that this world has beaten ("" for none) (1.4.2's small talk).</summary>
        public static string LatestBeaten(Func<string, bool> isBeaten)
        {
            for (int i = s_bossKeys.Length - 1; i >= 0; i--)
                if (isBeaten != null && isBeaten(s_bossKeys[i])) return s_bosses[i];
            return "";
        }

        private static readonly string[] s_defaultCosts =
        {
            "Wood:40, Stone:20, DeerHide:10",
            "RoundLog:40, Stone:40, Bronze:20, Iron:20",
            "FineWood:40, Silver:30, BlackMetal:20",
            "YggdrasilWood:30, BlackMarble:40, Eitr:10",
        };

        /// <summary>Every Forsaken's trophy, in the order they are met; all but Fader's raise a level (<see cref="TrophiesFor"/>).</summary>
        public static IReadOnlyList<string> Trophies => s_trophies;

        // The trophies each level 2–5 needs shown: Eikthyr; The Elder and Bonemass; Moder and Yagluth; the Queen.
        private static readonly string[][] s_levelTrophies =
        {
            new[] { "TrophyEikthyr" },
            new[] { "TrophyTheElder", "TrophyBonemass" },
            new[] { "TrophyDragonQueen", "TrophyGoblinKing" },
            new[] { "TrophySeekerQueen" },
        };

        public static int Clamp(int level) => Math.Max(1, Math.Min(MaxLevel, level));

        public static HallPerks Perks(int level, HallSettings s)
        {
            if (!s.Enabled) return new HallPerks(s.BaseRadius, s.MaxSettlers, HutLevels.MaxLevel);
            int above = Clamp(level) - 1;
            return new HallPerks(s.BaseRadius + s.RadiusPerLevel * above,
                Math.Min(s.MaxSettlers, s.SettlersAtLevel1 + s.SettlersPerLevel * above),
                Math.Min(HutLevels.MaxLevel, Clamp(level)));
        }

        /// <summary>The trophies the hall must be shown to reach <paramref name="level"/>; none for level 1 and past the top.</summary>
        public static IReadOnlyList<string> TrophiesFor(int level) =>
            level >= 2 && level <= MaxLevel ? s_levelTrophies[level - 2] : new string[0];

        /// <summary>The first of the level's trophies not shown (<paramref name="shown"/>); null when all are.</summary>
        public static string MissingTrophy(int level, Func<string, bool> shown) => TrophiesFor(level).FirstOrDefault(t => !shown(t));

        /// <summary>The level's bosses as name tokens for the Saga: "$enemy_gdking $vc_and $enemy_bonemass"; "" for none.</summary>
        public static string BossesToken(int level) => string.Join(" $vc_and ", TrophiesFor(level).Select(BossToken));

        /// <summary>
        /// The world key the game sets when the trophy's boss is beaten; "" for any other item. With
        /// Town Hall.BeatenBossesCount a beaten Forsaken counts as shown (after the review of 1.4: players who updated from
        /// 1.0 had hung their trophies on the sacrificial stones, which don't give them back).
        /// </summary>
        public static string BossKey(string trophy)
        {
            int i = Array.IndexOf(s_trophies, trophy);
            return i >= 0 ? s_bossKeys[i] : "";
        }

        /// <summary>The boss's name token for its trophy; "" for any other item.</summary>
        public static string BossToken(string trophy)
        {
            int i = Array.IndexOf(s_trophies, trophy);
            return i >= 0 ? s_bosses[i] : "";
        }

        /// <summary>The materials reaching <paramref name="level"/> costs out of the box, as setting text; "" for none.</summary>
        public static string DefaultCost(int level) => level >= 2 && level <= MaxLevel ? s_defaultCosts[level - 2] : "";

        /// <summary>
        /// "Prefab:count" items by commas, in their order; a repeated item adds up; anything else (no count, a count
        /// below 1, no name) is skipped, so a mistyped setting never breaks the window.
        /// </summary>
        public static List<KeyValuePair<string, int>> ParseCost(string text)
        {
            var cost = new List<KeyValuePair<string, int>>();
            if (string.IsNullOrEmpty(text)) return cost;
            foreach (string part in text.Split(','))
            {
                if (!ReadPart(part, out string item, out int count)) continue;
                int at = cost.FindIndex(c => c.Key == item);
                if (at >= 0) cost[at] = new KeyValuePair<string, int>(item, cost[at].Value + count);
                else cost.Add(new KeyValuePair<string, int>(item, count));
            }
            return cost;
        }

        /// <summary>How long after a raise the computers that see it celebrate it (they look every 2 s).</summary>
        public const double CelebrateSeconds = 10;

        /// <summary>
        /// A computer that saw the hall at <paramref name="seenLevel"/> (-1: not yet) and now sees <paramref name="level"/>
        /// tells its player only of a raise just made (<paramref name="raisedAt"/>, game time; 0 when never): a Town Hall
        /// loading at its level, or one seen again long after, is not news.
        /// </summary>
        public static bool JustRaised(int seenLevel, int level, double raisedAt, double now) =>
            seenLevel > 0 && level > seenLevel && raisedAt > 0 && now + ClockSlack >= raisedAt && now - raisedAt <= CelebrateSeconds;

        /// <summary>The raise time is a float in the save, and computers' clocks differ a little (review of 1.4).</summary>
        private const double ClockSlack = 2;

        /// <summary>
        /// The parts of a cost text that can't be read ("Wood=40", "Iron:0"): a raise waits while there are any (review of
        /// 1.4: a mistyped setting silently made the level cheaper). Empty parts don't count.
        /// </summary>
        public static List<string> UnreadableParts(string text)
        {
            var bad = new List<string>();
            if (string.IsNullOrEmpty(text)) return bad;
            foreach (string part in text.Split(','))
                if (part.Trim().Length > 0 && !ReadPart(part, out _, out _)) bad.Add(part.Trim());
            return bad;
        }

        private static bool ReadPart(string part, out string item, out int count)
        {
            item = "";
            count = 0;
            string[] bits = part.Split(':');
            if (bits.Length != 2) return false;
            item = bits[0].Trim();
            return item.Length > 0 && int.TryParse(bits[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count) && count >= 1;
        }

        /// <summary>
        /// Whether a hall at <paramref name="level"/> may be raised now: levels on, below the top, the next level's trophies shown,
        /// every cost item one the game knows (<paramref name="known"/>) and in the chest (<paramref name="inChest"/>).
        /// </summary>
        /// <param name="unreadable">Parts of the cost setting that couldn't be read (<see cref="UnreadableParts"/>).</param>
        /// <param name="seenLevel">The level the asking player saw (review of 1.4: a double click raised two levels); -1: now.</param>
        public static RaiseLock CanRaise(bool enabled, int level, bool trophiesShown, IReadOnlyList<KeyValuePair<string, int>> cost,
            Func<string, int> inChest, Func<string, bool> known, int unreadable = 0, int seenLevel = -1)
        {
            if (!enabled) return RaiseLock.Off;
            if (seenLevel > 0 && seenLevel != Clamp(level)) return RaiseLock.Already;
            if (Clamp(level) >= MaxLevel) return RaiseLock.MaxLevel;
            if (!trophiesShown) return RaiseLock.NoTrophy;
            if (unreadable > 0 || cost.Any(c => !known(c.Key))) return RaiseLock.UnknownItem;
            return cost.All(c => inChest(c.Key) >= c.Value) ? RaiseLock.None : RaiseLock.Materials;
        }
    }
}
