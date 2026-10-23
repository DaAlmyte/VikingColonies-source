using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Citizens;
using VikingColonies.Colony;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Buildings
{
    /// <summary>Where a trophy is shown at the Town Hall (1.4).</summary>
    /// <summary>How a Forsaken counts at a hall: not, its trophy in the chest or on a stand, or beaten in this world.</summary>
    public enum TrophyPlace { None, Chest, Stand, Beaten }

    /// <summary>Founds the colony (adopt + top-up) and keeps the colony roster. Logic runs on the Town Hall's owner.</summary>
    public class TownHallModule : MonoBehaviour
    {
        private const string FoundedKey = SaveKeys.Founded;
        private const string RosterKey = SaveKeys.Roster;
        private const float TickSeconds = 3f;
        private const float RosterRewriteSeconds = 15f;
        private const int MissingDaysBeforeRemoval = 3;
        private const string NewcomerDayKey = SaveKeys.NewcomerDay;
        private const string SagaKey = SaveKeys.Saga;
        private const string RpcSaga = "vc_Saga";
        /// <summary>A saga line holds at most this many words, each at most this long (anyone may send one).</summary>
        private const int MaxSagaWords = 6, MaxSagaWordLength = 64;
        private const float NewcomerCheckSeconds = 10f;
        private const string KeepsakesKey = SaveKeys.Keepsakes;
        private const string RpcBringBack = "vc_BringBack", RpcBringBackResult = "vc_BringBackResult";
        private const string RpcAlarm = "vc_Alarm";
        private const string RpcRaise = "vc_RaiseHall", RpcRaiseResult = "vc_RaiseHallResult";
        /// <summary>A trophy counts as shown on an item stand this near the Town Hall (1.4).</summary>
        public const float TrophyRange = 10f;

        private ColonyBuilding _building;
        private ZNetView _nview;
        private float _timer;
        private float _lastRosterWrite;
        private int _ticksSinceLoad;
        private bool _rosterDirty;
        private float _newcomerTimer = NewcomerCheckSeconds;

        public bool Founded => _nview != null && _nview.IsValid() && _nview.GetZDO().GetBool(FoundedKey, false);

        public ColonyRoster Roster =>
            _nview != null && _nview.IsValid() ? ColonyRoster.Decode(_nview.GetZDO().GetString(RosterKey, "")) : new ColonyRoster();

        /// <summary>
        /// What the colony keeps of each member, so one whose body was lost can be brought back (see <see cref="Keepsake"/>).
        /// Read again only when the Town Hall's saved data has changed (1.4.2: every settler read it every 2 s). Shared: to
        /// change it, decode a copy (as the roster's update does).
        /// </summary>
        public Keepsakes Keepsakes =>
            _nview != null && _nview.IsValid()
                ? _keepsakes.Get(_nview.GetZDO().DataRevision, () => Keepsakes.Decode(_nview.GetZDO().GetString(KeepsakesKey, "")))
                : new Keepsakes();

        private readonly RevisionCache<Keepsakes> _keepsakes = new RevisionCache<Keepsakes>();

        /// <summary>The Citizens tab's Bring back: asks the Town Hall's owner to bring a missing member back from his keepsake.</summary>
        public void RequestBringBack(string citizenId) => _nview.InvokeRPC(RpcBringBack, citizenId ?? "");

        /// <summary>
        /// The colony's saga: what happened here, newest first (see <see cref="Colony.Saga"/>). Read again only when the
        /// Town Hall's saved data has changed (0.18: small talk read it every 2 s, and mood once a minute for each settler).
        /// </summary>
        public SagaLog Saga
        {
            get
            {
                if (_nview == null || !_nview.IsValid()) return new SagaLog();
                ZDO zdo = _nview.GetZDO();
                if (_saga == null || zdo.DataRevision != _sagaRevision)
                {
                    _saga = SagaLog.Decode(zdo.GetString(SagaKey, ""));
                    _sagaRevision = zdo.DataRevision;
                }
                return _saga;
            }
        }

        private SagaLog _saga;
        private uint _sagaRevision;

        /// <summary>Adds a saga line, dated today: on the Town Hall's owner (from anywhere, the request goes there).</summary>
        public void Record(string kind, params string[] args)
        {
            if (_nview == null || !_nview.IsValid() || string.IsNullOrEmpty(kind)) return;
            var pkg = new ZPackage();
            pkg.Write(kind);
            string[] words = (args ?? new string[0]).Take(MaxSagaWords).ToArray();
            pkg.Write(words.Length);
            foreach (string word in words) pkg.Write(word ?? "");
            _nview.InvokeRPC(RpcSaga, pkg);   // runs on the owner: right here when that's us
        }

        private void RPC_Saga(long sender, ZPackage pkg)
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
            string kind = pkg.ReadString();
            if (!SagaLog.IsKnownKind(kind)) return;   // a modified game's request: not written
            int count = Mathf.Clamp(pkg.ReadInt(), 0, MaxSagaWords);
            var words = new string[count];
            for (int i = 0; i < count; i++)
            {
                string word = pkg.ReadString() ?? "";
                words[i] = word.Length > MaxSagaWordLength ? word.Substring(0, MaxSagaWordLength) : word;
            }
            int today = EnvMan.instance != null && ZNet.instance != null ? EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) : 0;
            SagaLog saga = Saga;
            saga.Add(new SagaEntry(today, kind, words));
            _nview.GetZDO().Set(SagaKey, saga.Encode());
            Log.Event($"Colony {_building?.ColonyId}: saga, day {today}: {kind} {string.Join(", ", words)}");
        }

        private void Awake()
        {
            _building = GetComponent<ColonyBuilding>();
            _nview = GetComponent<ZNetView>();
            if (_nview != null && _nview.IsValid())
            {
                _nview.Register<ZPackage>(RpcSaga, RPC_Saga);
                _nview.Register<string>(RpcBringBack, RPC_BringBack);
                _nview.Register<string, string>(RpcBringBackResult, RPC_BringBackResult);
                _nview.Register<bool, Vector3>(RpcAlarm, RPC_Alarm);
                _nview.Register<int>(RpcRaise, RPC_Raise);
                _nview.Register<string, string>(RpcRaiseResult, RPC_RaiseResult);
            }
            ApplyLevel();
        }

        private int _appliedLevel = -1;
        private float _appliedRadius = -1f;
        private float _levelCheck;

        /// <summary>
        /// Huts may be placed as far as the colony's area reaches: the station's range follows the Town Hall's level (1.4)
        /// and the colony radius, which may come from the server's settings after the pieces were registered.
        /// </summary>
        private void ApplyLevel()
        {
            CraftingStation station = GetComponent<CraftingStation>();
            if (station == null || _nview == null || !_nview.IsValid()) return;
            ZDO zdo = _nview.GetZDO();
            int level = TownHallLevels.Clamp(zdo.GetInt(ColonyBuilding.LevelKey, 1));   // its own copy: ColonyBuilding may not have woken yet
            float radius = HallRules.PerksAt(level).Radius;
            if (level == _appliedLevel && Mathf.Approximately(radius, _appliedRadius)) return;
            // Only a raise just made is news (review: a Town Hall loading at level 3 said "now level 3").
            if (TownHallLevels.JustRaised(_appliedLevel, level, zdo.GetFloat(SaveKeys.HallRaisedAt, 0f), Now)) Celebrate(level);
            _appliedLevel = level;
            _appliedRadius = radius;
            station.m_rangeBuild = ColonyRules.HutBuildRange(radius);
            station.m_discoverRange = radius;
        }

        private void OnEnable()
        {
            ColonyEvents.CitizenSpawned += MarkDirty;
            ColonyEvents.CitizenHired += MarkDirty;
            ColonyEvents.CitizenFired += MarkDirty;
            ColonyEvents.CitizenDied += OnCitizenDied;
        }

        private void OnDisable()
        {
            ColonyEvents.CitizenSpawned -= MarkDirty;
            ColonyEvents.CitizenHired -= MarkDirty;
            ColonyEvents.CitizenFired -= MarkDirty;
            ColonyEvents.CitizenDied -= OnCitizenDied;
        }

        // ---- Town Hall levels (1.4): the Hall of Trophies ----

        private static readonly List<Piece> s_stands = new List<Piece>();

        /// <summary>Where each Forsaken's trophy is shown at this hall now: in its chest, on an item stand within 10 m, or not.</summary>
        public Dictionary<string, TrophyPlace> TrophyPlaces()
        {
            var places = TownHallLevels.Trophies.ToDictionary(t => t, t => TrophyPlace.None);
            s_stands.Clear();
            Piece.GetAllPiecesInRadius(transform.position, TrophyRange + 8f, s_stands);   // a stand high on a wall counts by where it hangs over
            foreach (Piece piece in s_stands)
            {
                if (piece == null || Utils.DistanceXZ(piece.transform.position, transform.position) > TrophyRange) continue;
                // A stand keeps its item as the prefab name's hash.
                ItemStand stand = piece != null ? piece.GetComponentInChildren<ItemStand>() : null;
                int item = stand != null ? stand.GetAttachedItem() : 0;
                if (item == 0) continue;
                foreach (string trophy in TownHallLevels.Trophies)
                    if (trophy.GetStableHashCode() == item) places[trophy] = TrophyPlace.Stand;
            }
            StorageModule chest = _building != null ? _building.Storage : null;
            if (chest != null)
                foreach (string trophy in TownHallLevels.Trophies)
                    if (places[trophy] == TrophyPlace.None && chest.HasItem(trophy)) places[trophy] = TrophyPlace.Chest;
            // A Forsaken the world has beaten counts too (review of 1.4: trophies hung on the sacrificial stones can't be
            // taken back, and players coming from 1.0 had hung theirs there).
            if (ColonyConfig.HallBeatenBossesCount.Value && ZoneSystem.instance != null)
                foreach (string trophy in TownHallLevels.Trophies)
                    if (places[trophy] == TrophyPlace.None && ZoneSystem.instance.GetGlobalKey(TownHallLevels.BossKey(trophy)))
                        places[trophy] = TrophyPlace.Beaten;
            return places;
        }

        public TrophyPlace WhereShown(string trophy) => Where(TrophyPlaces(), trophy);

        public static TrophyPlace Where(Dictionary<string, TrophyPlace> places, string trophy) =>
            !string.IsNullOrEmpty(trophy) && places.TryGetValue(trophy, out TrophyPlace place) ? place : TrophyPlace.None;

        /// <summary>The first of <paramref name="level"/>'s trophies not shown here; null when all are.</summary>
        public static string MissingTrophy(Dictionary<string, TrophyPlace> places, int level) =>
            TownHallLevels.MissingTrophy(level, t => Where(places, t) != TrophyPlace.None);

        /// <summary>What the Town Hall's chest holds, by prefab name.</summary>
        public Dictionary<string, int> ChestContents() =>
            _building != null && _building.Storage != null ? _building.Storage.Contents() : new Dictionary<string, int>();

        /// <summary>Whether the hall may be raised a level now, as this computer sees it.</summary>
        public RaiseLock RaiseCheck() => RaiseCheck(TrophyPlaces());

        /// <summary>As <see cref="RaiseCheck()"/>, with the trophies' places already looked up.</summary>
        /// <param name="seenLevel">The level the asking player saw; -1: as it is now.</param>
        public RaiseLock RaiseCheck(Dictionary<string, TrophyPlace> places, int seenLevel = -1)
        {
            int level = _building != null ? _building.Level : 1;
            int next = level + 1;
            Dictionary<string, int> contents = ChestContents();
            return TownHallLevels.CanRaise(ColonyConfig.HallLevels.Value, level, MissingTrophy(places, next) == null,
                HallRules.Cost(next), item => contents.TryGetValue(item, out int n) ? n : 0,
                item => ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(item) != null,
                TownHallLevels.UnreadableParts(ColonyConfig.HallLevelCost(next)).Count, seenLevel);
        }

        /// <summary>
        /// Test tool (vc_build halllevel): the hall's level set at once, with no trophies or materials, and its camp woken, as a
        /// Raise does (the hub, 2026-10-20: `vc_build upgrade` built a level-5 hall's camp at level 1). No saga line, no
        /// celebration and no "rose to level" log line: a scenario waits for "vc_build halllevel:" instead.
        /// </summary>
        internal string SetLevelForTest(int level)
        {
            if (_nview == null || !_nview.IsValid() || _building == null) return "no Town Hall here.";
            if (!_nview.IsOwner()) _nview.ClaimOwnership();   // a test tool: this computer takes the hall (its review: a dead end otherwise)
            int was = _building.Level;
            level = TownHallLevels.Clamp(level);
            _building.SetLevel(level);
            ApplyLevel();
            GetComponent<Construction.ConstructionModule>()?.CampSoon();
            Log.Info($"vc_build halllevel: the Town Hall of colony {_building.ColonyId} goes from level {was} to {level}");
            return $"the Town Hall is now level {level} (from {was}); its camp grows a level at a time while a Builder's Hut allows it, and never goes down.";
        }

        /// <summary>Test tool: a Raise as a player who saw <paramref name="seenLevel"/> (a double click).</summary>
        internal void RaiseForTest(int seenLevel)
        {
            if (_nview != null && _nview.IsValid()) _nview.InvokeRPC(RpcRaise, seenLevel);
        }

        /// <summary>The Hall tab's Raise: the Town Hall's owner checks again, takes the materials and raises the level.</summary>
        public void RequestRaise()
        {
            if (_nview != null && _nview.IsValid() && _building != null) _nview.InvokeRPC(RpcRaise, _building.Level);   // the level the player saw
        }

        private void RPC_Raise(long sender, int seenLevel)
        {
            if (_nview == null || !_nview.IsValid() || _building == null) return;
            if (!_nview.IsOwner())
            {
                Answer(sender, "$vc_hall_raise_busy", "");   // the chest changed hands on the way (review of 1.4: no answer at all)
                return;
            }
            if (!ColonyPermissions.SenderMayManage(_building, sender))
            {
                Answer(sender, "$vc_hall_raise_not_yours", "");
                return;
            }
            StorageModule chest = _building.Storage;
            // The chest's latest contents, and nobody else changing them meanwhile (review focus: a player has it open).
            if (chest == null || chest.Claim() != StorageModule.Access.Ready)
            {
                Answer(sender, "$vc_hall_raise_busy", "");
                return;
            }
            int level = _building.Level;
            int next = level + 1;
            Dictionary<string, TrophyPlace> places = TrophyPlaces();
            RaiseLock why = RaiseCheck(places, seenLevel);
            if (why != RaiseLock.None)
            {
                Answer(sender, RefusalToken(why), why == RaiseLock.NoTrophy ? TownHallLevels.BossToken(MissingTrophy(places, next))
                    : why == RaiseLock.Already ? level.ToString() : next.ToString());
                return;
            }
            if (!chest.TakeAll(HallRules.Cost(next)))
            {
                Answer(sender, "$vc_hall_raise_materials", next.ToString());
                return;
            }
            _building.SetLevel(next);
            _nview.GetZDO().Set(SaveKeys.HallRaisedAt, (float)Now);   // every computer that sees it within seconds celebrates
            Record("raised", next.ToString(), TownHallLevels.BossesToken(next));
            Log.Event($"Colony {_building.ColonyId}: the Town Hall rose to level {next} ({string.Join(", ", TownHallLevels.TrophiesFor(next))} shown)");
            ApplyLevel();   // here at once (the effect and the message); other computers follow within 2 s
            GetComponent<Construction.ConstructionModule>()?.CampSoon();   // the camp's next level (huts round 2)
            Answer(sender, "$vc_hall_raised_you", next.ToString());
        }

        private void Answer(long sender, string token, string arg)
        {
            if (sender == ZNet.GetUID() || sender == 0L) RPC_RaiseResult(sender, token, arg);
            else _nview.InvokeRPC(sender, RpcRaiseResult, token, arg);
        }

        private void RPC_RaiseResult(long sender, string token, string arg)
        {
            string text = SafeText.Format(Localization.instance.Localize(token), Localization.instance.Localize(arg ?? ""));   // the token is the sender's
            if (Gui.Windows.TownHall.IsOpen) Gui.Windows.TownHall.ShowHallMessage(text);
            else if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.Center, text);
        }

        internal static string RefusalToken(RaiseLock why)
        {
            switch (why)
            {
                case RaiseLock.Off: return "$vc_hall_off";
                case RaiseLock.MaxLevel: return "$vc_hall_top";
                case RaiseLock.NoTrophy: return "$vc_hall_raise_no_trophy";
                case RaiseLock.UnknownItem: return "$vc_hall_raise_unknown";
                case RaiseLock.Already: return "$vc_hall_raise_already";
                default: return "$vc_hall_raise_materials";
            }
        }

        /// <summary>The hall went up a level: players in the colony's area hear of it, and the game's own effect plays there.</summary>
        /// <summary>Test tool: how many raises this computer has celebrated.</summary>
        internal static int CelebratedForTest;

        private void Celebrate(int level)
        {
            CelebratedForTest++;
            Player me = Player.m_localPlayer;
            if (me != null && ColonyAreas.ColonyAt(me.transform.position) == _building.ColonyId)
                me.Message(MessageHud.MessageType.Center,
                    string.Format(Localization.instance.Localize("$vc_hall_raised_all"), level, Gui.HallTab.Perks(HallRules.PerksAt(level))));
            GameObject fx = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(RaiseEffect) : null;
            // A networked effect is made once, by the owner (the others get it from the network); a local one on every computer.
            if (fx != null && (fx.GetComponent<ZNetView>() == null || _nview.IsOwner())) Instantiate(fx, transform.position + Vector3.up, Quaternion.identity);
        }

        /// <summary>The game's effect when a Forsaken power is taken at the stones.</summary>
        internal const string RaiseEffect = "fx_GP_Activation";

        // ---- The alarm (1.3) ----

        private static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0;

        /// <summary>The alarm sounds: everyone else shelters, the guards gather where it was sounded (the same on every computer).</summary>
        public bool AlarmOn => _nview != null && _nview.IsValid() && ColonyAlarm.Active(_nview.GetZDO().GetFloat(SaveKeys.AlarmUntil, 0f), Now);

        public Vector3 AlarmPoint => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetVec3(SaveKeys.AlarmAt, transform.position) : transform.position;

        /// <summary>A colony manager sounds the alarm where he stands, or calls it off.</summary>
        public void RequestAlarm(bool on, Vector3 at)
        {
            if (_nview != null && _nview.IsValid()) _nview.InvokeRPC(RpcAlarm, on, at);
        }

        private void RPC_Alarm(long sender, bool on, Vector3 at)
        {
            if (!_nview.IsOwner() || !ColonyPermissions.SenderMayManage(_building, sender)) return;
            if (Utils.DistanceXZ(at, transform.position) > ColonyConfig.MaxReach.Value) at = transform.position;   // past the colony's reach: at the Town Hall
            _nview.GetZDO().Set(SaveKeys.AlarmUntil, on ? (float)ColonyAlarm.Until(Now) : 0f);
            if (on) _nview.GetZDO().Set(SaveKeys.AlarmAt, at);
            Log.Event($"Colony {_building.ColonyId}: {(on ? $"the alarm sounds at ({at.x:0.0}, {at.z:0.0})" : "all clear")}");
        }

        /// <summary>
        /// The alarm of this colony, if it sounds and its Town Hall is loaded here; and where. Looked up at most twice a second
        /// per colony (review of 1.3: every settler asked each physics step, a search of all buildings each time).
        /// </summary>
        public static bool ColonyAlarmFor(string colonyId, out Vector3 at)
        {
            at = Vector3.zero;
            if (string.IsNullOrEmpty(colonyId)) return false;
            if (!s_alarmSeen.TryGetValue(colonyId, out AlarmSeen seen)) s_alarmSeen[colonyId] = seen = new AlarmSeen { At = float.MinValue };
            float now = Time.time;
            if (now - seen.At >= AlarmLookSeconds || now < seen.At)
            {
                ColonyBuilding hall = ColonyBuilding.FindTownHall(colonyId);
                TownHallModule module = hall != null ? hall.GetComponent<TownHallModule>() : null;
                seen.At = now;
                seen.On = module != null && module.AlarmOn;
                seen.Point = seen.On ? module.AlarmPoint : Vector3.zero;
            }
            at = seen.Point;
            return seen.On;
        }

        private const float AlarmLookSeconds = 0.5f;

        private sealed class AlarmSeen
        {
            public float At;
            public bool On;
            public Vector3 Point;
        }

        private static readonly Dictionary<string, AlarmSeen> s_alarmSeen = new Dictionary<string, AlarmSeen>();

        private bool _alarmWas;

        /// <summary>On every computer: a player in the colony's area is told when the alarm starts and ends.</summary>
        private void TellAlarm()
        {
            bool on = AlarmOn;
            if (on == _alarmWas) return;
            _alarmWas = on;
            Player me = Player.m_localPlayer;
            if (me == null || ColonyAreas.ColonyAt(me.transform.position) != _building.ColonyId) return;
            me.Message(MessageHud.MessageType.Center, Localization.instance.Localize(on ? "$vc_alarm_sounded" : "$vc_alarm_over"));
        }

        private void Update()
        {
            _levelCheck -= Time.deltaTime;
            if (_levelCheck <= 0f)
            {
                _levelCheck = 2f;
                ApplyLevel();   // raised on any computer: every computer's station follows
            }
            if (_nview != null && _nview.IsValid() && _building != null && !string.IsNullOrEmpty(_building.ColonyId)) TellAlarm();
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner())
            {
                _owned = false;
                return;
            }
            if (!_owned)
            {
                _owned = true;
                _raidWatch.Restart();   // a raid already on now counts for nothing (review I3)
            }
            _newcomerTimer -= Time.deltaTime;
            if (_newcomerTimer <= 0f)
            {
                _newcomerTimer = NewcomerCheckSeconds;
                if (Founded) WelcomeNewcomers();
            }
            _timer -= Time.deltaTime;
            if (_timer > 0f && !_rosterDirty) return;
            _timer = TickSeconds;
            _ticksSinceLoad++;
            // Wait until the area has finished loading, so members aren't missed and extra citizens spawned.
            if (!Founded && _ticksSinceLoad >= 2 && ZNetScene.instance.IsAreaReady(transform.position)) Found();
            else if (Founded) AdoptNearby();   // citizens who lost their colony, or v1 settlers whose hall is gone
            if (Founded) TrackRaid();
            if (Founded) TrackBoss();
            RefreshRoster();
        }

        private void Found()
        {
            string colony = _building.ColonyId;
            if (string.IsNullOrEmpty(colony)) return;
            Vector3 hallPos = transform.position;

            int members = Citizen.All.Count(c => c != null && c.Record.ColonyId == colony);
            // Adopted citizens may be run by another player's computer and join a moment later; count them already.
            int adopted = AdoptNearby();
            members += adopted;
            // Homeless huts join by themselves (HutMembership).

            // Taking over a colony that lost its Town Hall brings no newcomers: its settlers belong to it already.
            int spawn = _building.TookOver ? 0 : ColonyRules.CitizensToSpawn(ColonyConfig.StartingCitizens.Value, members);
            var rng = new System.Random();
            // The first settlers walk in together from the colony's edge (0.16); with no way in, they appear by the hall.
            Vector3? edge = spawn > 0 && ColonyConfig.NewcomersWalkIn.Value ? EdgeSpot(founding: true) : null;
            IReadOnlyList<string> callings = Callings.Founders(rng, spawn, Jobs.JobDefinitions.Ids);
            var names = new List<string>();
            for (int i = 0; i < spawn; i++)
            {
                Vector3 at = edge.HasValue
                    ? Beside(edge.Value, Quaternion.Euler(0f, i * 360f / spawn, 0f) * Vector3.forward * 1.5f)
                    : hallPos + Quaternion.Euler(0f, i * 360f / spawn, 0f) * Vector3.forward * 3f;
                Citizen citizen = CitizenPrefab.Spawn(at + Vector3.up * 0.5f, Quaternion.identity);
                if (citizen == null) break;   // no settler body: players near are told; the colony is founded all the same
                citizen.InitNew(colony, hallPos, rng, callings[i]);
                if (edge.HasValue) citizen.MarkArriving(true);
                names.Add(citizen.Record.Name);
                RecordCame(citizen.Record);
            }
            if (edge.HasValue && spawn > 0) Log.Event($"Colony {colony}: the first settlers walk in from ({edge.Value.x:0}, {edge.Value.z:0})");
            _nview.GetZDO().Set(FoundedKey, true);
            if (!_building.TookOver) Gui.HuginTips.ShowNear(hallPos, Gui.HuginTips.Colony);
            Log.Event(_building.TookOver ? $"Colony {colony} has a Town Hall again: {members} settlers here, adopted {adopted}"
                                        : $"Colony {colony} founded: adopted {adopted}, spawned {names.Count} of {spawn}");
            if (names.Count > 0 && Player.m_localPlayer != null && Vector3.Distance(Player.m_localPlayer.transform.position, hallPos) < 60f)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, edge.HasValue
                    ? string.Format(Localization.instance.Localize("$vc_founders_coming"), names.Count, string.Join(", ", names))
                    : string.Format(Localization.instance.Localize("$vc_citizens_arrived"), names.Count));
        }

        /// <summary>Asks every citizen without a colony inside this Town Hall's area to join. Returns how many were asked.</summary>
        private int AdoptNearby()
        {
            string colony = _building.ColonyId;
            if (string.IsNullOrEmpty(colony)) return 0;
            Vector3 hallPos = transform.position;
            GroundPoint here = Ground(hallPos);
            float radius = HallRules.PerksAt(_building.Level).Radius;
            int asked = 0;
            foreach (Citizen c in Citizen.All.ToList())
            {
                if (c == null || !ColonyRules.IsAdoptable(c.Record.ColonyId, Ground(c.transform.position), here, radius)) continue;
                c.RequestJoinColony(colony, hallPos);
                asked++;
            }
            return asked;
        }

        private void RefreshRoster()
        {
            ZDO zdo = _nview.GetZDO();
            string before = zdo.GetString(RosterKey, "");
            ColonyRoster roster = ColonyRoster.Decode(before);
            string signatureBefore = roster.Signature();
            string colony = _building.ColonyId;
            int today = EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;

            var seen = new HashSet<string>();
            Keepsakes keepsakes = Keepsakes.Decode(zdo.GetString(KeepsakesKey, ""));   // its own copy: it changes it
            bool keptChanged = false;
            foreach (Citizen c in Citizen.All)
            {
                if (c == null) continue;
                CitizenRecord r = c.Record;
                if (string.IsNullOrEmpty(r.Id)) continue;
                if (r.ColonyId == colony)
                {
                    roster.Seen(r.Id, r.Name, r.Job, r.Workplace, r.Status, Citizen.ToFloats(c.transform.position), today, r.Home, r.Mood);
                    seen.Add(r.Id);
                    keptChanged |= keepsakes.Set(r.Id, Keepsake.Of(r));   // changes only with his job, home or skill level
                }
                else
                {
                    roster.Remove(r.Id);   // left this colony
                }
            }
            foreach (RosterEntry entry in roster.Entries.ToList())
            {
                // Died out of this Town Hall's sight (another computer ran him, or it wasn't loaded): off the roster.
                if (ColonyDeaths.IsDead(entry.CitizenId)) roster.Remove(entry.CitizenId);
                else if (!seen.Contains(entry.CitizenId) && AreaLoaded(entry.LastPos)) roster.MarkMissing(entry.CitizenId, today);
            }
            roster.PruneMissing(today, MissingDaysBeforeRemoval);
            keptChanged |= keepsakes.Retain(new HashSet<string>(roster.Entries.Select(e => e.CitizenId)));   // died, left or dropped off
            if (keptChanged) zdo.Set(KeepsakesKey, keepsakes.Encode());

            bool meaningful = roster.Signature() != signatureBefore;
            if (meaningful || Time.time - _lastRosterWrite >= RosterRewriteSeconds)
            {
                string after = roster.Encode();
                if (after != before)
                {
                    zdo.Set(RosterKey, after);
                    _lastRosterWrite = Time.time;
                }
            }
            _rosterDirty = false;
        }

        /// <summary>The settings for newcomers, with the room this Town Hall's level makes (1.4).</summary>
        public NewcomerRules NewcomerRules =>
            new NewcomerRules(ColonyConfig.NewcomersPerDay.Value, ColonyConfig.NewcomerFood.Value, ColonyConfig.MaxSettlers.Value,
                HallRules.PerksAt(_building != null ? _building.Level : 1).Settlers);

        /// <summary>What the colony has for newcomers: its settlers (loaded or on the roster), Longhouse beds, the food in this chest.</summary>
        public ColonyNeeds Needs()
        {
            string colony = _building.ColonyId;
            int loaded = Citizen.All.Count(c => c != null && c.Record.ColonyId == colony);
            int settlers = Mathf.Max(loaded, Roster.Entries.Count(e => !e.IsMissing));   // one not seen where he should be is gone
            int beds = ResidenceModule.All
                .Where(r => r != null && r.Building != null && r.Building.IsValid && r.Building.ColonyId == colony)
                .Sum(r => r.Beds().Count);
            int food = _building.Storage != null ? FoodItems.Count(_building.Storage.Contents()) : 0;
            int last = _nview != null && _nview.IsValid() ? _nview.GetZDO().GetInt(NewcomerDayKey, Newcomers.NeverCame) : Newcomers.NeverCame;
            int today = EnvMan.instance != null && ZNet.instance != null ? EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) : 0;
            // The colony's mood (0.17): its members' average, from the roster so every computer agrees (0.19); content with mood off.
            MoodLevel mood = ColonyConfig.Mood.Value ? MoodLevels.Of(ColonyMoodValue()) : MoodLevel.Content;
            return new ColonyNeeds(settlers, beds, food, last, today, EnvMan.IsNight(), mood);
        }

        /// <summary>The colony's mood: its members' as the roster keeps them (the same on every computer), missing ones left out.</summary>
        public int ColonyMoodValue() => ColonyMood.Of(Roster.Entries.Where(e => !e.IsMissing).Select(e => e.Mood));

        private int _noBodyDay = -1;
        private bool _owned, _raidWas;
        private readonly RaidWatch _raidWatch = new RaidWatch();

        /// <summary>
        /// A raid at the colony (0.17): when one this computer saw begin ends with no settler of the colony dead since, the
        /// Saga says the colony held ("repelled"), which gladdens everyone for two days. One already on when this computer
        /// began watching (a load, a change of owner) writes nothing (<see cref="RaidWatch"/>).
        /// </summary>
        private void TrackRaid()
        {
            bool raid = RaidHere();
            int today = EnvMan.instance != null && ZNet.instance != null ? EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) : 0;
            if (raid != _raidWas) Log.Info($"Colony {_building.ColonyId}: {(raid ? "a raid at the colony" : "the raid is over")} (day {today}).");
            _raidWas = raid;
            if (!_raidWatch.Update(raid, today, Saga)) return;
            // The guards who held it (1.1): named in one word, so the line reads "Ulf, Bjorn held a raid on the colony off".
            string guards = GuardDuty.Names(GuardDuty.Guarding(Roster.Entries));   // the guards on duty (1.2)
            if (guards.Length > 0) Record("repelled", guards);
            else Record("repelled");
        }

        /// <summary>
        /// A boss's fall reaches the colony (1.4.2): the first time this Town Hall's owner sees one further along the way
        /// than the last it heard of, the saga tells it, and settlers talk of it for a few days (the review of polish round
        /// 2: talk read from the world's keys went on forever). A colony founded after a fall hears of it then.
        /// </summary>
        private void TrackBoss()
        {
            if (ZoneSystem.instance == null) return;
            ZDO zdo = _nview.GetZDO();
            string heard = zdo.GetString(SaveKeys.BossHeard, "");
            string latest = TownHallLevels.LatestBeaten(key => ZoneSystem.instance.GetGlobalKey(key));
            if (!TownHallLevels.BossNews(heard, latest)) return;
            zdo.Set(SaveKeys.BossHeard, latest);
            Record("bossfell", latest);
            Log.Event($"Colony {_building.ColonyId}: word came that {latest} has fallen");
        }

        /// <summary>A Valheim raid is running and its area holds this Town Hall.</summary>
        private bool RaidHere()
        {
            if (AI.CitizenAI.RaidForTest) return true;
            RandomEvent raid = RandEventSystem.instance != null ? RandEventSystem.instance.GetCurrentRandomEvent() : null;
            Vector3 hall = transform.position;
            return raid != null && IndoorRules.RaidCovers(new GroundPoint(raid.m_pos.x, raid.m_pos.z), raid.m_eventRange, new GroundPoint(hall.x, hall.z), hall.y);
        }

        /// <summary>Once a day, in daylight: newcomers for the colony's free beds, each fed from this chest (<see cref="Newcomers"/>).</summary>
        private void WelcomeNewcomers()
        {
            if (EnvMan.instance == null || ZNet.instance == null || string.IsNullOrEmpty(_building.ColonyId) || _building.Storage == null) return;
            if (Newcomers.HowMany(Needs(), NewcomerRules) == 0) return;
            if (!CitizenPrefab.Available)
            {
                // Told once a day here, no food eaten (review of 0.19: every minute all day).
                int day = EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds());
                if (_noBodyDay != day) CitizenPrefab.TellMissing(transform.position);
                _noBodyDay = day;
                return;
            }
            if (_building.Storage.Claim() != StorageModule.Access.Ready) return;   // a player has the chest open: next time
            ColonyNeeds needs = Needs();   // the chest as it is now
            int coming = Newcomers.HowMany(needs, NewcomerRules);
            var rng = new System.Random();
            Vector3 hallPos = transform.position;
            int came = 0;
            Vector3? edge = ColonyConfig.NewcomersWalkIn.Value ? EdgeSpot(founding: false) : null;
            for (int i = 0; i < coming && FeedNewcomer(NewcomerRules.FoodEach); i++)
            {
                Citizen citizen = SpawnNewcomer(rng, edge, i);
                if (citizen == null) break;
                Log.Event($"Colony {_building.ColonyId}: {citizen.Record.Name} came to live here (day {needs.Today}; {needs.Settlers + i + 1} settlers, {needs.Beds} beds)");
                came++;
            }
            if (came > 0) _nview.GetZDO().Set(NewcomerDayKey, needs.Today);
        }

        /// <summary>
        /// A newcomer (0.16): at the colony's edge to walk in (or by the Town Hall when there's no way in), often with the
        /// trade an empty hut needs; players near are told, and the Saga says where he came from.
        /// </summary>
        private Citizen SpawnNewcomer(System.Random rng, Vector3? edge, int index)
        {
            Vector3 hallPos = transform.position;
            Vector3 at = edge.HasValue ? Beside(edge.Value, Vector3.right * (index * 1.5f)) : hallPos + Quaternion.Euler(0f, rng.Next(360), 0f) * Vector3.forward * 3f;
            Citizen citizen = CitizenPrefab.Spawn(at + Vector3.up * 0.5f, Quaternion.identity);
            if (citizen == null) return null;   // no settler body: players near are told
            citizen.InitNew(_building.ColonyId, hallPos, rng, Callings.Pick(rng, Jobs.JobDefinitions.Ids, OpenHutJobs()));
            if (edge.HasValue) citizen.MarkArriving(true);
            CitizenRecord nr = citizen.Record;
            NewcomerMessages.Send(nr.Name + "|" + CallingWord(nr.Calling) + "|" + nr.Origin, hallPos);   // name, "a forager", "from the coast"
            RecordCame(nr);
            return citizen;
        }

        /// <summary>Self-test: a newcomer now, whatever the beds and food (walking in as the setting says).</summary>
        internal Citizen NewcomerForTest() => SpawnNewcomer(new System.Random(), ColonyConfig.NewcomersWalkIn.Value ? EdgeSpot(founding: false) : null, 0);

        /// <summary>The Saga's line for a settler who came (0.16): name, where from, his trade, his trait.</summary>
        private void RecordCame(CitizenRecord r) => Record("came", r.Name, r.Origin, CallingWord(r.Calling), Traits.Token(Traits.Parse(r.Trait, Trait.Cheerful)));

        /// <summary>"a forager" / "a jack of all trades": a translation token.</summary>
        public static string CallingWord(string calling) => "$vc_calling_" + (string.IsNullOrEmpty(calling) ? Callings.None : calling);

        /// <summary>The jobs of this colony's huts that have no worker (a newcomer often brings one of these trades).</summary>
        private List<string> OpenHutJobs()
        {
            var jobs = new List<string>();
            foreach (ColonyBuilding b in ColonyBuilding.All)
            {
                if (b == null || !b.IsValid || b.ColonyId != _building.ColonyId) continue;
                WorkerModule work = b.GetComponent<WorkerModule>();
                if (work == null || string.IsNullOrEmpty(work.JobId) || !work.MayHire) continue;
                if (work.HasRoom) jobs.Add(work.JobId);   // a Watchtower with a free place too (1.2)
            }
            return jobs;
        }

        /// <summary>
        /// Where a newcomer appears to walk in from (0.16): of 16 spots 35 m out (then 25 m), the one farthest from every
        /// player that is on dry ground, in a loaded area, with the game's paths to the Town Hall. For a new Town Hall
        /// (<paramref name="founding"/>) dry ground will do when neither ring has a way in yet (ArrivalSpot.Choose). Null when
        /// none fits: he appears by the Town Hall.
        /// </summary>
        private Vector3? EdgeSpot(bool founding)
        {
            if (ZoneSystem.instance == null || Pathfinding.instance == null || ZNetScene.instance == null) return null;
            Vector3 hall = transform.position;
            int unloaded = 0, wet = 0, noWay = 0;
            var rings = new List<IReadOnlyList<ArrivalCandidate>>();
            var spots = new List<Vector3>();
            foreach (float radius in new[] { ArrivalSpot.Far, ArrivalSpot.Near })
            {
                IReadOnlyList<GroundPoint> ring = ArrivalSpot.Directions(new GroundPoint(hall.x, hall.z), radius);
                var candidates = new List<ArrivalCandidate>();
                foreach (GroundPoint p in ring)
                {
                    var at = new Vector3(p.X, hall.y, p.Z);
                    bool loaded = ZNetScene.instance.IsAreaReady(at);
                    float ground = loaded ? ZoneSystem.instance.GetGroundHeight(at) : 0f;
                    at.y = ground;
                    bool dry = loaded && ground > ZoneSystem.instance.m_waterLevel + 0.3f;
                    // The way to just outside the hall on his side: a Town Hall placed a moment ago stands in the game's
                    // paths at its own spot (vc_lifetest: the first settlers appeared by the hall).
                    Vector3 outside = hall + (at - hall).normalized * 4f;
                    outside.y = ZoneSystem.instance.GetGroundHeight(outside);
                    bool reachable = dry && Pathfinding.instance.HavePath(at, outside, Pathfinding.AgentType.Humanoid);
                    if (!loaded) unloaded++;
                    else if (!dry) wet++;
                    else if (!reachable) noWay++;
                    float nearest = float.MaxValue;
                    foreach (Player player in Player.GetAllPlayers())
                        if (player != null) nearest = Mathf.Min(nearest, Vector3.Distance(player.transform.position, at));
                    // Seen or not where he will stand: the walkable point near the spot (0.24: the spot was behind a tree, the
                    // point 2 m on wasn't, and he appeared in view).
                    Vector3 stand = dry ? OnTheWalkableGround(at) : at;
                    candidates.Add(new ArrivalCandidate(p, dry, reachable, nearest, dry && SeenByAPlayer(stand)));
                    spots.Add(stand);
                }
                int found = ArrivalSpot.Choose(candidates);
                // The far ring will do when it has a spot out of view: no need to try the near one.
                if (found >= 0 && !candidates[found].Seen)
                {
                    LastEdgeSeen = false;
                    LastUnseenUsable = candidates.Count(c => c.Dry && c.Reachable && !c.Seen);
                    return spots[spots.Count - candidates.Count + found];
                }
                rings.Add(candidates);
            }
            int unseenUsable = rings.Sum(r => r.Count(c => c.Dry && c.Reachable && !c.Seen));
            // Right after a Town Hall goes up the game may not have worked out its paths out there yet (life run new3: none of 32
            // had "a way in" at founding): then dry ground will do, and one who can't get in is placed by the hall in time. Later,
            // "no way in" is real (an island, a closed palisade: review I2), and he appears by the Town Hall.
            int pick = ArrivalSpot.Choose(rings, founding);
            if (pick >= 0)
            {
                IReadOnlyList<ArrivalCandidate> all = rings.SelectMany(r => r).ToList();
                LastEdgeSeen = all[pick].Seen;
                LastUnseenUsable = unseenUsable;
                if (LastEdgeSeen) Log.Info($"A newcomer comes from where a player can see them: no spot out of view has a way in ({unseenUsable} usable out of view).");
                return spots[pick];
            }
            Log.Info($"No spot at the colony's edge to walk in from ({unloaded} not loaded, {wet} in water, {noWay} with no way in): by the Town Hall.");
            return null;
        }

        /// <summary>Self-test: whether a player sees this spot, by the rule newcomers' arrival spots are chosen by.</summary>
        internal static bool SeenForTest(Vector3 spot) => SeenByAPlayer(spot);

        /// <summary>Self-test: the last edge spot was in a player's view, and how many out of view had a way in then.</summary>
        internal static bool LastEdgeSeen;
        internal static int LastUnseenUsable;

        private static int s_viewMask;

        /// <summary>
        /// A player is looking that way (0.19, from the 0.16 review): within 60 m, less than 70° off where he looks, nothing
        /// in between. A spot nobody sees is where a newcomer appears.
        /// </summary>
        private static bool SeenByAPlayer(Vector3 spot)
        {
            if (s_viewMask == 0) s_viewMask = LayerMask.GetMask("piece", "static_solid", "terrain", "Default");
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player == null) continue;
                Vector3 eye = player.GetEyePoint();
                // Any of him: his knees, his chest or the top of his head as he appears, half a metre up (1.4.1: a spot
                // behind a rise passed at 1.6 m, and his head showed over it 34 degrees off the player's view).
                foreach (float up in s_seenHeights)
                {
                    Vector3 part = spot + Vector3.up * up, toPart = part - eye;
                    if (toPart.magnitude > 60f || Vector3.Angle(player.GetLookDir(), toPart) > 70f) continue;
                    if (Physics.Linecast(eye, part, s_viewMask, QueryTriggerInteraction.Ignore)) continue;   // a wall, a hill or a tree between
                    return true;
                }
            }
            return false;
        }

        private static readonly float[] s_seenHeights = { 0.8f, 1.6f, 2.4f };

        /// <summary>The game's walkable point for a spot, so a newcomer never stands in a boulder or under a floor (0.16 review M3).</summary>
        private static Vector3 OnTheWalkableGround(Vector3 spot) =>
            Pathfinding.instance != null && Pathfinding.instance.FindValidPoint(out Vector3 walkable, spot, 2f, Pathfinding.AgentType.Humanoid) ? walkable : spot;

        /// <summary>
        /// A spot a little off the edge spot for the next newcomer, on the walkable ground there (review of 0.19: set to the
        /// terrain's height, it put him back inside a rock); the edge spot itself when there is no offset or no such ground.
        /// </summary>
        private static Vector3 Beside(Vector3 edge, Vector3 offset)
        {
            if (offset.sqrMagnitude < 0.01f || ZoneSystem.instance == null) return edge;
            Vector3 at = edge + offset;
            at.y = ZoneSystem.instance.GetGroundHeight(at);
            return Pathfinding.instance != null && Pathfinding.instance.FindValidPoint(out Vector3 walkable, at, 1.5f, Pathfinding.AgentType.Humanoid)
                ? walkable : edge;
        }

        /// <summary>Takes a newcomer's meal out of this chest, the plainest food first.</summary>
        private bool FeedNewcomer(int items)
        {
            var plate = new CarryLoad();
            for (int i = 0; i < items; i++)
            {
                string food = Meals.Choose(FoodItems.In(_building.Storage.Contents()));
                if (food == null || !_building.Storage.TryTakeItem(food, plate, ownUse: true)) return false;   // its kept food is for them
            }
            return true;
        }

        private void MarkDirty(Citizen citizen) => _rosterDirty = true;

        /// <summary>
        /// On the Town Hall's owner: brings a member marked missing back as himself (his keepsake: name, looks, job, home,
        /// skills), at the Town Hall, empty-handed. Only when no body of his is known anywhere in the world, so there are
        /// never two of him. The asker is told how it went.
        /// </summary>
        private void RPC_BringBack(long sender, string citizenId)
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
            string name = Roster.Get(citizenId)?.Name ?? "?";
            string refused = MayBringBack(sender, citizenId);
            if (refused != null)
            {
                Answer(sender, citizenId, name, refused);
                return;
            }
            // The world's host knows every body; this computer only those near its player (review of 0.10).
            BodyCheck.Ask(citizenId, answer =>
            {
                if (this == null || _nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
                string again = MayBringBack(sender, citizenId);   // a moment has passed: still so?
                string result = again ?? (answer == BodyCheck.Answer.Found ? "$vc_bring_back_found"
                    : answer == BodyCheck.Answer.NoAnswer ? "$vc_bring_back_try_again" : BringBack(citizenId));
                Answer(sender, citizenId, name, result);
            });
        }

        private void Answer(long sender, string citizenId, string name, string result)
        {
            if (result != "$vc_bring_back_done") Log.Event($"Colony {_building.ColonyId}: {name} wasn't brought back ({result})");
            _nview.InvokeRPC(sender, RpcBringBackResult, result, name);
        }

        /// <summary>Why he can't be brought back now; null when he may (the host is asked about his body after).</summary>
        private string MayBringBack(long sender, string citizenId)
        {
            if (!ColonyPermissions.SenderMayManage(_building, sender)) return "$vc_not_your_colony";
            if (ColonyDeaths.IsDead(citizenId)) return "$vc_bring_back_dead";
            RosterEntry entry = Roster.Get(citizenId);
            CitizenRecord kept = CitizenRecord.Decode(Keepsakes.Get(citizenId));
            if (entry == null || !entry.IsMissing || kept.Id != citizenId) return "$vc_bring_back_none";
            if (!ColonyRoster.MayBringBack(entry, Today())) return "$vc_bring_back_wait";
            if (Citizen.All.Any(c => c != null && c.Id == citizenId) || Citizen.BodyKnown(citizenId)) return "$vc_bring_back_found";
            return null;
        }

        private static int Today() => EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;

        private string BringBack(string citizenId)
        {
            CitizenRecord kept = CitizenRecord.Decode(Keepsakes.Get(citizenId));
            if (kept.Id != citizenId) return "$vc_bring_back_none";
            kept.Life++;   // a body of his that turns up later is an old copy and goes (Keepsake.Outdated)
            Vector3 hallPos = transform.position;
            Vector3 offset = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * 3f;
            Citizen citizen = CitizenPrefab.Spawn(hallPos + offset + Vector3.up * 0.5f, Quaternion.identity);
            if (citizen == null) return "$vc_no_settler_body";   // his keepsake stays as it was
            citizen.InitRestored(kept, _building.ColonyId, hallPos);
            Record("returned", kept.Name);
            Log.Event($"Colony {_building.ColonyId}: {kept.Name} was brought back from the Town Hall's keepsake");
            _rosterDirty = true;
            return "$vc_bring_back_done";
        }

        /// <summary>On the asker's computer: how it went, in the open window (or the game's message corner).</summary>
        private void RPC_BringBackResult(long sender, string token, string name) =>
            Gui.Windows.Reply(SafeText.Format(Localization.instance.Localize(token), name));   // the token is the sender's

        /// <summary>
        /// Test command: takes a settler off the roster (a load test's settler, removed again). With no id, every one
        /// marked missing goes. Seen: settlers a test removed showed as "missing" in the Town Hall window for days.
        /// </summary>
        /// <summary>Self-test: his going missing moved back <paramref name="days"/> days (Bring back waits a day).</summary>
        internal void BackdateMissingForTest(string citizenId, int days)
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
            ColonyRoster roster = Roster;
            RosterEntry entry = roster.Get(citizenId);
            if (entry == null || !entry.IsMissing) return;
            entry.MissingSinceDay -= days;
            _nview.GetZDO().Set(RosterKey, roster.Encode());
        }

        internal int ForgetForTest(string citizenId = null)
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner()) return 0;
            ColonyRoster roster = Roster;
            int forgotten = citizenId != null ? (roster.Remove(citizenId) ? 1 : 0) : roster.PruneMissing(int.MaxValue, 0);
            if (forgotten > 0) _nview.GetZDO().Set(RosterKey, roster.Encode());
            return forgotten;
        }

        private void OnCitizenDied(Citizen citizen)
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner() || citizen.Record.ColonyId != _building.ColonyId) return;
            ColonyRoster roster = Roster;
            if (roster.Remove(citizen.Record.Id)) _nview.GetZDO().Set(RosterKey, roster.Encode());
        }

        private static GroundPoint Ground(Vector3 v) => new GroundPoint(v.x, v.z);

        private static bool AreaLoaded(float[] pos) =>
            pos != null && pos.Length == 3 && ZNetScene.instance != null && ZNetScene.instance.IsAreaReady(Citizen.ToVector3(pos));
    }
}
