using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    public enum Presence { Present, Gone, Unknown }

    public sealed class HallInfo
    {
        public HallInfo(string colonyId, GroundPoint pos)
        {
            ColonyId = colonyId;
            Pos = pos;
        }

        public string ColonyId { get; }
        public GroundPoint Pos { get; }
    }

    public sealed class CitizenInfo
    {
        public CitizenInfo(string id, string colonyId, string workplace, GroundPoint pos, string calling = "")
        {
            Id = id;
            ColonyId = colonyId;
            Workplace = workplace;
            Pos = pos;
            Calling = calling ?? "";
        }

        /// <summary>The trade he's good at (0.16): a hut of that trade hires him first.</summary>
        public string Calling { get; }

        public string Id { get; }
        public string ColonyId { get; }
        public string Workplace { get; }
        public GroundPoint Pos { get; }
    }

    /// <summary>The colony's membership and staffing rules.</summary>
    public static class ColonyRules
    {
        public const int MaxStartingCitizens = 10;

        /// <summary>The nearest Town Hall within the radius (ties: smaller colony id), or null.</summary>
        public static string ChooseColony(GroundPoint building, IEnumerable<HallInfo> halls, float radius)
        {
            HallInfo best = null;
            float bestDist = float.MaxValue;
            foreach (HallInfo hall in halls)
            {
                float d = Distance(building, hall.Pos);
                if (d > radius) continue;
                if (best == null || d < bestDist || (d == bestDist && string.CompareOrdinal(hall.ColonyId, best.ColonyId) < 0))
                {
                    best = hall;
                    bestDist = d;
                }
            }
            return best?.ColonyId;
        }

        public static int CitizensToSpawn(int configured, int members) =>
            Math.Max(0, Math.Min(Math.Max(configured, 0), MaxStartingCitizens) - Math.Max(members, 0));

        public static bool IsAdoptable(string colonyId, GroundPoint pos, GroundPoint hall, float radius) =>
            string.IsNullOrEmpty(colonyId) && Distance(pos, hall) <= radius;

        /// <summary>The jobless member of the colony nearest the workplace (ties: smaller id), or null.</summary>
        /// <summary>
        /// The jobless settler of the colony a hut hires: one good at its trade (<paramref name="job"/>) first (0.16), then the
        /// nearest; ties go to the lower id.
        /// </summary>
        public static string PickWorker(GroundPoint workplace, string colonyId, IEnumerable<CitizenInfo> citizens, string job = null)
        {
            if (string.IsNullOrEmpty(colonyId)) return null;
            CitizenInfo best = null;
            float bestDist = float.MaxValue;
            bool bestFits = false;
            foreach (CitizenInfo c in citizens)
            {
                if (c.ColonyId != colonyId || !string.IsNullOrEmpty(c.Workplace) || string.IsNullOrEmpty(c.Id)) continue;
                bool fits = Callings.Matches(c.Calling, job);
                float d = Distance(workplace, c.Pos);
                if (best == null || fits && !bestFits || fits == bestFits && (d < bestDist || (d == bestDist && string.CompareOrdinal(c.Id, best.Id) < 0)))
                {
                    best = c;
                    bestDist = d;
                    bestFits = fits;
                }
            }
            return best?.Id;
        }

        /// <summary>A building only counts as gone when its spot is loaded and it isn't there.</summary>
        public static Presence PresenceOf(bool found, bool areaLoaded) =>
            found ? Presence.Present : areaLoaded ? Presence.Gone : Presence.Unknown;

        /// <summary>How long a building must be seen missing before it counts as gone (a joining player can take that long to receive a big base).</summary>
        public const float GoneConfirmSeconds = 30f;

        /// <summary>Observations in a row that cover <see cref="GoneConfirmSeconds"/> when checking every <paramref name="checkIntervalSeconds"/>.</summary>
        public static int GoneChecksFor(float checkIntervalSeconds) =>
            checkIntervalSeconds <= 0f ? (int)GoneConfirmSeconds : (int)Math.Ceiling(GoneConfirmSeconds / checkIntervalSeconds);

        /// <summary>Where to check that the Town Hall's area is loaded: its known spot, else the citizen's own (v1 settlers never saved it).</summary>
        public static float[] CheckPoint(float[] colonyPos, float[] ownPos) =>
            colonyPos != null && colonyPos.Length == 3 ? colonyPos : ownPos;

        /// <summary>
        /// How close to a Town Hall the player must stand to place a job hut. The game measures from the player and a
        /// piece lands up to 5 m further, so this keeps the hut itself inside the colony area.
        /// </summary>
        public static float HutBuildRange(float colonyRadius) => Math.Max(colonyRadius - 6f, 5f);

        // Checks made by the computer running a citizen before it applies a change another computer asked for.

        public static bool CanEmploy(CitizenRecord citizen, string colonyId) =>
            !string.IsNullOrEmpty(colonyId) && citizen.ColonyId == colonyId && !citizen.IsEmployed;

        public static bool CanUnemploy(CitizenRecord citizen, string workplaceId) =>
            !string.IsNullOrEmpty(workplaceId) && citizen.Workplace == workplaceId;

        public static bool CanJoinColony(CitizenRecord citizen, string colonyId) =>
            !string.IsNullOrEmpty(colonyId) && !citizen.HasColony;

        public static float Distance(GroundPoint a, GroundPoint b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>Confirms "gone" only after several observations in a row, so a slowly loading area isn't mistaken for a destroyed building.</summary>
    public sealed class GoneTracker
    {
        private readonly int _needed;
        private int _streak;

        /// <summary>Gone seen this many times in a row so far, and what was seen last (a test reads them).</summary>
        public int Streak => _streak;
        public Presence Last { get; private set; } = Presence.Unknown;

        public GoneTracker(int confirmations = 3) { _needed = Math.Max(1, confirmations); }

        public bool Observe(Presence presence)
        {
            Last = presence;
            if (presence != Presence.Gone)
            {
                _streak = 0;
                return false;
            }
            _streak++;
            return _streak >= _needed;
        }

        public void Reset() => _streak = 0;
    }
}
