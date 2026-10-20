using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_turret (the Watchtower's ballista, 2026-10-18; the player: "the ballistas are behind fences ... will they even
    /// work there?"): what the ballistas near you see and hold, and the monsters near them with their health, so a test
    /// can tell whether a ballista on a tower aims and hits. 'load' puts bolts in, as a player does.
    /// </summary>
    internal sealed class TurretCommand : ConsoleCommand
    {
        /// <summary>
        /// The ballista on a watchtower (huts round 2): loaded, then a monster on the ground 12, 20 and 28 m in front of it in
        /// turn, each for up to 25 s: hit (its health fell), and the bolts it took. A ballista behind a rail taller than its
        /// eye shoots into the rail.
        /// </summary>
        private static System.Collections.IEnumerator Test(Turret t, string monster)
        {
            ZNetView view = t.GetComponent<ZNetView>();
            string ammo = t.m_allowedAmmo != null && t.m_allowedAmmo.Count > 0 && t.m_allowedAmmo[0].m_ammo != null ? t.m_allowedAmmo[0].m_ammo.name : "";
            for (int i = 0; i < 30 && view != null && view.IsValid(); i++) view.InvokeRPC("RPC_AddAmmo", ammo);
            yield return new WaitForSeconds(1f);
            BuildCommand.Print($"vc_turret test: {t.GetAmmo()} '{ammo}' bolts loaded");
            Vector3 forward = t.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            int hits = 0;
            foreach (float distance in new[] { 12f, 20f, 28f })
            {
                Vector3 at = t.transform.position + forward * distance;
                GameObject go = DevHelpers.Spawn(monster, at);
                Character c = go != null ? go.GetComponent<Character>() : null;
                if (c == null)
                {
                    BuildCommand.Print($"vc_turret test: no '{monster}' to set out");
                    yield break;
                }
                float below = t.transform.position.y - c.transform.position.y;   // Spawn sets it on the ground
                float health = c.GetHealth();
                int bolts = t.GetAmmo();
                bool aimed = false;
                for (float waited = 0f; waited < 25f && c != null && !c.IsDead() && c.GetHealth() >= health - 0.01f; waited += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    aimed |= s_target(t) == c;
                }
                bool hit = c == null || c.IsDead() || c.GetHealth() < health - 0.01f;
                if (hit) hits++;
                BuildCommand.Print($"vc_turret test: {monster} {distance:0} m out, {below:0.0} m below: " +
                                   $"{(hit ? "hit" : "not hit")}, aimed at {(aimed ? "yes" : "no")}, {bolts - t.GetAmmo()} bolts shot");
                if (c != null) ZNetScene.instance.Destroy(c.gameObject);
                yield return new WaitForSeconds(1f);
            }
            BuildCommand.Print($"vc_turret test: {hits} of 3 hit");
        }

        public override string Name => "vc_turret";
        public override string Help => "VikingColonies (test worlds): 'vc_turret' lists the ballistas within 40 m (their aim limits, eye, bolts, target) " +
                                       "and the monsters near them; 'vc_turret load [n]' puts n bolts (10) in each; 'vc_turret test [monster]' sets a " +
                                       "monster (a Greyling) 12, 20 and 28 m in front of the nearest ballista in turn and says whether it was hit";
        public override bool IsCheat => true;

        private static readonly HarmonyLib.AccessTools.FieldRef<Turret, Character> s_target = HarmonyLib.AccessTools.FieldRefAccess<Turret, Character>("m_target");

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_turret: test worlds only");
                return;
            }
            Turret[] turrets = Object.FindObjectsByType<Turret>(FindObjectsSortMode.None)
                .Where(t => t != null && Vector3.Distance(t.transform.position, p.transform.position) <= 40f).ToArray();
            if (turrets.Length == 0)
            {
                BuildCommand.Print("vc_turret: no ballista within 40 m");
                return;
            }
            if (args != null && args.Length > 0 && args[0].ToLowerInvariant() == "test")
            {
                Turret nearest = turrets.OrderBy(t => Vector3.Distance(t.transform.position, p.transform.position)).First();
                Plugin.Instance.StartCoroutine(Test(nearest, args.Length > 1 ? args[1] : "Greyling"));
                return;
            }
            bool load = args != null && args.Length > 0 && args[0].ToLowerInvariant() == "load";
            int n = load && args.Length > 1 && int.TryParse(args[1], out int k) ? k : 10;
            foreach (Turret t in turrets)
            {
                ZNetView view = t.GetComponent<ZNetView>();
                // The ballista names no default bolt: the first it takes is what a player loads (as vc_guardtest does).
                string ammo = t.m_defaultAmmo != null ? t.m_defaultAmmo.name
                    : t.m_allowedAmmo != null && t.m_allowedAmmo.Count > 0 && t.m_allowedAmmo[0].m_ammo != null ? t.m_allowedAmmo[0].m_ammo.name : "";
                if (load && view != null && view.IsValid())
                    for (int i = 0; i < n; i++) view.InvokeRPC("RPC_AddAmmo", ammo);
                Vector3 eye = t.m_eye != null ? t.m_eye.transform.position - t.transform.position : Vector3.zero;
                Character target = s_target(t);
                BuildCommand.Print($"vc_turret: ballista at ({t.transform.position.x:0.0}, {t.transform.position.y:0.0}, {t.transform.position.z:0.0}) facing " +
                    $"{t.transform.eulerAngles.y:0}°: eye {eye.y:0.00} m up, {new Vector2(eye.x, eye.z).magnitude:0.00} m forward; aims within {t.m_horizontalAngle:0}° " +
                    $"sideways and {t.m_verticalAngle:0}° up or down, sees {t.m_viewDistance:0} m; {t.GetAmmo()} of {t.m_maxAmmo} '{ammo}' bolts" +
                    (load ? $" (+{n})" : "") + $"; target {(target != null ? target.m_name + " " + Vector3.Distance(target.transform.position, t.transform.position).ToString("0") + " m" : "none")}");
            }
            foreach (Character c in Character.GetAllCharacters().Where(c => c != null && !c.IsPlayer() && c.GetComponent<Citizens.Citizen>() == null
                         && turrets.Any(t => Vector3.Distance(c.transform.position, t.transform.position) <= 45f)))
            {
                Turret t = turrets.OrderBy(x => Vector3.Distance(x.transform.position, c.transform.position)).First();
                Vector3 d = c.transform.position - t.transform.position;
                BuildCommand.Print($"vc_turret: {c.m_name} {new Vector2(d.x, d.z).magnitude:0.0} m out and {d.y:0.0} m below/above: health {c.GetHealth():0}/{c.GetMaxHealth():0}");
            }
        }
    }
}
