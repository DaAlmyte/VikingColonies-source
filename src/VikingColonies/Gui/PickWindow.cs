using System.Collections.Generic;
using System.Linq;
using VikingColonies.Buildings;
using VikingColonies.Core;
using VikingColonies.Jobs;

namespace VikingColonies.Gui
{
    /// <summary>The Forager's Hut's "What to pick" list: a tick box per wild kind, with where it grows and whether it's food.</summary>
    internal sealed class PickWindow : TickListWindow
    {
        protected override string Title => "$vc_pick_title";
        protected override float Width => 520f;
        protected override string HintToken => "$vc_pick_hint";
        protected override float HintHeight => 56f;
        protected override float LineHeight => 30f;

        protected override List<TickRow> Rows()
        {
            bool mayManage = ColonyPermissions.LocalMayManage(Building);
            return ForageList.For(Building).Select(line =>
            {
                string where = line.Kind.Biomes != 0 ? BiomeText.Names(line.Kind.Biomes) : L("$vc_pick_spots");
                string food = line.Kind.IsFood ? ", " + L("$vc_pick_food") : "";
                return new TickRow
                {
                    Key = line.Kind.Item, Ticked = line.Ticked, Enabled = mayManage,
                    Text = $"{L(line.Kind.Name)} <color=#bbbbbb>— {where}{food}</color>",
                };
            }).ToList();
        }

        protected override void SetTicked(string item, bool on)
        {
            WorkerModule worker = Building != null ? Building.GetComponent<WorkerModule>() : null;
            if (worker != null) worker.RequestSetPickTicked(item, on);
        }
    }
}
