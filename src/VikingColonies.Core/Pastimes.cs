using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>
    /// What a settler does for fun in free time (1.6, the life release): mostly at pieces the player builds, a few with
    /// nothing. None is a job's work (no fishing, hunting, farming, gathering, spinning, beekeeping or repairing).
    /// </summary>
    public enum Pastime { None, Archery, WeaponPractice, BoardGame, Toast, PoleDance, Story, Admire, Map, Stretch, Spar, Nap, FireDance, PetTame }

    /// <summary>The piece (or creature) a pastime is played at.</summary>
    public enum Prop { None, Target, Dummy, Table, Pole, Fire, Trophy, MapTable, Tame }

    /// <summary>What a settler in free time has at hand for pastimes: the pieces with room, a partner, the weather, the evening.</summary>
    public struct PastimeChoices
    {
        public PastimeChoices(bool target, bool dummy, bool table, bool pole, bool fire, bool trophy, bool mapTable, bool tame, bool partner,
            bool wet, bool roofedProps, bool evening)
        {
            Target = target;
            Dummy = dummy;
            Table = table;
            Pole = pole;
            Fire = fire;
            Trophy = trophy;
            MapTable = mapTable;
            Tame = tame;
            Partner = partner;
            Wet = wet;
            RoofedProps = roofedProps;
            Evening = evening;
        }

        public bool Target { get; }
        public bool Dummy { get; }
        public bool Table { get; }
        public bool Pole { get; }
        public bool Fire { get; }
        public bool Trophy { get; }
        public bool MapTable { get; }
        public bool Tame { get; }
        /// <summary>Another settler in free time nearby, for the pastimes of two or more.</summary>
        public bool Partner { get; }
        public bool Wet { get; }
        /// <summary>In rain: the pieces said to be at hand are under a roof (the caller counts only those).</summary>
        public bool RoofedProps { get; }
        /// <summary>The evening's gathering at the fire, when dancing there comes in.</summary>
        public bool Evening { get; }

        public bool Has(Prop prop)
        {
            switch (prop)
            {
                case Prop.Target: return Target;
                case Prop.Dummy: return Dummy;
                case Prop.Table: return Table;
                case Prop.Pole: return Pole;
                case Prop.Fire: return Fire;
                case Prop.Trophy: return Trophy;
                case Prop.MapTable: return MapTable;
                case Prop.Tame: return Tame;
                default: return true;
            }
        }
    }

    /// <summary>Which pastimes a settler may take with what is at hand, how likely, and their favourite by trait (1.6).</summary>
    public static class PastimeRules
    {
        private static readonly Pastime[] s_all =
        {
            Pastime.Archery, Pastime.WeaponPractice, Pastime.BoardGame, Pastime.Toast, Pastime.PoleDance, Pastime.Story, Pastime.Admire,
            Pastime.Map, Pastime.Stretch, Pastime.Spar, Pastime.Nap, Pastime.FireDance, Pastime.PetTame,
        };

        /// <summary>Every pastime, in the order they are weighed.</summary>
        public static IReadOnlyList<Pastime> All => s_all;

        public static Prop PropOf(Pastime p)
        {
            switch (p)
            {
                case Pastime.Archery: return Prop.Target;
                case Pastime.WeaponPractice: return Prop.Dummy;
                case Pastime.BoardGame:
                case Pastime.Toast: return Prop.Table;
                case Pastime.PoleDance: return Prop.Pole;
                case Pastime.Story:
                case Pastime.FireDance: return Prop.Fire;
                case Pastime.Admire: return Prop.Trophy;
                case Pastime.Map: return Prop.MapTable;
                case Pastime.PetTame: return Prop.Tame;
                default: return Prop.None;
            }
        }

        /// <summary>How many it takes: a board game and sparring two, a toast two to four, a story a teller and a listener.</summary>
        public static int MinPlayers(Pastime p) =>
            p == Pastime.BoardGame || p == Pastime.Spar || p == Pastime.Toast || p == Pastime.Story ? 2 : 1;

        /// <summary>How many may join: a toast four, a story or a dance as many as the fire or the pole holds.</summary>
        public static int MaxPlayers(Pastime p)
        {
            switch (p)
            {
                case Pastime.BoardGame:
                case Pastime.Spar: return 2;
                case Pastime.Toast: return 4;
                case Pastime.Story:
                case Pastime.FireDance:
                case Pastime.PoleDance: return 6;
                default: return 1;
            }
        }

        /// <summary>How many a piece holds at once; a table and a fire from their seats and ring (the caller's count), here 0.</summary>
        public static int Capacity(Prop prop)
        {
            switch (prop)
            {
                case Prop.Target:
                case Prop.Dummy:
                case Prop.Trophy: return 2;
                case Prop.Pole: return 6;
                case Prop.MapTable:
                case Prop.Tame: return 1;
                default: return 0;
            }
        }

        /// <summary>
        /// How likely a pastime is; 0 when its piece or partner isn't at hand, in rain unless its piece is roofed (stretching
        /// always), a nap never in rain, dancing at the fire only in the evening. Busy over idle: pieces 3, sparring and
        /// stretching 2, a nap and petting 1.
        /// </summary>
        public static int Weight(Pastime p, PastimeChoices c)
        {
            if (p == Pastime.None) return 0;
            Prop prop = PropOf(p);
            if (!c.Has(prop)) return 0;
            if (MinPlayers(p) > 1 && !c.Partner) return 0;
            if (p == Pastime.FireDance && !c.Evening) return 0;
            if (c.Wet && p != Pastime.Stretch && (prop == Prop.None || prop == Prop.Tame || !c.RoofedProps)) return 0;
            switch (p)
            {
                case Pastime.Stretch:
                case Pastime.Spar: return 2;
                case Pastime.Nap:
                case Pastime.PetTame: return 1;
                default: return 3;
            }
        }

        public static Pastime Favourite(Trait t)
        {
            switch (t)
            {
                case Trait.Lazy: return Pastime.Nap;
                case Trait.Chatty: return Pastime.BoardGame;
                case Trait.Brave: return Pastime.Spar;
                case Trait.Timid: return Pastime.Map;
                case Trait.Hardworking: return Pastime.Archery;
                case Trait.Cheerful: return Pastime.PoleDance;
                case Trait.Grumpy: return Pastime.WeaponPractice;
                case Trait.Quiet: return Pastime.Admire;
                default: return Pastime.None;
            }
        }

        public static bool IsDance(Pastime p) => p == Pastime.PoleDance || p == Pastime.FireDance;

        /// <summary>The favourite (for the cheerful, any dance: the maypole is seasonal).</summary>
        public static bool IsFavourite(Pastime p, Trait t) => p != Pastime.None && (p == Favourite(t) || (t == Trait.Cheerful && IsDance(p)));

        /// <summary><see cref="Weight"/>, doubled for their favourite.</summary>
        public static int WeightFor(Pastime p, PastimeChoices c, Trait t)
        {
            int w = Weight(p, c);
            return IsFavourite(p, t) ? w * 2 : w;
        }

        /// <summary>A pastime by <see cref="WeightFor"/>; <paramref name="roll"/> from 0 to 1. None when nothing is possible.</summary>
        public static Pastime Pick(PastimeChoices c, double roll, Trait t)
        {
            int total = 0;
            foreach (Pastime p in s_all) total += WeightFor(p, c, t);
            if (total == 0) return Pastime.None;
            double at = (roll < 0 ? 0 : roll >= 1 ? 0.9999999 : roll) * total;
            int sum = 0;
            Pastime last = Pastime.None;
            foreach (Pastime p in s_all)
            {
                int w = WeightFor(p, c, t);
                if (w == 0) continue;
                sum += w;
                last = p;
                if (at < sum) return p;
            }
            return last;
        }

        /// <summary>A worker's pastimes: pieces this near their hut (the jobless: the camp's).</summary>
        public const float PieceReach = 30f;
        /// <summary>The camp's pieces count for a worker whose hut is this near it.</summary>
        public const float CampReach = 60f;

        /// <summary>
        /// A piece is theirs to use: within <see cref="PieceReach"/> of their hut (or the camp, for the jobless), or of the
        /// camp when it stands within <see cref="CampReach"/> of their hut (the spec's Places).
        /// </summary>
        public static bool InReach(GroundPoint piece, GroundPoint anchor, GroundPoint? camp) =>
            ColonyRules.Distance(piece, anchor) <= PieceReach
            || camp.HasValue && ColonyRules.Distance(camp.Value, anchor) <= CampReach && ColonyRules.Distance(piece, camp.Value) <= PieceReach;

        /// <summary>A spot round a table whose floor stands more than this above the table's foot is on something (a bench, a table).</summary>
        public const float ToastStep = 0.3f;
        /// <summary>A spot round a table whose floor lies more than this below the table's foot is off its floor's edge (downhill outdoors is less).</summary>
        public const float ToastDrop = 0.6f;
        /// <summary>A toast's spot keeps this far from any seat: a body's width past where a seat counts as taken (0.5 m).</summary>
        public const float ToastSeatClear = 0.9f;
        /// <summary>A toast stands this far out from the table's edge (a step back from it).</summary>
        public const float ToastOut = 0.9f;

        /// <summary>
        /// The eight places round a table in its own frame (x along one axis, z the other, from its middle): its two ends, the
        /// middles of its two sides <see cref="ToastOut"/> from its edges, and its four corners as far from them on the slant.
        /// The review of the toast's spots: a circle by the longest reach put a long table's side spots 3 m out in the aisle.
        /// </summary>
        public static List<GroundPoint> AroundTable(float halfX, float halfZ)
        {
            float x = halfX + ToastOut, z = halfZ + ToastOut, slant = ToastOut * 0.7071f;
            return new List<GroundPoint>
            {
                new GroundPoint(x, 0f), new GroundPoint(halfX + slant, halfZ + slant), new GroundPoint(0f, z), new GroundPoint(-halfX - slant, halfZ + slant),
                new GroundPoint(-x, 0f), new GroundPoint(-halfX - slant, -halfZ - slant), new GroundPoint(0f, -z), new GroundPoint(halfX + slant, -halfZ - slant),
            };
        }

        /// <summary>
        /// A spot to stand round a table for a toast: on the floor the table stands on (not up on a bench or the next table, nor
        /// down off a raised floor's edge) and clear of every seat near it. v16's meadhall.txt, 27 Oct: the four spots round a Mead Hall table landed on its neighbour's top
        /// and a bench's end, and the toasting stood 0.8 m up.
        /// </summary>
        public static bool ToastSpotClear(GroundPoint spot, float floorY, float tableFootY, IEnumerable<GroundPoint> seats)
        {
            if (floorY - tableFootY > ToastStep || tableFootY - floorY > ToastDrop) return false;
            if (seats != null)
                foreach (GroundPoint seat in seats)
                    if (ColonyRules.Distance(spot, seat) < ToastSeatClear) return false;
            return true;
        }

        /// <summary>How far a pastime's places keep from the seats vc_seattest holds: as a chat's (<see cref="ChatPairing.HeldSeatClearance"/>).</summary>
        public const float HeldSeatClearance = ChatPairing.HeldSeatClearance;

        /// <summary>
        /// A piece whose places (a table's seats and the spots round it, another piece's spots) are all at least
        /// <see cref="HeldSeatClearance"/> from every seat the seat test holds (none: no test). v16's meadhall.txt, 27 Oct: a
        /// board game sat on the held seats and a toast stood by them, and the seat under test counted as taken.
        /// </summary>
        public static bool ClearOfHeldSeats(IEnumerable<GroundPoint> places, IEnumerable<GroundPoint> heldSeats)
        {
            if (places == null || heldSeats == null) return true;
            var held = new List<GroundPoint>(heldSeats);
            foreach (GroundPoint place in places)
                foreach (GroundPoint seat in held)
                    if (ColonyRules.Distance(place, seat) < HeldSeatClearance) return false;
            return true;
        }

        /// <summary>Free time's whole weight: today's activities (with the trait's leaning) and, when on, the pastimes.</summary>
        public static int FreeTimeTotal(LeisureChoices leisure, PastimeChoices pastimes, bool pastimesOn, Trait trait)
        {
            int total = 0;
            foreach (Leisure l in FreeTimeRules.Order) total += FreeTimeRules.WeightFor(l, leisure, trait);
            if (pastimesOn)
                foreach (Pastime p in s_all) total += WeightFor(p, pastimes, trait);
            return total;
        }

        /// <summary>
        /// Free time's choice with the pastimes (1.6), one roll over both: an activity, or <see cref="Leisure.Pastime"/> with
        /// the pastime in <paramref name="pastime"/>. Off, it is <see cref="FreeTimeRules.Pick(LeisureChoices, double, Trait?)"/>.
        /// </summary>
        public static Leisure PickFreeTime(LeisureChoices leisure, PastimeChoices pastimes, bool pastimesOn, double roll, Trait trait, out Pastime pastime)
        {
            pastime = Pastime.None;
            if (!pastimesOn) return FreeTimeRules.Pick(leisure, roll, trait);
            int total = FreeTimeTotal(leisure, pastimes, true, trait);
            if (total == 0) return Leisure.None;
            double at = (roll < 0 ? 0 : roll >= 1 ? 0.9999999 : roll) * total;
            int sum = 0;
            foreach (Leisure l in FreeTimeRules.Order)
            {
                sum += FreeTimeRules.WeightFor(l, leisure, trait);
                if (at < sum) return l;
            }
            Pastime last = Pastime.None;
            foreach (Pastime p in s_all)
            {
                int w = WeightFor(p, pastimes, trait);
                if (w == 0) continue;
                sum += w;
                last = p;
                if (at < sum) break;
            }
            pastime = last;
            return last == Pastime.None ? Leisure.None : Leisure.Pastime;
        }

        /// <summary>
        /// The game slides a character along with its swings and emotes (root motion). A settler stays where they stand
        /// through every gesture, pose and practice blow; only a job's own swing (outside a pastime) is the game's, as before
        /// (the player's look at 1.6: sparring settlers lunged into each other, a napper slid, a dancer drifted, one slid while
        /// cheering, and one jumped standing up from the ground: the rise without the sinking). Players are left alone.
        /// </summary>
        public static bool StaysPut(bool isSettler, bool inAttack, Pastime pastime, bool posed) =>
            isSettler && (!inAttack || pastime != Pastime.None || posed);

        /// <summary>How long it's kept at: a minute or two.</summary>
        public static float DoSeconds(Pastime p, double roll) => (float)(60 + 60 * (roll < 0 ? 0 : roll > 1 ? 1 : roll));
    }

    /// <summary>
    /// A settler's last pastimes (1.6), saved on their record as "Archery@3" (what, and on which day), newest last: the
    /// varied free time counts the kinds of the last two days, so a settler busy at work for days keeps no old bonus.
    /// </summary>
    public static class PastimeMemory
    {
        public const int Remembered = 5;
        /// <summary>Only pastimes of this many days (today and yesterday) count.</summary>
        public const int Days = 2;

        /// <summary>The list with <paramref name="p"/> added on <paramref name="day"/>, at most <see cref="Remembered"/>.</summary>
        public static List<string> Add(IReadOnlyList<string> last, Pastime p, int day)
        {
            var list = last != null ? new List<string>(last) : new List<string>();
            if (p == Pastime.None) return list;
            list.Add(p + "@" + day.ToString(System.Globalization.CultureInfo.InvariantCulture));
            while (list.Count > Remembered) list.RemoveAt(0);
            return list;
        }

        /// <summary>How many kinds of pastime within the last <see cref="Days"/> days.</summary>
        public static int Distinct(IReadOnlyList<string> last, int today)
        {
            var kinds = new HashSet<Pastime>();
            if (last == null) return 0;
            foreach (string entry in last)
                if (TryRead(entry, out Pastime p, out int day) && MoodDays.Within(day, today, Days)) kinds.Add(p);
            return kinds.Count;
        }

        /// <summary>They took <paramref name="p"/> today.</summary>
        public static bool Did(IReadOnlyList<string> last, Pastime p, int today)
        {
            if (last == null) return false;
            foreach (string entry in last)
                if (TryRead(entry, out Pastime done, out int day) && done == p && day == today) return true;
            return false;
        }

        /// <summary>They took their favourite today (for the cheerful, any dance).</summary>
        public static bool DidFavourite(IReadOnlyList<string> last, Trait trait, int today)
        {
            if (last == null) return false;
            foreach (string entry in last)
                if (TryRead(entry, out Pastime done, out int day) && day == today && PastimeRules.IsFavourite(done, trait)) return true;
            return false;
        }

        private static bool TryRead(string entry, out Pastime p, out int day)
        {
            p = Pastime.None;
            day = -1;
            if (string.IsNullOrEmpty(entry)) return false;
            int at = entry.IndexOf('@');
            if (at <= 0) return false;
            string name = entry.Substring(0, at);
            foreach (Pastime known in PastimeRules.All)
                if (known.ToString() == name)
                {
                    p = known;
                    break;
                }
            return p != Pastime.None
                && int.TryParse(entry.Substring(at + 1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out day);
        }
    }
}
