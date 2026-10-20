using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;
using VikingColonies.Jobs;

namespace VikingColonies.Gui
{
    /// <summary>
    /// A job hut's window, in tabs (worker polish, 0.20). Hut: its level and upgrade, its worker with Hire, Fire, Auto-hire
    /// and Recall, its list, its building. Fields (a Farm): the colony's fields and which of them this Farm works. Keep in
    /// stock (every hut, 0.21): what its chest keeps (<see cref="StockTab"/>).
    /// </summary>
    internal sealed partial class HutWindow : WindowBase
    {
        private enum Tab { Hut, Fields, Stock }

        private Tab _tab = Tab.Hut;
        private Button _hutTab, _fieldsTab, _stockTab;
        private StockTab _stock;
        private readonly List<GameObject> _hutControls = new List<GameObject>();
        private readonly List<GameObject> _fieldsControls = new List<GameObject>();
        private Text _message;
        private Button _recall;
        private Button _role, _orders, _post;
        private Text _info;
        private Button _autoHire;
        private Button _build;
        private Button _upgrade;
        private Button _cancelUpgrade;
        private Button _replant;
        private Button _woodLimit;
        private Button _handOver;
        private Button _takeDown;
        private float _takeDownArmedUntil;
        private const float TakeDownConfirmSeconds = 5f;
        private Button _pick;
        private Button _grow;
        private Button _hire;
        private Button _fire;
        private UpgradePreview _preview;
        private float _previewAt = -100f;
        private int _previewLevel;
        private const float PreviewSeconds = 2f;   // the site check behind the upgrade line is redone this often

        protected override string Title => Building != null ? Building.Type?.NameToken ?? "" : "$vc_hut_lumberjack";
        protected override float Width => 460f;
        protected override float Height => 580f;

