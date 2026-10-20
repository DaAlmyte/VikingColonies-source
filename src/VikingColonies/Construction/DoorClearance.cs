using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Core;

namespace VikingColonies.Construction
{
    /// <summary>
    /// What stands in a doorway: a settler-wide corridor through the door, 2 m out on each side, from knee to head height.
    /// Seen: settlers stuck at doors with a small beech, a log or a piece of furniture in the way, at some spots and not
    /// others. The door itself, people and the hut's own chest don't count.
    /// </summary>
    internal static class DoorClearance
    {
        /// <summary>Half a settler's width, and a little room.</summary>
        private const float HalfWidth = 0.4f;
        private const float Reach = 2f;
        /// <summary>From knee height (a lip below that is hopped over) to head height, measured from the door's middle.</summary>
        private const float Bottom = -0.6f, Top = 0.7f;
        /// <summary>A step at the threshold higher than this is reported (a settler hops about this much).</summary>
        public const float HighStep = 0.4f;

        private static readonly Collider[] s_hits = new Collider[64];
        private static int s_mask;

        /// <summary>Everything in the doorway, as "beech (in front, 1.2 m out)"; empty when the way is clear.</summary>
        public static List<string> Obstacles(Door door, ColonyBuilding hut)
        {
            var found = new List<string>();
            if (door == null) return found;
            if (s_mask == 0)
            {
                int character = LayerMask.NameToLayer("character");
                for (int i = 0; i < 32; i++)
                    if (!Physics.GetIgnoreLayerCollision(character, i)) s_mask |= 1 << i;
                s_mask &= ~(1 << LayerMask.NameToLayer("terrain"));   // the ground is measured as steps instead
            }
            Transform t = door.transform;
            Vector3 centre = t.position + Vector3.up * ((Bottom + Top) / 2f);
            var half = new Vector3(HalfWidth, (Top - Bottom) / 2f, Reach);
            // Door rotation: forward goes through the doorway, right runs along the wall.
            int count = Physics.OverlapBoxNonAlloc(centre, half, s_hits, t.rotation, s_mask, QueryTriggerInteraction.Ignore);
            var seen = new HashSet<GameObject>();
            for (int i = 0; i < count; i++)
            {
                Collider hit = s_hits[i];
                if (hit == null || hit.transform.IsChildOf(t)) continue;
                if (hit.GetComponentInParent<Character>() != null) continue;
                ZNetView nview = hit.GetComponentInParent<ZNetView>();
                GameObject root = nview != null ? nview.gameObject : hit.transform.root.gameObject;
                if (hut != null && root == hut.gameObject) continue;   // the hut's own chest
                if (!seen.Add(root)) continue;
                Vector3 at = hit.ClosestPoint(t.position);
                float along = Vector3.Dot(at - t.position, t.forward);
                found.Add($"{Name(root, hit)} ({(along >= 0f ? "front" : "back")} side, {Mathf.Abs(along):0.0} m from the door)");
            }
            return found;
        }

        /// <summary>How much higher (+) or lower (-) the ground is 1 m out on each side than the door's bottom: "+0.1 / -0.5".</summary>
        public static string Steps(Door door, out float highest)
        {
            highest = 0f;
            if (door == null) return "";
            Transform t = door.transform;
            float bottom = t.position.y + Bottom - 0.4f;   // the door's lower edge, about
            var parts = new List<string>();
            foreach (float side in new[] { 1f, -1f })
            {
                // From a metre above the door's foot: higher, inside a hut, the ray met the ceiling ("+2,0 m").
                Vector3 probe = t.position + t.forward * side;
                probe.y = bottom + 1f;
                if (!Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 5f, LayerMask.GetMask("terrain", "piece", "Default", "static_solid"), QueryTriggerInteraction.Ignore))
                {
                    parts.Add("?");
                    continue;
                }
                float step = hit.point.y - bottom;
                if (Mathf.Abs(step) > Mathf.Abs(highest)) highest = step;
                parts.Add($"{step:+0.0;-0.0}");
            }
            return string.Join(" / ", parts);
        }

        private static string Name(GameObject root, Collider hit)
        {
            Piece piece = root.GetComponent<Piece>();
            if (piece != null) return Utils.GetPrefabName(root);
            string prefab = Utils.GetPrefabName(root);
            return string.IsNullOrEmpty(prefab) ? $"{hit.name} ({LayerMask.LayerToName(hit.gameObject.layer)})" : prefab;
        }

        /// <summary>Every door of the hut's building that stands.</summary>
        public static List<Door> DoorsOf(ConstructionModule site)
        {
            var doors = new List<Door>();
            if (site == null) return doors;
            foreach (PlannedPiece piece in site.Plan.Where(p => p.IsValid))
            {
                GameObject prefab = PiecePlacer.Prefab(piece.Prefab);
                if (prefab == null || prefab.GetComponent<Door>() == null) continue;
                Piece standing = PiecePlacer.FindStanding(piece);
                if (standing != null) doors.Add(standing.GetComponent<Door>());
            }
            return doors;
        }
    }
}
