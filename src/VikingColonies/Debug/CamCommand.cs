using System.Globalization;
using System.Linq;
using HarmonyLib;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_cam (hut designs, 2026-10-18): the camera at a spot in a hut's own space, looking at another, for pictures of a
    /// building's inside and its details (vc_view only stands the player outside). The camera is set just before it draws
    /// (Camera.onPreCull), after the game has placed it, so nothing of the game is changed; the HUD is hidden meanwhile.
    /// 'vc_cam follow' (1.4.2, for the store pictures) keeps the camera on a settler as they walk and work.
    /// </summary>
    internal sealed class CamCommand : ConsoleCommand
    {
        public override string Name => "vc_cam";
        public override string Help => "VikingColonies (test worlds): 'vc_cam <hut> <x> <y> <z> [<x> <y> <z>]' puts the camera at that spot of the nearest " +
                                       "hut of that kind (metres from its chest: x to its right, y up, z towards its door), looking at the second spot " +
                                       "(default: 1 m above the chest), with the HUD hidden; 'vc_cam at <X> <Z> <x> <y> <z> [<x> <y> <z>]' the same from the ground at X Z of the world, " +
                                       "facing north (a design vc_stamp put up, which has no chest); 'vc_cam follow <settler name|job|jobless|any|talking> [distance 6] [height 2] [angle 30]' " +
                                       "keeps the camera on the nearest such settler ('talking': whoever's small talk showed last; angle 0 in front of them, 90 to their right, 180 behind), " +
                                       "turning with them; a last word 'hud' (or 'tags') keeps the screen, with name tags and speech; 'vc_cam off' gives the camera back";
        public override bool IsCheat => true;

        private static bool s_on, s_hooked, s_keepHud;

        /// <summary>The camera is ours now (the words above settlers' heads are logged meanwhile, for a picture run to wait on).</summary>
        internal static bool IsOn => s_on;
        private static Vector3 s_at, s_look;
        private static Citizen s_follow;
        private static bool s_following;
        private static string s_followName = "";
        private static float s_distance, s_height, s_angle, s_yaw;
        private static int s_poseFrame = -1;
        private static Vector3 s_followAt, s_followLook;
        /// <summary>How fast the camera turns after the settler (per second): a step aside doesn't swing it.</summary>
        private const float TurnRate = 2.5f;

        public override void Run(string[] args)
        {
            args = args ?? new string[0];
            s_keepHud = CamFollow.KeepsHud(args);
            if (s_keepHud) args = args.Take(args.Length - 1).ToArray();
            if (args.Length > 0 && args[0].ToLowerInvariant() == "off")
            {
                Off();
                BuildCommand.Print("vc_cam: the camera is back");
                return;
            }
            Player p = Player.m_localPlayer;
            if (args.Length > 0 && args[0].ToLowerInvariant() == "follow")
            {
                Follow(p, args);
                return;
            }
            if (args.Length > 0 && args[0].ToLowerInvariant() == "at")
            {
                At(p, args);
                return;
            }
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
            s_follow = null;
            s_following = false;
            TurnOn();
            BuildCommand.Print($"vc_cam: the camera at ({n[0]:0.##}, {n[1]:0.##}, {n[2]:0.##}) of the {type.Id} {b.Id.Substring(0, System.Math.Min(8, b.Id.Length))} (level {b.Level})");
        }

        private static void TurnOn()
        {
            s_on = true;
            if (!s_hooked)
            {
                Camera.onPreCull += Place;
                s_hooked = true;
            }
            if (Hud.instance != null) Hud.instance.m_userHidden = !s_keepHud;
        }

        /// <summary>
        /// vc_cam at X Z: the spots are metres from the ground at X Z of the world, facing north (x east, z north): the
        /// gallery's stamped designs (vc_stamp) stand there with their front to the north and no chest to aim from.
        /// </summary>
        private static void At(Player p, string[] args)
        {
            float[] n = args.Skip(1).Take(8).Select(a => float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : float.NaN).ToArray();
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()) || n.Any(float.IsNaN) || (n.Length != 5 && n.Length != 8))
            {
                BuildCommand.Print("vc_cam: in a test world, 'vc_cam at <X> <Z> <x> <y> <z> [<x> <y> <z>]'");
                return;
            }
            var origin = new Vector3(n[0], 0f, n[1]);
            origin.y = ZoneSystem.instance.GetGroundHeight(origin);
            s_at = origin + new Vector3(n[2], n[3], n[4]);
            s_look = origin + (n.Length == 8 ? new Vector3(n[5], n[6], n[7]) : Vector3.up);
            s_follow = null;
            s_following = false;
            TurnOn();
            BuildCommand.Print($"vc_cam: the camera at ({n[2]:0.##}, {n[3]:0.##}, {n[4]:0.##}) from the ground at ({n[0]:0.#}, {n[1]:0.#})");
        }

        /// <summary>vc_cam follow: the nearest settler of that name or job, logged once (name, job, what they're doing).</summary>
        private static void Follow(Player p, string[] args)
        {
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()) || args.Length < 2)
            {
                BuildCommand.Print("vc_cam: in a test world, 'vc_cam follow <settler name|job> [distance] [height] [angle]'");
                return;
            }
            float[] n = args.Skip(2).Take(3).Select(a => float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : float.NaN).ToArray();
            if (n.Any(float.IsNaN))
            {
                BuildCommand.Print("vc_cam: the distance, height and angle are numbers (metres, metres, degrees)");
                return;
            }
            Citizen c = CamFollow.FollowsLastSpeaker(args[1]) ? Gui.SettlerWords.LastSpeaker
                : Citizen.All.Where(x => x != null && CamFollow.Matches(args[1], x.CitizenName, x.Record.Job))
                    .OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
            if (c == null)
            {
                BuildCommand.Print($"vc_cam: no settler called or working as '{args[1]}' is loaded here");
                return;
            }
            s_follow = c;
            s_following = true;
            s_followName = c.CitizenName;
            s_distance = n.Length > 0 ? n[0] : 6f;
            s_height = n.Length > 1 ? n[1] : 2f;
            s_angle = n.Length > 2 ? n[2] : 30f;
            s_yaw = c.transform.eulerAngles.y;
            TurnOn();
            CitizenRecord r = c.Record;
            if (s_keepHud) BuildCommand.Print("vc_cam: the screen stays, with name tags and speech");
            BuildCommand.Print($"vc_cam: following {r.Name} ({(string.IsNullOrEmpty(r.Job) ? "jobless" : r.Job)}, {Localization.instance.Localize(r.Status ?? "")}) " +
                               $"{Vector3.Distance(c.transform.position, p.transform.position):0} m away, from {s_distance:0.#} m, {s_height:0.#} m up, at {s_angle:0} degrees");
        }

        /// <summary>Leaving the world gives the camera back too.</summary>
        public static void Off()
        {
            if (!s_on) return;
            s_on = false;
            s_follow = null;
            s_following = false;
            if (Hud.instance != null) Hud.instance.m_userHidden = false;
        }

        /// <summary>
        /// Name tags and the words above heads are put on the screen from the camera as it stands when they're drawn up,
        /// which may be after the game placed it: so the camera is moved there too, right after the game, not only just
        /// before it draws (else they'd float where the game's own camera would see them).
        /// </summary>
        [HarmonyPatch]
        private static class AfterTheGameHook
        {
            [HarmonyPatch(typeof(GameCamera), "LateUpdate"), HarmonyPostfix]
            private static void AfterTheGame(GameCamera __instance)
            {
                if (!s_on || __instance == null) return;
                Camera camera = __instance.GetComponent<Camera>();
                if (camera != null) Place(camera);
            }
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
            Vector3 at = s_at, look = s_look;
            if (s_following)
            {
                if (s_follow == null)   // unloaded or dead
                {
                    BuildCommand.Print($"vc_cam: {s_followName} is gone (unloaded or dead); the camera is back");
                    Off();
                    return;
                }
                // Worked out once a frame: Place runs after the game's camera and again before drawing, and both must agree
                // (the review of 1.4.2: the camera turned twice as fast, and name tags drifted while it turned).
                if (s_poseFrame != Time.frameCount)
                {
                    s_poseFrame = Time.frameCount;
                    Vector3 feet = s_follow.transform.position;
                    s_yaw = Mathf.LerpAngle(s_yaw, s_follow.transform.eulerAngles.y, 1f - Mathf.Exp(-TurnRate * Time.deltaTime));
                    Vec3 offset = CamFollow.Offset(s_yaw, s_distance, s_height, s_angle);
                    s_followAt = feet + new Vector3(offset.X, offset.Y, offset.Z);
                    if (ZoneSystem.instance != null) s_followAt.y = Mathf.Max(s_followAt.y, ZoneSystem.instance.GetGroundHeight(s_followAt) + 0.5f);   // never under the ground on a slope
                    s_followLook = feet + Vector3.up * 1.2f;
                }
                at = s_followAt;
                look = s_followLook;
            }
            camera.transform.position = at;
            Vector3 dir = look - at;
            if (dir.sqrMagnitude > 0.0001f) camera.transform.rotation = Quaternion.LookRotation(dir);
        }
    }
}