        protected override void Build(Transform root)
        {
            // Built again after every scene change (Jötunn makes its GUI anew): the old controls are gone (the whole-mod review of
            // 1.4.1: kept, they broke the window till the game was restarted).
            _hutControls.Clear();
            _fieldsControls.Clear();
            _hutTab = UiKit.TopButton(root, L("$vc_tab_hut"), -85f, -62f, 160f, 34f, () => Show(Tab.Hut));
            _fieldsTab = UiKit.TopButton(root, L("$vc_tab_fields"), 85f, -62f, 160f, 34f, () => Show(Tab.Fields));
            _stockTab = UiKit.TopButton(root, L("$vc_tab_stock"), 85f, -62f, 160f, 34f, () => Show(Tab.Stock));
            _info = UiKit.Label(root, "", -105f, Width - 60f, 235f);
            // The text shrinks to fit rather than run under the buttons (a hut with an order, an upgrade line and a note).
            _info.resizeTextForBestFit = true;
            _info.resizeTextMinSize = 12;
            _info.resizeTextMaxSize = 18;
            _message = UiKit.Fitted(UiKit.Label(root, "", -344f, Width - 60f, 26f, 16, false, TextAnchor.MiddleCenter));
            _build = UiKit.Button(root, L("$vc_build_button"), 0f, 130f, 150f, 36f, () =>
            {
                ConstructionModule site = Site();
                if (site == null) return;
                site.RequestCreateOrder();
                if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.Center, L("$vc_build_ordered"));
            });
            _hire = UiKit.Button(root, L("$vc_hire"), -150f, 80f, 130f, 36f, () => Worker()?.RequestHire());
            _fire = UiKit.Button(root, L("$vc_fire"), 0f, 80f, 130f, 36f, () => Worker()?.RequestFire());
            _autoHire = UiKit.Button(root, L("$vc_autohire_on"), 150f, 80f, 150f, 36f, () =>
            {
                WorkerModule w = Worker();
                if (w != null) w.RequestSetAutoHire(!w.AutoHire);
            });
            _upgrade = UiKit.Button(root, "", 0f, 130f, 220f, 36f, OnUpgrade);
            _cancelUpgrade = UiKit.Button(root, L("$vc_upgrade_cancel"), 0f, 130f, 220f, 36f, () =>
            {
                ConstructionModule s = Site();
                if (s == null) return;
                if (s.IsTakingDown) s.RequestCancelTakeDown();   // review of 0.23
                else s.RequestCancelUpgrade();
            });
            _recall = UiKit.Button(root, L("$vc_recall"), 145f, 180f, 140f, 36f, OnRecall);
            // The Watchtower (1.2): its guards' role, beside Recall; 1.3: their orders between, and their post beside Close.
            _role = UiKit.Button(root, "", -145f, 180f, 140f, 36f, () =>
            {
                WatchtowerModule tower = Building != null ? Building.GetComponent<WatchtowerModule>() : null;
                if (tower != null) tower.RequestSetRole(tower.Role == GuardRole.Archer ? GuardRole.Warrior : GuardRole.Archer);
            });
            _orders = UiKit.Button(root, "", 0f, 180f, 140f, 36f, () =>
            {
                WatchtowerModule tower = Building != null ? Building.GetComponent<WatchtowerModule>() : null;
                if (tower != null) tower.RequestSetOrders(tower.Orders == GuardOrders.HoldPost ? GuardOrders.Patrol : GuardOrders.HoldPost);
            });
            _post = UiKit.Button(root, "", -150f, 25f, 160f, 36f, () =>
            {
                WatchtowerModule tower = Building != null ? Building.GetComponent<WatchtowerModule>() : null;
                if (tower == null || Player.m_localPlayer == null) return;
                if (tower.PostAt != null) tower.RequestClearPost();
                else tower.RequestSetPost(Player.m_localPlayer.transform.position);
            });
            // The Lumberjack's Hut (0.22): Trees…, Stop at, Recall in a row.
            _woodLimit = UiKit.Button(root, "", 0f, 180f, 140f, 36f, () =>
            {
                WorkerModule w = Worker();
                if (w != null) w.RequestSetWoodLimit(WoodLimit.Next(w.WoodLimit));
            });
            // Take down (0.23): a finished hut, the second click within 5 s.
            _takeDown = UiKit.Button(root, L("$vc_takedown"), 150f, 25f, 140f, 36f, OnTakeDown);   // beside Close, off the message line (review of 0.23)
            // The Builder's Hut (0.23): what his order still needs, from your bag into his chest.
            _handOver = UiKit.Button(root, L("$vc_handover"), -145f, 180f, 140f, 36f, () => _message.text = HandOver());
            _replant = UiKit.Button(root, L("$vc_trees_button"), -145f, 180f, 140f, 36f, () =>
            {
                ColonyBuilding hut = Building;
                Close();
                if (hut != null) Windows.Trees.Open(hut);
            });
            _pick = UiKit.Button(root, L("$vc_pick_button"), -80f, 180f, 220f, 36f, () =>
            {
                ColonyBuilding hut = Building;
                Close();
                if (hut != null) Windows.Pick.Open(hut);
            });
            _grow = UiKit.Button(root, L("$vc_grow_button"), -80f, 180f, 220f, 36f, () =>
            {
                ColonyBuilding hut = Building;
                Close();
                if (hut != null) Windows.Grow.Open(hut);
            });
            foreach (Component c in new Component[] { _info, _message, _build, _hire, _fire, _autoHire, _upgrade, _cancelUpgrade, _recall, _replant, _woodLimit, _handOver, _takeDown, _pick, _grow, _role, _orders, _post })
                _hutControls.Add(c.gameObject);
            BuildFields(root);
            _stock = new StockTab(() => Building, hut =>
            {
                Close();
                Windows.StockAdd.Open(hut);
            });
            _stock.Build(root, Width, Height);
        }

