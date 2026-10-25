using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>A settler's day (settler life, 0.15): work by day, the evening by the fire, the night in bed.</summary>
    public enum DayPhase { Night, Day, Evening }

    public static class TheDay
    {
        /// <summary>The evening starts here (the game's night starts at 0.75): about 90 seconds of a 30-minute day.</summary>
        public const float EveningStart = 0.70f;

        /// <summary>The phase from the game's day fraction and its night.</summary>
        public static DayPhase Phase(float fraction, bool night)
        {
            if (night) return DayPhase.Night;
            return fraction >= EveningStart && fraction < 0.75f ? DayPhase.Evening : DayPhase.Day;
        }
    }

    /// <summary>A fire the colony might gather at in the evening.</summary>
    public struct GatheringFire
    {
        public GatheringFire(string id, GroundPoint at, bool burning, bool roofed, bool atHall = false, bool inHut = false)
        {
            Id = id ?? "";
            At = at;
            Burning = burning;
            Roofed = roofed;
            AtHall = atHall;
            InHut = inHut;
        }

        /// <summary>Part of one of the colony's huts (the Longhouse's fire pit): that hut's hearth, not the colony's evening fire.</summary>
        public bool InHut { get; }

        public string Id { get; }
        public GroundPoint At { get; }
        public bool Burning { get; }
        public bool Roofed { get; }
        /// <summary>The Town Hall camp's own fire (huts round 2): the colony's meeting place.</summary>
        public bool AtHall { get; }
    }

    /// <summary>
    /// The evening gathering (0.15): at the burning fire inside the colony nearest its Town Hall (the Town Hall camp's own
    /// fire first, then one under a roof), or at the Town Hall; settlers who find no seat stand or sit on rings round it, a metre apart, facing it.
    /// </summary>
    public static class Gathering
    {
        /// <summary>
        /// Who has a place on the ring: the colony's roster (the same on every computer) and any settler loaded here not
        /// on it yet, sorted by id (0.18: each computer counted only the settlers loaded there).
        /// </summary>
        public static List<string> Members(IEnumerable<string> roster, IEnumerable<string> loaded) =>
            (roster ?? Enumerable.Empty<string>()).Concat(loaded ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrEmpty(id)).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();

        /// <summary>The first ring's radius; each further ring is a metre out.</summary>
        public const float RingInner = 2.2f;
        public const float RingStep = 1f;
        /// <summary>Neighbours on a ring stand this far apart at least.</summary>
        public const float Spacing = 1f;

        /// <summary>The index of the fire to gather at, or -1 for the Town Hall.</summary>
        public static int Place(IReadOnlyList<GatheringFire> fires, GroundPoint hall, float colonyRadius)
        {
            int best = -1;
            for (int i = 0; i < fires.Count; i++)
            {
                GatheringFire f = fires[i];
                // A hut's own hearth isn't where the whole colony gathers (1.4.1: in the Longhouse the places round its fire
                // pit fell among the walls and beds, and nobody sat).
                if (!f.Burning || f.InHut || ColonyRules.Distance(f.At, hall) > colonyRadius) continue;
                if (best < 0 || Better(f, fires[best], hall)) best = i;
            }
            return best;
        }

        private static bool Better(GatheringFire a, GatheringFire b, GroundPoint hall)
        {
            if (a.AtHall != b.AtHall) return a.AtHall;
            if (a.Roofed != b.Roofed) return a.Roofed;
            return ColonyRules.Distance(a.At, hall) < ColonyRules.Distance(b.At, hall);
        }

        /// <summary>The first ring round a fire this many metres across: clear of a bonfire's edge (huts round 2).</summary>
        public static float InnerFor(float fireRadius) => Math.Max(RingInner, fireRadius + 0.8f);

        /// <summary>How many fit on ring <paramref name="ring"/> (0 = the inner one) a metre apart.</summary>
        public static int RingCapacity(int ring, float inner = RingInner)
        {
            double radius = inner + ring * RingStep;
            return Math.Max(1, (int)Math.Floor(Math.PI / Math.Asin(Spacing / 2.0 / radius)));
        }

        /// <summary>Spot <paramref name="index"/> of <paramref name="count"/> round <paramref name="centre"/>: inner rings first, evenly spread.</summary>
        public static GroundPoint Ring(GroundPoint centre, int index, int count, float inner = RingInner)
        {
            int ring = 0, first = 0;
            while (index >= first + RingCapacity(ring, inner)) first += RingCapacity(ring++, inner);
            int onRing = Math.Min(RingCapacity(ring, inner), Math.Max(1, count - first));
            double radius = inner + ring * RingStep;
            double angle = 2 * Math.PI * (index - first) / onRing + ring * 0.5;   // each ring turned a little: not in rows
            return new GroundPoint(centre.X + (float)(Math.Cos(angle) * radius), centre.Z + (float)(Math.Sin(angle) * radius));
        }

        /// <summary>His place in the ring: his id's order among the settlers' ids (-1 when he isn't among them).</summary>
        public static int Index(string id, IEnumerable<string> ids)
        {
            List<string> sorted = ids.OrderBy(s => s, StringComparer.Ordinal).ToList();
            return sorted.IndexOf(id);
        }
    }

    /// <summary>Keeping the colony's fires fed (0.15).</summary>
    public static class FireFuel
    {
        /// <summary>This many of a fuel stay in the colony's chests for the player.</summary>
        public const int Keep = 10;

        /// <summary>
        /// How much fuel to bring: enough to take the fire past half full, never more than it holds, and never the last
        /// <paramref name="keep"/> in the chests. 0 when it is half full or more.
        /// </summary>
        public static int ToFetch(float fuel, float max, int inChests, int keep = Keep)
        {
            if (max <= 0f || fuel >= max / 2f) return 0;
            int need = (int)Math.Ceiling(max / 2f - fuel) + 1;
            int room = (int)Math.Floor(max - fuel);
            return Math.Max(0, Math.Min(need, Math.Min(room, inChests - keep)));
        }
    }

    /// <summary>A colony chest a settler might eat breakfast at.</summary>
    public struct BreakfastChest
    {
        public BreakfastChest(string id, GroundPoint at, bool townHall, bool hasFood, bool storehouse = false)
        {
            Id = id ?? "";
            At = at;
            TownHall = townHall;
            HasFood = hasFood;
            Storehouse = storehouse;
        }

        /// <summary>The storehouse (1.5): meals come from it first.</summary>
        public bool Storehouse { get; }

        public string Id { get; }
        public GroundPoint At { get; }
        public bool TownHall { get; }
        public bool HasFood { get; }
    }

    /// <summary>
    /// Breakfast at the table (0.15): at the storehouse when it has the food (1.5), else the nearest colony chest with it,
    /// the Town Hall's when as near.
    /// </summary>
    public static class Breakfast
    {
        /// <summary>A seat this near the chest is his table.</summary>
        public const float SeatNear = 8f;

        /// <summary>The first minute after his meal is due: each settler goes at his own moment in it (0.18).</summary>
        public const float Spread = 60f;

        /// <summary>How long after his meal is due he sets off (from his id, the same on every computer).</summary>
        public static float Delay(string id)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in id ?? "") hash = (hash ^ c) * 16777619;
                return hash % 600 / 10f;
            }
        }

        public static string Chest(IEnumerable<BreakfastChest> chests, GroundPoint me) =>
            chests.Where(c => c.HasFood)
                  .OrderByDescending(c => c.Storehouse)
                  .ThenBy(c => Math.Round(ColonyRules.Distance(c.At, me), 1))
                  .ThenByDescending(c => c.TownHall)
                  .Select(c => c.Id)
                  .FirstOrDefault() ?? "";
    }
}
