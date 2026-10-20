using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>
    /// Which settlers' words show above their heads: only those whose head is on the screen, and the nearest few (the
    /// player, 2026-10-19: words "stack up in weird places on the screen" when settlers are just out of view).
    /// </summary>
    public static class WordsOnScreen
    {
        /// <summary>A head this close to the screen's edge (a share of its width or height) counts as off it.</summary>
        public const float Margin = 0.02f;

        /// <summary>At most this many settlers' words at once.</summary>
        public const int MostAtOnce = 8;

        /// <summary>
        /// Whether a head at this viewport point shows its words: in front of the camera (<paramref name="depth"/> above 0)
        /// and inside the screen, a thin margin in from each edge. Viewport x and y run 0 to 1 across the screen.
        /// </summary>
        public static bool Shows(float viewportX, float viewportY, float depth) =>
            depth > 0f && viewportX >= Margin && viewportX <= 1f - Margin && viewportY >= Margin && viewportY <= 1f - Margin;

        /// <summary>The indexes of the nearest <paramref name="most"/> of these distances, nearest first.</summary>
        public static List<int> Nearest(IReadOnlyList<float> distances, int most) =>
            Enumerable.Range(0, distances.Count).OrderBy(i => distances[i]).ThenBy(i => i).Take(most).ToList();
    }
}