        protected override void Opened()
        {
            _preview = null;   // another hut's upgrade line isn't this one's (the whole-mod review of 1.4.1)
            _message.text = "";
            if (_fieldsMessage != null) _fieldsMessage.text = "";
            _fieldsPageIndex = 0;
            _stock?.Reset();
            _takeDownArmedUntil = 0f;   // review of 0.23: "Sure?" carried over to the next hut opened
            Show(Tab.Hut);
        }

        /// <summary>How a recall went (the settler's computer answers).</summary>
        internal void ShowMessage(string text)
        {
            _message.text = text;
            if (_fieldsMessage != null) _fieldsMessage.text = text;
            _stock?.ShowMessage(text);
        }

        private void Show(Tab tab)
        {
            bool farm = Building != null && Building.TypeId == BuildingTypes.FarmId;
            if (tab == Tab.Fields && !farm) tab = Tab.Hut;
            _tab = tab;
            // Hidden controls first: a list button the hut doesn't have stays hidden by Refresh on the Hut tab.
            foreach (GameObject go in _fieldsControls) go.SetActive(tab == Tab.Fields);
            foreach (GameObject go in _stock.Controls) go.SetActive(tab == Tab.Stock);
            foreach (GameObject go in _hutControls) go.SetActive(tab == Tab.Hut);
            // Hut and Keep in stock on every hut; Fields between them on a Farm.
            _fieldsTab.gameObject.SetActive(farm);
            PlaceTab(_hutTab, farm ? -150f : -85f, farm ? 140f : 160f);
            PlaceTab(_fieldsTab, 0f, 140f);
            PlaceTab(_stockTab, farm ? 150f : 85f, farm ? 140f : 160f);
            _hutTab.interactable = tab != Tab.Hut;
            _fieldsTab.interactable = tab != Tab.Fields;
            _stockTab.interactable = tab != Tab.Stock;
            if (tab == Tab.Stock) HuginTips.Show(HuginTips.Stock);
            Refresh();
        }

        private static void PlaceTab(Button tab, float x, float width)
        {
            RectTransform rect = tab.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        }

        private void OnTakeDown()
        {
            ConstructionModule site = Site();
            if (site == null || !site.MayTakeDown) return;
            if (Time.time >= _takeDownArmedUntil)
            {
                _takeDownArmedUntil = Time.time + TakeDownConfirmSeconds;   // the first click asks
                UiKit.SetLabel(_takeDown, L("$vc_takedown_sure"));
                return;
            }
            _takeDownArmedUntil = 0f;
            _message.text = L("$vc_takedown_ordered");
            site.RequestTakeDown();   // after: on the hut's own computer a refusal comes at once and stays shown (review of 0.26)
        }

        private void OnRecall()
        {
            WorkerModule worker = Worker();
            if (worker == null || worker.FindWorker(out Citizen loaded, out _) != WorkerModule.WorkerState.Loaded) return;
            loaded.RequestRecallToHut();
            _message.text = string.Format(L("$vc_recalling_hut"), loaded.CitizenName);
        }

