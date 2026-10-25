using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Settlers stand up to weak animals (1.5.0; the design of 2026-10-21, the player's answers 2026-10-23).</summary>
    public class StandUpTests
    {
        private static StandUpChoice Decide(bool weak = true, bool goesForMe = true, float distance = 5f, bool hitFromAfar = false,
            float health01 = 1f, bool standing = false, Trait trait = Trait.Hardworking, bool raidOrAlarm = false, bool setting = true) =>
            StandUp.Decide(weak, goesForMe, distance, hitFromAfar, health01, standing, trait, raidOrAlarm, setting);

        [Fact]
        public void A_Greydwarf_is_weak_from_level_2_and_a_Skeleton_from_3()
        {
            Assert.False(WeakRules.IsWeakFor("Greydwarf", 1, 1));
            Assert.True(WeakRules.IsWeakFor("Greydwarf", 1, 2));
            Assert.False(WeakRules.IsWeakFor("Skeleton", 1, 2));
            Assert.True(WeakRules.IsWeakFor("Skeleton", 1, 3));
            Assert.True(WeakRules.IsWeakFor("Skeleton_NoArcher", 1, 5));
            Assert.False(WeakRules.IsWeakFor("Troll", 1, 5));
            Assert.False(WeakRules.IsWeakFor("Neck", 3, 5));   // two stars: never weak
            Assert.Equal(FleeTracker.IsWeak("Neck", 1), WeakRules.IsWeakFor("Neck", 1, 1));
            Assert.Equal(FleeTracker.IsWeak("Boar", 2), WeakRules.IsWeakFor("Boar", 2, 0));   // a level below 1 counts as 1
        }

        [Fact]
        public void Not_weak_a_raid_the_alarm_or_the_setting_off_flee_as_today()
        {
            Assert.Equal(StandUpChoice.Flee, Decide(weak: false));
            Assert.Equal(StandUpChoice.Flee, Decide(raidOrAlarm: true));
            Assert.Equal(StandUpChoice.Flee, Decide(setting: false));
        }

        [Fact]
        public void Weak_going_for_me_within_8_m_is_fought_beyond_it_carry_on()
        {
            Assert.Equal(StandUpChoice.Fight, Decide(distance: 5f));
            Assert.Equal(StandUpChoice.Fight, Decide(distance: StandUp.GoToFight));
            Assert.Equal(StandUpChoice.CarryOn, Decide(distance: 9f));
        }

        [Fact]
        public void Hit_from_afar_or_at_half_health_flees()
        {
            Assert.Equal(StandUpChoice.Flee, Decide(hitFromAfar: true));
            Assert.Equal(StandUpChoice.Flee, Decide(health01: 0.5f));
            Assert.Equal(StandUpChoice.Fight, Decide(health01: 0.51f));
        }

        [Fact]
        public void A_timid_settler_flees_unless_standing_their_ground()
        {
            Assert.Equal(StandUpChoice.Flee, Decide(trait: Trait.Timid));
            Assert.Equal(StandUpChoice.Fight, Decide(trait: Trait.Timid, standing: true));
            Assert.Equal(StandUpChoice.Flee, Decide(trait: Trait.Timid, standing: true, health01: 0.4f));
        }

        [Fact]
        public void A_brave_settler_fights_one_after_someone_within_3_m()
        {
            Assert.Equal(StandUpChoice.Fight, Decide(trait: Trait.Brave, goesForMe: false, distance: 2.5f));
            Assert.Equal(StandUpChoice.Flee, Decide(trait: Trait.Brave, goesForMe: false, distance: 4f));
            Assert.Equal(StandUpChoice.Flee, Decide(trait: Trait.Hardworking, goesForMe: false, distance: 2.5f));
            Assert.Equal(StandUpChoice.CarryOn, Decide(trait: Trait.Hardworking, goesForMe: false, distance: 2.5f, standing: true));
        }

        [Fact]
        public void Three_flees_from_a_kind_in_10_minutes_stand_their_ground_for_10()
        {
            var ground = new StandGround();
            ground.NoteFlee("Neck", 0);
            ground.NoteFlee("Neck", 100);
            Assert.False(ground.Standing("Neck", 150));
            ground.NoteFlee("Neck", 200);
            Assert.True(ground.Standing("Neck", 201));
            Assert.True(ground.Standing("Neck", 799));
            Assert.False(ground.Standing("Neck", 801));
            Assert.False(ground.Standing("Greyling", 201));

            var spread = new StandGround();
            spread.NoteFlee("Greyling", 0);
            spread.NoteFlee("Greyling", 350);
            spread.NoteFlee("Greyling", 700);   // the first is more than 10 minutes back: two in the window
            Assert.False(spread.Standing("Greyling", 701));
        }

        [Fact]
        public void Weapons_and_torches_are_held_anything_else_is_fists()
        {
            Assert.Equal(FightHand.Held, FightRules.Hand("OneHandedWeapon"));
            Assert.Equal(FightHand.Held, FightRules.Hand("TwoHandedWeapon"));
            Assert.Equal(FightHand.Held, FightRules.Hand("TwoHandedWeaponLeft"));
            Assert.Equal(FightHand.Held, FightRules.Hand("Torch"));
            Assert.Equal(FightHand.Fists, FightRules.Hand("Tool"));
            Assert.Equal(FightHand.Fists, FightRules.Hand("Bow"));
            Assert.Equal(FightHand.Fists, FightRules.Hand("Shield"));
            Assert.Equal(FightHand.Fists, FightRules.Hand(""));
            Assert.Equal(FightHand.Fists, FightRules.Hand(null));
        }

        [Fact]
        public void A_settler_heals_2_percent_every_5_s_once_untouched_for_20_s()
        {
            Assert.Equal(0.5f, Healing.Step(0.5f, 19f, false), 3);
            Assert.Equal(0.52f, Healing.Step(0.5f, 20f, false), 3);
            Assert.Equal(1f, Healing.Step(0.99f, 60f, false), 3);
            Assert.Equal(0f, Healing.Step(0f, 60f, false), 3);   // the dead don't heal
        }

        [Fact]
        public void In_bed_they_heal_fast()
        {
            Assert.Equal(0.5f, Healing.Step(0.3f, 1f, true), 3);
            Assert.Equal(1f, Healing.Step(0.95f, 1f, true), 3);
        }

        [Fact]
        public void A_guard_hunts_a_weak_monster_about_in_the_colony_beyond_the_tower()
        {
            Assert.True(GuardRules.HuntsInColony(weakForColony: true, isAnimal: false, insideColonyArea: true, alertedOrHunting: true, fromTower: 50f, reach: 25f));
            Assert.False(GuardRules.HuntsInColony(true, false, insideColonyArea: false, alertedOrHunting: true, fromTower: 50f, reach: 25f));
            Assert.False(GuardRules.HuntsInColony(true, false, true, alertedOrHunting: false, fromTower: 50f, reach: 25f));
            Assert.False(GuardRules.HuntsInColony(true, isAnimal: true, insideColonyArea: true, alertedOrHunting: true, fromTower: 10f, reach: 25f));   // a calm Boar
            Assert.False(GuardRules.HuntsInColony(true, false, true, true, fromTower: 56f, reach: 25f));
            Assert.False(GuardRules.HuntsInColony(weakForColony: false, isAnimal: false, insideColonyArea: true, alertedOrHunting: true, fromTower: 10f, reach: 25f));
        }

        // Its review's fix pass: the fight in the game.
        [Fact]
        public void A_fight_keeps_its_target_while_it_still_goes_for_them_close_by()
        {
            Assert.True(FightRules.KeepTarget(alive: true, goesForMe: true, distance: 5f));
            Assert.False(FightRules.KeepTarget(alive: false, goesForMe: true, distance: 5f));
            Assert.False(FightRules.KeepTarget(alive: true, goesForMe: false, distance: 5f));
            Assert.False(FightRules.KeepTarget(alive: true, goesForMe: true, distance: 9f));
        }

        [Fact]
        public void Bites_from_before_a_fight_ended_dont_start_a_run()
        {
            Assert.False(FightRules.HitCounts(hitTime: 10, fightEndedAt: 12));
            Assert.True(FightRules.HitCounts(hitTime: 13, fightEndedAt: 12));
            Assert.True(FightRules.HitCounts(hitTime: 13, fightEndedAt: double.NegativeInfinity));
        }

        [Fact]
        public void A_creature_swimming_flying_or_high_up_is_out_of_reach()
        {
            Assert.True(FightRules.Reachable(swimming: false, flying: false, aboveGround: 1f));
            Assert.False(FightRules.Reachable(swimming: true, flying: false, aboveGround: 0f));
            Assert.False(FightRules.Reachable(swimming: false, flying: true, aboveGround: 0f));
            Assert.False(FightRules.Reachable(swimming: false, flying: false, aboveGround: 3f));
        }

        [Fact]
        public void A_fight_ends_when_it_draws_them_too_far_from_where_it_began()
        {
            Assert.False(FightRules.TooFarFromStart(fromStart: 9f, reach: 2f));
            Assert.True(FightRules.TooFarFromStart(fromStart: 11f, reach: 2f));
        }

        [Fact]
        public void A_pickaxe_is_put_away_for_fists_it_would_dig_the_ground()
        {
            Assert.Equal(FightHand.Fists, FightRules.Hand("TwoHandedWeapon", "Pickaxes"));
            Assert.Equal(FightHand.Held, FightRules.Hand("OneHandedWeapon", "Axes"));
            Assert.Equal(FightHand.Held, FightRules.Hand("TwoHandedWeapon", ""));
        }

        [Fact]
        public void A_fight_with_no_blow_landing_gives_up()
        {
            Assert.True(FightRules.GivesUp(sinceBlowLanded: 16f, distanceAtStart: 0f));
            Assert.False(FightRules.GivesUp(sinceBlowLanded: 14f, distanceAtStart: 0f));
            Assert.False(FightRules.GivesUp(sinceBlowLanded: 24f, distanceAtStart: 40f));   // the walk there counts: 15 + 40/4
            Assert.True(FightRules.GivesUp(sinceBlowLanded: 25f, distanceAtStart: 40f));
        }
    }
}
