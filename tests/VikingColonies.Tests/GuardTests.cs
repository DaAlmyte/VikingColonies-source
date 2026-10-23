using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>1.1: the colony's area — the Town Hall's circle and its watchtowers'.</summary>
    public class ColonyAreaTests
    {
        private static ColonyAreaInfo Colony(string id, float hallX, float hallZ, params AreaCircle[] towers)
        {
            var info = new ColonyAreaInfo { ColonyId = id, Hall = new AreaCircle(new GroundPoint(hallX, hallZ), 40f) };
            info.Towers.AddRange(towers);
            return info;
        }

        [Fact]
        public void A_point_by_a_tower_far_from_the_hall_is_in_the_colony()
        {
            var tower = new AreaCircle(new GroundPoint(60f, 0f), 25f);
            ColonyAreaInfo colony = Colony("a", 0f, 0f, tower);
            Assert.Equal("a", ColonyArea.ChooseColony(new GroundPoint(75f, 0f), new[] { colony }));   // 75 m from the hall, 15 m from the tower
            Assert.Null(ColonyArea.ChooseColony(new GroundPoint(90f, 0f), new[] { colony }));          // past both circles
            Assert.True(ColonyArea.Contains(new[] { colony.Hall, tower }, new GroundPoint(75f, 0f)));
        }

        [Fact]
        public void A_hall_circle_comes_before_another_colony_s_tower()
        {
            ColonyAreaInfo a = Colony("a", 0f, 0f);
            ColonyAreaInfo b = Colony("b", 100f, 0f, new AreaCircle(new GroundPoint(50f, 0f), 30f));
            Assert.Equal("a", ColonyArea.ChooseColony(new GroundPoint(30f, 0f), new[] { b, a }));     // in a's hall circle and b's tower circle
            Assert.Equal("b", ColonyArea.ChooseColony(new GroundPoint(55f, 0f), new[] { a, b }));     // only in b's tower circle
        }

        [Fact]
        public void Between_two_towers_the_nearest_wins_ties_by_id()
        {
            ColonyAreaInfo a = Colony("a", -200f, 0f, new AreaCircle(new GroundPoint(-10f, 0f), 25f));
            ColonyAreaInfo b = Colony("b", 200f, 0f, new AreaCircle(new GroundPoint(10f, 0f), 25f));
            Assert.Equal("b", ColonyArea.ChooseColony(new GroundPoint(4f, 0f), new[] { a, b }));
            Assert.Equal("a", ColonyArea.ChooseColony(new GroundPoint(0f, 0f), new[] { b, a }));       // a tie: the lower id
        }

        private static AreaCircle Some(AreaCircle? circle) => circle ?? throw new Xunit.Sdk.XunitException("no circle");

        [Fact]   // review of 1.1: upgrading a tower near the edge made the colony smaller
        public void A_tower_near_max_reach_adds_only_what_fits_and_nothing_past_it()
        {
            var hall = new GroundPoint(0f, 0f);
            Assert.Equal(45f, Some(ColonyArea.TowerCircle(hall, new GroundPoint(100f, 0f), 45f, 150f)).Radius, 3);
            Assert.Equal(40f, Some(ColonyArea.TowerCircle(hall, new GroundPoint(110f, 0f), 45f, 150f)).Radius, 3);   // clamped, not dropped
            Assert.Equal(30f, Some(ColonyArea.TowerCircle(hall, new GroundPoint(120f, 0f), 35f, 150f)).Radius, 3);   // level 3 at 120 m: still 30 m
            Assert.Null(ColonyArea.TowerCircle(hall, new GroundPoint(145f, 0f), 25f, 150f));                       // less than 10 m left: none
            Assert.Equal(new GroundPoint(110f, 0f), Some(ColonyArea.TowerCircle(hall, new GroundPoint(110f, 0f), 45f, 150f)).Centre);
        }

        [Fact]   // review of 1.1: a tower being upgraded stopped counting
        public void A_tower_stands_once_it_was_finished_whatever_its_order_is_now()
        {
            Assert.True(ColonyArea.TowerStands(null));
            Assert.False(ColonyArea.TowerStands(new BuildOrderRecord { Kind = OrderKind.Build, State = BuildState.Building }));
            Assert.True(ColonyArea.TowerStands(new BuildOrderRecord { Kind = OrderKind.Build, State = BuildState.Done }));
            Assert.True(ColonyArea.TowerStands(new BuildOrderRecord { Kind = OrderKind.Upgrade, State = BuildState.Building }));
            Assert.True(ColonyArea.TowerStands(new BuildOrderRecord { Kind = OrderKind.Repair, State = BuildState.Waiting }));
        }

        [Fact]
        public void Tower_radius_by_level()
        {
            Assert.Equal(25f, GuardLevels.Radius(1));
            Assert.Equal(45f, GuardLevels.Radius(5));
            Assert.Equal(45f, GuardLevels.Radius(9));
        }
    }

    /// <summary>1.1: whom a guard fights, how far, and when he goes back to heal.</summary>
    public class GuardRulesTests
    {
        [Fact]
        public void The_reason_a_target_is_out_of_reach_is_named()
        {
            // 1.4.1's release run: two archers gave a Greyling up at once "(no blow landed for 0 s, or out of reach)",
            // and nobody could tell which.
            Assert.Equal("it is swimming", GuardRules.WhyOutOfReach(heightAboveGround: 0f, swimming: true, flying: false));
            Assert.Equal("it is flying", GuardRules.WhyOutOfReach(0f, false, true));
            Assert.Equal("it is 3.5 m above the ground", GuardRules.WhyOutOfReach(3.5f, false, false));
            Assert.Null(GuardRules.WhyOutOfReach(1f, false, false));   // within reach
            Assert.Equal(GuardRules.Reachable(3.5f, false, false), GuardRules.WhyOutOfReach(3.5f, false, false) == null);
        }

        [Fact]
        public void An_archer_shoots_what_swims_flies_or_stands_high_up_within_a_bowshot()
        {
            // 1.4.1's release run: archers left a swimming Neck alone that their arrows could have reached (polish round 2).
            Assert.True(GuardRules.Reachable(heightAboveGround: 0f, swimming: true, flying: false, ranged: true));
            Assert.True(GuardRules.Reachable(6f, false, true, ranged: true));
            Assert.True(GuardRules.Reachable(GuardRules.MaxShootUp, false, false, ranged: true));
            Assert.False(GuardRules.Reachable(GuardRules.MaxShootUp + 1f, false, false, ranged: true));
            Assert.Null(GuardRules.WhyOutOfReach(0f, true, false, ranged: true));
            Assert.Equal("it is 16 m above the ground", GuardRules.WhyOutOfReach(16f, false, false, ranged: true));
            Assert.False(GuardRules.Reachable(0f, true, false));   // a sword still can't
        }

        [Fact]
        public void A_guard_takes_what_is_left_in_his_load_back_to_a_chest_on_his_rounds()
        {
            // The whole-mod review of 1.4.1: bolts left in his load at the alarm stayed there for good.
            Assert.True(GuardRules.TakesLoadBack(onRounds: true, carried: 30, sinceFailed: 1000));
            Assert.False(GuardRules.TakesLoadBack(onRounds: false, carried: 30, sinceFailed: 1000));   // the alarm, Hold post: later
            Assert.False(GuardRules.TakesLoadBack(onRounds: true, carried: 0, sinceFailed: 1000));
            // Every chest full: he tries again a while later, not every moment.
            Assert.False(GuardRules.TakesLoadBack(onRounds: true, carried: 30, sinceFailed: GuardRules.LoadBackRetrySeconds - 1));
            Assert.True(GuardRules.TakesLoadBack(onRounds: true, carried: 30, sinceFailed: GuardRules.LoadBackRetrySeconds));
        }

        private static TargetFacts Monster() => new TargetFacts { Hostile = true };

        [Fact]   // Review Focus
        public void Never_a_target()
        {
            Assert.True(GuardRules.IsTarget(Monster()));
            TargetFacts f = Monster(); f.IsPlayer = true; Assert.False(GuardRules.IsTarget(f));
            f = Monster(); f.IsSettler = true; Assert.False(GuardRules.IsTarget(f));
            f = Monster(); f.Tamed = true; Assert.False(GuardRules.IsTarget(f));
            f = Monster(); f.Dead = true; Assert.False(GuardRules.IsTarget(f));
            Assert.False(GuardRules.IsTarget(new TargetFacts()));                                 // neutral: not hostile
            Assert.False(GuardRules.IsTarget(new TargetFacts { PassiveAnimal = true }));          // a deer
            Assert.True(GuardRules.IsTarget(new TargetFacts { PassiveAnimal = true, AttackingColony = true }));   // a boar at a settler
            Assert.False(GuardRules.IsTarget(new TargetFacts { PassiveAnimal = true, Hostile = true }));          // a boar minding its own business
        }

        [Fact]   // Review Focus; the full run of 1.1: a deer (the game's animal brain) was taken for a monster
        public void An_animal_is_known_by_its_faction_or_its_brain_and_only_a_monster_brain_attacks()
        {
            Assert.True(GuardRules.IsAnimal("AnimalsVeg", "MonsterAI"));    // a boar
            Assert.True(GuardRules.IsAnimal("ForestMonsters", "AnimalAI"));  // whatever its faction, a deer's brain only flees
            Assert.False(GuardRules.IsAnimal("ForestMonsters", "MonsterAI"));
            Assert.True(GuardRules.IsAnimal("PlainsMonsters", "MonsterAI", passiveAggressive: true));   // a lox: only when it attacks
            Assert.True(GuardRules.Attacks("MonsterAI"));
            Assert.False(GuardRules.Attacks("AnimalAI"));   // its target is what it flees from
            Assert.False(GuardRules.Attacks(""));
        }

        [Fact]   // review of 1.1: the same monster rule as the builder's (a monster brain; not a dummy or a summon)
        public void A_monster_has_a_monster_brain_and_isnt_a_dummy_a_summon_or_a_dwarf()
        {
            Assert.True(GuardRules.IsMonster("ForestMonsters", "MonsterAI"));
            Assert.False(GuardRules.IsMonster("ForestMonsters", ""));            // no brain
            Assert.False(GuardRules.IsMonster("TrainingDummy", "MonsterAI"));
            Assert.False(GuardRules.IsMonster("PlayerSpawned", "MonsterAI"));
            Assert.False(GuardRules.IsMonster("Dverger", "MonsterAI"));
            Assert.False(GuardRules.IsMonster("Players", "MonsterAI"));
        }

        [Fact]   // review of 1.1: a guard stood on the shore "fighting" a leech, or swung at a drake overhead
        public void Only_what_he_can_reach_on_foot_is_his()
        {
            Assert.True(GuardRules.Reachable(0.5f, swimming: false, flying: false));
            Assert.False(GuardRules.Reachable(0.5f, swimming: true, flying: false));
            Assert.False(GuardRules.Reachable(0.5f, swimming: false, flying: true));
            Assert.False(GuardRules.Reachable(3f, swimming: false, flying: false));   // above his reach
            Assert.True(GuardRules.GiveUpSeconds > 0f && GuardRules.IgnoreSeconds > GuardRules.GiveUpSeconds);
        }

        [Fact]
        public void On_watch_near_the_colony_and_wider_in_a_raid()
        {
            Assert.True(GuardRules.InWatch(24f, 25f, 10f, raid: false));
            Assert.False(GuardRules.InWatch(26f, 25f, 10f, raid: false));
            Assert.True(GuardRules.InWatch(36f, 25f, 10f, raid: true));
            Assert.False(GuardRules.InWatch(10f, 25f, 31f, raid: false));   // far from anything of the colony
        }

        [Fact]   // Review Focus
        public void Leash()
        {
            Assert.False(GuardRules.LetGo(34f, 25f, 10f, raid: false));
            Assert.True(GuardRules.LetGo(36f, 25f, 10f, raid: false));
            Assert.False(GuardRules.LetGo(45f, 25f, 10f, raid: true));     // 37.5 + 10
            Assert.True(GuardRules.LetGo(48f, 25f, 10f, raid: true));
            Assert.True(GuardRules.LetGo(26f, 25f, -5f, raid: false));     // a negative setting counts as 0
        }

        [Fact]   // the 1.2 run: running full tilt he stopped 3 m past the leash
        public void Running_he_lets_go_where_he_would_stop()
        {
            Assert.False(GuardRules.LetGo(32f, 25f, 10f, raid: false, speed: 7f));   // 32 + 2.8 = 34.8
            Assert.True(GuardRules.LetGo(33f, 25f, 10f, raid: false, speed: 7f));    // 33 + 2.8 = 35.8
            Assert.False(GuardRules.LetGo(34f, 25f, 10f, raid: false, speed: 0f));
            Assert.Equal(GuardRules.LetGo(34f, 25f, 10f, raid: false), GuardRules.LetGo(34f, 25f, 10f, raid: false, speed: -3f));
        }

        [Fact]
        public void One_attacking_the_colony_first_then_the_nearest()
        {
            var c = new List<TargetCandidate>
            {
                new TargetCandidate { Distance = 5f }, new TargetCandidate { Distance = 12f, AttackingColony = true }, new TargetCandidate { Distance = 3f },
            };
            Assert.Equal(1, GuardRules.ChooseTarget(c));
            c[1] = new TargetCandidate { Distance = 12f };
            Assert.Equal(2, GuardRules.ChooseTarget(c));
            Assert.Equal(-1, GuardRules.ChooseTarget(new List<TargetCandidate>()));
        }

        [Fact]   // Review Focus
        public void Retreat()
        {
            Assert.False(GuardRules.Retreating(false, 0.31f, 0.3f));
            Assert.True(GuardRules.Retreating(false, 0.29f, 0.3f));
            Assert.True(GuardRules.Retreating(true, 0.6f, 0.3f));          // still healing
            Assert.False(GuardRules.Retreating(true, 0.8f, 0.3f));
        }
    }

    /// <summary>1.1: what a guard does next.</summary>
    public class GuardBrainTests
    {
        [Fact]
        public void In_order_of_need()
        {
            Assert.Equal(GuardAction.NeedsWeapon, GuardBrain.Decide(new GuardSituation { HasTarget = true }));
            var s = new GuardSituation { HasWeapon = true, Retreating = true, HasTarget = true };
            Assert.Equal(GuardAction.Retreat, GuardBrain.Decide(s));
            s.Retreating = false;
            Assert.Equal(GuardAction.Fight, GuardBrain.Decide(s));
            s.HasTarget = false; s.Patrolling = true;
            Assert.Equal(GuardAction.Patrol, GuardBrain.Decide(s));
            s.Patrolling = false; s.PatrolDue = true;
            Assert.Equal(GuardAction.Patrol, GuardBrain.Decide(s));
            s.PatrolDue = false;
            Assert.Equal(GuardAction.ReturnToPost, GuardBrain.Decide(s));
            s.AtPost = true;
            Assert.Equal(GuardAction.OnWatch, GuardBrain.Decide(s));
        }
    }

    /// <summary>1.1: the guard's weapon and shield from what the chest holds.</summary>
    public class WeaponChoiceTests
    {
        [Fact]
        public void The_strongest_melee_weapon_and_a_shield_with_a_one_handed_one()
        {
            GuardGear gear = WeaponChoice.Best(new[]
            {
                new WeaponOption("Club", WeaponKind.OneHanded, 12f, 0f), new WeaponOption("AxeBronze", WeaponKind.OneHanded, 40f, 0f),
                new WeaponOption("Bow", WeaponKind.Bow, 60f, 0f), new WeaponOption("PickaxeAntler", WeaponKind.Tool, 30f, 0f),
                new WeaponOption("ShieldWood", WeaponKind.Shield, 0f, 10f), new WeaponOption("ShieldBronzeBuckler", WeaponKind.Shield, 0f, 30f),
            });
            Assert.Equal("AxeBronze", gear.Weapon);
            Assert.Equal("ShieldBronzeBuckler", gear.Shield);
            Assert.True(gear.Armed);
        }

        [Fact]
        public void No_shield_with_a_two_handed_weapon_and_nothing_without_one()
        {
            GuardGear gear = WeaponChoice.Best(new[]
                { new WeaponOption("AtgeirBronze", WeaponKind.TwoHanded, 50f, 0f), new WeaponOption("ShieldWood", WeaponKind.Shield, 0f, 10f) });
            Assert.Equal("AtgeirBronze", gear.Weapon);
            Assert.Equal("", gear.Shield);
            GuardGear none = WeaponChoice.Best(new[] { new WeaponOption("ShieldWood", WeaponKind.Shield, 0f, 10f) });
            Assert.False(none.Armed);
            Assert.Equal("", none.Shield);
            Assert.False(WeaponChoice.Best(null).Armed);
        }
    }

    /// <summary>1.1: what kind of thing an item is, to a guard (the game's item and skill types, by name).</summary>
    public class WeaponKindsTests
    {
        [Theory]
        [InlineData("OneHandedWeapon", "Clubs", WeaponKind.OneHanded)]
        [InlineData("OneHandedWeapon", "Axes", WeaponKind.OneHanded)]
        [InlineData("OneHandedWeapon", "Spears", WeaponKind.OneHanded)]
        [InlineData("OneHandedWeapon", "Knives", WeaponKind.OneHanded)]
        [InlineData("OneHandedWeapon", "ElementalMagic", WeaponKind.Other)]
        [InlineData("TwoHandedWeapon", "Polearms", WeaponKind.TwoHanded)]
        [InlineData("TwoHandedWeapon", "Swords", WeaponKind.TwoHanded)]
        [InlineData("TwoHandedWeaponLeft", "Unarmed", WeaponKind.TwoHanded)]
        [InlineData("TwoHandedWeapon", "Pickaxes", WeaponKind.Tool)]
        [InlineData("TwoHandedWeapon", "Crossbows", WeaponKind.Bow)]
        [InlineData("TwoHandedWeapon", "BloodMagic", WeaponKind.Other)]
        [InlineData("TwoHandedWeapon", "Fishing", WeaponKind.Other)]
        [InlineData("Bow", "Bows", WeaponKind.Bow)]
        [InlineData("Shield", "Blocking", WeaponKind.Shield)]
        [InlineData("Tool", "Swords", WeaponKind.Tool)]
        [InlineData("Torch", "Clubs", WeaponKind.Other)]
        [InlineData("Material", "None", WeaponKind.Other)]
        public void Of(string itemType, string skill, WeaponKind kind) => Assert.Equal(kind, WeaponKinds.Of(itemType, skill));
    }

    /// <summary>1.1: the guard's duty — he stays out, heals at his tower, walks his rounds.</summary>
    public class GuardDutyTests
    {
        // The whole-mod review of 1.4.1: a guard whose brain was new at night, in the evening or in a raid (the world loaded
        // at night, his area loaded again, another computer took him over) was never armed and went to bed.
        [Fact]
        public void A_guard_with_no_brain_yet_is_on_duty_for_a_moment_so_he_can_arm()
        {
            Assert.True(GuardDuty.NeedsBrain(hasBrain: false, isGuard: true, enabled: true));
            Assert.False(GuardDuty.NeedsBrain(hasBrain: true, isGuard: true, enabled: true));    // his brain says
            Assert.False(GuardDuty.NeedsBrain(hasBrain: false, isGuard: false, enabled: true));  // not a guard
            Assert.False(GuardDuty.NeedsBrain(hasBrain: false, isGuard: true, enabled: false));  // guards off
        }

        [Fact]   // Review Focus: an armed guard on duty stays out; one without a weapon shelters like everyone
        public void On_duty_he_never_goes_in()
        {
            foreach (IndoorReason r in new[] { IndoorReason.Night, IndoorReason.Attack, IndoorReason.Raid, IndoorReason.None })
            {
                Assert.Equal(IndoorReason.None, GuardDuty.Indoors(r, onDuty: true));
                Assert.Equal(r, GuardDuty.Indoors(r, onDuty: false));
            }
        }

        [Fact]   // review of 1.1: a hurt guard stood at his post healing while a monster hit him
        public void A_hurt_guard_is_off_duty_and_shelters_or_runs_as_anyone()
        {
            Assert.True(GuardDuty.OnDuty(armed: true, enabled: true, retreating: false));
            Assert.False(GuardDuty.OnDuty(armed: true, enabled: true, retreating: true));
            Assert.False(GuardDuty.OnDuty(armed: false, enabled: true, retreating: false));
            Assert.False(GuardDuty.OnDuty(armed: true, enabled: false, retreating: false));
        }

        [Fact]
        public void He_heals_at_his_tower_in_a_minute_and_walks_a_round_every_two_to_four()
        {
            Assert.Equal(2f, GuardDuty.HealPerSecond(120f), 3);
            Assert.Equal(0f, GuardDuty.HealPerSecond(-5f), 3);
            Assert.Equal(120f, GuardDuty.PatrolDelay(0f), 3);
            Assert.Equal(240f, GuardDuty.PatrolDelay(1f), 3);
            Assert.Equal(240f, GuardDuty.PatrolDelay(7f), 3);
        }

        [Fact]
        public void A_guard_killed_by_a_monster_fell_defending_the_colony()
        {
            DeathCause killed = GuardDuty.Cause(new DeathCause(DeathCauses.KilledBy, "$enemy_greyling"));
            Assert.Equal(DeathCauses.Defending, killed.Token);
            Assert.Equal("$enemy_greyling", killed.Arg);
            Assert.Equal(DeathCauses.Attacked, GuardDuty.Cause(new DeathCause(DeathCauses.Attacked)).Token);   // no monster to name
            Assert.Equal(DeathCauses.Tree, GuardDuty.Cause(new DeathCause(DeathCauses.Tree)).Token);   // not in a fight
            Assert.Contains(DeathCauses.Defending, DeathCauses.All);
        }

        [Fact]
        public void The_guards_who_held_a_raid_off_are_named_in_one_saga_word()
        {
            Assert.Equal("Ulf, Bjorn", GuardDuty.Names(new[] { "Ulf", "", "Bjorn" }));
            Assert.Equal("", GuardDuty.Names(new string[0]));
            Assert.True(GuardDuty.Names(new[] { new string('a', 40), new string('b', 40) }).Length <= 64);
            Assert.Equal(new[] { "ArmorLeatherChest", "ArmorLeatherLegs", "CapeDeerHide" }, Wardrobe.For(SaveKeys.GuardJob));
        }
    }

    /// <summary>1.1: the guard's skill, mood and saved names.</summary>
    public class GuardRecordTests
    {
        [Fact]
        public void Fighting_round_trips_and_isnt_written_unset()
        {
            var r = new CitizenRecord { Id = "a", Name = "Ulf", Fighting = 6f, FightingXp = 0.5f };
            CitizenRecord copy = CitizenRecord.Decode(r.Encode());
            Assert.Equal(6f, copy.Fighting);
            Assert.Equal(0.5f, copy.FightingXp);
            Assert.DoesNotContain("fighting", new CitizenRecord { Id = "a", Name = "Ivar" }.Encode());
            Assert.Equal(WorkSkill.Fighting, WorkSkills.ForJob(SaveKeys.GuardJob));
            Assert.Equal(6f, copy.Level(WorkSkill.Fighting));
        }

        [Fact]
        public void A_guard_on_watch_gladdens_the_colony()
        {
            MoodResult guarded = Mood.Compute(new MoodInputs { Guarded = true });
            Assert.Contains(guarded.Reasons, r => r.Token == "$vc_mood_guarded" && r.Change == 3);
            Assert.Contains("$vc_mood_guarded", Mood.ReasonTokens);
            Assert.DoesNotContain(Mood.Compute(new MoodInputs()).Reasons, r => r.Token == "$vc_mood_guarded");
        }
    }
    /// <summary>1.2: guards per tower level and where each stands.</summary>
    public class GuardLevelsTests
    {
        [Fact]   // Review Focus
        public void Guards_by_level()
        {
            Assert.Equal(new[] { 1, 1, 2, 2, 3 }, new[] { 1, 2, 3, 4, 5 }.Select(GuardLevels.Guards).ToArray());
            Assert.Equal(1, GuardLevels.Guards(0));
            Assert.Equal(3, GuardLevels.Guards(9));
        }

        [Fact]
        public void Each_guard_stands_beside_the_post()
        {
            Assert.Equal(0f, GuardPosts.Offset(0));
            Assert.Equal(3f, GuardPosts.Offset(1));   // the 1.2 run: 2.5 m apart, two guards each near his own place stood 1.1 m apart
            Assert.Equal(-3f, GuardPosts.Offset(2));
            Assert.Equal(6f, GuardPosts.Offset(3));
        }
    }

    /// <summary>1.2: the archer's bow and ammunition; thrown things are no melee weapon.</summary>
    public class ArcherGearTests
    {
        [Theory]
        [InlineData("Ammo", "None", "", WeaponKind.Ammo)]
        [InlineData("OneHandedWeapon", "Spears", "Projectile", WeaponKind.Other)]   // a bomb or a thrown thing
        [InlineData("OneHandedWeapon", "Spears", "Horizontal", WeaponKind.OneHanded)]
        public void Kinds(string itemType, string skill, string attack, WeaponKind kind) => Assert.Equal(kind, WeaponKinds.Of(itemType, skill, attack));

        [Fact]   // Review Focus
        public void An_archer_takes_the_best_bow_with_ammunition_for_it()
        {
            var options = new[]
            {
                new WeaponOption("BowFineWood", WeaponKind.Bow, 32f, 0f, "$ammo_arrows"),
                new WeaponOption("CrossbowArbalest", WeaponKind.Bow, 80f, 0f, "$ammo_bolts"),   // no bolts: not his
                new WeaponOption("ArrowWood", WeaponKind.Ammo, 22f, 0f, "$ammo_arrows"),
                new WeaponOption("ArrowFlint", WeaponKind.Ammo, 26f, 0f, "$ammo_arrows"),
                new WeaponOption("AxeBronze", WeaponKind.OneHanded, 40f, 0f),
                new WeaponOption("ShieldWood", WeaponKind.Shield, 0f, 10f),
            };
            GuardGear archer = WeaponChoice.Best(options, GuardRole.Archer);
            Assert.True(archer.Ranged);
            Assert.Equal("BowFineWood", archer.Weapon);
            Assert.Equal("ArrowFlint", archer.Ammo);
            Assert.Equal("", archer.Shield);
            GuardGear warrior = WeaponChoice.Best(options, GuardRole.Warrior);
            Assert.False(warrior.Ranged);
            Assert.Equal("AxeBronze", warrior.Weapon);
            Assert.Equal("ShieldWood", warrior.Shield);
            Assert.Equal("", warrior.Ammo);
        }

        [Fact]   // Review Focus
        public void An_archer_without_ammunition_fights_with_the_best_melee_weapon()
        {
            GuardGear gear = WeaponChoice.Best(new[]
            {
                new WeaponOption("BowFineWood", WeaponKind.Bow, 32f, 0f, "$ammo_arrows"),
                new WeaponOption("BoltBone", WeaponKind.Ammo, 30f, 0f, "$ammo_bolts"),   // not for his bow
                new WeaponOption("Club", WeaponKind.OneHanded, 12f, 0f),
            }, GuardRole.Archer);
            Assert.False(gear.Ranged);
            Assert.Equal("Club", gear.Weapon);
            Assert.False(WeaponChoice.Best(new[] { new WeaponOption("Bow", WeaponKind.Bow, 20f, 0f, "$ammo_arrows") }, GuardRole.Archer).Armed);
        }
    }

    /// <summary>1.2: armour from the chests, worn in place of the trade's clothes in its own slot.</summary>
    public class WardrobeMergeTests
    {
        [Fact]
        public void The_best_piece_for_each_slot()
        {
            Dictionary<string, string> best = ArmourChoice.Best(new[]
            {
                new ArmourOption("HelmetLeather", "Helmet", 2f), new ArmourOption("HelmetBronze", "Helmet", 4f),
                new ArmourOption("ArmorBronzeChest", "Chest", 8f), new ArmourOption("CapeDeerHide", "Shoulder", 1f),
            });
            Assert.Equal("HelmetBronze", best["Helmet"]);
            Assert.Equal("ArmorBronzeChest", best["Chest"]);
            Assert.Equal("CapeDeerHide", best["Shoulder"]);
            Assert.False(best.ContainsKey("Legs"));
            Assert.Empty(ArmourChoice.Best(null));
        }

        [Fact]   // Review Focus
        public void Armour_replaces_the_trades_clothes_only_in_its_own_slot()
        {
            var trade = new[]
            {
                new KeyValuePair<string, string>("Chest", "ArmorLeatherChest"), new KeyValuePair<string, string>("Legs", "ArmorLeatherLegs"),
                new KeyValuePair<string, string>("Shoulder", "CapeDeerHide"),
            };
            var armour = new Dictionary<string, string> { ["Chest"] = "ArmorBronzeChest", ["Helmet"] = "HelmetBronze" };
            Assert.Equal(new[] { "ArmorLeatherLegs", "CapeDeerHide", "ArmorBronzeChest", "HelmetBronze" }, Wardrobe.Merge(trade, armour));
            Assert.Equal(new[] { "ArmorLeatherChest", "ArmorLeatherLegs", "CapeDeerHide" }, Wardrobe.Merge(trade, new Dictionary<string, string>()));
        }
    }

    /// <summary>1.2: the shield's guard drops briefly after three quick blocks (settlers have no stamina).</summary>
    public class ShieldGuardTests
    {
        [Fact]   // Review Focus
        public void Three_blocks_in_three_seconds_lower_it_a_moment()
        {
            var shield = new ShieldGuard();
            Assert.True(shield.Raised(0));
            shield.Blocked(1.0);
            shield.Blocked(2.0);
            Assert.True(shield.Raised(2.1));
            shield.Blocked(3.5);   // the third within 3 s of the first
            Assert.False(shield.Raised(3.6));
            Assert.False(shield.Raised(4.9));
            Assert.True(shield.Raised(5.1));
        }

        [Fact]
        public void Blocks_spread_out_never_lower_it()
        {
            var shield = new ShieldGuard();
            foreach (double at in new[] { 0.0, 2.0, 4.0, 6.0, 8.0 }) shield.Blocked(at);
            Assert.True(shield.Raised(8.1));
        }
    }

    /// <summary>1.2: health with Fighting; who counts as a guard on duty.</summary>
    public class GuardDutyPart2Tests
    {
        [Fact]   // review of 1.2: a raised maximum started empty (100/150), and a reload put it back at 100
        public void A_new_maximum_keeps_his_share_of_health()
        {
            Assert.Equal(150f, GuardDuty.HealthAfter(100f, 100f, 150f), 3);
            Assert.Equal(75f, GuardDuty.HealthAfter(50f, 100f, 150f), 3);
            Assert.Equal(100f, GuardDuty.HealthAfter(150f, 150f, 100f), 3);
            Assert.Equal(0f, GuardDuty.HealthAfter(10f, 0f, 150f), 3);   // no maximum to share
        }

        [Fact]
        public void Health_grows_half_again_at_100()
        {
            Assert.Equal(100f, GuardDuty.MaxHealth(100f, 0f), 3);
            Assert.Equal(125f, GuardDuty.MaxHealth(100f, 50f), 3);
            Assert.Equal(150f, GuardDuty.MaxHealth(100f, 100f), 3);
            Assert.Equal(150f, GuardDuty.MaxHealth(100f, 300f), 3);
        }

        [Fact]   // part 1's leftover: an unarmed guard isn't "a guard on watch"
        public void Only_a_guard_on_duty_counts()
        {
            Assert.True(GuardDuty.OnDutyStatus("$vc_status_on_watch"));
            Assert.True(GuardDuty.OnDutyStatus("$vc_status_fighting $enemy_greyling"));
            Assert.True(GuardDuty.OnDutyStatus("$vc_status_patrolling"));
            Assert.True(GuardDuty.OnDutyStatus("$vc_status_resting_hurt"));
            Assert.False(GuardDuty.OnDutyStatus("$vc_status_needs $vc_a_weapon"));
            Assert.False(GuardDuty.OnDutyStatus(null));
            var entries = new[]
            {
                new RosterEntry { Name = "Ulf", Job = SaveKeys.GuardJob, Workplace = "t", Status = "$vc_status_on_watch" },
                new RosterEntry { Name = "Bjorn", Job = SaveKeys.GuardJob, Workplace = "t", Status = "$vc_status_needs $vc_a_weapon" },
                new RosterEntry { Name = "Ivar", Job = SaveKeys.GuardJob, Workplace = "t", Status = "$vc_status_on_watch", MissingSinceDay = 3 },
                new RosterEntry { Name = "Astrid", Job = SaveKeys.FarmerJob, Workplace = "f", Status = "$vc_status_on_watch" },
            };
            Assert.Equal(new[] { "Ulf" }, GuardDuty.Guarding(entries));
        }

        // The review of polish round 2: an archer after a Neck swimming far out walked to the shore and stood there till
        // the give-up time, then took it again later. With the way ended short of bow range for a moment, it is let go.
        [Fact]
        public void An_archer_whose_way_ends_short_of_bow_range_lets_it_go()
        {
            Assert.True(GuardRules.OutOfBowReach(wayEnded: true, gap: 40f, shootRange: 25f, endedFor: GuardRules.WayEndedSeconds));
            Assert.False(GuardRules.OutOfBowReach(wayEnded: true, gap: 40f, shootRange: 25f, endedFor: GuardRules.WayEndedSeconds - 1f));
            Assert.False(GuardRules.OutOfBowReach(wayEnded: false, gap: 40f, shootRange: 25f, endedFor: 60f));   // still walking
            Assert.False(GuardRules.OutOfBowReach(wayEnded: true, gap: 20f, shootRange: 25f, endedFor: 60f));    // in range: shoots
        }

        // 1.4.2's release run (1 in about 10): the alarm's ring of spots, round where it was sounded (mostly inside the Town
        // Hall), took no notice of buildings: a guard's spot behind a hut's wall kept them going in and out of its door. Each
        // guard tries their own ring spots in turn, never one another guard tries, and takes the first that stands open.
        [Fact]
        public void Each_guard_tries_their_own_alarm_spots_never_another_guards()
        {
            Assert.Equal(new[] { 1, 4, 7, 10 }, GuardPosts.AlarmSpotsToTry(place: 1, guards: 3, tries: 4));
            Assert.Equal(new[] { 0, 1, 2 }, GuardPosts.AlarmSpotsToTry(place: 0, guards: 1, tries: 3));
            Assert.Equal(new[] { 0, 1 }, GuardPosts.AlarmSpotsToTry(place: 0, guards: 0, tries: 2));   // none counted yet: as one
            var taken = new System.Collections.Generic.HashSet<int>();
            for (int place = 0; place < 3; place++)
                foreach (int spot in GuardPosts.AlarmSpotsToTry(place, 3, 6))
                    Assert.True(taken.Add(spot), $"spot {spot} tried by two guards");
        }

        // Its review: a spot by the camp's fire is no place to stand (a settler burned in 1.4.0), and a paved yard at the
        // alarm's level is as good as the ground; a floor at another height (a roof above, a hut's floor) is not.
        [Fact]
        public void An_alarm_spot_is_open_on_the_ground_or_the_alarms_floor_clear_of_fires_and_water()
        {
            Assert.True(GuardPosts.AlarmSpotOpen(onGround: true, onAlarmsFloor: false, inWater: false, heightOff: 1f, roomToStand: true, clearOfFires: true));
            Assert.True(GuardPosts.AlarmSpotOpen(onGround: false, onAlarmsFloor: true, inWater: false, heightOff: 0f, roomToStand: true, clearOfFires: true));
            Assert.False(GuardPosts.AlarmSpotOpen(onGround: false, onAlarmsFloor: false, inWater: false, heightOff: 0f, roomToStand: true, clearOfFires: true));   // a roof or a rock
            Assert.False(GuardPosts.AlarmSpotOpen(onGround: true, onAlarmsFloor: false, inWater: false, heightOff: 0f, roomToStand: true, clearOfFires: false));   // the fire
            Assert.False(GuardPosts.AlarmSpotOpen(onGround: true, onAlarmsFloor: false, inWater: true, heightOff: 0f, roomToStand: true, clearOfFires: true));
            Assert.False(GuardPosts.AlarmSpotOpen(onGround: true, onAlarmsFloor: false, inWater: false, heightOff: 3.5f, roomToStand: true, clearOfFires: true));
            Assert.False(GuardPosts.AlarmSpotOpen(onGround: true, onAlarmsFloor: false, inWater: false, heightOff: 0f, roomToStand: false, clearOfFires: true));
        }

        // 1.4.3's early full run: a spot in the open but walled in (an earlier test's roofless hut beside the alarm, as a
        // fenced yard would be) kept a guard going in and out of a door 20 m off for the whole alarm. The first spot that is
        // open and one they can walk to; else the first open one (a guard inside a hut, behind its closed door, or far off on
        // patrol, has a way to none yet: its review); else none (their plain ring spot).
        [Fact]
        public void A_guard_takes_the_first_open_spot_they_can_walk_to_else_the_first_open_one()
        {
            bool[] open = { false, true, true, true }, walk = { true, false, true, true };
            Assert.Equal(2, GuardPosts.ChooseAlarmSpot(4, i => open[i], i => walk[i], out bool reached));
            Assert.True(reached);
            Assert.Equal(1, GuardPosts.ChooseAlarmSpot(4, i => open[i], i => false, out reached));
            Assert.False(reached);
            Assert.Equal(-1, GuardPosts.ChooseAlarmSpot(4, i => false, i => true, out reached));
            Assert.False(reached);
        }

        [Fact]
        public void The_way_is_looked_for_only_to_open_spots_and_only_until_one_is_found()
        {
            var asked = new List<int>();
            bool[] open = { false, true, true, true };
            GuardPosts.ChooseAlarmSpot(4, i => open[i], i => { asked.Add(i); return i == 2; }, out bool reached);
            Assert.Equal(new[] { 1, 2 }, asked);   // not the closed spot 0, and nothing after the first found
        }

        // Its review: a spot chosen with a way to it isn't dropped because a guard fighting across a stream has no way back
        // for a moment; one chosen without a way is looked at again for one.
        [Fact]
        public void A_kept_spot_is_looked_at_for_a_way_only_when_it_was_chosen_without_one()
        {
            Assert.False(GuardPosts.RecheckWay(chosenWithWay: true));
            Assert.True(GuardPosts.RecheckWay(chosenWithWay: false));
        }

        // Its review: a spot chosen before the colony's huts had loaded (a player joining mid-alarm) stood behind a wall for
        // the whole alarm. A kept spot is looked at again every few seconds, and given up once it has closed.
        [Fact]
        public void A_kept_alarm_spot_is_looked_at_again_and_given_up_once_it_closes()
        {
            Assert.True(GuardPosts.KeepAlarmSpot(sameAlarm: true, sinceChecked: 1f, stillOpen: false));   // not looked at yet: kept
            Assert.True(GuardPosts.KeepAlarmSpot(sameAlarm: true, sinceChecked: GuardPosts.AlarmRecheckSeconds, stillOpen: true));
            Assert.False(GuardPosts.KeepAlarmSpot(sameAlarm: true, sinceChecked: GuardPosts.AlarmRecheckSeconds, stillOpen: false));
            Assert.False(GuardPosts.KeepAlarmSpot(sameAlarm: false, sinceChecked: 0f, stillOpen: true));   // another alarm, place or count
        }

        [Fact]
        public void A_hut_lists_all_its_workers()
        {
            var roster = new ColonyRoster();
            roster.Seen("a", "Ulf", SaveKeys.GuardJob, "t", "", null, 1);
            roster.Seen("b", "Bjorn", SaveKeys.GuardJob, "t", "", null, 1);
            roster.Seen("c", "Astrid", SaveKeys.FarmerJob, "f", "", null, 1);
            Assert.Equal(new[] { "a", "b" }, roster.WorkersOf("t").Select(e => e.CitizenId).ToArray());
            Assert.Empty(roster.WorkersOf(""));
            Assert.Equal("vc_guard_role", SaveKeys.GuardRole);
        }
    }
}
