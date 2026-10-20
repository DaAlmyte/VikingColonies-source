using UnityEngine;
using VikingColonies.Core;

namespace VikingColonies.Construction
{
    /// <summary>Converts between Unity's vectors and Core's.</summary>
    internal static class CoreVectors
    {
        public static Vec3 V(Vector3 v) => new Vec3(v.x, v.y, v.z);
        public static Vector3 U(Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Quat Q(Quaternion q) => new Quat(q.x, q.y, q.z, q.w);
        public static Quaternion U(Quat q) => new Quaternion(q.X, q.Y, q.Z, q.W);
    }
}
