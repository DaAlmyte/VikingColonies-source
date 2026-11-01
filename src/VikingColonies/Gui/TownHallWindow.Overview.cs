using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Gui
{
    /// <summary>
    /// The camp's Overview (1.5.1, review 8's picture: every other colony mod opens on one): the colony at a glance. Since
    /// 1.6.0 it looks like the approved picture (`docs/mockups/town-hall-window.html`, the player's look at 1.5.1): a box per
    /// block with a gold heading, big numbers, a settlers' bar, green for what's well and orange for what needs you, and the
    /// boxes filling the window (<see cref="CampWindowLayout.Overview"/>). Settlers, food and mood across the top; the next
    /// newcomer, what the builders need (with Hand over) and the storehouse under them; Hugin's next step across the foot.
    /// </summary>
    internal sealed partial class TownHallWindow
    {
        // The picture's colours.
        private static readonly Color BoxFill = new Color(0f, 0f, 0f, 0.26f);
        private static readonly Color BoxEdge = new Color32(0x9a, 0x76, 0x48, 0xff);   // the picture's edge, lighter: #5b4330 vanished on the wood
        private static readonly Color Gold = new Color32(0xd9, 0xb2, 0x6a, 0xff);
        private static readonly Color BarBack = new Color32(0x20, 0x16, 0x0f, 0xe6);
        private const string Green = "#9ccc65", Dim = "#cbb791";
        private const float Pad = CampWindowLayout.BoxPad, EdgeWidth = 2f, BigHeight = 34f, BarHeight = 10f;

        /// <summary>One block of the Overview: its box, its heading, its big line (if it has one) and the lines under it.</summary>
        private sealed class OverviewBox
        {
            public GameObject Frame;
            public Text Head, Big, Body;
            public Image Bar, BarFill;
            public float BodyFoot;   // room kept at the box's foot (the builders' Hand over)

            public IEnumerable<GameObject> Parts()
            {
                yield return Frame;
                yield return Head.gameObject;
                if (Big != null) yield return Big.gameObject;
                yield return Body.gameObject;
                if (Bar != null) yield return Bar.gameObject;
            }

            public void SetShown(bool shown)
            {
                foreach (GameObject go in Parts()) go.SetActive(shown);
            }
        }

        private readonly List<GameObject> _overviewControls = new List<GameObject>();
        private Text _ovSubtitle;
        private OverviewBox _ovSettlers, _ovFood, _ovMood, _ovNewcomer, _ovBuilders, _ovStore, _ovSteps;
        private Button _ovHandOver;
        private int _ovShape = -1;   // which of the storehouse and Hugin's step the boxes are placed for

        private void BuildOverview(Transform root)
        {
            _overviewControls.Clear();
            _ovShape = -1;
            _ovSubtitle = UiKit.Label(root, "", CampWindowLayout.SubtitleTop, Width - 60f, 24f, 15, false, TextAnchor.MiddleCenter);
            _ovSettlers = Box(root, "$vc_overview_settlers", big: true, bar: true);
            _ovFood = Box(root, "$vc_overview_food", big: true);
            _ovMood = Box(root, "$vc_overview_mood", big: true);
            _ovNewcomer = Box(root, "$vc_overview_newcomer");
            _ovBuilders = Box(root, "$vc_overview_builders");
            _ovBuilders.BodyFoot = CampWindowLayout.HandOverRoom;
            _ovStore = Box(root, "$vc_overview_storehouse");
            _ovSteps = Box(root, "$vc_overview_steps");
            _ovHandOver = UiKit.TopButton(root, L("$vc_handover"), 0f, 0f, CampWindowLayout.HandOverWidth, CampWindowLayout.HandOverHeight, HandOverFromOverview);
            _overviewControls.Add(_ovSubtitle.gameObject);
            foreach (OverviewBox box in new[] { _ovSettlers, _ovFood, _ovMood, _ovNewcomer, _ovBuilders, _ovStore, _ovSteps })
                _overviewControls.AddRange(box.Parts());
            _overviewControls.Add(_ovHandOver.gameObject);
            PlaceOverview(false, false);
        }

        /// <summary>A block's box with its heading and text; placed later (<see cref="PlaceOverview"/>).</summary>
        private static OverviewBox Box(Transform root, string heading, bool big = false, bool bar = false)
        {
            var box = new OverviewBox();
            box.Frame = new GameObject("vc_overview_box", typeof(RectTransform));
            box.Frame.transform.SetParent(root, false);
            Image fill = box.Frame.AddComponent<Image>();
            fill.color = BoxFill;
            fill.raycastTarget = false;
            // The edge: four thin strips round the fill (an outline effect would tint the see-through fill).
            foreach (Vector4 side in new[] { new Vector4(0f, 0f, 1f, 0f), new Vector4(0f, 1f, 1f, 1f), new Vector4(0f, 0f, 0f, 1f), new Vector4(1f, 0f, 1f, 1f) })
            {
                var strip = new GameObject("edge", typeof(RectTransform));
                strip.transform.SetParent(box.Frame.transform, false);
                Image line = strip.AddComponent<Image>();
                line.color = BoxEdge;
                line.raycastTarget = false;
                RectTransform rect = strip.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(side.x, side.y);
                rect.anchorMax = new Vector2(side.z, side.w);
                rect.pivot = new Vector2(side.x, side.y);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(side.x == side.z ? EdgeWidth : 0f, side.y == side.w ? EdgeWidth : 0f);
            }

            box.Head = UiKit.Label(root, L(heading), 0f, 100f, 20f, 15);
            box.Head.color = Gold;
            UiKit.Fitted(box.Head);
            if (big)
            {
                box.Big = UiKit.Label(root, "", 0f, 100f, BigHeight, 26, false, TextAnchor.MiddleLeft);
                box.Big.resizeTextForBestFit = true;   // a long level word shrinks rather than wraps
                box.Big.resizeTextMinSize = 16;
                box.Big.resizeTextMaxSize = 26;
            }
            if (bar)
            {
                box.Bar = Strip(root, BarBack);
                box.BarFill = Strip(box.Bar.transform, Gold);
                RectTransform rect = box.BarFill.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
            }
            box.Body = UiKit.Label(root, "", 0f, 100f, 40f, 15);
            box.Body.resizeTextForBestFit = true;   // shrinks to fit its box rather than run out of it
            box.Body.resizeTextMinSize = 11;
            box.Body.resizeTextMaxSize = 15;
            return box;
        }

        private static Image Strip(Transform parent, Color color)
        {
            var go = new GameObject("vc_overview_bar", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Puts every box where the layout says, for the boxes shown (the storehouse's and Hugin's come and go).</summary>
        private void PlaceOverview(bool storehouse, bool steps)
        {
            int shape = (storehouse ? 1 : 0) + (steps ? 2 : 0);
            if (shape == _ovShape) return;
            _ovShape = shape;
            OverviewBoxes boxes = CampWindowLayout.Overview(storehouse, steps);
            Place(_ovSettlers, boxes.Settlers);
            Place(_ovFood, boxes.Food);
            Place(_ovMood, boxes.Mood);
            Place(_ovNewcomer, boxes.Newcomer);
            Place(_ovBuilders, boxes.Builders);
            Place(_ovStore, boxes.Storehouse);
            Place(_ovSteps, boxes.Steps);
            UiBox handOver = CampWindowLayout.HandOver(boxes.Builders);
            _ovHandOver.GetComponent<RectTransform>().anchoredPosition = new Vector2(handOver.X, handOver.Top);
        }

        private static void Place(OverviewBox box, UiBox b)
        {
            // A box not shown (no height) is hidden here as well as by the refresh: Show(Tab) turns every part on, and a
            // refresh that stops early mustn't leave its heading on top of another box's (the review).
            if (b.Height <= 0f) box.SetShown(false);
            float inner = b.Width - 2f * Pad, y = b.Top - 8f;
            Put(box.Frame.GetComponent<RectTransform>(), b.X, b.Top, b.Width, b.Height);
            Put(box.Head.rectTransform, b.X, y, inner, 20f);
            y -= 22f;
            if (box.Big != null)
            {
                Put(box.Big.rectTransform, b.X, y, inner, BigHeight);
                y -= BigHeight + 2f;
            }
            if (box.Bar != null)
            {
                Put(box.Bar.rectTransform, b.X, y - 2f, inner, BarHeight);
                y -= BarHeight + 8f;
            }
            Put(box.Body.rectTransform, b.X, y, inner, Mathf.Max(18f, y - (b.Bottom + Pad + box.BodyFoot)));
        }

        /// <summary>A part pinned by its top edge at <paramref name="x"/> across the window's centre, <paramref name="top"/> down.</summary>
        private static void Put(RectTransform rect, float x, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(x, top);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static string Colored(string color, string text) => $"<color={color}>{text}</color>";

        private void RefreshOverview(TownHallModule townHall)
        {
            ColonyBuilding building = townHall.GetComponent<ColonyBuilding>();
            string colony = building != null ? building.ColonyId : "";
            int today = EnvMan.instance != null && ZNet.instance != null ? EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) : 0;
            _ovSubtitle.text = string.Format(L("$vc_camp_subtitle"), building != null ? building.Level : 1, TownHallLevels.MaxLevel, today);
            ColonyNeeds needs = townHall.Needs();
            NewcomerRules rules = townHall.NewcomerRules;

            // Settlers: "9 of 20" big, a bar of how full the camp is, "2 beds free".
            int cap = System.Math.Min(rules.MaxSettlers, rules.HallSettlers);
            _ovSettlers.Big.text = string.Format(L("$vc_overview_settlers_line"), needs.Settlers, cap);
            _ovSettlers.BarFill.rectTransform.anchorMax = new Vector2(cap > 0 ? Mathf.Clamp01(needs.Settlers / (float)cap) : 0f, 1f);
            _ovSettlers.Body.text = needs.FreeBeds == 0 ? Colored("orange", L("$vc_overview_no_bed_free"))
                : Colored(Dim, needs.FreeBeds == 1 ? L("$vc_overview_one_bed_free") : string.Format(L("$vc_overview_beds_free"), needs.FreeBeds));

            // Food: what the camp's chest holds, big; what it keeps for newcomers; who is hungry (three names, then how many more).
            int keptFood = townHall.GetComponent<StockModule>()?.List.Kept(StockList.AnyFood) ?? 0;
            _ovFood.Big.text = needs.Food.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string food = Colored(Dim, L("$vc_overview_food_held"));
            if (keptFood > 0) food += "\n" + Colored(Dim, string.Format(L("$vc_food_kept_newcomers"), keptFood));
            List<string> hungry = Colony.Creative.SettlersEat(colony)
                ? Citizen.All.Where(c => c != null && c.Record.ColonyId == colony && c.Record.Hungry).Select(c => c.CitizenName).ToList()
                : new List<string>();
            IReadOnlyList<string> named = CampOverview.FirstNames(hungry, 3, out int more);
            string names = string.Join(", ", named) + (more > 0 ? " " + string.Format(L("$vc_overview_and_more"), more) : "");
            food += "\n" + (hungry.Count == 0 ? Colored(Green, L("$vc_overview_nobody_hungry"))
                : Colored("orange", string.Format(L("$vc_overview_hungry"), hungry.Count, names)));
            _ovFood.Body.text = food;

            // Mood: the colony's level and value, big (green when content or better); the reasons that weigh most, one a line,
            // those that take away in orange.
            if (!ColonyConfig.Mood.Value)
            {
                _ovMood.Big.text = Colored(Dim, L("$vc_overview_mood_off"));   // in the big line's place, no empty band above it
                _ovMood.Body.text = "";
            }
            else
            {
                string level = string.Format(L("$vc_overview_mood_line"), L(MoodLevels.Token(needs.Mood)), townHall.ColonyMoodValue());
                _ovMood.Big.text = Colored(needs.Mood < MoodLevel.Content ? "orange" : Green, level);
                IEnumerable<IReadOnlyList<MoodReason>> each = Citizen.All.Where(c => c != null && c.Record.ColonyId == colony)
                    .Select(c => MoodLevels.Decode(c.Record.WhyMood));
                _ovMood.Body.text = string.Join("\n", CampOverview.WeightiestReasons(each, 3)
                    .Select(r => r.Change < 0 ? Colored("orange", MoodLevels.ReasonText(r, L)) : MoodLevels.ReasonText(r, L)));
            }

            _ovNewcomer.Body.text = NewcomerWaitLine(townHall, needs);

            // What the builders need: the Work tab's line, worked out every couple of seconds (it looks in every chest).
            if (Time.time - _needsAt >= NeedsRefreshSeconds)
            {
                _needsAt = Time.time;
                _needsText = StillToBring(OpenOrders());
            }
            _ovBuilders.Body.text = _needsText.Length > 0 ? _needsText : Colored(Green, L("$vc_overview_nothing_to_bring"));
            _ovHandOver.interactable = _needsText.Length > 0 && _needsText != L("$vc_orders_needs_none");

            // The storehouse: only when it is full or couriers hold loads (1.5.0's line).
            string store = StorehouseLine(townHall, colony);
            _ovStore.Body.text = store;

            // Hugin's next step: the last step done and the next with Hugin's line; hidden with the raven hints off, and once
            // every step is done.
            StartFacts facts = building != null ? HuginTips.StepFacts(building) : null;
            StartStep next = facts != null ? GuidedStart.Next(facts) : StartStep.None;
            bool steps = ColonyConfig.HuginTips.Value && Raven.m_tutorialsEnabled && next != StartStep.None;
            PlaceOverview(store.Length > 0, steps);
            _ovStore.SetShown(store.Length > 0);
            _ovSteps.SetShown(steps);
            if (!steps) return;
            StartStep last = GuidedStart.LastDone(facts);
            string done = last != StartStep.None ? Colored(Green, string.Format(L("$vc_overview_step_done"), L(GuidedStart.Token(last)))) + "\n" : "";
            string line = GuidedStart.LineToken(next);
            _ovSteps.Body.text = $"{done}<b>{L(GuidedStart.Token(next))}</b>" + (line.Length > 0 ? ": " + L(line) : "");
        }

        /// <summary>Hand over on the Overview: what the first open order still needs, from your bag (as on the Work tab).</summary>
        private void HandOverFromOverview()
        {
            _chosenOrder = "";
            HandOverChosen();
        }
    }
}
