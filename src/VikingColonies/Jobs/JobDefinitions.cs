using System.Collections.Generic;
using VikingColonies.Core;
using System;
using System.Linq;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Construction;

namespace VikingColonies.Jobs
{
    public sealed class JobDefinition
    {
        public JobDefinition(string id, string titleToken, string buildingTypeId, Func<Citizen, CitizenAI, ColonyBuilding, Job> createBrain)
        {
            Id = id;
            TitleToken = titleToken;
            BuildingTypeId = buildingTypeId;
            CreateBrain = createBrain;
        }

        public string Id { get; }
        public string TitleToken { get; }
        public string BuildingTypeId { get; }
        public Func<Citizen, CitizenAI, ColonyBuilding, Job> CreateBrain { get; }
    }

    /// <summary>Every job. Adding a job = one entry here + its brain + a building type that employs it.</summary>
    public static class JobDefinitions
    {
        public const string LumberjackId = SaveKeys.LumberjackJob;
        public const string BuilderId = SaveKeys.BuilderJob;
        public const string ForagerId = SaveKeys.ForagerJob;
        public const string FarmerId = SaveKeys.FarmerJob;
        public const string GuardId = SaveKeys.GuardJob;
        public const string CourierId = SaveKeys.CourierJob;

        public static readonly JobDefinition Lumberjack = new JobDefinition(LumberjackId, "$vc_lumberjack",
            BuildingTypes.LumberjackHutId, (citizen, ai, hut) => new LumberjackJob(citizen, ai, hut));

        public static readonly JobDefinition Builder = new JobDefinition(BuilderId, "$vc_builder",
            BuildingTypes.BuilderHutId, (citizen, ai, hut) => new BuilderJob(citizen, ai, hut));

        public static readonly JobDefinition Forager = new JobDefinition(ForagerId, "$vc_forager",
            BuildingTypes.ForagerHutId, (citizen, ai, hut) => new ForagerJob(citizen, ai, hut));

        public static readonly JobDefinition Farmer = new JobDefinition(FarmerId, "$vc_farmer",
            BuildingTypes.FarmId, (citizen, ai, hut) => new FarmerJob(citizen, ai, hut));

        /// <summary>The Watchtower's guard (1.1).</summary>
        public static readonly JobDefinition Guard = new JobDefinition(GuardId, "$vc_guard",
            BuildingTypes.WatchtowerId, (citizen, ai, hut) => new GuardJob(citizen, ai, hut));

        /// <summary>The storehouse's courier (1.5).</summary>
        public static readonly JobDefinition Courier = new JobDefinition(CourierId, "$vc_courier",
            BuildingTypes.StorehouseId, (citizen, ai, hut) => new CourierJob(citizen, ai, hut));

        private static readonly JobDefinition[] s_jobs = { Lumberjack, Builder, Forager, Farmer, Guard, Courier };

        public static JobDefinition Get(string id) => string.IsNullOrEmpty(id) ? null : s_jobs.FirstOrDefault(j => j.Id == id);

        /// <summary>Every job's id (a settler's calling is one of them, 0.16).</summary>
        public static IReadOnlyList<string> Ids => s_jobs.Select(j => j.Id).ToList();

        public static string TitleOf(string jobId)
        {
            JobDefinition job = Get(jobId);
            return job == null ? "" : Localization.instance.Localize(job.TitleToken);
        }
    }
}
