using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>When the builder's piece goes up if his hammer swing never reports the moment it lands.</summary>
    public static class BuildSwing
    {
        /// <summary>A swing that never seemed to start (no attack showed) places the piece after this long.</summary>
        public const float GiveUpSeconds = 3f;

        /// <summary>
        /// The piece normally appears when the hammer lands. If the swing ended without that moment, or no swing showed
        /// for a while, it appears anyway — the builder must never swing forever without building.
        /// </summary>
        public static bool PlaceAfterSwing(bool wasInAttack, bool inAttack, bool placed, float secondsSinceSwing) =>
            !placed && ((wasInAttack && !inAttack) || (!wasInAttack && !inAttack && secondsSinceSwing >= GiveUpSeconds));

        /// <summary>
        /// The game slides a character along with its attack animation; a builder's swings mustn't (hammer, axe, pickaxe
        /// or hoe), or he creeps towards what he works on and keeps walking back. Players and other settlers are left alone.
        /// </summary>
        public static bool StaysPutWhileSwinging(bool isSettler, bool isBuilder) => isSettler && isBuilder;

        /// <summary>
        /// A builder's swings are for show: the game's own hit is skipped, and his job changes the piece, tree, rock or
        /// ground itself the moment the tool lands — so a swing never hits a wall, digs a hole or hurts a passer-by.
        /// </summary>
        public static bool SkipsGameHit(bool isSettler, bool isBuilder) => isSettler && isBuilder;
    }

    /// <summary>Waits a while before something is tried again (a stuck or idle builder looking around).</summary>
    public sealed class Cooldown
    {
        private readonly double _seconds;
        private double _started = double.NaN;

        public Cooldown(double seconds) { _seconds = seconds; }

        /// <summary>Ready when never started, cleared, the time is up, or the clock went backwards.</summary>
        public bool Ready(double now) => double.IsNaN(_started) || now < _started || now - _started >= _seconds;

        public void Start(double now) => _started = now;

        public void Clear() => _started = double.NaN;
    }

    /// <summary>Reads a saved text once per change instead of on every look.</summary>
    public sealed class TextCache<T>
    {
        private readonly Func<string, T> _decode;
        private string _text;
        private T _value;
        private bool _have;

        public TextCache(Func<string, T> decode) { _decode = decode; }

        public T Get(string text)
        {
            text = text ?? "";
            if (_have && (ReferenceEquals(text, _text) || string.Equals(text, _text, StringComparison.Ordinal))) return _value;
            _text = text;
            _value = _decode(text);
            _have = true;
            return _value;
        }
    }

    public static class BuildStance
    {
        /// <summary>
        /// Whether the builder can work from where he stands: outside the building and within reach of the piece
        /// (measured from 1 m above his feet). Each hammer swing slides him a little; while this holds he doesn't walk
        /// back to his spot, which made him step and twitch after every swing.
        /// </summary>
        public static bool InPlace(Vec3 settler, Vec3 target, float reach, bool insideFootprint) =>
            !insideFootprint && Vec3.Distance(settler + new Vec3(0f, 1f, 0f), target) <= reach;

        /// <summary>How far below the ground a piece's top may be and still count as standing on it (floors sit a little in).</summary>
        public const float BuriedTolerance = 0.15f;

        /// <summary>
        /// A piece whose top would be well below the ground is left out: the game breaks a buried piece at once (it has
        /// nothing to stand on). Happens when a design built on a slope is put up on flatter or higher ground.
        /// </summary>
        public static bool Buried(float top, float ground) => top < ground - BuriedTolerance;

        /// <summary>How close the builder stands to the piece he works on: within his reach, but never more than 8 m.</summary>
        public static float WorkDistance(float reach) => Math.Min(8f, reach);
    }

    /// <summary>Places remembered for a while (where the builder's pieces fell, so he only picks up their wood).</summary>
    public sealed class RecentSpots
    {
        private readonly double _maxAge;
        private readonly List<KeyValuePair<Vec3, double>> _spots = new List<KeyValuePair<Vec3, double>>();

        public RecentSpots(double maxAgeSeconds) { _maxAge = maxAgeSeconds; }

        public void Add(Vec3 point, double now)
        {
            _spots.RemoveAll(s => now - s.Value > _maxAge || now < s.Value);
            _spots.Add(new KeyValuePair<Vec3, double>(point, now));
        }

        public bool Near(Vec3 point, float radius, double now) =>
            _spots.Any(s => now - s.Value <= _maxAge && now >= s.Value && Vec3.Distance(s.Key.Flat, point.Flat) <= radius);
    }

    /// <summary>What becomes of a rock the site's scan reaches (SiteRules.Rock).</summary>
    public enum RockOnSite
    {
        /// <summary>The builder breaks it.</summary>
        Clear,
        /// <summary>Another building rests on it, where the building itself goes: the site can't go up there.</summary>
        Blocks,
        /// <summary>Another building rests on it, in the site's margin only: left where it is.</summary>
        LeftAlone,
    }

    public static class SiteRules
    {
        /// <summary>
        /// In front of a door (its landing, outside the site) the builder clears what grows or lies there naturally, so
        /// nothing blocks the way in; dropped items and pieces there are someone's and stay.
        /// </summary>
        public static bool ClearsInFrontOfDoor(ClearKind kind) => kind != ClearKind.Item && kind != ClearKind.Leftover;

        /// <summary>
        /// Whether a piece standing where the builder wants to put one blocks it. His own hut's chest, pieces he put up
        /// for this hut and pieces that are exactly part of the plan are fine; anything else (a player's own building,
        /// another hut's building) is in the way — he never builds into another building.
        /// </summary>
        public static bool IsInTheWay(bool isThisHutsChest, string pieceSite, string thisSite, bool matchesPlan) =>
            !isThisHutsChest && pieceSite != thisSite && !matchesPlan;

        /// <summary>
        /// Whether a piece near a spot keeps the builder's ground work off it (the ground under another building must stay
        /// as it is): another building's pieces do, as with <see cref="IsInTheWay"/>; wild plants, a settler's sapling and
        /// a loose item don't, they aren't built (1.4.1: mushrooms lying by a site, which the game gives a Piece, left a
        /// corner of it unlevelled, and the Lumberjack's Hut's door torch had nothing to stand on).
        /// </summary>
        public static bool KeepsGroundOff(bool isThisHutsChest, string pieceSite, string thisSite, bool matchesPlan, bool vegetation,
            bool looseItem) =>
            !vegetation && !looseItem && IsInTheWay(isThisHutsChest, pieceSite, thisSite, matchesPlan);

        /// <summary>
        /// An item lying on the ground that nobody placed: an item, not a building's piece, though the game gives some a
        /// Piece (a mushroom: the site check took them for another building, and the builder never carried them off).
        /// One a player placed is theirs, as any piece, and so is one the game marks as placed as a piece (a decoration,
        /// which Infinity Hammer can place with no creator: the review of the fix, 1.4.2).
        /// </summary>
        public static bool IsLooseItem(bool isItem, bool placedAsPiece, bool placedByPlayer) => isItem && !placedAsPiece && !placedByPlayer;

        /// <summary>
        /// A piece in the way that the builder may take down himself: part of a colony building whose hut is gone
        /// (a leftover). A player's own pieces and a standing hut's building are never his to remove.
        /// </summary>
        public static bool IsLeftover(string pieceSite, string thisSite, bool siteHutExists) =>
            !string.IsNullOrEmpty(pieceSite) && pieceSite != thisSite && !siteHutExists;

        /// <summary>
        /// Whether a collider found on a site counts for what it belongs to. A building piece counts by its solid parts
        /// only: the trigger spheres around fires and crafting stations (warmth, the player's base) reach far beyond them.
        /// </summary>
        public static bool Counts(bool isTrigger, bool isPiece) => !(isTrigger && isPiece);

        /// <summary>A rock nothing breaks counts from this wide (metres, its widest across)...</summary>
        public const float UnbreakableAcross = 1f;
        /// <summary>...and this tall over the ground: a pebble or a slab in the ground doesn't.</summary>
        public const float UnbreakableTall = 0.5f;

        /// <summary>
        /// A solid thing on a site that the scan knows as nothing to clear (no building piece, tree, rock to mine, bush or
        /// item) and nothing breaks: in the way for good when it's a body, still, and of real size (the release build's
        /// hands-on run, 2026-10-19: the Town Hall's camp was built into a big meadow boulder the scan read as clear).
        /// </summary>
        public static bool Unbreakable(bool isTrigger, bool moves, float across, float aboveGround) =>
            !isTrigger && !moves && across >= UnbreakableAcross && aboveGround >= UnbreakableTall;

        /// <summary>
        /// A rock the site's scan reaches (a rock in one piece, or a big rock's part; any of it on the site: the release
        /// build's hands-on run, 2026-10-19, built the Town Hall's camp into a big boulder whose middle stood off its lot).
        /// It is broken up, unless a building rests on any of it: the game grounds a piece on any solid body that isn't a
        /// building piece within 0.15 m, and a rock in one piece breaks whole (the fix pass's review of 1.4.1); a big rock is
        /// judged by all its parts (the whole-mod review, I2). <paramref name="holdsAnother"/>: a piece of another building
        /// (a player's, another hut's) rests on it: where the building itself goes, it blocks the site; in the margin it is
        /// left alone. <paramref name="holdsOwn"/>: the site's own standing pieces rest on it (a hut built into a boulder
        /// under 1.4.0): left as it is (the whole-mod review, M6).
        /// </summary>
        public static RockOnSite Rock(bool holdsAnother, bool holdsOwn, bool inTheBuilding) =>
            holdsAnother && inTheBuilding ? RockOnSite.Blocks
            : holdsAnother || holdsOwn ? RockOnSite.LeftAlone
            : RockOnSite.Clear;

        /// <summary>
        /// Whether the builder may carry a dropped item off his site. He carries items as plain counts, so only plain
        /// ones: stackable, first quality, undamaged and made by no one. A player's own gear stays where it lies.
        /// </summary>
        public static bool CanCarryOff(int maxStack, int quality, bool worn, bool crafted) =>
            maxStack > 1 && quality <= 1 && !worn && !crafted;
    }

    public static class SitePrep
    {
        /// <summary>
        /// Whether site work holds up the building. While the site is cleared everything waits for it (a tool he lacks,
        /// something he can't reach); once building has started only what he is clearing right now pauses it — what he
        /// can't clear is named in the hut window but doesn't stop the building.
        /// </summary>
        public static bool HoldsUpBuilding(BuildState state, bool busy, bool waiting) =>
            busy || (waiting && state != BuildState.Building);
    }

    public static class BlueprintNames
    {
        /// <summary>
        /// Why a design saved around a <paramref name="chestTypeId"/> chest can't be saved as <paramref name="name"/>, or
        /// null when it can. A file named after another hut would be refused for that hut (it marks the wrong chest) and
        /// would replace the player's working design for it.
        /// </summary>
        public static string SaveProblem(string name, string chestTypeId, IEnumerable<string> hutTypeIds)
        {
            if (!IsPlainName(name))
                return $"a design's name may hold only letters, digits, _ and - ('{name}'): nothing was saved.";
            if (name == chestTypeId || !hutTypeIds.Contains(name)) return null;
            return $"this was built around a {chestTypeId} chest, so it can't be the {name} design (that needs a {name} chest). " +
                   $"Nothing was saved; use the name {chestTypeId} or another name.";
        }

        /// <summary>
        /// Why a design with <paramref name="buildingPieces"/> pieces besides the chest can't be saved, or null when it can.
        /// An empty design would make every later hut of that kind count as built with no building.
        /// </summary>
        public static string EmptyProblem(int buildingPieces, float radius) =>
            buildingPieces > 0 ? null : $"nothing is built within {radius:0} m of the chest, so nothing was saved.";

        /// <summary>
        /// A name that is a plain file name: letters, digits, _ and - only (1.4.2, the hub's audit of the public code: a
        /// typed "../../x" would have been written outside the designs' folder).
        /// </summary>
        public static bool IsPlainName(string name) =>
            !string.IsNullOrEmpty(name) && name.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-');

        /// <summary>The design file for a hut's level: the hut's name for level 1, "&lt;hut&gt;_&lt;level&gt;" for levels 2–5.</summary>
        public static string FileName(string typeId, int level)
        {
            level = HutLevels.Clamp(level);
            return level <= 1 ? typeId : typeId + "_" + level;
        }
    }
}
