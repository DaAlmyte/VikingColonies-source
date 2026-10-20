using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VikingColonies.Core
{
    public sealed class RosterEntry
    {
        public string CitizenId = "";
        public string Name = "";
        public string Job = "";
        public string Workplace = "";
        public string Status = "";
        public int LastSeenDay;
        public float[] LastPos;
        public int MissingSinceDay = -1;
        /// <summary>His bed ("longhouse id#index"), "" for none: kept for him while he's out of sight.</summary>
        public string Home = "";
        /// <summary>His mood as last worked out (0.19), so the colony's mood is the same on every computer; unknown when never.</summary>
        public int Mood = global::VikingColonies.Core.Mood.Unknown;

        public bool IsMissing => MissingSinceDay >= 0;
    }

    /// <summary>The colony's member list, saved on the Town Hall so it survives members being unloaded.</summary>
    public sealed class ColonyRoster
    {
        public const int CurrentVersion = 1;

        private readonly List<RosterEntry> _entries = new List<RosterEntry>();

        public IReadOnlyList<RosterEntry> Entries => _entries;

        public RosterEntry Get(string citizenId) => _entries.FirstOrDefault(e => e.CitizenId == citizenId);

        public RosterEntry WorkerOf(string workplaceId) =>
            string.IsNullOrEmpty(workplaceId) ? null : _entries.FirstOrDefault(e => e.Workplace == workplaceId);

        /// <summary>Everyone working there (a Watchtower holds several guards, 1.2).</summary>
        public List<RosterEntry> WorkersOf(string workplaceId) =>
            string.IsNullOrEmpty(workplaceId) ? new List<RosterEntry>() : _entries.Where(e => e.Workplace == workplaceId).ToList();

        /// <summary>Records a sighting: adds or updates the member and clears Missing.</summary>
        public void Seen(string citizenId, string name, string job, string workplace, string status, float[] pos, int day, string home = "",
            int mood = global::VikingColonies.Core.Mood.Unknown)
        {
            if (string.IsNullOrEmpty(citizenId)) return;
            RosterEntry entry = Get(citizenId);
            if (entry == null)
            {
                entry = new RosterEntry { CitizenId = citizenId };
                _entries.Add(entry);
            }
            entry.Name = name ?? "";
            entry.Job = job ?? "";
            entry.Workplace = workplace ?? "";
            entry.Status = status ?? "";
            entry.LastPos = pos;
            entry.LastSeenDay = day;
            entry.MissingSinceDay = -1;
            entry.Home = home ?? "";
            if (mood != global::VikingColonies.Core.Mood.Unknown) entry.Mood = mood;   // not worked out here yet: the last known stays
        }

        public bool Remove(string citizenId) => _entries.RemoveAll(e => e.CitizenId == citizenId) > 0;

        public void MarkMissing(string citizenId, int day)
        {
            RosterEntry entry = Get(citizenId);
            if (entry != null && entry.MissingSinceDay < 0) entry.MissingSinceDay = day;
        }

        /// <summary>
        /// Bring back is offered for a member missing a whole day: one who just stepped past the loaded area shows as
        /// missing at once (review of 0.10).
        /// </summary>
        public static bool MayBringBack(RosterEntry entry, int today) => entry != null && entry.IsMissing && today - entry.MissingSinceDay >= 1;

        public int PruneMissing(int today, int maxDaysMissing) =>
            _entries.RemoveAll(e => e.MissingSinceDay >= 0 && today - e.MissingSinceDay >= maxDaysMissing);

        /// <summary>Changes only when something worth saving changed (not positions or last-seen days).</summary>
        public string Signature()
        {
            var sb = new StringBuilder();
            foreach (RosterEntry e in _entries)
                sb.Append(e.CitizenId).Append('|').Append(e.Name).Append('|').Append(e.Job).Append('|')
                  .Append(e.Workplace).Append('|').Append(e.Status).Append('|').Append(e.MissingSinceDay).Append('|').Append(e.Home)
                  .Append('|').Append(e.Mood).Append('\n');
            return sb.ToString();
        }

        public string Encode()
        {
            var lines = new List<string>
            {
                RecordCodec.Encode(new[] { new KeyValuePair<string, string>("v", RecordCodec.FormatInt(CurrentVersion)) }),
            };
            foreach (RosterEntry e in _entries)
            {
                var fields = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("id", e.CitizenId),
                    new KeyValuePair<string, string>("name", e.Name),
                    new KeyValuePair<string, string>("job", e.Job),
                    new KeyValuePair<string, string>("work", e.Workplace),
                    new KeyValuePair<string, string>("status", e.Status),
                    new KeyValuePair<string, string>("seen", RecordCodec.FormatInt(e.LastSeenDay)),
                    new KeyValuePair<string, string>("pos", RecordCodec.FormatFloats(e.LastPos)),
                    new KeyValuePair<string, string>("missing", RecordCodec.FormatInt(e.MissingSinceDay)),
                    new KeyValuePair<string, string>("home", e.Home),
                };
                // Written only when known (0.19): a roster from before reads and writes back the same.
                if (e.Mood != global::VikingColonies.Core.Mood.Unknown) fields.Add(new KeyValuePair<string, string>("mood", RecordCodec.FormatInt(e.Mood)));
                lines.Add(RecordCodec.Encode(fields));
            }
            return string.Join("\n", lines);
        }

        /// <summary>Never throws; lines without a citizen id are skipped.</summary>
        public static ColonyRoster Decode(string text)
        {
            var roster = new ColonyRoster();
            if (string.IsNullOrEmpty(text)) return roster;
            foreach (string line in text.Split('\n'))
            {
                Dictionary<string, string> f = RecordCodec.Decode(line);
                string id = RecordCodec.Get(f, "id");
                if (string.IsNullOrEmpty(id) || roster.Get(id) != null) continue;
                roster._entries.Add(new RosterEntry
                {
                    CitizenId = id,
                    Name = RecordCodec.Get(f, "name"),
                    Job = RecordCodec.Get(f, "job"),
                    Workplace = RecordCodec.Get(f, "work"),
                    Status = RecordCodec.Get(f, "status"),
                    LastSeenDay = RecordCodec.ParseInt(RecordCodec.Get(f, "seen"), 0),
                    LastPos = RecordCodec.ParseFloats(RecordCodec.Get(f, "pos"), 3),
                    MissingSinceDay = RecordCodec.ParseInt(RecordCodec.Get(f, "missing"), -1),
                    Home = RecordCodec.Get(f, "home"),
                    Mood = RecordCodec.ParseInt(RecordCodec.Get(f, "mood"), global::VikingColonies.Core.Mood.Unknown) is int m && m >= 0 && m <= 100
                        ? m : global::VikingColonies.Core.Mood.Unknown,
                });
            }
            return roster;
        }
    }
}
