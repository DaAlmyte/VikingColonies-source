using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>A circle of a colony's area on the ground: the Town Hall's, or a watchtower's (1.1).</summary>
    public struct AreaCircle
    {
        public AreaCircle(GroundPoint centre, float radius)
        {
            Centre = centre;
            Radius = radius;
        }

        public GroundPoint Centre { get; }
        public float Radius { get; }

        public bool Holds(GroundPoint p) => ColonyRules.Distance(Centre, p) <= Radius;
    }

    /// <summary>One colony's area: its Town Hall's circle and its watchtowers' (only those that count).</summary>
    public sealed class ColonyAreaInfo
    {
        public string ColonyId = "";
        public AreaCircle Hall;
        /// <summary>The Hall circle is the Town Hall's own, not one a hut remembers (1.4).</summary>
        public bool HallLoaded;
        public List<AreaCircle> Towers { get; } = new List<AreaCircle>();
    }

    /// <summary>
    /// The colony's area (1.1, as MineColonies' guard towers claim land): the Town Hall's circle and a circle round each of
    /// its finished watchtowers, never reaching past <c>Colony.MaxReach</c> from the Town Hall. Huts may stand anywhere in it.
    /// </summary>
    public static class ColonyArea
    {
        public static bool Contains(IEnumerable<AreaCircle> circles, GroundPoint p) => circles != null && circles.Any(c => c.Holds(p));

        /// <summary>A tower adds less than this, or nothing.</summary>
        public const float MinTowerRadius = 10f;

        /// <summary>
        /// The circle a tower adds: its radius, cut down so nothing of it lies further than <paramref name="maxReach"/> from the
        /// Town Hall (review of 1.1: an upgrade near the edge took the whole circle away); null when less than
        /// <see cref="MinTowerRadius"/> would be left.
        /// </summary>
        public static AreaCircle? TowerCircle(GroundPoint hall, GroundPoint tower, float radius, float maxReach)
        {
            float fits = Math.Min(radius, maxReach - ColonyRules.Distance(hall, tower));
            return fits >= MinTowerRadius ? new AreaCircle(tower, fits) : (AreaCircle?)null;
        }

        /// <summary>A tower stands once its building was finished, whatever it is doing now (an upgrade, a repair).</summary>
        public static bool TowerStands(BuildOrderRecord order) => order == null || order.Kind != OrderKind.Build || order.State == BuildState.Done;

        /// <summary>
        /// The colony a building at <paramref name="at"/> belongs to: one whose Town Hall's circle holds it (the nearest hall),
        /// else one whose watchtower's circle holds it (the nearest tower); ties by colony id; null when none.
        /// </summary>
        public static string ChooseColony(GroundPoint at, IEnumerable<ColonyAreaInfo> colonies)
        {
            List<ColonyAreaInfo> all = (colonies ?? Enumerable.Empty<ColonyAreaInfo>()).Where(c => c != null).ToList();
            string byHall = Nearest(all.Where(c => c.Hall.Holds(at)).Select(c => new KeyValuePair<string, float>(c.ColonyId, ColonyRules.Distance(c.Hall.Centre, at))));
            if (byHall != null) return byHall;
            return Nearest(all.SelectMany(c => c.Towers.Where(t => t.Holds(at)).Select(t => new KeyValuePair<string, float>(c.ColonyId, ColonyRules.Distance(t.Centre, at)))));
        }

        private static string Nearest(IEnumerable<KeyValuePair<string, float>> candidates)
        {
            string best = null;
            float bestDistance = float.MaxValue;
            foreach (KeyValuePair<string, float> c in candidates)
                if (best == null || c.Value < bestDistance || (Math.Abs(c.Value - bestDistance) < 0.0001f && string.CompareOrdinal(c.Key, best) < 0))
                {
                    best = c.Key;
                    bestDistance = c.Value;
                }
            return best;
        }
    }

    /// <summary>A watchtower's reach by its level (1.1): the guard's patrol radius and the circle it adds to the colony.</summary>
    public static class GuardLevels
    {
        public static float Radius(int level) => 20f + 5f * HutLevels.Clamp(level);

        /// <summary>How many guards a Watchtower holds (1.2): 1 at levels 1–2, 2 at 3–4, 3 at 5 (MineColonies' barracks tower: one a level).</summary>
        public static int Guards(int level) => (HutLevels.Clamp(level) + 1) / 2;
    }

    /// <summary>Where each of a tower's guards stands (1.2): beside the post, 3 m apart, the first on it.</summary>
    public static class GuardPosts
    {
        public const float Spacing = 3f;
        /// <summary>The post without a door to go by: this far in front of the tower's chest (1.1).</summary>
        public const float ChestOut = 3.5f;
        /// <summary>
        /// The post: this far in front of the tower's front door (huts round 2: the chest stands against the back wall),
        /// well clear of where the door lets out (DoorRules.StepBeyond; the review of 2026-10-19: at 1.5 m the first guard
        /// stood there, and anyone leaving the tower waited, gave up and tried again).
        /// </summary>
        public const float DoorOut = 2.8f;

        /// <summary>
        /// Where the guards' post is in the tower's own space (x to its right, z to its front, from its chest): 2.8 m in
        /// front of its front door when its design has one (huts round 2, 2026-10-18: the chest moved to the back wall,
        /// and 3.5 m in front of it was the doorway), else 3.5 m in front of the chest.
        /// </summary>
        public static void PostSpot(bool hasDoor, float doorX, float doorZ, out float x, out float z)
        {
            if (!hasDoor || doorZ <= 0f)
            {
                x = 0f;
                z = ChestOut;
                return;
            }
            x = doorX;
            z = doorZ + DoorOut;
        }

        /// <summary>A post the player chooses (1.3) lies within the tower's patrol circle.</summary>
        public static bool MayPost(float fromTower, float patrol) => fromTower <= patrol;

        /// <summary>Metres to the side of the post: 0, +3, −3, +6…</summary>
        public static float Offset(int index)
        {
            if (index <= 0) return 0f;
            int step = (index + 1) / 2;
            return (index % 2 == 1 ? 1f : -1f) * Spacing * step;
        }

        /// <summary>
        /// At the alarm (review of 1.3): each guard of the colony by his place among them all — the first where it was
        /// sounded, then rings 3 m apart (6 spots on the first, 12 on the second…), each spot about 3 m from the next.
        /// </summary>
        public static void AlarmSpot(int index, out float dx, out float dz)
        {
            dx = dz = 0f;
            if (index <= 0) return;
            int ring = 1, first = 1;
            while (index >= first + 6 * ring)
            {
                first += 6 * ring;
                ring++;
            }
            double angle = 2.0 * Math.PI * (index - first) / (6 * ring);
            dx = (float)(Math.Sin(angle) * Spacing * ring);
            dz = (float)(Math.Cos(angle) * Spacing * ring);
        }
    }

    /// <summary>What a guard knows about a creature near the colony.</summary>
    public struct TargetFacts
    {
        public bool IsPlayer;
        public bool IsSettler;
        public bool Tamed;
        public bool Dead;
        /// <summary>The game counts it an enemy of players (a monster).</summary>
        public bool Hostile;
        /// <summary>A deer, boar, neck…: the game's animals.</summary>
        public bool PassiveAnimal;
        /// <summary>It is after a settler or a player now.</summary>
        public bool AttackingColony;
    }

    public struct TargetCandidate
    {
        public float Distance;
        public bool AttackingColony;
    }

    /// <summary>Whom a guard fights, how far he goes after it, and when he goes back to heal (1.1).</summary>
    public static class GuardRules
    {
        /// <summary>A raid call-out reaches players this far outside the colony's area (coming home to it).</summary>
        public const float RaidCallReach = 30f;

        /// <summary>
        /// Whether a player this far from the Town Hall hears a guard call out raiders: inside the colony's area or near it,
        /// not every player on the server (1.4.0's review: it went to everyone, wherever they were).
        /// </summary>
        public static bool HearsRaidCall(float fromHall, float colonyRadius) => fromHall <= colonyRadius + RaidCallReach;

        /// <summary>After a load he couldn't put away (every chest full), he tries again this much later.</summary>
        public const double LoadBackRetrySeconds = 120;

        /// <summary>
        /// Whether a guard on his rounds takes what is in his load (bolts left from a ballista errand cut short by the alarm
        /// or Hold post, or by a full chest) back to a chest: nothing else ever empties a working guard's load (the whole-mod
        /// review of 1.4.1).
        /// </summary>
        public static bool TakesLoadBack(bool onRounds, int carried, double sinceFailed) =>
            onRounds && carried > 0 && sinceFailed >= LoadBackRetrySeconds;

        /// <summary>A monster this close to a colony building, settler or player is the guard's business.</summary>
        public const float NearColony = 30f;
        /// <summary>In a raid at the colony his watch reaches this much further.</summary>
        public const float RaidWiden = 1.5f;
        /// <summary>A guard who went back to heal comes out again at this much health.</summary>
        public const float ResumeHealth = 0.8f;

        /// <summary>Never a player, a settler, a tame or the dead; an animal only while it attacks the colony; else a monster.</summary>
        public static bool IsTarget(TargetFacts f)
        {
            if (f.IsPlayer || f.IsSettler || f.Tamed || f.Dead) return false;
            return f.PassiveAnimal ? f.AttackingColony : f.Hostile;
        }

        /// <summary>
        /// An animal (a deer, a boar…): the game's animal faction, or the game's animal brain whatever its faction (the 1.1 run:
        /// a deer was taken for a monster). By name: Core knows no game types ("AnimalAI", "MonsterAI", "" for none).
        /// </summary>
        public static bool IsAnimal(string faction, string brain, bool passiveAggressive = false) =>
            faction == "AnimalsVeg" || brain == "AnimalAI" || passiveAggressive;

        /// <summary>
        /// A monster: the game's monster brain, and not a training dummy, something a player summoned, a dwarf or one of
        /// the players' own (the builder's rule for monsters near a site, review of 1.1).
        /// </summary>
        public static bool IsMonster(string faction, string brain) =>
            brain == "MonsterAI" && faction != "TrainingDummy" && faction != "PlayerSpawned" && faction != "Dverger" && faction != "Players";

        /// <summary>He can't reach above this (metres above the ground under it) with a weapon in hand.</summary>
        public const float MaxReachUp = 2.5f;
        /// <summary>After this long fighting it with no blow landing nearer, he gives a target up…</summary>
        public const float GiveUpSeconds = 15f;
        /// <summary>…and leaves it alone this long.</summary>
        public const float IgnoreSeconds = 60f;
        /// <summary>Metres a second he is allowed for the walk to a target before its clock counts (his jog).</summary>
        public const float ApproachSpeed = 4f;

        /// <summary>
        /// How long he may go without a blow landing on a target chosen <paramref name="distance"/> metres away: the walk there,
        /// then <see cref="GiveUpSeconds"/> (review of 1.3: a call 80 m away took longer than that to answer).
        /// </summary>
        public static float GiveUpAfter(float distance) => GiveUpSeconds + Math.Max(0f, distance) / ApproachSpeed;

        /// <summary>How long an archer's way may have ended short of bow range before the target is let go.</summary>
        public const float WayEndedSeconds = 3f;

        /// <summary>
        /// An archer whose way to a target ended short of bow range for <see cref="WayEndedSeconds"/> (a Neck swimming far
        /// out, past the shore where the walk stops) lets it go (the review of polish round 2: they stood at the shore till
        /// the give-up time, then took it again).
        /// </summary>
        public static bool OutOfBowReach(bool wayEnded, float gap, float shootRange, float endedFor) =>
            wayEnded && gap > shootRange && endedFor >= WayEndedSeconds;

        /// <summary>An archer shoots what is up to this many metres above the ground under it (1.4.2).</summary>
        public const float MaxShootUp = 15f;

        /// <summary>
        /// Only what he can walk up to: not something swimming, flying or high above the ground. With a bow and arrows
        /// (<paramref name="ranged"/>), what swims or flies too, and what stands up to <see cref="MaxShootUp"/> high (1.4.2:
        /// archers left a swimming Neck alone that their arrows could reach).
        /// </summary>
        public static bool Reachable(float heightAboveGround, bool swimming, bool flying, bool ranged = false) =>
            ranged ? heightAboveGround <= MaxShootUp : !swimming && !flying && heightAboveGround <= MaxReachUp;

        /// <summary>
        /// Why a target is out of his reach, for the log; null when it isn't (1.4.1's release run: archers gave a Greyling
        /// up at once, and the line didn't say which reason held).
        /// </summary>
        public static string WhyOutOfReach(float heightAboveGround, bool swimming, bool flying, bool ranged = false) =>
            ranged ? (heightAboveGround > MaxShootUp ? $"it is {heightAboveGround.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} m above the ground" : null)
            : swimming ? "it is swimming"
            : flying ? "it is flying"
            : heightAboveGround > MaxReachUp ? $"it is {heightAboveGround.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} m above the ground"
            : null;

        /// <summary>Only a monster's brain attacks what it is after; an animal's "target" is what it flees from.</summary>
        public static bool Attacks(string brain) => brain == "MonsterAI";

        public static float WatchRadius(float patrol, bool raid) => raid ? patrol * RaidWiden : patrol;

        /// <summary>How far from his tower he answers a call for help (1.3): the setting, at least twice his patrol.</summary>
        public static float HelpRadius(float patrol, float setting) => Math.Max(setting, 2f * patrol);

        /// <summary>
        /// He takes it on: in his watch as before, or (1.3) a monster attacking someone of the colony inside its area within
        /// his help radius of the tower.
        /// </summary>
        public static bool Answers(float fromTower, float patrol, float nearestColonyThing, bool raid, bool attackingColony, bool insideArea, float helpRadius) =>
            InWatch(fromTower, patrol, nearestColonyThing, raid) || (attackingColony && insideArea && fromTower <= helpRadius);

        /// <summary>Gone too far (or, running, would stop too far) from the middle of his watch: <paramref name="radius"/> + the chase.</summary>
        public static bool LetGoFrom(float fromCentre, float radius, float chaseBeyond, float speed = 0f) =>
            fromCentre + Math.Max(0f, speed) * StopSeconds > radius + Math.Max(0f, chaseBeyond);

        /// <summary>
        /// His speed away from the middle of his watch (<paramref name="dx"/>, <paramref name="dz"/>: from the middle to him):
        /// running round it or back towards it carries him no further out (1.2's review: the whole speed let him go early).
        /// </summary>
        public static float OutwardSpeed(float dx, float dz, float vx, float vz)
        {
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            if (distance < 0.001f) return 0f;
            return Math.Max(0f, (dx * vx + dz * vz) / distance);
        }

        public static bool InWatch(float fromTower, float patrol, float nearestColonyThing, bool raid) =>
            fromTower <= WatchRadius(patrol, raid) && nearestColonyThing <= NearColony;

        /// <summary>Seconds a running guard takes to stop: he lets go where he would stop, not where he is (the 1.2 run).</summary>
        public const float StopSeconds = 0.4f;

        /// <summary>He has gone (or, running at <paramref name="speed"/>, would stop) too far from his tower: he lets it go and returns.</summary>
        public static bool LetGo(float fromTower, float patrol, float chaseBeyond, bool raid, float speed = 0f) =>
            LetGoFrom(fromTower, WatchRadius(patrol, raid), chaseBeyond, speed);

        /// <summary>One attacking a settler or a player first, then the nearest; -1 when none.</summary>
        public static int ChooseTarget(IReadOnlyList<TargetCandidate> candidates)
        {
            int best = -1;
            for (int i = 0; i < (candidates?.Count ?? 0); i++)
            {
                if (best < 0) { best = i; continue; }
                TargetCandidate c = candidates[i], b = candidates[best];
                if (c.AttackingColony != b.AttackingColony ? c.AttackingColony : c.Distance < b.Distance) best = i;
            }
            return best;
        }

        /// <summary>Below <paramref name="retreatBelow"/> he goes back to heal, and stays until <paramref name="resumeAt"/>.</summary>
        public static bool Retreating(bool wasRetreating, float health, float retreatBelow, float resumeAt = ResumeHealth) =>
            wasRetreating ? health < resumeAt : health < retreatBelow;
    }

    public enum GuardAction { NeedsWeapon, Retreat, Fight, ReturnToPost, Patrol, OnWatch, Loading }

    public struct GuardSituation
    {
        public bool HasWeapon;
        public bool Retreating;
        public bool HasTarget;
        public bool AtPost;
        /// <summary>Time for a round of the colony.</summary>
        public bool PatrolDue;
        /// <summary>On a round now.</summary>
        public bool Patrolling;
        /// <summary>Loading a ballista (1.3): fetching, carrying or putting back its ammunition, wherever that takes him.</summary>
        public bool Loading;
    }

    /// <summary>What a guard does next (1.1): armed, hurt, a monster, a round, his post.</summary>
    public static class GuardBrain
    {
        public static GuardAction Decide(GuardSituation s)
        {
            if (!s.HasWeapon) return GuardAction.NeedsWeapon;
            if (s.Retreating) return GuardAction.Retreat;
            if (s.HasTarget) return GuardAction.Fight;
            if (s.Loading) return GuardAction.Loading;   // an errand he finishes before a round (review of 1.3)
            if (s.Patrolling || s.PatrolDue) return GuardAction.Patrol;
            return s.AtPost ? GuardAction.OnWatch : GuardAction.ReturnToPost;
        }
    }

    /// <summary>A piece of armour a guard might wear (1.2): its slot ("Helmet", "Chest", "Legs", "Shoulder") and armour.</summary>
    public struct ArmourOption
    {
        public ArmourOption(string prefab, string slot, float armour)
        {
            Prefab = prefab ?? "";
            Slot = slot ?? "";
            Armour = armour;
        }

        public string Prefab { get; }
        public string Slot { get; }
        public float Armour { get; }
    }

    /// <summary>The best piece for each slot by its armour (1.2); no era limit, as for weapons.</summary>
    public static class ArmourChoice
    {
        public static Dictionary<string, string> Best(IReadOnlyList<ArmourOption> options)
        {
            var best = new Dictionary<string, ArmourOption>();
            foreach (ArmourOption o in options ?? new ArmourOption[0])
            {
                if (o.Slot.Length == 0 || o.Prefab.Length == 0) continue;
                if (!best.TryGetValue(o.Slot, out ArmourOption had) || o.Armour > had.Armour
                    || (o.Armour == had.Armour && string.CompareOrdinal(o.Prefab, had.Prefab) < 0))
                    best[o.Slot] = o;
            }
            return best.ToDictionary(kv => kv.Key, kv => kv.Value.Prefab);
        }
    }

    /// <summary>
    /// A shield held up between blows (1.2): after three blocks within 3 seconds it comes down for 1.5 seconds — settlers
    /// have no stamina to hold it forever (Hearthfolk's rule).
    /// </summary>
    public sealed class ShieldGuard
    {
        public const int Blocks = 3;
        public const double Within = 3.0, Lowered = 1.5;
        /// <summary>He waits this long after each blow of his with the shield up…</summary>
        public const float Pause = 0.5f;
        /// <summary>…and never longer than this without striking (the fifth and seventh 1.3 runs: no blow for a minute).</summary>
        public const float SwingOverdue = 2.5f;

        /// <summary>
        /// Between blows with the shield raised (not lowered after three blocks): just after his own blow, or while a monster's
        /// blow at him is coming (<paramref name="guardLeft"/> seconds of it); never once a blow of his is overdue.
        /// </summary>
        public static bool Guarding(bool raised, float sinceOwnBlow, float guardLeft) =>
            raised && sinceOwnBlow < SwingOverdue && (sinceOwnBlow < Pause || guardLeft > 0f);

        private readonly Queue<double> _blocks = new Queue<double>();
        private double _downUntil = double.NegativeInfinity;

        public bool Raised(double now) => now >= _downUntil;

        public void Blocked(double now)
        {
            _blocks.Enqueue(now);
            while (_blocks.Count > 0 && now - _blocks.Peek() > Within) _blocks.Dequeue();
            if (_blocks.Count < Blocks) return;
            _downUntil = now + Lowered;
            _blocks.Clear();
        }
    }

    public enum WeaponKind { OneHanded, TwoHanded, Shield, Bow, Tool, Other, Ammo }

    /// <summary>A Watchtower's guards walk rounds, or hold their post (1.3: a gatehouse).</summary>
    public enum GuardOrders { Patrol = 0, HoldPost = 1 }

    /// <summary>Why a guard takes on a monster while the alarm sounds, and so which leash holds him.</summary>
    public enum AlarmAnswer { None = 0, Alarm = 1, Help = 2 }

    /// <summary>
    /// The Town Hall's alarm (1.3, Hearthfolk's alert, MineColonies' rally banner): while it sounds everyone else shelters
    /// and the guards gather where it was sounded and fight within <see cref="Radius"/> of it; it ends by itself.
    /// </summary>
    public static class ColonyAlarm
    {
        public const double Seconds = 300;
        public const float Radius = 30f;

        public static double Until(double now) => now + Seconds;

        public static bool Active(double until, double now) => until > 0 && now < until;

        /// <summary>
        /// While it sounds: any monster within <see cref="Radius"/> of where it was sounded (that area holds him), and still
        /// (1.4's review) one attacking someone of the colony inside its area within his help radius of the tower.
        /// </summary>
        public static AlarmAnswer Answers(float fromAlarm, float fromTower, bool attackingColony, bool insideArea, float helpRadius)
        {
            if (fromAlarm <= Radius) return AlarmAnswer.Alarm;
            return attackingColony && insideArea && fromTower <= helpRadius ? AlarmAnswer.Help : AlarmAnswer.None;
        }
    }

    /// <summary>Where something lies, in words (1.3: raiders called out): eight directions, z north.</summary>
    public static class Compass
    {
        private static readonly string[] s_tokens =
            { "$vc_dir_n", "$vc_dir_ne", "$vc_dir_e", "$vc_dir_se", "$vc_dir_s", "$vc_dir_sw", "$vc_dir_w", "$vc_dir_nw" };

        public static string Token(float dx, float dz)
        {
            double degrees = Math.Atan2(dx, dz) * 180.0 / Math.PI;   // 0 north, 90 east
            int sector = (int)Math.Round(degrees / 45.0);
            return s_tokens[((sector % 8) + 8) % 8];
        }
    }

    /// <summary>A ballista of the colony kept loaded by its guards (1.3, Viking Settlements' engineer).</summary>
    public static class BallistaRules
    {
        /// <summary>How many to put in: none at half or more; else what it lacks, at most what the chests can spare.</summary>
        public static int ToLoad(int ammo, int max, int spare)
        {
            if (max <= 0 || ammo * 2 >= max) return 0;
            return Math.Max(0, Math.Min(max - ammo, spare));
        }
    }

    /// <summary>A Watchtower's guard fights up close or shoots (1.2).</summary>
    public enum GuardRole { Warrior = 0, Archer = 1 }

    /// <summary>An item a guard might carry, as the game describes it.</summary>
    public struct WeaponOption
    {
        /// <param name="ammoType">A bow's ammunition, or ammunition's own kind ("$ammo_arrows", "$ammo_bolts"); "" for the rest.</param>
        public WeaponOption(string prefab, WeaponKind kind, float damage, float block, string ammoType = "")
        {
            Prefab = prefab ?? "";
            Kind = kind;
            Damage = damage;
            Block = block;
            AmmoType = ammoType ?? "";
        }

        public string Prefab { get; }
        public WeaponKind Kind { get; }
        public float Damage { get; }
        public float Block { get; }
        public string AmmoType { get; }
    }

    /// <summary>What kind of thing an item is to a guard, from the game's item type and skill (by name: Core knows no game types).</summary>
    public static class WeaponKinds
    {
        /// <param name="attackType">Its first attack's kind ("Horizontal", "Projectile"…): a melee kind thrown is no melee weapon (1.2).</param>
        public static WeaponKind Of(string itemType, string skill, string attackType = "")
        {
            WeaponKind kind = OfType(itemType, skill);
            return (kind == WeaponKind.OneHanded || kind == WeaponKind.TwoHanded) && attackType == "Projectile" ? WeaponKind.Other : kind;
        }

        private static WeaponKind OfType(string itemType, string skill)
        {
            switch (itemType)
            {
                case "Ammo": return WeaponKind.Ammo;
                case "Shield": return WeaponKind.Shield;
                case "Bow": return WeaponKind.Bow;
                case "Tool": return WeaponKind.Tool;
                case "OneHandedWeapon":
                case "TwoHandedWeapon":
                case "TwoHandedWeaponLeft":
                    switch (skill)
                    {
                        case "Pickaxes": return WeaponKind.Tool;
                        case "Crossbows": return WeaponKind.Bow;
                        case "Swords":
                        case "Knives":
                        case "Clubs":
                        case "Polearms":
                        case "Spears":
                        case "Axes":
                        case "Unarmed":
                            return itemType == "OneHandedWeapon" ? WeaponKind.OneHanded : WeaponKind.TwoHanded;
                        default: return WeaponKind.Other;   // magic, a fishing rod
                    }
                default: return WeaponKind.Other;
            }
        }
    }

    /// <summary>
    /// The guard's duty (1.1): armed and on duty he stays out (no fleeing, sheltering in a raid or going in at night); hurt,
    /// he heals at his tower in about a minute; he walks a round every 2–4 minutes; killed by a monster, he fell defending
    /// the colony; a raid held off names the guards.
    /// </summary>
    public static class GuardDuty
    {
        public const float HealSeconds = 60f;
        public const float PatrolMinSeconds = 120f, PatrolMaxSeconds = 240f;
        /// <summary>The guards' names share one saga word, which holds at most this many characters.</summary>
        public const int MaxNamesLength = 64;

        public static IndoorReason Indoors(IndoorReason reason, bool onDuty) => onDuty ? IndoorReason.None : reason;

        /// <summary>Armed, guards on, and not hurt: hurt, he shelters or runs from a monster as anyone (review of 1.1), then heals at his tower.</summary>
        public static bool OnDuty(bool armed, bool enabled, bool retreating) => armed && enabled && !retreating;

        /// <summary>
        /// A guard whose work brain isn't made yet counts as on duty for that moment, so his work runs, makes it and arms him
        /// (the whole-mod review of 1.4.1: made at night, in the evening or in a raid, it never was, and he went to bed).
        /// </summary>
        public static bool NeedsBrain(bool hasBrain, bool isGuard, bool enabled) => !hasBrain && isGuard && enabled;

        public static float HealPerSecond(float maxHealth) => Math.Max(0f, maxHealth) / HealSeconds;

        public static float PatrolDelay(float random01) =>
            PatrolMinSeconds + (PatrolMaxSeconds - PatrolMinSeconds) * Math.Max(0f, Math.Min(1f, random01));

        public static DeathCause Cause(DeathCause cause) =>
            cause.Token == DeathCauses.KilledBy ? new DeathCause(DeathCauses.Defending, cause.Arg) : cause;

        /// <summary>His health when his maximum changes: the same share of it (review of 1.2: a raised maximum started empty).</summary>
        public static float HealthAfter(float health, float oldMax, float newMax) =>
            oldMax <= 0f ? 0f : newMax * Math.Max(0f, Math.Min(1f, health / oldMax));

        /// <summary>His health with Fighting (1.2, MineColonies' knight): half again at 100.</summary>
        public static float MaxHealth(float baseHealth, float fighting) => baseHealth * (1f + 0.5f * Math.Max(0f, Math.Min(1f, fighting / 100f)));

        private static readonly string[] s_onDuty = { "$vc_status_on_watch", "$vc_status_patrolling", "$vc_status_fighting", "$vc_status_resting_hurt", "$vc_status_loading" };

        /// <summary>A status of a guard on duty (armed): on watch, on his round, fighting, healing (1.2); not "needs a weapon".</summary>
        public static bool OnDutyStatus(string status) => status != null && s_onDuty.Any(s => status.StartsWith(s, StringComparison.Ordinal));

        /// <summary>The colony's guards on duty, as the roster has them (the same on every computer).</summary>
        public static List<string> Guarding(IEnumerable<RosterEntry> entries) =>
            (entries ?? Enumerable.Empty<RosterEntry>())
            .Where(e => e != null && e.Job == SaveKeys.GuardJob && !string.IsNullOrEmpty(e.Workplace) && !e.IsMissing && OnDutyStatus(e.Status))
            .Select(e => e.Name).ToList();

        public static string Names(IEnumerable<string> names)
        {
            string joined = string.Join(", ", (names ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrEmpty(n)).ToArray());
            return joined.Length <= MaxNamesLength ? joined : joined.Substring(0, MaxNamesLength - 1) + "…";
        }
    }

    public sealed class GuardGear
    {
        public string Weapon = "";
        public string Shield = "";
        /// <summary>An archer's ammunition (1.2); "" when he fights up close.</summary>
        public string Ammo = "";
        public bool Armed => Weapon.Length > 0;
        /// <summary>A bow and ammunition for it: he shoots.</summary>
        public bool Ranged => Weapon.Length > 0 && Ammo.Length > 0;
    }

    /// <summary>
    /// The guard's gear from what the chest holds (1.1): the strongest melee weapon (not a bow, a tool or a torch), and the
    /// best shield with a one-handed weapon. Part 1 has no era limit: the player decides by what he gives.
    /// </summary>
    public static class WeaponChoice
    {
        public static GuardGear Best(IReadOnlyList<WeaponOption> options) => Best(options, GuardRole.Warrior);

        /// <summary>
        /// An archer (1.2): the strongest bow for which there is ammunition of its kind (its damage and the best ammunition's
        /// together), and that ammunition; without, as a warrior. A warrior: the strongest melee weapon, a shield with a
        /// one-handed one.
        /// </summary>
        public static GuardGear Best(IReadOnlyList<WeaponOption> options, GuardRole role)
        {
            if (role == GuardRole.Archer && options != null)
            {
                WeaponOption? bow = null, ammo = null;
                float bestTotal = 0f;
                foreach (WeaponOption b in options.Where(o => o.Kind == WeaponKind.Bow && o.AmmoType.Length > 0))
                {
                    WeaponOption? a = Strongest(options.Where(o => o.Kind == WeaponKind.Ammo && o.AmmoType == b.AmmoType));
                    if (a == null) continue;
                    float total = b.Damage + a.Value.Damage;
                    if (bow == null || total > bestTotal || (total == bestTotal && string.CompareOrdinal(b.Prefab, bow.Value.Prefab) < 0))
                    {
                        bow = b;
                        ammo = a;
                        bestTotal = total;
                    }
                }
                if (bow != null) return new GuardGear { Weapon = bow.Value.Prefab, Ammo = ammo.Value.Prefab };
            }
            return Melee(options);
        }

        private static WeaponOption? Strongest(IEnumerable<WeaponOption> options)
        {
            WeaponOption? best = null;
            foreach (WeaponOption o in options)
                if (best == null || o.Damage > best.Value.Damage || (o.Damage == best.Value.Damage && string.CompareOrdinal(o.Prefab, best.Value.Prefab) < 0))
                    best = o;
            return best;
        }

        private static GuardGear Melee(IReadOnlyList<WeaponOption> options)
        {
            var gear = new GuardGear();
            if (options == null) return gear;
            WeaponOption? weapon = null;
            foreach (WeaponOption o in options.Where(o => o.Kind == WeaponKind.OneHanded || o.Kind == WeaponKind.TwoHanded))
                if (weapon == null || o.Damage > weapon.Value.Damage || (o.Damage == weapon.Value.Damage && string.CompareOrdinal(o.Prefab, weapon.Value.Prefab) < 0))
                    weapon = o;
            if (weapon == null) return gear;
            gear.Weapon = weapon.Value.Prefab;
            if (weapon.Value.Kind != WeaponKind.OneHanded) return gear;
            WeaponOption? shield = null;
            foreach (WeaponOption o in options.Where(o => o.Kind == WeaponKind.Shield))
                if (shield == null || o.Block > shield.Value.Block || (o.Block == shield.Value.Block && string.CompareOrdinal(o.Prefab, shield.Value.Prefab) < 0))
                    shield = o;
            if (shield != null) gear.Shield = shield.Value.Prefab;
            return gear;
        }
    }
}
