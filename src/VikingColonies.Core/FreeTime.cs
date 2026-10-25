using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>What a settler does with free time (0.14); the evening by the fire and tending a fire came in 0.15.</summary>
    public enum Leisure { None, Seat, Fire, Chat, Stroll, SitDown, Evening, TendFire, CarryStock }

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

        /// <summary>An activity by weight; <paramref name="roll"/> from 0 to 1. A stroll is always possible.</summary>
        public static Leisure Pick(LeisureChoices choices, double roll) => Pick(choices, roll, null);

        /// <summary>As <see cref="Pick(LeisureChoices, double)"/>, with his trait's leaning (0.16, <see cref="Traits.LeisureWeight"/>).</summary>
        public static Leisure Pick(LeisureChoices choices, double roll, Trait? trait)
        {
            int Weighed(Leisure l) => trait.HasValue ? Traits.LeisureWeight(trait.Value, l, Weight(l, choices)) : Weight(l, choices);
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
