using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// The test toolkit's background work: keeps vc_speed's game speed while the game isn't paused, rewrites the report
    /// for 'vc_report auto', and runs the command file (Developer › CommandFile, off by default; needs devcommands).
    /// </summary>
    internal sealed class DevRunner : MonoBehaviour
    {
        private const float FileCheckSeconds = 1f;
        private static readonly FieldInfo s_gameTimeScale = AccessTools.Field(typeof(Game), "m_timeScale");

        public static int Speed { get; private set; } = 1;
        /// <summary>Seconds between automatic reports; 0 = off.</summary>
        public static float ReportEvery { get; set; }

        public static string CommandsPath => Path.Combine(Paths.BepInExRootPath, "VikingColonies-commands.txt");
        public static string OutputPath => Path.Combine(Paths.BepInExRootPath, "VikingColonies-commands-out.txt");

        internal static bool Capturing;
        internal static readonly List<string> Captured = new List<string>();

        private const float HeartbeatSeconds = 5f;
        // The game's popup, looked up by name when first needed (a test tool: nothing of it on a player's start-up path).
        private static bool s_popupLooked;
        private static MethodInfo s_popupVisible;
        private static FieldInfo s_popupInstance, s_popupHeader, s_popupBody;

        /// <summary>The runner's sign of life (0.24.1): rewritten every 5 s while the game runs (with CommandFile on).</summary>
        public static string HeartbeatPath => Path.Combine(Paths.BepInExRootPath, "VikingColonies-heartbeat.txt");

        private float _reportTimer;
        private float _fileTimer;
        private float _heartbeatTimer;
        private string _popupTold = "";

        /// <summary>Single player only; returns what happened, for the console.</summary>
        public static string SetSpeed(int speed)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZNet.instance.GetPeers().Count > 0)
                return "vc_speed: only in a single-player world (everyone shares one game speed).";
            Speed = GameSpeed.Clamp(speed);
            Apply(Speed);
            return $"vc_speed: the game runs {Speed}x" + (Speed == 1 ? " (normal)." : "; 'vc_speed 1' sets it back.");
        }

        private static void Apply(float speed)
        {
            if (s_gameTimeScale != null && Game.instance != null) s_gameTimeScale.SetValue(s_gameTimeScale.IsStatic ? null : Game.instance, speed);
            if (Time.timeScale > 0f) Time.timeScale = speed;   // paused: the game sets it again when it goes on
        }

        private void Update()
        {
            Heartbeat();
            if (Player.m_localPlayer == null)
            {
                if (Speed != 1)
                {
                    Speed = 1;   // left the world
                    Apply(1f);
                }
                ReportEvery = 0f;
                return;
            }
            if (Speed != 1 && Time.timeScale > 0f && Math.Abs(Time.timeScale - Speed) > 0.01f) Time.timeScale = Speed;
            if (ReportEvery > 0f)
            {
                _reportTimer -= Time.unscaledDeltaTime;
                if (_reportTimer <= 0f)
                {
                    _reportTimer = ReportEvery;
                    try { DevReport.Write(); }
                    catch (Exception e) { Log.Warn($"vc_report auto: {e.Message}"); }
                }
            }
            _fileTimer -= Time.unscaledDeltaTime;
            if (_fileTimer > 0f) return;
            _fileTimer = FileCheckSeconds;
            RunCommandFile();
        }

        /// <summary>
        /// Tells the test runner the game still runs (0.24.1): a frozen game stops writing it, and a popup waiting for a
        /// click (a lost connection, a warning) is named in it and in the log, so a run stops at once instead of timing out.
        /// </summary>
        private void Heartbeat()
        {
            _heartbeatTimer -= Time.unscaledDeltaTime;
            if (_heartbeatTimer > 0f || !ColonyConfig.CommandFile.Value) return;
            _heartbeatTimer = HeartbeatSeconds;
            string popup = PopupText();
            if (popup != _popupTold)
            {
                if (popup.Length > 0) Log.Event($"Dev: a popup is showing: {popup}");
                _popupTold = popup;
            }
            try
            {
                string world = Player.m_localPlayer != null && ZNet.instance != null ? ZNet.instance.GetWorldName() : "menu";
                File.WriteAllText(HeartbeatPath, $"time {DateTime.Now:yyyy-MM-ddTHH:mm:ss}\nframes {Time.frameCount}\nworld {world}\n" +
                                                 $"popup {(popup.Length > 0 ? popup : "none")}\n");
            }
            catch (Exception) { }   // being read, or not writable: next time; never the rest of the dev runner's work
        }

        /// <summary>The popup on screen, "header - body" ("" when none).</summary>
        private static string PopupText()
        {
            try
            {
                if (!s_popupLooked)
                {
                    s_popupLooked = true;
                    Type type = AccessTools.TypeByName("UnifiedPopup");
                    s_popupVisible = type != null ? AccessTools.Method(type, "IsVisible") : null;
                    s_popupInstance = type != null ? AccessTools.Field(type, "instance") : null;
                    s_popupHeader = type != null ? AccessTools.Field(type, "headerText") : null;
                    s_popupBody = type != null ? AccessTools.Field(type, "bodyText") : null;
                }
                if (s_popupVisible == null || !(s_popupVisible.Invoke(null, null) is bool shown) || !shown) return "";
                object popup = s_popupInstance?.GetValue(null);
                string header = (s_popupHeader?.GetValue(popup) as TMPro.TMP_Text)?.text ?? "";
                string body = (s_popupBody?.GetValue(popup) as TMPro.TMP_Text)?.text ?? "";
                string text = (header + " - " + body).Replace("\r", "").Replace("\n", " / ").Trim(' ', '-');
                return text.Length == 0 ? "a popup" : text.Length > 200 ? text.Substring(0, 200) : text;
            }
            catch (Exception) { return ""; }   // no popup system yet (start-up)
        }

        /// <summary>Runs the commands written into the command file, one per line, and empties it; answers go to the output file.</summary>
        private static void RunCommandFile()
        {
            if (!ColonyConfig.CommandFile.Value || global::Console.instance == null || !global::Console.instance.IsCheatsEnabled()) return;
            if (!File.Exists(CommandsPath)) return;
            string world = Player.m_localPlayer != null && ZNet.instance != null ? ZNet.instance.GetWorldName() : "";
            CommandFileState state = CommandFile.In(world);
            if (state == CommandFileState.Wait) return;   // no world yet: the commands wait in the file
            List<string> lines;
            try
            {
                lines = CommandFile.Parse(File.ReadAllText(CommandsPath));
                if (lines.Count == 0) return;
                File.WriteAllText(CommandsPath, "");   // taken: each command runs once
            }
            catch (IOException) { return; }   // still being written: next second
            foreach (string line in lines)
            {
                var answer = new List<string> { $"[{DateTime.Now:HH:mm:ss}] > {line}" };
                if (state == CommandFileState.Refuse) answer.Add($"  refused: the command file runs only in a test world (its name must contain 'test'); this is {world}");
                else if (!CommandFile.IsAllowed(line)) answer.Add("  refused: only vc_ commands and a few harmless dev commands run from the file");
                else
                {
                    Captured.Clear();
                    Capturing = true;
                    try { global::Console.instance.TryRunCommand(line, false, false); }
                    catch (Exception e) { Captured.Add("error: " + e.Message); }
                    finally { Capturing = false; }
                    answer.AddRange(Captured.Select(c => "  " + c));
                }
                Log.Event("Command file: " + string.Join(" | ", answer));
                File.AppendAllText(OutputPath, string.Join("\n", answer) + "\n");
            }
        }
    }

    /// <summary>Copies what the console prints while a command from the command file runs.</summary>
    [HarmonyPatch(typeof(Terminal), "AddString", new[] { typeof(string) })]
    internal static class TerminalCapture
    {
        [HarmonyPostfix]
        private static void After(string text)
        {
            if (DevRunner.Capturing) DevRunner.Captured.Add(text);
        }
    }
}
