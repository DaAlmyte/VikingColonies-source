using System.Globalization;
using System.Text;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_ground (test worlds): a grid of the ground's heights round a point, rows north to south (the old-huts upgrade
    /// test, 2026-10-19: did new lots, each levelled to its own height, leave banks a settler can't climb?).
    /// </summary>
    internal sealed class GroundCommand : ConsoleCommand
    {
        public override string Name => "vc_ground";
        public override string Help => "VikingColonies (test worlds): 'vc_ground <x> <z> [radius 10] [step 2]' logs the ground's heights round a point, " +
                                       "rows north to south, with the steepest step between neighbours (to the centimetre with a step under 1 m)";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_ground: test worlds only");
                return;
            }
            if (args == null || args.Length < 2 || !Read(args[0], out float cx) || !Read(args[1], out float cz))
            {
                BuildCommand.Print("vc_ground <x> <z> [radius] [step]");
                return;
            }
            float radius = args.Length > 2 && Read(args[2], out float r) ? r : 10f;
            float step = args.Length > 3 && Read(args[3], out float s) ? Mathf.Max(0.5f, s) : 2f;
            int n = Mathf.Clamp(Mathf.RoundToInt(radius / step), 1, 20);
            var heights = new float[2 * n + 1, 2 * n + 1];
            for (int i = -n; i <= n; i++)
                for (int k = -n; k <= n; k++)
                    heights[i + n, k + n] = ZoneSystem.instance.GetGroundHeight(new Vector3(cx + k * step, 0f, cz + i * step));
            float steepest = 0f;
            string where = "";
            for (int i = 0; i <= 2 * n; i++)
                for (int k = 0; k <= 2 * n; k++)
                {
                    if (k < 2 * n && Mathf.Abs(heights[i, k + 1] - heights[i, k]) > steepest)
                    {
                        steepest = Mathf.Abs(heights[i, k + 1] - heights[i, k]);
                        where = $"({cx + (k - n) * step:0}, {cz + (i - n) * step:0}) to the east";
                    }
                    if (i < 2 * n && Mathf.Abs(heights[i + 1, k] - heights[i, k]) > steepest)
                    {
                        steepest = Mathf.Abs(heights[i + 1, k] - heights[i, k]);
                        where = $"({cx + (k - n) * step:0}, {cz + (i - n) * step:0}) to the north";
                    }
                }
            BuildCommand.Print($"vc_ground: round ({cx:0}, {cz:0}), every {step:0.#} m, west to east; the steepest step {steepest:0.0} m over {step:0.#} m at {where}");
            for (int i = 2 * n; i >= 0; i--)   // north (bigger z) first
            {
                var row = new StringBuilder($"vc_ground: z {cz + (i - n) * step,6:0}:");
                for (int k = 0; k <= 2 * n; k++) row.Append(heights[i, k].ToString(step < 1f ? " 00.00" : " 00.0", CultureInfo.InvariantCulture));
                BuildCommand.Print(row.ToString());
            }
        }

        private static bool Read(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
