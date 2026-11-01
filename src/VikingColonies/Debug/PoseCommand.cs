using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Citizens;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_pose (test worlds): sits, lays or stands the nearest settler (or one by name), plays a gesture, and logs his
    /// animator, so the poses can be checked on a settler's body (settler life, 0.14).
    /// </summary>
    internal sealed class PoseCommand : ConsoleCommand
    {
        public override string Name => "vc_pose";
        public override string Help => "VikingColonies (test worlds): 'vc_pose [name] seat|bed|ground|dance|rest|kneel|stand|pullseat|list|where' (pullseat: their seat taken away under them; fireon [metres]: a bonfire put up on them, its middle that far from them, as a builder puts the camp's up), 'vc_pose [name] trigger <animation>', " +
                                       "'vc_pose [name] bool <animation> on|off'. The settler's own brain pauses for a minute; seat and bed use one within 15 m, " +
                                       "or place a bench or a bed in front of them.";
        public override bool IsCheat => true;

        /// <summary>The settler the last pose was given to: 'look' looks at him.</summary>
        private static string s_lastName = "";

        private static readonly string[] Verbs = { "seat", "bed", "ground", "stand", "pullseat", "fireon", "list", "where", "trigger", "bool", "look", "dance", "rest", "kneel" };

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_pose: test worlds only");
                return;
            }
            args = args ?? new string[0];
            int verbAt = System.Array.FindIndex(args, a => Verbs.Contains(a));
            if (verbAt < 0)
            {
                BuildCommand.Print("vc_pose: say seat, bed, ground, stand, list, where, trigger <animation> or bool <animation> on|off");
                return;
            }
            string name = string.Join(" ", args.Take(verbAt)).Trim();
            if (name.Length == 0 && (args[verbAt] == "look" || args[verbAt] == "pullseat" || args[verbAt] == "fireon")) name = s_lastName;
            Citizen c = Citizen.All.Where(x => x != null && (name.Length == 0 || x.CitizenName == name))
                .OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
            SettlerPose pose = c != null ? c.GetComponent<SettlerPose>() : null;
            if (pose == null)
            {
                BuildCommand.Print("vc_pose: no such settler near");
                return;
            }
            s_lastName = c.CitizenName;
            CitizenAI ai = c.GetComponent<CitizenAI>();
            if (!c.IsOwner) c.GetComponent<ZNetView>().ClaimOwnership();
            ai?.PauseForTest(60f);
            string verb = args[verbAt];
            string arg = args.Length > verbAt + 1 ? args[verbAt + 1] : "";
            switch (verb)
            {
                case "seat":
                    Chair chair = Object.FindObjectsByType<Chair>(FindObjectsSortMode.None)
                        .Where(x => x != null && !x.m_inShip && Vector3.Distance(x.transform.position, c.transform.position) < 15f)
                        .OrderBy(x => Vector3.Distance(x.transform.position, c.transform.position)).FirstOrDefault();
                    if (chair == null)
                    {
                        GameObject bench = DevHelpers.Spawn("piece_bench01", c.transform.position + c.transform.forward * 2f);
                        chair = bench != null ? bench.GetComponentInChildren<Chair>() : null;
                    }
                    BuildCommand.Print($"vc_pose: {c.CitizenName} sits on {(chair != null ? chair.name : "nothing")}: {pose.SitOn(chair)}");
                    Chair[] same = chair != null && chair.GetComponentInParent<Piece>() != null ? chair.GetComponentInParent<Piece>().GetComponentsInChildren<Chair>() : new Chair[0];
                    if (same.Length > 1)
                        Log.Event($"vc_pose: that piece seats {same.Length}, " + string.Join(", ", same.Skip(1).Select((x, i) =>
                            $"{Vector3.Distance(same[i].m_attachPoint.position, x.m_attachPoint.position):0.00} m")) + " apart");
                    break;
                case "bed":
                    Bed bed = Object.FindObjectsByType<Bed>(FindObjectsSortMode.None)
                        .Where(x => x != null && Vector3.Distance(x.transform.position, c.transform.position) < 15f)
                        .OrderBy(x => Vector3.Distance(x.transform.position, c.transform.position)).FirstOrDefault();
                    if (bed == null)
                    {
                        GameObject made = DevHelpers.Spawn("bed", c.transform.position + c.transform.forward * 2.5f);
                        bed = made != null ? made.GetComponentInChildren<Bed>() : null;
                    }
                    BuildCommand.Print($"vc_pose: {c.CitizenName} lies in {(bed != null ? bed.name : "nothing")}: {pose.LieIn(bed)}");
                    break;
                case "ground":
                    pose.SitGround();
                    BuildCommand.Print($"vc_pose: {c.CitizenName} sits down");
                    break;
                case "dance":
                case "rest":
                case "kneel":
                    pose.Hold(verb == "dance" ? SettlerPose.Kind.Dance : verb == "rest" ? SettlerPose.Kind.Rest : SettlerPose.Kind.Kneel);
                    BuildCommand.Print($"vc_pose: {c.CitizenName} holds {verb}");
                    break;
                case "stand":
                    pose.Stand();
                    BuildCommand.Print($"vc_pose: {c.CitizenName} stands up");
                    break;
                case "pullseat":
                    // The seat they're on taken away under them, as a builder's upgrade takes down a camp's log bench.
                    Transform held = pose.Point;
                    // The seat or bed piece itself, never a ship a seat stands in (vc_pose seat skips those).
                    WearNTear seatPiece = held != null && (held.GetComponentInParent<Chair>() is Chair inChair ? !inChair.m_inShip : held.GetComponentInParent<Bed>() != null)
                        ? held.GetComponentInParent<WearNTear>() : null;
                    if (seatPiece == null || seatPiece.GetComponent<Ship>() != null)
                    {
                        BuildCommand.Print($"vc_pose: {c.CitizenName} sits on no piece");
                        break;
                    }
                    ZNetView seatView = seatPiece.GetComponent<ZNetView>();
                    if (seatView != null && seatView.IsValid() && !seatView.IsOwner()) seatView.ClaimOwnership();
                    seatPiece.Remove();
                    ai?.PauseForTest(95f);   // held still while followed: another seat of their own choosing would read as held (the camp's build-through tests the routine)
                    Log.Event($"vc_pose: {c.CitizenName}'s seat taken away");
                    Plugin.Instance.StartCoroutine(Follow(c));
                    return;
                case "fireon":
                {
                    // A bonfire put up where they stand, as the builder puts the camp's (its pivot half a metre over the ground),
                    // its middle this far off (the builders' test of 27 Oct: the camp's bonfire, its level's last piece, went up
                    // on a settler walking to a chat spot inside it; later the settler was under the ground and the bonfire gone).
                    float off = float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float m) ? m : 0f;
                    GameObject bonfirePrefab = PiecePlacer.Prefab("bonfire");
                    Vector3 middle = c.transform.position + c.transform.forward * off;
                    middle.y = ZoneSystem.instance.GetGroundHeight(middle) + 0.4962f;
                    GameObject fire = bonfirePrefab != null ? PiecePlacer.Place(bonfirePrefab, middle, Quaternion.identity, "probe", 0L) : null;
                    ai?.PauseForTest(95f);
                    Log.Event($"vc_pose: a bonfire put up at {middle} on {c.CitizenName} at {c.transform.position}: {fire != null}");
                    if (fire != null) Plugin.Instance.StartCoroutine(FollowFire(c, fire));
                    return;
                }
                case "trigger":
                    pose.Gesture(arg);
                    BuildCommand.Print($"vc_pose: {c.CitizenName} plays {arg}");
                    break;
                case "look":
                    // From the side, a few steps off, so the player's own body doesn't hide him.
                    GotoCommand.Go(p, c.transform.position + c.transform.right * 3.5f + c.transform.forward * 1.5f, c.transform.position + Vector3.up * 0.8f);
                    BuildCommand.Print($"vc_pose: looking at {c.CitizenName}");
                    break;
                case "bool":
                    pose.SetBoolForTest(arg, args.Length > verbAt + 2 && args[verbAt + 2] == "on");
                    BuildCommand.Print($"vc_pose: {c.CitizenName}'s {arg} set");
                    break;
            }
            Plugin.Instance.StartCoroutine(Report(c, pose, verb == "list" ? "." : "emote|attach|eat|sit"));
        }

        /// <summary>After 'pullseat': where they are, the ground under them and whether the game still holds them, every 5 s for 90 s.</summary>
        private static System.Collections.IEnumerator Follow(Citizen c)
        {
            for (int i = 0; i < 18; i++)
            {
                yield return new WaitForSeconds(i == 0 ? 1f : 5f);
                if (c == null) yield break;
                Vector3 at = c.transform.position;
                Rigidbody rb = c.GetComponent<Rigidbody>();
                float ground = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(at) : float.NaN;
                bool below = !float.IsNaN(ground) && at.y < ground - 1f;
                Log.Event($"vc_pose: {c.CitizenName} after the seat went: at {at}, ground {ground:0.00}, {(below ? "under the ground" : "on the ground")}, " +
                          $"{(c.Body.IsAttached() ? "still held as seated" : "let go")}, gravity {(rb != null && rb.useGravity ? "on" : "off")}, status {c.Status}");
            }
        }

        /// <summary>After 'fireon': where they are and whether the bonfire still stands, every 5 s for 90 s.</summary>
        private static System.Collections.IEnumerator FollowFire(Citizen c, GameObject fire)
        {
            for (int i = 0; i < 18; i++)
            {
                yield return new WaitForSeconds(i == 0 ? 1f : 5f);
                if (c == null) yield break;
                Vector3 at = c.transform.position;
                float ground = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(at) : float.NaN;
                WearNTear wear = fire != null ? fire.GetComponent<WearNTear>() : null;
                ZNetView view = fire != null ? fire.GetComponent<ZNetView>() : null;
                string support = view != null && view.IsValid() ? view.GetZDO().GetFloat(ZDOVars.s_support, -1f).ToString("0") : "-";
                string health = view != null && view.IsValid() ? view.GetZDO().GetFloat(ZDOVars.s_health, -1f).ToString("0") : "-";
                Log.Event($"vc_pose: {c.CitizenName} by the bonfire: at {at}, ground {ground:0.00}, {(at.y < ground - 1f ? "under the ground" : "on the ground")}; " +
                          $"the bonfire {(fire != null ? $"stands (support {support}, health {health})" : "is gone")}");
            }
        }

        private static System.Collections.IEnumerator Report(Citizen c, SettlerPose pose, string match)
        {
            yield return new WaitForSeconds(1f);
            if (c == null) yield break;
            Transform point = pose.Point;
            string where = point != null ? $"{Vector3.Distance(c.transform.position, point.position):0.00} m from his point" : "not held";
            Rigidbody rb = c.GetComponent<Rigidbody>();
            Log.Event($"vc_pose: {c.CitizenName} pose {pose.Current}, {where}, at {c.transform.position}, gravity {(rb != null && rb.useGravity)}, " +
                      $"attached {c.Body.IsAttached()} | {pose.DescribeAnimator(match)}");
            yield return new WaitForSeconds(9f);
            if (c == null) yield break;
            point = pose.Point;
            Log.Event($"vc_pose: {c.CitizenName} 10 s later: pose {pose.Current}, " +
                      $"{(point != null ? $"{Vector3.Distance(c.transform.position, point.position):0.00} m from his point" : "not held")}, at {c.transform.position}");
        }
    }
}