        protected override void Refresh()
        {
            if (_tab == Tab.Fields)
            {
                RefreshFields();
                return;
            }
            if (_tab == Tab.Stock)
            {
                _stock.Refresh();
                return;
            }
            WorkerModule worker = Worker();
            ResidenceModule residence = Building != null ? Building.GetComponent<ResidenceModule>() : null;
            if (worker == null && residence == null) return;
            bool lumberjack = Building.TypeId == BuildingTypes.LumberjackHutId;
            bool forager = Building.TypeId == BuildingTypes.ForagerHutId;
            bool farm = Building.TypeId == BuildingTypes.FarmId;
            string line = "";
            Citizen loaded = null;
            RosterEntry entry = null;
            WorkerModule.WorkerState state = worker != null ? worker.FindWorker(out loaded, out entry) : WorkerModule.WorkerState.None;
            switch (state)
            {
                case WorkerModule.WorkerState.Loaded:
                    // Good at this trade (0.18): "Astrid (good at foraging) — Picking"; a lumberjack's skill in the same brackets.
                    var notes = new List<string>();
                    if (Callings.Matches(loaded.Record.Calling, loaded.Record.Job)) notes.Add(L("$vc_good_at_" + loaded.Record.Calling));
                    WorkSkill skill = WorkSkills.ForJob(loaded.Record.Job);   // his trade's skill (0.22: every worker has one)
                    if (skill != WorkSkill.None) notes.Add($"{L(WorkSkills.Token(skill))} {loaded.Record.Level(skill):0}");
                    if (loaded.Record.Kills > 0) notes.Add(string.Format(L("$vc_kills"), loaded.Record.Kills));   // a guard's tally (1.3)
                    string about = notes.Count > 0 ? $" ({string.Join(", ", notes)})" : "";
                    line = $"{loaded.CitizenName}{about} — {L(loaded.Status)}";
                    break;
                case WorkerModule.WorkerState.Elsewhere:
                    line = $"{entry.Name} — {L(entry.Status)} ({L("$vc_far_away")})";
                    break;
                case WorkerModule.WorkerState.Unknown:
                    line = L("$vc_unknown");
                    break;
                default:
                    line = L("$vc_no_worker");
                    break;
            }
            bool mayManage = ColonyPermissions.LocalMayManage(Building);
            string colony = string.IsNullOrEmpty(Building.ColonyId)
                ? $"\n\n<color=orange>{L("$vc_build_near_town_hall")}</color>"
                : Building.GraceDaysLeft > 0 ? $"\n\n<color=orange>{string.Format(L(Building.GraceDaysLeft == 1 ? "$vc_hall_gone_grace_one" : "$vc_hall_gone_grace"), Building.GraceDaysLeft)}</color>"
                : mayManage ? "" : $"\n\n<color=orange>{L("$vc_not_your_colony")}</color>";
            string replant = lumberjack ? "\n" + TreesLine(Building) : forager ? "\n" + PickLine(Building) : farm ? "\n" + GrowLine(Building) : "";
            string work = Building.TypeId == BuildingTypes.BuilderHutId
                ? Line(ConstructionModule.DescribeBuilderWork(Building, state == WorkerModule.WorkerState.Loaded ? loaded : null)) : "";
            ConstructionModule site = Site();
            string building = site != null ? "\n\n" + site.Describe() : "";
            int level = Building.Level;
            bool hasOrder = site != null && site.Order != null;
            bool canCancel = hasOrder && (site.CanCancelUpgrade || site.IsTakingDown);
            bool showUpgrade = hasOrder && !canCancel && !site.IsUpgrading && !site.IsTakingDown && level < HutLevels.MaxLevel;
            string upgrade = showUpgrade ? "\n\n" + UpgradeLine(site, level) : "";
            if (worker != null && worker.Capacity > 1) line = WorkersLine(worker);   // a Watchtower's guards (1.2)
            string who = worker != null ? $"\n<b>{L("$vc_worker")}:</b> {line}" : $"\n<b>{L("$vc_beds")}:</b> {residence.Describe()}";
            string perks = worker != null ? $" — {PerksLine(Building)}" : "";
            _info.text = $"<b>{L("$vc_level")}:</b> {level}{perks}{who}{replant}{work}{building}{upgrade}{colony}";
            if (worker != null) UiKit.SetLabel(_autoHire, L(worker.AutoHire ? "$vc_autohire_on" : "$vc_autohire_off"));
            foreach (Button button in new[] { _hire, _fire, _autoHire, _recall }) button.gameObject.SetActive(worker != null);
            _recall.interactable = mayManage && state == WorkerModule.WorkerState.Loaded;
            _build.gameObject.SetActive(site != null && site.Order == null);
            _cancelUpgrade.gameObject.SetActive(canCancel);
            if (canCancel) UiKit.SetLabel(_cancelUpgrade, L(site.IsTakingDown ? "$vc_takedown_cancel" : "$vc_upgrade_cancel"));
            _replant.gameObject.SetActive(lumberjack);
            _handOver.gameObject.SetActive(Building.TypeId == BuildingTypes.BuilderHutId);
            bool mayTakeDown = site != null && site.MayTakeDown && !site.IsTakingDown;
            _takeDown.gameObject.SetActive(mayTakeDown);
            _takeDown.interactable = mayManage;
            UiKit.SetLabel(_takeDown, L(Time.time < _takeDownArmedUntil ? "$vc_takedown_sure" : "$vc_takedown"));
            _woodLimit.gameObject.SetActive(lumberjack && worker != null);
            if (lumberjack && worker != null)
            {
                UiKit.SetLabel(_woodLimit, worker.WoodLimit > 0 ? string.Format(L("$vc_wood_limit"), worker.WoodLimit) : L("$vc_wood_limit_none"));
                _woodLimit.interactable = mayManage;
            }
            _pick.gameObject.SetActive(forager);
            _grow.gameObject.SetActive(farm);
            WatchtowerModule watchtower = Building.GetComponent<WatchtowerModule>();
            _role.gameObject.SetActive(watchtower != null);
            _orders.gameObject.SetActive(watchtower != null);
            _post.gameObject.SetActive(watchtower != null);
            if (watchtower != null)
            {
                UiKit.SetLabel(_role, L(watchtower.Role == GuardRole.Archer ? "$vc_role_archer" : "$vc_role_warrior"));
                UiKit.SetLabel(_orders, L(watchtower.Orders == GuardOrders.HoldPost ? "$vc_orders_hold" : "$vc_orders_patrol"));
                UiKit.SetLabel(_post, L(watchtower.PostAt != null ? "$vc_post_by_tower" : "$vc_post_here"));
                _role.interactable = _orders.interactable = _post.interactable = mayManage;
            }
            _upgrade.gameObject.SetActive(showUpgrade);
            if (showUpgrade) UiKit.SetLabel(_upgrade, string.Format(L("$vc_upgrade_button"), level + 1));
            foreach (Button button in new[] { _hire, _fire, _autoHire, _build, _upgrade, _cancelUpgrade })
                button.interactable = mayManage;   // the replanting list can still be looked at; its window greys its ticks
        }

