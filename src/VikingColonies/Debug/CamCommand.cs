using System.Globalization;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_cam (hut designs, 2026-10-18): the camera at a spot in a hut's own space, looking at another, for pictures of a
    /// building's inside and its details (vc_view only stands the player outside). The camera is set just before it draws
    /// (Camera.onPreCull), after the game has placed it, so nothing of the game is changed; the HUD is hidden meanwhile.
    /// </summary>
    internal sealed class CamCommand : ConsoleCommand
    {
        public override string Name => "vc_cam";
        public override string Help => "VikingColonies (test worlds): 'vc_cam <hut> <x> <y> <z> [<x> <y> <z>]' puts the camera at that spot of the nearest " +
                                       "hut of that kind (metres from its chest: x to its right, y up, z towards its door), looking at the second spot " +
                                       "(default: 1 m above the chest), with the HUD hidden; 'vc_cam off' gives the camera back";
        public override bool IsCheat => true;

        private static bool s_on, s_hooked;
        private static Vector3 s_at, s_look;

        public override void Run(string[] args)
        {
            args = args ?? new string[0];
            if (args.Length > 0 && args[0].ToLowerInvariant() == "off")
            {
                Off();
                BuildCommand.Print("vc_cam: the camera is back");
                return;
            }
            Player p = Player.m_localPlayer;
            BuildingType type = args.Length > 0 ? DevHelpers.TypeByWord(args[0]) : null;
            if (p == null || type == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()) || args.Length < 4)
            {
                BuildCommand.Print("vc_cam: in a test world, 'vc_cam <hut> <x> <y> <z> [<x> <y> <z>]' or 'vc_cam off'");
                return;
            }
            ColonyBuilding b = ColonyBuilding.All.Where(x => x != null && x.IsValid && x.TypeId == type.Id)
                .OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
            if (b == null)
            {
                BuildCommand.Print($"vc_cam: no {type.Id} loaded here");
                return;
            }
            float[] n = args.Skip(1).Take(6).Select(a => float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : float.NaN).ToArray();
            if (n.Any(float.IsNaN) || (n.Length != 3 && n.Length != 6))
            {
                BuildCommand.Print("vc_cam: three numbers for the spot, and three more for where it looks");
                return;
            }
            s_at = b.transform.TransformPoint(new Vector3(n[0], n[1], n[2]));
            s_look = b.transform.TransformPoint(n.Length == 6 ? new Vector3(n[3], n[4], n[5]) : Vector3.up);
            s_on = true;
            if (!s_hooked)
            {
                Camera.onPreCull += Place;
                s_hooked = true;
            }
            if (Hud.instance != null) Hud.instance.m_userHidden = true;
            BuildCommand.Print($"vc_cam: the camera at ({n[0]:0.##}, {n[1]:0.##}, {n[2]:0.##}) of the {type.Id} {b.Id.Substring(0, System.Math.Min(8, b.Id.Length))} (level {b.Level})");
        }

        /// <summary>Leaving the world gives the camera back too.</summary>
        public static void Off()
        {
            if (!s_on) return;
            s_on = false;
            if (Hud.instance != null) Hud.instance.m_userHidden = false;
        }

        private static void Place(Camera camera)
        {
            if (!s_on) return;
            if (Player.m_localPlayer == null)
            {
                Off();
                return;
            }
            if (camera == null || GameCamera.instance == null || camera.gameObject != GameCamera.instance.gameObject) return;
            camera.transform.position = s_at;
            Vector3 dir = s_look - s_at;
            if (dir.sqrMagnitude > 0.0001f) camera.transform.rotation = Quaternion.LookRotation(dir);
        }
    }
}
