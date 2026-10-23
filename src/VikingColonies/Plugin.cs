using System;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Core;
using VikingColonies.DevTools;

namespace VikingColonies
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "daalmyte.vikingcolonies";   // the player's GitHub name (decided 2026-10-17): it names the config file and can't change once released
        public const string PluginName = "VikingColonies";
        public const string PluginVersion = "1.4.3";

        private Harmony _harmony;

        public static Plugin Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            _harmony = new Harmony(PluginGuid);
            // The settings come first: every other part reads them (review of 0.25). Without them the mod stays off.
            List<string> settingsFailed = StartupSteps.Run(new[] { new StartupStep("settings", "the mod's settings", () => ColonyConfig.Bind(Config)) });
            if (settingsFailed.Count > 0)
            {
                Log.Error($"{PluginName} {PluginVersion} didn't start: {settingsFailed[0]}");
                return;
            }
            // One part at a time (0.25, review of 0.20, bug 1): a hook whose game method a Valheim update renamed fails
            // alone, is named in the log with the start-up check, and the rest of the mod still starts.
            var steps = new List<StartupStep>
            {
                new StartupStep("placement keys", "turning and placing huts", Construction.PlacementPatches.RegisterKeys),
                new StartupStep("texts", "the mod's texts", Translations.Register),
                new StartupStep("settler deaths", "settler deaths told to players", SettlerDeaths.Register),
                new StartupStep("pieces and settlers", "the colony's buildings, settlers and the builder's ground work", () =>
                {
                    PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabsAvailable;
                    ItemManager.OnItemsRegistered += Construction.GroundShaper.Register;   // the builder's ground operations, on every computer
                }),
            };
            // As PatchAll, class by class (each hook is its own class: one renamed game method fails alone).
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
                steps.Add(new StartupStep("hook " + (type.DeclaringType != null ? type.DeclaringType.Name + "." : "") + type.Name,
                    "a part of the mod (" + type.FullName + ")", () => Hook(type)));
            steps.Add(AddPart<Visuals.AreaRings>("colony rings"));
            steps.Add(AddPart<Visuals.FieldOutline>("field outlines"));
            steps.Add(AddPart<Visuals.BuildOutline>("the placement outline"));
            steps.Add(AddPart<DevRunner>("the test tools' background work"));
            steps.AddRange(new[]
            {
                Command<SelfTestCommand>(), Command<Construction.BuildCommand>(), Command<Construction.BlueprintCommand>(), Command<ReportCommand>(),
                Command<GotoCommand>(), Command<ViewCommand>(), Command<DoorsCommand>(), Command<WalkCommand>(), Command<SettlersCommand>(), Command<BedsCommand>(),
                Command<UiCommand>(), Command<SetCommand>(), Command<RemoveCommand>(), Command<KillCommand>(), Command<PerfCommand>(),
                Command<PlaceCommand>(), Command<FindFlatCommand>(), Command<GroundCommand>(), Command<SpeedCommand>(), Command<QuitCommand>(), Command<WorldCommand>(),
                Command<PoseCommand>(), Command<LifeTestCommand>(), Command<PolishTestCommand>(), Command<CropsCommand>(),
                Command<ObserveCommand>(), Command<WorkerTestCommand>(), Command<GuardTestCommand>(), Command<HallTestCommand>(), Command<WatchCommand>(), Command<ShotCommand>(), Command<BugReportCommand>(), Command<PieceDumpCommand>(), Command<SpawnInfoCommand>(), Command<CamCommand>(), Command<StampCommand>(), Command<TurretCommand>(), Command<BuildRunCommand>(),
            });
            steps.Add(AddPart<Gui.SettlerWords>("words above settlers' heads and Hugin's night tip"));
            steps.Add(AddPart<Gui.FeedbackAsker>("asking once what the player thinks of the mod"));
            steps.Add(AddPart<Citizens.SettlerBodies>("settlers passing through each other and players instead of shoving"));
            GameCheck.StartupFailures.Clear();
            GameCheck.StartupFailures.AddRange(StartupSteps.Run(steps));
            Log.Event($"{PluginName} {PluginVersion} loaded");
            GameCheck.Run();
        }

        private StartupStep AddPart<T>(string feature) where T : UnityEngine.Component =>
            new StartupStep("component " + typeof(T).Name, feature, () => gameObject.AddComponent<T>());

        private static StartupStep Command<T>() where T : ConsoleCommand, new() =>
            new StartupStep("command " + typeof(T).Name, "a console command", () => CommandManager.Instance.AddConsoleCommand(new T()));

        /// <summary>
        /// Patches one class, as PatchAll does. A hook class's own setup, and its outer class's (the game fields its hook
        /// reads), runs first: a field a Valheim update renamed fails the step and the hook isn't made, instead of
        /// failing inside the game's own code every frame (review of 0.25: building would have stopped working).
        /// </summary>
        private void Hook(Type type)
        {
            if (type.IsDefined(typeof(HarmonyPatch), false))
                for (Type outer = type; outer != null; outer = outer.DeclaringType)
                    System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(outer.TypeHandle);
            _harmony.CreateClassProcessor(type).Patch();
        }

        private void OnVanillaPrefabsAvailable()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= OnVanillaPrefabsAvailable;
            BuildingPieces.Register();
            CitizenPrefab.Register();
            SettlerCairn.Register();
            Construction.BlueprintLibrary.LogPlaceholderShapes();
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
