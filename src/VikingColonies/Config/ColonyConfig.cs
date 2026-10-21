using BepInEx.Configuration;
using Jotunn.Extensions;
using UnityEngine;
using VikingColonies.Core;

namespace VikingColonies.Config
{
    /// <summary>Settings in BepInEx/config/daalmyte.vikingcolonies.cfg. Synced from the server to every player (the keys are each player's own).</summary>
    internal static class ColonyConfig
    {
        public static ConfigEntry<bool> VerboseLog;
        public static ConfigEntry<bool> HuginTips;
        public static ConfigEntry<bool> AskForFeedback;
        public static ConfigEntry<bool> WordsAboveHeads;
        public static ConfigEntry<bool> SmallTalk;
        public static ConfigEntry<bool> FreeTime;
        public static ConfigEntry<bool> EveningGathering;
        public static ConfigEntry<bool> SleepInBeds;
        public static ConfigEntry<bool> TendFires;
        public static ConfigEntry<bool> CarryToStock;
        public static ConfigEntry<bool> NewcomersWalkIn;
        public static ConfigEntry<float> CallingBonus;
        public static ConfigEntry<bool> Mood;
        public static ConfigEntry<int> HallWoodCost;
        public static ConfigEntry<int> HallStoneCost;
        public static ConfigEntry<int> StartingCitizens;
        public static ConfigEntry<float> ColonyRadius;
        public static ConfigEntry<float> MaxReach;
        public static ConfigEntry<int> WatchtowerWoodCost;
        public static ConfigEntry<bool> GuardsEnabled;
        public static ConfigEntry<float> ChaseBeyondPatrol;
        public static ConfigEntry<float> RetreatHealth;
        public static ConfigEntry<float> HelpRadius;
        public static ConfigEntry<bool> LoadBallistas;
        public static ConfigEntry<bool> TowersStopSpawns;
        public static ConfigEntry<bool> SettlersSafeFromTrees;
        public static ConfigEntry<bool> SettlersGoIndoorsAtNight;
        public static ConfigEntry<bool> SettlersShelterInRaids;
        public static ConfigEntry<int> TownHallGraceDays;
        public static ConfigEntry<bool> SettlersEat;
        public static ConfigEntry<float> HungryWorkSpeed;
        public static ConfigEntry<int> NewcomersPerDay;
        public static ConfigEntry<int> NewcomerFood;
        public static ConfigEntry<int> MaxSettlers;
        public static ConfigEntry<bool> HallLevels;
        public static ConfigEntry<bool> HallBeatenBossesCount;
        public static ConfigEntry<float> HallRadiusPerLevel;
        public static ConfigEntry<int> SettlersAtLevel1;
        public static ConfigEntry<int> SettlersPerLevel;
        private static ConfigEntry<string>[] s_levelCosts = new ConfigEntry<string>[0];

        /// <summary>The materials raising the Town Hall to <paramref name="level"/> costs, as the setting's text; "" for none.</summary>
        public static string HallLevelCost(int level)
        {
            int i = level - 2;
            return i >= 0 && i < s_levelCosts.Length && s_levelCosts[i] != null ? s_levelCosts[i].Value ?? "" : "";
        }
        public static ConfigEntry<DeathMessageStyle> DeathMessage;
        public static ConfigEntry<int> LumberjackHutWoodCost;
        public static ConfigEntry<int> ForagerHutWoodCost;
        public static ConfigEntry<int> FarmWoodCost;
        public static ConfigEntry<int> FieldWoodCost;
        public static ConfigEntry<int> FarmerCarryCapacity;
        public static ConfigEntry<float> ForagerWorkRadius;
        public static ConfigEntry<float> ForagerRadiusPerLevel;
        public static ConfigEntry<int> ForagerCarryCapacity;
        public static ConfigEntry<float> WorkRadius;
        public static ConfigEntry<float> RadiusPerLevel;
        public static ConfigEntry<int> RowsPerLevel;
        public static ConfigEntry<int> CarryCapacity;
        public static ConfigEntry<bool> ReplantEnabled;
        public static ConfigEntry<bool> ReplantNeedsCultivator;
        public static ConfigEntry<float> LearningRate;
        public static ConfigEntry<float> LeaveTreesNearBuildings;
        public static ConfigEntry<int> BuilderHutWoodCost;
        public static ConfigEntry<int> LonghouseWoodCost;
        public static ConfigEntry<float> BuilderReach;
        public static ConfigEntry<float> SecondsPerPiece;
        public static ConfigEntry<bool> RebuildMissingPieces;
        public static ConfigEntry<int> BuilderCarryCapacity;
        public static ConfigEntry<KeyCode> RaiseHutKey;
        public static ConfigEntry<KeyCode> LowerHutKey;
        public static ConfigEntry<bool> CommandFile;
        public static ConfigEntry<bool> SkipIntro;
        public static ConfigEntry<string> AutoStartCharacter;
        public static ConfigEntry<string> AutoStartWorld;

