using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VikingColonies.Core
{
    /// <summary>
    /// One settler the colony has lost (1.5.1, the memorial stone): who, their trade, the day and how. With the fallen return
    /// on when they died, also their record (until they return).
    /// </summary>
    public sealed class FallenLine
    {
        public const int NotBack = -1;

        /// <summary>The dead settler's id; "" for a line taken from an old save's Saga.</summary>
        public string Id = "";
        public string Name = "";
        /// <summary>The job they had (a job id), "" for none or unknown.</summary>
        public string Job = "";
        public int Day;
        /// <summary>The death's own words: a translation token and its argument ("killed by {0}", "Greydwarf").</summary>
        public string Cause = "", CauseArg = "";
        /// <summary>Their saved record (<see cref="CitizenRecord.Encode"/>), kept only while a return is due; "" otherwise.</summary>
        public string Record = "";
        /// <summary>The day they returned, or <see cref="NotBack"/>.</summary>
        public int BackDay = NotBack;

        public bool Returned => BackDay >= 0;

        /// <summary>What tells two lines apart: the settler's id, or the name and day of a line from an old Saga.</summary>
        public string Key => string.IsNullOrEmpty(Id) ? "saga:" + Name + "@" + Day.ToString(CultureInfo.InvariantCulture) : Id;

        internal FallenLine Copy(bool withRecord) => new FallenLine
        {
            Id = Id, Name = Name, Job = Job, Day = Day, Cause = Cause, CauseArg = CauseArg, Record = withRecord ? Record : "", BackDay = BackDay,
        };
    }

    /// <summary>
    /// The colony's fallen (1.5.1): every settler it has lost, kept on its camp with no limit (the Saga keeps 60 lines), and
    /// copied onto its memorial stone. Lines are only added or marked returned, so two copies merge without loss.
    /// </summary>
    public sealed class FallenList
    {
        private const string Header = "1";
        private readonly List<FallenLine> _lines = new List<FallenLine>();

        /// <summary>In the order they were added (oldest death first).</summary>
        public IReadOnlyList<FallenLine> Lines => _lines;

        /// <summary>
        /// Adds a death; false for one already here (the same death told twice) or a line without a name. A death an old
        /// save's Saga told (no id) is replaced by its own full line.
        /// </summary>
        public bool Add(FallenLine line)
        {
            if (line == null || string.IsNullOrEmpty(line.Name)) return false;
            FallenLine twin = _lines.FirstOrDefault(l => SameDeath(l, line));
            if (twin == null)
            {
                _lines.Add(line);
                return true;
            }
            if (!Fuller(line, twin)) return false;
            if (twin.Returned && !line.Returned) line.BackDay = twin.BackDay;
            _lines[_lines.IndexOf(twin)] = line;
            return true;
        }

        /// <summary>The same death: the same key, or one told without an id (an old save's Saga) with the same name and day.</summary>
        private static bool SameDeath(FallenLine a, FallenLine b) =>
            a.Key == b.Key || ((string.IsNullOrEmpty(a.Id) || string.IsNullOrEmpty(b.Id)) && a.Name == b.Name && a.Day == b.Day);

        /// <summary>A line with the settler's id tells more than the Saga's line for the same death.</summary>
        private static bool Fuller(FallenLine line, FallenLine than) => !string.IsNullOrEmpty(line.Id) && string.IsNullOrEmpty(than.Id);

        public string Encode()
        {
            var sb = new StringBuilder(Header);
            foreach (FallenLine l in _lines)
            {
                sb.Append('\n').Append(RecordCodec.Encode(new[]
                {
                    Field("id", l.Id), Field("name", l.Name), Field("job", l.Job), Field("day", RecordCodec.FormatInt(l.Day)),
                    Field("cause", l.Cause), Field("arg", l.CauseArg), Field("back", RecordCodec.FormatInt(l.BackDay)), Field("rec", l.Record),
                }));
            }
            return sb.ToString();
        }

        /// <summary>Whether a camp has saved its list yet (an empty list is saved too): an old save has not.</summary>
        public static bool IsSaved(string text) => text != null && (text == Header || text.StartsWith(Header + "\n", StringComparison.Ordinal));

        /// <summary>Never throws; a line without a name or a day is skipped.</summary>
        public static FallenList Decode(string text)
        {
            var list = new FallenList();
            if (!IsSaved(text)) return list;
            foreach (string part in text.Split('\n').Skip(1))
            {
                Dictionary<string, string> f = RecordCodec.Decode(part);
                string name = RecordCodec.Get(f, "name");
                if (string.IsNullOrEmpty(name) || !int.TryParse(RecordCodec.Get(f, "day"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int day)) continue;
                list.Add(new FallenLine
                {
                    Id = RecordCodec.Get(f, "id"), Name = name, Job = RecordCodec.Get(f, "job"), Day = day,
                    Cause = RecordCodec.Get(f, "cause"), CauseArg = RecordCodec.Get(f, "arg"), Record = RecordCodec.Get(f, "rec"),
                    BackDay = RecordCodec.ParseInt(RecordCodec.Get(f, "back"), FallenLine.NotBack),
                });
            }
            return list;
        }

        /// <summary>An old save's colony: the deaths its Saga still holds (oldest first), without ids, trades or records.</summary>
        public static FallenList FromSaga(SagaLog saga)
        {
            var list = new FallenList();
            if (saga == null) return list;
            foreach (SagaEntry e in saga.Page(0, SagaLog.MaxEntries).Reverse())
            {
                if (e.Kind != "died" || e.Args.Count == 0) continue;
                list.Add(new FallenLine
                {
                    Name = e.Args[0], Day = e.Day, Cause = e.Args.Count > 1 ? e.Args[1] : "", CauseArg = e.Args.Count > 2 ? e.Args[2] : "",
                });
            }
            return list;
        }

        /// <summary>
        /// Takes in the other copy's lines this one lacks (without their records: a return waiting on a lost camp is lost
        /// with it) and the returns it has seen. True when anything changed.
        /// </summary>
        public bool MergeFrom(FallenList other)
        {
            if (other == null) return false;
            bool changed = false;
            foreach (FallenLine theirs in other._lines)
            {
                FallenLine mine = _lines.FirstOrDefault(l => SameDeath(l, theirs));
                if (mine == null)
                {
                    _lines.Add(theirs.Copy(withRecord: false));
                    changed = true;
                    continue;
                }
                if (Fuller(theirs, mine))
                {
                    FallenLine full = theirs.Copy(withRecord: false);
                    if (mine.Returned && !full.Returned) full.BackDay = mine.BackDay;
                    _lines[_lines.IndexOf(mine)] = full;
                    mine = full;
                    changed = true;
                }
                if (theirs.Returned && !mine.Returned)
                {
                    mine.BackDay = theirs.BackDay;
                    mine.Record = "";
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>A copy without the records: the memorial stone's words.</summary>
        public FallenList WordsOnly()
        {
            var copy = new FallenList();
            foreach (FallenLine l in _lines) copy._lines.Add(l.Copy(withRecord: false));
            return copy;
        }

        /// <summary>
        /// The settler due back now, or null: only with the setting on, one a day (after <paramref name="lastBackDay"/>), with
        /// room under the colony's cap, and only one whose record was kept and whose days have passed; the earliest death first.
        /// </summary>
        public FallenLine NextBack(int today, int returnDays, bool settingOn, int lastBackDay, int settlers, int cap)
        {
            if (!settingOn || lastBackDay >= today || settlers >= cap) return null;
            return _lines.Where(l => !l.Returned && !string.IsNullOrEmpty(l.Record) && l.Day + Math.Max(0, returnDays) <= today)
                .OrderBy(l => l.Day).FirstOrDefault();
        }

        /// <summary>They returned on <paramref name="day"/>: marked so, and their record let go. False when no line has that key.</summary>
        public bool MarkReturned(string key, int day)
        {
            FallenLine line = _lines.FirstOrDefault(l => l.Key == key);
            if (line == null) return false;
            line.BackDay = day;
            line.Record = "";
            return true;
        }

        private static KeyValuePair<string, string> Field(string key, string value) => new KeyValuePair<string, string>(key, value ?? "");
    }

    /// <summary>The fallen return (1.5.1): who comes back.</summary>
    public static class FallenReturn
    {
        /// <summary>
        /// The settler who returns: their name, looks, trait, trade, origin and skills, with a new id (a player away at the
        /// return has the old one saved as dead) and nothing of their old place: no colony yet, home, hut, job or load.
        /// </summary>
        public static CitizenRecord Restore(CitizenRecord dead, string newId, int today)
        {
            if (dead == null) return null;
            return new CitizenRecord
            {
                Id = newId ?? "",
                Name = dead.Name, Model = dead.Model, Hair = dead.Hair, Beard = dead.Beard,
                Skin = (float[])dead.Skin?.Clone() ?? new[] { 1f, 1f, 1f },
                HairColor = (float[])dead.HairColor?.Clone() ?? new[] { 1f, 1f, 1f },
                Calling = dead.Calling, Trait = dead.Trait, Origin = dead.Origin,
                Woodcutting = dead.Woodcutting, WoodcuttingXp = dead.WoodcuttingXp, Farming = dead.Farming, FarmingXp = dead.FarmingXp,
                Crafting = dead.Crafting, CraftingXp = dead.CraftingXp, Fighting = dead.Fighting, FightingXp = dead.FightingXp,
                Hauling = dead.Hauling, HaulingXp = dead.HaulingXp, Cooking = dead.Cooking, CookingXp = dead.CookingXp, Kills = dead.Kills,
                Arrived = today, JoblessSince = today,
            };
        }
    }

    /// <summary>One page of the memorial stone's words.</summary>
    public sealed class MemorialPage
    {
        public IReadOnlyList<FallenLine> Lines = new FallenLine[0];
        public int Page;
        public int Pages = 1;
    }

    /// <summary>The memorial stone's words (1.5.1): newest death first, 20 a page.</summary>
    public static class MemorialText
    {
        public const int PerPage = 20;

        /// <summary>The page asked for, round again after the last.</summary>
        public static MemorialPage Page(FallenList list, int page)
        {
            List<FallenLine> newest = (list?.Lines ?? new FallenLine[0]).Select((l, i) => new { l, i })
                .OrderByDescending(x => x.l.Day).ThenByDescending(x => x.i).Select(x => x.l).ToList();
            int pages = Math.Max(1, (newest.Count + PerPage - 1) / PerPage);
            int p = ((page % pages) + pages) % pages;
            return new MemorialPage { Lines = newest.Skip(p * PerPage).Take(PerPage).ToList(), Page = p, Pages = pages };
        }
    }

    /// <summary>
    /// What the camp's owner takes of a fallen line sent from another computer (1.5.1; its review: a modified game could
    /// send huge words, or a living settler's record to make a second copy walk in).
    /// </summary>
    public static class FallenChecks
    {
        public const int MaxWord = 64, MaxRecord = 4096;

        /// <summary>
        /// The line as kept, or null: only a death this computer knows of (the death message reaches every computer
        /// first), with an id, a day and no word over <see cref="MaxWord"/>; its record only when it is that settler's,
        /// of this colony, and not too long (else the words are kept without it); never marked returned.
        /// </summary>
        public static FallenLine Checked(FallenLine line, string colonyId, bool knownDead)
        {
            if (line == null || !knownDead || string.IsNullOrEmpty(line.Id) || string.IsNullOrEmpty(line.Name) || line.Day < 0) return null;
            if (new[] { line.Id, line.Name, line.Job, line.Cause, line.CauseArg }.Any(w => (w ?? "").Length > MaxWord)) return null;
            FallenLine kept = line.Copy(withRecord: RecordFits(line, colonyId));
            kept.BackDay = FallenLine.NotBack;
            return kept;
        }

        private static bool RecordFits(FallenLine line, string colonyId)
        {
            if (string.IsNullOrEmpty(line.Record) || line.Record.Length > MaxRecord) return false;
            CitizenRecord r = CitizenRecord.Decode(line.Record);
            return r.Id == line.Id && r.Name == line.Name && !string.IsNullOrEmpty(colonyId) && r.ColonyId == colonyId;
        }
    }

    /// <summary>
    /// Deaths waiting for their camp (1.5.1; its review): a settler who dies where the camp isn't loaded on the computer
    /// running them (a guard far out on a chase) is told to the camp once it is near. Kept while the game runs.
    /// </summary>
    public sealed class FallenWaiting
    {
        public const int Max = 50;
        private readonly List<KeyValuePair<string, FallenLine>> _lines = new List<KeyValuePair<string, FallenLine>>();

        public int Count => _lines.Count;

        /// <summary>Waits for the colony's camp; beyond <see cref="Max"/> the oldest is let go.</summary>
        public void Add(string colonyId, FallenLine line)
        {
            if (string.IsNullOrEmpty(colonyId) || line == null) return;
            _lines.Add(new KeyValuePair<string, FallenLine>(colonyId, line));
            if (_lines.Count > Max) _lines.RemoveAt(0);
        }

        /// <summary>The colony's waiting deaths, taken off the list.</summary>
        public IReadOnlyList<FallenLine> Take(string colonyId)
        {
            List<FallenLine> taken = _lines.Where(p => p.Key == colonyId).Select(p => p.Value).ToList();
            if (taken.Count > 0) _lines.RemoveAll(p => p.Key == colonyId);
            return taken;
        }
    }

    /// <summary>Where a memorial stone may go (1.5.1).</summary>
    public static class MemorialRules
    {
        /// <summary>Only inside a colony, and one per colony.</summary>
        public static bool MayRaise(string colonyId, IEnumerable<string> stoneColonies) =>
            !string.IsNullOrEmpty(colonyId) && !(stoneColonies ?? Enumerable.Empty<string>()).Contains(colonyId);
    }
}
