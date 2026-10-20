using UnityEngine;

namespace VikingColonies.Jobs
{
    /// <summary>The axis (two end points) and radius of a tree, log or stump, read from its largest solid collider.</summary>
    internal static class ChopShape
    {
        /// <returns>False when no collider gives a shape; then a and b are the object's position and radius is 0.</returns>
        public static bool TryGet(GameObject target, out Vector3 a, out Vector3 b, out float radius)
        {
            a = b = target.transform.position;
            radius = 0f;
            Collider best = null;
            float bestSize = 0f;
            foreach (Collider collider in target.GetComponentsInChildren<Collider>())
            {
                if (collider == null || collider.isTrigger || !collider.enabled) continue;
                float size = collider.bounds.size.sqrMagnitude;
                if (size <= bestSize) continue;
                best = collider;
                bestSize = size;
            }
            if (best == null) return false;

            Transform t = best.transform;
            Vector3 center, size3;
            switch (best)
            {
                case CapsuleCollider capsule:
                    Vector3 axis = Axis(capsule.direction);
                    float half = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
                    a = t.TransformPoint(capsule.center - axis * half);
                    b = t.TransformPoint(capsule.center + axis * half);
                    radius = t.TransformVector(Axis((capsule.direction + 1) % 3) * capsule.radius).magnitude;
                    return true;
                case BoxCollider box:
                    center = box.center;
                    size3 = box.size;
                    break;
                case MeshCollider mesh when mesh.sharedMesh != null:
                    center = mesh.sharedMesh.bounds.center;
                    size3 = mesh.sharedMesh.bounds.size;
                    break;
                default:
                    return false;
            }
            int longest = size3.x >= size3.y && size3.x >= size3.z ? 0 : size3.y >= size3.z ? 1 : 2;
            Vector3 along = Axis(longest) * (size3[longest] * 0.5f);
            a = t.TransformPoint(center - along);
            b = t.TransformPoint(center + along);
            int j = (longest + 1) % 3, k = (longest + 2) % 3;
            radius = Mathf.Max(t.TransformVector(Axis(j) * (size3[j] * 0.5f)).magnitude,
                t.TransformVector(Axis(k) * (size3[k] * 0.5f)).magnitude);
            return true;
        }

        public const float DefaultTrunkRadius = 0.4f;

        /// <summary>
        /// A trunk's radius where the axe lands: cast at the trunk from the settler's side at that height and see
        /// where the bark is. A tree's biggest collider can be its whole crown, so its shape is no guide.
        /// </summary>
        public static float TrunkRadius(GameObject target, Vector3 trunk, Vector3 from, float height)
        {
            Vector3 dir = from - trunk;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            dir.Normalize();
            const float probe = 3f;
            var ray = new Ray(trunk + dir * probe + Vector3.up * height, -dir);
            float nearest = float.MaxValue;
            foreach (Collider collider in target.GetComponentsInChildren<Collider>())
            {
                if (collider == null || collider.isTrigger || !collider.enabled) continue;
                if (collider.Raycast(ray, out RaycastHit hit, probe) && hit.distance < nearest) nearest = hit.distance;
            }
            return nearest == float.MaxValue ? DefaultTrunkRadius : Mathf.Clamp(probe - nearest, 0.1f, 2f);
        }

        private static Vector3 Axis(int index) => index == 0 ? Vector3.right : index == 1 ? Vector3.up : Vector3.forward;
    }
}
