using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>
    /// What a settler does with free time (0.14); the evening by the fire and tending a fire came in 0.15; a pastime (which
    /// one: <see cref="Core.Pastime"/>) in 1.6.
    /// </summary>
    public enum Leisure { None, Seat, Fire, Chat, Stroll, SitDown, Evening, TendFire, CarryStock, Pastime }

    /// <summary>What a settler in free time has at hand: a free seat, a burning fire, a friend to talk to; and the weather.</summary>
    public struct LeisureChoices
    {
        public LeisureChoices(bool seat, bool fire, bool partner, bool wet, bool tend = false, bool carry = false)
        {
            Seat = seat;
            Fire = fire;
            Partner = partner;
            Wet = wet;
            Tend = tend;
            Carry = carry;
        }

        public bool Seat { get; }
        public bool Fire { get; }
        public bool Partner { get; }
        public bool Wet { get; }
        /// <summary>A colony fire nearby is below half, and its fuel is in the colony's chests (0.15).</summary>
        public bool Tend { get; }
        /// <summary>A colony building is short of an item it keeps, and another chest has some to spare (0.21).</summary>
        public bool Carry { get; }
    }

    /// <summary>What stands on a spot in front of a seat, from a seat's height up (<see cref="FreeTimeRules.ChooseSeatSpot"/>).</summary>
    public enum SeatSpotKind
    {
        Clear,
        /// <summary>A table, a bench: a settler sits down across it.</summary>
        Furniture,
        /// <summary>A wall, a door, a railing, the ground falling away: never past it.</summary>
        Solid,
    }

    /// <summary>
    /// Free time (settler life, 0.14): a jobless settler, or one whose job has nothing to do, sits on a seat, sits by a
    /// fire, chats, strolls or sits down near his hut or the Town Hall, picking again every 30-90 seconds.
    /// </summary>
    public static class FreeTimeRules
    {
        /// <summary>How far from his hut (or the Town Hall) he spends free time.</summary>
        public const float Radius = 15f;
        /// <summary>A fire this near his hut (or the Town Hall) is one he sits by and keeps fed.</summary>
        public const float FireReach = Radius + 3f;

        /// <summary>
        /// Whether settlers keep a fire fed: fire tending and free time on, a fire that burns out, and within reach of a
        /// hut or the Town Hall (1.4.2's review: the fire's hover must say so only when they will).
        /// </summary>
        public static bool Tends(bool tendFires, bool freeTime, bool infiniteFuel, float fromNearestHome) =>
            tendFires && freeTime && !infiniteFuel && fromNearestHome <= FireReach;
        /// <summary>By a fire he sits this far from it.</summary>
        public const float FireSitMin = 2f;
        /// <summary>
        /// A stroll ends, and he sits down, at least this far from a fire (full self-test run 6, 0.20: one strolled into the
        /// life test's fire, sat down there and burned to death).
        /// </summary>
        public const float FireClearance = 1.6f;

        /// <summary>The spot is within <see cref="FireClearance"/> of one of these fires.</summary>
        public static bool TooNearAFire(GroundPoint spot, IEnumerable<GroundPoint> fires) =>
            fires != null && fires.Any(f => Math.Sqrt((f.X - spot.X) * (f.X - spot.X) + (f.Z - spot.Z) * (f.Z - spot.Z)) < FireClearance);
        public const float FireSitMax = 3f;
        /// <summary>Settlers at a fire keep this far apart.</summary>
        public const float FireSpacing = 1f;
        /// <summary>A friend this near can be asked for a chat.</summary>
        public const float ChatRange = 20f;
        /// <summary>Two chatting stand this far apart.</summary>
        public const float ChatGap = 1.6f;
        /// <summary>A player this near is looked at, and greeted once a day.</summary>
        public const float LookAtPlayer = 6f;
        /// <summary>A seat with a player this near its point is the player's.</summary>
        public const float SeatNearPlayer = 0.5f;
        /// <summary>A seat this near a burning fire is preferred.</summary>
        public const float FireNearSeat = 6f;

        /// <summary>A settler sits down from this near a seat with nothing between (as a player sits from beside it).</summary>
        public const float SitFrom = 1.3f;
        /// <summary>The farthest a settler sits down from, across a table or bench in front of the seat (a player can from 5 m).</summary>
        public const float SitAcrossMax = 4f;
        /// <summary>The walk to a seat stops this far in front of it when nothing stands there.</summary>
        public const float SeatSpot = 0.7f;
        /// <summary>From the walk's spot beyond a table, a settler sits down from this much further: the game's paths stop half a metre or more short of furniture (runs 8-9).</summary>
        public const float SitAcrossSlack = 1.3f;

        private static readonly float[] s_seatSpotSteps = { 0.7f, 1.0f, 1.3f, 1.6f, 1.9f, 2.2f, 2.5f };

        /// <summary>How far in front of a seat its walk may stop, nearest first.</summary>
        public static IEnumerable<float> SeatSpotSteps() => s_seatSpotSteps;

        /// <summary>
        /// Where in front of a seat its walk stops (the Mead Hall's seat test, 25 Oct: its long table stands 5 cm in front of
        /// the benches, so 0.7 m is under it): the first step clear of everything, past furniture only (a table, a bench);
        /// anything else in the way (a wall, a door, a railing, the ground falling away) and it's 0.7 m as always.
        /// <paramref name="reach"/>: from how far the settler may sit down, across the furniture, from that spot.
        /// </summary>
        public static float ChooseSeatSpot(Func<float, SeatSpotKind> at, out float reach)
        {
            foreach (float d in s_seatSpotSteps)
            {
                SeatSpotKind kind = at(d);
                if (kind == SeatSpotKind.Clear)
                {
                    reach = d <= SeatSpot ? SitFrom : Math.Min(SitAcrossMax, d + SitAcrossSlack);
                    return d;
                }
                if (kind == SeatSpotKind.Solid) break;
            }
            reach = SitFrom;
            return SeatSpot;
        }

        /// <summary>A station this near a fire on the plan stands over it (the game puts it on the fire's spot).</summary>
        public const float OverFire = 1f;

        /// <summary>
        /// A cook's fire (1.6): a cooking station, cauldron or iron cooking station stands over it. Settlers don't spend
        /// their free time standing by it (the player's look at the Mead Hall: they crowded round its fire pit, onto the
        /// long table and the benches); its fuel is still kept up.
        /// </summary>
        public static bool IsCooksFire(GroundPoint fire, IEnumerable<GroundPoint> stationsOverFires)
        {
            if (stationsOverFires == null) return false;
            foreach (GroundPoint s in stationsOverFires)
            {
                float dx = s.X - fire.X, dz = s.Z - fire.Z;
                if (dx * dx + dz * dz <= OverFire * OverFire) return true;
            }
            return false;
        }

        /// <summary>
        /// A station that makes the fire under it a cook's: one that needs a fire, inside the colony's Mead Hall, where
        /// the cook works it (the review of the Mead Hall's designs: a spit over the camp's campfire leaves it the
        /// colony's fire to gather round).
        /// </summary>
        public static bool IsCooksStation(bool needsFire, bool inMeadHall) => needsFire && inMeadHall;

        private static readonly Leisure[] s_order = { Leisure.TendFire, Leisure.CarryStock, Leisure.Seat, Leisure.Fire, Leisure.Chat, Leisure.Stroll, Leisure.SitDown };

        /// <summary>How likely each activity is; 0 when there is nothing to use (or, sitting on the ground, in rain).</summary>
        public static int Weight(Leisure leisure, LeisureChoices choices)
        {
            switch (leisure)
            {
                case Leisure.Seat: return choices.Seat ? 3 : 0;
                case Leisure.Fire: return choices.Fire ? 3 : 0;
                case Leisure.Chat: return choices.Partner ? 2 : 0;
                case Leisure.Stroll: return 1;
                case Leisure.SitDown: return choices.Wet ? 0 : 1;
                case Leisure.TendFire: return choices.Tend ? 5 : 0;
                case Leisure.CarryStock: return choices.Carry ? 4 : 0;
                default: return 0;   // the evening is not picked: it comes with the time of day
            }
        }

        /// <summary>The activities in the order they are weighed (a pastime is weighed by <see cref="PastimeRules"/>).</summary>
        public static IReadOnlyList<Leisure> Order => s_order;

        /// <summary><see cref="Weight"/> with his trait's leaning, when he has one.</summary>
        public static int WeightFor(Leisure leisure, LeisureChoices choices, Trait? trait) =>
            trait.HasValue ? Traits.LeisureWeight(trait.Value, leisure, Weight(leisure, choices)) : Weight(leisure, choices);

        /// <summary>An activity by weight; <paramref name="roll"/> from 0 to 1. A stroll is always possible.</summary>
        public static Leisure Pick(LeisureChoices choices, double roll) => Pick(choices, roll, null);

        /// <summary>As <see cref="Pick(LeisureChoices, double)"/>, with his trait's leaning (0.16, <see cref="Traits.LeisureWeight"/>).</summary>
        public static Leisure Pick(LeisureChoices choices, double roll, Trait? trait)
        {
            int Weighed(Leisure l) => WeightFor(l, choices, trait);
            int total = 0;
            foreach (Leisure l in s_order) total += Weighed(l);
            if (total == 0) return Leisure.None;
            double at = Clamp(roll) * total;
            int sum = 0;
            Leisure last = Leisure.None;
            foreach (Leisure l in s_order)
            {
                int w = Weighed(l);
                if (w == 0) continue;
                sum += w;
                last = l;
                if (at < sum) return l;
            }
            return last;
        }

        public static float ChoiceSeconds(double roll) => (float)(30 + 60 * Clamp01(roll));

        /// <summary>
        /// Where one by a door steps to when there's nowhere to stroll (the 1.6.0 self-test a6d3: moved to just in front of a
        /// storehouse's door, she "strolled" where she stood and kept its courier out): a metre past <paramref name="clearance"/>
        /// from the door, straight away from it (through where they stand) first, then 45 and 90 degrees to either side. The
        /// angles are from that line, not the door's facing: beside a door along its wall, one may point into the hut, which
        /// the caller's roof and wall checks turn down.
        /// </summary>
        public static IReadOnlyList<GroundPoint> StepsClearOfDoor(GroundPoint me, GroundPoint door, float clearance)
        {
            float dx = me.X - door.X, dz = me.Z - door.Z, length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 0.01f)
            {
                dx = 1f;   // on the door's very spot: any way out will do
                dz = 0f;
                length = 1f;
            }
            dx /= length;
            dz /= length;
            float reach = clearance + 1f;
            var steps = new List<GroundPoint>();
            foreach (float degrees in s_doorStepAngles)
            {
                double a = degrees * Math.PI / 180.0;
                float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
                steps.Add(new GroundPoint(door.X + (dx * cos - dz * sin) * reach, door.Z + (dx * sin + dz * cos) * reach));
            }
            return steps;
        }

        private static readonly float[] s_doorStepAngles = { 0f, 45f, -45f, 90f, -90f };

        /// <summary>
        /// A walk to a seat may end this far short of its spot and count as arriving (<see cref="MoveRules.ReachedPathEnd"/>):
        /// from as far as this the last steps go straight on (<see cref="WalkOnToSeatSpot"/>).
        /// </summary>
        public const float LastStepsReach = MoveRules.MinStopDistance + MoveRules.PathEndSlack;
        /// <summary>The last steps to a seat's spot take this long at most...</summary>
        public const float LastStepsSeconds = 5f;
        /// <summary>... and stop when they get no closer for this long (a bench, a settler in the way).</summary>
        public const float LastStepsStallSeconds = 1.5f;
        /// <summary>This near the spot, more steps don't help.</summary>
        public const float LastStepsDone = 0.3f;
        /// <summary>Not to a spot higher or lower than this (a seat up on a platform above where the way ended), as sitting down.</summary>
        public const float LastStepsHeight = 1f;

        /// <summary>
        /// The walk to a seat with a table in front ended <paramref name="toSpot"/> short of its spot, out of reach of the
        /// seat (the Mead Hall's seat test, 26 Oct: the game's paths ended 1.5 m short, just inside its door): with nothing
        /// between and <paramref name="heightOff"/> metres above or below, the settler walks the last steps straight on,
        /// <paramref name="stepping"/> seconds so far and <paramref name="sinceCloser"/> since getting closer; false, and
        /// the seat is given up as before.
        /// </summary>
        public static bool WalkOnToSeatSpot(float toSpot, float heightOff, bool clearToSpot, float stepping, float sinceCloser) =>
            clearToSpot && Math.Abs(heightOff) <= LastStepsHeight && toSpot > LastStepsDone && toSpot <= LastStepsReach
            && stepping <= LastStepsSeconds && sinceCloser <= LastStepsStallSeconds;

        /// <summary>Kept to vc_seattest's seat, a settler chooses again at most this often (<see cref="MayChooseAgain"/>).</summary>
        public const float TestSeatRetrySeconds = 1f;

        /// <summary>
        /// Whether a choice that is due may be made now, <paramref name="sinceLastChoice"/> seconds after the last. Kept to
        /// vc_seattest's seat, at most once a second: the test gives back a seat just given up on (a walk that ended in the
        /// doorway, out of reach of it), and back on the same spot the settler gave it up again every tick (run 10, 25 Oct:
        /// 614 log lines). Normal play passes such a seat over, so a choice comes as soon as it's due.
        /// </summary>
        public static bool MayChooseAgain(float sinceLastChoice, bool keptToTestSeat) =>
            !keptToTestSeat || sinceLastChoice >= TestSeatRetrySeconds;

        /// <summary>How long he keeps at it once there.</summary>
        public static float DoSeconds(Leisure leisure, double roll)
        {
            double r = Clamp01(roll);
            switch (leisure)
            {
                case Leisure.Seat:
                case Leisure.Fire: return (float)(40 + 50 * r);
                case Leisure.Chat: return (float)(20 + 25 * r);
                case Leisure.Stroll: return (float)(5 + 5 * r);
                case Leisure.SitDown: return (float)(30 + 30 * r);
                default: return 0f;
            }
        }

        /// <summary>In a chat, the next gesture comes after this long.</summary>
        public static float GestureEverySeconds(double roll) => (float)(4 + 3 * Clamp01(roll));

        private static readonly string[] s_freeWaits =
        {
            "$vc_status_jobless", "$vc_status_no_trees", "$vc_status_crops_growing", "$vc_status_nothing_to_pick", "$vc_status_no_work",
        };

        /// <summary>A status that means "nothing to do": free time. Waits that ask for the player ("Needs …") aren't.</summary>
        public static bool IsFreeWait(string status) => status != null && Array.IndexOf(s_freeWaits, status) >= 0;

        /// <summary>For picking by weight: 0 up to just under 1.</summary>
        private static double Clamp(double roll) => roll < 0 ? 0 : roll >= 1 ? 0.9999999 : roll;

        private static double Clamp01(double roll) => roll < 0 ? 0 : roll > 1 ? 1 : roll;
    }

    /// <summary>A settler who might chat: his id, where he stands, and whether he is free (in free time, not chatting).</summary>
    public struct ChatCandidate
    {
        public ChatCandidate(string id, GroundPoint at, bool free)
        {
            Id = id ?? "";
            At = at;
            Free = free;
        }

        public string Id { get; }
        public GroundPoint At { get; }
        public bool Free { get; }
    }

    /// <summary>Where two settlers stand to chat: halfway, <see cref="FreeTimeRules.ChatGap"/> apart; First is the asker's.</summary>
    public struct ChatMeeting
    {
        public ChatMeeting(GroundPoint first, GroundPoint second)
        {
            First = first;
            Second = second;
        }

        public GroundPoint First { get; }
        public GroundPoint Second { get; }
    }

    /// <summary>Chats in free time: the nearest free settler asks, the one with the lower id asking so only one does.</summary>
    public static class ChatPairing
    {
        /// <summary>The nearest free settler within <see cref="FreeTimeRules.ChatRange"/>, or "".</summary>
        public static string Partner(string me, GroundPoint at, System.Collections.Generic.IEnumerable<ChatCandidate> others) =>
            Partners(me, at, others).FirstOrDefault() ?? "";

        /// <summary>
        /// The free settlers within <see cref="FreeTimeRules.ChatRange"/>, nearest first: when a wall stands between the
        /// asker and the first wherever they'd meet, the next is asked (1.4.2's review, done in 1.4.3).
        /// </summary>
        public static List<string> Partners(string me, GroundPoint at, System.Collections.Generic.IEnumerable<ChatCandidate> others) =>
            (others ?? new ChatCandidate[0])
                .Where(c => c.Free && c.Id != me && c.Id.Length > 0 && ColonyRules.Distance(at, c.At) <= FreeTimeRules.ChatRange)
                .OrderBy(c => ColonyRules.Distance(at, c.At))
                .Select(c => c.Id)
                .ToList();

        /// <summary>How many are asked, nearest first, before a stroll alone (1.4.3).</summary>
        public const int Tries = 3;

        /// <summary>The ones asked to chat: the first <see cref="Tries"/> of <see cref="Partners"/>.</summary>
        public static List<string> ToAsk(string me, GroundPoint at, System.Collections.Generic.IEnumerable<ChatCandidate> others) =>
            Partners(me, at, others).Take(Tries).ToList();

        /// <summary>
        /// "Astrid", "Astrid or Bjorn", "Astrid, Bjorn or Arne", or "nobody": the ones asked, in one log line (1.4.3's review:
        /// a line per try made three lines of one choice).
        /// </summary>
        public static string Names(IList<string> names)
        {
            if (names == null || names.Count == 0) return "nobody";
            if (names.Count == 1) return names[0];
            return string.Join(", ", names.Take(names.Count - 1)) + " or " + names[names.Count - 1];
        }

        /// <summary>Only one of the two asks: the one whose id comes first.</summary>
        public static bool Asks(string me, string other) => string.CompareOrdinal(me ?? "", other ?? "") < 0;

        /// <summary>
        /// Where two may meet, in order: halfway, then beside the asker, then beside the other. The first with no wall
        /// between its two spots is used (1.4.2, the player: "I don't want them talking to each other through a wall").
        /// </summary>
        public static List<ChatMeeting> Meetings(GroundPoint a, GroundPoint b)
        {
            ChatMeeting halfway = Meeting(a, b);
            float dx = (halfway.Second.X - halfway.First.X) / FreeTimeRules.ChatGap, dz = (halfway.Second.Z - halfway.First.Z) / FreeTimeRules.ChatGap;
            float gap = FreeTimeRules.ChatGap;
            return new List<ChatMeeting>
            {
                halfway,
                new ChatMeeting(a, new GroundPoint(a.X + dx * gap, a.Z + dz * gap)),
                new ChatMeeting(new GroundPoint(b.X - dx * gap, b.Z - dz * gap), b),
            };
        }

        /// <summary>
        /// How far a chat's spots keep from a door: two talking in a doorway block it (the 1.5.0 release run: a courier gave
        /// up at the storehouse's door, "Solveig stood in the doorway"). 3 m, as a stroll's (its review: at 2 m a chatter who
        /// stops 0.4 m short stands on the door's landing, 1.5 m beyond it, where anyone going through waits for them).
        /// </summary>
        public const float DoorClearance = 3f;

        /// <summary>
        /// How far a chat's spots keep from the seats vc_seattest holds (26 Oct: two met on the Mead Hall's bench 0.4 m from
        /// the seat under test, and it counted as taken): as from a door, past where the walk to a seat stops (up to 2.5 m).
        /// </summary>
        public const float HeldSeatClearance = DoorClearance;

        /// <summary>Both spots at least <see cref="HeldSeatClearance"/> from every seat the seat test holds (none: no test).</summary>
        public static bool ClearOfHeldSeats(ChatMeeting meeting, IEnumerable<GroundPoint> heldSeats)
        {
            foreach (GroundPoint seat in heldSeats ?? Enumerable.Empty<GroundPoint>())
                if (ColonyRules.Distance(meeting.First, seat) < HeldSeatClearance || ColonyRules.Distance(meeting.Second, seat) < HeldSeatClearance) return false;
            return true;
        }

        /// <summary>Both spots at least <see cref="DoorClearance"/> from every door.</summary>
        public static bool ClearOfDoors(ChatMeeting meeting, IEnumerable<GroundPoint> doors)
        {
            foreach (GroundPoint door in doors ?? Enumerable.Empty<GroundPoint>())
                if (ColonyRules.Distance(meeting.First, door) < DoorClearance || ColonyRules.Distance(meeting.Second, door) < DoorClearance) return false;
            return true;
        }

        /// <summary>
        /// The height a meeting's two spots are checked at (the review of 1.4.2: each at its own settler's height, a wall
        /// check on a slope climbed over the wall): halfway between them, else the height of the one it's beside.
        /// </summary>
        public static float MeetingHeight(int meeting, float myHeight, float theirHeight) =>
            meeting == 1 ? myHeight : meeting == 2 ? theirHeight : (myHeight + theirHeight) / 2f;

        public static ChatMeeting Meeting(GroundPoint a, GroundPoint b)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 0.01f)
            {
                dx = 1f;
                dz = 0f;
                length = 1f;
            }
            dx /= length;
            dz /= length;
            float midX = (a.X + b.X) / 2f, midZ = (a.Z + b.Z) / 2f, half = FreeTimeRules.ChatGap / 2f;
            return new ChatMeeting(new GroundPoint(midX - dx * half, midZ - dz * half), new GroundPoint(midX + dx * half, midZ + dz * half));
        }
    }

    /// <summary>A settler greets each player once a day (0.14), remembered on each computer.</summary>
    public sealed class GreetMemory
    {
        private readonly System.Collections.Generic.Dictionary<string, int> _greeted = new System.Collections.Generic.Dictionary<string, int>();

        public bool ShouldGreet(string settlerId, long playerId, int today, float distance) =>
            distance <= FreeTimeRules.LookAtPlayer && !(_greeted.TryGetValue(Key(settlerId, playerId), out int day) && day == today);

        public void Mark(string settlerId, long playerId, int today) => _greeted[Key(settlerId, playerId)] = today;

        private static string Key(string settlerId, long playerId) => (settlerId ?? "") + "|" + playerId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>What a settler in free time is doing, as the hover says it: "Crops growing (on a bench)".</summary>
    public static class ActivityWords
    {
        public static string Token(Leisure leisure)
        {
            switch (leisure)
            {
                case Leisure.Seat: return "$vc_doing_seat";
                case Leisure.Fire: return "$vc_doing_fire";
                case Leisure.Chat: return "$vc_doing_chat";
                case Leisure.Stroll: return "$vc_doing_stroll";
                case Leisure.SitDown: return "$vc_doing_sitdown";
                case Leisure.Evening: return "$vc_doing_evening";
                case Leisure.TendFire: return "$vc_doing_tending";
                case Leisure.CarryStock: return "$vc_doing_carrying";
                default: return "";
            }
        }

        /// <summary>A story's listeners (1.6): "listening to Ulf's story", the teller saved as the partner.</summary>
        public const string Listening = "$vc_doing_listening";

        /// <summary>A pastime's words (1.6): "$vc_doing_archery".</summary>
        public static string Token(Pastime pastime) => pastime == Pastime.None ? "" : "$vc_doing_" + pastime.ToString().ToLowerInvariant();

        /// <summary>
        /// The value saved on the settler: the activity's words, and the friend he chats with, by name (for the hover) and
        /// by id (whose turn it is to speak: two of one name took the same turns, 0.18).
        /// </summary>
        public static string Encode(string token, string partner, string partnerId = "") =>
            string.IsNullOrEmpty(partner) ? token ?? ""
            : (token ?? "") + "|" + partner + (string.IsNullOrEmpty(partnerId) ? "" : "|" + partnerId);

        public static bool Decode(string saved, out string token, out string partner) => Decode(saved, out token, out partner, out _);

        /// <summary>Also reads what 0.14-0.17 wrote ("token|name": no id).</summary>
        public static bool Decode(string saved, out string token, out string partner, out string partnerId)
        {
            token = partner = partnerId = "";
            if (string.IsNullOrEmpty(saved)) return false;
            string[] parts = saved.Split('|');
            token = parts[0];
            if (parts.Length > 1) partner = parts[1];
            if (parts.Length > 2) partnerId = parts[2];
            return token.Length > 0;
        }

        /// <summary>
        /// Whether a doing shows in brackets in the hover: never the evening's, which its status says whole, at a fire or at
        /// the hall (1.4.2: "Evening at the Town Hall (no fire is lit outdoors) (the evening by the fire)").
        /// </summary>
        public static bool InHover(string token) => !string.IsNullOrEmpty(token) && token != Token(Leisure.Evening);

        public static string Hover(string status, string activity)
        {
            if (string.IsNullOrEmpty(activity)) return status ?? "";
            if (string.IsNullOrEmpty(status)) return activity;
            // Said once when the status says it already (the hub's store pictures: "Evening by the fire (the evening by the fire)").
            string said = activity.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? activity.Substring(4) : activity;
            if (status.IndexOf(said, StringComparison.OrdinalIgnoreCase) >= 0) return status;
            return status + " (" + activity + ")";
        }
    }
}
