using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>
    /// What the colony keeps of a settler so one whose body was lost can come back as himself: who he is, his looks, his
    /// colony, job, home and skill levels. Not what he was doing (status, load, practice towards the next level, hunger),
    /// nor how he feels or his day (mood and its reasons, meals, the evening, a hit, arriving: 1.4.2, the whole-mod review
    /// of 1.4.1), so it changes only when who he is changes. From the 2026-10 review: the citizen's data lived only on the body.
    /// </summary>
    public static class Keepsake
    {
        public static string Of(CitizenRecord record)
        {
            CitizenRecord kept = CitizenRecord.Decode(record.Encode());
            if (kept == null) return "";
            kept.Status = "";
            kept.Carry = "";
            kept.WoodcuttingXp = 0f;
            kept.FarmingXp = 0f;
            kept.CraftingXp = 0f;
            kept.FightingXp = 0f;
            kept.Hungry = false;
            kept.LastMealDay = Meals.NeverAte;
            kept.HallLostAt = ColonyGrace.NotLost;
            kept.Mood = global::VikingColonies.Core.Mood.Unknown;
            kept.WhyMood = "";
            kept.LastMeals = new List<string>();
            kept.EveningDay = CitizenRecord.NoDay;
            kept.EveningCompany = false;
            kept.HitDay = CitizenRecord.NoDay;
            kept.Arriving = false;
            kept.JoblessSince = CitizenRecord.NoDay;
            return kept.Encode();
        }

        /// <summary>
        /// A body whose life number is below its keepsake's is an old copy (it was brought back while this body lay in a
        /// part of the world nobody had loaded): it goes, so there are never two of him.
        /// </summary>
        public static bool Outdated(int bodyLife, int keptLife) => keptLife > bodyLife;
    }

    /// <summary>The settlers known to have died, newest first, saved as one short text (with each player's character).</summary>
    public static class DeathList
    {
        public const int MaxRemembered = 200;

        public static List<string> Parse(string saved) =>
            (saved ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().Take(MaxRemembered).ToList();

        public static string Add(string saved, string citizenId)
        {
            List<string> ids = Parse(saved);
            if (string.IsNullOrEmpty(citizenId) || ids.Contains(citizenId)) return string.Join(",", ids);   // heard twice: one death
            ids.Insert(0, citizenId);
            return string.Join(",", ids.Take(MaxRemembered));
        }
    }

    /// <summary>The Town Hall's keepsakes: one per member, by citizen id, saved as one text.</summary>
    public sealed class Keepsakes
    {
        private readonly Dictionary<string, string> _byId = new Dictionary<string, string>();

        public int Count => _byId.Count;

        public string Get(string citizenId) => citizenId != null && _byId.TryGetValue(citizenId, out string kept) ? kept : null;

        /// <summary>Keeps <paramref name="keepsake"/> for the citizen; true when it is new or changed.</summary>
        public bool Set(string citizenId, string keepsake)
        {
            if (string.IsNullOrEmpty(citizenId) || string.IsNullOrEmpty(keepsake)) return false;
            if (_byId.TryGetValue(citizenId, out string old) && old == keepsake) return false;
            _byId[citizenId] = keepsake;
            return true;
        }

        /// <summary>Forgets everyone not in <paramref name="members"/> (left the colony, or dropped off the roster); true when any went.</summary>
        public bool Retain(ICollection<string> members)
        {
            List<string> gone = _byId.Keys.Where(id => !members.Contains(id)).ToList();
            foreach (string id in gone) _byId.Remove(id);
            return gone.Count > 0;
        }

        public string Encode() => RecordCodec.Encode(_byId.OrderBy(k => k.Key, System.StringComparer.Ordinal));

        /// <summary>Never throws; unreadable parts are skipped.</summary>
        public static Keepsakes Decode(string text)
        {
            var keep = new Keepsakes();
            foreach (KeyValuePair<string, string> pair in RecordCodec.Decode(text))
                if (pair.Key.Length > 0 && pair.Value.Length > 0) keep._byId[pair.Key] = pair.Value;
            return keep;
        }
    }
}