        /// <summary>What the upgrade needs, or why it is locked.</summary>
        private string UpgradeLine(ConstructionModule site, int level)
        {
            UpgradeLock locked = site.UpgradeCheck();
            if (locked != UpgradeLock.None) return $"<color=orange>{LockText(locked, level + 1)}</color>";
            UpgradePreview preview = Preview(site, level);
            if (preview == null) return $"<color=orange>{L("$vc_upgrade_no_design")}</color>";
            if (preview.Site.Colour == SiteColour.Red)
                return $"<color=orange>{string.Format(L("$vc_upgrade_lock_site"), SiteText.Describe(preview.Site, 0f))}</color>";
            string needs = preview.Needs.Count > 0 ? PiecePlacer.DescribeItems(preview.Needs) : L("$vc_upgrade_nothing");
            string line = string.Format(L("$vc_upgrade_needs"), level + 1, L(needs));
            string above = SiteText.AboveLevel(preview.Site);
            return above.Length > 0 ? $"{line}\n<color=orange>{above}</color>" : line;
        }

        /// <summary>The upgrade's needs and site check, worked out at most every two seconds.</summary>
        private UpgradePreview Preview(ConstructionModule site, int level)
        {
            if (_preview != null && _previewLevel == level && Time.time - _previewAt < PreviewSeconds) return _preview;
            _preview = site.PreviewUpgrade();
            _previewLevel = level;
            _previewAt = Time.time;
            return _preview;
        }

