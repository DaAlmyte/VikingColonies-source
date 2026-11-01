using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Core;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.Construction
{
    /// <summary>
    /// The hut being placed: its plan where the placement ghost stands (for the outline), and, at most four times a
    /// second, the site check for that spot (for the colour and the line under the crosshair). On this computer only.
    /// </summary>
    internal static class SitePreview
    {
        private const float CheckSeconds = 0.25f;
        private static readonly List<SiteThing> s_things = new List<SiteThing>();
        private static readonly List<Piece> s_blockers = new List<Piece>();
        private static readonly Dictionary<string, List<Vector3>> s_byPrefab = new Dictionary<string, List<Vector3>>();
        /// <summary>Each hut's sites by level, unturned at the chest, while placing; worked out again next time (the player's designs may change).</summary>
        private static readonly Dictionary<string, List<Footprint>> s_grow = new Dictionary<string, List<Footprint>>();
        private static List<PlannedPiece> s_plan;
        private static BuildingType s_type;
        private static Vector3 s_position;
        private static Quaternion s_rotation;
        private static float s_nextCheck;

        public static bool Active { get; private set; }
        public static IReadOnlyList<PlannedPiece> Plan => s_plan;
        public static SiteVerdict Verdict { get; private set; }
        /// <summary>What the last check found (its numbers go into the log when a hut is placed).</summary>
        public static SiteFindings Findings { get; private set; }
        public static string Text { get; private set; } = "";

        /// <param name="force">Check now, not at most four times a second (the player is placing the hut).</param>
        public static void Update(BuildingType type, GameObject ghost, float offset, bool force = false)
        {
            Active = true;
            Vector3 position = ghost.transform.position;
            Quaternion rotation = ghost.transform.rotation;
            if (type != s_type || position != s_position || rotation != s_rotation)
            {
                s_type = type;
                s_position = position;
                s_rotation = rotation;
                s_plan = PlanAt(type, position, rotation);
                Index();
            }
            if (s_plan == null || s_plan.Count == 0)
            {
                Verdict = null;
                Text = "";
                return;
            }
            if (force || Time.time >= s_nextCheck)
            {
                s_nextCheck = Time.time + CheckSeconds;
                Findings = SiteSurvey.Look(s_plan, position, rotation, null, ghost, MatchesPlan,
                    GroundShaper.StonePerCubicMetre, s_things, s_blockers, type: s_type);
                // Only while placing: the same site check also runs for upgrades on whichever computer owns the hut.
                if (!ColonyPermissions.LocalMayBuildAt(SiteScanner.ColonyFor(position), position)) Findings.AddRed(RedReason.NotYourColony);
                Verdict = SiteCheck.Judge(Findings, SiteScanner.BuilderLevelFor(position), SiteScanner.ToolsAtHandFor(position));
            }
            if (Verdict != null) Text = SiteText.Describe(Verdict, offset) + GrowLine(type, QuarterTurns(rotation));
        }

        /// <summary>
        /// "\nBy level 5 it grows about 12 m toward you: leave room there", or "" for a hut that hardly grows past the level
        /// it's placed at (GrowRoom). The sides are as the player sees them, <paramref name="quarterTurns"/> as GrowRoom.Seen has.
        /// </summary>
        internal static string GrowLine(BuildingType type, int quarterTurns)
        {
            if (!s_grow.TryGetValue(type.Id, out List<Footprint> byLevel))
            {
                byLevel = new List<Footprint>();
                for (int level = 1; level <= HutLevels.MaxLevel; level++)
                {
                    BlueprintFile blueprint = BlueprintLibrary.For(type, level, out _);
                    byLevel.Add(blueprint != null && BlueprintPlan.TryPlace(blueprint, type.PrefabName, V(Vector3.zero), Quat.Identity, out List<PlannedPiece> pieces, out _)
                        && pieces.Count > 0 ? SiteSurvey.SiteFootprint(pieces, Vector3.zero, Quaternion.identity) : null);
                }
                s_grow[type.Id] = byLevel;
            }
            GrowRoom.Growth growth = GrowRoom.From(byLevel, CreativePlacing.Level);   // creative mode: from the level it goes up at
            return growth != null && growth.Worth ? "\n" + SiteText.GrowsBy(GrowRoom.Seen(growth, quarterTurns)) : "";
        }

        /// <summary>Quarter turns the ghost is turned clockwise from facing straight away from the camera.</summary>
        private static int QuarterTurns(Quaternion rotation)
        {
            Camera camera = Utils.GetMainCamera();
            if (camera == null) return 0;
            Vector3 look = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
            Vector3 front = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
            return Mathf.RoundToInt(Vector3.SignedAngle(look, front, Vector3.up) / 90f);
        }

        public static void Clear()
        {
            if (!Active) return;
            Active = false;
            s_plan = null;
            s_type = null;
            Verdict = null;
            Findings = null;
            Text = "";
            s_nextCheck = 0f;
            s_grow.Clear();
        }

        private static List<PlannedPiece> PlanAt(BuildingType type, Vector3 position, Quaternion rotation)
        {
            BlueprintFile blueprint = BlueprintLibrary.For(type, out _);
            if (blueprint == null) return null;
            return BlueprintPlan.TryPlace(blueprint, type.PrefabName, V(position), Q(rotation), out List<PlannedPiece> pieces, out _) ? pieces : null;
        }

        private static void Index()
        {
            s_byPrefab.Clear();
            if (s_plan == null) return;
            foreach (PlannedPiece piece in s_plan.Where(p => p.IsValid))
            {
                if (!s_byPrefab.TryGetValue(piece.Prefab, out List<Vector3> spots)) s_byPrefab[piece.Prefab] = spots = new List<Vector3>();
                spots.Add(U(piece.Position));
            }
        }

        /// <summary>A standing piece exactly where the plan wants the same kind (a player built it already): not in the way.</summary>
        private static bool MatchesPlan(Piece piece) =>
            piece != null && s_byPrefab.TryGetValue(Utils.GetPrefabName(piece.gameObject), out List<Vector3> spots)
            && spots.Any(p => Vector3.Distance(p, piece.transform.position) <= DesignDiff.SamePlace);
    }
}
