using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.Jobs
{
    /// <summary>
    /// The guard (1.1): keeps watch by his Watchtower with the best weapon (and a shield) its chest holds — a copy, the
    /// weapon stays in the chest — fights the monsters that come near the colony, walks a round now and then, and goes back
    /// to his tower to heal when hurt. 1.2: an archer shoots with the best bow and its ammunition; armour from the chests is
    /// worn; a shield is raised between blows; Fighting makes him tougher. Every tick: his gear (every few seconds), a look
    /// round (every second), then <see cref="GuardBrain"/> decides. With <c>Guards.Enabled</c> off he only stands by his
    /// tower, and shelters as anyone.
    /// </summary>
    public class GuardJob : Job
    {
        private const float GearCheckSeconds = 5f;
        private const float LookSeconds = 1f;
        private const float PlaceSeconds = 1.5f;   // how often he counts his place among the guards (1.4's review: every step)
        /// <summary>He stands this near his place (1.2: two guards 3 m apart must not stand together).</summary>
        private const float PostReach = 1f;
        private const float PatrolReach = 4f;
        private const float MinReach = 1.5f;
        private const float HealEverySeconds = 1f;
        /// <summary>An archer shoots at a target this near that he can see; nearer than <see cref="TooClose"/> he steps back.</summary>
        private const float ShootRange = 25f, TooClose = 5f;
        /// <summary>Copies of ammunition kept in hand (topped up; the chest's aren't used up).</summary>
        private const int AmmoInHand = 20;
        /// <summary>A warrior with a shield holds it up while a monster is this near (between blows: ShieldGuard.Guarding).</summary>
        private const float ShieldRange = 5f;
        /// <summary>When the monster starts a blow he holds his shield up this long rather than swing into it.</summary>
        private const float StrikeGuard = 0.6f;   // the fifth 1.2 run: a second each blow made a 45 s fight of a Greyling

        private static readonly List<Character> s_nearby = new List<Character>();
        private static readonly HashSet<Character> s_seen = new HashSet<Character>();
        private static readonly AccessTools.FieldRef<Character, bool> s_blocking = AccessTools.FieldRefAccess<Character, bool>("m_blocking");
        private static readonly AccessTools.FieldRef<Humanoid, float> s_drawTime = AccessTools.FieldRefAccess<Humanoid, float>("m_attackDrawTime");
        private static int s_sightMask;

        /// <summary>Blocks guards made with a shield (vc_guardtest).</summary>
        internal static int BlocksForTest;

        private readonly ColonyBuilding _tower;
        private readonly WatchtowerModule _module;
        private readonly StuckDetector _patrolStuck = new StuckDetector();
        private readonly StuckDetector _backStuck = new StuckDetector(0.5f, 3);
        /// <summary>Monsters he gave up on (he couldn't land a blow), left alone a while (review of 1.1).</summary>
        private readonly TargetBlacklist _givenUp = new TargetBlacklist(GuardRules.IgnoreSeconds);
        private readonly ShieldGuard _shieldGuard = new ShieldGuard();
        private ItemDrop.ItemData _weapon, _shield, _ammo;
        private string _weaponPrefab = "", _shieldPrefab = "", _ammoPrefab = "";
        private bool _ranged;
        private Dictionary<string, string> _armour = new Dictionary<string, string>();
        private float _gearTimer, _lookTimer, _healTimer;
        private float _placeAt = float.NegativeInfinity;
        private int _towerPlace, _alarmPlace;
        private Character _target;
        private float _targetHealth, _harmedAt, _giveUpAfter = GuardRules.GiveUpSeconds;
        /// <summary>When an archer's way to the target ended short of bow range (-infinity while it goes on).</summary>
        private float _wayEndedAt = float.NegativeInfinity;
        /// <summary>The post his walk last stopped short of (the way ends there), while he stays by it.</summary>
        private Vector3? _stoppedShortOf;
        /// <summary>The middle and radius of the watch he took it from (his tower's, a call's, the alarm's), for the leash.</summary>
        private Vector3 _leashCentre;
        private float _leashRadius;
        /// <summary>Raiders called out, per colony, on this computer (once a raid, 1.3).</summary>
        private static readonly Dictionary<string, float> s_raidCalled = new Dictionary<string, float>();
        private bool _retreating, _wasInAttack, _cornered, _targetWasStriking;
        private float _guardUntil = float.NegativeInfinity;
        private float _swungAt = float.NegativeInfinity, _shotAt = float.NegativeInfinity;
        /// <summary>When he last came within reach of his target (-1 while out of it): the overdue blow counts from there too.</summary>
        private float _inReachAt = -1f;
        private float _approachLogAt;
        private Vector3? _patrolTo;
        // A ballista he keeps loaded (1.3): which, with what, from which chest, how many, whether he has them in hand.
        private const float BallistaCheckSeconds = 30f, ChestReach = 2.5f, BallistaReach = 2.5f;
        private float _ballistaTimer = 5f;
        private Turret _ballista;
        private string _ballistaAmmo = "", _ballistaChest = "";
        private float _loadBackFailedAt = -1000f;
        private int _ballistaWant;
        private bool _ammoTaken;
        /// <summary>Ammunition he put in ballistas (vc_guardtest).</summary>
        internal static int LoadedForTest;
        /// <summary>Rounds begun (vc_guardtest: a status read twice a second missed a short one).</summary>
        internal static int RoundsForTest;
        private float _patrolAt;
        private GuardAction? _lastAction;

        public GuardJob(Citizen citizen, CitizenAI ai, ColonyBuilding tower) : base(citizen, ai)
        {
            _tower = tower;
            _module = tower != null ? tower.GetComponent<WatchtowerModule>() : null;
            _patrolAt = Time.time + GuardDuty.PatrolDelay(Random.value);
        }

        /// <summary>He holds a weapon: with <c>Guards.Enabled</c> he stays out on duty (CitizenAI asks).</summary>
        public bool Armed => _weapon != null;

        /// <summary>Hurt, he is on his way to heal at his tower: off duty, he shelters or runs from a monster as anyone (CitizenAI asks).</summary>
        public bool Retreating => _retreating;

        /// <summary>The monster he is after; null when none (vc_guardtest).</summary>
        public Character Target => _target;

        public string WeaponPrefab => _weaponPrefab;
        public string AmmoPrefab => _ammoPrefab;
        public bool Ranged => _ranged;
        public IReadOnlyDictionary<string, string> Armour => _armour;

        private GuardRole Role => _module != null ? _module.Role : GuardRole.Warrior;

        private GuardOrders Orders => _module != null ? _module.Orders : GuardOrders.Patrol;

        /// <summary>His colony's alarm sounds, and where (1.3).</summary>
        private bool Alarm(out Vector3 at) => TownHallModule.ColonyAlarmFor(Citizen.ColonyId, out at);

        private static bool Enabled => ColonyConfig.GuardsEnabled.Value;

        public override void Tick(float dt)
        {
            if (_tower == null)
            {
                Citizen.Status = "$vc_status_unemployed";
                Ai.Idle();
                return;
            }
            _gearTimer -= dt;
            if (_gearTimer <= 0f)
            {
                _gearTimer = GearCheckSeconds;
                ChooseGear();
            }
            HoldGear();

            Humanoid body = Citizen.Body;
            _retreating = Armed && Enabled && GuardRules.Retreating(_retreating, body.GetHealthPercentage(), ColonyConfig.RetreatHealth.Value);
            if (!ReferenceEquals(_target, null) && !StillAfter(_target)) ForgetTarget();
            if (_retreating || !Armed || !Enabled)
            {
                ForgetTarget();
            }
            else
            {
                _lookTimer -= dt;
                if (_lookTimer <= 0f)
                {
                    _lookTimer = LookSeconds;
                    LookRound();
                }
            }

            // A ballista errand (1.3) runs to its end, wherever it takes him; holding a post or at the alarm he leaves none
            // (review of 1.3): what he holds goes with his load.
            bool loadingAllowed = LoadingAllowed;
            // On his rounds with the ballistas' setting off he still takes what he holds back to a chest (KeepBallistaLoaded).
            if (!loadingAllowed && (_ballista != null || Citizen.InHand.Total > 0 && !OnRounds)) DropErrand();
            Vector3 post = Post();
            float toPost = Utils.DistanceXZ(Citizen.transform.position, post);
            // His walk to this very spot stopped short (the way ends there: 2.8 m off in the second 1.3 run): there counts as at
            // his post, or he would stand there for good, never on watch. A post that moved (a new guard's place) he walks to.
            bool stoppedShort = _stoppedShortOf.HasValue && Utils.DistanceXZ(_stoppedShortOf.Value, post) < 0.5f;
            GuardAction action = GuardBrain.Decide(new GuardSituation
            {
                HasWeapon = Armed,
                Retreating = _retreating,
                HasTarget = _target != null,
                AtPost = toPost <= PostReach + 1f || (stoppedShort && toPost <= PostReach + MoveRules.PathEndSlack),   // a margin past where his walk stops (no flapping)
                PatrolDue = Enabled && Time.time >= _patrolAt && Orders == GuardOrders.Patrol && !Alarm(out _),   // holding his post, or the alarm: no rounds (1.3)
                Patrolling = _patrolTo != null,
                Loading = loadingAllowed && (_ballista != null || Citizen.InHand.Total > 0),
            });
            if (action != _lastAction)   // one log line per step change
            {
                Log.Info($"{Citizen.CitizenName}: {_lastAction?.ToString() ?? "start"} -> {action}" +
                         (action == GuardAction.Fight ? $" ({_target.m_name} {Utils.DistanceXZ(_target.transform.position, _tower.transform.position):0.0} m from the tower)" : ""));
                _lastAction = action;
            }
            if (action != GuardAction.Fight) Block(false);
            if (action == GuardAction.Fight || action == GuardAction.Patrol || action == GuardAction.Loading) _stoppedShortOf = null;   // away: walk all the way back
            if (action != GuardAction.OnWatch && action != GuardAction.Loading && _ballista != null && !_ammoTaken) _ballista = null;   // a monster first; nothing in hand yet

            switch (action)
            {
                case GuardAction.NeedsWeapon:
                    Citizen.Status = Role == GuardRole.Archer ? "$vc_status_needs $vc_a_bow" : "$vc_status_needs $vc_a_weapon";
                    if (Ai.GoTo(post, PostReach)) Ai.Idle();
                    break;
                case GuardAction.Retreat:
                    Citizen.Status = "$vc_status_resting_hurt";
                    if (Ai.GoTo(post, PostReach, hurry: true))
                    {
                        Ai.Idle();
                        Heal(dt);
                    }
                    break;
                case GuardAction.Fight:
                    Citizen.Status = "$vc_status_fighting " + _target.m_name;
                    if (_ranged) Shoot();
                    else Fight();
                    break;
                case GuardAction.Patrol:
                    Citizen.Status = "$vc_status_patrolling";
                    Patrol();
                    break;
                case GuardAction.ReturnToPost:
                    Citizen.Status = "$vc_status_on_watch";
                    if (Ai.GoTo(post, PostReach))
                    {
                        Ai.Idle();
                        if (toPost > PostReach + 1f) _stoppedShortOf = post;
                    }
                    break;
                case GuardAction.Loading:
                    if (!KeepBallistaLoaded(dt)) Ai.Idle();
                    break;
                case GuardAction.OnWatch:
                    if (KeepBallistaLoaded(dt)) break;   // 1.3
                    Citizen.Status = "$vc_status_on_watch";
                    Ai.Idle();
                    Vector3 outward = post - _tower.transform.position;
                    outward.y = 0f;
                    Ai.Face(post + (outward.sqrMagnitude > 0.25f ? outward : _tower.transform.forward));   // he looks out, away from his tower
                    break;
            }
        }

        public override void Resume()
        {
            _patrolStuck.Reset();
            _lookTimer = 0f;
        }

        public override void Stop()
        {
            ForgetTarget();
            SwingLog.EndSwing(Citizen);   // an open swing mustn't keep his aim once the job is gone
            Block(false);
            Borrow(ref _weapon, ref _weaponPrefab, "");   // the copies go back; the weapon and shield never left the chest
            Borrow(ref _shield, ref _shieldPrefab, "");
            Borrow(ref _ammo, ref _ammoPrefab, "");
            _armour = new Dictionary<string, string>();
            _ballista = null;
            if (Citizen != null && Citizen.Body != null && Citizen.IsOwner)   // not on a computer that just lost him (review of 1.2)
            {
                HandsToLoad();
                Citizen.SetArmour(_armour);   // his trade's clothes again
                SetMaxHealth(Citizen.Body, Citizen.Body.GetMaxHealthBase());
            }
        }

        /// <summary>The armour he wears (copies from the chests, and his trade's pieces still on), as a player's counts (CitizenPatches).</summary>
        internal static float ArmourOf(Humanoid body)
        {
            float armour = 0f;
            foreach (ItemDrop.ItemData item in body.GetInventory().GetAllItems())
            {
                string slot = item.m_shared.m_itemType.ToString();
                if ((slot == "Helmet" || slot == "Chest" || slot == "Legs" || slot == "Shoulder") && body.IsItemEquiped(item)) armour += item.GetArmor();
            }
            return armour;
        }

        /// <summary>His next round is due now (vc_guardtest: holding his post he walks none).</summary>
        internal void PatrolNowForTest() => _patrolAt = Time.time;

        /// <summary>A block with his shield (CitizenPatches): three quick ones lower it a moment.</summary>
        internal void NoteBlock()
        {
            BlocksForTest++;
            _shieldGuard.Blocked(Time.time);
        }

        /// <summary>Where he stands on watch (vc_guardtest).</summary>
        public Vector3 PostSpot => _tower != null ? Post() : Vector3.zero;

        /// <summary>
        /// His post: in front of his tower, outside its front wall; a tower's second and third guards stand beside it (1.2), by
        /// their place among its guards here (by id, the same on every computer).
        /// </summary>
        private Vector3 Post()
        {
            CountPlaces();
            if (Alarm(out Vector3 alarmAt))
            {
                // The alarm (1.3): they gather where it was sounded, each guard of the colony at his own spot round it (review of
                // 1.3: the first of every tower stood on the same one).
                GuardPosts.AlarmSpot(_alarmPlace, out float dx, out float dz);
                return alarmAt + new Vector3(dx, 0f, dz);
            }
            Vector3? chosen = _module != null ? _module.PostAt : null;   // where a manager put them (1.3)
            Vector3 post = chosen ?? DefaultPost();
            return post + _tower.transform.right * GuardPosts.Offset(_towerPlace);
        }

        /// <summary>His place among his tower's guards and among the colony's (for the alarm), by id: counted now and then.</summary>
        private void CountPlaces()
        {
            float now = Time.time;
            if (now - _placeAt < PlaceSeconds && now >= _placeAt) return;
            _placeAt = now;
            string tower = _tower.Id, me = Citizen.Id, colony = Citizen.ColonyId;
            _towerPlace = _alarmPlace = 0;
            foreach (Citizen c in Citizen.All)
            {
                if (c == null) continue;
                CitizenRecord r = c.Record;
                if (string.CompareOrdinal(r.Id, me) >= 0) continue;
                if (r.Workplace == tower) _towerPlace++;
                if (r.ColonyId == colony && r.Job == SaveKeys.GuardJob) _alarmPlace++;
            }
        }

        /// <summary>
        /// The post no manager chose: in front of the tower's front door (GuardPosts.PostSpot). Worked out from the tower's
        /// plan once, and again only when its plan changes (an upgrade): Post() runs every tick (the review of 2026-10-19).
        /// </summary>
        private Vector3 DefaultPost()
        {
            Transform t = _tower.transform;
            Construction.ConstructionModule site = _tower.GetComponent<Construction.ConstructionModule>();
            IReadOnlyList<PlannedPiece> plan = site != null ? site.Plan : null;
            if (_postPlan != null && ReferenceEquals(plan, _postPlan) && t.position == _postFrom) return _postAt;
            bool hasDoor = false;
            Vector3 door = Vector3.zero;
            if (plan != null)
                foreach (PlannedPiece piece in plan)
                {
                    GameObject prefab = piece.IsValid ? Construction.PiecePlacer.Prefab(piece.Prefab) : null;
                    if (prefab == null || prefab.GetComponent<Door>() == null) continue;
                    Vector3 local = t.InverseTransformPoint(Construction.CoreVectors.U(piece.Position));
                    if (!hasDoor || local.z > door.z) door = local;
                    hasDoor = true;
                }
            GuardPosts.PostSpot(hasDoor, door.x, door.z, out float x, out float z);
            _postPlan = plan;
            _postFrom = t.position;
            _postAt = t.position + t.right * x + t.forward * z;
            return _postAt;
        }

        private IReadOnlyList<PlannedPiece> _postPlan;
        private Vector3 _postFrom;
        private Vector3 _postAt;

        // ---- Gear ----

        /// <summary>
        /// The best gear for his role in his tower's chest, else in the colony's chests (an archer who finds no bow and
        /// ammunition in his tower's looks there too); the best armour likewise; his health by his Fighting.
        /// </summary>
        private void ChooseGear()
        {
            GuardRole role = Role;
            StorageModule[] own = { _tower.Storage };
            List<StorageModule> all = BuilderTools.ColonyStores(Citizen.ColonyId, _tower, _tower.transform.position);
            GuardGear gear = WeaponChoice.Best(OptionsIn(own), role);
            if (!gear.Armed || (role == GuardRole.Archer && !gear.Ranged))
            {
                GuardGear wide = WeaponChoice.Best(OptionsIn(all), role);
                if (wide.Ranged || !gear.Armed) gear = wide;
            }
            if (gear.Weapon != _weaponPrefab) Borrow(ref _weapon, ref _weaponPrefab, gear.Weapon);
            if (gear.Shield != _shieldPrefab) Borrow(ref _shield, ref _shieldPrefab, gear.Shield);
            if (gear.Ammo != _ammoPrefab || (_ammo != null && _ammo.m_stack < AmmoInHand / 2)) Borrow(ref _ammo, ref _ammoPrefab, gear.Ammo, AmmoInHand);
            _ranged = gear.Ranged && _ammo != null;

            List<ArmourOption> armour = ArmourIn(own);
            Dictionary<string, string> best = ArmourChoice.Best(armour.Count > 0 ? armour : ArmourIn(all));
            if (!SameArmour(best, _armour))
            {
                _armour = best;
                Citizen.SetArmour(best);
                if (best.Count > 0) Log.Info($"{Citizen.CitizenName} wears copies of the colony's {string.Join(", ", best.Values.ToArray())}");
            }

            Humanoid body = Citizen.Body;
            float health = GuardDuty.MaxHealth(body.GetMaxHealthBase(), Citizen.Record.Fighting);
            if (Mathf.Abs(body.GetMaxHealth() - health) > 0.5f) SetMaxHealth(body, health);
        }

        /// <summary>A new maximum, keeping his share of health (review of 1.2: raised, it started empty; a reload put it back).</summary>
        internal static void SetMaxHealth(Character body, float max)
        {
            float health = GuardDuty.HealthAfter(body.GetHealth(), body.GetMaxHealth(), max);
            body.SetMaxHealth(max);
            body.SetHealth(health);
        }

        private static bool SameArmour(Dictionary<string, string> a, Dictionary<string, string> b) =>
            a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out string v) && v == kv.Value);

        private static List<WeaponOption> OptionsIn(IEnumerable<StorageModule> stores)
        {
            var options = new List<WeaponOption>();
            foreach (KeyValuePair<string, ItemDrop.ItemData> found in ItemsIn(stores))
            {
                ItemDrop.ItemData item = found.Value;
                var s = item.m_shared;
                WeaponKind kind = WeaponKinds.Of(s.m_itemType.ToString(), s.m_skillType.ToString(), s.m_attack != null ? s.m_attack.m_attackType.ToString() : "");
                if (kind == WeaponKind.Other || kind == WeaponKind.Tool) continue;
                string prefab = found.Key;
                bool shield = kind == WeaponKind.Shield;
                options.Add(new WeaponOption(prefab, kind, shield ? 0f : Damage(item), shield ? item.GetBaseBlockPower() : 0f,
                    kind == WeaponKind.Bow || kind == WeaponKind.Ammo ? s.m_ammoType ?? "" : ""));
            }
            return options;
        }

        private static List<ArmourOption> ArmourIn(IEnumerable<StorageModule> stores)
        {
            var options = new List<ArmourOption>();
            foreach (KeyValuePair<string, ItemDrop.ItemData> found in ItemsIn(stores))
            {
                string slot = found.Value.m_shared.m_itemType.ToString();
                if (slot != "Helmet" && slot != "Chest" && slot != "Legs" && slot != "Shoulder") continue;
                options.Add(new ArmourOption(found.Key, slot, found.Value.m_shared.m_armor));
            }
            return options;
        }

        /// <summary>The kinds of item these chests hold (each once): its prefab name and the game's description of it.</summary>
        private static IEnumerable<KeyValuePair<string, ItemDrop.ItemData>> ItemsIn(IEnumerable<StorageModule> stores)
        {
            var seen = new HashSet<string>();
            foreach (StorageModule store in stores)
            {
                if (store == null) continue;
                foreach (string prefab in store.Contents().Keys)
                {
                    if (!seen.Add(prefab)) continue;
                    ItemDrop.ItemData item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData : null;
                    if (item != null) yield return new KeyValuePair<string, ItemDrop.ItemData>(prefab, item);
                }
            }
        }

        /// <summary>What a hit of it does to a monster (not its chopping or mining).</summary>
        private static float Damage(ItemDrop.ItemData item)
        {
            HitData.DamageTypes d = item.GetDamage();
            return d.m_damage + d.m_blunt + d.m_slash + d.m_pierce + d.m_fire + d.m_frost + d.m_lightning + d.m_poison + d.m_spirit;
        }

        /// <summary>Puts the old copy away and takes a copy of this item (none for ""), so many of it.</summary>
        private void Borrow(ref ItemDrop.ItemData held, ref string heldPrefab, string prefabName, int amount = 1)
        {
            Humanoid body = Citizen != null ? Citizen.Body : null;
            if (body == null)   // the settler is being removed: nothing to hand back
            {
                held = null;
                heldPrefab = "";
                return;
            }
            if (held != null)
            {
                if (body.IsItemEquiped(held)) body.UnequipItem(held, false);
                body.GetInventory().RemoveItem(held);
                held = null;
            }
            heldPrefab = "";
            GameObject prefab = prefabName.Length > 0 && ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            if (prefab == null || !body.GetInventory().AddItem(prefab, amount)) return;
            held = body.GetInventory().GetAllItems().LastOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefabName);
            if (held == null) return;
            heldPrefab = prefabName;
            Log.Info($"{Citizen.CitizenName} keeps watch with a copy of the colony's {prefabName}{(amount > 1 ? $" (x{amount})" : "")} (it stays in the chest)");
        }

        private void HoldGear()
        {
            Humanoid body = Citizen.Body;
            if (_weapon != null && !body.IsItemEquiped(_weapon)) body.EquipItem(_weapon, false);
            if (_shield != null && _weapon != null && !_ranged && !body.IsItemEquiped(_shield)
                && WeaponKinds.Of(_weapon.m_shared.m_itemType.ToString(), _weapon.m_shared.m_skillType.ToString()) == WeaponKind.OneHanded)
                body.EquipItem(_shield, false);
        }

        // ---- Watching and fighting ----

        private float PatrolRadius => GuardLevels.Radius(_tower.Level);

        /// <summary>
        /// The monsters near the colony within his watch: the one attacking someone of the colony first, else the nearest.
        /// 1.3: also one attacking someone inside the colony's area within his help radius; while the alarm sounds, those
        /// within 30 m of where it was sounded, and still (1.4's review) the calls for help.
        /// </summary>
        private void LookRound()
        {
            Vector3 towerAt = _tower.transform.position;
            float patrol = PatrolRadius;
            bool raid = Ai.RaidAtHome;
            float watch = GuardRules.WatchRadius(patrol, raid), help = GuardRules.HelpRadius(patrol, ColonyConfig.HelpRadius.Value);
            bool alarm = Alarm(out Vector3 alarmAt);
            s_nearby.Clear();
            s_seen.Clear();
            // While the alarm sounds: round where it was sounded, and round his tower for calls for help (1.4's review).
            if (alarm) Character.GetCharactersInRange(alarmAt, ColonyAlarm.Radius, s_nearby);
            Character.GetCharactersInRange(towerAt, alarm ? help : Mathf.Max(watch, help), s_nearby);
            var candidates = new List<TargetCandidate>();
            var who = new List<Character>();
            var leash = new List<KeyValuePair<Vector3, float>>();
            Vector3 me = Citizen.transform.position;
            string colony = Citizen.ColonyId;
            foreach (Character c in s_nearby)
            {
                if (c == null || c == Citizen.Body || !s_seen.Add(c)) continue;
                TargetFacts facts = FactsOf(Citizen.Body, c);
                if (!GuardRules.IsTarget(facts) || !Reachable(c) || _givenUp.IsBlocked(IdOf(c), Time.time)) continue;
                Vector3 at = c.transform.position;
                float fromTower = Utils.DistanceXZ(at, towerAt);
                if (alarm)
                {
                    AlarmAnswer answer = ColonyAlarm.Answers(Utils.DistanceXZ(at, alarmAt), fromTower, facts.AttackingColony,
                                                             Inside(c, at, colony, facts.AttackingColony), help);
                    if (answer == AlarmAnswer.None) continue;
                    leash.Add(answer == AlarmAnswer.Alarm ? new KeyValuePair<Vector3, float>(alarmAt, ColonyAlarm.Radius)
                                                          : new KeyValuePair<Vector3, float>(towerAt, help));
                }
                else
                {
                    float nearest = NearestOfColony(at);
                    bool inWatch = GuardRules.InWatch(fromTower, patrol, nearest, raid);
                    if (!inWatch && !GuardRules.Answers(fromTower, patrol, nearest, raid, facts.AttackingColony, Inside(c, at, colony, facts.AttackingColony), help)) continue;
                    leash.Add(new KeyValuePair<Vector3, float>(towerAt, inWatch ? watch : help));
                }
                candidates.Add(new TargetCandidate { Distance = Utils.DistanceXZ(at, me), AttackingColony = facts.AttackingColony });
                who.Add(c);
            }
            int pick = GuardRules.ChooseTarget(candidates);
            if (pick < 0) return;
            Character chosen = who[pick];
            if (_target != null && chosen != _target && !(candidates[pick].AttackingColony && !FactsOf(Citizen.Body, _target).AttackingColony)) return;   // he keeps his
            if (chosen != _target) Log.Info($"{Citizen.CitizenName} goes for the {chosen.m_name} ({Utils.DistanceXZ(chosen.transform.position, me):0.0} m away, " +
                                            $"{chosen.GetFaction()}, {chosen.GetBaseAI()?.GetType().Name ?? "no brain"}" +
                                            $"{(candidates[pick].AttackingColony ? ", attacking someone of the colony" : "")}" +
                                            $"{(alarm && leash[pick].Key == alarmAt ? ", the alarm" : leash[pick].Value > watch + 0.01f || alarm ? ", a call for help" : "")})");
            if (chosen != _target && raid) CallRaiders(chosen);
            SetTarget(chosen);
            _leashCentre = leash[pick].Key;
            _leashRadius = leash[pick].Value;
        }

        /// <summary>
        /// In a raid at his colony the first guard to go for a raider calls out where they come from (1.3, once a raid on this
        /// computer): "Ulf: raiders coming from the north-east!" to the players at the colony (GuardRules.HearsRaidCall; 1.4.1:
        /// it went to every player on the server).
        /// </summary>
        private void CallRaiders(Character raider)
        {
            string colony = Citizen.ColonyId;
            if (s_raidCalled.TryGetValue(colony, out float at) && Time.time - at < 600f) return;
            s_raidCalled[colony] = Time.time;
            Vector3? hall = ColonyAreas.HallPosition(colony);
            Vector3 from = raider.transform.position - (hall ?? _tower.transform.position);
            string words = $"{Citizen.CitizenName}: $vc_raid_from {Compass.Token(from.x, from.z)}";
            Log.Event($"Colony {colony}: {Citizen.CitizenName} calls out raiders ({Compass.Token(from.x, from.z)})");
            Vector3 centre = hall ?? _tower.transform.position;
            float radius = HallRules.RadiusOf(colony);
            foreach (Player player in Player.GetAllPlayers())
                if (player != null && GuardRules.HearsRaidCall(Utils.DistanceXZ(player.transform.position, centre), radius))
                    player.Message(MessageHud.MessageType.Center, words);   // the game's own: to that player's computer, translated there
        }

        /// <summary>What a guard knows of this creature (also asked when his blow lands, CitizenPatches).</summary>
        internal static TargetFacts FactsOf(Humanoid guard, Character c)
        {
            BaseAI ai = c.GetBaseAI();
            string brain = ai is AnimalAI ? "AnimalAI" : ai is MonsterAI ? "MonsterAI" : "";
            string faction = c.GetFaction().ToString();
            Character after = GuardRules.Attacks(brain) ? ai.GetTargetCreature() : null;
            return new TargetFacts
            {
                IsPlayer = c.IsPlayer(),
                IsSettler = c.GetComponent<Citizen>() != null,
                Tamed = c.IsTamed(),
                Dead = c.IsDead(),
                Hostile = guard != null && BaseAI.IsEnemy(guard, c) && GuardRules.IsMonster(faction, brain),
                PassiveAnimal = GuardRules.IsAnimal(faction, brain, ai != null && ai.m_passiveAggresive),
                AttackingColony = after != null && (after.IsPlayer() || after.GetComponent<Citizen>() != null),
            };
        }

        /// <summary>Inside the colony's area: the monster or the one it attacks (review of 1.3: one shooting a settler from just outside was let be).</summary>
        private static bool Inside(Character monster, Vector3 at, string colony, bool attackingColony) =>
            ColonyAreas.ColonyAt(at) == colony || (attackingColony && VictimInside(monster, colony));

        /// <summary>The one this monster attacks stands inside the colony's area.</summary>
        private static bool VictimInside(Character monster, string colony)
        {
            Character victim = monster.GetBaseAI()?.GetTargetCreature();
            return victim != null && ColonyAreas.ColonyAt(victim.transform.position) == colony;
        }

        /// <summary>He can walk up to it (not swimming, flying or high above the ground), or shoot it with his bow (1.4.2).</summary>
        private bool Reachable(Character c)
        {
            Vector3 at = c.transform.position;
            float ground = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(at) : at.y;
            return GuardRules.Reachable(at.y - ground, c.IsSwimming(), c.IsFlying(), _ranged);
        }

        private string WhyOutOfReach(Character c)
        {
            Vector3 at = c.transform.position;
            float ground = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(at) : at.y;
            return GuardRules.WhyOutOfReach(at.y - ground, c.IsSwimming(), c.IsFlying(), _ranged);
        }

        private static string IdOf(Character c)
        {
            ZNetView nview = c.GetComponent<ZNetView>();
            return nview != null && nview.IsValid() ? nview.GetZDO().m_uid.ToString() : c.GetInstanceID().ToString();
        }

        /// <summary>
        /// Chosen again at each look round, a target's give-up clock starts again: he keeps after a monster while it stays in
        /// his watch, and the clock runs only once it has left (review of 1.3: a clock that ran on made him give up a Greyling
        /// beside him by a wall and leave it be for a minute — the fifth 1.3 run). What he can't walk up to Reachable rules out.
        /// </summary>
        private void SetTarget(Character target)
        {
            if (target != _target)
            {
                _leashCentre = _tower.transform.position;   // his tower's watch unless LookRound says otherwise
                _leashRadius = GuardRules.WatchRadius(PatrolRadius, Ai.RaidAtHome);
                _giveUpAfter = GuardRules.GiveUpAfter(Utils.DistanceXZ(target.transform.position, Citizen.transform.position));   // the walk there first
            }
            _target = target;
            _targetHealth = target.GetHealth();
            _harmedAt = Time.time;
            _cornered = false;
            _backStuck.Reset();
        }

        /// <summary>How far the spot is from the nearest building, settler or player of his colony.</summary>
        private float NearestOfColony(Vector3 at)
        {
            string colony = Citizen.ColonyId;
            float best = float.MaxValue;
            foreach (ColonyBuilding b in ColonyBuilding.All)
                if (b != null && b.IsValid && b.ColonyId == colony) best = Mathf.Min(best, Utils.DistanceXZ(b.transform.position, at));
            foreach (Citizen c in Citizen.All)
                if (c != null && c.ColonyId == colony && c != Citizen) best = Mathf.Min(best, Utils.DistanceXZ(c.transform.position, at));
            foreach (Player p in Player.GetAllPlayers())
                if (p != null && !p.IsDead()) best = Mathf.Min(best, Utils.DistanceXZ(p.transform.position, at));
            return best;
        }

        /// <summary>Whether he goes on after it: it lives, is still a target, and he hasn't gone too far from his tower.</summary>
        private bool StillAfter(Character target)
        {
            if (target == null || !GuardRules.IsTarget(FactsOf(Citizen.Body, target))) return false;
            // A blow that lands is progress; none for a while (it is out of his reach: in the water, on a roof), he gives it up.
            float health = target.GetHealth();
            if (health < _targetHealth - 0.01f)
            {
                _harmedAt = Time.time;
                _giveUpAfter = GuardRules.GiveUpSeconds;   // there: the walk is behind him
            }
            _targetHealth = health;
            if (!Reachable(target) || Time.time - _harmedAt > _giveUpAfter)
            {
                Log.Info($"{Citizen.CitizenName} gives the {target.m_name} up ({WhyOutOfReach(target) ?? $"no blow landed for {Time.time - _harmedAt:0} s"})");
                _givenUp.Add(IdOf(target), Time.time);
                return false;
            }
            // The leash of the watch he took it from: his tower's (1.1), a call's, the alarm's (1.3).
            float fromCentre = Utils.DistanceXZ(Citizen.transform.position, _leashCentre);
            Vector3 velocity = Citizen.Body.GetVelocity(), outward = Citizen.transform.position - _leashCentre;
            float speed = GuardRules.OutwardSpeed(outward.x, outward.z, velocity.x, velocity.z);
            if (!GuardRules.LetGoFrom(fromCentre, _leashRadius, ColonyConfig.ChaseBeyondPatrol.Value, speed)) return true;
            Log.Info($"{Citizen.CitizenName} lets the {target.m_name} go ({fromCentre:0.0} m from the middle of their watch) and goes back");
            return false;
        }

        private void ForgetTarget()
        {
            if (!ReferenceEquals(_target, null)) SwingLog.EndSwing(Citizen);
            _target = null;
        }

        /// <summary>
        /// Walks up to weapon's reach, faces it and swings (the game's own attack, as the lumberjack swings his axe); with a
        /// shield he holds it up between blows and while it is near (1.2).
        /// </summary>
        private void Fight()
        {
            Vector3 at = _target.transform.position;
            float reach = Mathf.Max(MinReach, _weapon.m_shared.m_attack.m_attackRange) + _target.GetRadius();
            float gap = Utils.DistanceXZ(Citizen.transform.position, at);
            Humanoid body = Citizen.Body;
            bool inAttack = body.InAttack();
            if (_wasInAttack && !inAttack) _swungAt = Time.time;   // a blow just ended
            _wasInAttack = inAttack;
            bool shieldUp = _shield != null && gap <= ShieldRange && !inAttack && _shieldGuard.Raised(Time.time);
            if (gap > reach)
            {
                _inReachAt = -1f;
                Block(shieldUp);
                // The walk counts as arrived a little short of where he stops (review of 1.1): the last steps he takes straight.
                if (gap > reach + 3f) Ai.GoTo(at, reach * 0.8f, hurry: true);
                else Ai.StepTowards(at);
                return;
            }
            Ai.Idle();
            Ai.Face(at);
            if (_inReachAt < 0f) _inReachAt = Time.time;
            // With a shield he waits a moment after each blow, and while the monster strikes he holds it up rather than swing
            // into its blow (the second 1.2 run: blows landing during his own swings were never blocked) — but never longer than
            // ShieldGuard.SwingOverdue without a blow of his (the 1.3 runs: a Greyling striking on and on at the player beside it
            // kept his shield up a minute; its blows at others still reach him, so he guards against all of them).
            bool strikes = _target.InAttack();
            if (strikes && !_targetWasStriking) _guardUntil = Time.time + StrikeGuard;   // a blow begins: once a blow, never a stalemate (the third run)
            _targetWasStriking = strikes;
            // His last blow, or his coming within reach, whichever is later (the tenth 1.3 run: a blow of an earlier fight made
            // his first one here "overdue", into the monster's first blow, unguarded).
            float sinceOwnBlow = Time.time - Mathf.Max(_swungAt, _inReachAt);
            // While its blow is under way, not a set time from its start (the full 1.3 run: a Greyling's blow lands after the
            // 0.6 s, so his shield was down when it came — struck, never a block).
            float guardLeft = strikes ? Mathf.Max(_guardUntil - Time.time, 0.1f) : _guardUntil - Time.time;
            bool guarding = _shield != null && ShieldGuard.Guarding(_shieldGuard.Raised(Time.time), sinceOwnBlow, guardLeft);
            bool ready = !inAttack && Ai.IsFacing(at) && !guarding;
            if (!ready)
            {
                Block(shieldUp);
                return;
            }
            Block(false);
            // His swing hurts only what he aims at (as the woodcutter's): never a wall, a crop or a tree beside it (review of 1.1).
            if (body.StartAttack(_target, false)) SwingLog.BeginSwing(Citizen, null, _target.gameObject);
        }

        /// <summary>
        /// An archer (1.2): closes to shooting range where he can see it, steps back from one too near (cornered, he shoots
        /// anyway), aims at its middle a little high for the arrow's fall, draws fully and shoots.
        /// </summary>
        private void Shoot()
        {
            Block(false);   // a bow takes both hands (a shield raised as a warrior comes down; 1.2's review)
            Vector3 centre = _target.GetCenterPoint();
            Vector3 me = Citizen.transform.position;
            float gap = Utils.DistanceXZ(me, centre);
            bool seen = InSight(_target);
            if (gap > ShootRange || !seen)
            {
                // Out of range he closes to 15 m; in range but out of sight (behind a wall, a rock) he walks on until he sees it
                // (1.2's review: within 15 m he stood where he was).
                bool walked = Ai.GoTo(_target.transform.position, gap > ShootRange ? ShootRange * 0.6f : MinReach, hurry: true);
                bool endedShort = walked && gap > ShootRange;
                if (!endedShort) _wayEndedAt = float.NegativeInfinity;
                else if (float.IsNegativeInfinity(_wayEndedAt)) _wayEndedAt = Time.time;
                if (GuardRules.OutOfBowReach(endedShort, gap, ShootRange, Time.time - _wayEndedAt))
                {
                    Log.Info($"{Citizen.CitizenName} gives the {_target.m_name} up: their way ends {gap:0} m from it, past a bow's reach");
                    _givenUp.Add(IdOf(_target), Time.time);
                    _wayEndedAt = float.NegativeInfinity;
                    ForgetTarget();
                    return;
                }
                if (Time.time >= _approachLogAt)
                {
                    _approachLogAt = Time.time + 5f;
                    Log.Info($"{Citizen.CitizenName} closes on the {_target.m_name} to shoot ({gap:0.0} m, {_target.transform.position.y - me.y:0.0} m above them, " +
                             $"in sight {seen}, their way {(walked ? "ended" : "goes on")})");
                }
                return;
            }
            if (gap < TooClose && !_cornered)
            {
                Vector3 away = me - centre;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = -Citizen.transform.forward;
                Ai.StepTowards(me + away.normalized * 4f);
                if (_backStuck.Update(TooClose + 3f - gap, Time.time)) _cornered = true;   // no room to step back: he shoots from here
                return;
            }
            if (gap >= TooClose + 2f) _cornered = false;
            Ai.Idle();
            Vector3 aim = centre + Vector3.up * (gap * 0.02f);
            Ai.Face(aim);
            Humanoid body = Citizen.Body;
            if (body.InAttack() || !Ai.IsFacing(aim)) return;
            // A crossbow is loaded between shots, as a player's (review of 1.2: the game loads one only for players; see CitizenPatches).
            Attack attack = _weapon.m_shared.m_attack;
            if (attack.m_requiresReload && Time.time - _shotAt < Mathf.Max(1f, attack.m_reloadTime)) return;
            s_drawTime(body) = 10f;   // fully drawn: settlers don't hold the button as a player does
            // His arrow hurts only what he aims at, as a swing (a wall it hits takes nothing).
            if (!body.StartAttack(_target, false)) return;
            _shotAt = Time.time;
            SwingLog.BeginSwing(Citizen, null, _target.gameObject);
        }

        /// <summary>He sees its middle from his eyes: no piece, rock or ground between.</summary>
        private bool InSight(Character target)
        {
            if (s_sightMask == 0) s_sightMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            Vector3 eye = Citizen.Body.GetEyePoint(), to = target.GetCenterPoint();
            if (!Physics.Linecast(eye, to, out RaycastHit hit, s_sightMask, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider != null && hit.collider.GetComponentInParent<Character>() == target;
        }

        /// <summary>Raises or lowers his shield (the game's own block: its block power against hits from the front).</summary>
        private void Block(bool up)
        {
            Humanoid body = Citizen != null ? Citizen.Body : null;
            if (body == null || s_blocking(body) == up) return;
            s_blocking(body) = up && _shield != null;
        }

        // ---- The colony's ballistas (1.3) ----

        /// <summary>
        /// On watch with nothing to fight he keeps the ballistas within his patrol loaded, as settlers keep fires fed: one below
        /// half gets what the colony's chests can spare of an ammunition it takes (the same kind it holds), fetched from the
        /// nearest such chest, carried over and put in (the game's own call, run by the ballista's owner); what is left goes
        /// back to that chest. True while he is at it.
        /// </summary>
        private bool KeepBallistaLoaded(float dt)
        {
            // What an errand cut short left in his load goes back first (it needs no ballistas, only his rounds).
            if (_ballista == null && Citizen.InHand.Total == 0 &&
                GuardRules.TakesLoadBack(OnRounds, Citizen.Carry.Total, Time.time - _loadBackFailedAt))
            {
                foreach (string item in Citizen.Carry.ItemNames.ToList()) Citizen.InHand.Add(item, Citizen.Carry.Count(item));
                Citizen.Carry.Clear();
                Citizen.SaveCarry();
                Log.Info($"{Citizen.CitizenName} takes what is left in their load ({Citizen.InHand.Total}) back to a chest");
            }
            if (Citizen.InHand.Total > 0 && _ballista == null && OnRounds) return ReturnAmmo();
            if (!LoadingAllowed) return false;
            if (_ballista == null)
            {
                _ballistaTimer -= dt;
                if (_ballistaTimer > 0f) return false;
                _ballistaTimer = BallistaCheckSeconds;
                if (!FindBallista()) return false;
            }
            Citizen.Status = "$vc_status_loading";
            ZNetView view = _ballista.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                _ballista = null;
                return true;
            }
            if (!_ammoTaken)
            {
                ColonyBuilding chest = ColonyBuilding.FindById(_ballistaChest);
                if (chest == null || chest.Storage == null)
                {
                    _ballista = null;
                    return true;
                }
                if (!Ai.GoTo(chest.transform.position, ChestReach)) return true;
                Ai.Idle();
                int spare = chest.Storage.Spare(_ballistaAmmo);   // what the chest keeps stays (Keep in stock)
                while (Citizen.InHand.Count(_ballistaAmmo) < Mathf.Min(_ballistaWant, spare) && chest.Storage.TryTakeItem(_ballistaAmmo, Citizen.InHand)) { }
                Citizen.SaveCarry();
                if (Citizen.InHand.Count(_ballistaAmmo) == 0)
                {
                    _ballista = null;
                    return true;
                }
                _ammoTaken = true;
                return true;
            }
            Vector3 at = _ballista.transform.position;
            if (!Ai.GoTo(at, BallistaReach)) return true;
            Ai.Idle();
            Ai.Face(at);
            // Only what it still takes (someone may have loaded it meanwhile), and only its own kind.
            int room = _ballista.m_maxAmmo - _ballista.GetAmmo();
            bool sameKind = _ballista.GetAmmo() == 0 || _ballista.GetAmmoType() == _ballistaAmmo;
            int n = sameKind ? Mathf.Min(Citizen.InHand.Count(_ballistaAmmo), room) : 0;
            for (int i = 0; i < n; i++) view.InvokeRPC("RPC_AddAmmo", _ballistaAmmo);
            if (n > 0)
            {
                Citizen.InHand.Remove(_ballistaAmmo, n);
                Citizen.SaveCarry();
                LoadedForTest += n;
                Log.Info($"{Citizen.CitizenName} put {n} {_ballistaAmmo} in the ballista at ({at.x:0}, {at.z:0})");
            }
            _ballista = null;   // what is left in his hands goes back next (ReturnAmmo)
            return true;
        }

        /// <summary>A ballista of the colony's area within his patrol below half, with an ammunition the chests can spare.</summary>
        private bool FindBallista()
        {
            Vector3 towerAt = _tower.transform.position;
            var pieces = new List<Piece>();
            Piece.GetAllPiecesInRadius(towerAt, PatrolRadius, pieces);
            string colony = Citizen.ColonyId;
            foreach (Piece piece in pieces)
            {
                Turret turret = piece != null ? piece.GetComponent<Turret>() : null;
                ZNetView view = turret != null ? turret.GetComponent<ZNetView>() : null;
                if (view == null || !view.IsValid() || turret.m_allowedAmmo == null) continue;
                int ammo = turret.GetAmmo();
                foreach (Turret.AmmoType kind in turret.m_allowedAmmo)
                {
                    string name = kind.m_ammo != null ? kind.m_ammo.name : "";
                    if (name.Length == 0 || (ammo > 0 && turret.GetAmmoType() != name)) continue;
                    int spare = 0;
                    ColonyBuilding nearest = null;
                    foreach (ColonyBuilding b in ColonyBuilding.All)
                    {
                        if (b == null || !b.IsValid || b.ColonyId != colony || b.Storage == null) continue;
                        int s = b.Storage.Spare(name);
                        if (s <= 0) continue;
                        spare += s;
                        if (nearest == null || Utils.DistanceXZ(b.transform.position, towerAt) < Utils.DistanceXZ(nearest.transform.position, towerAt)) nearest = b;
                    }
                    int want = BallistaRules.ToLoad(ammo, turret.m_maxAmmo, spare);
                    if (want <= 0 || nearest == null) continue;
                    _ballista = turret;
                    _ballistaAmmo = name;
                    _ballistaWant = want;
                    _ballistaChest = nearest.Id;
                    _ammoTaken = false;
                    Log.Info($"{Citizen.CitizenName} goes to load a ballista ({ammo} of {turret.m_maxAmmo}): {want} {name} from {nearest.TypeId} {nearest.Id}");
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// What is left in his hands goes back to the chest it came from (or another of the colony's); what doesn't fit, or
        /// with no chest, goes with his load (review of 1.3: at a full chest he stood there for good). True while he is at it.
        /// </summary>
        private bool ReturnAmmo()
        {
            ColonyBuilding chest = ColonyBuilding.FindById(_ballistaChest);
            if (chest == null || chest.Storage == null)
                chest = ColonyBuilding.All.FirstOrDefault(b => b != null && b.IsValid && b.ColonyId == Citizen.ColonyId && b.Storage != null);
            if (chest == null)
            {
                HandsToLoad();
                _loadBackFailedAt = Time.time;
                return false;
            }
            Citizen.Status = "$vc_status_loading";
            if (!Ai.GoTo(chest.transform.position, ChestReach)) return true;
            Ai.Idle();
            if (chest.Storage.TryDeposit(Citizen.InHand) == StorageModule.DepositResult.Busy) return true;   // a player has it open: he waits
            Citizen.SaveCarry();
            if (Citizen.InHand.Total > 0)
            {
                HandsToLoad();
                _loadBackFailedAt = Time.time;   // full: tried again a while later
            }
            return false;
        }

        /// <summary>On his rounds: guards on, no Hold post and no alarm (what may take him to a chest).</summary>
        private bool OnRounds => Enabled && Orders == GuardOrders.Patrol && !Alarm(out _);

        /// <summary>He may see to the ballistas: guards on, the setting on, on rounds (not holding a post) and no alarm (review of 1.3).</summary>
        private bool LoadingAllowed => Enabled && ColonyConfig.LoadBallistas.Value && Orders == GuardOrders.Patrol && !Alarm(out _);

        /// <summary>The errand ends where he is: what he holds goes with his load.</summary>
        private void DropErrand()
        {
            _ballista = null;
            HandsToLoad();
        }

        /// <summary>Ammunition still in his hands goes with his load, which a settler takes to the Town Hall (1.3).</summary>
        private void HandsToLoad()
        {
            if (Citizen.InHand.Total == 0) return;
            foreach (string item in Citizen.InHand.ItemNames.ToList()) Citizen.Carry.Add(item, Citizen.InHand.Count(item));
            Citizen.InHand.Clear();
            Citizen.SaveCarry();
        }

        // ---- Rounds and rest ----

        /// <summary>A round: to a building of the colony within his patrol (a random one), then back to his post.</summary>
        private void Patrol()
        {
            if (_patrolTo == null)
            {
                Vector3 towerAt = _tower.transform.position;
                float patrol = PatrolRadius;
                List<ColonyBuilding> near = ColonyBuilding.All.Where(b => b != null && b.IsValid && b != _tower && b.ColonyId == Citizen.ColonyId
                                                                          && Utils.DistanceXZ(b.transform.position, towerAt) <= patrol).ToList();
                if (near.Count == 0)
                {
                    EndPatrol();
                    return;
                }
                // A spot outside the building, on his tower's side (1.2: its chest is inside, so his rounds went through doors).
                Vector3 at = near[Random.Range(0, near.Count)].transform.position, toTower = towerAt - at;
                toTower.y = 0f;
                _patrolTo = toTower.sqrMagnitude > 0.01f ? at + toTower.normalized * 5f : at;
                _patrolStuck.Reset();
                RoundsForTest++;
            }
            Vector3 to = _patrolTo.Value;
            float gap = Utils.DistanceXZ(Citizen.transform.position, to);
            if (Ai.GoTo(to, PatrolReach) || _patrolStuck.Update(gap, Time.time)) EndPatrol();
        }

        private void EndPatrol()
        {
            _patrolTo = null;
            _patrolAt = Time.time + GuardDuty.PatrolDelay(Random.value);
        }

        /// <summary>At his tower he heals, full in about a minute (settlers have no healing of their own).</summary>
        private void Heal(float dt)
        {
            _healTimer -= dt;
            if (_healTimer > 0f) return;
            _healTimer = HealEverySeconds;
            Humanoid body = Citizen.Body;
            body.Heal(GuardDuty.HealPerSecond(body.GetMaxHealth()) * HealEverySeconds, false);
        }
    }
}
