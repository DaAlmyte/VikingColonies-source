using System.Collections.Generic;
using UnityEngine;

namespace VikingColonies.Construction
{
    /// <summary>
    /// A piece's colliders, measured once from its prefab: the boxes the game's support check uses
    /// (as WearNTear.SetupColliders builds them) and the piece's overall size.
    /// </summary>
    internal sealed class PieceShape
    {
        public struct WorldBox
        {
            public Vector3 Center;
            public Vector3 Half;
            public Quaternion Rotation;
            /// <summary>A mesh collider's bounds, not a box collider (the game's support check grows the two differently).</summary>
            public bool Mesh;
            /// <summary>The collider's layer (the game's support check only sees some).</summary>
            public int Layer;
        }

        private struct LocalBox
        {
            public Vector3 Center;
            public Vector3 Size;
            public Quaternion Rotation;
            public int Layer;
        }

        private static readonly Dictionary<string, PieceShape> s_cache = new Dictionary<string, PieceShape>();
        private readonly List<LocalBox> _boxes = new List<LocalBox>();
        private readonly List<Vector3[]> _otherCorners = new List<Vector3[]>();
        // Only what stands when the piece is whole and solid (vc_piecedump): not the worn and broken models' colliders,
        // which are switched off until the piece is damaged, nor an area box (the artisan table's is 40 x 200 m).
        private readonly List<LocalBox> _wholeBoxes = new List<LocalBox>();
        private readonly List<Vector3[]> _wholeOtherCorners = new List<Vector3[]>();
        private readonly List<int> _wholeOtherLayers = new List<int>();
        private const float LargestPart = 10f;

        /// <summary>All colliders together, in the prefab's own space (pivot at the origin).</summary>
        public Bounds Extents { get; private set; }

        public static PieceShape Of(GameObject prefab)
        {
            if (!s_cache.TryGetValue(prefab.name, out PieceShape shape)) s_cache[prefab.name] = shape = new PieceShape(prefab);
            return shape;
        }

