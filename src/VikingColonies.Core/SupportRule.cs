using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>A building material's support numbers, as the game gives them (WearNTear.GetMaterialProperties).</summary>
    public struct SupportMaterial
    {
        public SupportMaterial(float maxSupport, float minSupport, float horizontalLoss, float verticalLoss)
        {
            MaxSupport = maxSupport;
            MinSupport = minSupport;
            HorizontalLoss = horizontalLoss;
            VerticalLoss = verticalLoss;
        }

        public float MaxSupport { get; }
        public float MinSupport { get; }
        public float HorizontalLoss { get; }
        public float VerticalLoss { get; }
    }

    /// <summary>A piece touching the one being checked.</summary>
    public struct SupportContact
    {
        public SupportContact(float neighbourSupport, float distance, Vec3 pointOffset)
        {
            NeighbourSupport = neighbourSupport;
            Distance = distance;
            PointOffset = pointOffset;
        }

        public float NeighbourSupport { get; }
        /// <summary>See <see cref="SupportRule.ContactDistance"/>.</summary>
        public float Distance { get; }
        /// <summary>Where the neighbour holds this piece, relative to this piece's centre of mass.</summary>
        public Vec3 PointOffset { get; }
    }

    /// <summary>The game's support rule (WearNTear.UpdateSupport), for checking a piece before it is placed.</summary>
    public static class SupportRule
    {
        public static float Evaluate(SupportMaterial material, bool grounded, IReadOnlyList<SupportContact> contacts)
        {
            if (grounded) return material.MaxSupport;
            float h = material.HorizontalLoss, v = material.VerticalLoss;
            float best = 0f;
            var points = new List<Vec3>();
            var values = new List<float>();
            foreach (SupportContact c in contacts)
            {
                float s = c.NeighbourSupport, d = c.Distance;
                best = Math.Max(best, s - h * d * s);
                if (c.PointOffset.Y >= 0.05f) continue;   // only points below (or level with) the centre of mass carry it
                Vec3 n = c.PointOffset.Normalized;
                if (n.Y < 0f)
                {
                    float t = (float)(Math.Acos(1.0 - Math.Abs(n.Y)) / (Math.PI / 2.0));
                    float loss = h + (v - h) * Math.Min(1f, Math.Max(0f, t));
                    best = Math.Max(best, s - loss * d * s);
                }
                points.Add(c.PointOffset.Flat);
                values.Add(s - v * d * s);
            }
            for (int i = 0; i < points.Count - 1; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    float average = (values[i] + values[j]) * 0.5f;
                    if (average > best && Vec3.Angle(points[i], points[j]) >= 100f) best = average;
                }
            }
            return Math.Min(best, material.MaxSupport);
        }

        public static bool Holds(SupportMaterial material, float support) => support >= material.MinSupport;

        /// <summary>The game measures to the neighbour's centre of mass, or to its position when that is nearer.</summary>
        public static float ContactDistance(Vec3 myCom, Vec3 otherCom, Vec3 otherPosition, bool forceCorrectCom)
        {
            float toCom = Vec3.Distance(myCom, otherCom) + 0.1f;
            float toPosition = Vec3.Distance(myCom, otherPosition) + 0.1f;
            return toPosition < toCom && !forceCorrectCom ? toPosition : toCom;
        }
    }
}
