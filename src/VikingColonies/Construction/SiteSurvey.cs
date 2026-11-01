using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Core;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.Construction
{
    /// <summary>
    /// Measures a hut's site from its plan: the building's outline seen from above, how far it reaches below and above
    /// the floor (the chest's height), and the ground level the builder flattens it to.
    /// </summary>
    internal static class SiteSurvey
    {
        /// <summary>The site is the building's outline plus this much on every side.</summary>
        public const float SiteMargin = 0.5f;

        /// <summary>The corners of every planned piece's real size, seen from above.</summary>
        public static List<Vec3> OutlinePoints(IReadOnlyList<PlannedPiece> plan)
        {
            var points = new List<Vec3>();
            foreach (PlannedPiece piece in plan)
            {
                if (!piece.IsValid) continue;
                GameObject prefab = PiecePlacer.Prefab(piece.Prefab);
                if (prefab == null)
                {
                    points.Add(piece.Position);
                    continue;
                }
                Bounds shape = PieceShape.Of(prefab).Extents;
                Vector3 position = U(piece.Position);
                Quaternion rotation = U(piece.Rotation);
                foreach (float x in new[] { shape.min.x, shape.max.x })
                    foreach (float z in new[] { shape.min.z, shape.max.z })
                        points.Add(V(position + rotation * new Vector3(x, shape.center.y, z)));
            }
            return points;
        }

        public static Footprint SiteFootprint(IReadOnlyList<PlannedPiece> plan, Vector3 chest, Quaternion rotation) =>
            Footprint.Around(OutlinePoints(plan), V(chest), Q(rotation), SiteMargin);

        /// <summary>
        /// What the plan's walls on the ground enclose (RoomRules), with the site's margin round it as SiteFootprint has:
        /// inside the hut, without the yard, the porch or the woodpile strip round it. Null for a design without such
        /// walls (the Town Hall's camp).
        /// </summary>
        public static Footprint RoomFootprint(IReadOnlyList<PlannedPiece> plan, Vector3 chest, Quaternion rotation)
        {
            var walls = new List<PlannedPiece>();
            foreach (PlannedPiece piece in plan)
            {
                if (!piece.IsValid) continue;
                GameObject prefab = PiecePlacer.Prefab(piece.Prefab);
                if (prefab == null) continue;
                Bounds shape = PieceShape.Of(prefab).Extents;
                float bottom = piece.Position.Y + shape.min.y, top = piece.Position.Y + shape.max.y;   // walls stand upright: only turned about y
                if (RoomRules.IsRoomWall(piece.Prefab, bottom - chest.y, top - chest.y)) walls.Add(piece);
            }
            return walls.Count == 0 ? null : Footprint.Around(OutlinePoints(walls), V(chest), Q(rotation), SiteMargin);
        }

        /// <summary>The ground in front of each of the plan's doors, levelled with the site (<see cref="DoorLanding"/>).</summary>
        public static List<Footprint> DoorLandings(IReadOnlyList<PlannedPiece> plan, Footprint site, Vector3 chest)
        {
            var landings = new List<Footprint>();
            foreach (PlannedPiece piece in plan)
            {
                if (!piece.IsValid) continue;
                GameObject prefab = PiecePlacer.Prefab(piece.Prefab);
                if (prefab == null || prefab.GetComponent<Door>() == null) continue;
                Vec3 forward = piece.Rotation.Rotate(new Vec3(0f, 0f, 1f));
                Vec3 outward = Vec3.Dot(forward.Flat, (piece.Position - V(chest)).Flat) >= 0f ? forward : forward * -1f;
                landings.Add(DoorLanding.For(site, piece.Position, outward));
            }
            return landings;
        }

        /// <summary>How far the design's pieces reach below the floor (0 or more) and above it.</summary>
        public static void Span(IReadOnlyList<PlannedPiece> plan, float floor, out float reachBelow, out float topAbove)
        {
            float lowest = floor, highest = floor;
            foreach (PlannedPiece piece in plan)
            {
                if (!piece.IsValid) continue;
                GameObject prefab = PiecePlacer.Prefab(piece.Prefab);
                if (prefab == null)
                {
                    lowest = Mathf.Min(lowest, piece.Position.Y);
                    highest = Mathf.Max(highest, piece.Position.Y);
                    continue;
                }
                Bounds shape = PieceShape.Of(prefab).Extents;
                Vector3 position = U(piece.Position);
                Quaternion rotation = U(piece.Rotation);
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? shape.min.x : shape.max.x, (i & 2) == 0 ? shape.min.y : shape.max.y, (i & 4) == 0 ? shape.min.z : shape.max.z);
                    float y = (position + rotation * corner).y;
                    lowest = Mathf.Min(lowest, y);
                    highest = Mathf.Max(highest, y);
                }
            }
            reachBelow = floor - lowest;
            topAbove = highest - floor;
        }

        /// <summary>The ground's height here (the terrain as it is now, after any digging).</summary>
        public static float GroundAt(float x, float z) => ZoneSystem.instance.GetGroundHeight(new Vector3(x, 0f, z));

        /// <summary>The level the builder flattens this hut's site to (GroundRule), from the natural ground at the chest.</summary>
        public static float Target(IReadOnlyList<PlannedPiece> plan, Vector3 chest)
        {
            Span(plan, chest.y, out float reachBelow, out _);
            return GroundRule.Target(chest.y, reachBelow, GroundAt(chest.x, chest.z));
        }

        /// <summary>The level an upgrade to this design flattens to: the ground the hut already has when the design stands on it (GroundRule.ForUpgrade).</summary>
        public static float UpgradeTarget(IReadOnlyList<PlannedPiece> plan, Vector3 chest)
        {
            Span(plan, chest.y, out float reachBelow, out _);
            float current = GroundAt(chest.x, chest.z);
            return GroundRule.ForUpgrade(chest.y, reachBelow, current, current);
        }

        /// <summary>
        /// Everything the site check needs for a hut at this spot: what the builder would clear, how the ground must
        /// change (and the stone for it), and every reason the hut can't stand here. <paramref name="clear"/> and
        /// <paramref name="blockers"/> are filled as by <see cref="SiteScanner.Scan"/>. An <paramref name="upgrade"/>'s
        /// ground is judged as the upgrade levels it (<see cref="GroundRule.Judge"/>). <paramref name="type"/> is the kind being
        /// placed (a building's own when <paramref name="self"/> is given), for its own rules (the storehouse's, 1.5). An upgrade
        /// keeps the player's own pieces (<see cref="KeepOwnPieces"/>); <paramref name="putBacks"/>, when given, gets those the
        /// builder takes down and puts back.
        /// </summary>
        public static SiteFindings Look(IReadOnlyList<PlannedPiece> plan, Vector3 chest, Quaternion rotation, ConstructionModule self,
            GameObject ignore, Func<Piece, bool> matchesPlan, float stonePerCubicMetre, List<SiteThing> clear, List<Piece> blockers,
            bool upgrade = false, BuildingType type = null, List<Piece> putBacks = null)
        {
            var findings = new SiteFindings();
            Footprint site = SiteFootprint(plan, chest, rotation);
            float floor = chest.y;
            Span(plan, floor, out float reachBelow, out float topAbove);
            float natural = GroundAt(chest.x, chest.z);
            findings.Floor = floor;
            findings.ReachBelow = reachBelow;
            findings.Natural = natural;
            findings.AddRed(GroundRule.Judge(floor, reachBelow, natural, upgrade, out float target));
            findings.Target = target;
            GroundPlan.Range(site, GroundAt, out float lowest, out float highest);
            if (GroundRule.TooSteep(lowest, highest)) findings.AddRed(RedReason.TooSteep);
            SiteScanner.CheckPlace(findings, site, chest, self, type ?? (self != null ? self.Building?.Type : null));

            float bottom = Mathf.Min(lowest, floor - reachBelow) - 1f;
            float top = Mathf.Max(highest, floor + Mathf.Max(topAbove, 2f)) + 1f;
            SiteScanner.Scan(site, bottom, top, self != null ? self.Building : null, ignore, matchesPlan, true, clear, blockers, s_unbreakable, s_graves);
            // A player's grave or a ship's crate where the building itself goes: it waits for it to be emptied.
            s_graveNames.Clear();
            if (SiteScanner.ReachesIn(site.Grow(-SiteMargin), floor - reachBelow, top, s_graves, s_graveNames))
                findings.AddRed(RedReason.Grave);
            // A rock nothing breaks where the building itself goes (not only the margin round it) is in the way for good.
            if (SiteScanner.ReachesIn(site.Grow(-SiteMargin), floor - reachBelow, top, s_unbreakable, findings.Unbreakable))
                findings.AddRed(RedReason.Unbreakable);
            SiteScanner.AddDoorLandings(DoorLandings(plan, site, chest), bottom, top, self != null ? self.Building : null, matchesPlan, clear);
            foreach (SiteThing thing in clear.Where(t => t.Kind != ClearKind.Rock)) findings.Add(thing.Kind, thing.Tier);
            foreach (IGrouping<GameObject, SiteThing> rock in clear.Where(t => t.Kind == ClearKind.Rock).GroupBy(t => t.Root))
                findings.Add(ClearKind.Rock, rock.Max(t => t.Tier));   // a big rock counts once, however many parts are on the site
            if (upgrade && self != null && self.Building != null)
            {
                AddOwnPiecesOnOldParts(self, chest, rotation, bottom, top, ignore, matchesPlan, blockers);
                KeepOwnPieces(self, plan, chest, rotation, clear, blockers, findings, putBacks);
            }
            if (blockers.Count > 0) findings.AddRed(RedReason.OtherBuilding);
            LastBlockersForTest = string.Join(", ", blockers.Where(b => b != null).Select(b => $"{Utils.GetPrefabName(b.gameObject)} at {b.transform.position}"));

            List<GroundOp> ops = GroundPlan.Plan(site, findings.Target, GroundAt);
            foreach (Footprint landing in DoorLandings(plan, site, chest))   // the ground in front of the doors is levelled too
                ops.AddRange(GroundPlan.Plan(landing, findings.Target, GroundAt));
            findings.Dig = ops.Any(o => o.Dig);
            findings.DeepDig = ops.Any(o => o.DeepDig);
            findings.Raise = ops.Any(o => o.Raise);
            findings.Stone = GroundPlan.Stone(ops, stonePerCubicMetre);
            return findings;
        }

        /// <summary>
        /// The player's pieces on old parts outside the new level's outline (a porch it drops, a lantern on their beam just
        /// outside) are judged too, as they'd fall with them (1.6, the review's finding 8): the old outline is looked at as well.
        /// </summary>
        private static void AddOwnPiecesOnOldParts(ConstructionModule self, Vector3 chest, Quaternion rotation, float bottom, float top,
            GameObject ignore, Func<Piece, bool> matchesPlan, List<Piece> blockers)
        {
            IReadOnlyList<PlannedPiece> oldPlan = self.Plan;
            if (oldPlan == null || oldPlan.Count == 0) return;
            var oldClear = new List<SiteThing>();
            var oldBlockers = new List<Piece>();
            SiteScanner.Scan(SiteFootprint(oldPlan, chest, rotation), bottom, top, self.Building, ignore, matchesPlan, false, oldClear, oldBlockers);
            foreach (Piece piece in oldBlockers)
                if (PiecePlacer.IsPlayersOwn(piece) && !blockers.Contains(piece)) blockers.Add(piece);
        }

        /// <summary>
        /// An upgrade and the player's own pieces in its lot (1.6, the own-pieces design): each stays, or the builder takes it
        /// down and puts it back, and neither is in the way; the upgrade waits only for one where a new piece goes, one
        /// holding things, a player's bed, or one nothing of the new level would hold (<see cref="OwnPieceRules.Fates"/>).
        /// All of the player's pieces come out of <paramref name="blockers"/>: the first the upgrade waits for is named in
        /// <paramref name="findings"/> (RedReason.OwnPiece), with the put-backs' count. Every box is measured in the hut's frame.
        /// </summary>
        private static void KeepOwnPieces(ConstructionModule self, IReadOnlyList<PlannedPiece> plan, Vector3 chest, Quaternion turn,
            List<SiteThing> clear, List<Piece> blockers, SiteFindings findings, List<Piece> putBacks)
        {
            List<Piece> own = blockers.Where(PiecePlacer.IsPlayersOwn).Distinct().ToList();
            if (own.Count == 0)
            {
                s_lastOwnPieces = "";
                return;
            }
            blockers.RemoveAll(b => own.Contains(b));
            IReadOnlyList<PlannedPiece> oldPlan = self.Plan;
            DesignDiff diff = DesignDiff.Compare(oldPlan, plan);
            List<PlannedPiece> built = diff.Build.Select(i => plan[i]).Where(p => p.IsValid).ToList();
            List<PlannedPiece> keptPlan = diff.Kept.Select(i => plan[i]).Where(p => p.IsValid).ToList();
            var takenAway = new List<PieceBox>();
            var takenNames = new List<string>();
            foreach (PlannedPiece old in diff.TakeAway.Select(i => oldPlan[i]).Where(p => p.IsValid))
            {
                takenAway.Add(PiecePlacer.BoxIn(old, chest, turn));
                takenNames.Add(old.Prefab);
            }
            foreach (SiteThing leftover in clear.Where(t => t.Kind == ClearKind.Leftover && t.Piece != null))   // the leftovers it clears
            {
                takenAway.Add(PiecePlacer.BoxIn(leftover.Piece, chest, turn));
                takenNames.Add(Utils.GetPrefabName(leftover.Piece.gameObject));
            }
            Func<PieceBox, bool> onGround = box => OnGroundIn(box, chest, turn);
            List<PieceBox> keptBoxes = keptPlan.Select(p => PiecePlacer.BoxIn(p, chest, turn)).ToList();
            var forAWhile = new HashSet<int>(OwnPieceRules.KeptForAWhile(keptBoxes, takenAway, onGround));
            var kept = new List<PieceBox>();
            var keptForAWhile = new List<PieceBox>();
            for (int i = 0; i < keptBoxes.Count; i++)
            {
                if (forAWhile.Contains(i))
                {
                    keptForAWhile.Add(keptBoxes[i]);
                    takenNames.Add(keptPlan[i].Prefab + " (kept, taken down for a while)");   // after the taken-away ones, as Fates counts them
                }
                else kept.Add(keptBoxes[i]);
            }
            Piece hutChest = self.GetComponent<Piece>();
            if (hutChest != null) kept.Add(PiecePlacer.BoxIn(hutChest, chest, turn));

            var pieces = new List<OwnPiece>();
            var holdsWhy = new List<OwnPieceWhy>();
            foreach (Piece piece in own)
            {
                PieceBox box = PiecePlacer.BoxIn(piece, chest, turn);
                bool holds = PiecePlacer.HoldsThings(piece, out OwnPieceWhy why);
                pieces.Add(new OwnPiece(box, onGround(box), holds, PiecePlacer.IsPlayersBed(piece)));
                holdsWhy.Add(why);
            }
            List<OwnPieceVerdict> verdicts = OwnPieceRules.Fates(pieces, built.Select(p => PiecePlacer.BoxIn(p, chest, turn)).ToList(), kept,
                keptForAWhile, takenAway);

            int first = OwnPieceRules.FirstWait(verdicts);
            if (first >= 0)
            {
                OwnPieceVerdict waits = verdicts[first];
                findings.AddRed(RedReason.OwnPiece);
                findings.OwnPiece = own[first].m_name;
                findings.OwnPieceWhy = OwnPieceRules.WhyOf(waits, holdsWhy[first]);
                findings.OwnPieceNewPiece = waits.Fate == OwnPieceFate.InTheWay ? PieceName(built[waits.Which].Prefab) : "";
            }
            findings.PutBacks = OwnPieceRules.PutBackCount(verdicts);
            var lines = new List<string>();
            for (int i = 0; i < own.Count; i++)
            {
                OwnPieceVerdict v = verdicts[i];
                if (v.Fate == OwnPieceFate.PutBack) putBacks?.Add(own[i]);
                if (v.Fate == OwnPieceFate.Stays) continue;
                string what = $"{PiecePlacer.Whose(own[i].GetCreator())} {Utils.GetPrefabName(own[i].gameObject)} at {own[i].transform.position}";
                if (v.Fate == OwnPieceFate.InTheWay) lines.Add($"{what} is in the upgrade's way: the new level's {built[v.Which].Prefab} goes there");
                else if (v.Fate == OwnPieceFate.PutBack)
                    lines.Add($"{what} is taken down and put back after ({(v.Which >= 0 ? "it touches the old " + takenNames[v.Which] : "it touches another of their pieces")})");
                else lines.Add($"{what} makes the upgrade wait ({v.Wait})");
            }
            string said = string.Join("; ", lines);
            if (said.Length > 0 && said != s_lastOwnPieces)   // once, not every look of the window
                Log.Info($"{self.Building.TypeId} {self.Building.Id}: the player's pieces through its upgrade: {said}");
            s_lastOwnPieces = said;
        }

        private static string s_lastOwnPieces = "";

        /// <summary>A piece's own name token (<c>$piece_…</c>), so each player reads it in their language; its prefab's name without one.</summary>
        private static string PieceName(string prefabName)
        {
            GameObject prefab = PiecePlacer.Prefab(prefabName);
            Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
            return piece != null && !string.IsNullOrEmpty(piece.m_name) ? piece.m_name : prefabName;
        }

        /// <summary><see cref="OnGround"/> for a box measured in a hut's frame (<see cref="PiecePlacer.BoxIn(Piece, Vector3, Quaternion)"/>).</summary>
        public static bool OnGroundIn(PieceBox box, Vector3 origin, Quaternion turn)
        {
            Vector3 middle = origin + turn * new Vector3((box.Min.X + box.Max.X) / 2f, 0f, (box.Min.Z + box.Max.Z) / 2f);
            return origin.y + box.Min.Y <= GroundAt(middle.x, middle.z) + 0.3f;
        }

        /// <summary>vc_storehousetest: the pieces in the way at the last site check.</summary>
        internal static string LastBlockersForTest = "";

        /// <summary>A piece standing on the ground (as the builder judges it): it needs nothing under it.</summary>
        public static bool OnGround(PieceBox box) => box.Min.Y <= GroundAt((box.Min.X + box.Max.X) / 2f, (box.Min.Z + box.Max.Z) / 2f) + 0.3f;

        private static readonly List<Piece> s_near = new List<Piece>();
        private static readonly List<Collider> s_unbreakable = new List<Collider>();
        private static readonly List<Collider> s_graves = new List<Collider>();
        private static readonly List<string> s_graveNames = new List<string>();

        /// <summary>
        /// Whether reshaping the ground here would reach another building (a player's, another hut's): the ground under it
        /// must stay as it is, or the building could lose its footing. The site's own chest and pieces don't count.
        /// </summary>
        public static bool NearOtherBuilding(ConstructionModule site, float x, float z, float radius) =>
            OtherBuildingNear(site, x, z, radius) != null;

        /// <summary>The other building's piece <see cref="NearOtherBuilding"/> finds there, or null.</summary>
        public static Piece OtherBuildingNear(ConstructionModule site, float x, float z, float radius)
        {
            s_near.Clear();
            Piece.GetAllPiecesInRadius(new Vector3(x, GroundAt(x, z), z), radius + KeepOffMargin, s_near);
            foreach (Piece piece in s_near)
            {
                if (piece == null) continue;
                // Wild plants, a settler's sapling and a loose item (the game gives a mushroom a Piece) aren't built.
                bool vegetation = PiecePlacer.IsWildVegetation(piece) || (piece.GetComponent<Plant>() != null && !piece.IsPlacedByPlayer());
                if (SiteRules.KeepsGroundOff(piece.gameObject == site.gameObject, PiecePlacer.SiteOf(piece.gameObject), site.Building.Id,
                        site.MatchesPlan(piece), vegetation, PiecePlacer.IsLooseItem(piece)))
                    return piece;
            }
            return null;
        }

        /// <summary>Ground work stays this far from other buildings.</summary>
        private const float KeepOffMargin = 0.5f;
    }
}
