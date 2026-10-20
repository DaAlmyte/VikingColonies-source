using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using static System.FormattableString;

namespace VikingColonies.Construction
{
    /// <summary>
    /// vc_build support (2026-10-18): how the game itself holds up one standing piece — the boxes WearNTear.UpdateSupport
    /// looks for neighbours with, next to the piece's own colliders; the parts those boxes find and their layers; what
    /// the game has cached as holding it; its support. It measures what the hut tool's support check (tools/blueprints)
    /// copies: the grausten roofs that fell in the game had been said to hold.
    /// </summary>
    internal static class SupportInfo
    {
        private static readonly FieldInfo BoundsField = AccessTools.Field(typeof(WearNTear), "m_bounds");
        private static readonly FieldInfo SupportCollidersField = AccessTools.Field(typeof(WearNTear), "m_supportColliders");
        private static readonly FieldInfo SupportValuesField = AccessTools.Field(typeof(WearNTear), "m_supportValue");
        private static readonly FieldInfo RayMaskField = AccessTools.Field(typeof(WearNTear), "s_rayMask");
        private static readonly FieldInfo TerrainLayerField = AccessTools.Field(typeof(WearNTear), "s_terrainLayer");
        private static readonly Collider[] s_hits = new Collider[64];

        public static List<string> Describe(Vector3 spot, Transform hut)
        {
            var lines = new List<string>();
            var near = new List<Piece>();
            Piece.GetAllPiecesInRadius(spot, 2f, near);
            WearNTear wear = near.Select(p => p.GetComponent<WearNTear>()).Where(w => w != null)
                .OrderBy(w => Vector3.Distance(w.transform.position, spot)).FirstOrDefault();
            if (wear == null)
            {
                lines.Add("no piece stands within 2 m of that spot");
                return lines;
            }
            int mask = (int)RayMaskField.GetValue(null);
            int? terrain = (int?)TerrainLayerField.GetValue(null);
            Vector3 at = hut.InverseTransformPoint(wear.transform.position);
            lines.Add(Invariant($"{Utils.GetPrefabName(wear.gameObject)} at ({at.x:0.##}, {at.y:0.##}, {at.z:0.##}): support {SupportProbe.SupportOf(wear):0}, it needs {SupportProbe.MaterialOf(wear).MinSupport:0}"));
            lines.Add("the support check sees the layers " + string.Join(", ", Enumerable.Range(0, 32).Where(l => (mask & (1 << l)) != 0).Select(LayerMask.LayerToName)) +
                      $"; the ground is layer {(terrain.HasValue ? LayerMask.LayerToName(terrain.Value) : "?")}");
            foreach (Collider c in wear.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger))
            {
                Bounds b = c.bounds;
                string size = c is BoxCollider box ? Invariant($"box size {Scaled(box):0.##}") : $"{c.GetType().Name}";
                lines.Add(Invariant($"its collider {c.name} ({size}, layer {LayerMask.LayerToName(c.gameObject.layer)}), bounds {b.size:0.##} at {Local(hut, b.center)}"));
            }
            if (BoundsField.GetValue(wear) is IList bounds)
            {
                foreach (object bound in bounds)
                {
                    Traverse t = Traverse.Create(bound);
                    Vector3 pos = t.Field("m_pos").GetValue<Vector3>();
                    Quaternion rot = t.Field("m_rot").GetValue<Quaternion>();
                    Vector3 size = t.Field("m_size").GetValue<Vector3>();
                    int count = Physics.OverlapBoxNonAlloc(pos, size, s_hits, rot, mask);
                    var found = new List<string>();
                    for (int i = 0; i < count; i++)
                    {
                        Collider hit = s_hits[i];
                        if (hit.transform.IsChildOf(wear.transform)) continue;
                        WearNTear other = hit.GetComponentInParent<WearNTear>();
                        string what = terrain.HasValue && hit.gameObject.layer == terrain.Value ? "the ground"
                            : other != null ? Invariant($"{Utils.GetPrefabName(other.gameObject)} at {Local(hut, other.transform.position)}{(other.m_supports ? "" : " (holds nothing up)")}")
                            : hit.name;
                        found.Add($"{what} [{LayerMask.LayerToName(hit.gameObject.layer)}]");
                    }
                    lines.Add(Invariant($"a support box at {Local(hut, pos)}, half size {size:0.##} (the game's), turned {rot.eulerAngles:0}: finds {(found.Count == 0 ? "nothing" : string.Join("; ", found))}"));
                }
            }
            if (SupportCollidersField.GetValue(wear) is IList held && SupportValuesField.GetValue(wear) is IList values)
            {
                for (int i = 0; i < held.Count; i++)
                {
                    var c = held[i] as Collider;
                    WearNTear other = c != null ? c.GetComponentInParent<WearNTear>() : null;
                    lines.Add(Invariant($"the game has it held by {(other != null ? Utils.GetPrefabName(other.gameObject) + " at " + Local(hut, other.transform.position) : c != null ? c.name : "?")}{(i < values.Count ? ", " + values[i] : "")}"));
                }
            }
            return lines;
        }

        private static Vector3 Scaled(BoxCollider box) => Vector3.Scale(box.size, box.transform.lossyScale);

        private static string Local(Transform hut, Vector3 world)
        {
            Vector3 v = hut.InverseTransformPoint(world);
            return Invariant($"({v.x:0.##}, {v.y:0.##}, {v.z:0.##})");
        }
    }
}
