using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Gui
{
    /// <summary>
    /// Asks the player what they think, once per computer (1.4.2, the feedback design of 2026-10-20): at the first time a
    /// hut near them finishes its upgrade to level 2, or after 2 hours of play with a colony, whichever comes first
    /// (<see cref="FeedbackAsk"/>). It waits while an enemy is near, a menu or window is open, or a raid is on. A small file
    /// next to the mod's config remembers that it asked and the play time so far; if that file can't be written, it never
    /// asks (it would ask every session). In our own test worlds it asks only after 'vc_ui feedback arm', so the test runs
    /// are never interrupted. Pressing a link in the Feedback window counts as an answer.
    /// </summary>
    internal sealed class FeedbackAsker : MonoBehaviour
    {
        internal const float EverySeconds = 2f;
        /// <summary>A hut this near the player counts (as for Hugin's tips).</summary>
        private const float NearMetres = 40f;
        private const float SaveEverySeconds = 60f;
        /// <summary>A bigger file isn't ours: it's read as nothing.</summary>
        private const long MaxFileBytes = 4096;

        private static FeedbackMemory s_memory;
        private static FeedbackAsk s_ask = new FeedbackAsk();
        private static string s_world;
        private static bool s_hasColony;
        private static bool s_armed;
        private static bool s_forcedDue;
        private static bool s_dirty;
        /// <summary>Something here threw: it's logged once and the ask stops for this game (it must never get in the way).</summary>
        private static bool s_broken;
        /// <summary>The file couldn't be written: logged once, and no ask (it couldn't be remembered).</summary>
        private static bool s_cantWrite;
        private float _elapsed;
        private float _sinceSave;

        private static string FilePath => Path.Combine(Paths.ConfigPath, "VikingColonies-feedback.txt");

        private static FeedbackMemory Memory => s_memory ?? (s_memory = Load());

        private void Update()
        {
            _elapsed += Time.deltaTime;   // not while the game is paused
            if (_elapsed < EverySeconds) return;
            float step = _elapsed;
            _elapsed = 0f;
            if (s_broken || s_cantWrite) return;
            try
            {
                Check(step);
            }
            catch (System.Exception e)
            {
                s_broken = true;
                Log.Event($"Feedback: the ask stopped for this game after an error: {e}");
                return;
            }
            _sinceSave += step;
            if (s_dirty && _sinceSave >= SaveEverySeconds) Save();
        }

        private void OnDestroy()
        {
            if (s_dirty) Save();
        }

        private void OnApplicationQuit()
        {
            if (s_dirty) Save();
        }

        private void Check(float step)
        {
            Player player = Player.m_localPlayer;
            if (player == null || ZNet.instance == null) return;
            string world = ZNet.instance.GetWorldName() ?? "";
            if (world != s_world)   // another world: its own huts and colony; a test world's arming doesn't carry over
            {
                s_world = world;
                s_ask = new FeedbackAsk();
                s_hasColony = false;
                s_armed = false;
                s_forcedDue = false;
            }
            FeedbackMemory memory = Memory;
            if (memory.Asked || !ColonyConfig.AskForFeedback.Value) return;
            if (FeedbackAsk.OurTestWorld(world) && !s_armed) return;
            Vector3 me = player.transform.position;
            if (!s_hasColony) s_hasColony = ColonyBuilding.TownHalls().Any(ColonyPermissions.LocalMayManage);
            if (s_hasColony)
            {
                memory.AddPlayed(step);
                s_dirty = true;
            }
            foreach (ColonyBuilding building in ColonyBuilding.All)
                if (building != null && building.IsValid && Vector3.Distance(building.transform.position, me) <= NearMetres)
                    s_ask.SawHut(building.Id, building.Level);
            bool due = s_forcedDue || FeedbackAsk.Due(s_ask.HutReachedLevel2, memory.PlayedSeconds);
            if (!due) return;   // the rest only when it's time
            if (!FeedbackAsk.ShowNow(due, ColonyConfig.AskForFeedback.Value, memory.Asked, enemyNear: EnemyNear(player), inWindow: Busy(player), raid: RandEventSystem.instance != null && RandEventSystem.HaveActiveEvent()))
                return;
            // Remembered before it shows, so it's once however it's closed; never shown if it can't be remembered.
            memory.Asked = true;
            if (!Save())
            {
                memory.Asked = false;
                return;
            }
            Windows.Feedback.Show(asking: true);
            if (!Windows.Feedback.IsOpen)   // the game's windows aren't ready: not used up, and no more tries this game
            {
                memory.Asked = false;
                Save();
                s_broken = true;
                Log.Event("Feedback: the window couldn't open, so it didn't ask; no more tries this game");
                return;
            }
            Log.Event($"Feedback: asked ({(s_forcedDue ? "made due by a test" : s_ask.HutReachedLevel2 ? "a hut near reached level 2" : "two hours with a colony")}, " +
                      $"{memory.PlayedSeconds / 60:0} min played with a colony)");
        }

        /// <summary>A monster within 30 m that would fight the player, or anything after them within 60 m (<see cref="FeedbackAsk.Threat"/>).</summary>
        private static bool EnemyNear(Player player)
        {
            Vector3 me = player.transform.position;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c == player || c.IsDead() || c.IsTamed()) continue;
                float distance = Vector3.Distance(c.transform.position, me);
                if (distance > FeedbackAsk.TargetReach) continue;
                BaseAI ai = c.GetBaseAI();
                string faction = c.GetFaction().ToString();
                string brain = ai is MonsterAI ? "MonsterAI" : ai is AnimalAI ? "AnimalAI" : "";
                bool targetsPlayer = ai is MonsterAI monster && monster.GetTargetCreature() == player;
                if (FeedbackAsk.Threat(distance, BaseAI.IsEnemy(player, c), GuardRules.IsMonster(faction, brain),
                        GuardRules.IsAnimal(faction, brain, ai != null && ai.m_passiveAggresive), targetsPlayer))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// A menu, window or the map is open, the player is typing, or isn't free to read a window now. The cursor is free
        /// whenever any window has it, another mod's too (the review of 1.4.2: only ours and the game's were seen).
        /// </summary>
        private static bool Busy(Player player) =>
            Cursor.lockState != CursorLockMode.Locked || UnifiedPopup.IsVisible()
            || Menu.IsVisible() || InventoryGui.IsVisible() || Minimap.IsOpen() || global::Console.IsVisible() || TextInput.IsVisible()
            || StoreGui.IsVisible() || Hud.IsPieceSelectionVisible() || (Chat.instance != null && Chat.instance.HasFocus())
            || (TextViewer.instance != null && TextViewer.instance.IsVisible()) || Windows.AnyOpen() || GameCamera.InFreeFly()
            || player.IsDead() || player.InCutscene() || player.IsTeleporting() || player.IsSleeping() || player.IsAttached()
            || player.IsSwimming() || !player.IsOnGround();

        private static FeedbackMemory Load()
        {
            try
            {
                var file = new FileInfo(FilePath);
                if (!file.Exists || file.Length > MaxFileBytes) return new FeedbackMemory();
                return FeedbackMemory.Read(File.ReadAllText(file.FullName));
            }
            catch (System.Exception e)
            {
                Log.Event($"Feedback: couldn't read {FilePath} ({e.GetType().Name}): it counts as not asked");
                return new FeedbackMemory();
            }
        }

        private bool Save()
        {
            _sinceSave = 0f;
            return WriteFile();
        }

        /// <summary>Writes the file; false (logged once, and no ask from then on) when it can't.</summary>
        private static bool WriteFile()
        {
            s_dirty = false;
            if (s_cantWrite) return false;
            try
            {
                File.WriteAllText(FilePath, Memory.Write());
                return true;
            }
            catch (System.Exception e)
            {
                s_cantWrite = true;
                Log.Event($"Feedback: couldn't write {FilePath} ({e.GetType().Name}), so it won't ask: it couldn't remember that it did");
                return false;
            }
        }

        /// <summary>A link in the Feedback window was opened: that's an answer, and the ask won't come.</summary>
        internal static void Answered()
        {
            if (Memory.Asked) return;
            Memory.Asked = true;
            WriteFile();
        }

        /// <summary>vc_ui feedback arm (test worlds): forget the ask and the play time, and let it come here this session.</summary>
        internal static void ArmForTest(bool dueNow)
        {
            s_memory = new FeedbackMemory();
            s_ask = new FeedbackAsk();
            s_world = ZNet.instance != null ? ZNet.instance.GetWorldName() ?? "" : "";
            s_hasColony = false;
            s_armed = true;
            s_forcedDue = dueNow;
            WriteFile();
        }

        /// <summary>vc_selftest: back to a test world's normal, never asking by itself (after its checks, passed or not).</summary>
        internal static void DisarmForTest()
        {
            s_armed = false;
            s_forcedDue = false;
            s_ask = new FeedbackAsk();
        }

        /// <summary>vc_selftest: the ask was shown on this computer.</summary>
        internal static bool AskedForTest => Memory.Asked;

        /// <summary>vc_ui feedback state: what it remembers and whether it's due.</summary>
        internal static string StateForTest() =>
            $"asked={(Memory.Asked ? 1 : 0)} played={Memory.PlayedSeconds:0}s hutReachedLevel2={s_ask.HutReachedLevel2} colony={s_hasColony} " +
            $"armed={s_armed} forcedDue={s_forcedDue} setting={ColonyConfig.AskForFeedback.Value} file={FilePath}";
    }
}
