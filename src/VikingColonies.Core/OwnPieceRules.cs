using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>What happens to one of the player's own pieces in a hut's lot when the hut is upgraded (1.6).</summary>
    public enum OwnPieceFate
    {
        /// <summary>Nothing the upgrade does touches it.</summary>
        Stays,
        /// <summary>A piece the upgrade builds goes where it stands: the upgrade waits for the player to move it.</summary>
        InTheWay,
        /// <summary>It rests on or hangs from a part the upgrade replaces: the builder takes it down and puts it back after.</summary>
        PutBack,
        /// <summary>It would have to come down, and the builder doesn't take it down: the upgrade waits (<see cref="OwnPieceWait"/>).</summary>
        Waits,
    }

    /// <summary>Why the upgrade waits for a piece the builder won't take down.</summary>
    public enum OwnPieceWait { None, HoldsThings, PlayersBed, NothingHoldsIt }

    /// <summary>What the window says about a piece the upgrade waits for (the design's point 6).</summary>
    public enum OwnPieceWhy
    {
        None,
        /// <summary>A new piece goes where it stands: move it.</summary>
        InTheWay,
        /// <summary>A chest with something in it: empty it first.</summary>
        Full,
        /// <summary>An item or armour stand with something on it: take it off first.</summary>
        HasItem,
        PlayersBed,
        /// <summary>Nothing of the new level would hold it there: move it.</summary>
        NothingHoldsIt,
        /// <summary>It keeps something a put-back can't carry (a ward's list, a map table's map): move it.</summary>
        KeepsWhat,
    }

    /// <summary>One of the player's pieces as the rule sees it.</summary>
    public sealed class OwnPiece
    {
        public OwnPiece(PieceBox box, bool onGround, bool holdsThings, bool playersBed)
        {
            Box = box;
            OnGround = onGround;
            HoldsThings = holdsThings;
            PlayersBed = playersBed;
        }

        public PieceBox Box { get; }
        /// <summary>It stands on the ground: it needs nothing under it.</summary>
        public bool OnGround { get; }
        /// <summary>A chest that isn't empty, an item or armour stand with something on it: what it holds would spill.</summary>
        public bool HoldsThings { get; }
        /// <summary>A bed a player sleeps in (their spawn point).</summary>
        public bool PlayersBed { get; }
    }

    public struct OwnPieceVerdict
    {
        public OwnPieceVerdict(OwnPieceFate fate, OwnPieceWait wait, int which)
        {
            Fate = fate;
            Wait = wait;
            Which = which;
        }

        public OwnPieceFate Fate { get; }
        public OwnPieceWait Wait { get; }
        /// <summary>
        /// In the way: the index of the built piece. Put back: the first piece taken away it touches (the taken-away list,
        /// then the kept pieces taken down for a while after it); -1 when that is another of the player's pieces.
        /// </summary>
        public int Which { get; }
    }

    /// <summary>
    /// The player's own pieces through a hut's upgrade (1.6): each stays where the player put it, or the builder takes it
    /// down and puts it back there; the upgrade waits only for one in a new piece's spot, one holding things, a player's
    /// bed, or one nothing of the new level would hold
    /// (docs/superpowers/specs/2026-10-25-vikingcolonies-own-pieces-through-upgrades-design.md).
    /// </summary>
    public static class OwnPieceRules
    {
        /// <summary>
        /// Two pieces touch when one rests on the other, hangs from its side or under it, or runs into it: their boxes are
        /// within <see cref="RemovalOrder.Touch"/> of each other every way and overlap by more than
        /// <see cref="RemovalOrder.Overlap"/> at least two ways.
        /// </summary>
        public static bool Touches(PieceBox a, PieceBox b)
        {
            float x = Math.Min(a.Max.X, b.Max.X) - Math.Max(a.Min.X, b.Min.X);
            float y = Math.Min(a.Max.Y, b.Max.Y) - Math.Max(a.Min.Y, b.Min.Y);
            float z = Math.Min(a.Max.Z, b.Max.Z) - Math.Max(a.Min.Z, b.Min.Z);
            if (x < -RemovalOrder.Touch || y < -RemovalOrder.Touch || z < -RemovalOrder.Touch) return false;
            int across = (x > RemovalOrder.Overlap ? 1 : 0) + (y > RemovalOrder.Overlap ? 1 : 0) + (z > RemovalOrder.Overlap ? 1 : 0);
            return across >= 2;
        }

        /// <param name="own">The player's pieces in the hut's lot.</param>
        /// <param name="built">The pieces the upgrade builds (not the kept ones).</param>
        /// <param name="kept">What stands throughout: the kept pieces, the hut's chest, other pieces that stay.</param>
        /// <param name="keptForAWhile">Kept pieces the builder takes down for a while (each rests on an old piece alone) and puts up again.</param>
        /// <param name="takenAway">The old pieces the upgrade takes away, and the leftovers it clears.</param>
        public static List<OwnPieceVerdict> Fates(IReadOnlyList<OwnPiece> own, IReadOnlyList<PieceBox> built, IReadOnlyList<PieceBox> kept,
            IReadOnlyList<PieceBox> keptForAWhile, IReadOnlyList<PieceBox> takenAway)
        {
            var removed = new List<PieceBox>(takenAway);
            removed.AddRange(keptForAWhile);
            var newLevel = new List<PieceBox>(built);
            newLevel.AddRange(kept);
            newLevel.AddRange(keptForAWhile);

            var verdicts = new List<OwnPieceVerdict>();
            var stays = new bool[own.Count];
            for (int i = 0; i < own.Count; i++)
            {
                int way = RunsInto(own[i].Box, built);
                verdicts.Add(way >= 0 ? new OwnPieceVerdict(OwnPieceFate.InTheWay, OwnPieceWait.None, way) : new OwnPieceVerdict(OwnPieceFate.Stays, OwnPieceWait.None, -1));
                stays[i] = way < 0;
            }
            // What the player's other pieces do matters (a lantern on their table follows the table): a piece only ever
            // stops staying, so this settles within one round a piece.
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int i = 0; i < own.Count; i++)
                {
                    if (verdicts[i].Fate == OwnPieceFate.InTheWay) continue;
                    OwnPieceVerdict now = Judge(i, own, stays, kept, removed, newLevel);
                    if (now.Fate == verdicts[i].Fate && now.Wait == verdicts[i].Wait && now.Which == verdicts[i].Which) continue;
                    verdicts[i] = now;
                    if (stays[i] && now.Fate != OwnPieceFate.Stays)
                    {
                        stays[i] = false;
                        changed = true;
                    }
                }
            }
            return verdicts;
        }

        private static OwnPieceVerdict Judge(int i, IReadOnlyList<OwnPiece> own, bool[] stays, IReadOnlyList<PieceBox> kept,
            List<PieceBox> removed, List<PieceBox> newLevel)
        {
            OwnPiece piece = own[i];
            // Held from below by what stays wins over touching: the game holds a chest by the floor it stands on.
            if (piece.OnGround || RestsOnAny(piece.Box, kept) || RestsOnOwn(i, own, stays))
                return new OwnPieceVerdict(OwnPieceFate.Stays, OwnPieceWait.None, -1);
            int which = -1;
            bool touches = false;
            for (int r = 0; r < removed.Count && !touches; r++)
                if (Touches(piece.Box, removed[r]))
                {
                    touches = true;
                    which = r;
                }
            for (int j = 0; j < own.Count && !touches; j++)
                touches = j != i && !stays[j] && Touches(piece.Box, own[j].Box);   // another of theirs that moves
            if (!touches) return new OwnPieceVerdict(OwnPieceFate.Stays, OwnPieceWait.None, -1);
            if (piece.PlayersBed) return new OwnPieceVerdict(OwnPieceFate.Waits, OwnPieceWait.PlayersBed, -1);
            if (piece.HoldsThings) return new OwnPieceVerdict(OwnPieceFate.Waits, OwnPieceWait.HoldsThings, -1);
            bool held = false;
            foreach (PieceBox holder in newLevel) held |= Touches(piece.Box, holder);
            for (int j = 0; j < own.Count && !held; j++) held = j != i && stays[j] && Touches(piece.Box, own[j].Box);
            return held ? new OwnPieceVerdict(OwnPieceFate.PutBack, OwnPieceWait.None, which)
                : new OwnPieceVerdict(OwnPieceFate.Waits, OwnPieceWait.NothingHoldsIt, -1);
        }

        private static bool RestsOnAny(PieceBox box, IReadOnlyList<PieceBox> under)
        {
            foreach (PieceBox lower in under)
                if (RemovalOrder.RestsOn(box, lower)) return true;
            return false;
        }

        private static bool RestsOnOwn(int i, IReadOnlyList<OwnPiece> own, bool[] stays)
        {
            for (int j = 0; j < own.Count; j++)
                if (j != i && stays[j] && RemovalOrder.RestsOn(own[i].Box, own[j].Box)) return true;
            return false;
        }

        /// <summary>The first built piece that runs through this one (<see cref="StorehouseRules.InTheWayOf"/>), or -1.</summary>
        private static int RunsInto(PieceBox box, IReadOnlyList<PieceBox> built) => StorehouseRules.InTheWayOf(box, built);

        /// <summary>
        /// The kept pieces (their indices) the builder takes down for a while: each rests on a taken-away piece and on
        /// nothing else that stays (<see cref="RemovalOrder.HeldUpBy"/>, the KeptDown case).
        /// </summary>
        public static List<int> KeptForAWhile(IReadOnlyList<PieceBox> kept, IReadOnlyList<PieceBox> takenAway, Func<PieceBox, bool> onGround)
        {
            var forAWhile = new SortedSet<int>();
            foreach (PieceBox old in takenAway)
                foreach (int held in RemovalOrder.HeldUpBy(old, kept, onGround)) forAWhile.Add(held);
            return new List<int>(forAWhile);
        }

        /// <summary>The first piece the upgrade waits for (in the way, or one the builder won't take down), or -1.</summary>
        public static int FirstWait(IReadOnlyList<OwnPieceVerdict> verdicts)
        {
            for (int i = 0; i < verdicts.Count; i++)
                if (verdicts[i].Fate == OwnPieceFate.InTheWay || verdicts[i].Fate == OwnPieceFate.Waits) return i;
            return -1;
        }

        /// <summary>How many pieces the builder takes down and puts back.</summary>
        public static int PutBackCount(IReadOnlyList<OwnPieceVerdict> verdicts)
        {
            int count = 0;
            foreach (OwnPieceVerdict verdict in verdicts)
                if (verdict.Fate == OwnPieceFate.PutBack) count++;
            return count;
        }

        /// <summary>What the window says about this piece; <paramref name="stand"/>: an item or armour stand (not a chest).</summary>
        public static OwnPieceWhy WhyOf(OwnPieceVerdict verdict, bool stand) => WhyOf(verdict, stand ? OwnPieceWhy.HasItem : OwnPieceWhy.Full);

        /// <summary>
        /// What the window says about this piece; <paramref name="holds"/>: what it says should the piece hold things (a
        /// chest's Full, a stand's HasItem, KeepsWhat for a ward or a map table; None counts as Full).
        /// </summary>
        public static OwnPieceWhy WhyOf(OwnPieceVerdict verdict, OwnPieceWhy holds)
        {
            if (verdict.Fate == OwnPieceFate.InTheWay) return OwnPieceWhy.InTheWay;
            if (verdict.Fate != OwnPieceFate.Waits) return OwnPieceWhy.None;
            switch (verdict.Wait)
            {
                case OwnPieceWait.HoldsThings: return holds == OwnPieceWhy.HasItem || holds == OwnPieceWhy.KeepsWhat ? holds : OwnPieceWhy.Full;
                case OwnPieceWait.PlayersBed: return OwnPieceWhy.PlayersBed;
                default: return OwnPieceWhy.NothingHoldsIt;
            }
        }

        /// <summary>
        /// A take-down (the design's point 7): every piece of the hut comes down and nothing new goes up. The indices of the
        /// player's pieces that would fall: those resting on or hanging from the building.
        /// </summary>
        public static List<int> ForTakeDown(IReadOnlyList<OwnPiece> own, IReadOnlyList<PieceBox> hutPieces)
        {
            List<OwnPieceVerdict> verdicts = Fates(own, new PieceBox[0], new PieceBox[0], new PieceBox[0], hutPieces);
            var falls = new List<int>();
            for (int i = 0; i < verdicts.Count; i++)
                if (verdicts[i].Fate != OwnPieceFate.Stays) falls.Add(i);
            return falls;
        }

        /// <summary>
        /// One of the player's pieces is held by another of theirs (1.6, the review's finding 3): it rests on it, or hangs
        /// from its side, the smaller from the bigger (a torch on their beam). Two the same side by side hold neither.
        /// </summary>
        public static bool DependsOn(PieceBox a, PieceBox b)
        {
            if (RemovalOrder.RestsOn(a, b)) return true;
            if (RemovalOrder.RestsOn(b, a) || !Touches(a, b)) return false;
            return Volume(a) < Volume(b) * 0.99f;
        }

        private static float Volume(PieceBox box) =>
            Math.Max(0f, box.Max.X - box.Min.X) * Math.Max(0f, box.Max.Y - box.Min.Y) * Math.Max(0f, box.Max.Z - box.Min.Z);

        /// <summary>A put-back still standing comes down before an old piece it touches does, or it would fall with it.</summary>
        public static bool TakeDownFirst(PieceBox putBack, PieceBox old) => Touches(putBack, old);

        /// <summary>
        /// A put-back goes back once no old piece it touched still stands and every piece of the new level it touches does.
        /// </summary>
        /// <param name="oldStanding">Old pieces (and kept ones taken down for a while) of the upgrade still standing.</param>
        /// <param name="newLevel">The new level's pieces (built and kept)…</param>
        /// <param name="newStanding">…and whether each stands now.</param>
        public static bool ReadyToGoBack(PieceBox putBack, IReadOnlyList<PieceBox> oldStanding, IReadOnlyList<PieceBox> newLevel,
            IReadOnlyList<bool> newStanding)
        {
            foreach (PieceBox old in oldStanding)
                if (Touches(putBack, old)) return false;
            for (int i = 0; i < newLevel.Count; i++)
                if (Touches(putBack, newLevel[i]) && !(i < newStanding.Count && newStanding[i])) return false;
            return true;
        }

        /// <summary>
        /// The put-back the builder takes down before an old piece (1.6): one still standing that touches it, and before that
        /// one, any put-back held by it (<see cref="DependsOn"/>: a lantern on the player's shelf, a torch on their beam,
        /// comes off before it). -1 for none.
        /// </summary>
        /// <param name="standing">The put-backs still standing.</param>
        public static int FirstToTakeDown(PieceBox old, IReadOnlyList<PieceBox> standing)
        {
            int first = -1;
            for (int i = 0; i < standing.Count && first < 0; i++)
                if (TakeDownFirst(standing[i], old)) first = i;
            var seen = new HashSet<int>();
            while (first >= 0 && seen.Add(first))
            {
                int above = -1;
                for (int i = 0; i < standing.Count && above < 0; i++)
                    if (!seen.Contains(i) && DependsOn(standing[i], standing[first])) above = i;
                if (above < 0) break;
                first = above;
            }
            return first;
        }

        /// <summary>
        /// The put-back the builder puts back next (1.6): the first one down that is ready (<see cref="ReadyToGoBack"/>) and
        /// held by no other put-back still down (<see cref="DependsOn"/>: the shelf goes back before the lantern on it, the
        /// beam before the torch on it). -1 for none yet.
        /// </summary>
        /// <param name="down">The put-backs taken down, where they go back.</param>
        public static int NextToPutBack(IReadOnlyList<PieceBox> down, IReadOnlyList<PieceBox> oldStanding, IReadOnlyList<PieceBox> newLevel,
            IReadOnlyList<bool> newStanding)
        {
            for (int i = 0; i < down.Count; i++)
            {
                if (!ReadyToGoBack(down[i], oldStanding, newLevel, newStanding)) continue;
                bool onAnother = false;
                for (int j = 0; j < down.Count && !onAnother; j++) onAnother = j != i && DependsOn(down[i], down[j]);
                if (!onAnother) return i;
            }
            return -1;
        }

        /// <summary>
        /// The box a piece takes, measured in the hut's own frame (the design's point 5): along the world's axes a turned
        /// hut's walls swell and "touch" what stands near them. Huts turn about the vertical only.
        /// </summary>
        /// <param name="shapeMin">The piece's shape, in its own frame…</param>
        /// <param name="position">…its place and turn in the world…</param>
        /// <param name="origin">…and the hut's chest and turn.</param>
        public static PieceBox BoxIn(Vec3 shapeMin, Vec3 shapeMax, Vec3 position, Quat rotation, Vec3 origin, Quat turn)
        {
            Quat back = turn.Normalized.Inverse;
            Vec3 at = back.Rotate(position - origin);
            Quat local = back * rotation.Normalized;
            float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vec3((i & 1) == 0 ? shapeMin.X : shapeMax.X, (i & 2) == 0 ? shapeMin.Y : shapeMax.Y, (i & 4) == 0 ? shapeMin.Z : shapeMax.Z);
                Vec3 p = at + local.Rotate(corner);
                x0 = Math.Min(x0, p.X);
                y0 = Math.Min(y0, p.Y);
                z0 = Math.Min(z0, p.Z);
                x1 = Math.Max(x1, p.X);
                y1 = Math.Max(y1, p.Y);
                z1 = Math.Max(z1, p.Z);
            }
            return new PieceBox(new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));
        }
    }
}
