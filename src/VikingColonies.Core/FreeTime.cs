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
        public static string Partner(string me, GroundPoint at, System.Collections.Generic.IEnumerable<ChatCandidate> others)
        {
            string best = "";
            float bestDistance = float.MaxValue;
            foreach (ChatCandidate c in others)
            {
                if (!c.Free || c.Id == me || c.Id.Length == 0) continue;
                float d = ColonyRules.Distance(at, c.At);
                if (d > FreeTimeRules.ChatRange || d >= bestDistance) continue;
                best = c.Id;
                bestDistance = d;
            }
            return best;
        }

        /// <summary>Only one of the two asks: the one whose id comes first.</summary>
        public static bool Asks(string me, string other) => string.CompareOrdinal(me ?? "", other ?? "") < 0;

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

        public static string Hover(string status, string activity)
        {
            if (string.IsNullOrEmpty(activity)) return status ?? "";
            if (string.IsNullOrEmpty(status)) return activity;
            return status + " (" + activity + ")";
        }
    }
}
