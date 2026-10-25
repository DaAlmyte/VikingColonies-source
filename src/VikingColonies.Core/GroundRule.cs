using System;

namespace VikingColonies.Core
{
    /// <summary>Why a hut can't be placed where it is (a red outline). When there are several, the first in this list is shown.</summary>
    public enum RedReason
    {
        None = 0,
        Water,
        NoBuild,
        Ward,
        OutsideColony,
        NotYourColony,   // the player placing it may not manage this colony
        OtherBuilding,
        OtherSite,
        Grave,         // a player's grave or a crate from a ship: it goes once it's emptied (1.4.1)
        Unbreakable,   // a rock nothing breaks (a big meadow boulder) stands where the building goes
        TooSteep,
        TooHigh,
        CantFloat,
        OnlyOneStorehouse,   // the storehouse (1.5): one per colony
        NeedsBuilderHut,     // the storehouse (1.5): a Builder's Hut builds it
    }

    /// <summary>
    /// The level the builder flattens a site to. F is the floor (the chest's height), S how far the design reaches
    /// below it, G the natural ground at the chest. A design with posts under its floor (S over half a metre) gets its
    /// ground 30 cm under the floor, or keeps the natural ground when raised, and its posts carry the floor (when they
    /// would only just touch it, the ground is raised a little so their feet are 15 cm in). A design without posts gets
    /// its ground at the floors' underside, so they rest on it.
    /// </summary>
    public static class GroundRule
    {
        public const float PostsBelow = 0.5f;
        public const float UnderFloor = 0.3f;
        /// <summary>Ground this close to its target counts as flat.</summary>
        public const float Tolerance = 0.1f;
        public const float MaxVariance = 4f;
        /// <summary>Ground up to this much too high is levelled with the hoe, as the game's level ground does; deeper needs a pickaxe.</summary>
        public const float HoeDigDepth = 0.5f;
        /// <summary>How far the posts' feet go into the ground at least, so a raised hut's posts don't only just touch it.</summary>
        public const float PostsInGround = 0.15f;
        /// <summary>An upgrade raises the ground under the hut this much at most (as far as a hut can be raised when placed).</summary>
        public const float MaxUpgradeRaise = 2f;

        public static bool HasPosts(float reachBelow) => reachBelow > PostsBelow;

        public static float Target(float floor, float reachBelow, float natural) =>
            HasPosts(reachBelow)
                ? Math.Max(Math.Min(natural, floor - UnderFloor), floor - reachBelow + PostsInGround)
                : floor - Math.Max(0f, reachBelow);

        /// <summary>
        /// The level an upgrade flattens to: the ground the hut already has (<paramref name="current"/>) when the new
        /// design stands on it — its posts' feet 15 cm in, or its floors on or in the ground, and the ground not above
        /// the floor — so nothing is dug away under the old hut while it stands; else <see cref="Target"/> (which then
        /// raises the ground).
        /// </summary>
        public static float ForUpgrade(float floor, float reachBelow, float current, float natural)
        {
            float lowest = HasPosts(reachBelow) ? floor - reachBelow + PostsInGround : floor - Math.Max(0f, reachBelow) - Tolerance;
            return current >= lowest && current <= floor ? current : Target(floor, reachBelow, natural);
        }

        /// <summary>TooHigh when the posts wouldn't reach the natural ground; CantFloat when a design without posts is raised.</summary>
        public static RedReason Problem(float floor, float reachBelow, float natural)
        {
            if (HasPosts(reachBelow)) return floor - natural > reachBelow + 0.001f ? RedReason.TooHigh : RedReason.None;
            return natural < Target(floor, reachBelow, natural) - Tolerance ? RedReason.CantFloat : RedReason.None;
        }

        /// <summary>
        /// The site check's view of the ground: the level it is flattened to, and why the hut can't stand there. A new hut
        /// gets <see cref="Target"/> and <see cref="Problem"/>; an upgrade gets the level the upgrade levels to
        /// (<see cref="ForUpgrade"/>), which raises the ground where the new design would float or its posts not reach:
        /// only a raise of more than <see cref="MaxUpgradeRaise"/> refuses it.
        /// </summary>
        public static RedReason Judge(float floor, float reachBelow, float natural, bool upgrade, out float target)
        {
            if (upgrade)
            {
                target = ForUpgrade(floor, reachBelow, natural, natural);
                return target - natural > MaxUpgradeRaise ? RedReason.TooHigh : RedReason.None;
            }
            target = Target(floor, reachBelow, natural);
            return Problem(floor, reachBelow, natural);
        }

        /// <summary>Ground varying more than 4 m within a site is too steep to flatten.</summary>
        public static bool TooSteep(float lowest, float highest) => highest - lowest > MaxVariance;
    }

    /// <summary>How far the player has raised (+) or lowered (−) the hut being placed: 25 cm steps, up to 2 m either way.</summary>
    public sealed class PlacementOffset
    {
        public const float Step = 0.25f;
        public const float Limit = 2f;
        private string _piece = "";

        public float Value { get; private set; }

        public void Raise() => Value = Clamp(Value + Step);

        public void Lower() => Value = Clamp(Value - Step);

        /// <summary>Back to the piece's start when another piece is selected (<see cref="StartFor"/>).</summary>
        public void Select(string piece)
        {
            piece = piece ?? "";
            if (piece == _piece) return;
            _piece = piece;
            Value = StartFor(piece);
        }

        /// <summary>
        /// Where a piece's ghost starts: the Storehouse 0.75 m up, so it stands on its posts as placed (its hand-over, 1.5:
        /// at 0 it stood 30 cm up); every other piece on the ground.
        /// </summary>
        public static float StartFor(string piece) => piece == SaveKeys.StorehousePiece ? 0.75f : 0f;

        private static float Clamp(float value) =>
            (float)(Math.Round(Math.Max(-Limit, Math.Min(Limit, value)) / Step) * Step);
    }

    /// <summary>
    /// Lifts the placement ghost by the offset exactly once, however often the game updates it in a frame: when the
    /// game left the ghost where we last put it, the lift is measured from the spot the game chose, not added again.
    /// </summary>
    public sealed class GhostLift
    {
        private Vec3 _base;
        private Vec3 _lifted;
        private bool _have;

        public Vec3 Lift(Vec3 position, float offset)
        {
            Vec3 from = _have && Vec3.Distance(position, _lifted) < 0.0005f ? _base : position;
            _base = from;
            _lifted = new Vec3(from.X, from.Y + offset, from.Z);
            _have = true;
            return _lifted;
        }

        public void Forget() => _have = false;
    }
}
