using System;

namespace VikingColonies.Core
{
    /// <summary>A point or direction in 3D (X east, Y up, Z north) — Core can't use Unity's Vector3.</summary>
    public struct Vec3
    {
        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);

        public float Length => (float)Math.Sqrt((double)X * X + (double)Y * Y + (double)Z * Z);

        /// <summary>The same point at height 0.</summary>
        public Vec3 Flat => new Vec3(X, 0f, Z);

        /// <summary>Length 1, or zero for a (nearly) zero vector — like Unity.</summary>
        public Vec3 Normalized
        {
            get
            {
                float length = Length;
                return length < 1e-5f ? Zero : new Vec3(X / length, Y / length, Z / length);
            }
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, float k) => new Vec3(a.X * k, a.Y * k, a.Z * k);

        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static float Distance(Vec3 a, Vec3 b) => (a - b).Length;

        /// <summary>Angle between two directions in degrees; 0 when either is (nearly) zero — like Unity's Vector3.Angle.</summary>
        public static float Angle(Vec3 a, Vec3 b)
        {
            double denominator = Math.Sqrt((double)Dot(a, a) * Dot(b, b));
            if (denominator < 1e-15) return 0f;
            double cos = Math.Max(-1.0, Math.Min(1.0, Dot(a, b) / denominator));
            return (float)(Math.Acos(cos) * 180.0 / Math.PI);
        }

        public override string ToString() => $"({X:0.##}, {Y:0.##}, {Z:0.##})";
    }

    /// <summary>A rotation, laid out and multiplied exactly like Unity's Quaternion.</summary>
    public struct Quat
    {
        public Quat(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float W { get; }

        public static readonly Quat Identity = new Quat(0f, 0f, 0f, 1f);

        /// <summary>A turn around the vertical axis (Unity: Quaternion.Euler(0, degrees, 0)).</summary>
        public static Quat Yaw(float degrees)
        {
            double half = degrees * Math.PI / 360.0;
            return new Quat(0f, (float)Math.Sin(half), 0f, (float)Math.Cos(half));
        }

        public Quat Normalized
        {
            get
            {
                double length = Math.Sqrt((double)X * X + (double)Y * Y + (double)Z * Z + (double)W * W);
                return length < 1e-9 ? Identity : new Quat((float)(X / length), (float)(Y / length), (float)(Z / length), (float)(W / length));
            }
        }

        /// <summary>The opposite turn (for rotations of length 1).</summary>
        public Quat Inverse => new Quat(-X, -Y, -Z, W);

        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z,
            a.W * b.Z + a.Z * b.W + a.X * b.Y - a.Y * b.X,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        /// <summary>Turns a point (Unity: rotation * point).</summary>
        public Vec3 Rotate(Vec3 v)
        {
            float x2 = X * 2f, y2 = Y * 2f, z2 = Z * 2f;
            float xx = X * x2, yy = Y * y2, zz = Z * z2;
            float xy = X * y2, xz = X * z2, yz = Y * z2;
            float wx = W * x2, wy = W * y2, wz = W * z2;
            return new Vec3(
                (1f - (yy + zz)) * v.X + (xy - wz) * v.Y + (xz + wy) * v.Z,
                (xy + wz) * v.X + (1f - (xx + zz)) * v.Y + (yz - wx) * v.Z,
                (xz - wy) * v.X + (yz + wx) * v.Y + (1f - (xx + yy)) * v.Z);
        }

        /// <summary>Angle between two rotations in degrees (Unity: Quaternion.Angle).</summary>
        public static float Angle(Quat a, Quat b)
        {
            double dot = Math.Min(Math.Abs((double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z + (double)a.W * b.W), 1.0);
            return dot > 0.999999 ? 0f : (float)(Math.Acos(dot) * 2.0 * 180.0 / Math.PI);
        }
    }
}
