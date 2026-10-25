using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>The space a piece takes in the world: the box around it, sides along the world's axes.</summary>
    public struct PieceBox
    {
        public PieceBox(Vec3 min, Vec3 max)
        {
            Min = min;
            Max = max;
        }

        public Vec3 Min { get; }
        public Vec3 Max { get; }
    }

    /// <summary>
    /// Which old piece an upgrade takes away next: from the top down, never one another old piece still rests on, and
    /// never one a staying piece (kept or newly built) rests on with nothing else under it — until every new piece stands;
    /// then the rest comes down from the top anyway (anything that falls is in the new design and gets rebuilt).
    /// </summary>
    public static class RemovalOrder
    {
        /// <summary>A piece rests on another when its bottom is this close to the other's top…</summary>
        public const float Touch = 0.15f;
        /// <summary>…and they overlap by more than this across both ways.</summary>
        public const float Overlap = 0.1f;

        public static bool RestsOn(PieceBox upper, PieceBox lower) =>
            upper.Min.Y + upper.Max.Y > lower.Min.Y + lower.Max.Y   // its middle is higher: two pieces at one height never hold each other up
            && Math.Abs(upper.Min.Y - lower.Max.Y) <= Touch
            && Math.Min(upper.Max.X, lower.Max.X) - Math.Max(upper.Min.X, lower.Min.X) > Overlap
            && Math.Min(upper.Max.Z, lower.Max.Z) - Math.Max(upper.Min.Z, lower.Min.Z) > Overlap;

        /// <param name="old">Old pieces still standing, by key.</param>
        /// <param name="staying">Pieces that stay: kept ones and new ones already built.</param>
        /// <param name="onGround">Whether a piece stands on the ground (it needs nothing under it).</param>
        /// <param name="allBuilt">Every new piece is up (or skipped): nothing more will come to hold things up.</param>
        /// <returns>The key of the old piece to take away next, or -1 to wait.</returns>
        public static int Next(IReadOnlyList<KeyValuePair<int, PieceBox>> old, IReadOnlyList<PieceBox> staying, Func<PieceBox, bool> onGround, bool allBuilt)
        {
            int best = -1, fallback = -1;
            float bestTop = float.MinValue, fallbackTop = float.MinValue;
            for (int c = 0; c < old.Count; c++)
            {
                PieceBox candidate = old[c].Value;
                bool oldOnTop = false;
                for (int o = 0; o < old.Count && !oldOnTop; o++) oldOnTop = o != c && RestsOn(old[o].Value, candidate);
                if (oldOnTop) continue;   // what is above it comes down first
                float top = candidate.Max.Y;
                if (top > fallbackTop)
                {
                    fallback = old[c].Key;
                    fallbackTop = top;
                }
                if (HoldsUp(candidate, staying, onGround)) continue;
                if (top > bestTop)
                {
                    best = old[c].Key;
                    bestTop = top;
                }
            }
            return best >= 0 ? best : allBuilt ? fallback : -1;
        }

        /// <summary>How far an old piece reaches into a new door's box to stand in its doorway (a floor under it only touches it).</summary>
        public const float DoorwayInset = 0.15f;

        /// <summary>
        /// An old piece stands in the doorway of one of the new design's doors: it reaches well into the door's height and
        /// across, not just under it (the old-huts upgrade test, 2026-10-19).
        /// </summary>
        public static bool InDoorway(PieceBox old, IReadOnlyList<PieceBox> newDoors)
        {
            foreach (PieceBox door in newDoors)
            {
                float x = Math.Min(old.Max.X, door.Max.X) - Math.Max(old.Min.X, door.Min.X);
                float y = Math.Min(old.Max.Y, door.Max.Y) - Math.Max(old.Min.Y, door.Min.Y);
                float z = Math.Min(old.Max.Z, door.Max.Z) - Math.Max(old.Min.Z, door.Min.Z);
                if (y > DoorwayInset && x > 0f && z > 0f && (x > DoorwayInset || z > DoorwayInset)) return true;
            }
            return false;
        }

        /// <summary>
        /// In an upgrade the doorways come first (the old-huts upgrade test, 2026-10-19: an old door led into the new
        /// design's wall 129 times, and a builder was walled in before the new door's spot was cleared): of the old pieces
        /// with keys in <paramref name="doorway"/> (old doors and gates, and old pieces in a new door's doorway) and what
        /// rests on them, the one to take down next, by <see cref="Next"/>'s rule; -1 when none (or none may come down yet).
        /// </summary>
        public static int NextDoorway(IReadOnlyList<KeyValuePair<int, PieceBox>> old, ICollection<int> doorway,
            IReadOnlyList<PieceBox> staying, Func<PieceBox, bool> onGround)
        {
            var members = new HashSet<int>();
            for (int i = 0; i < old.Count; i++) if (doorway.Contains(old[i].Key)) members.Add(i);
            if (members.Count == 0) return -1;
            for (bool grew = true; grew;)   // what rests on them comes down first: they're in it too
            {
                grew = false;
                for (int o = 0; o < old.Count; o++)
                {
                    if (members.Contains(o)) continue;
                    foreach (int m in members)
                    {
                        if (!RestsOn(old[o].Value, old[m].Value)) continue;
                        members.Add(o);
                        grew = true;
                        break;
                    }
                    if (grew) break;
                }
            }
            var subset = new List<KeyValuePair<int, PieceBox>>();
            foreach (int m in members) subset.Add(old[m]);
            return Next(subset, staying, onGround, allBuilt: false);
        }

        /// <summary>
        /// The old piece to take down so the one with <paramref name="key"/> (in the way of a new piece) can come down
        /// without old pieces falling: the top of what rests on it, climbing up; itself when nothing old rests on it.
        /// </summary>
        public static int TopOf(IReadOnlyList<KeyValuePair<int, PieceBox>> old, int key)
        {
            int current = -1;
            for (int i = 0; i < old.Count && current < 0; i++) if (old[i].Key == key) current = i;
            if (current < 0) return key;
            var seen = new HashSet<int> { current };
            while (true)
            {
                int above = -1;
                float aboveTop = float.MinValue;
                for (int o = 0; o < old.Count; o++)
                {
                    if (seen.Contains(o) || !RestsOn(old[o].Value, old[current].Value) || old[o].Value.Max.Y <= aboveTop) continue;
                    above = o;
                    aboveTop = old[o].Value.Max.Y;
                }
                if (above < 0) return old[current].Key;
                seen.Add(above);
                current = above;
            }
        }

        /// <summary>A staying piece rests on this one and on nothing else that stays, and not on the ground.</summary>
        private static bool HoldsUp(PieceBox candidate, IReadOnlyList<PieceBox> staying, Func<PieceBox, bool> onGround) =>
            HeldUpBy(candidate, staying, onGround).Count > 0;

        /// <summary>
        /// The staying pieces (their indices) that rest on this old one and on nothing else that stays, nor the ground: they
        /// would fall if it came down (1.4.1, A3: an upgrade took an old ridge down to put the new one in its place, and the
        /// kept ornament resting on it alone fell with it).
        /// </summary>
        public static List<int> HeldUpBy(PieceBox old, IReadOnlyList<PieceBox> staying, Func<PieceBox, bool> onGround)
        {
            var held = new List<int>();
            for (int s = 0; s < staying.Count; s++)
            {
                if (!RestsOn(staying[s], old) || onGround(staying[s])) continue;
                bool other = false;
                for (int t = 0; t < staying.Count && !other; t++) other = t != s && RestsOn(staying[s], staying[t]);
                if (!other) held.Add(s);
            }
            return held;
        }
    }

    /// <summary>
    /// Kept pieces of an upgrade's plan the builder took down (each rested on an old piece alone), waiting to go back on the
    /// order's list: once the old piece they rested on is down too, never before, or they'd only go up on it again (the 1.5.0
    /// build-through: an order finished with two such poles on the ground; its fix's review: reopened at once, one lower than
    /// the new piece could go straight back up on the old one, again and again).
    /// </summary>
    public sealed class KeptDown
    {
        private readonly List<KeyValuePair<int, int>> _waiting = new List<KeyValuePair<int, int>>();

        /// <param name="oldIndex">The upgrade's old piece it rested on, or -1 (nothing to wait for).</param>
        public void Add(int keptIndex, int oldIndex)
        {
            if (keptIndex >= 0) _waiting.Add(new KeyValuePair<int, int>(keptIndex, oldIndex));
        }

        /// <summary>The kept pieces to put back on the list now (each once): those whose old piece is down.</summary>
        public List<int> Due(Func<int, bool> oldTakenAway)
        {
            var due = new List<int>();
            for (int i = _waiting.Count - 1; i >= 0; i--)
            {
                KeyValuePair<int, int> kept = _waiting[i];
                if (kept.Value >= 0 && !oldTakenAway(kept.Value)) continue;
                due.Insert(0, kept.Key);
                _waiting.RemoveAt(i);
            }
            return due;
        }

        /// <summary>He left the site: the site's own look for missing pieces finds them.</summary>
        public void Clear() => _waiting.Clear();
    }
}