        private PieceShape(GameObject prefab)
        {
            Transform root = prefab.transform;
            bool first = true, firstWhole = true;
            Bounds extents = default, solid = default;
            foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger || collider.GetComponentInParent<Rigidbody>() != null) continue;
                Matrix4x4 toRoot = root.worldToLocalMatrix * collider.transform.localToWorldMatrix;
                Vector3[] corners = LocalCorners(collider);
                if (corners == null) continue;
                for (int i = 0; i < corners.Length; i++) corners[i] = toRoot.MultiplyPoint3x4(corners[i]);
                bool whole = ActiveUpTo(collider.transform, root);
                if (collider is BoxCollider box)
                {
                    var scale = new Vector3(toRoot.GetColumn(0).magnitude, toRoot.GetColumn(1).magnitude, toRoot.GetColumn(2).magnitude);
                    var local = new LocalBox
                    {
                        Center = toRoot.MultiplyPoint3x4(box.center),
                        Size = Vector3.Scale(scale, box.size),   // the game passes the full size as the half size
                        Rotation = Quaternion.Inverse(root.rotation) * collider.transform.rotation,
                        Layer = collider.gameObject.layer,
                    };
                    _boxes.Add(local);
                    if (whole && Mathf.Max(local.Size.x, local.Size.y, local.Size.z) <= 2f * LargestPart) _wholeBoxes.Add(local);
                }
                else
                {
                    _otherCorners.Add(corners);
                    if (whole && SmallEnough(corners))
                    {
                        _wholeOtherCorners.Add(corners);
                        _wholeOtherLayers.Add(collider.gameObject.layer);
                    }
                }
                foreach (Vector3 corner in corners)
                {
                    if (first) extents = new Bounds(corner, Vector3.zero);
                    else extents.Encapsulate(corner);
                    first = false;
                }
                if (!IsWhole(collider, root, corners)) continue;
                foreach (Vector3 corner in corners)
                {
                    if (firstWhole) solid = new Bounds(corner, Vector3.zero);
                    else solid.Encapsulate(corner);
                    firstWhole = false;
                }
            }
            // The whole piece's solid parts (2026-10-18: the artisan table's 40 x 200 m area box made it "in the way" of a
            // hut 16 m off); all colliders for a piece whose parts are all switched off until it is placed.
            Extents = firstWhole ? extents : solid;
        }

        /// <summary>
        /// Each collider of the whole piece as a box of its true size in the prefab's own space (vc_piecedump; meshes by
        /// their bounds): not the worn and broken models, nor an area box.
        /// </summary>
        public IEnumerable<WorldBox> ColliderBoxes()
        {
            foreach (LocalBox box in _wholeBoxes)
                yield return new WorldBox { Center = box.Center, Half = box.Size * 0.5f, Rotation = box.Rotation, Layer = box.Layer };
            for (int n = 0; n < _wholeOtherCorners.Count; n++)
            {
                Vector3[] corners = _wholeOtherCorners[n];
                var bounds = new Bounds(corners[0], Vector3.zero);
                for (int i = 1; i < corners.Length; i++) bounds.Encapsulate(corners[i]);
                yield return new WorldBox { Center = bounds.center, Half = bounds.extents, Rotation = Quaternion.identity, Mesh = true, Layer = _wholeOtherLayers[n] };
            }
        }

        /// <summary>
        /// The boxes the game's support check overlaps, for the piece standing at this spot: each collider 0.15 m bigger
        /// each way. Measured with vc_build support (2026-10-18): a 2 x 0.2 x 2.83 m roof board's box is 1.15 x 0.25 x 1.57
        /// half; it had been taken as twice the board, and pieces that fell in the game were said to hold.
        /// </summary>
        public IEnumerable<WorldBox> SupportBoxesAt(Vector3 position, Quaternion rotation)
        {
            foreach (LocalBox box in _boxes)
                yield return new WorldBox { Center = position + rotation * box.Center, Half = box.Size * 0.5f + Vector3.one * 0.15f, Rotation = rotation * box.Rotation };
            foreach (Vector3[] corners in _otherCorners)
            {
                var world = new Bounds(position + rotation * corners[0], Vector3.zero);
                for (int i = 1; i < corners.Length; i++) world.Encapsulate(position + rotation * corners[i]);
                yield return new WorldBox { Center = world.center, Half = (world.size + Vector3.one * 0.3f) * 0.5f, Rotation = Quaternion.identity };
            }
        }

        private static bool IsWhole(Collider collider, Transform root, Vector3[] corners) =>
            ActiveUpTo(collider.transform, root) && SmallEnough(corners);

        /// <summary>The collider's object and every parent up to the prefab's root are switched on (a prefab isn't in a scene,
        /// so activeInHierarchy can't tell).</summary>
        internal static bool ActiveUpTo(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return true;
        }

        private static bool SmallEnough(Vector3[] corners)
        {
            var bounds = new Bounds(corners[0], Vector3.zero);
            for (int i = 1; i < corners.Length; i++) bounds.Encapsulate(corners[i]);
            return Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) <= LargestPart;
        }

        private static Vector3[] LocalCorners(Collider collider)
        {
            Vector3 center, half;
            switch (collider)
            {
                case BoxCollider box:
                    center = box.center;
                    half = box.size * 0.5f;
                    break;
                case MeshCollider mesh when mesh.sharedMesh != null:
                    center = mesh.sharedMesh.bounds.center;
                    half = mesh.sharedMesh.bounds.extents;
                    break;
                case SphereCollider sphere:
                    center = sphere.center;
                    half = Vector3.one * sphere.radius;
                    break;
                case CapsuleCollider capsule:
                    center = capsule.center;
                    half = Vector3.one * capsule.radius;
                    half[capsule.direction] = Mathf.Max(capsule.radius, capsule.height * 0.5f);
                    break;
                default:
                    return null;
            }
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
                corners[i] = center + new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
            return corners;
        }
    }
}
