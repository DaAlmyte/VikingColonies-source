using System.Collections.Generic;
using UnityEngine;
using VikingColonies.Core;

namespace VikingColonies.Citizens
{
    /// <summary>
    /// Settlers' bodies pass through each other and through players (1.4.1, the player: "villagers keep pushing each other
    /// and me when they run into each other and me"), as followers do in other games' mods. Only the bodies' bumping:
    /// blows, arrows and the game's searches still find them (those don't ask the physics engine to keep them apart).
    /// On every computer, for the settlers and players loaded there; the game resets an ignored pair when a body is
    /// switched off and on (sitting down, a ragdoll), so the pairs are set again every few seconds.
    /// </summary>
    internal sealed class SettlerBodies : MonoBehaviour
    {
        private const float EverySeconds = 2f;
        private float _timer;
        private static readonly List<Collider> s_settlers = new List<Collider>();
        private static readonly List<Collider> s_players = new List<Collider>();

        /// <summary>Pairs set at the last look (vc_selftest reads it).</summary>
        internal static int PairsForTest { get; private set; }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = EverySeconds;
            Refresh();
        }

        internal static void Refresh()
        {
            s_settlers.Clear();
            s_players.Clear();
            foreach (Citizen c in Citizen.All)
            {
                Collider body = c != null ? c.GetComponent<CapsuleCollider>() : null;
                if (Usable(body)) s_settlers.Add(body);
            }
            if (s_settlers.Count == 0)
            {
                PairsForTest = 0;
                return;
            }
            foreach (Player p in Player.GetAllPlayers())
            {
                Collider body = p != null ? p.GetComponent<CapsuleCollider>() : null;
                if (Usable(body)) s_players.Add(body);
            }
            int pairs = 0;
            for (int i = 0; i < s_settlers.Count; i++)
            {
                for (int j = i + 1; j < s_settlers.Count; j++)
                    if (PassThrough.Between(aIsSettler: true, bIsSettler: true, bIsPlayer: false)) pairs += Ignore(s_settlers[i], s_settlers[j]);
                foreach (Collider player in s_players)
                    if (PassThrough.Between(aIsSettler: true, bIsSettler: false, bIsPlayer: true)) pairs += Ignore(s_settlers[i], player);
            }
            PairsForTest = pairs;
        }

        /// <summary>
        /// Sets a pair to pass through each other, unless it already does (the second review of 1.4.1: setting every pair
        /// again every 2 s is thousands of calls at 100 settlers). Counts the pairs that pass through.
        /// </summary>
        private static int Ignore(Collider a, Collider b)
        {
            if (a == null || b == null || a == b) return 0;
            if (!Physics.GetIgnoreCollision(a, b)) Physics.IgnoreCollision(a, b, true);
            return 1;
        }

        /// <summary>A body switched off can't be set (the game says so in the log); it is set once it is on again.</summary>
        private static bool Usable(Collider body) => body != null && body.enabled && body.gameObject.activeInHierarchy;

        /// <summary>vc_selftest: whether these two bodies pass through each other now.</summary>
        internal static bool IgnoredForTest(Component a, Component b)
        {
            Collider ca = a != null ? a.GetComponent<CapsuleCollider>() : null, cb = b != null ? b.GetComponent<CapsuleCollider>() : null;
            return ca != null && cb != null && Physics.GetIgnoreCollision(ca, cb);
        }
    }
}
