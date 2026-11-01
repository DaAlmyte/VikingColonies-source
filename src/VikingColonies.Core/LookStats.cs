using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>
    /// How a painted texture looks, in numbers (the Valheim look pass, 27 Oct 2026): brightness and its spread, colour
    /// strength, how many hues, how grainy, the share of dark and light texels, over the texels a model uses. vc_look
    /// measures the game's own pieces with it at run time and tools/assets/look.py measures ours the same way (the same
    /// formulas; LookStatsTests and tools/assets/tests check one fixture gives the same numbers in both), so the two can
    /// be compared. Pixels are sRGB bytes, RGBA, row 0 at the bottom (as Unity reads them back).
    /// </summary>
    public sealed class LookStats
    {
        public int Texels;
        public double Luma;          // mean brightness, 0..1 (Rec. 709 weights on the sRGB values)
        public double LumaSpread;    // its standard deviation
        public double Saturation;    // mean HSV saturation, 0..1
        public double HueMean;       // degrees, of the coloured texels (saturation over 0.15, value over 0.1)
        public double HueSpread;     // circular standard deviation of their hue, degrees (0: one hue)
        public double Grain;         // mean brightness step between neighbouring texels (painted: low; noise: high)
        public double Dark;          // share of texels darker than 0.15
        public double Light;         // share of texels lighter than 0.7
        public double R, G, B;       // the mean colour, 0..1

        public const double DarkBelow = 0.15, LightAbove = 0.7, ColouredSaturation = 0.15, ColouredValue = 0.1;

        /// <summary>
        /// The numbers over the texels with alpha at least 128 and, when `mask` is given (width x height, row 0 at the
        /// bottom), set in it. Null when no texel counts.
        /// </summary>
        public static LookStats Measure(byte[] rgba, int width, int height, bool[] mask = null)
        {
            if (rgba == null || rgba.Length < width * height * 4) throw new ArgumentException("rgba must hold width x height x 4 bytes");
            if (mask != null && mask.Length < width * height) throw new ArgumentException("mask must hold width x height");
            var luma = new double[width * height];
            var used = new bool[width * height];
            double sum = 0, sumSq = 0, sat = 0, r = 0, g = 0, b = 0, hx = 0, hy = 0;
            int n = 0, coloured = 0, dark = 0, light = 0;
            for (int i = 0; i < width * height; i++)
            {
                if (rgba[i * 4 + 3] < 128 || (mask != null && !mask[i])) continue;
                double cr = rgba[i * 4] / 255.0, cg = rgba[i * 4 + 1] / 255.0, cb = rgba[i * 4 + 2] / 255.0;
                double l = 0.2126 * cr + 0.7152 * cg + 0.0722 * cb;
                luma[i] = l;
                used[i] = true;
                n++;
                sum += l;
                sumSq += l * l;
                r += cr; g += cg; b += cb;
                if (l < DarkBelow) dark++;
                if (l > LightAbove) light++;
                double max = Math.Max(cr, Math.Max(cg, cb)), min = Math.Min(cr, Math.Min(cg, cb));
                double s = max > 0 ? (max - min) / max : 0;
                sat += s;
                if (s > ColouredSaturation && max > ColouredValue)
                {
                    double hue = Hue(cr, cg, cb, max, min) * Math.PI / 180.0;
                    hx += Math.Cos(hue);
                    hy += Math.Sin(hue);
                    coloured++;
                }
            }
            if (n == 0) return null;
            double steps = 0;
            int pairs = 0;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (!used[i]) continue;
                    if (x + 1 < width && used[i + 1]) { steps += Math.Abs(luma[i] - luma[i + 1]); pairs++; }
                    if (y + 1 < height && used[i + width]) { steps += Math.Abs(luma[i] - luma[i + width]); pairs++; }
                }
            double mean = sum / n;
            var stats = new LookStats
            {
                Texels = n,
                Luma = mean,
                LumaSpread = Math.Sqrt(Math.Max(0, sumSq / n - mean * mean)),
                Saturation = sat / n,
                Grain = pairs > 0 ? steps / pairs : 0,
                Dark = (double)dark / n,
                Light = (double)light / n,
                R = r / n, G = g / n, B = b / n,
            };
            if (coloured > 0)
            {
                double length = Math.Sqrt(hx * hx + hy * hy) / coloured;
                stats.HueMean = (Math.Atan2(hy, hx) * 180.0 / Math.PI + 360.0) % 360.0;
                stats.HueSpread = length >= 1 ? 0 : Math.Sqrt(-2 * Math.Log(Math.Max(length, 1e-9))) * 180.0 / Math.PI;
            }
            return stats;
        }

        /// <summary>HSV hue in degrees, 0..360.</summary>
        public static double Hue(double r, double g, double b, double max, double min)
        {
            double d = max - min;
            if (d <= 0) return 0;
            double h;
            if (max == r) h = (g - b) / d % 6;
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
            return h < 0 ? h + 360 : h;
        }

        /// <summary>
        /// The texels a mesh's UVs cover (each triangle filled, texel centres inside it), width x height, row 0 at the
        /// bottom. `uv` holds u, v pairs per vertex; `triangles` three vertex indices each. UVs outside 0..1 wrap.
        /// </summary>
        public static bool[] UvMask(float[] uv, int[] triangles, int width, int height)
        {
            var mask = new bool[width * height];
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                double ax = Wrap(uv[triangles[t] * 2]) * width, ay = Wrap(uv[triangles[t] * 2 + 1]) * height;
                double bx = Wrap(uv[triangles[t + 1] * 2]) * width, by = Wrap(uv[triangles[t + 1] * 2 + 1]) * height;
                double cx = Wrap(uv[triangles[t + 2] * 2]) * width, cy = Wrap(uv[triangles[t + 2] * 2 + 1]) * height;
                double area = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);
                int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
                int x1 = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
                int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy))));
                int y1 = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
                if (Math.Abs(area) < 1e-12)
                {
                    // a sliver: mark the texels under its corners
                    Mark(mask, width, height, ax, ay); Mark(mask, width, height, bx, by); Mark(mask, width, height, cx, cy);
                    continue;
                }
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        double px = x + 0.5, py = y + 0.5;
                        double w0 = (bx - px) * (cy - py) - (cx - px) * (by - py);
                        double w1 = (cx - px) * (ay - py) - (ax - px) * (cy - py);
                        double w2 = (ax - px) * (by - py) - (bx - px) * (ay - py);
                        if (area > 0 ? (w0 >= 0 && w1 >= 0 && w2 >= 0) : (w0 <= 0 && w1 <= 0 && w2 <= 0)) mask[y * width + x] = true;
                    }
                Mark(mask, width, height, ax, ay); Mark(mask, width, height, bx, by); Mark(mask, width, height, cx, cy);
            }
            return mask;
        }

        private static void Mark(bool[] mask, int width, int height, double x, double y)
        {
            int ix = Math.Min(width - 1, Math.Max(0, (int)Math.Floor(x)));
            int iy = Math.Min(height - 1, Math.Max(0, (int)Math.Floor(y)));
            mask[iy * width + ix] = true;
        }

        private static double Wrap(float v)
        {
            if (v >= 0f && v <= 1f) return v;
            double w = v - Math.Floor(v);
            return w;
        }

        /// <summary>
        /// Texture pixels per metre of surface: the square root of (UV area in pixels) over (surface area in m²), summed
        /// over the triangles. `positions` x, y, z per vertex in metres; `scaleU`, `scaleV` the material's tiling.
        /// </summary>
        public static double TexelsPerMetre(float[] positions, float[] uv, int[] triangles, int width, int height,
            double scaleU = 1, double scaleV = 1)
        {
            double uvArea = 0, area = 0;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                double ux = (uv[b * 2] - uv[a * 2]) * width * scaleU, uy = (uv[b * 2 + 1] - uv[a * 2 + 1]) * height * scaleV;
                double vx = (uv[c * 2] - uv[a * 2]) * width * scaleU, vy = (uv[c * 2 + 1] - uv[a * 2 + 1]) * height * scaleV;
                uvArea += Math.Abs(ux * vy - uy * vx) / 2;
                double e1x = positions[b * 3] - positions[a * 3], e1y = positions[b * 3 + 1] - positions[a * 3 + 1], e1z = positions[b * 3 + 2] - positions[a * 3 + 2];
                double e2x = positions[c * 3] - positions[a * 3], e2y = positions[c * 3 + 1] - positions[a * 3 + 1], e2z = positions[c * 3 + 2] - positions[a * 3 + 2];
                double cx = e1y * e2z - e1z * e2y, cy = e1z * e2x - e1x * e2z, cz = e1x * e2y - e1y * e2x;
                area += Math.Sqrt(cx * cx + cy * cy + cz * cz) / 2;
            }
            return area > 0 ? Math.Sqrt(uvArea / area) : 0;
        }

        /// <summary>At most this many points are compared by Lopsided (every k-th of the sorted ones past it).</summary>
        public const int LopsidedPoints = 3000;

        /// <summary>
        /// How far a shape is from mirror-even (0: the same either side), as tools/assets/look.py's lopsided: its points
        /// (x, y, z per vertex; rounded to the millimetre, duplicates dropped, sorted) mirrored through their box's middle
        /// across X, and again across Z; for each, the mean distance from a mirrored point to the nearest real one, over
        /// the box's diagonal. The smaller of the two.
        /// </summary>
        public static double Lopsided(float[] positions)
        {
            var set = new SortedSet<Tuple<double, double, double>>();
            for (int i = 0; i + 2 < positions.Length; i += 3)
                set.Add(Tuple.Create(Round(positions[i]), Round(positions[i + 1]), Round(positions[i + 2])));
            var pts = new List<double[]>();
            int step = set.Count > LopsidedPoints ? (set.Count + LopsidedPoints - 1) / LopsidedPoints : 1;
            int k = 0;
            foreach (Tuple<double, double, double> p in set)
                if (k++ % step == 0) pts.Add(new[] { p.Item1, p.Item2, p.Item3 });
            if (pts.Count == 0) return 0;
            var lo = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
            var hi = new[] { double.MinValue, double.MinValue, double.MinValue };
            foreach (double[] p in pts)
                for (int a = 0; a < 3; a++) { lo[a] = Math.Min(lo[a], p[a]); hi[a] = Math.Max(hi[a], p[a]); }
            double diag = Math.Sqrt((hi[0] - lo[0]) * (hi[0] - lo[0]) + (hi[1] - lo[1]) * (hi[1] - lo[1]) + (hi[2] - lo[2]) * (hi[2] - lo[2]));
            if (diag == 0) return 0;
            double best = double.MaxValue;
            foreach (int axis in new[] { 0, 2 })
            {
                double mid = (lo[axis] + hi[axis]) / 2, total = 0;
                foreach (double[] p in pts)
                {
                    double mx = axis == 0 ? 2 * mid - p[0] : p[0], mz = axis == 2 ? 2 * mid - p[2] : p[2], near = double.MaxValue;
                    foreach (double[] q in pts)
                    {
                        double dx = mx - q[0], dy = p[1] - q[1], dz = mz - q[2];
                        near = Math.Min(near, dx * dx + dy * dy + dz * dz);
                    }
                    total += Math.Sqrt(near);
                }
                best = Math.Min(best, total / pts.Count / diag);
            }
            return best;
        }

        private static double Round(float v) => Math.Round((double)v, 3, MidpointRounding.ToEven);

        /// <summary>
        /// A normal map's strength: the mean length of its tilt (x, y) over the texels, 0 for flat. Unity's normal maps
        /// come back either with x in red or, packed (DXT5nm), red near 1 and x in alpha.
        /// </summary>
        public static double NormalStrength(byte[] rgba, int width, int height)
        {
            int n = width * height;
            if (n == 0) return 0;
            double red = 0;
            for (int i = 0; i < n; i++) red += rgba[i * 4];
            bool packed = red / n > 0.95 * 255;
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                double x = (packed ? rgba[i * 4 + 3] : rgba[i * 4]) / 255.0 * 2 - 1;
                double y = rgba[i * 4 + 1] / 255.0 * 2 - 1;
                sum += Math.Min(1, Math.Sqrt(x * x + y * y));
            }
            return sum / n;
        }

        public string ToJson()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"texels\": {0}, \"luma\": {1:0.####}, \"luma_spread\": {2:0.####}, \"saturation\": {3:0.####}, \"hue_mean\": {4:0.#}, " +
                "\"hue_spread\": {5:0.#}, \"grain\": {6:0.####}, \"dark\": {7:0.####}, \"light\": {8:0.####}, \"mean_rgb\": [{9:0.###}, {10:0.###}, {11:0.###}]}}",
                Texels, Luma, LumaSpread, Saturation, HueMean, HueSpread, Grain, Dark, Light, R, G, B);
        }
    }
}