        private void OnUpgrade()
        {
            ConstructionModule site = Site();
            if (site == null) return;
            int target = Building.Level + 1;
            UpgradeLock locked = site.UpgradeCheck();
            string message;
            if (locked != UpgradeLock.None) message = LockText(locked, target);
            else
            {
                UpgradePreview preview = site.PreviewUpgrade();
                if (preview == null) message = L("$vc_upgrade_no_design");
                else if (preview.Site.Colour == SiteColour.Red) message = string.Format(L("$vc_upgrade_lock_site"), SiteText.Describe(preview.Site, 0f));
                else
                {
                    site.RequestUpgrade();
                    _preview = null;
                    message = string.Format(L("$vc_upgrade_ordered"), target);
                }
            }
            if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.Center, message);
        }

        private static string LockText(UpgradeLock locked, int target)
        {
            switch (locked)
            {
                case UpgradeLock.MaxLevel: return L("$vc_upgrade_lock_max");
                case UpgradeLock.NotFinished: return L("$vc_upgrade_lock_unfinished");
                case UpgradeLock.Upgrading: return L("$vc_upgrade_lock_running");
                case UpgradeLock.NoBuilderHut: return L("$vc_upgrade_lock_nobuilder");
                case UpgradeLock.BuilderTooLow: return string.Format(L("$vc_upgrade_lock_builder"), target);
                case UpgradeLock.TakingDown: return L("$vc_upgrade_lock_takedown");
                case UpgradeLock.TownHallTooLow: return string.Format(L("$vc_upgrade_lock_hall"), target, target);   // huts go as high as the hall
                default: return "";
            }
        }

        /// <summary>What the hut's level gives: work area (lumberjack), the best tools allowed, the guard's reach, the chest's rows.</summary>
        private static string PerksLine(ColonyBuilding hut)
        {
            int level = hut.Level;
            int rows = hut.Storage != null ? hut.Storage.Rows : 0;
            string axe = ToolName(ToolKind.Axe, HutLevels.ToolTierCap(ToolKind.Axe, level));
            if (hut.TypeId == BuildingTypes.LumberjackHutId)
                return string.Format(L("$vc_perks_lumberjack"), LumberjackJob.WorkRadiusOf(hut).ToString("0"), axe, rows);
            if (hut.TypeId == BuildingTypes.ForagerHutId)
                return string.Format(L("$vc_perks_forager"), ForagerJob.WorkRadiusOf(hut).ToString("0"), rows);
            if (hut.TypeId == BuildingTypes.FarmId)
                return string.Format(L("$vc_perks_farm"), FieldAssignment.Capacity(level), rows);
            if (hut.TypeId == BuildingTypes.WatchtowerId)
                return string.Format(L("$vc_perks_watchtower"), GuardLevels.Radius(level).ToString("0"), rows);
            return string.Format(L("$vc_perks_builder"), axe, ToolName(ToolKind.Pickaxe, HutLevels.ToolTierCap(ToolKind.Pickaxe, level)), rows);
        }

        /// <summary>The name of a tool of exactly this tier (the first by item name), e.g. "Iron axe".</summary>
        private static string ToolName(ToolKind kind, int tier)
        {
            ToolInfo tool = ToolChoice.Weakest(BuilderTools.Known.Where(t => t.Tier == tier), kind, tier);
            return tool != null ? L(tool.Name) : tier.ToString();
        }

        private static string Line(string text) => string.IsNullOrEmpty(text) ? "" : "\n" + text;

        /// <summary>"Grows 5 of 8 crops; fields: 2 of 3 (48 plots)", or how to give the Farm its first field (0.20.1).</summary>
        private static string GrowLine(ColonyBuilding farm)
        {
            List<CropLine> lines = CropList.For(farm);
            FieldsModule module = farm.GetComponent<FieldsModule>();
            List<Colony.FieldMarker> fields = module != null ? module.Fields() : new List<Colony.FieldMarker>();
            if (fields.Count == 0) return L("$vc_grow_no_field");
            int fieldPlots = fields.Sum(f => f.Size * f.Size - 1);
            return string.Format(L("$vc_grow_summary_fields"), lines.Count(l => l.Ticked), lines.Count(l => l.Offered),
                fields.Count, module != null ? module.Capacity : 1, fieldPlots);
        }

        /// <summary>"Picks 9 of 10 kinds" (his hut's list).</summary>
        private static string PickLine(ColonyBuilding hut)
        {
            List<ForageLine> lines = ForageList.For(hut);
            return string.Format(L("$vc_pick_summary"), lines.Count(l => l.Ticked), lines.Count);
        }

        /// <summary>"Fells 5 of 6 kinds, replants 3 · Replanting: on" (0.22).</summary>
        private static string TreesLine(ColonyBuilding hut)
        {
            List<TreeLine> lines = TreeList.For(hut);
            return string.Format(L("$vc_trees_summary"), lines.Count(l => l.Fell), lines.Count(l => l.CanFell), lines.Count(l => l.Replant))
                   + " · " + L(ReplantToken(hut));
        }

        private static string ReplantToken(ColonyBuilding hut)
        {
            bool hasCultivator = Construction.BuilderTools.InColony(hut, ReplantRules.CultivatorPrefab);
            switch (ReplantRules.State(ColonyConfig.ReplantEnabled.Value, ColonyConfig.ReplantNeedsCultivator.Value, hasCultivator))
            {
                case ReplantState.On: return "$vc_replant_on";
                case ReplantState.NeedsCultivator: return "$vc_replant_needs_cultivator";
                default: return "$vc_replant_off";
            }
        }

        private WorkerModule Worker() => Building != null ? Building.GetComponent<WorkerModule>() : null;

        /// <summary>"2 of 3: Ulf (Fighting 12) — On watch; Bjorn — Patrolling" (a Watchtower's guards, 1.2).</summary>
        private static string WorkersLine(WorkerModule worker)
        {
            if (!worker.Workers(out List<Citizen> loaded, out List<RosterEntry> elsewhere)) return L("$vc_unknown");
            var lines = new List<string>();
            foreach (Citizen c in loaded)
            {
                WorkSkill skill = WorkSkills.ForJob(c.Record.Job);
                string kills = c.Record.Kills > 0 ? ", " + string.Format(L("$vc_kills"), c.Record.Kills) : "";   // a guard's tally (1.3)
                string about = skill != WorkSkill.None ? $" ({L(WorkSkills.Token(skill))} {c.Record.Level(skill):0}{kills})" : "";
                lines.Add($"{c.CitizenName}{about} — {L(c.Status)}");
            }
            foreach (RosterEntry e in elsewhere) lines.Add($"{e.Name} — {L(e.Status)} ({L("$vc_far_away")})");
            string count = string.Format(L("$vc_places_count"), loaded.Count + elsewhere.Count, worker.Capacity);
            return lines.Count == 0 ? $"{count}, {L("$vc_no_worker")}" : count + "\n" + string.Join("\n", lines.ToArray());
        }

        /// <summary>Hand over on a Builder's Hut (0.23): for his current order, else the colony's first; returns the message.</summary>
        internal string HandOver()
        {
            ColonyBuilding hut = Building;
            if (hut == null) return "";
            WorkerModule worker = Worker();
            string builderId = "";
            if (worker != null && worker.FindWorker(out Citizen loaded, out RosterEntry entry) != WorkerModule.WorkerState.None)
                builderId = loaded != null ? loaded.Id : entry != null ? entry.CitizenId : "";
            return HandOverFromBag.ToChest(Player.m_localPlayer, ConstructionModule.CurrentFor(hut, builderId), hut.Storage);
        }

        private ConstructionModule Site() => Building != null ? Building.GetComponent<ConstructionModule>() : null;
    }
}
