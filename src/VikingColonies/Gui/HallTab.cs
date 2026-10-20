using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using VikingColonies.Buildings;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Gui
{
    /// <summary>
    /// The Town Hall's Hall tab (1.4, the Hall of Trophies): the hall's level and what it gives, what the next level needs
    /// (the trophies of the Forsaken met since the level before, materials in the chest) and gives, Raise, and the trophies
    /// shown in the hall; the Town Hall's camp and Build the camp (huts round 2, when the mod has a design for the camp).
    /// </summary>
    internal sealed class HallTab
    {
        private readonly Func<ColonyBuilding> _building;
        private readonly List<GameObject> _controls = new List<GameObject>();
        private Text _header, _perks, _needs, _gives, _trophies, _camp, _message;
        private Button _raise, _startCamp;

        public HallTab(Func<ColonyBuilding> building) { _building = building; }

        public IReadOnlyList<GameObject> Controls => _controls;

        public void Build(Transform root, float width)
        {
            _controls.Clear();   // built again after a scene change
            _header = UiKit.Label(root, "", -105f, width - 60f, 28f, 20);
            _perks = UiKit.Label(root, "", -137f, width - 60f, 28f, 16);
            _needs = UiKit.Label(root, "", -180f, width - 60f, 170f, 16);
            _needs.supportRichText = true;
            _needs.resizeTextForBestFit = true;   // a long need line (an item the game doesn't know) shrinks rather than runs off
            _needs.resizeTextMinSize = 12;
            _needs.resizeTextMaxSize = 16;
            _gives = UiKit.Label(root, "", -360f, width - 60f, 50f, 16);
            _trophies = UiKit.Label(root, "", -420f, width - 60f, 50f, 15);
            _message = UiKit.Fitted(UiKit.Label(root, "", -480f, width - 60f, 28f, 16, false, TextAnchor.MiddleCenter));
            _camp = UiKit.Label(root, "", -512f, width - 60f, 44f, 15);
            _camp.resizeTextForBestFit = true;
            _camp.resizeTextMinSize = 12;
            _camp.resizeTextMaxSize = 15;
            _raise = UiKit.Button(root, "", 0f, 75f, 240f, 36f, Raise);
            _startCamp = UiKit.Button(root, L("$vc_camp_start"), 0f, 118f, 240f, 36f, StartCamp);
            _controls.AddRange(new[] { _header.gameObject, _perks.gameObject, _needs.gameObject, _gives.gameObject, _trophies.gameObject,
                _camp.gameObject, _message.gameObject, _raise.gameObject, _startCamp.gameObject });
        }

        public void Reset()
        {
            if (_message != null) _message.text = "";
        }

        /// <summary>How the raise went (the Town Hall's owner answers).</summary>
        public void ShowMessage(string text)
        {
            if (_message != null) _message.text = text;
        }

        public void Refresh()
        {
            ColonyBuilding building = _building();
            TownHallModule hall = building != null ? building.GetComponent<TownHallModule>() : null;
            if (hall == null) return;
            int level = TownHallLevels.Clamp(building.Level);
            bool on = ColonyConfig.HallLevels.Value;
            _header.text = string.Format(L("$vc_hall_header"), level, TownHallLevels.MaxLevel);
            _perks.text = on ? Perks(HallRules.PerksAt(level)) : L("$vc_hall_off");
            Dictionary<string, TrophyPlace> places = hall.TrophyPlaces();   // one look at the stands and the chest per refresh
            _trophies.text = TrophiesText(places);
            Construction.ConstructionModule camp = building.GetComponent<Construction.ConstructionModule>();
            string campLine = camp != null ? camp.CampLine() : null;
            _camp.text = campLine ?? "";
            bool offerCamp = camp != null && camp.MayStartCamp && ColonyPermissions.LocalMayManage(building);
            _startCamp.gameObject.SetActive(offerCamp);
            if (!on || level >= TownHallLevels.MaxLevel)
            {
                _needs.text = on ? L("$vc_hall_top") : "";
                _gives.text = "";
                _raise.gameObject.SetActive(false);
                return;
            }
            int next = level + 1;
            _needs.text = string.Format(L("$vc_hall_needs"), next) + "\n" + NeedLines(hall, places, next);
            _gives.text = string.Format(L("$vc_hall_gives"), next, Perks(HallRules.PerksAt(next)));
            _raise.gameObject.SetActive(true);
            UiKit.SetLabel(_raise, string.Format(L("$vc_hall_raise"), next));
            _raise.interactable = ColonyPermissions.LocalMayManage(building) && hall.RaiseCheck(places) == RaiseLock.None;
        }

        /// <summary>Build the camp: its first level is ordered when its site is clear (the Town Hall's owner checks).</summary>
        private void StartCamp()
        {
            ColonyBuilding building = _building();
            Construction.ConstructionModule camp = building != null ? building.GetComponent<Construction.ConstructionModule>() : null;
            if (camp == null || !ColonyPermissions.LocalMayManage(building)) return;
            camp.RequestStartCamp();
        }

        private void Raise()
        {
            ColonyBuilding building = _building();
            TownHallModule hall = building != null ? building.GetComponent<TownHallModule>() : null;
            if (hall == null || !ColonyPermissions.LocalMayManage(building)) return;
            _message.text = L("$vc_hall_raising");
            _raise.interactable = false;   // one raise per click (review of 1.4: a double click raised two levels)
            hall.RequestRaise();
        }

        /// <summary>"colony area 60 m · up to 15 settlers · huts up to level 3"</summary>
        internal static string Perks(HallPerks perks) =>
            string.Format(L("$vc_hall_perks"), Mathf.RoundToInt(perks.Radius), perks.Settlers, perks.HutCap);

        private static string NeedLines(TownHallModule hall, Dictionary<string, TrophyPlace> places, int level)
        {
            var lines = new StringBuilder();
            foreach (string trophy in TownHallLevels.TrophiesFor(level))   // one or two Forsaken (review of 1.4)
            {
                string boss = L(TownHallLevels.BossToken(trophy));
                TrophyPlace place = TownHallModule.Where(places, trophy);
                if (lines.Length > 0) lines.Append('\n');
                lines.Append(place == TrophyPlace.None
                    ? $"<color=orange>{string.Format(L("$vc_hall_need_trophy_missing"), boss)}</color>"
                    : string.Format(L(place == TrophyPlace.Stand ? "$vc_hall_need_trophy_stand"
                        : place == TrophyPlace.Chest ? "$vc_hall_need_trophy_chest" : "$vc_hall_need_trophy_beaten"), boss));
            }
            Dictionary<string, int> contents = hall.ChestContents();
            foreach (string part in TownHallLevels.UnreadableParts(ColonyConfig.HallLevelCost(level)))
                lines.Append('\n').Append($"<color=orange>{string.Format(L("$vc_hall_need_unreadable"), part, level)}</color>");
            foreach (KeyValuePair<string, int> item in HallRules.Cost(level))
            {
                lines.Append('\n');
                GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(item.Key) : null;
                if (prefab == null)
                {
                    lines.Append($"<color=orange>{string.Format(L("$vc_hall_need_unknown"), item.Key, level)}</color>");
                    continue;
                }
                contents.TryGetValue(item.Key, out int have);
                string name = L(prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
                string line = string.Format(L("$vc_hall_need_item"), name, Math.Min(have, item.Value), item.Value);
                lines.Append(have >= item.Value ? line : $"<color=orange>{line}</color>");
            }
            return lines.ToString();
        }

        private static string TrophiesText(Dictionary<string, TrophyPlace> places)
        {
            // Only trophies actually in the hall (a boss beaten elsewhere counts for levels but isn't "shown").
            List<string> shown = TownHallLevels.Trophies.Where(t => TownHallModule.Where(places, t) == TrophyPlace.Stand || TownHallModule.Where(places, t) == TrophyPlace.Chest)
                .Select(t => L(TownHallLevels.BossToken(t))).ToList();
            return string.Format(L("$vc_hall_trophies"), shown.Count > 0 ? string.Join(", ", shown) : L("$vc_hall_trophies_none"));
        }

        private static string L(string token) => Localization.instance.Localize(token);
    }
}
