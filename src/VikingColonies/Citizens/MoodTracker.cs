using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Citizens
{
    /// <summary>
    /// Works out a settler's mood (0.17) on the computer that runs him: at once after a load or a change of computer, then
    /// once a minute. It reads his record, the colony's Saga and the comfort at his bed as the game counts it for a player's
    /// Rested, and saves the mood and its reasons when they change, so every computer shows the same.
    /// </summary>
    internal sealed class MoodTracker
    {
        public const float EverySeconds = 60f;
        private float _nextAt;

        /// <summary>
        /// Tests: nobody's mood is worked out by itself meanwhile (a test sets it, or works it out when it wants). In memory
        /// only, so a run cut short changes no setting of the player's.
        /// </summary>
        internal static bool HoldForTest;

        /// <summary>
        /// A load, or this computer took him over: worked out a few seconds from now (his bed and his "came fed" are in place
        /// by then), at his own moment within the minute (review I1, M4).
        /// </summary>
        public void Restart(string id) => _nextAt = Time.time + MoodClock.FirstDelay(id);

        public void Tick(Citizen citizen)
        {
            if (HoldForTest || Time.time < _nextAt) return;
            _nextAt = Time.time + EverySeconds;
            WorkOut(citizen);
        }

        public static void WorkOut(Citizen citizen)
        {
            CitizenRecord r = citizen != null ? citizen.Record : null;
            if (r == null || !r.HasColony || !ColonyConfig.Mood.Value || EnvMan.instance == null || ZNet.instance == null) return;
            int today = EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds());
            citizen.NoteJobless(today);   // one jobless from before 0.17 counts from now
            r = citizen.Record;
            ColonyBuilding hall = ColonyBuilding.FindTownHall(r.ColonyId);
            TownHallModule module = hall != null ? hall.GetComponent<TownHallModule>() : null;
            SagaLog saga = module != null ? module.Saga : null;   // not loaded here: no news either way
            bool eat = Colony.Creative.SettlersEat(r.ColonyId);
            var inputs = new MoodInputs
            {
                HasHome = r.HasHome,
                Homeless = !r.HasHome,
                BedComfort = r.HasHome ? KnownComfort(BedComfort(r.HomePos), r.WhyMood) : MoodInputs.Unknown,
                AteToday = Meals.Fed(r.LastMealDay, r.Hungry, today, eat),   // fed through the night till breakfast; meals off: fed
                Hungry = eat && r.Hungry,
                DistinctFoods = eat ? MealMemory.Distinct(r.LastMeals) : 0,
                SameFoodRunning = eat && MealMemory.SameRunning(r.LastMeals),
                EveningByFire = MoodDays.Within(r.EveningDay, today, 2),
                EveningCompany = r.EveningCompany,
                InCalling = r.IsEmployed && Callings.Matches(r.Calling, r.Job),
                JoblessDays = r.IsEmployed ? 0 : MoodDays.Since(r.JoblessSince, today),
                Death = MoodSaga.RecentDeath(saga, today),
                HitYesterday = r.Job != SaveKeys.GuardJob && MoodDays.Within(r.HitDay, today, 2),   // a guard's blows are his work (1.2)
                RaidRepelled = MoodSaga.Repelled(saga, today),
                Guarded = module != null && ColonyConfig.GuardsEnabled.Value && GuardDuty.Guarding(module.Roster.Entries).Count > 0,   // 1.1; only one on duty (1.2)
                Trait = citizen.TraitKind,
            };
            MoodResult mood = Mood.Compute(inputs);
            citizen.SetMood(mood.Value, MoodLevels.Encode(mood.Reasons));
        }

        /// <summary>The comfort read now, or, with the bed's area not loaded here, the one his last reasons imply (0.19).</summary>
        private static int KnownComfort(int now, string lastWhy) => now != MoodInputs.Unknown ? now : MoodLevels.LastComfort(lastWhy);

        /// <summary>
        /// The comfort at his bed: the pieces near it that a player resting there would count, under a roof as the game
        /// judges a player's shelter; <see cref="MoodInputs.Unknown"/> when the bed's area isn't loaded here.
        /// </summary>
        internal static int BedComfort(float[] homePos)
        {
            if (homePos == null || homePos.Length != 3 || ZNetScene.instance == null) return MoodInputs.Unknown;
            Vector3 bed = Citizen.ToVector3(homePos);
            if (!ZNetScene.instance.IsAreaReady(bed)) return MoodInputs.Unknown;
            Cover.GetCoverForPoint(bed + Vector3.up, out float cover, out bool underRoof);
            return SE_Rested.CalculateComfortLevel(cover >= 0.8f && underRoof, bed);
        }
    }
}
