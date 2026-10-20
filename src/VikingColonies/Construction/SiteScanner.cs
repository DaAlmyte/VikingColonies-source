using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Config;
using VikingColonies.Core;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.Construction
{
    /// <summary>
    /// Something on a site the builder clears: a dropped item, a plant, a tree, log or stump, a rock (or one part of a
    /// big rock), or a leftover piece of an old colony building.
    /// </summary>
    internal sealed class SiteThing
    {
        public SiteThing(GameObject root, ClearKind kind, int tier, Collider area, Piece piece)
        {
            Root = root;
            Kind = kind;
            Tier = Math.Max(0, tier);
            Area = area;
            Piece = piece;
            Key = KeyOf(root, area);
        }

        /// <summary>What the builder remembers a thing by (for things he gave up on for a while).</summary>
        public static string KeyOf(GameObject root, Collider area)
        {
            ZNetView nview = root.GetComponent<ZNetView>();
            string id = nview != null && nview.IsValid() ? nview.GetZDO().m_uid.ToString() : root.GetInstanceID().ToString();
            return area != null ? id + "/" + area.GetInstanceID() : id;
        }

        public GameObject Root { get; }
        public ClearKind Kind { get; }
        /// <summary>The tool tier it needs (axes for wood, pickaxes for rocks).</summary>
        public int Tier { get; }
        /// <summary>The part of a big rock on the site; null for everything else.</summary>
        public Collider Area { get; }
        public Piece Piece { get; }
        public string Key { get; }

        public bool IsGone => Root == null || (Area != null && (!Area.enabled || !Area.gameObject.activeInHierarchy));

        public Vector3 Position => Area != null ? Area.bounds.center : Root.transform.position;
    }

    /// <summary>Looks at what stands on a site, and at the reasons a hut can't go there.</summary>
    internal static class SiteScanner
    {
        private static readonly Collider[] s_hits = new Collider[1024];
        private static readonly HashSet<GameObject> s_seen = new HashSet<GameObject>();
        private static readonly HashSet<GameObject> s_unknown = new HashSet<GameObject>();
        private static int s_solidLayers;
        private static readonly List<SiteThing> s_landing = new List<SiteThing>();
        private static readonly List<Piece> s_landingBlockers = new List<Piece>();
        private static bool s_warnedFull;

        /// <param name="bottom">The lowest height to look at (below the lowest ground).</param>
        /// <param name="top">The highest height to look at.</param>
        /// <param name="hut">The hut whose site it is (its chest and the builder's pieces for it never count); null while placing.</param>
        /// <param name="ignore">An object to leave out (the placement ghost).</param>
        /// <param name="matchesPlan">Whether a standing piece is exactly one of the plan's pieces (those never count).</param>
        /// <param name="items">Whether dropped items count (only while clearing a new site).</param>
        /// <param name="clear">Filled with what the builder clears.</param>
        /// <param name="blockers">Filled with pieces in the way that he must never touch (a player's, another hut's).</param>
        /// <param name="unbreakable">When given: what nothing breaks on the site (SiteRules.Unbreakable), a collider each.</param>
        /// <param name="graves">When given: a player's grave or a crate from a ship on the site, a collider each.</param>
        /// <param name="heldRocks">When given: a rock another building rests on, where the building itself goes (it holds the
        /// site, and nothing goes up into it), a collider each.</param>
        public static void Scan(Footprint site, float bottom, float top, ColonyBuilding hut, GameObject ignore, Func<Piece, bool> matchesPlan,
            bool items, List<SiteThing> clear, List<Piece> blockers, List<Collider> unbreakable = null, List<Collider> graves = null,
            List<Collider> heldRocks = null)
        {
            clear.Clear();
            blockers.Clear();
            unbreakable?.Clear();
            graves?.Clear();
            heldRocks?.Clear();
            s_rockHolds.Clear();
            s_seen.Clear();
            s_unknown.Clear();
            var centre = new Vector3(site.Centre.X, (bottom + top) / 2f, site.Centre.Z);
            var half = new Vector3(site.HalfX, Mathf.Max(0.5f, (top - bottom) / 2f), site.HalfZ);
            int count = Physics.OverlapBoxNonAlloc(centre, half, s_hits, U(site.Rotation), ~0, QueryTriggerInteraction.Collide);
            if (count == s_hits.Length && !s_warnedFull)
            {
                s_warnedFull = true;
                Log.Warn($"A building site holds more than {s_hits.Length} colliders; only the first are looked at.");
            }
            string thisSite = hut != null ? hut.Id : "";
            for (int i = 0; i < count; i++)
            {
                Collider hit = s_hits[i];
                if (hit == null || hit.GetComponentInParent<Heightmap>() != null) continue;
                ZNetView nview = hit.GetComponentInParent<ZNetView>();
                if (nview == null || !nview.IsValid())
                {
                    // The placement ghost has no network id; nor have some of the world's rocks and cliffs.
                    if (unbreakable != null && (ignore == null || !hit.transform.IsChildOf(ignore.transform)) && Unbreakable(hit)) unbreakable.Add(hit);
                    continue;
                }
                GameObject root = nview.gameObject;
                if (root == ignore || (hut != null && root == hut.gameObject) || root.GetComponent<Character>() != null) continue;
                MineRock5 bigRock = root.GetComponent<MineRock5>();
                MineRock rock = root.GetComponent<MineRock>();
                if (bigRock != null || rock != null)
                {
                    // Big rocks are broken part by part: every part the site's box reaches, wherever its middle is, unless
                    // another building rests on it (SiteRules.Rock).
                    if (!hit.isTrigger && hit.enabled)
                        RockFound(root, hit, hit, bigRock != null ? bigRock.m_minToolTier : rock.m_minToolTier, site, thisSite, matchesPlan, clear,
                            blockers, heldRocks);
                    continue;
                }
                if (!SiteRules.Counts(hit.isTrigger, root.GetComponent<Piece>() != null)) continue;   // a fire's warmth, a workbench's base area
                if (IsGraveOrCrate(root))   // a player's grave, a crate from a ship: left alone, and the site waits for it to be emptied
                {
                    if (graves != null && !hit.isTrigger) graves.Add(hit);
                    continue;
                }
                if (s_unknown.Contains(root))   // nothing the scan knows: each of its colliders is looked at
                {
                    if (unbreakable != null && Unbreakable(hit)) unbreakable.Add(hit);
                    continue;
                }
                if (!s_seen.Add(root)) continue;
                if (Known(root)) Classify(root, hit, site, thisSite, matchesPlan, items, clear, blockers, heldRocks);
                else
                {
                    s_unknown.Add(root);
                    if (unbreakable != null && Unbreakable(hit)) unbreakable.Add(hit);
                }
            }
        }

        /// <summary>
        /// A rock the site's box reaches (<paramref name="part"/>: a big rock's part, or null for a rock in one piece): broken
        /// up, unless a building rests on any of it (SiteRules.Rock) — the game grounds a piece on any solid body that isn't
        /// a building piece within its 0.15 m support reach (SupportProbe, as WearNTear.UpdateSupport), so breaking the rock
        /// could bring it down (the fix pass's review of 1.4.1). Another building's: where the building itself goes it blocks
        /// the site (in <paramref name="heldRocks"/>: it holds the clearing, and nothing goes up into it), in the margin it is
        /// left alone. The site's own standing pieces': left as it is.
        /// </summary>
        private static void RockFound(GameObject root, Collider hit, Collider part, int tier, Footprint site, string thisSite,
            Func<Piece, bool> matchesPlan, List<SiteThing> clear, List<Piece> blockers, List<Collider> heldRocks)
        {
            RockHold hold = HoldOf(root, thisSite, matchesPlan);
            switch (SiteRules.Rock(hold.Another != null, hold.Own, Reaches(site.Grow(-SiteSurvey.SiteMargin), hit.bounds)))
            {
                case RockOnSite.Clear:
                    clear.Add(new SiteThing(root, ClearKind.Rock, tier, part, null));
                    break;
                case RockOnSite.Blocks:
                    if (!blockers.Contains(hold.Another)) blockers.Add(hold.Another);
                    heldRocks?.Add(hit);
                    break;
            }
        }

        /// <summary>The game's support reach round a piece's colliders, and a little more: what touches a rock this close may rest on it.</summary>
        private const float RestsOnReach = 0.2f;

        /// <summary>Who rests on a rock: a piece of another building, and whether the site's own standing pieces do.</summary>
        private sealed class RockHold
        {
            public Piece Another;
            public bool Own;
        }

        private static readonly Dictionary<GameObject, RockHold> s_rockHolds = new Dictionary<GameObject, RockHold>();

        /// <summary>
        /// Who rests on any of this rock (all its colliders, once a scan: a big rock is judged whole, the whole-mod review of
        /// 1.4.1's builder, I2). Not counted: the placement ghost (no network id) and this site's chest, which stand on the
        /// ground anyway; old pieces of this site coming down, and an old colony building's leftovers, which come down too
        /// (M5). The plan's pieces standing (this site's, or a player's built just so) are the site's own (M6).
        /// </summary>
        private static RockHold HoldOf(GameObject root, string thisSite, Func<Piece, bool> matchesPlan)
        {
            if (s_rockHolds.TryGetValue(root, out RockHold known)) return known;
            var hold = new RockHold();
            if (s_pieceMask == 0) s_pieceMask = LayerMask.GetMask("piece", "piece_nonsolid");
            foreach (Collider collider in root.GetComponentsInChildren<Collider>())
            {
                if (collider == null || collider.isTrigger || !collider.enabled) continue;
                Bounds b = collider.bounds;
                int count = Physics.OverlapBoxNonAlloc(b.center, b.extents + Vector3.one * RestsOnReach, s_touching, Quaternion.identity, s_pieceMask,
                    QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count && hold.Another == null; i++)
                {
                    Piece piece = s_touching[i] != null ? s_touching[i].GetComponentInParent<Piece>() : null;
                    ZNetView view = piece != null ? piece.GetComponent<ZNetView>() : null;
                    if (piece == null || view == null || !view.IsValid() || PiecePlacer.IsWildVegetation(piece) || PiecePlacer.IsLooseItem(piece)) continue;
                    string pieceSite = PiecePlacer.SiteOf(piece.gameObject);
                    bool ofThisSite = thisSite.Length > 0 && pieceSite == thisSite;
                    if (thisSite.Length > 0 && piece.GetComponent<ColonyBuilding>()?.Id == thisSite) continue;   // its chest
                    if (matchesPlan != null && matchesPlan(piece)) hold.Own = true;
                    else if (ofThisSite) continue;   // an old piece of this site: it comes down anyway
                    else if (SiteRules.IsLeftover(pieceSite, thisSite, ColonyBuilding.FindById(pieceSite) != null)) continue;
                    else hold.Another = piece;
                }
                if (hold.Another != null) break;
            }
            s_rockHolds[root] = hold;
            return hold;
        }

        private static int s_pieceMask;
        private static readonly Collider[] s_touching = new Collider[64];

        /// <summary>A box (seen from above) reaches into the footprint: any of it, not only its middle or a corner.</summary>
        public static bool Reaches(Footprint footprint, Bounds bounds) =>
            footprint.Overlaps(new Footprint(V(bounds.center), Quat.Identity, bounds.extents.x, bounds.extents.z));

        /// <summary>A player's grave, or a crate from a ship (a container that isn't a building piece): it goes once it's emptied.</summary>
        private static bool IsGraveOrCrate(GameObject root) =>
            root.GetComponent<TombStone>() != null || (root.GetComponent<Container>() != null && root.GetComponent<Piece>() == null);

        /// <summary>Something the scan knows how to clear, keep or wait for (everything Classify looks at).</summary>
        private static bool Known(GameObject root) =>
            root.GetComponent<Piece>() != null || root.GetComponent<ItemDrop>() != null || root.GetComponent<TreeBase>() != null
            || root.GetComponent<TreeLog>() != null || root.GetComponent<Destructible>() != null || root.GetComponent<Pickable>() != null
            || root.GetComponent<Plant>() != null;

        /// <summary>A solid, still body of the world's, of real size (SiteRules.Unbreakable): a boulder nothing breaks.</summary>
        private static bool Unbreakable(Collider hit)
        {
            if (s_solidLayers == 0) s_solidLayers = LayerMask.GetMask("static_solid", "Default");
            if (!hit.enabled || (s_solidLayers & (1 << hit.gameObject.layer)) == 0) return false;
            Bounds bounds = hit.bounds;
            bool moves = hit.attachedRigidbody != null && !hit.attachedRigidbody.isKinematic;   // a cart, a ship
            float ground = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(bounds.center) : bounds.min.y;
            return SiteRules.Unbreakable(hit.isTrigger, moves, Mathf.Max(bounds.size.x, bounds.size.z), bounds.max.y - ground);
        }

        /// <summary>
        /// Whether any of these reaches into the building itself (<paramref name="inside"/>: the site without its margin),
        /// from <paramref name="bottom"/> up (the design's lowest point: a rock's roots under the ground don't count); the
        /// ones that do are named in <paramref name="names"/>.
        /// </summary>
        public static bool ReachesIn(Footprint inside, float bottom, float top, List<Collider> things, List<string> names)
        {
            if (things.Count == 0) return false;
            var centre = new Vector3(inside.Centre.X, (bottom + top) / 2f, inside.Centre.Z);
            var half = new Vector3(Mathf.Max(0.05f, inside.HalfX), Mathf.Max(0.5f, (top - bottom) / 2f), Mathf.Max(0.05f, inside.HalfZ));
            int count = Physics.OverlapBoxNonAlloc(centre, half, s_hits, U(inside.Rotation), ~0, QueryTriggerInteraction.Ignore);
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                if (!things.Contains(s_hits[i])) continue;
                any = true;
                Bounds b = s_hits[i].bounds;
                names.Add($"{s_hits[i].transform.root.name} ({b.center.x:0}, {b.center.z:0}), {Mathf.Max(b.size.x, b.size.z):0.0} m across");
            }
            return any;
        }

        /// <summary>
        /// Adds to <paramref name="clear"/> what grows or lies naturally in front of the doors (their landings, outside the
        /// site), so nothing blocks the way in. Seen: a small beech in a doorway kept settlers out of their hut.
        /// </summary>
        public static void AddDoorLandings(IEnumerable<Footprint> landings, float bottom, float top, ColonyBuilding hut,
            Func<Piece, bool> matchesPlan, List<SiteThing> clear)
        {
            var known = new HashSet<string>(clear.Select(t => t.Key));
            foreach (Footprint landing in landings)
            {
                Scan(landing, bottom, top, hut, null, matchesPlan, false, s_landing, s_landingBlockers);
                foreach (SiteThing thing in s_landing)
                    if (SiteRules.ClearsInFrontOfDoor(thing.Kind) && known.Add(thing.Key)) clear.Add(thing);
            }
        }

        private static void Classify(GameObject root, Collider hit, Footprint site, string thisSite, Func<Piece, bool> matchesPlan,
            bool items, List<SiteThing> clear, List<Piece> blockers, List<Collider> heldRocks)
        {
            bool inside = site.Contains(V(root.transform.position));
            Piece piece = root.GetComponent<Piece>();
            if (PiecePlacer.IsWildVegetation(piece)) piece = null;   // a mod's plantable tree or bush, growing wild
            if (PiecePlacer.IsLooseItem(piece)) piece = null;        // a mushroom lying there: an item, though it has a Piece
            if (piece != null)
            {
                if (root.GetComponent<Plant>() != null && !piece.IsPlacedByPlayer())
                {
                    if (inside) clear.Add(new SiteThing(root, ClearKind.Plant, 0, null, null));   // a sapling a settler planted
                    return;
                }
                string pieceSite = PiecePlacer.SiteOf(root);
                if (pieceSite.Length > 0 && pieceSite == thisSite) return;   // the builder's own work
                if (matchesPlan != null && matchesPlan(piece)) return;      // exactly part of the plan
                if (SiteRules.IsLeftover(pieceSite, thisSite, ColonyBuilding.FindById(pieceSite) != null))
                    clear.Add(new SiteThing(root, ClearKind.Leftover, 0, null, piece));
                else blockers.Add(piece);
                return;
            }
            ItemDrop drop = root.GetComponent<ItemDrop>();
            if (drop != null)
            {
                if (items && inside && CanCarryOff(drop.m_itemData)) clear.Add(new SiteThing(root, ClearKind.Item, 0, null, null));
                return;   // a player's own gear (a weapon, a crafted or worn item) stays where it lies
            }
            TreeBase tree = root.GetComponent<TreeBase>();
            if (tree != null)
            {
                if (inside) clear.Add(new SiteThing(root, ClearKind.Tree, tree.m_minToolTier, null, null));
                return;
            }
            TreeLog log = root.GetComponent<TreeLog>();
            if (log != null)
            {
                if (inside || Touches(site, hit.bounds)) clear.Add(new SiteThing(root, ClearKind.Log, log.m_minToolTier, null, null));
                return;
            }
            Destructible destructible = root.GetComponent<Destructible>();
            if (destructible != null && destructible.GetDestructibleType() != DestructibleType.Tree && IsRock(destructible))
            {
                // A rock in one piece: in the way when any of it reaches into the site, not only when its middle stands
                // there; it breaks whole, so never one another building rests on (SiteRules.Rock).
                RockFound(root, hit, null, destructible.m_minToolTier, site, thisSite, matchesPlan, clear, blockers, heldRocks);
                return;
            }
            if (!inside) return;
            if (destructible != null)
            {
                if (destructible.GetDestructibleType() == DestructibleType.Tree) clear.Add(new SiteThing(root, ClearKind.Stump, destructible.m_minToolTier, null, null));
                else clear.Add(new SiteThing(root, ClearKind.Plant, 0, null, null));
                return;
            }
            if (root.GetComponent<Pickable>() != null || root.GetComponent<Plant>() != null) clear.Add(new SiteThing(root, ClearKind.Plant, 0, null, null));
        }

        private static bool CanCarryOff(ItemDrop.ItemData item) => SiteRules.CanCarryOff(item.m_shared.m_maxStackSize, item.m_quality,
            item.m_shared.m_useDurability && item.m_durability < item.GetMaxDurability(), !string.IsNullOrEmpty(item.m_crafterName));

        /// <summary>A rock: only a pickaxe hurts it, an axe doesn't.</summary>
        private static bool IsRock(Destructible destructible) =>
            Hurts(destructible.m_damages.m_pickaxe) && !Hurts(destructible.m_damages.m_chop);

        private static bool Hurts(HitData.DamageModifier modifier) =>
            modifier != HitData.DamageModifier.Immune && modifier != HitData.DamageModifier.Ignore;

        /// <summary>Whether a box seen from above reaches into the site (its middle or a corner on it).</summary>
        private static bool Touches(Footprint site, Bounds bounds)
        {
            if (site.Contains(V(bounds.center))) return true;
            foreach (float x in new[] { bounds.min.x, bounds.max.x })
                foreach (float z in new[] { bounds.min.z, bounds.max.z })
                    if (site.Contains(new Vec3(x, bounds.center.y, z))) return true;
            return false;
        }

        /// <summary>
        /// The colony a hut at this spot joins (its area, 1.1: the Town Hall's circle and its Watchtowers'), as a building of
        /// it: its Town Hall, or while that isn't loaded here another of its buildings; null for none.
        /// </summary>
        public static ColonyBuilding ColonyFor(Vector3 chest) => ColonyAreas.BuildingAt(chest);

        /// <summary>The builder level that decides what may be cleared from a hut's site at this spot (see ColonyBuilderLevel).</summary>
        public static int BuilderLevelFor(Vector3 chest) => ConstructionModule.ColonyBuilderLevel(ColonyFor(chest)?.ColonyId);

        /// <summary>The tools in the chests of the colony a hut here would belong to (none without a Town Hall).</summary>
        public static List<ToolInfo> ToolsAtHandFor(Vector3 chest)
        {
            string colony = ColonyFor(chest)?.ColonyId;
            return string.IsNullOrEmpty(colony) ? new List<ToolInfo>() : BuilderTools.AtHand(BuilderTools.ColonyStores(colony, null, chest));
        }

        /// <summary>
        /// Adds every reason a hut can't stand here, apart from the ground's (the survey adds those): below the water
        /// line, a no-build place, someone else's ward, outside the colony area, or another unfinished hut's site.
        /// </summary>
        public static void CheckPlace(SiteFindings findings, Footprint site, Vector3 chest, ConstructionModule self)
        {
            if (chest.y < ZoneSystem.instance.m_waterLevel) findings.AddRed(RedReason.Water);
            var points = new List<Vector3> { U(site.Centre) };
            points.AddRange(site.Corners().Select(c => U(c)));
            foreach (Vector3 point in points)
            {
                if (Location.IsInsideNoBuildLocation(point)) findings.AddRed(RedReason.NoBuild);
                if (!PrivateArea.CheckAccess(point, 0f, false, false)) findings.AddRed(RedReason.Ward);
            }
            ColonyBuilding colony = ColonyFor(chest);
            List<AreaCircle> area = colony != null ? ColonyAreas.CirclesOf(colony.ColonyId) : null;
            if (colony == null || points.Any(p => !ColonyArea.Contains(area, ColonyAreas.Ground(p)))) findings.AddRed(RedReason.OutsideColony);
            foreach (ConstructionModule other in ConstructionModule.All)
            {
                if (other == null || other == self || !other.IsValid) continue;
                BuildOrderRecord order = other.Order;
                if (order == null || order.State == BuildState.Done) continue;
                Footprint theirs = other.SiteFootprint;
                if (theirs != null && theirs.Overlaps(site)) findings.AddRed(RedReason.OtherSite);
            }
        }
    }
}
