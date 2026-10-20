using System;

namespace VikingColonies.Core
{
    /// <summary>What the colony has, as its Town Hall sees it: settlers, Longhouse beds, food in the Town Hall's chest.</summary>
    public struct ColonyNeeds
    {
        public ColonyNeeds(int settlers, int beds, int food, int lastArrivalDay, int today, bool night, MoodLevel mood = MoodLevel.Content)
        {
            Mood = mood;
            Settlers = settlers;
            Beds = beds;
            Food = food;
            LastArrivalDay = lastArrivalDay;
            Today = today;
            Night = night;
        }

        public int Settlers { get; }
        public int Beds { get; }
        public int Food { get; }
        public int LastArrivalDay { get; }
        public int Today { get; }
        public bool Night { get; }
        /// <summary>The colony's mood (0.17): content when mood is off or not known.</summary>
        public MoodLevel Mood { get; }
        public int FreeBeds => Math.Max(0, Beds - Settlers);
    }

    /// <summary>
    /// The settings: newcomers a day (0 = none), the food each eats on arrival, the colony's cap, and how many its Town
    /// Hall's level makes room for (1.4).
    /// </summary>
    public struct NewcomerRules
    {
        public NewcomerRules(int perDay, int foodEach, int maxSettlers, int hallSettlers = int.MaxValue)
        {
            PerDay = Math.Max(0, perDay);
            FoodEach = Math.Max(0, foodEach);
            MaxSettlers = Math.Max(0, maxSettlers);
            HallSettlers = Math.Max(0, hallSettlers);
        }

        public int PerDay { get; }
        public int FoodEach { get; }
        public int MaxSettlers { get; }
        /// <summary>The Town Hall's level's room (<see cref="HallPerks.Settlers"/>); int.MaxValue when not given.</summary>
        public int HallSettlers { get; }
    }

    /// <summary>What the next newcomer waits for (the Town Hall's window says it).</summary>
    public enum NewcomerWait { None, Off, Full, Bed, Food, Tomorrow, Morning, Mood, HallFull }

    /// <summary>
    /// A colony grows by itself: once a day, in daylight, while it has a free bed (more Longhouse beds than settlers) and
    /// enough food in the Town Hall's chest, newcomers arrive there, each eating his share of that food. At most
    /// <see cref="NewcomerRules.PerDay"/> a day (one more when the colony is joyful), never past the cap; nobody joins an
    /// unhappy colony (0.17).
    /// </summary>
    public static class Newcomers
    {
        /// <summary>"Nobody has come yet."</summary>
        public const int NeverCame = -1;

        public static int HowMany(ColonyNeeds colony, NewcomerRules rules)
        {
            if (WaitsFor(colony, rules) != NewcomerWait.None) return 0;
            int perDay = rules.PerDay + (colony.Mood == MoodLevel.Joyful ? 1 : 0);
            int fed = rules.FoodEach > 0 ? colony.Food / rules.FoodEach : perDay;
            return Math.Max(0, Min(perDay, colony.FreeBeds, Math.Min(rules.MaxSettlers, rules.HallSettlers) - colony.Settlers, fed));
        }

        public static NewcomerWait WaitsFor(ColonyNeeds colony, NewcomerRules rules)
        {
            if (rules.PerDay == 0) return NewcomerWait.Off;
            if (colony.Settlers >= rules.MaxSettlers) return NewcomerWait.Full;
            if (colony.Settlers >= rules.HallSettlers) return NewcomerWait.HallFull;   // "raise the Town Hall for more" (1.4)
            if (colony.FreeBeds == 0) return NewcomerWait.Bed;
            if (colony.Food < rules.FoodEach) return NewcomerWait.Food;
            if (colony.Mood < MoodLevel.Content) return NewcomerWait.Mood;   // "Nobody wants to join an unhappy colony"
            if (colony.LastArrivalDay == colony.Today) return NewcomerWait.Tomorrow;   // a later day: the clock was put back
            if (colony.Night) return NewcomerWait.Morning;
            return NewcomerWait.None;
        }

        private static int Min(int a, int b, int c, int d) => Math.Min(Math.Min(a, b), Math.Min(c, d));
    }
}
