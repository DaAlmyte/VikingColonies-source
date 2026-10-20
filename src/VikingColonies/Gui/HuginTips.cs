using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Gui
{
    /// <summary>
    /// Hugin, the game's raven, explains the colony as you play (0.11): each tip is one of the game's tutorial texts,
    /// shown once per character, only with the game's tutorials on and the mod's Messages.HuginTips on. From the
    /// project review of 2026-10: a new player found out what a colony needs only when it stalled.
    /// </summary>
    internal static class HuginTips
    {
        public const string Colony = "vc_tip_colony", Builder = "vc_tip_builder", Needs = "vc_tip_needs", Worker = "vc_tip_worker",
            Night = "vc_tip_night", Hunger = "vc_tip_hunger", Death = "vc_tip_death", FreeTime = "vc_tip_freetime", Evening = "vc_tip_evening", Field = "vc_tip_field",
            Stock = "vc_tip_stock",
            Mood = "vc_tip_mood", Trophy = "vc_tip_trophy";

        /// <summary>A tip comes only to a player this near where it happened.</summary>
        private const float NearMetres = 40f;

        private static readonly string[] s_all = { Colony, Builder, Needs, Worker, Night, Hunger, Death, FreeTime, Evening, Field, Mood, Stock, Trophy };

        private static readonly AccessTools.FieldRef<List<Raven.RavenText>> s_tempTexts = TempTextsRef();

        private static AccessTools.FieldRef<List<Raven.RavenText>> TempTextsRef()
        {
            try { return AccessTools.StaticFieldRefAccess<List<Raven.RavenText>>(AccessTools.Field(typeof(Raven), "m_tempTexts")); }
            catch (System.Exception) { return null; }
        }

        /// <summary>Hugin keeps tips waiting until the player talks to him (the game marks a tip seen then).</summary>
        public static bool Queued(string tip)
        {
            List<Raven.RavenText> texts = s_tempTexts != null ? s_tempTexts() : null;
            return texts != null && texts.Any(t => t != null && t.m_key == tip);
        }

        /// <summary>Takes our tips off Hugin's waiting list.</summary>
        public static void Unqueue(string tip = null)
        {
            List<Raven.RavenText> texts = s_tempTexts != null ? s_tempTexts() : null;
            texts?.RemoveAll(t => t != null && t.m_key != null && (tip == null ? s_all.Contains(t.m_key) : t.m_key == tip));
        }

        /// <summary>Joining a world: tips queued for another character don't follow (review of 0.11).</summary>
        [HarmonyPatch]
        private static class ClearQueuedHook
        {
            [HarmonyPatch(typeof(Game), "Start"), HarmonyPostfix]
            private static void ClearQueued() => Unqueue();
        }

        /// <summary>The game's tutorial list is built in its scene: ours go in with it.</summary>
        [HarmonyPatch]
        private static class AddTextsHook
        {
            [HarmonyPatch(typeof(Tutorial), "Awake"), HarmonyPostfix]
            private static void AddTexts(Tutorial __instance)
            {
                if (__instance == null || __instance.m_texts == null) return;
                foreach (string name in s_all)
                {
                    if (__instance.m_texts.Any(t => t != null && t.m_name == name)) continue;
                    __instance.m_texts.Add(new Tutorial.TutorialText
                    {
                        m_name = name,
                        m_topic = "$" + name + "_topic",
                        m_label = "$" + name + "_topic",
                        m_text = "$" + name,
                        m_globalKeyTrigger = "",
                        m_tutorialTrigger = "",
                    });
                }
            }
        }

        /// <summary>Whether the game's tutorial list has every tip (the self-test checks it).</summary>
        public static bool Registered => Tutorial.instance != null && s_all.All(n => Tutorial.instance.m_texts.Any(t => t != null && t.m_name == n));

        public static IEnumerable<string> All => s_all;

        /// <summary>Shows the tip to the local player once (the game remembers it with the character).</summary>
        public static void Show(string tip)
        {
            Player player = Player.m_localPlayer;
            if (player == null || !ColonyConfig.HuginTips.Value || player.HaveSeenTutorial(tip) || Queued(tip)) return;
            player.ShowTutorial(tip);
        }

        /// <summary>As <see cref="Show"/>, when the local player is near <paramref name="where"/>.</summary>
        public static void ShowNear(Vector3 where, string tip)
        {
            Player player = Player.m_localPlayer;
            if (player != null && Vector3.Distance(player.transform.position, where) <= NearMetres) Show(tip);
        }

        /// <summary>
        /// Every few seconds (<see cref="SettlerWords"/>' clock): a player carrying a trophy the nearest Town Hall they may
        /// manage, within 100 m, needs for its next level learns what it's for (1.4).
        /// </summary>
        public static void CheckTrophy()
        {
            Player player = Player.m_localPlayer;
            if (player == null || !ColonyConfig.HuginTips.Value || !Raven.m_tutorialsEnabled || !ColonyConfig.HallLevels.Value
                || player.HaveSeenTutorial(Trophy) || Queued(Trophy)) return;
            Vector3 me = player.transform.position;
            ColonyBuilding hall = ColonyBuilding.TownHalls().Where(h => Vector3.Distance(h.transform.position, me) <= 100f)
                .OrderBy(h => Vector3.Distance(h.transform.position, me)).FirstOrDefault();
            if (hall == null || !ColonyPermissions.LocalMayManage(hall)) return;
            if (TownHallLevels.TrophiesFor(hall.Level + 1).Any(t => player.GetInventory().GetItem(t, -1, true) != null)) Show(Trophy);
        }

        /// <summary>Every few seconds (<see cref="SettlerWords"/>' clock): the first night near a colony with settlers.</summary>
        public static void CheckNight()
        {
            Player player = Player.m_localPlayer;
            // Cheap checks first: with tips off this never looks further (review of 0.11).
            if (player == null || EnvMan.instance == null || !ColonyConfig.HuginTips.Value || !Raven.m_tutorialsEnabled || !EnvMan.IsNight()
                || player.HaveSeenTutorial(Night) || Queued(Night)) return;
            Vector3 me = player.transform.position;
            ColonyBuilding hall = ColonyBuilding.TownHalls().OrderBy(h => Vector3.Distance(h.transform.position, me)).FirstOrDefault();
            if (hall == null) return;
            int settlers = Citizen.All.Any(c => c != null && c.Record.ColonyId == hall.ColonyId) ? 1 : 0;
            if (TipRules.NightNearColony(EnvMan.IsNight(), Vector3.Distance(hall.transform.position, me), HallRules.PerksAt(hall.Level).Radius, settlers))
                Show(Night);
        }
    }

    /// <summary>
    /// Words above a settler's head when he needs the player (0.11): "Needs 12 Wood", "Storage full", "Hungry"… on the
    /// local player's computer, for settlers near the player, with the game's own NPC speech. Plain work shows nothing.
    /// </summary>
    internal sealed class SettlerWords : MonoBehaviour
    {
        private const float EverySeconds = 2f;
        private const float NearMetres = 15f;
        /// <summary>Small talk is heard this near (0.14); a greeting comes within <see cref="FreeTimeRules.LookAtPlayer"/>.</summary>
        private const float TalkMetres = 10f;
        private const float GreetSeconds = 4f;
        private const float LineSeconds = 5f;
        private static readonly Vector3 AboveHead = new Vector3(0f, 2.3f, 0f);
        private readonly Dictionary<Citizen, string> _shown = new Dictionary<Citizen, string>();
        private readonly GreetMemory _greeted = new GreetMemory();
        private readonly Dictionary<Citizen, float> _greetUntil = new Dictionary<Citizen, float>();
        private readonly Dictionary<Citizen, string> _greeting = new Dictionary<Citizen, string>();
        private readonly Dictionary<Citizen, float> _talkNext = new Dictionary<Citizen, float>();
        private readonly Dictionary<Citizen, float> _talkUntil = new Dictionary<Citizen, float>();
        private readonly Dictionary<Citizen, string> _talkLine = new Dictionary<Citizen, string>();
        private readonly Dictionary<string, SagaLog> _sagas = new Dictionary<string, SagaLog>();
        private float _timer;
        private float _screenTimer;
        /// <summary>Words shown are looked at this often: gone the moment their settler leaves the screen.</summary>
        private const float ScreenCheckSeconds = 0.25f;

        public static int ShownCount { get; private set; }
        /// <summary>Greetings and lines of small talk shown on this computer (the self-test counts them).</summary>
        public static int GreetingsShown { get; private set; }
        public static int TalkShown { get; private set; }
        /// <summary>The last words shown above any settler's head here, and the last line of small talk (the self-test reads them).</summary>
        public static string LastWords { get; private set; } = "";
        public static string LastTalk { get; private set; } = "";
        public static string LastGreeting { get; private set; } = "";

        private void Update()
        {
            _screenTimer -= Time.deltaTime;
            if (_screenTimer <= 0f)
            {
                _screenTimer = ScreenCheckSeconds;
                HideOffScreen();
            }
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = EverySeconds;
            HuginTips.CheckNight();
            HuginTips.CheckTrophy();
            Refresh();
        }

        /// <summary>
        /// Whether a settler's head is on the screen (WordsOnScreen): the game draws a speaker's words even when they stand
        /// just out of view, and they piled up at odd places (the player, 2026-10-19).
        /// </summary>
        private static bool OnScreen(Citizen c)
        {
            Camera camera = Utils.GetMainCamera();
            if (camera == null) return false;
            Vector3 at = camera.WorldToViewportPoint(c.transform.position + AboveHead);
            return WordsOnScreen.Shows(at.x, at.y, at.z);
        }

        /// <summary>Takes the words off a settler who has left the screen since they were shown.</summary>
        private void HideOffScreen()
        {
            if (_shown.Count == 0) return;
            Chat chat = Chat.instance;
            foreach (Citizen gone in _shown.Keys.Where(c => c == null || !OnScreen(c)).ToList())
            {
                if (gone != null) chat?.ClearNpcText(gone.gameObject);
                _shown.Remove(gone);
            }
        }

        private void Refresh()
        {
            Chat chat = Chat.instance;
            Player player = Player.m_localPlayer;
            var now = new Dictionary<Citizen, string>();
            var needs = new HashSet<Citizen>();   // words about what they need, ahead of small talk when only a few show
            if (chat != null && player != null)
            {
                Vector3 me = player.transform.position;
                _sagas.Clear();
                foreach (Citizen c in Citizen.All)
                {
                    if (c == null) continue;
                    float distance = Vector3.Distance(c.transform.position, me);
                    if (distance > NearMetres) continue;
                    CitizenRecord record = c.Record;
                    string words = HelpWords.Words(record.Status, record.Hungry && ColonyConfig.SettlersEat.Value);
                    if (words == HelpWords.HungryToken)
                    {
                        HuginTips.Show(HuginTips.Hunger);   // with the words off too
                        // Now and then, his talk in between (0.18: "Hungry" stuck over his head all day, through his chats).
                        if (!HelpWords.HungerShows(WorldSeconds(), c.Id)) words = "";
                    }
                    string moodWords = MoodWords(c, record);
                    if (moodWords.Length > 0) HuginTips.Show(HuginTips.Mood);   // with the words off too (0.18: it came only with them on)
                    if (!ColonyConfig.WordsAboveHeads.Value) continue;
                    if (!OnScreen(c)) continue;   // a greeting or a line no one sees isn't used up either
                    if (words.Length > 0)   // a need always comes first
                    {
                        now[c] = words;
                        needs.Add(c);
                    }
                    else if (moodWords.Length > 0 && MoodWordsDue(c))   // then an unhappy settler's worst reason (0.17)
                    {
                        now[c] = moodWords;
                        needs.Add(c);
                    }
                    else if (ColonyConfig.SmallTalk.Value)
                    {
                        string talk = Talk(c, record, distance, player);
                        if (talk.Length > 0) now[c] = talk;
                    }
                }
            }
            // Only settlers on the screen, the nearest few (WordsOnScreen).
            if (now.Count > 0 && player != null)
            {
                // Needs first, then the nearest (the review of 1.4.1: small talk could crowd out a "Hungry").
                List<Citizen> seen = now.Keys.Where(OnScreen).ToList();
                Vector3 me = player.transform.position;
                List<int> nearest = WordsOnScreen.Nearest(seen.Select(c => Vector3.Distance(c.transform.position, me) + (needs.Contains(c) ? 0f : 1000f)).ToList(),
                    WordsOnScreen.MostAtOnce);
                var kept = new HashSet<Citizen>(nearest.Select(i => seen[i]));
                foreach (Citizen hidden in now.Keys.Where(c => !kept.Contains(c)).ToList()) now.Remove(hidden);
            }
            foreach (KeyValuePair<Citizen, string> gone in _shown.Where(s => s.Key != null && !now.ContainsKey(s.Key)).ToList())
                chat?.ClearNpcText(gone.Key.gameObject);
            // Drawn again only when the words change or the game dropped them: a redraw fades in anew (review: they blinked).
            foreach (KeyValuePair<Citizen, string> say in now)
                if (!_shown.TryGetValue(say.Key, out string was) || was != say.Value || !chat.IsDialogVisible(say.Key.gameObject))
                    chat.SetNpcText(say.Key.gameObject, AboveHead, NearMetres + 5f, 0f, "", Localization.instance.Localize(say.Value), false);
            _shown.Clear();
            foreach (KeyValuePair<Citizen, string> say in now) _shown[say.Key] = say.Value;
            ShownCount = now.Count;
            if (now.Count > 0) LastWords = Localization.instance.Localize(now.Values.Last());
            Forget(_greetUntil);
            Forget(_greeting);
            Forget(_talkNext);
            Forget(_talkUntil);
            Forget(_talkLine);
        }

        /// <summary>
        /// A greeting the first time each day this player comes near him (unless he is asleep, sheltering or fleeing), then
        /// small talk while he is in free time: a chatting pair takes turns, a line each 5 seconds; anyone else says a line
        /// every 2-4 minutes. "" for nothing to say now.
        /// </summary>
        private string Talk(Citizen c, CitizenRecord record, float distance, Player player)
        {
            float now = Time.time;
            if (_greetUntil.TryGetValue(c, out float greetUntil) && now < greetUntil) return _greeting[c];
            int today = EnvMan.instance != null && ZNet.instance != null ? EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) : 0;
            if (!HelpWords.IsResting(record.Status) && _greeted.ShouldGreet(c.Id, player.GetPlayerID(), today, distance))
            {
                _greeted.Mark(c.Id, player.GetPlayerID(), today);
                string greeting = string.Format(Localization.instance.Localize("$vc_greet"), player.GetPlayerName());
                _greetUntil[c] = now + GreetSeconds;
                _greeting[c] = greeting;
                LastGreeting = greeting;
                GreetingsShown++;
                return greeting;
            }
            if (distance > TalkMetres || !ActivityWords.Decode(c.Doing, out string doing, out string partner, out string partnerId)) return "";
            TalkContext context = Context(record, today);
            context.Trait = c.TraitKind;   // the cheerful say cheerful things, the grumpy grumble (0.17)
            if (ColonyConfig.Mood.Value) context.Mood = c.MoodLevel;   // the cheerful only when content or better (0.18)
            context.Evening = doing == ActivityWords.Token(Leisure.Evening);
            if (doing == ActivityWords.Token(Leisure.Chat))
            {
                // Turns: the one whose name comes first speaks in the even 5-second slots, the other in the odd ones; the
                // world's clock, so every player sees the same line (review of 0.14).
                int slot = (int)(WorldSeconds() / LineSeconds);
                // By id: two of one name took the same turns (0.18); names for what a 0.17 computer wrote.
                bool asks = partnerId.Length > 0 ? ChatPairing.Asks(c.Id, partnerId) : ChatPairing.Asks(record.Name, partner);
                if ((slot % 2 == 0) != asks) return "";
                TalkShown++;
                return LastTalk = Say(context, SmallTalk.Seed(c.Id, slot));
            }
            if (_talkUntil.TryGetValue(c, out float talkUntil) && now < talkUntil) return _talkLine[c];
            if (!_talkNext.TryGetValue(c, out float next))
            {
                _talkNext[c] = now + Random.Range(3f, 20f);   // the first line comes soon after he's seen in free time
                return "";
            }
            if (now < next) return "";
            _talkNext[c] = now + Random.Range(120f, 240f) * Traits.TalkEvery(c.TraitKind);   // the chatty more, the quiet less (0.16)
            string line = Say(context, SmallTalk.Seed(c.Id, (int)(WorldSeconds() / LineSeconds)));
            _talkUntil[c] = now + LineSeconds;
            _talkLine[c] = line;
            TalkShown++;
            return LastTalk = line;
        }

        /// <summary>An unhappy or miserable settler's worst reason, in the player's words ("We lost Ingrid."); "" for none.</summary>
        private static string MoodWords(Citizen c, CitizenRecord record)
        {
            if (!ColonyConfig.Mood.Value || !record.HasColony || !MoodLevels.ShowsWords(c.MoodLevel)) return "";
            MoodReason? worst = MoodLevels.Worst(MoodLevels.Decode(record.WhyMood));
            if (worst == null) return "";
            string text = Localization.instance.Localize(MoodLevels.WordsToken(worst.Value));
            if (text.IndexOf("{0}", System.StringComparison.Ordinal) < 0) return text;
            try { return string.Format(text, worst.Value.Name); }
            catch (System.FormatException) { return text; }
        }

        /// <summary>He says it for 6 seconds in every minute, at his own moment by the world's clock (the same on every computer).</summary>
        private static bool MoodWordsDue(Citizen c)
        {
            int slot = (int)(WorldSeconds() / 6.0);
            return ((slot + (SmallTalk.Seed(c.Id, 0) & 0x7fffffff)) % 10) == 0;
        }

        private static double WorldSeconds() => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time;

        private TalkContext Context(CitizenRecord record, int today)
        {
            if (!_sagas.TryGetValue(record.ColonyId, out SagaLog saga))
            {
                ColonyBuilding hall = ColonyBuilding.FindTownHall(record.ColonyId);
                TownHallModule module = hall != null ? hall.GetComponent<TownHallModule>() : null;
                _sagas[record.ColonyId] = saga = module != null ? module.Saga : new SagaLog();
            }
            TalkContext context = SmallTalk.FromSaga(saga, today, record.Name);
            context.Hungry = record.Hungry && ColonyConfig.SettlersEat.Value;
            context.Wet = EnvMan.IsWet();
            context.WaitStatus = record.Status ?? "";
            context.Jobless = !record.IsEmployed;
            return context;
        }

        /// <summary>A line in the player's words, with the name or building it is about.</summary>
        private static string Say(TalkContext context, int seed)
        {
            var rng = new System.Random(seed);
            TalkTopic topic = SmallTalk.Topic(context, rng.NextDouble);
            TalkLine line = SmallTalk.Line(topic, context.WaitStatus, rng.Next(), context);
            string text = Localization.instance.Localize(line.Token);
            if (line.Arg.Length == 0) return text;
            try
            {
                return string.Format(text, Localization.instance.Localize(line.Arg));
            }
            catch (System.FormatException)
            {
                return text;
            }
        }

        private static void Forget<T>(Dictionary<Citizen, T> map)
        {
            if (map.Count == 0) return;
            foreach (Citizen gone in map.Keys.Where(k => k == null).ToList()) map.Remove(gone);
        }

        /// <summary>Self-test: the words above his head now, as a token or text ("" for none).</summary>
        internal static string ShownForTest(Citizen c)
        {
            SettlerWords words = FindAnyObjectByType<SettlerWords>();
            return words != null && c != null && words._shown.TryGetValue(c, out string said) ? said : "";
        }

        /// <summary>Self-test: look again now.</summary>
        internal static void RefreshForTest()
        {
            SettlerWords words = FindAnyObjectByType<SettlerWords>();
            if (words != null) words.Refresh();
        }
    }
}
