using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using VikingColonies.Buildings;
using VikingColonies.Colony;
using VikingColonies.Construction;
using VikingColonies.Core;
using VikingColonies.Jobs;

namespace VikingColonies.Gui
{
    /// <summary>
    /// A field marker's window (worker polish, 0.20): its crop, its size, the Farm that works it and how its plots stand,
    /// with Crop…, − / + and Assign to a Farm / Leave it. Greyed for players who may not manage the colony.
    /// </summary>
    internal sealed class FieldWindow : WindowBase
    {
        private Text _info;
        private Text _message;
        private Button _crop, _smaller, _bigger, _assign, _leave;

        protected override string Title => "$vc_field_marker";
        protected override float Width => 460f;
        protected override float Height => 400f;

        private FieldMarker Marker => Target as FieldMarker;

        protected override void Build(Transform root)
        {
            _info = UiKit.Label(root, "", -70f, Width - 60f, 170f);
            _message = UiKit.Fitted(UiKit.Label(root, "", -250f, Width - 60f, 26f, 16, false, TextAnchor.MiddleCenter));
            _crop = UiKit.Button(root, L("$vc_field_crop_button"), -110f, 130f, 180f, 36f, OnCrop);
            _smaller = UiKit.Button(root, "−", 45f, 130f, 60f, 36f, () => Marker?.RequestSetSize(FieldSizes.Smaller(Marker.Size)));
            _bigger = UiKit.Button(root, "+", 115f, 130f, 60f, 36f, () => Marker?.RequestSetSize(FieldSizes.Bigger(Marker.Size)));
            _assign = UiKit.Button(root, L("$vc_field_assign"), 0f, 80f, 220f, 36f, OnAssign);
            _leave = UiKit.Button(root, L("$vc_field_leave"), 0f, 80f, 220f, 36f, () => Marker?.RequestAssign(""));
        }

        protected override void Opened() => _message.text = "";

        /// <summary>How a request went (the marker's computer answers).</summary>
        internal void ShowMessage(string text) => _message.text = text;

        protected override void Refresh()
        {
            FieldMarker marker = Marker;
            if (marker == null) return;
            bool mayManage = marker.LocalMayManage();
            ColonyBuilding farm = ColonyBuilding.FindById(marker.FarmId);
            List<Vector3> plots = marker.PlotPositions();
            string farmLine;
            if (marker.ColonyId.Length == 0) farmLine = $"<color=orange>{L("$vc_field_not_in_colony")}</color>";
            else if (farm != null)
                farmLine = string.Format(L("$vc_field_farm_value"), FieldMarker.FarmLabel(farm), Utils.DistanceXZ(farm.transform.position, marker.transform.position).ToString("0"));
            else if (FieldMarker.FarmsOf(marker.ColonyId).Count == 0) farmLine = $"<color=orange>{L("$vc_field_no_farm_none")}</color>";
            else farmLine = $"<color=orange>{L("$vc_field_no_farm_full")}</color>";
            CountPlots(plots, out int growing, out int ripe, out int bare);
            _info.text = $"<b>{L("$vc_field_line_crop")}:</b> {marker.CropName()}\n" +
                         $"<b>{L("$vc_field_line_size")}:</b> {string.Format(L("$vc_field_size_value"), marker.Size, plots.Count)}\n" +
                         $"<b>{L("$vc_field_line_farm")}:</b> {farmLine}\n" +
                         $"<b>{L("$vc_field_line_plots")}:</b> {string.Format(L("$vc_field_plots_value"), growing, ripe, bare)}";
            foreach (Button button in new[] { _crop, _smaller, _bigger, _assign, _leave }) button.interactable = mayManage;
            _smaller.interactable = mayManage && marker.Size > FieldSizes.All[0];
            _bigger.interactable = mayManage && marker.Size < FieldSizes.All[FieldSizes.All.Length - 1];
            _assign.gameObject.SetActive(marker.FarmId.Length == 0);
            _leave.gameObject.SetActive(marker.FarmId.Length > 0);
        }

        /// <summary>Plots with a growing plant, with a ripe crop, and bare (nothing on them).</summary>
        private static void CountPlots(List<Vector3> plots, out int growing, out int ripe, out int bare)
        {
            growing = ripe = 0;
            float near = FieldPlots.OnPlotTolerance * 2f;
            var taken = new HashSet<int>();
            foreach (Plant plant in WorldScan.Plants())
            {
                if (plant == null) continue;
                int i = plots.FindIndex(p => Utils.DistanceXZ(p, plant.transform.position) <= near);
                if (i >= 0 && taken.Add(i)) growing++;
            }
            foreach (Pickable pick in WorldScan.Pickables())
            {
                if (pick == null) continue;
                int i = plots.FindIndex(p => Utils.DistanceXZ(p, pick.transform.position) <= near);
                if (i < 0 || !taken.Add(i)) continue;
                if (pick.CanBePicked()) ripe++;
                else growing++;
            }
            bare = plots.Count - taken.Count;
        }

        private void OnCrop()
        {
            FieldMarker marker = Marker;
            Close();
            if (marker != null) Windows.FieldCrop.OpenOn(marker);
        }

        private void OnAssign()
        {
            FieldMarker marker = Marker;
            if (marker == null) return;
            string farm = FieldAssignment.Choose(FieldMarker.FarmsOf(marker.ColonyId, marker), CoreVectors.V(marker.transform.position));
            if (farm.Length == 0) _message.text = L("$vc_field_no_farm_room");
            else marker.RequestAssign(farm);
        }
    }

    /// <summary>A field's crop list (0.20): the Farm's list, or one crop; greyed where it doesn't grow.</summary>
    internal sealed class FieldCropWindow : TickListWindow
    {
        private const string FarmsList = "*";   // the tick list ignores empty keys

        protected override string Title => "$vc_field_crop_title";
        protected override float Width => 540f;
        protected override string HintToken => "$vc_field_crop_hint";
        protected override float HintHeight => 46f;
        protected override float LineHeight => 30f;

        private FieldMarker Marker => Target as FieldMarker;

        protected override List<TickRow> Rows()
        {
            FieldMarker marker = Marker;
            if (marker == null) return new List<TickRow>();
            bool mayManage = marker.LocalMayManage();
            int biome = WorldGenerator.instance != null ? (int)WorldGenerator.instance.GetBiome(marker.transform.position) : 0;
            return FieldCrops.Lines(CropList.Crops, biome, marker.Crop).Select(line =>
            {
                string text;
                if (line.Crop == null) text = L("$vc_field_farms_list");
                else
                {
                    string seed = string.Format(L("$vc_grow_seed"), (line.Crop.SeedCount > 1 ? line.Crop.SeedCount + " " : "") + GrowWindow.ItemName(line.Crop.Seed));
                    string why = line.Grows ? "" : $" — <color=orange>{L("$vc_grow_not_here")}</color>";
                    text = $"{L(line.Crop.NameToken)} <color=#bbbbbb>({seed})</color>{why}";
                }
                return new TickRow
                {
                    Key = line.Key.Length == 0 ? FarmsList : line.Key, Ticked = line.Chosen,
                    Enabled = mayManage && line.Grows && !line.Chosen, Text = text,
                };
            }).ToList();
        }

        protected override void SetTicked(string key, bool on)
        {
            if (on) Marker?.RequestSetCrop(key == FarmsList ? "" : key);
        }
    }
}
