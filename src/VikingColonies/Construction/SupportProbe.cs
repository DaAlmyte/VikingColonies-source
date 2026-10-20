using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Core;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.Construction
{
    /// <summary>
    /// Would a piece hold if it were placed here now? Mirrors WearNTear.UpdateSupport against the real pieces around
    /// the spot, with the game's own support numbers (so mods that change them are respected).
    /// </summary>
    internal static class SupportProbe
    {
        private static readonly FieldInfo RayMaskField = AccessTools.Field(typeof(WearNTear), "s_rayMask");
        private static readonly FieldInfo TerrainLayerField = AccessTools.Field(typeof(WearNTear), "s_terrainLayer");
        private static readonly FieldInfo SupportField = AccessTools.Field(typeof(WearNTear), "m_support");
        private static readonly MethodInfo MaterialPropertiesMethod = AccessTools.Method(typeof(WearNTear), "GetMaterialProperties");
        private static readonly Collider[] s_hits = new Collider[256];

        /// <summary>The piece's material numbers: GetMaterialProperties(ref max, ref min, ref horizontalLoss, ref verticalLoss).</summary>
        public static SupportMaterial MaterialOf(WearNTear piece)
        {
            object[] values = { 0f, 0f, 0f, 0f };
            MaterialPropertiesMethod.Invoke(piece, values);
            return new SupportMaterial((float)values[0], (float)values[1], (float)values[2], (float)values[3]);
        }

        /// <summary>A standing piece's support as the game reports it (WearNTear.GetSupport).</summary>
        public static float SupportOf(WearNTear piece)
        {
            float max = MaterialOf(piece).MaxSupport;
            ZNetView nview = piece.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.HasOwner()) return max;
            if (nview.IsOwner()) return (float)SupportField.GetValue(piece);
            return nview.GetZDO().GetFloat(ZDOVars.s_support, max);
        }

        public static bool WouldHold(GameObject prefab, Vector3 position, Quaternion rotation, out float support)
        {
            support = 0f;
            WearNTear wear = prefab.GetComponent<WearNTear>();
            if (wear == null) return true;   // pieces outside the support system always stay
            SupportMaterial material = MaterialOf(wear);
            Vector3 com = position + rotation * wear.m_comOffset;
            int mask = (int)RayMaskField.GetValue(null);
            if (mask == 0) mask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small", "terrain");
            int? terrainLayer = (int?)TerrainLayerField.GetValue(null);
            bool grounded = false;
            var contacts = new List<SupportContact>();
            foreach (PieceShape.WorldBox box in PieceShape.Of(prefab).SupportBoxesAt(position, rotation))
            {
                int count = Physics.OverlapBoxNonAlloc(box.Center, box.Half, s_hits, box.Rotation, mask);
                for (int i = 0; i < count; i++)
                {
                    Collider hit = s_hits[i];
                    if (hit.attachedRigidbody != null || hit.isTrigger) continue;
                    if (terrainLayer.HasValue && hit.gameObject.layer == terrainLayer.Value)
                    {
                        grounded = true;
                        continue;
                    }
                    WearNTear other = hit.GetComponentInParent<WearNTear>();
                    if (other == null)
                    {
                        support = material.MaxSupport;   // a rock, a tree or other solid ground
                        return true;
                    }
                    if (!other.m_supports) continue;
                    Vector3 otherCom = other.transform.position + other.transform.rotation * other.m_comOffset;
                    float distance = SupportRule.ContactDistance(V(com), V(otherCom), V(other.transform.position), wear.m_forceCorrectCOMCalculation);
                    contacts.Add(new SupportContact(SupportOf(other), distance, V(SupportPoint(com, otherCom, hit) - com)));
                }
            }
            support = SupportRule.Evaluate(material, grounded, contacts);
            return SupportRule.Holds(material, support);
        }

        /// <summary>Where the neighbour holds the piece (WearNTear.FindSupportPoint).</summary>
        private static Vector3 SupportPoint(Vector3 com, Vector3 otherCom, Collider collider)
        {
            if (collider is MeshCollider mesh && !mesh.convex)
                return mesh.Raycast(new Ray(com, Vector3.down), out RaycastHit hit, 10f) ? hit.point : (com + otherCom) * 0.5f;
            return collider.ClosestPoint(com);
        }
    }
}
