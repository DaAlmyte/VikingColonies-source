using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>Section and key names in the config file. BepInEx refuses names containing = \n \t \ " ' [ ].</summary>
    public static class ConfigNames
    {
        public const string TownHallSection = "Town Hall";
        public const string ColonySection = "Colony";
        public const string LumberjackHutSection = "Lumberjack Hut";
        public const string LumberjackSection = "Lumberjack";
        public const string BuilderHutSection = "Builder Hut";
        public const string LonghouseSection = "Longhouse";
        public const string ForagerHutSection = "Forager Hut";
        public const string ForagerSection = "Forager";
        public const string FarmSection = "Farm";
        public const string FarmerSection = "Farmer";
        public const string FieldSection = "Field";
        public const string BuilderSection = "Builder";
        public const string PlacingSection = "Placing";
        public const string DeveloperSection = "Developer";
        public const string CreativeSection = "Creative";
        public const string StorageSection = "Storage";
        public const string MessagesSection = "Messages";
        public const string WatchtowerSection = "Watchtower";
        public const string StorehouseSection = "Storehouse";   // 1.5
        public const string CourierSection = "Courier";         // 1.5
        public const string Carry = "Carry";
        public const string CarryPerLevel = "CarryPerLevel";
        public const string GuardsSection = "Guards";

        public const string WoodCost = "WoodCost";
        public const string StoneCost = "StoneCost";
        public const string StartingCitizens = "StartingCitizens";
        public const string ColonyRadius = "ColonyRadius";
        public const string WorkRadius = "WorkRadius";
        public const string CarryCapacity = "CarryCapacity";
        public const string Replant = "Replant";
        public const string ReplantNeedsCultivator = "ReplantNeedsCultivator";
        public const string LearningRate = "LearningRate";
        public const string Reach = "Reach";
        public const string SecondsPerPiece = "SecondsPerPiece";
        public const string RaiseKey = "RaiseKey";
        public const string LowerKey = "LowerKey";
        public const string CreativeMode = "CreativeMode";
        public const string CreativeLevelUpKey = "LevelUpKey";
        public const string CreativeLevelDownKey = "LevelDownKey";
        public const string CommandFile = "CommandFile";
        public const string RadiusPerLevel = "RadiusPerLevel";
        public const string RowsPerLevel = "RowsPerLevel";
        public const string SkipIntro = "SkipIntro";
        public const string AutoStartCharacter = "AutoStartCharacter";
        public const string AutoStartWorld = "AutoStartWorld";
        public const string SettlersSafeFromTrees = "SettlersSafeFromTrees";
        public const string DeathMessage = "DeathMessage";
        public const string SettlersGoIndoorsAtNight = "SettlersGoIndoorsAtNight";
        public const string SettlersShelterInRaids = "SettlersShelterInRaids";
        public const string SettlersFightBack = "SettlersFightBack";   // 1.5.0
        public const string TownHallGraceDays = "TownHallGraceDays";
        public const string SettlersEat = "SettlersEat";
        public const string HungryWorkSpeed = "HungryWorkSpeed";
        public const string NewcomersPerDay = "NewcomersPerDay";
        public const string NewcomerFood = "NewcomerFood";
        public const string MaxSettlers = "MaxSettlers";
        public const string Levels = "Levels";
        public const string SettlersAtLevel1 = "SettlersAtLevel1";
        public const string SettlersPerLevel = "SettlersPerLevel";
        public const string Level2Cost = "Level2Cost";
        public const string Level3Cost = "Level3Cost";
        public const string Level4Cost = "Level4Cost";
        public const string Level5Cost = "Level5Cost";
        public const string BeatenBossesCount = "BeatenBossesCount";
        public const string VerboseLog = "VerboseLog";
        public const string HuginTips = "HuginTips";
        public const string AskForFeedback = "AskForFeedback";
        public const string WordsAboveHeads = "WordsAboveHeads";
        public const string RebuildMissingPieces = "RebuildMissingPieces";
        public const string FreeTime = "FreeTime";
        public const string SmallTalk = "SmallTalk";
        public const string EveningGathering = "EveningGathering";
        public const string SleepInBeds = "SleepInBeds";
        public const string TendFires = "TendFires";
        public const string CarryToStock = "CarryToStock";
        public const string LeaveTreesNearBuildings = "LeaveTreesNearBuildings";
        public const string NewcomersWalkIn = "NewcomersWalkIn";
        public const string CallingBonus = "CallingBonus";
        public const string Mood = "Mood";
        public const string MaxReach = "MaxReach";
        public const string Enabled = "Enabled";
        public const string ChaseBeyondPatrol = "ChaseBeyondPatrol";
        public const string RetreatHealth = "RetreatHealth";
        public const string HelpRadius = "HelpRadius";
        public const string LoadBallistas = "LoadBallistas";
        public const string StopsSpawns = "StopsSpawns";

        public static readonly IReadOnlyList<string> All = new[]
        {
            TownHallSection, ColonySection, LumberjackHutSection, LumberjackSection, BuilderHutSection, BuilderSection,
            WoodCost, StoneCost, StartingCitizens, ColonyRadius, WorkRadius, CarryCapacity, Replant, ReplantNeedsCultivator, LearningRate,
            Reach, SecondsPerPiece, PlacingSection, RaiseKey, LowerKey, DeveloperSection, CommandFile,
            SkipIntro, AutoStartCharacter, AutoStartWorld, StorageSection, RadiusPerLevel, RowsPerLevel,
            MessagesSection, SettlersSafeFromTrees, DeathMessage, SettlersGoIndoorsAtNight, SettlersShelterInRaids, SettlersFightBack,
            TownHallGraceDays, LonghouseSection, SettlersEat, HungryWorkSpeed, NewcomersPerDay, NewcomerFood, MaxSettlers,
            ForagerHutSection, ForagerSection, VerboseLog, HuginTips, AskForFeedback, WordsAboveHeads, FarmSection, FarmerSection,
            RebuildMissingPieces, FreeTime, SmallTalk, EveningGathering, SleepInBeds, TendFires, CarryToStock, LeaveTreesNearBuildings, NewcomersWalkIn, CallingBonus,
            Mood, FieldSection, WatchtowerSection, GuardsSection, MaxReach, Enabled, ChaseBeyondPatrol, RetreatHealth, HelpRadius, LoadBallistas, StopsSpawns,
            Levels, SettlersAtLevel1, SettlersPerLevel, Level2Cost, Level3Cost, Level4Cost, Level5Cost, BeatenBossesCount,
            CreativeSection, CreativeMode, CreativeLevelUpKey, CreativeLevelDownKey, StorehouseSection,
            CourierSection, Carry, CarryPerLevel,
        };
    }
}
