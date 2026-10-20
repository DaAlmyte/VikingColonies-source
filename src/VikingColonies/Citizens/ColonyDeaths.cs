using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Core;

namespace VikingColonies.Citizens
{
    /// <summary>
    /// The settlers known to have died, on every computer: told by the death message every computer receives, and saved
    /// with each player's character, so a Town Hall that wasn't loaded when he died still learns of it and Bring back
    /// never revives the dead (review of 0.10).
    /// </summary>
    internal static class ColonyDeaths
    {
        private const string CustomKey = SaveKeys.Dead;
        private static readonly HashSet<string> s_dead = new HashSet<string>();
        private static Player s_loadedFor;

        public static bool IsDead(string citizenId)
        {
            LoadFromPlayer();
            return !string.IsNullOrEmpty(citizenId) && s_dead.Contains(citizenId);
        }

        public static void Remember(string citizenId)
        {
            if (string.IsNullOrEmpty(citizenId)) return;
            LoadFromPlayer();
            s_dead.Add(citizenId);
            Player player = Player.m_localPlayer;
            if (player == null) return;   // a dedicated server: remembered while it runs
            player.m_customData.TryGetValue(CustomKey, out string saved);
            player.m_customData[CustomKey] = DeathList.Add(saved, citizenId);
        }

        private static void LoadFromPlayer()
        {
            Player player = Player.m_localPlayer;
            if (player == null || ReferenceEquals(player, s_loadedFor)) return;
            s_loadedFor = player;
            if (player.m_customData.TryGetValue(CustomKey, out string saved))
                foreach (string id in DeathList.Parse(saved)) s_dead.Add(id);
        }
    }

    /// <summary>
    /// Whether a settler's body exists anywhere in the world, asked of the world's host, which knows every one (another
    /// computer only knows the bodies near its player). Review of 0.10: on a dedicated server, Bring back made a second
    /// settler whose body lay in a part of the world that player hadn't seen.
    /// </summary>
    [HarmonyPatch]
    internal static class BodyCheck
    {
        public enum Answer { NotFound, Found, NoAnswer }

        private const string RpcQuery = "vc_BodyQuery", RpcAnswer = "vc_BodyAnswer";
        private const float AnswerSeconds = 5f;
        private static readonly Dictionary<string, Action<Answer>> s_waiting = new Dictionary<string, Action<Answer>>();

        [HarmonyPatch(typeof(Game), "Start"), HarmonyPostfix]
        private static void RegisterRpc()
        {
            ZRoutedRpc.instance?.Register<string>(RpcQuery, OnQuery);
            ZRoutedRpc.instance?.Register<string, bool>(RpcAnswer, OnAnswer);
        }

        /// <summary>Asks the host; <paramref name="then"/> runs once with its answer (or NoAnswer after a few seconds).</summary>
        public static void Ask(string citizenId, Action<Answer> then)
        {
            if (ZNet.instance == null || ZRoutedRpc.instance == null)
            {
                then(Answer.NoAnswer);
                return;
            }
            if (ZNet.instance.IsServer())
            {
                then(Citizen.BodyKnown(citizenId) ? Answer.Found : Answer.NotFound);
                return;
            }
            s_waiting[citizenId] = then;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcQuery, citizenId);
            Plugin.Instance.StartCoroutine(GiveUp(citizenId));
        }

        private static IEnumerator GiveUp(string citizenId)
        {
            yield return new WaitForSecondsRealtime(AnswerSeconds);
            if (!s_waiting.TryGetValue(citizenId, out Action<Answer> then)) yield break;
            s_waiting.Remove(citizenId);
            then(Answer.NoAnswer);   // a host without the mod, or a lost message
        }

        private static void OnQuery(long sender, string citizenId)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcAnswer, citizenId, Citizen.BodyKnown(citizenId) || ColonyDeaths.IsDead(citizenId));
        }

        private static void OnAnswer(long sender, string citizenId, bool found)
        {
            if (!s_waiting.TryGetValue(citizenId, out Action<Answer> then)) return;
            s_waiting.Remove(citizenId);
            then(found ? Answer.Found : Answer.NotFound);
        }
    }
}
