using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>The numbers are saved with the hut: new states get new numbers.</summary>
    public enum BuildState { Waiting = 0, Building = 1, Done = 2, Clearing = 3, Shaping = 4 }

    /// <summary>What an order is for (saved with the hut: new kinds get new numbers).</summary>
    public enum OrderKind { Build = 0, Upgrade = 1, Repair = 2, Remove = 3 }

    /// <summary>
    /// A hut's build order, saved with the hut: its state, which pieces are up, which keep falling, and which builder
    /// has it. A builder's claim lapses a minute after he last renewed it.
    /// </summary>
    public sealed class BuildOrderRecord
    {
        public const int CurrentVersion = 1;
        public const double ClaimSeconds = 60;
        public const int MaxFailures = 3;
        /// <summary>A piece put back this often in quick succession (it stands a moment, then falls) is left out.</summary>
        public const int MaxRebuilds = 3;
        /// <summary>Repairs this far apart (seconds) don't count together: the piece was broken, not falling.</summary>
        public const double RebuildMemorySeconds = 1800;

        public BuildState State = BuildState.Waiting;
        public int Total;
        public readonly SortedSet<int> Done = new SortedSet<int>();
        public readonly SortedDictionary<int, int> Failures = new SortedDictionary<int, int>();
        public string Builder = "";
        public double ClaimTime;
        public double Created;
        public string Needs = "";
        /// <summary>What stands where a piece must go (a $piece name), so the player can remove it; "" when nothing.</summary>
        public string Blocked = "";
        /// <summary>The height the builder flattens the site to; NaN for orders from before site preparation.</summary>
        public float Ground = float.NaN;
        /// <summary>What the order is for: an upgrade also takes the old design's other pieces away.</summary>
        public OrderKind Kind = OrderKind.Build;
        /// <summary>The level the hut has once the order is finished.</summary>
        public int TargetLevel = 1;
        /// <summary>How many old pieces an upgrade takes away, and which of them are gone.</summary>
        public int TakeAwayTotal;
        public readonly SortedSet<int> TakenAway = new SortedSet<int>();

        public int TakeAwayLeft => Math.Max(0, TakeAwayTotal - TakenAway.Count);
        /// <summary>A piece was put up or an old one taken away: an upgrade can no longer be cancelled.</summary>
        public bool Changed;
        /// <summary>The network time the player last pressed Do first (Town Hall › Work orders); 0 for never.</summary>
        public double First;
        /// <summary>Set aside by the player: no builder takes it until it's resumed.</summary>
        public bool Paused;
        /// <summary>
        /// Gone pieces are put back (0.13). False for orders from before: their first check takes what is gone then as
        /// the player's choice (<see cref="TakeBaseline"/>), so pieces taken away long ago aren't rebuilt.
        /// </summary>
        public bool Watched;
        /// <summary>How often each piece was put back by recent repairs.</summary>
        public readonly SortedDictionary<int, int> Rebuilt = new SortedDictionary<int, int>();

        public int DoneCount => Done.Count;
        public int SkippedCount => Failures.Count(f => f.Value >= MaxFailures && !Done.Contains(f.Key));
        public bool IsSkipped(int index) => !Done.Contains(index) && Failures.TryGetValue(index, out int n) && n >= MaxFailures;
        public bool IsOpen(int index) => index >= 0 && index < Total && !Done.Contains(index) && !IsSkipped(index);
        /// <summary>Every piece to build is up or skipped.</summary>
        public bool BuildHandled => Enumerable.Range(0, Total).All(i => !IsOpen(i));

        /// <summary>Every piece to build is up or skipped, and every old piece to take away is gone.</summary>
        public bool AllHandled => BuildHandled && TakeAwayLeft == 0;

        public bool IsPreparing => State == BuildState.Clearing || State == BuildState.Shaping;

        /// <summary>How far along an order is: waiting, clearing the site, shaping the ground, building, finished.</summary>
        public static int Rank(BuildState state)
        {
            switch (state)
            {
                case BuildState.Clearing: return 1;
                case BuildState.Shaping: return 2;
                case BuildState.Building: return 3;
                case BuildState.Done: return 4;
                default: return 0;
            }
        }

        /// <summary>
        /// Moves the order on to <paramref name="to"/>: never back, and never to finished (that has its own rule). Steps may
        /// be skipped (a site with nothing to shape goes from clearing to building). True when it moved.
        /// </summary>
        public bool Advance(BuildState to)
        {
            if (to == BuildState.Done || Rank(to) <= Rank(State)) return false;
            State = to;
            return true;
        }

        /// <summary>Held and renewed within the last minute. A time far off in either direction counts as lapsed.</summary>
        public bool HasLiveClaim(double now) => Builder.Length > 0 && Math.Abs(now - ClaimTime) < ClaimSeconds;

        public bool IsClaimedByOther(string builder, double now) => HasLiveClaim(now) && Builder != builder;

        /// <summary>
        /// Takes or renews the order; false when another builder holds it. Taking it afresh (not renewing a live claim)
        /// clears what was reported before: the builder looks at the site again and says what it still needs.
        /// </summary>
        public bool TryClaim(string builder, double now)
        {
            if (string.IsNullOrEmpty(builder) || IsClaimedByOther(builder, now)) return false;
            if (!HasLiveClaim(now))
            {
                Needs = "";
                Blocked = "";
            }
            Builder = builder;
            ClaimTime = now;
            return true;
        }

        public void Release(string builder)
        {
            if (Builder != builder) return;
            Builder = "";
            ClaimTime = 0;
        }

        public void MarkDone(int index)
        {
            if (index < 0 || index >= Total) return;
            Done.Add(index);
            Failures.Remove(index);
            Changed = true;
        }

        /// <summary>An upgrade's piece that stays from the old design: done from the start, nothing changed.</summary>
        public void MarkKept(int index)
        {
            if (index < 0 || index >= Total) return;
            Done.Add(index);
        }

        /// <summary>The piece fell or couldn't be placed. True when it has just failed often enough to be skipped.</summary>
        public bool MarkFailed(int index)
        {
            if (index < 0 || index >= Total) return false;
            Done.Remove(index);
            Failures.TryGetValue(index, out int n);
            Failures[index] = n + 1;
            return n + 1 == MaxFailures;
        }

        /// <summary>A piece that can never be built (its saved plan line is damaged): skipped at once.</summary>
        public void MarkSkipped(int index)
        {
            if (index < 0 || index >= Total) return;
            Done.Remove(index);
            Failures[index] = MaxFailures;
        }

        /// <summary>A piece that was up disappeared (knocked down): it goes back on the list.</summary>
        public void MarkGone(int index) => Done.Remove(index);

        /// <summary>An old piece an upgrade takes away is gone.</summary>
        public void MarkTaken(int index)
        {
            if (index < 0 || index >= TakeAwayTotal) return;
            TakenAway.Add(index);
            Changed = true;
        }

        /// <summary>
        /// A finished hut's pieces are gone (0.13): its order reopens as a repair — the same plan and level, only those
        /// pieces open, the site long since cleared and shaped. False (and nothing changes) for an order not finished or
        /// with none of those pieces in it.
        /// </summary>
        public bool ReopenForRepair(IEnumerable<int> missing, double now)
        {
            List<int> pieces = (missing ?? Enumerable.Empty<int>()).Where(i => Done.Contains(i)).Distinct().ToList();
            if (State != BuildState.Done || pieces.Count == 0) return false;
            if (Math.Abs(now - Created) > RebuildMemorySeconds) Rebuilt.Clear();   // the last repair was long ago
            var open = new List<int>();
            foreach (int index in pieces)
            {
                Rebuilt.TryGetValue(index, out int times);
                if (times >= MaxRebuilds)
                {
                    MarkSkipped(index);   // it stands a moment and falls again (review of 0.13): left out
                    continue;
                }
                Rebuilt[index] = times + 1;
                open.Add(index);
            }
            if (open.Count == 0) return false;
            // Pieces that never went up (a hut marked built early, or on ground where they couldn't go) aren't part of
            // the finished building: left out, so only the broken ones open (self-test run 25 of 0.13).
            for (int i = 0; i < Total; i++)
                if (IsOpen(i)) MarkSkipped(i);
            foreach (int index in open)
            {
                Done.Remove(index);
                Failures.Remove(index);
            }
            Kind = OrderKind.Repair;
            State = BuildState.Building;
            Builder = "";
            ClaimTime = 0;
            Needs = "";
            Blocked = "";
            Created = now;
            First = 0;
            Paused = false;
            Changed = false;
            TakeAwayTotal = 0;
            TakenAway.Clear();
            return true;
        }

        /// <summary>
        /// The player leaves a repair as it is (a piece taken away on purpose): its open pieces are skipped, so the order
        /// finishes and they aren't asked for again. Returns how many; 0 for an order that isn't a running repair.
        /// </summary>
        /// <summary>
        /// The player took this piece of a finished building away with the hammer (0.13): it's left out, not put back.
        /// True when it was skipped. A piece taken away while the building is still going up goes back on the list, as before.
        /// </summary>
        public bool SkipRemoved(int index)
        {
            if (!Done.Contains(index) || (State != BuildState.Done && Kind != OrderKind.Repair)) return false;
            MarkSkipped(index);
            return true;
        }

        /// <summary>
        /// The first check of an order from before 0.13: what is gone now was taken away by the player (or long ago) and is
        /// left out; from now on gone pieces are put back. Returns how many were left out.
        /// </summary>
        public int TakeBaseline(IEnumerable<int> missing)
        {
            int left = 0;
            foreach (int index in (missing ?? Enumerable.Empty<int>()).Distinct().ToList())
            {
                if (!Done.Contains(index)) continue;
                MarkSkipped(index);
                left++;
            }
            Watched = true;
            return left;
        }

        public int LeaveAsItIs()
        {
            if (Kind != OrderKind.Repair || State == BuildState.Done) return 0;
            List<int> open = Enumerable.Range(0, Total).Where(IsOpen).ToList();
            foreach (int index in open) MarkSkipped(index);
            return open.Count;
        }

        public void MarkBuilt()
        {
            State = BuildState.Done;
            Builder = "";
            ClaimTime = 0;
            Needs = "";
            Blocked = "";
        }

        /// <summary>
        /// Take down (0.23): every piece of the hut is to be taken away (from the top, as an upgrade's old pieces), nothing
        /// built; when all are gone the hut's chest goes too.
        /// </summary>
        public static BuildOrderRecord ForRemove(int pieces, int level, double now) => new BuildOrderRecord
        {
            Kind = OrderKind.Remove,
            Total = 0,
            TakeAwayTotal = Math.Max(0, pieces),
            TargetLevel = Math.Max(1, level),
            State = BuildState.Waiting,
            Watched = false,
            Created = now,
        };

        public string Encode() => RecordCodec.Encode(new[]
        {
            Pair("v", RecordCodec.FormatInt(CurrentVersion)),
            Pair("state", RecordCodec.FormatInt((int)State)),
            Pair("total", RecordCodec.FormatInt(Total)),
            Pair("done", string.Join(",", Done.Select(RecordCodec.FormatInt))),
            Pair("fail", string.Join(",", Failures.Select(f => RecordCodec.FormatInt(f.Key) + ":" + RecordCodec.FormatInt(f.Value)))),
            Pair("builder", Builder),
            Pair("claim", ClaimTime.ToString("R", CultureInfo.InvariantCulture)),
            Pair("created", Created.ToString("R", CultureInfo.InvariantCulture)),
            Pair("needs", Needs),
            Pair("blocked", Blocked),
            Pair("ground", float.IsNaN(Ground) ? "" : Ground.ToString("R", CultureInfo.InvariantCulture)),
            Pair("kind", RecordCodec.FormatInt((int)Kind)),
            Pair("level", RecordCodec.FormatInt(TargetLevel)),
            Pair("take", RecordCodec.FormatInt(TakeAwayTotal)),
            Pair("taken", string.Join(",", TakenAway.Select(RecordCodec.FormatInt))),
            Pair("changed", Changed ? "1" : "0"),
            Pair("first", First > 0 ? First.ToString("R", CultureInfo.InvariantCulture) : ""),
            Pair("paused", Paused ? "1" : "0"),
            Pair("watched", Watched ? "1" : "0"),
            Pair("rebuilt", string.Join(",", Rebuilt.Select(r => RecordCodec.FormatInt(r.Key) + ":" + RecordCodec.FormatInt(r.Value)))),
        });

        /// <summary>
        /// The most pieces an order counts, far above any design (the biggest is a few hundred): a saved count beyond it
        /// is cut to it (the review round of 2026-10-23: a tampered "total=2000000000" hung the hut's owner on every load).
        /// </summary>
        public const int MaxPieces = 20000;

        /// <summary>Null for an empty or unreadable text. Never throws; unknown fields are ignored.</summary>
        public static BuildOrderRecord Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            Dictionary<string, string> f = RecordCodec.Decode(text);
            if (RecordCodec.ParseInt(RecordCodec.Get(f, "v"), 0) < 1) return null;
            int state = RecordCodec.ParseInt(RecordCodec.Get(f, "state"), 0);
            int kind = RecordCodec.ParseInt(RecordCodec.Get(f, "kind"), 0);
            var order = new BuildOrderRecord
            {
                State = Enum.IsDefined(typeof(BuildState), state) ? (BuildState)state : BuildState.Waiting,
                Total = Math.Min(MaxPieces, Math.Max(0, RecordCodec.ParseInt(RecordCodec.Get(f, "total"), 0))),
                Builder = RecordCodec.Get(f, "builder"),
                ClaimTime = ParseDouble(RecordCodec.Get(f, "claim")),
                Created = ParseDouble(RecordCodec.Get(f, "created")),
                Needs = RecordCodec.Get(f, "needs"),
                Blocked = RecordCodec.Get(f, "blocked"),
                Ground = ParseFloat(RecordCodec.Get(f, "ground")),
                Kind = Enum.IsDefined(typeof(OrderKind), kind) ? (OrderKind)kind : OrderKind.Build,
                TargetLevel = HutLevels.Clamp(RecordCodec.ParseInt(RecordCodec.Get(f, "level"), 1)),
                TakeAwayTotal = Math.Min(MaxPieces, Math.Max(0, RecordCodec.ParseInt(RecordCodec.Get(f, "take"), 0))),
                Changed = RecordCodec.Get(f, "changed") == "1",
                First = System.Math.Max(0, ParseDouble(RecordCodec.Get(f, "first"))),
                Paused = RecordCodec.Get(f, "paused") == "1",
                Watched = RecordCodec.Get(f, "watched") == "1",
            };
            foreach (string part in RecordCodec.Get(f, "done").Split(','))
            {
                int index = RecordCodec.ParseInt(part, -1);
                if (index >= 0 && index < order.Total) order.Done.Add(index);
            }
            foreach (string part in RecordCodec.Get(f, "fail").Split(','))
            {
                string[] kv = part.Split(':');
                if (kv.Length != 2) continue;
                int index = RecordCodec.ParseInt(kv[0], -1), count = RecordCodec.ParseInt(kv[1], 0);
                if (index >= 0 && index < order.Total && count > 0) order.Failures[index] = count;
            }
            foreach (string part in RecordCodec.Get(f, "rebuilt").Split(','))
            {
                string[] kv = part.Split(':');
                if (kv.Length != 2) continue;
                int index = RecordCodec.ParseInt(kv[0], -1), count = RecordCodec.ParseInt(kv[1], 0);
                if (index >= 0 && index < order.Total && count > 0) order.Rebuilt[index] = count;
            }
            foreach (string part in RecordCodec.Get(f, "taken").Split(','))
            {
                int index = RecordCodec.ParseInt(part, -1);
                if (index >= 0 && index < order.TakeAwayTotal) order.TakenAway.Add(index);
            }
            return order;
        }

        private static double ParseDouble(string text) => RecordCodec.TryParseFinite(text, out double value) ? value : 0;

        /// <summary>NaN (not set) for anything but a finite number.</summary>
        private static float ParseFloat(string text) => RecordCodec.TryParseFinite(text, out float value) ? value : float.NaN;

        private static KeyValuePair<string, string> Pair(string key, string value) => new KeyValuePair<string, string>(key, value);
    }
}
