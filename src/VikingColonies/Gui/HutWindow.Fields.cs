using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using VikingColonies.Buildings;
using VikingColonies.Colony;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.Gui
{
    /// <summary>
    /// A Farm's Fields tab (worker polish, 0.20; MineColonies' Farm "Fields" tab): how many fields it works of how many it
    /// has room for (one per level) and whether new fields join it by themselves, then every field of the colony, nearest
    /// first, with Assign / Leave.
    /// </summary>
    internal sealed partial class HutWindow
    {
        private const int FieldRows = 7;
        private const float FieldRowsTop = -210f, FieldRowStep = 34f;

        private Text _fieldsHeader, _fieldsMessage, _fieldsPage;
        private Toggle _autoToggle;
        private readonly Text[] _fieldLabels = new Text[FieldRows];
        private readonly Button[] _fieldButtons = new Button[FieldRows];
        private readonly string[] _fieldIds = new string[FieldRows];
        private Button _fieldsPrev, _fieldsNext;
        private int _fieldsPageIndex;

        private void BuildFields(Transform root)
        {
            _fieldsHeader = UiKit.Label(root, "", -105f, Width - 60f, 28f, 20);
            _autoToggle = UiKit.Toggle(root, 30f, -138f, on => Fields()?.RequestSetAutoAssign(on));
            Text autoLabel = UiKit.Label(root, L("$vc_fields_auto"), -140f, Width - 130f, 28f, 16);
            _fieldsControls.AddRange(new[] { _fieldsHeader.gameObject, _autoToggle.gameObject, autoLabel.gameObject });
            for (int i = 0; i < FieldRows; i++)
            {
                int row = i;
                float y = FieldRowsTop - FieldRowStep * i;
                _fieldLabels[i] = UiKit.Label(root, "", y - 2f, Width - 150f, 30f, 15);
                RectTransform rect = _fieldLabels[i].GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x - 45f, rect.anchoredPosition.y);
                _fieldButtons[i] = UiKit.TopButton(root, "", 170f, y, 90f, 30f, () => OnFieldButton(row));
                _fieldsControls.Add(_fieldLabels[i].gameObject);
                _fieldsControls.Add(_fieldButtons[i].gameObject);
            }
            _fieldsMessage = UiKit.Fitted(UiKit.Label(root, "", -452f, Width - 60f, 24f, 15, false, TextAnchor.MiddleCenter));
            _fieldsPrev = UiKit.Button(root, L("$vc_prev"), -140f, 70f, 110f, 32f, () => TurnFields(-1));
            _fieldsNext = UiKit.Button(root, L("$vc_next"), 140f, 70f, 110f, 32f, () => TurnFields(1));
            _fieldsPage = UiKit.Label(root, "", -(Height - 86f), 120f, 30f, 16, align: TextAnchor.MiddleCenter);
            _fieldsControls.AddRange(new[] { _fieldsMessage.gameObject, _fieldsPrev.gameObject, _fieldsNext.gameObject, _fieldsPage.gameObject });
        }

        private FieldsModule Fields() => Building != null ? Building.GetComponent<FieldsModule>() : null;

        private void TurnFields(int by)
        {
            _fieldsPageIndex += by;
            Refresh();
        }

        private void RefreshFields()
        {
            ColonyBuilding farm = Building;
            FieldsModule module = Fields();
            if (farm == null || module == null) return;
            bool mayManage = ColonyPermissions.LocalMayManage(farm);
            ConstructionModule site = farm.GetComponent<ConstructionModule>();
            bool finished = site == null || FieldAssignment.Works(site.IsBuilt, site.IsUpgrading);   // an upgrading Farm works on (final review of 0.20)
            List<FieldMarker> mine = module.Fields();
            _fieldsHeader.text = string.Format(L("$vc_fields_count"), mine.Count, module.Capacity);
            _autoToggle.SetIsOnWithoutNotify(module.AutoAssign);
            _autoToggle.interactable = mayManage;

            List<FieldMarker> fields = FieldMarker.All
                .Where(m => m != null && m.IsValid && farm.ColonyId.Length > 0 && m.ColonyId == farm.ColonyId)
                .OrderBy(m => Utils.DistanceXZ(m.transform.position, farm.transform.position)).ToList();
            int pages = ListPage.Pages(fields.Count, FieldRows);
            _fieldsPageIndex = ListPage.Clamp(_fieldsPageIndex, fields.Count, FieldRows);
            IReadOnlyList<FieldMarker> shown = ListPage.Slice(fields, _fieldsPageIndex, FieldRows);
            for (int i = 0; i < FieldRows; i++)
            {
                bool on = i < shown.Count;
                _fieldIds[i] = on ? shown[i].Id : "";
                _fieldButtons[i].gameObject.SetActive(on);
                _fieldLabels[i].gameObject.SetActive(on || (i == 0 && fields.Count == 0));
                if (!on)
                {
                    if (i == 0 && fields.Count == 0) _fieldLabels[0].text = L("$vc_fields_none");
                    continue;
                }
                FieldMarker m = shown[i];
                bool ours = m.FarmId == farm.Id;
                string owner = ours ? L("$vc_fields_this_farm") : m.FarmId.Length == 0 ? L("$vc_field_no_farm_short")
                    : ColonyBuilding.FindById(m.FarmId) is ColonyBuilding other ? FieldMarker.FarmLabel(other) : L("$vc_fields_other_farm");
                float distance = Utils.DistanceXZ(m.transform.position, farm.transform.position);
                _fieldLabels[i].text = $"{m.CropName()} {m.Size}×{m.Size}, {distance:0} m — {owner}";
                UiKit.SetLabel(_fieldButtons[i], L(ours ? "$vc_fields_leave" : "$vc_fields_assign"));
                _fieldButtons[i].interactable = mayManage && (ours || (finished && mine.Count < module.Capacity));
            }
            bool paged = pages > 1;
            _fieldsPrev.gameObject.SetActive(paged);
            _fieldsNext.gameObject.SetActive(paged);
            _fieldsPrev.interactable = _fieldsPageIndex > 0;
            _fieldsNext.interactable = _fieldsPageIndex < pages - 1;
            _fieldsPage.text = paged ? $"{_fieldsPageIndex + 1} / {pages}" : "";
        }

        private void OnFieldButton(int row)
        {
            ColonyBuilding farm = Building;
            FieldMarker marker = FieldMarker.All.FirstOrDefault(m => m != null && m.Id == _fieldIds[row]);
            if (farm == null || marker == null) return;
            marker.RequestAssign(marker.FarmId == farm.Id ? "" : farm.Id);
        }

        /// <summary>Test tool (vc_ui): shows a tab ("hut", "fields" or "stock").</summary>
        internal void ShowTabForTest(string tab) => Show(tab == "fields" ? Tab.Fields : tab == "stock" ? Tab.Stock : Tab.Hut);
    }
}
