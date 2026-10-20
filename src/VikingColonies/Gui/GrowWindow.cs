using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Core;
using VikingColonies.Jobs;

namespace VikingColonies.Gui
{
    /// <summary>The Farm's "What to grow" list: a tick per crop with its seed, greyed where it doesn't grow (0.12).</summary>
    internal sealed class GrowWindow : TickListWindow
    {
        protected override string Title => "$vc_grow_title";
        protected override float Width => 540f;
        protected override string HintToken => "$vc_grow_hint";
        protected override float HintHeight => 66f;
        protected override float LineHeight => 30f;

        protected override List<TickRow> Rows()
        {
            bool mayManage = ColonyPermissions.LocalMayManage(Building);
            return CropList.For(Building).Select(line =>
            {
                // A bush's price shows: "seed: 5 Raspberries" (review of 0.19).
                string seed = string.Format(L("$vc_grow_seed"), (line.Crop.SeedCount > 1 ? line.Crop.SeedCount + " " : "") + ItemName(line.Crop.Seed));
                string why = line.Offered ? "" : $" — <color=orange>{L("$vc_grow_not_here")}</color>";
                return new TickRow
                {
                    Key = line.Crop.Piece, Ticked = line.Ticked, Enabled = line.Offered && mayManage,
                    Text = $"{L(line.Crop.NameToken)} <color=#bbbbbb>({seed})</color>{why}",
                };
            }).ToList();
        }

        protected override void SetTicked(string crop, bool on)
        {
            WorkerModule worker = Building != null ? Building.GetComponent<WorkerModule>() : null;
            if (worker != null) worker.RequestSetGrowTicked(crop, on);
        }

        internal static string ItemName(string item)
        {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(item) : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return drop != null ? L(drop.m_itemData.m_shared.m_name) : item;
        }
    }
}
