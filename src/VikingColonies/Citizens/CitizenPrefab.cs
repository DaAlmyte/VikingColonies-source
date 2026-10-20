using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jotunn.Managers;
using UnityEngine;
using VikingColonies.AI;
using Object = UnityEngine.Object;

namespace VikingColonies.Citizens
{
    /// <summary>Builds the settler template from the vanilla Player body, once, at game start.</summary>
    internal static class CitizenPrefab
    {
        public const string PrefabName = "vc_citizen";
        public const string StartingAxe = "AxeFlint";
        public const string StartingHammer = "Hammer";

        // Parts of the Player prefab that only make sense for a human at the keyboard.
        private static readonly string[] PlayerOnlyComponents = { "PlayerController", "Talker", "Skills" };

        public static GameObject Prefab { get; private set; }

        public static void Register()
        {
            GameObject go = PrefabManager.Instance.CreateClonedPrefab(PrefabName, "Player");
            if (go == null)
            {
                Log.Error("Could not clone the Player prefab; settlers are disabled.");
                return;
            }

            foreach (string typeName in PlayerOnlyComponents)
            {
                Component c = go.GetComponent(typeName);
                if (c != null) Object.DestroyImmediate(c);
            }

            Humanoid humanoid = ReplacePlayerWithHumanoid(go);
            humanoid.m_name = "$vc_lumberjack";
            humanoid.m_faction = Character.Faction.Players;   // monsters attack settlers; players are friends
            humanoid.m_health = 100f;
            humanoid.m_defaultItems = new GameObject[0];
            humanoid.m_randomWeapon = new GameObject[0];
            humanoid.m_randomArmor = new GameObject[0];
            humanoid.m_randomShield = new GameObject[0];
            humanoid.m_randomSets = new Humanoid.ItemSet[0];
            humanoid.m_randomItems = new Humanoid.RandomItem[0];

            go.layer = LayerMask.NameToLayer("character");
            go.GetComponent<ZNetView>().m_persistent = true;   // players aren't saved with the world; settlers must be

            CitizenAI ai = go.AddComponent<CitizenAI>();
            ai.m_pathAgentType = Pathfinding.AgentType.Humanoid;
            ai.m_viewRange = 30f;
            ai.m_viewAngle = 90f;
            ai.m_hearRange = 30f;
            ai.m_avoidFire = true;
            ai.m_avoidWater = true;
            ai.m_moveMinAngle = 90f;   // turn while walking (slow down in sharp bends) instead of stopping to turn

            go.AddComponent<Citizen>();
            go.AddComponent<SettlerPose>();

            Log.Info("Settler prefab parts: " + string.Join(", ", go.GetComponents<Component>().Select(c => c.GetType().Name)));
            PrefabManager.Instance.AddPrefab(go);
            Prefab = go;
        }

        /// <summary>Self-test: the settler body counts as not loaded.</summary>
        internal static bool HideForTest;

        /// <summary>How many times players were told the settler body didn't load (the self-test counts it).</summary>
        internal static int MissingTold;

        /// <summary>The settler body loaded at start-up (it can fail after a game update).</summary>
        public static bool Available => Prefab != null && !HideForTest;

        private static bool s_loggedMissing;
        private static float s_toldAt = float.NegativeInfinity;

        /// <summary>
        /// Players near are told settlers can't come (0.19, from the v1 review: it was an error and a half-made settler);
        /// the log says it once. The polish test's own (HideForTest) is no error (0.25: the run's log summary counted it).
        /// </summary>
        public static void TellMissing(Vector3 near)
        {
            if (HideForTest) Log.Event("The settler body is hidden by the polish test: no settlers can come meanwhile.");
            else if (!s_loggedMissing)
            {
                Log.Error("The settler body didn't load (see the errors at start-up): no settlers can come.");
                s_loggedMissing = true;
            }
            s_toldAt = Time.time;
            MissingTold++;
            NewcomerMessages.SendNoBody(near);
        }

        public static Citizen Spawn(Vector3 position, Quaternion rotation)
        {
            if (!Available)
            {
                if (Time.time - s_toldAt >= 60f || HideForTest) TellMissing(position);
                return null;
            }
            GameObject go = Object.Instantiate(Prefab, position, rotation);
            return go.GetComponent<Citizen>();
        }

        /// <summary>Player derives from Humanoid; copy every Character/Humanoid field over, then drop Player.</summary>
        private static Humanoid ReplacePlayerWithHumanoid(GameObject go)
        {
            Player player = go.GetComponent<Player>();
            var snapshot = new List<KeyValuePair<FieldInfo, object>>();
            foreach (Type type in new[] { typeof(Character), typeof(Humanoid) })
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    snapshot.Add(new KeyValuePair<FieldInfo, object>(field, field.GetValue(player)));

            Humanoid humanoid = go.AddComponent<Humanoid>();
            if (humanoid == null)   // Unity refused a second Character on the object: remove Player first
            {
                Object.DestroyImmediate(player);
                humanoid = go.AddComponent<Humanoid>();
            }
            foreach (KeyValuePair<FieldInfo, object> entry in snapshot) entry.Key.SetValue(humanoid, entry.Value);
            if (player != null) Object.DestroyImmediate(player);
            return humanoid;
        }
    }
}
