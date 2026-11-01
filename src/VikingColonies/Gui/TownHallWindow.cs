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
    /// The Town Hall's window, in four tabs. Citizens: the colony at a glance (beds, food, hunger, the next newcomer) and
    /// every member with job, status and workplace, ten a page; choose one and Recall brings him to the Town Hall.
    /// Saga: what happened in the colony, newest first (<see cref="SagaLog"/>). Work orders: every open build and upgrade
    /// order as builders take them, with Do first, Set aside / Resume and Cancel upgrade, and what they still need. Keep in
    /// stock (0.21): what its chest keeps, 10 food for newcomers to start (<see cref="StockTab"/>).
    /// </summary>
    internal sealed partial class TownHallWindow : WindowBase
    {
        private const int RowsPerPage = 10;
        private const int SagaPerPage = 12;
        private const float RowsTop = -210f, RowStep = 30f;   // 20 px lower for the details' mood line (0.17 review M5)
        /// <summary>
        /// The people's rows start lower still: a chosen settler's page sits above them (the 1.0 play-through: five lines had
        /// shrunk to a tiny font; 1.5.1: the page grew by their load and their saga lines).
        /// </summary>
        private const float CitizenRowsTop = RowsTop - 65f;
        private const int OrdersPerPage = 8;
        private const float NeedsRefreshSeconds = 2f;
        /// <summary>The camp tab's two parts sit under their buttons (1.5.1).</summary>
        private const float CampPartTop = -36f;

        /// <summary>1.5.1, the Chieftain's Camp's window (review 8's picture): it opens on the Overview.</summary>
        private enum Tab { Overview, People, Work, Saga, Camp }
        private enum CampPart { Levels, Stock }

        private Tab _tab = Tab.Overview;
        private CampPart _campPart = CampPart.Levels;
        private readonly List<GameObject> _citizensControls = new List<GameObject>();
        private readonly List<GameObject> _sagaControls = new List<GameObject>();
        private readonly List<GameObject> _ordersControls = new List<GameObject>();
        private readonly List<GameObject> _campControls = new List<GameObject>();
        private Button _overviewTab, _citizensTab, _sagaTab, _ordersTab, _hallTab;
        private Button _campLevels, _campStock;
        private StockTab _stock;
        private HallTab _hall;
        private Text _deeds;

        // Citizens
        private Text _header;
        private Text _summary;
        private readonly List<Button> _rows = new List<Button>();
        private readonly List<string> _rowIds = new List<string>();
        private Button _prev, _next, _recall;
        private Button _alarm;
        private Text _message;
        private int _citizenPage;
        private string _chosenId = "";

        // Saga
        private Text _saga;
        private Button _newer, _older;
        private int _sagaPage;

        // Work orders
        private Text _ordersHeader;
        private Text _ordersNeeds;
        private readonly List<Button> _orderRows = new List<Button>();
        private readonly List<string> _orderIds = new List<string>();
        private Text _orderDetail;
        private Button _orderPrev, _orderNext, _orderFirst, _orderAside, _orderCancel, _orderHandOver;
        private int _orderPage;
        private string _chosenOrder = "";
        private string _needsText = "";
        private float _needsAt = float.NegativeInfinity;

        protected override string Title => "$vc_town_hall";
        protected override float Width => CampWindowLayout.Width;   // 1.5.1: the Overview's two columns
        // The bottom row (1.5.1, review 8): Sound the alarm on the left, Close in the middle, Report and Feedback small on the right.
        protected override float CloseX => 0f;
        protected override float Height => CampWindowLayout.Height;   // 1.5.1: the camp tab's part buttons and a settler's page; fits a 1080p screen

        protected override void Build(Transform root)
        {
            // Built again after every scene change (Jötunn makes its GUI anew): the old controls are gone (the whole-mod review of
            // 1.4.1: kept, they broke the window till the game was restarted).
            _citizensControls.Clear();
            _sagaControls.Clear();
            _ordersControls.Clear();
            _campControls.Clear();
            _rows.Clear();
            _rowIds.Clear();
            _orderRows.Clear();
            _orderIds.Clear();
            // Five tabs (1.5.1, review 8): Overview, People, Work, Saga, The camp (its levels and its Keep in stock).
            _overviewTab = UiKit.TopButton(root, L("$vc_tab_overview"), -284f, -62f, 132f, 34f, () => Show(Tab.Overview));
            _citizensTab = UiKit.TopButton(root, L("$vc_tab_people"), -142f, -62f, 132f, 34f, () => Show(Tab.People));
            _ordersTab = UiKit.TopButton(root, L("$vc_tab_work"), 0f, -62f, 132f, 34f, () => Show(Tab.Work));
            _sagaTab = UiKit.TopButton(root, L("$vc_tab_saga"), 142f, -62f, 132f, 34f, () => Show(Tab.Saga));
            _hallTab = UiKit.TopButton(root, L("$vc_tab_hall"), 284f, -62f, 132f, 34f, () => Show(Tab.Camp));
            foreach (Button tab in new[] { _overviewTab, _citizensTab, _ordersTab, _sagaTab, _hallTab })
            {
                Text label = tab.GetComponentInChildren<Text>();
                label.resizeTextForBestFit = true;   // a longer word in another language shrinks rather than runs out
                label.resizeTextMinSize = 12;
                label.resizeTextMaxSize = label.fontSize;
            }
            BuildOverview(root);

            _header = UiKit.Label(root, "", -105f, Width - 60f, 28f, 20);
            // A chosen settler's page (1.5.1): who they are, mood and why, skills, home, their load, their saga lines.
            _summary = UiKit.Label(root, "", -135f, Width - 60f, 135f, 16);
            _summary.resizeTextForBestFit = true;   // shrinks to fit rather than run into the rows
            _summary.resizeTextMinSize = 12;
            _summary.resizeTextMaxSize = 16;
            _citizensControls.Add(_header.gameObject);
            _citizensControls.Add(_summary.gameObject);
            for (int i = 0; i < RowsPerPage; i++)
            {
                int index = i;
                Button row = UiKit.TopButton(root, "", 0f, CitizenRowsTop - i * RowStep, Width - 70f, 28f, () => Choose(index));
                Text label = row.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.supportRichText = true;
                label.fontSize = 15;
                label.resizeTextForBestFit = true;   // a long status shrinks a little rather than being cut off (the 1.0 play-through)
                label.resizeTextMinSize = 11;
                label.resizeTextMaxSize = 15;
                _rows.Add(row);
                _rowIds.Add("");
                _citizensControls.Add(row.gameObject);
            }
            _message = UiKit.Fitted(UiKit.Label(root, "", CitizenRowsTop - RowsPerPage * RowStep - 6f, Width - 60f, 28f, 16, false, TextAnchor.MiddleCenter));
            _citizensControls.Add(_message.gameObject);
            _prev = UiKit.Button(root, L("$vc_prev"), -220f, 75f, 120f, 36f, () => TurnCitizenPage(-1));
            _next = UiKit.Button(root, L("$vc_next"), -90f, 75f, 120f, 36f, () => TurnCitizenPage(1));
            _recall = UiKit.Button(root, L("$vc_recall"), 160f, 75f, 180f, 36f, RecallChosen);
            _citizensControls.Add(_prev.gameObject);
            _citizensControls.Add(_next.gameObject);
            _citizensControls.Add(_recall.gameObject);

            // The Saga on the left, the colony's deeds on the right (1.5.1).
            float sagaWidth = (Width - 60f) * 0.6f, deedsWidth = (Width - 60f) - sagaWidth - 20f;
            _saga = UiKit.Label(root, "", -105f, sagaWidth, 470f, 15);
            Shift(_saga, -(Width - 60f - sagaWidth) / 2f);
            _deeds = UiKit.Label(root, "", -105f, deedsWidth, 300f, 15);
            Shift(_deeds, (Width - 60f - deedsWidth) / 2f);
            _newer = UiKit.Button(root, L("$vc_newer"), -90f, 75f, 150f, 36f, () => TurnSagaPage(-1));
            _older = UiKit.Button(root, L("$vc_older"), 90f, 75f, 150f, 36f, () => TurnSagaPage(1));
            _sagaControls.Add(_saga.gameObject);
            _sagaControls.Add(_deeds.gameObject);
            _sagaControls.Add(_newer.gameObject);
            _sagaControls.Add(_older.gameObject);

            _ordersHeader = UiKit.Label(root, "", -105f, Width - 60f, 28f, 20);
            _ordersNeeds = UiKit.Label(root, "", -135f, Width - 220f, 50f, 16);
            RectTransform needsRect = _ordersNeeds.GetComponent<RectTransform>();
            needsRect.anchoredPosition = new Vector2(needsRect.anchoredPosition.x - 80f, needsRect.anchoredPosition.y);
            // Hand over (0.23): what the chosen order (else the first) still needs, from your bag into this chest.
            _orderHandOver = UiKit.TopButton(root, L("$vc_handover"), Width / 2f - 100f, -135f, 140f, 32f, HandOverChosen);   // clear of the needs line (review of 1.4.2)
            _ordersControls.Add(_ordersHeader.gameObject);
            _ordersControls.Add(_ordersNeeds.gameObject);
            _ordersControls.Add(_orderHandOver.gameObject);
            for (int i = 0; i < OrdersPerPage; i++)
            {
                int index = i;
                Button row = UiKit.TopButton(root, "", 0f, RowsTop - i * RowStep, Width - 70f, 28f, () => ChooseOrder(index));
                Text label = row.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.resizeTextForBestFit = true;   // a long status shrinks a little rather than being cut off (1.3.1)
                label.resizeTextMinSize = 11;
                label.resizeTextMaxSize = 15;
                label.supportRichText = true;
                label.fontSize = 15;
                _orderRows.Add(row);
                _orderIds.Add("");
                _ordersControls.Add(row.gameObject);
            }
            _orderDetail = UiKit.Label(root, "", RowsTop - OrdersPerPage * RowStep - 6f, Width - 60f, 125f, 15);
            _ordersControls.Add(_orderDetail.gameObject);
            _orderPrev = UiKit.Button(root, L("$vc_prev"), -268f, 75f, 76f, 36f, () => TurnOrderPage(-1));
            _orderNext = UiKit.Button(root, L("$vc_next"), -188f, 75f, 76f, 36f, () => TurnOrderPage(1));
            _orderFirst = UiKit.Button(root, L("$vc_order_do_first"), -80f, 75f, 120f, 36f, FirstChosen);
            _orderAside = UiKit.Button(root, L("$vc_order_set_aside"), 46f, 75f, 120f, 36f, AsideChosen);
            _orderCancel = UiKit.Button(root, L("$vc_order_cancel"), 190f, 75f, 150f, 36f, CancelChosen);
            foreach (Button b in new[] { _orderPrev, _orderNext, _orderFirst, _orderAside, _orderCancel }) _ordersControls.Add(b.gameObject);
            _stock = new StockTab(() => Building, hall =>
            {
                Close();
                Windows.StockAdd.Open(hall);
            });
            _stock.Build(root, Width, Height, CampPartTop);
            _hall = new HallTab(() => Building);
            _hall.Build(root, Width, CampPartTop);
            // The camp tab's two parts (1.5.1): its levels (the Hall of Trophies) and its Keep in stock, one at a time.
            _campLevels = UiKit.TopButton(root, L("$vc_camp_part_levels"), -80f, -100f, 150f, 28f, () => ShowCampPart(CampPart.Levels));
            _campStock = UiKit.TopButton(root, L("$vc_tab_stock"), 80f, -100f, 150f, 28f, () => ShowCampPart(CampPart.Stock));
            _campControls.Add(_campLevels.gameObject);
            _campControls.Add(_campStock.gameObject);
            // The bottom row (1.5.1): the alarm (1.3) on the left, sounded where you stand; Close in the middle; Report a
            // problem (1.4) and Feedback (1.4.2) smaller on the right; their places in CampWindowLayout (a test keeps them apart).
            _alarm = UiKit.Button(root, "", CampWindowLayout.Alarm.X, 25f, CampWindowLayout.Alarm.Width, 36f, () =>
            {
                TownHallModule hall = Building != null ? Building.GetComponent<TownHallModule>() : null;
                if (hall != null && Player.m_localPlayer != null) hall.RequestAlarm(!hall.AlarmOn, Player.m_localPlayer.transform.position);
            });
            // Report a problem opens its own window (1.6): the bug form or Discord, and where the file is.
            Button report = UiKit.Button(root, L("$vc_report_problem"), CampWindowLayout.Report.X, 25f, CampWindowLayout.Report.Width, 30f, PressReport);
            Button feedback = UiKit.Button(root, L("$vc_feedback"), CampWindowLayout.Feedback.X, 25f, CampWindowLayout.Feedback.Width, 30f, PressFeedback);
            foreach (Button small in new[] { report, feedback })
            {
                Text label = small.GetComponentInChildren<Text>();
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 11;
                label.resizeTextMaxSize = 14;
            }
            Show(Tab.Overview);
        }

        /// <summary>Moves a top-anchored label sideways (the Saga's two columns, the Overview's).</summary>
        private static void Shift(Text label, float x)
        {
            RectTransform rect = label.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x + x, rect.anchoredPosition.y);
        }

        /// <summary>The Report a problem button: this window closes and Report a problem's window opens (1.6), writing the report.</summary>
        internal void PressReport()
        {
            Close();
            Windows.Report.Show();
        }

        /// <summary>The Feedback button: this window closes and the feedback window opens (without the ask's "Not now").</summary>
        internal void PressFeedback()
        {
            Close();
            Windows.Feedback.Show(asking: false);
        }

        /// <summary>Each opening starts on the Overview, first pages, nobody chosen. Seen in review: the last window's state stayed.</summary>
        protected override void Opened()
        {
            _citizenPage = 0;
            _sagaPage = 0;
            _orderPage = 0;
            _chosenId = "";
            _chosenOrder = "";
            _needsAt = float.NegativeInfinity;
            _message.text = "";
            _stock?.Reset();
            _hall?.Reset();
            _campPart = CampPart.Levels;
            Show(Tab.Overview);
        }

        /// <summary>How a recall went (the settler's computer tells the asker).</summary>
        /// <summary>A reply, on whichever tab is open (1.4.3's review: only the Citizens tab showed it).</summary>
        internal void ShowMessage(string text)
        {
            _message.text = text;
            _stock?.ShowMessage(text);
            _hall?.ShowMessage(text);
        }

        private void Show(Tab tab)
        {
            _tab = tab;
            foreach (GameObject go in _overviewControls) go.SetActive(tab == Tab.Overview);
            foreach (GameObject go in _citizensControls) go.SetActive(tab == Tab.People);
            foreach (GameObject go in _sagaControls) go.SetActive(tab == Tab.Saga);
            foreach (GameObject go in _ordersControls) go.SetActive(tab == Tab.Work);
            foreach (GameObject go in _campControls) go.SetActive(tab == Tab.Camp);
            foreach (GameObject go in _stock.Controls) go.SetActive(tab == Tab.Camp && _campPart == CampPart.Stock);
            foreach (GameObject go in _hall.Controls) go.SetActive(tab == Tab.Camp && _campPart == CampPart.Levels);
            _overviewTab.interactable = tab != Tab.Overview;   // the open tab's button is greyed: you're on it
            _citizensTab.interactable = tab != Tab.People;
            _sagaTab.interactable = tab != Tab.Saga;
            _ordersTab.interactable = tab != Tab.Work;
            _hallTab.interactable = tab != Tab.Camp;
            _campLevels.interactable = _campPart != CampPart.Levels;
            _campStock.interactable = _campPart != CampPart.Stock;
            if (tab == Tab.Camp && _campPart == CampPart.Stock) HuginTips.Show(HuginTips.Stock);
            if (Building != null) Refresh();
        }

        /// <summary>The camp tab's part (1.5.1): its levels or its Keep in stock.</summary>
        private void ShowCampPart(CampPart part)
        {
            _campPart = part;
            Show(Tab.Camp);
        }

        private Button _creativeEat;

        /// <summary>
        /// Creative mode (1.5): Finish all now, Set level, Add settlers, Settlers eat (only while the world has No build cost),
        /// and the camp's Build now and Repair now.
        /// </summary>
        protected override void BuildCreative(CreativeBar bar)
        {
            bar.Add("$vc_creative_finish_all", "$vc_creative_finish_all_hover", () => HallModule()?.FinishAllNow(), () => HallModule() != null);
            bar.Add("$vc_creative_level_up", "$vc_creative_level_hover",
                () => HallModule()?.RequestCreative(HallCreativeAction.SetLevel, Building.Level + 1), () => HallModule() != null && Building.Level < TownHallLevels.MaxLevel);
            bar.Add("$vc_creative_level_down", "$vc_creative_level_hover",
                () => HallModule()?.RequestCreative(HallCreativeAction.SetLevel, Building.Level - 1), () => HallModule() != null && Building.Level > 1);
            bar.Add("$vc_creative_add1", "$vc_creative_add_hover", () => HallModule()?.RequestCreative(HallCreativeAction.AddSettlers, 1), () => HallModule() != null);
            bar.Add("$vc_creative_add5", "$vc_creative_add_hover", () => HallModule()?.RequestCreative(HallCreativeAction.AddSettlers, 5), () => HallModule() != null);
            _creativeEat = bar.Add("$vc_creative_eat_button_off", "$vc_creative_eat_hover",
                () => HallModule()?.RequestCreative(HallCreativeAction.EatOff, EatOff() ? 0 : 1), () =>
                {
                    if (HallModule() == null || !Colony.Creative.WorldNoBuildCost()) return false;
                    UiKit.SetLabel(_creativeEat, L(EatOff() ? "$vc_creative_eat_button_on" : "$vc_creative_eat_button_off"));
                    return true;
                });
            bar.Add("$vc_creative_camp_build", "$vc_creative_build_hover", () => CampSite()?.RequestCreative(CreativeAction.Build), () =>
            {
                BuildOrderRecord order = CampSite()?.Order;
                return order != null && order.State != BuildState.Done && order.Kind != OrderKind.Remove;
            });
            bar.Add("$vc_creative_camp_repair", "$vc_creative_repair_hover", () => CampSite()?.RequestCreative(CreativeAction.Repair),
                () => CampSite() != null && CampSite().Order != null && CampSite().IsBuilt);
        }

        private TownHallModule HallModule() => Building != null ? Building.GetComponent<TownHallModule>() : null;
        private ConstructionModule CampSite() => Building != null ? Building.GetComponent<ConstructionModule>() : null;

        private bool EatOff()
        {
            ZNetView nview = Building != null ? Building.GetComponent<ZNetView>() : null;
            return nview != null && nview.IsValid() && nview.GetZDO().GetInt(SaveKeys.ColonyEatOff, 0) == 1;
        }

        protected override void Refresh()
        {
            TownHallModule townHall = Building != null ? Building.GetComponent<TownHallModule>() : null;
            if (townHall == null) return;
            UiKit.SetLabel(_alarm, L(townHall.AlarmOn ? "$vc_alarm_clear" : "$vc_alarm_sound"));
            _alarm.interactable = ColonyPermissions.LocalMayManage(Building);
            if (_tab == Tab.Overview) RefreshOverview(townHall);
            else if (_tab == Tab.People) RefreshCitizens(townHall);
            else if (_tab == Tab.Saga) RefreshSaga(townHall);
            else if (_tab == Tab.Camp && _campPart == CampPart.Stock) _stock.Refresh();
            else if (_tab == Tab.Camp) _hall.Refresh();
            else RefreshOrders();
        }

        private void RefreshCitizens(TownHallModule townHall)
        {
            ColonyRoster roster = townHall.Roster;
            IReadOnlyList<RosterEntry> entries = roster.Entries;
            int jobless = entries.Count(e => string.IsNullOrEmpty(e.Workplace));
            _header.text = string.Format(L("$vc_citizens_count"), entries.Count, jobless);
            _summary.text = L("$vc_page_choose");   // the colony at a glance is the Overview now (1.5.1)
            _citizenPage = RosterPage.ClampPage(_citizenPage, entries.Count, RowsPerPage);
            _chosenId = RosterPage.KeepChoice(_chosenId, entries);
            IReadOnlyList<RosterEntry> rows = RosterPage.Rows(entries, _citizenPage, RowsPerPage);
            var loadedIds = new HashSet<string>(Citizen.All.Where(c => c != null).Select(c => c.Id));
            int today = EnvMan.instance != null && ZNet.instance != null ? EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) : 0;
            for (int i = 0; i < RowsPerPage; i++)
            {
                bool used = i < rows.Count;
                _rows[i].gameObject.SetActive(used);
                _rowIds[i] = used ? rows[i].CitizenId : "";
                if (!used) continue;
                RosterEntry e = rows[i];
                string job = string.IsNullOrEmpty(e.Job) ? "" : JobDefinitions.TitleOf(e.Job);
                string status = e.IsMissing
                    ? $"<color=orange>{L("$vc_status_missing")}</color> ({string.Format(L("$vc_last_seen"), e.MissingSinceDay)})"
                    : L(e.Status);
                // Out of sight here, not missing (0.19): when the roster last saw him.
                string seen = CitizenLine.SeenToken(e.LastSeenDay, today);
                bool hutFarAway = e.Workplace.Length > 0 && ColonyBuilding.FindById(e.Workplace) == null;   // the line ends "@ far away" already
                if (!e.IsMissing && !loadedIds.Contains(e.CitizenId) && (seen != "$vc_far_away" || !hutFarAway))
                    status += $" <color=#c8c8c8>({string.Format(L(seen), e.LastSeenDay)})</color>";
                string line = CitizenLine.Format(e.Name, job, status, WorkplaceName(e.Workplace));
                UiKit.SetLabel(_rows[i], (e.CitizenId == _chosenId ? "<color=orange>▶</color> " : "   ") + line);
            }
            int pages = RosterPage.Pages(entries.Count, RowsPerPage);
            _prev.interactable = _citizenPage > 0;
            _next.interactable = _citizenPage < pages - 1;
            Citizen chosen = ChosenCitizen(roster);
            RosterEntry lost = MissingChosen(townHall);
            if (chosen != null) _summary.text = string.Join("\n", Page(chosen, townHall));   // their page (1.5.1)
            _recall.interactable = (chosen != null || lost != null) && ColonyPermissions.LocalMayManage(Building);
            // The chosen one may be on another page: the button says whom it calls back (or brings back, when he is missing).
            UiKit.SetLabel(_recall, chosen != null ? string.Format(L("$vc_recall_who"), chosen.CitizenName)
                : lost != null ? string.Format(L("$vc_bring_back_who"), lost.Name) : L("$vc_recall"));
        }

        /// <summary>
        /// A settler's page (1.5.1, review 8): who they are, mood and why, skills, home (<see cref="Details"/>), then what they
        /// carry and their newest lines in the Saga.
        /// </summary>
        private static List<string> Page(Citizen citizen, TownHallModule townHall)
        {
            var lines = new List<string>(Details(citizen));
            var load = new Dictionary<string, int>();
            foreach (string item in citizen.Carry.ItemNames) load[item] = citizen.Carry.Count(item);
            lines.Add(load.Count > 0 ? string.Format(L("$vc_page_carrying"), L(PiecePlacer.DescribeItems(load))) : L("$vc_page_carrying_nothing"));
            IReadOnlyList<SagaEntry> saga = CampOverview.LinesNaming(townHall.Saga, citizen.CitizenName, 2);
            lines.Add(L("$vc_page_saga") + " " + (saga.Count > 0 ? string.Join("  ·  ", saga.Select(SagaText.Line)) : L("$vc_page_saga_none")));
            return lines;
        }

        /// <summary>"Astrid: good at foraging, cheerful. From the coast." / "Here 23 days. Home: a Longhouse bed."</summary>
        private static string[] Details(Citizen citizen)
        {
            CitizenRecord r = citizen.Record;
            string calling = r.Calling.Length > 0 && r.Calling != Callings.None ? L("$vc_good_at_" + r.Calling) : L("$vc_calling_none");
            int today = EnvMan.instance != null && ZNet.instance != null ? EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) : 0;
            string days = r.Arrived == CitizenRecord.ArrivedUnknown ? "" : string.Format(L(CitizenLine.DaysToken(today - r.Arrived)), today - r.Arrived);
            int bed = r.HasHome ? ResidenceModule.BedNumberOf(r.Home) : 0;
            string home = !r.HasHome ? L("$vc_no_home") : bed > 0 ? string.Format(L("$vc_home_bed_n"), bed) : L("$vc_home_bed");
            string[] lines = CitizenLine.Details(r.Name, calling, L(Traits.Token(citizen.TraitKind)), r.Origin.Length > 0 ? L(r.Origin) : "",
                days, home);
            string skills = WorkSkills.Line(r, L);   // "Woodcutting 12 · Farming 3" (0.22)
            if (r.Kills > 0) skills = (skills.Length > 0 ? skills + " · " : "") + string.Format(L("$vc_kills"), r.Kills);   // a guard's tally (1.3)
            if (skills.Length > 0) lines = lines.Concat(new[] { skills }).ToArray();
            if (!ColonyConfig.Mood.Value || r.Mood == Mood.Unknown) return lines;
            // "Content (62): +5 home, +5 cosy bed, +5 ate, -10 Ingrid died" (0.17).
            string why = MoodLevels.ReasonsLine(MoodLevels.Decode(r.WhyMood), L);
            return lines.Concat(new[] { string.Format(L("$vc_mood_details"), L(MoodLevels.Token(citizen.MoodLevel)), r.Mood, why) }).ToArray();
        }

        private void RefreshSaga(TownHallModule townHall)
        {
            SagaLog saga = townHall.Saga;
            int pages = saga.PageCount(SagaPerPage);
            _sagaPage = Mathf.Clamp(_sagaPage, 0, pages - 1);
            IReadOnlyList<SagaEntry> lines = saga.Page(_sagaPage, SagaPerPage);
            _saga.text = lines.Count == 0 ? L("$vc_saga_empty") : string.Join("\n", lines.Select(SagaText.Line));
            _newer.interactable = _sagaPage > 0;
            _older.interactable = _sagaPage < pages - 1;
            _deeds.text = DeedsText(townHall.Deeds);
        }

        /// <summary>
        /// The colony's deeds (1.5.1, from MineColonies' statistics): today and all time, a row each. Counting began with
        /// 1.5.1, so an older colony starts at 0.
        /// </summary>
        private static string DeedsText(ColonyDeeds deeds)
        {
            int today = EnvMan.instance != null && ZNet.instance != null ? EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) : 0;
            var text = new System.Text.StringBuilder();
            text.Append($"<color=orange>{L("$vc_deeds_heading")}</color>\n");
            text.Append($"<color=#c8c8c8>{L("$vc_deeds_today")} · {L("$vc_deeds_all")}</color>\n");
            foreach (string kind in DeedKinds.All)
                text.Append($"{L("$vc_deeds_" + kind)}: {deeds.Today(kind, today)} · {deeds.AllTime(kind)}\n");
            return text.ToString().TrimEnd('\n');
        }

        /// <summary>The colony's open orders (loaded huts: the colony, when you stand at its Town Hall), as builders take them.</summary>
        private List<ConstructionModule> OpenOrders()
        {
            return ConstructionModule.OpenOrders(Building != null ? Building.ColonyId : "");
        }

        private void RefreshOrders()
        {
            List<ConstructionModule> orders = OpenOrders();
            int aside = orders.Count(s => s.Order.Paused);
            _ordersHeader.text = string.Format(L("$vc_orders_count"), orders.Count, aside);
            if (Time.time - _needsAt >= NeedsRefreshSeconds)
            {
                _needsAt = Time.time;
                _needsText = StillToBring(orders);
            }
            _ordersNeeds.text = _needsText;
            _orderPage = RosterPage.ClampPage(_orderPage, orders.Count, OrdersPerPage);
            if (orders.All(s => s.Building.Id != _chosenOrder)) _chosenOrder = "";
            List<ConstructionModule> page = orders.Skip(_orderPage * OrdersPerPage).Take(OrdersPerPage).ToList();
            // Only the newest Do first is tagged: it is the one builders take first (review: every mark kept its tag).
            ConstructionModule firstOne = orders.Where(s => !s.Order.Paused && s.Order.First > 0).OrderByDescending(s => s.Order.First).FirstOrDefault();
            for (int i = 0; i < OrdersPerPage; i++)
            {
                bool used = i < page.Count;
                _orderRows[i].gameObject.SetActive(used);
                _orderIds[i] = used ? page[i].Building.Id : "";
                if (!used) continue;
                string marker = page[i].Building.Id == _chosenOrder ? "<color=orange>▶</color> " : "   ";
                UiKit.SetLabel(_orderRows[i], marker + OrderLine(page[i], page[i] == firstOne));
            }
            if (orders.Count == 0) _orderDetail.text = L("$vc_orders_none");
            ConstructionModule chosen = orders.FirstOrDefault(s => s.Building.Id == _chosenOrder);
            // Named, as the chosen order may be on another page after the list re-sorts (review).
            if (orders.Count > 0) _orderDetail.text = chosen != null ? $"<b>{OrderTitle(chosen)}</b>\n{chosen.Describe()}" : L("$vc_orders_choose");
            int pages = RosterPage.Pages(orders.Count, OrdersPerPage);
            _orderPrev.interactable = _orderPage > 0;
            _orderNext.interactable = _orderPage < pages - 1;
            bool may = chosen != null && ColonyPermissions.LocalMayManage(chosen.Building);
            BuildOrderRecord chosenOrder = chosen?.Order;
            _orderFirst.interactable = may;
            _orderAside.interactable = may;
            UiKit.SetLabel(_orderAside, L(chosenOrder != null && chosenOrder.Paused ? "$vc_order_resume" : "$vc_order_set_aside"));
            _orderCancel.interactable = may && (chosen.CanCancelUpgrade || chosen.IsRepairing || chosen.IsTakingDown);
            // The camp's order has no Cancel: it grows with the hall and would be ordered again within 15 s (review of 2026-10-19).
            _orderCancel.gameObject.SetActive(chosen == null || !chosen.IsCamp);
            UiKit.SetLabel(_orderCancel, L(chosen != null && chosen.IsRepairing ? "$vc_order_leave_as_is"
                : chosen != null && chosen.IsTakingDown ? "$vc_order_cancel_takedown"
                : chosenOrder != null && chosenOrder.Kind == OrderKind.Upgrade ? "$vc_order_cancel" : "$vc_order_cancel_plain"));   // 'Cancel upgrade' on a new build (1.0 play-through)
        }

        /// <summary>"Lumberjack's Hut, level 2", "Forager's Hut, new" or "Town Hall camp, level 3".</summary>
        private static string OrderTitle(ConstructionModule site)
        {
            BuildOrderRecord order = site.Order;
            string what = string.Format(L(OrderWords.KindToken(order.Kind)), order.TargetLevel);
            string name = site.IsCamp ? L("$vc_camp_name") : L(site.Building.Type?.NameToken ?? "");   // not "Town Hall, level 3" (review of 2026-10-19)
            return $"{name}, {what}";
        }

        /// <summary>"First Lumberjack's Hut, level 2 — 32/80 — Signe is building" (the tag only on the newest Do first).</summary>
        private static string OrderLine(ConstructionModule site, bool firstOne)
        {
            BuildOrderRecord order = site.Order;
            string first = firstOne ? $"<color=yellow>{L("$vc_order_first_tag")}</color> " : "";
            string progress = order.Kind == OrderKind.Remove ? string.Format(L("$vc_order_pieces_left"), order.TakeAwayLeft) : $"{order.DoneCount}/{order.Total}";
            return $"{first}{OrderTitle(site)} — {progress} — {OrderStatus(site, order)}";
        }

        private static string OrderStatus(ConstructionModule site, BuildOrderRecord order)
        {
            if (order.Paused) return $"<color=orange>{L("$vc_order_status_aside")}</color>";
            // What it waits on comes first, also once the builder has moved on from it (review: it read "waiting for a builder").
            // Set aside for materials the chests hold by now: he comes back to it (1.3.1: it went on saying "needs 1 Wood").
            if (order.Needs.Length > 0 && !order.HasLiveClaim(ConstructionModule.Now()) && OrderWords.IsMaterials(order.Needs) && Covered(site))
                return L("$vc_order_status_has_now");
            if (order.Needs.Length > 0) return $"<color=orange>{string.Format(L("$vc_order_status_needs"), Short(L(order.Needs)))}</color>";
            if (order.Blocked.Length > 0) return $"<color=orange>{string.Format(L("$vc_order_status_blocked"), Short(L(order.Blocked)))}</color>";
            if (order.HasLiveClaim(ConstructionModule.Now()))
            {
                Citizen builder = Citizen.All.FirstOrDefault(c => c != null && c.Id == order.Builder);
                string who = builder != null ? builder.CitizenName : "?";
                if (order.State == BuildState.Clearing) return string.Format(L("$vc_order_status_clearing"), who);
                if (order.State == BuildState.Shaping) return string.Format(L("$vc_order_status_shaping"), who);
                if (order.Kind == OrderKind.Remove) return string.Format(L("$vc_order_status_taking_down"), who);
                return string.Format(L("$vc_order_status_building"), who);
            }
            int builderLevel = ConstructionModule.HighestBuilderHutLevel(site.Building.ColonyId);
            if (builderLevel == 0) return L("$vc_order_status_no_builder");
            // No Builder's Hut of the colony may build this level: say which level it takes (review: it read "waiting" for ever).
            if (!HutLevels.MayBuild(builderLevel, order.TargetLevel, false) && !(site.Building.TypeId == BuildingTypes.BuilderHutId))
                return $"<color=orange>{string.Format(L("$vc_order_status_needs_level"), order.TargetLevel)}</color>";
            return L("$vc_order_status_waiting");
        }

        /// <summary>The colony's chests hold all the order still needs (what the builders carry for it counts too).</summary>
        private static bool Covered(ConstructionModule site)
        {
            Dictionary<string, int> stock = BuilderTools.StoresForBuilders(site.Building.ColonyId, site.Building.transform.position, site.Building);
            stock = HandOver.Available(stock, Construction.HandOverFromBag.BuilderCarries(site));
            return NeedsTally.Shortfall(site.RemainingNeeds(), stock).Count == 0;
        }

        private static string Short(string text) => text.Length <= 40 ? text : text.Substring(0, 39) + "…";

        /// <summary>What the open orders (not set aside) still need beyond what the colony's chests hold.</summary>
        private string StillToBring(List<ConstructionModule> orders)
        {
            var total = new Dictionary<string, int>();
            foreach (ConstructionModule site in orders.Where(s => !s.Order.Paused)) NeedsTally.Add(total, site.RemainingNeeds());
            if (total.Count == 0) return orders.Count > 0 ? L("$vc_orders_needs_none") : "";
            Dictionary<string, int> stock = BuilderTools.StoresForBuilders(Building.ColonyId, Building.transform.position);
            // What the builders already carry for these orders is on its way (review of 0.26: as Hand over counts it).
            foreach (ConstructionModule site in orders.Where(s => !s.Order.Paused))
                stock = HandOver.Available(stock, Construction.HandOverFromBag.BuilderCarries(site));
            Dictionary<string, int> missing = NeedsTally.Shortfall(total, stock);
            return missing.Count == 0 ? L("$vc_orders_needs_none") : string.Format(L("$vc_orders_needs"), L(PiecePlacer.DescribeItems(missing)));   // item names in the player's language
        }

        private ConstructionModule ChosenOrder() =>
            _chosenOrder.Length == 0 ? null : ConstructionModule.All.FirstOrDefault(s => s != null && s.IsValid && s.Building != null && s.Building.Id == _chosenOrder);

        private void ChooseOrder(int row)
        {
            if (row < 0 || row >= _orderIds.Count || _orderIds[row].Length == 0) return;
            _chosenOrder = _chosenOrder == _orderIds[row] ? "" : _orderIds[row];   // a second click lets go
            Refresh();
        }

        private void TurnOrderPage(int by)
        {
            _orderPage += by;
            Refresh();
        }

        private void FirstChosen()
        {
            ConstructionModule site = ChosenOrder();
            if (site == null || !ColonyPermissions.LocalMayManage(site.Building)) return;
            site.RequestFirst();
            SoonRecount();
        }

        /// <summary>The sum is worked out again a moment from now, once the hut's computer has applied the change (review).</summary>
        private void SoonRecount() => _needsAt = Time.time - NeedsRefreshSeconds + 0.6f;

        private void AsideChosen()
        {
            ConstructionModule site = ChosenOrder();
            BuildOrderRecord order = site?.Order;
            if (order == null || !ColonyPermissions.LocalMayManage(site.Building)) return;
            site.RequestPause(!order.Paused);
            SoonRecount();
        }

        private void HandOverChosen()
        {
            List<ConstructionModule> orders = OpenOrders();
            ConstructionModule site = ChosenOrder() ?? orders.FirstOrDefault(s => !s.Order.Paused && s.Order.Kind != OrderKind.Remove);
            string message = HandOverFromBag.ToChest(Player.m_localPlayer, site, Building != null ? Building.Storage : null);
            if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.Center, message);
            _needsAt = float.NegativeInfinity;   // "Still to bring" again now
        }

        private void CancelChosen()
        {
            ConstructionModule site = ChosenOrder();
            if (site == null || !ColonyPermissions.LocalMayManage(site.Building)) return;
            if (site.IsRepairing) site.RequestLeaveAsIs();   // the same button leaves a repair as it is
            else if (site.IsTakingDown) site.RequestCancelTakeDown();   // review of 0.23
            else if (site.CanCancelUpgrade) site.RequestCancelUpgrade();
            else return;
            SoonRecount();
        }

        /// <summary>Test tool (vc_ui order): chooses the first listed order of a hut kind and presses a button; false when there's none.</summary>
        internal bool OrderActionForTest(string hutType, string action)
        {
            ConstructionModule site = OpenOrders().FirstOrDefault(s => s.Building.TypeId == hutType);
            if (site == null) return false;
            _chosenOrder = site.Building.Id;
            switch (action)
            {
                case "first": FirstChosen(); break;
                case "aside": if (!site.Order.Paused) AsideChosen(); break;
                case "resume": if (site.Order.Paused) AsideChosen(); break;
                case "cancel": CancelChosen(); break;
            }
            Refresh();
            return true;
        }

        /// <summary>The chosen citizen, when he's on the roster, not missing and loaded here (so he can be called back).</summary>
        private Citizen ChosenCitizen(ColonyRoster roster)
        {
            if (_chosenId.Length == 0) return null;
            RosterEntry entry = roster.Get(_chosenId);
            if (entry == null || entry.IsMissing) return null;
            return Citizen.All.FirstOrDefault(c => c != null && c.Id == _chosenId);
        }

        private void Choose(int row)
        {
            if (row < 0 || row >= _rowIds.Count || _rowIds[row].Length == 0) return;
            _chosenId = _chosenId == _rowIds[row] ? "" : _rowIds[row];   // a second click lets go
            _message.text = "";
            Refresh();
        }

        private void TurnCitizenPage(int by)
        {
            _citizenPage += by;
            Refresh();
        }

        private void TurnSagaPage(int by)
        {
            _sagaPage += by;
            Refresh();
        }

        /// <summary>The chosen citizen when he is marked missing and the colony keeps a keepsake of him (he can be brought back).</summary>
        private RosterEntry MissingChosen(TownHallModule townHall)
        {
            if (_chosenId.Length == 0 || townHall == null) return null;
            RosterEntry entry = townHall.Roster.Get(_chosenId);
            int today = EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;
            return ColonyRoster.MayBringBack(entry, today) && townHall.Keepsakes.Get(_chosenId) != null ? entry : null;
        }

        private void RecallChosen()
        {
            TownHallModule townHall = Building != null ? Building.GetComponent<TownHallModule>() : null;
            RosterEntry lost = MissingChosen(townHall);
            if (lost != null && ColonyPermissions.LocalMayManage(Building))
            {
                townHall.RequestBringBack(lost.CitizenId);   // the Town Hall's computer answers how it went
                return;
            }
            Citizen citizen = townHall != null ? ChosenCitizen(townHall.Roster) : null;
            if (citizen == null || !ColonyPermissions.LocalMayManage(Building)) return;
            _message.text = string.Format(L("$vc_recalling"), citizen.CitizenName);   // his computer answers how it went
            citizen.RequestRecall();
        }

        /// <summary>Test tool (vc_ui tab): shows a tab of the open window. The old names still work (citizens, orders, hall,
        /// stock), so older scenarios run as they did.</summary>
        internal void ShowTabForTest(string tab)
        {
            switch (tab)
            {
                case "people": case "citizens": Show(Tab.People); break;
                case "work": case "orders": Show(Tab.Work); break;
                case "saga": Show(Tab.Saga); break;
                case "camp": case "hall": ShowCampPart(CampPart.Levels); break;
                case "stock": ShowCampPart(CampPart.Stock); break;
                default: Show(Tab.Overview); break;
            }
        }

        /// <summary>How raising the camp went (its owner answers): on its levels.</summary>
        internal void ShowHallMessage(string text)
        {
            if (_tab != Tab.Camp || _campPart != CampPart.Levels) ShowCampPart(CampPart.Levels);
            _hall.ShowMessage(text);
        }

        /// <summary>Test tool: chooses a citizen by name on the Citizens tab (true when found on the roster).</summary>
        internal bool ChooseForTest(string name)
        {
            TownHallModule townHall = Building != null ? Building.GetComponent<TownHallModule>() : null;
            RosterEntry entry = townHall?.Roster.Entries.FirstOrDefault(e => e.Name == name);
            if (entry == null) return false;
            _chosenId = entry.CitizenId;
            Refresh();
            return true;
        }

        /// <summary>Test tool: presses Recall.</summary>
        internal void RecallForTest() => RecallChosen();

        /// <summary>"Beds: 3 of 4 taken   Food in the Town Hall: 12   Hungry: 1" and what the next newcomer waits for.</summary>
        public static string Summary(TownHallModule townHall)
        {
            ColonyNeeds needs = townHall.Needs();
            string colony = townHall.GetComponent<ColonyBuilding>()?.ColonyId ?? "";
            int hungry = Colony.Creative.SettlersEat(colony) ? Citizen.All.Count(c => c != null && c.Record.ColonyId == colony && c.Record.Hungry) : 0;
            // The food it keeps for newcomers, said where its food is counted (1.4.2: a rule only the guide told).
            int keptFood = townHall.GetComponent<StockModule>()?.List.Kept(StockList.AnyFood) ?? 0;
            string food = keptFood > 0 ? $"{needs.Food} ({string.Format(L("$vc_food_kept_newcomers"), keptFood)})" : needs.Food.ToString();
            string first = string.Format(L("$vc_colony_summary"), System.Math.Min(needs.Settlers, needs.Beds), needs.Beds, food,
                hungry > 0 ? $"<color=orange>{hungry}</color>" : "0");
            if (ColonyConfig.Mood.Value)
            {
                // The colony's mood (0.17): its settlers' average.
                int mood = townHall.ColonyMoodValue();   // from the roster: the same on every computer (0.19)
                string word = L(MoodLevels.Token(needs.Mood));
                first += "   " + string.Format(L("$vc_colony_mood"), needs.Mood < MoodLevel.Content ? $"<color=orange>{word}</color>" : word, mood);
            }
            string next = NewcomerWaitLine(townHall, needs);
            string store = StorehouseLine(townHall, colony);
            return first + "\n" + string.Format(L("$vc_newcomer_next"), next) + (store.Length > 0 ? "\n" + store : "");
        }

        /// <summary>What the next newcomer waits for, in words (the Overview's and the summary's).</summary>
        private static string NewcomerWaitLine(TownHallModule townHall, ColonyNeeds needs)
        {
            NewcomerRules rules = townHall.NewcomerRules;
            switch (Newcomers.WaitsFor(needs, rules))
            {
                case NewcomerWait.Off: return L("$vc_newcomer_off");
                case NewcomerWait.Full: return string.Format(L("$vc_newcomer_full"), rules.MaxSettlers);
                case NewcomerWait.HallFull:
                    // Only when raising the hall makes room (review of 1.4: at the top level it said so too).
                    int level = townHall.GetComponent<ColonyBuilding>().Level;
                    bool raising = level < TownHallLevels.MaxLevel && HallRules.PerksAt(level + 1).Settlers > rules.HallSettlers;
                    return string.Format(L(raising ? "$vc_newcomer_hall_full" : "$vc_newcomer_full"), rules.HallSettlers);
                case NewcomerWait.Bed: return L("$vc_newcomer_needs_bed");
                case NewcomerWait.Food: return string.Format(L("$vc_newcomer_needs_food"), rules.FoodEach);
                case NewcomerWait.Tomorrow: return L("$vc_newcomer_tomorrow");
                case NewcomerWait.Morning: return L("$vc_newcomer_morning");
                case NewcomerWait.Mood: return L("$vc_newcomer_unhappy");
                default: return L("$vc_newcomer_soon");
            }
        }

        /// <summary>
        /// The storehouse full, or couriers holding loads they can't put away (1.5.0, the review round's pick): said while
        /// couriers carry for the colony, with what to do. The storehouse's flag and the roster's statuses are on every computer.
        /// </summary>
        internal static string StorehouseLine(TownHallModule townHall, string colony)
        {
            ColonyBuilding storehouse = ColonyBuilding.All.FirstOrDefault(b => b != null && b.IsValid && b.IsStorehouse && b.ColonyId == colony);
            ZNetView view = storehouse != null ? storehouse.GetComponent<ZNetView>() : null;
            bool full = view != null && view.IsValid() && view.GetZDO().GetBool(SaveKeys.StoreFull);
            IReadOnlyList<RosterEntry> entries = townHall.Roster.Entries;
            int couriers = entries.Count(e => e.Job == SaveKeys.CourierJob);
            int holding = entries.Count(e => e.Job == SaveKeys.CourierJob && e.Status == "$vc_status_courier_holding");
            switch (StorehouseWarnings.For(storehouse != null, full, couriers, holding))
            {
                case StorehouseWarning.Full:
                    string held = holding == 0 ? "" : " " + (holding == 1 ? L("$vc_store_full_holding_one") : string.Format(L("$vc_store_full_holding"), holding));
                    return $"<color=orange>{L("$vc_store_full")}{held}</color>";
                case StorehouseWarning.Holding:
                    return $"<color=orange>{(holding == 1 ? L("$vc_store_holding_one") : string.Format(L("$vc_store_holding"), holding))}</color>";
                default:
                    return "";
            }
        }

        private static string WorkplaceName(string buildingId)
        {
            if (string.IsNullOrEmpty(buildingId)) return "";
            ColonyBuilding building = ColonyBuilding.FindById(buildingId);
            return building != null && building.Type != null
                ? $"{L(building.Type.NameToken)} {string.Format(L("$vc_level_short"), building.Level)}"
                : L("$vc_far_away");
        }
    }
}