        public static void Bind(ConfigFile config)
        {
            HallWoodCost = config.BindConfig(ConfigNames.TownHallSection, ConfigNames.WoodCost, 20,
                "Wood needed to build a Town Hall (on a server, the server's).",
                synced: true, acceptableValues: new AcceptableValueRange<int>(0, 500));
            HallStoneCost = config.BindConfig(ConfigNames.TownHallSection, ConfigNames.StoneCost, 10,
                "Stone needed to build a Town Hall (on a server, the server's).",
                synced: true, acceptableValues: new AcceptableValueRange<int>(0, 500));
            // Town Hall levels (1.4): the Hall of Trophies.
            HallLevels = config.BindConfig(ConfigNames.TownHallSection, ConfigNames.Levels, true,
                "The Town Hall has levels (1-5), each raised by showing it the trophies of the Forsaken met since the level before and " +
                "paying materials from its chest; each level widens the colony's area, makes room for more settlers and opens the next " +
                "age of huts (huts go as high as the hall). " +
                "Off: as before 1.4 (area Colony.ColonyRadius, settlers up to Colony.MaxSettlers, huts limited by the Builder's Hut only).",
                synced: true);
            HallBeatenBossesCount = config.BindConfig(ConfigNames.TownHallSection, ConfigNames.BeatenBossesCount, true,
                "A Forsaken this world has already beaten counts for the Town Hall as if its trophy were shown (a trophy hung on " +
                "the sacrificial stones can't be taken back). Off: only trophies shown at the hall count.",
                synced: true);
            HallRadiusPerLevel = config.BindConfig(ConfigNames.TownHallSection, ConfigNames.RadiusPerLevel, 10f,
                "Metres added to the colony's area (around the Town Hall) per Town Hall level above 1.",
                synced: true, acceptableValues: new AcceptableValueRange<float>(0f, 50f));
            SettlersAtLevel1 = config.BindConfig(ConfigNames.TownHallSection, ConfigNames.SettlersAtLevel1, 10,
                "How many settlers a colony with a level-1 Town Hall grows to (never past Colony.MaxSettlers).",
                synced: true, acceptableValues: new AcceptableValueRange<int>(1, 100));
            SettlersPerLevel = config.BindConfig(ConfigNames.TownHallSection, ConfigNames.SettlersPerLevel, 5,
                "More settlers per Town Hall level above 1 (never past Colony.MaxSettlers).",
                synced: true, acceptableValues: new AcceptableValueRange<int>(0, 50));
            string[] costNames = { ConfigNames.Level2Cost, ConfigNames.Level3Cost, ConfigNames.Level4Cost, ConfigNames.Level5Cost };
            s_levelCosts = new ConfigEntry<string>[costNames.Length];
            for (int i = 0; i < costNames.Length; i++)
                s_levelCosts[i] = config.BindConfig(ConfigNames.TownHallSection, costNames[i], TownHallLevels.DefaultCost(i + 2),
                    $"Materials raising the Town Hall to level {i + 2} uses up from its chest, as Prefab:count by commas (the items' " +
                    "prefab names, as the game's spawn command takes them). Empty: only the trophies.",
                    synced: true);
            StartingCitizens = config.BindConfig(ConfigNames.ColonySection, ConfigNames.StartingCitizens, 4,
                "Citizens who arrive when a Town Hall is built.",
                synced: true, acceptableValues: new AcceptableValueRange<int>(0, 10));
            ColonyRadius = config.BindConfig(ConfigNames.ColonySection, ConfigNames.ColonyRadius, 50f,
                "How far from a level-1 Town Hall (metres) colony buildings can be built. Each Town Hall level above 1 adds " +
                "Town Hall › RadiusPerLevel, and each Watchtower adds a circle of its own (within Colony › MaxReach).",
                synced: true, acceptableValues: new AcceptableValueRange<float>(20f, 150f));
            MaxReach = config.BindConfig(ConfigNames.ColonySection, ConfigNames.MaxReach, 150f,
                "How far from the Town Hall (metres) the colony may reach with its Watchtowers: of a tower near it only the part of " +
                "its area within reach counts (its guard still keeps watch).",
                synced: true, acceptableValues: new AcceptableValueRange<float>(50f, 500f));
            WatchtowerWoodCost = config.BindConfig(ConfigNames.WatchtowerSection, ConfigNames.WoodCost, 20,
                "Wood needed to place a Watchtower (on a server, the server's); the builder then puts up its building.",
                synced: true, acceptableValues: new AcceptableValueRange<int>(0, 500));
            GuardsEnabled = config.BindConfig(ConfigNames.GuardsSection, ConfigNames.Enabled, true,
                "Watchtower guards fight monsters near the colony. Off: a guard shelters like any settler.",
                synced: true);
            ChaseBeyondPatrol = config.BindConfig(ConfigNames.GuardsSection, ConfigNames.ChaseBeyondPatrol, 10f,
                "How far (metres) past their patrol area a guard follows a monster before they let it go and walk back.",
                synced: true, acceptableValues: new AcceptableValueRange<float>(0f, 50f));
            RetreatHealth = config.BindConfig(ConfigNames.GuardsSection, ConfigNames.RetreatHealth, 0.3f,
                "Below this share of their health a guard goes back to their tower to heal (they come out again at 80%).",
                synced: true, acceptableValues: new AcceptableValueRange<float>(0f, 0.75f));
            HelpRadius = config.BindConfig(ConfigNames.GuardsSection, ConfigNames.HelpRadius, 80f,
                "How far from their tower (metres) a guard comes when a monster attacks someone inside the colony's area (at least twice their patrol).",
                synced: true, acceptableValues: new AcceptableValueRange<float>(0f, 300f));
            LoadBallistas = config.BindConfig(ConfigNames.GuardsSection, ConfigNames.LoadBallistas, true,
                "Guards on watch keep the colony's ballistas within their patrol loaded from what the colony's chests can spare.",
                synced: true);
            TowersStopSpawns = config.BindConfig(ConfigNames.WatchtowerSection, ConfigNames.StopsSpawns, true,
                "A finished Watchtower keeps monsters from springing up within its circle, as a workbench or a fire does.",
                synced: true);
            SettlersSafeFromTrees = config.BindConfig(ConfigNames.ColonySection, ConfigNames.SettlersSafeFromTrees, true,
                "Settlers aren't hurt by falling trees and rolling logs (players still are).",
                synced: true);
            SettlersGoIndoorsAtNight = config.BindConfig(ConfigNames.ColonySection, ConfigNames.SettlersGoIndoorsAtNight, true,
                "Settlers stop work at night and sleep inside their huts (off: they rest outdoors by their hut and the builder works on).",
                synced: true);
            SettlersShelterInRaids = config.BindConfig(ConfigNames.ColonySection, ConfigNames.SettlersShelterInRaids, true,
                "During a Valheim raid at the colony, settlers shelter inside their huts.",
                synced: true);
            TownHallGraceDays = config.BindConfig(ConfigNames.ColonySection, ConfigNames.TownHallGraceDays, 3,
                "In-game days a colony lives on after losing its Town Hall: a new Town Hall built among its huts in that time takes " +
                "it over. 0 = the colony ends 30 seconds after its Town Hall is gone.",
                synced: true, acceptableValues: new AcceptableValueRange<int>(0, ColonyGrace.MaxDays));
            SettlersEat = config.BindConfig(ConfigNames.ColonySection, ConfigNames.SettlersEat, true,
                "Settlers eat once a day from the colony's chests (the plainest food first); one who finds nothing is hungry and works slower.",
                synced: true);
            HungryWorkSpeed = config.BindConfig(ConfigNames.ColonySection, ConfigNames.HungryWorkSpeed, 0.5f,
                "How fast a hungry settler works (1 = as fast as a fed one).",
                synced: true, acceptableValues: new AcceptableValueRange<float>(0.1f, 1f));
            NewcomersPerDay = config.BindConfig(ConfigNames.ColonySection, ConfigNames.NewcomersPerDay, 1,
                "Newcomers who may arrive at the Town Hall each day, while the colony has a free bed and food (0 = none).",
                synced: true, acceptableValues: new AcceptableValueRange<int>(0, 5));
            NewcomerFood = config.BindConfig(ConfigNames.ColonySection, ConfigNames.NewcomerFood, 5,
                "Food items in the Town Hall's chest a newcomer needs, and eats when they arrive.",
                synced: true, acceptableValues: new AcceptableValueRange<int>(0, 50));
            FreeTime = config.BindConfig(ConfigNames.ColonySection, ConfigNames.FreeTime, true,
                "Settlers with nothing to do sit on benches and chairs, sit by burning fires, chat and stroll near their hut or the " +
                "Town Hall (off: they wait standing, as before 0.14).",
                synced: true);
            EveningGathering = config.BindConfig(ConfigNames.ColonySection, ConfigNames.EveningGathering, true,
                "In the evening, before night, settlers stop work and gather round the colony's fire (the burning campfire or hearth " +
                "nearest the Town Hall; the Town Hall with none), then go indoors at night.",
                synced: true);
            SleepInBeds = config.BindConfig(ConfigNames.ColonySection, ConfigNames.SleepInBeds, true,
                "At night a settler with a home lies in their Longhouse bed (off: they stand by it, as before 0.15).",
                synced: true);
            TendFires = config.BindConfig(ConfigNames.ColonySection, ConfigNames.TendFires, true,
                "Settlers with nothing to do keep the colony's fires fed from its chests (always leaving 10 of the fuel for you).",
                synced: true);
            CarryToStock = config.BindConfig(ConfigNames.ColonySection, ConfigNames.CarryToStock, true,
                "Settlers with nothing to do carry what a building keeps in stock and lacks from colony chests that can spare it (off: kept items are still left alone).",
                synced: true);
            NewcomersWalkIn = config.BindConfig(ConfigNames.ColonySection, ConfigNames.NewcomersWalkIn, true,
                "Newcomers, and the first settlers of a new Town Hall, walk in from the edge of the colony (off: they appear at the Town Hall).",
                synced: true);
            Mood = config.BindConfig(ConfigNames.ColonySection, ConfigNames.Mood, true,
                "Settlers have a mood from their home, meals, evenings and the colony's news: it speeds up or slows down their work, " +
                "and nobody joins an unhappy colony (off: everyone is content and newcomers come as before).",
                synced: true);
            CallingBonus = config.BindConfig(ConfigNames.ColonySection, ConfigNames.CallingBonus, 0.2f,
                "How much faster a settler works in the trade they're good at (0.2 = 20% faster; 0 = no difference).",
                synced: true, acceptableValues: new AcceptableValueRange<float>(0f, 1f));
            MaxSettlers = config.BindConfig(ConfigNames.ColonySection, ConfigNames.MaxSettlers, 30,
                "No newcomers once a colony has this many settlers.",
                synced: true, acceptableValues: new AcceptableValueRange<int>(1, 100));
            HuginTips = config.BindConfig(ConfigNames.MessagesSection, ConfigNames.HuginTips, true,
                "Hugin, the raven, explains the colony as you play: the first Town Hall, a hut without a Builder's Hut, the first " +
                "things the builder needs, the first worker, night, hunger and a death. Each tip once per character, and only with " +
                "the game's tutorials on. Each player's own.", synced: false);
            AskForFeedback = config.BindConfig(ConfigNames.MessagesSection, ConfigNames.AskForFeedback, true,
                "Once on this computer, at a good moment (a hut near you reaching level 2, or two hours with a colony), a small " +
                "window asks what you think of the mod, with links to its Discord and its GitHub page. Nothing is sent from the " +
                "game. Off: it never asks; the Feedback button in the Town Hall's window stays. On a server, the server's setting is used.",
                synced: true);
            WordsAboveHeads = config.BindConfig(ConfigNames.MessagesSection, ConfigNames.WordsAboveHeads, true,
                "A settler near you who needs you says so above their head (\"Needs 12 Wood\", \"Storage full\", \"Hungry\"). " +
                "Each player's own.", synced: false);
            SmallTalk = config.BindConfig(ConfigNames.MessagesSection, ConfigNames.SmallTalk, true,
                "Settlers greet you by name once a day and make small talk above their heads in their free time (needs always come " +
                "first). Each player's own.", synced: false);
            DeathMessage = config.BindConfig(ConfigNames.MessagesSection, ConfigNames.DeathMessage, DeathMessageStyle.CentreAndChat,
                "How you're told a settler died: CentreAndChat (a message in the middle of the screen and a line in the chat) " +
                "or ChatOnly. Each player's own.", synced: false);
            FarmWoodCost = config.BindConfig(ConfigNames.FarmSection, ConfigNames.WoodCost, 20,
                "Wood needed to place a Farm (on a server, the server's); the builder then puts up its building.",
                synced: true, acceptableValues: new AcceptableValueRange<int>(0, 500));
            FieldWoodCost = config.BindConfig(ConfigNames.FieldSection, ConfigNames.WoodCost, 4,
                "Wood needed to place a field marker (on a server, the server's).",
                synced: true, acceptableValues: new AcceptableValueRange<int>(0, 500));
            FarmerCarryCapacity = config.BindConfig(ConfigNames.FarmerSection, ConfigNames.CarryCapacity, 20,
                "How many items the farmer carries before walking back to deliver.",
                synced: true, acceptableValues: new AcceptableValueRange<int>(1, 200));
            ForagerHutWoodCost = config.BindConfig(ConfigNames.ForagerHutSection, ConfigNames.WoodCost, 20,
                "Wood needed to place a Forager's Hut (on a server, the server's); the builder then puts up its building.",
                synced: true, acceptableValues: new AcceptableValueRange<int>(0, 500));
            ForagerWorkRadius = config.BindConfig(ConfigNames.ForagerSection, ConfigNames.WorkRadius, 40f,
                "How far from their hut (metres) the forager picks wild berries and mushrooms.",
                synced: true, acceptableValues: new AcceptableValueRange<float>(10f, 100f));
            ForagerRadiusPerLevel = config.BindConfig(ConfigNames.ForagerSection, ConfigNames.RadiusPerLevel, 10f,
                "Metres added to the forager's work area for each level of their hut above 1.",
                synced: true, acceptableValues: new AcceptableValueRange<float>(0f, 50f));
            ForagerCarryCapacity = config.BindConfig(ConfigNames.ForagerSection, ConfigNames.CarryCapacity, 20,
                "How many items the forager carries before walking back to deliver.",
                synced: true, acceptableValues: new AcceptableValueRange<int>(1, 200));
            LumberjackHutWoodCost = config.BindConfig(ConfigNames.LumberjackHutSection, ConfigNames.WoodCost, 20,
                "Wood needed to build a Lumberjack's Hut (on a server, the server's).",
                synced: true, acceptableValues: new AcceptableValueRange<int>(0, 500));
            WorkRadius = config.BindConfig(ConfigNames.LumberjackSection, ConfigNames.WorkRadius, 40f,
                "How far from its hut (metres) the lumberjack works.",
                synced: true, acceptableValues: new AcceptableValueRange<float>(10f, 100f));
            RadiusPerLevel = config.BindConfig(ConfigNames.LumberjackSection, ConfigNames.RadiusPerLevel, 10f,
                "Metres added to the lumberjack's work area for each level of their hut above 1.",
                synced: true, acceptableValues: new AcceptableValueRange<float>(0f, 50f));
            RowsPerLevel = config.BindConfig(ConfigNames.StorageSection, ConfigNames.RowsPerLevel, 1,
                "Rows added to a colony building's chest for each level above 1 (the Town Hall's too, by its level).",
                synced: true, acceptableValues: new AcceptableValueRange<int>(0, 3));
            CarryCapacity = config.BindConfig(ConfigNames.LumberjackSection, ConfigNames.CarryCapacity, 50,
                "How many items the lumberjack carries before walking back to deliver.",
                synced: true, acceptableValues: new AcceptableValueRange<int>(1, 500));
            ReplantEnabled = config.BindConfig(ConfigNames.LumberjackSection, ConfigNames.Replant, true,
                "Whether the lumberjack plants a sapling where it cut a tree.",
                synced: true);
            ReplantNeedsCultivator = config.BindConfig(ConfigNames.LumberjackSection, ConfigNames.ReplantNeedsCultivator, true,
                "Whether the lumberjack only replants while a Cultivator is in the Lumberjack's Hut's storage (like the base game's bronze age).",
                synced: true);
            LeaveTreesNearBuildings = config.BindConfig(ConfigNames.LumberjackSection, ConfigNames.LeaveTreesNearBuildings, 3f,
                "Lumberjacks leave a tree standing when its trunk is within this many metres of a piece a player built (0 = off). The builder still clears trees from a building site.",
                synced: true, acceptableValues: new AcceptableValueRange<float>(0f, 20f));
            LearningRate = config.BindConfig(ConfigNames.ColonySection, ConfigNames.LearningRate, 0.25f,
                "How fast settlers improve at their work (Woodcutting, Farming, Crafting): practice per action compared with a player's per hit (1 = as fast as a player; they work all day, so less is fairer). Before 0.22: Lumberjack.LearningRate.",
                synced: true, acceptableValues: new AcceptableValueRange<float>(0.01f, 5f));
            ConfigMoves.Move(config, ConfigNames.LumberjackSection, ConfigNames.LearningRate, LearningRate);
            BuilderHutWoodCost = config.BindConfig(ConfigNames.BuilderHutSection, ConfigNames.WoodCost, 20,
                "Wood needed to build a Builder's Hut (on a server, the server's).",
                synced: true, acceptableValues: new AcceptableValueRange<int>(0, 500));
            LonghouseWoodCost = config.BindConfig(ConfigNames.LonghouseSection, ConfigNames.WoodCost, 20,
                "Wood needed to place a Longhouse (on a server, the server's); the builder then puts up its building.",
                synced: true, acceptableValues: new AcceptableValueRange<int>(0, 500));
            BuilderReach = config.BindConfig(ConfigNames.BuilderSection, ConfigNames.Reach, 15f,
                "How far (metres) the builder reaches from where they stand; they don't need to see the spot.",
                synced: true, acceptableValues: new AcceptableValueRange<float>(5f, 30f));
            SecondsPerPiece = config.BindConfig(ConfigNames.BuilderSection, ConfigNames.SecondsPerPiece, 2f,
                "Seconds of hammer work per building piece (walking and fetching come on top).",
                synced: true, acceptableValues: new AcceptableValueRange<float>(0.5f, 10f));
            BuilderCarryCapacity = config.BindConfig(ConfigNames.BuilderSection, ConfigNames.CarryCapacity, 50,
                "How many items the builder carries from the chests per trip.",
                synced: true, acceptableValues: new AcceptableValueRange<int>(1, 500));
            RebuildMissingPieces = config.BindConfig(ConfigNames.BuilderSection, ConfigNames.RebuildMissingPieces, true,
                "A finished colony building whose pieces are broken (a troll, a fire, a falling tree) has them put back by a builder. " +
                "Pieces you take away with your hammer are left out.",
                synced: true);
            RaiseHutKey = config.BindConfig(ConfigNames.PlacingSection, ConfigNames.RaiseKey, KeyCode.PageUp,
                "Raises a hut 25 cm while you place it (up to 2 m). Each player's own key.", synced: false);
            LowerHutKey = config.BindConfig(ConfigNames.PlacingSection, ConfigNames.LowerKey, KeyCode.PageDown,
                "Lowers a hut 25 cm while you place it (down to 2 m). Each player's own key.", synced: false);
            CommandFile = config.BindConfig(ConfigNames.DeveloperSection, ConfigNames.CommandFile, false,
                "For testing the mod: runs VikingColonies console commands written into BepInEx/VikingColonies-commands.txt " +
                "(only while devcommands is on; answers go to VikingColonies-commands-out.txt). Leave off when playing.", synced: false);
            SkipIntro = config.BindConfig(ConfigNames.DeveloperSection, ConfigNames.SkipIntro, false,
                "Skips the intro film when the game starts.", synced: false);
            AutoStartCharacter = config.BindConfig(ConfigNames.DeveloperSection, ConfigNames.AutoStartCharacter, "",
                "For testing the mod: with AutoStartWorld, the game goes straight from the main menu into that world with this " +
                "character (once per launch). Empty = off.", synced: false);
            AutoStartWorld = config.BindConfig(ConfigNames.DeveloperSection, ConfigNames.AutoStartWorld, "",
                "For testing the mod: the world AutoStartCharacter goes straight into. Only a world whose name contains 'test'. " +
                "Empty = off.", synced: false);
            VerboseLog = config.BindConfig(ConfigNames.DeveloperSection, ConfigNames.VerboseLog, false,
                "Writes every settler's step (walks, doors, swings, deliveries) to BepInEx/LogOutput.log, for finding out why " +
                "something went wrong. Always on in test worlds (a name containing 'test'). Start-up, deaths, the colony's saga, " +
                "warnings and errors are written either way.", synced: false);
        }
    }
}
