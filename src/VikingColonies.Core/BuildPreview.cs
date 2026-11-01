using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>A preview picture's camera: where it stands and what it looks at, in the design's space (x right, y up, z front).</summary>
    public sealed class PreviewView
    {
        public PreviewView(string name, Vec3 eye, Vec3 look)
        {
            Name = name;
            Eye = eye;
            Look = look;
        }

        public string Name { get; }
        public Vec3 Eye { get; }
        public Vec3 Look { get; }
    }

    /// <summary>
    /// vc_preview's set angles (the build tools, 2026-10-25; the player's ask: a design turned into pictures from fixed
    /// angles, front, back, both sides, three-quarter, above, inside). Each outside view stands back just far enough for
    /// the design's whole box to fill the picture, so a long hall's gable is seen close and its side from far enough back.
    /// </summary>
    public static class PreviewViews
    {
        /// <summary>The views taken again with every piece in its support colour (the load pictures).</summary>
        public static readonly string[] LoadViews = { "frontright", "backleft", "above", "inside" };

        /// <summary>How much room is left round the design in the picture.</summary>
        public const float Margin = 1.08f;
        /// <summary>The inside view's eye above the ground (y = 0 is the ground the design stands on).</summary>
        public const float EyeHeight = 1.7f;

        /// <summary>How far in front of a hut's chest the inside view stands at most, and the share of the way to the design's front.</summary>
        public const float ChestView = 4.5f;
        public const float ChestViewShare = 0.6f;
        /// <summary>How far in front of the chest the inside view stands at least (a chest at the design's front edge).</summary>
        public const float ChestViewMin = 1.5f;

        /// <summary>
        /// The views of a design whose pieces fill the box min to max, for a camera of that vertical field of view (degrees)
        /// and width over height. A hut's design has its chest (its chest line, in the design's space): the inside view then
        /// stands in front of it looking at it, inside the hut (the box's middle of a farm with its field beside it was out
        /// in the field, and the view looked at a wall, 26 Oct). Without one (a showcase), down the box's long way.
        /// </summary>
        public static List<PreviewView> For(Vec3 min, Vec3 max, float verticalFov, float aspect, Vec3? chest = null)
        {
            var centre = new Vec3((min.X + max.X) / 2f, (min.Y + max.Y) / 2f, (min.Z + max.Z) / 2f);
            double tanV = Math.Tan(verticalFov * Math.PI / 360.0), tanH = tanV * Math.Max(0.1f, aspect);
            var views = new List<PreviewView>
            {
                Outside("front", 0f, 10f, min, max, centre, tanV, tanH),
                Outside("back", 180f, 10f, min, max, centre, tanV, tanH),
                Outside("left", 270f, 10f, min, max, centre, tanV, tanH),
                Outside("right", 90f, 10f, min, max, centre, tanV, tanH),
                Outside("frontright", 45f, 25f, min, max, centre, tanV, tanH),
                Outside("backleft", 225f, 25f, min, max, centre, tanV, tanH),
                Outside("above", 20f, 70f, min, max, centre, tanV, tanH),
            };
            float eye = Math.Max(min.Y, 0f) + EyeHeight;
            float dx = max.X - min.X, dz = max.Z - min.Z;
            if (chest.HasValue)
            {
                Vec3 c = chest.Value;
                float back = Math.Max(ChestViewMin, Math.Min(ChestView, ChestViewShare * Math.Max(0f, max.Z - c.Z)));
                float onFloor = Math.Max(c.Y, 0f) + EyeHeight;   // the chest's floor (an upper one too)
                views.Add(new PreviewView("inside", new Vec3(c.X, onFloor, c.Z + back), new Vec3(c.X, onFloor - 0.9f, c.Z - 1f)));
                return views;
            }
            views.Add(dz >= dx
                ? new PreviewView("inside", new Vec3(centre.X, eye, max.Z - 0.2f * dz), new Vec3(centre.X, eye, min.Z))
                : new PreviewView("inside", new Vec3(max.X - 0.2f * dx, eye, centre.Z), new Vec3(min.X, eye, centre.Z)));
            return views;
        }

        /// <summary>azimuth: 0 from the front (+z), 90 from the right (+x); elevation: degrees up.</summary>
        private static PreviewView Outside(string name, float azimuth, float elevation, Vec3 min, Vec3 max, Vec3 centre, double tanV, double tanH)
        {
            double a = azimuth * Math.PI / 180.0, e = elevation * Math.PI / 180.0;
            var toEye = new Vec3((float)(Math.Sin(a) * Math.Cos(e)), (float)Math.Sin(e), (float)(Math.Cos(a) * Math.Cos(e)));
            Vec3 forward = toEye * -1f;
            Vec3 right = Cross(forward, new Vec3(0, 1, 0)).Normalized;
            Vec3 up = Cross(right, forward);
            // Stand back until every corner is on screen: a corner needs the camera this far from the centre.
            double distance = 3.0;
            foreach (Vec3 corner in Corners(min, max))
            {
                Vec3 d = corner - centre;
                double need = Math.Max(Math.Abs(Vec3.Dot(d, right)) / tanH, Math.Abs(Vec3.Dot(d, up)) / tanV) * Margin + Vec3.Dot(d, toEye);
                distance = Math.Max(distance, need);
            }
            return new PreviewView(name, centre + toEye * (float)distance, centre);
        }

        private static IEnumerable<Vec3> Corners(Vec3 a, Vec3 b)
        {
            foreach (float x in new[] { a.X, b.X })
            foreach (float y in new[] { a.Y, b.Y })
            foreach (float z in new[] { a.Z, b.Z })
                yield return new Vec3(x, y, z);
        }

        private static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    }

    /// <summary>
    /// When a preview's support has settled: the game works each piece's support out again every so often, and pieces
    /// fall as it does. Settled when the count standing and the sum of support stay the same for <see cref="StableSamples"/>
    /// samples in a row (a second apart), after <see cref="MinSeconds"/> at least; it gives up after <see cref="MaxSeconds"/>.
    /// </summary>
    public sealed class PreviewSettle
    {
        public const int StableSamples = 4;
        public const float MinSeconds = 5f;
        public const float MaxSeconds = 120f;

        private float _start = float.NaN;
        private int _standing = -1, _same;
        private float _sum;

        public bool Settled { get; private set; }
        public bool GaveUp { get; private set; }

        public void Add(float time, int standing, float supportSum)
        {
            if (float.IsNaN(_start)) _start = time;
            else if (standing == _standing && Math.Abs(supportSum - _sum) < Math.Max(1f, Math.Abs(_sum) * 0.0005f)) _same++;
            else _same = 0;
            _standing = standing;
            _sum = supportSum;
            float waited = time - _start;
            bool steady = _same >= StableSamples;
            if (waited >= MaxSeconds)
            {
                GaveUp = !steady;
                Settled = true;
                return;
            }
            Settled = steady && waited >= MinSeconds;
        }
    }

    /// <summary>
    /// A preview's design name: the file preview/&lt;name&gt;.blueprint, so only letters, digits, '_' and '-' (no paths), and
    /// at most <see cref="Longest"/>: its pictures are named p-&lt;name&gt;-load-frontright, and a picture's name is cut at
    /// 40 letters (ShotFile.MaxLabel).
    /// </summary>
    public static class PreviewName
    {
        public const int Longest = 22;

        public static bool IsSafe(string name) =>
            !string.IsNullOrEmpty(name) && name.Length <= Longest &&
            name.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-');
    }

    /// <summary>
    /// The load file vc_preview writes (preview/&lt;name&gt;-load.tsv): a line a piece of the design, tab-separated, with a dot
    /// for decimals; a piece that doesn't stand leaves its numbers empty.
    /// </summary>
    public static class PreviewLoad
    {
        public const string Header = "index\tprefab\tx\ty\tz\tstands\tsupport\tneeds\tmost\tcolour";

        public static string Line(int index, string prefab, Vec3 at, bool stands, float? support, float? needs, float? most, float? colour) =>
            string.Join("\t", index.ToString(CultureInfo.InvariantCulture), prefab, N(at.X, "0.##"), N(at.Y, "0.##"), N(at.Z, "0.##"),
                stands ? "1" : "0", N(support, "0.#"), N(needs, "0.#"), N(most, "0.#"), N(colour, "0.###"));

        private static string N(float? v, string format) => v.HasValue ? v.Value.ToString(format, CultureInfo.InvariantCulture) : "";
    }
}
