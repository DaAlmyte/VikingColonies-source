using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Core;

namespace VikingColonies.Construction
{
    /// <summary>The line under the crosshair while placing a hut: green, yellow or red, and why.</summary>
    internal static class SiteText
    {
        public static string Describe(SiteVerdict verdict, float offset)
        {
            string text;
            switch (verdict.Colour)
            {
                case SiteColour.Red:
                    text = Colour("#ff6b6b", verdict.Red == RedReason.OwnPiece && verdict.OwnPiece.Length > 0 ? OwnPieceLine(verdict) : L(RedToken(verdict.Red)));
                    break;
                case SiteColour.Yellow:
                    text = Colour("#ffd75e", Work(verdict));
                    string above = AboveLevel(verdict);
                    if (above.Length > 0) text += "\n" + Colour("orange", above);
                    break;
                default:
                    text = Colour("#8fe388", L("$vc_site_ready"));
                    break;
            }
            if (Mathf.Abs(offset) < 0.001f) return text;
            string height = string.Format(L(offset > 0f ? "$vc_site_raised" : "$vc_site_lowered"), Mathf.Abs(offset).ToString("0.00"));
            return height + "\n" + text;
        }

        /// <summary>"2 trees, 1 rock, flatten the ground (about 12 stone): the builder needs an axe, a pickaxe and a hoe …".</summary>
        private static string Work(SiteVerdict verdict)
        {
            List<string> parts = verdict.Clear.Select(c => string.Format(L(KindToken(c.Key, c.Value)), c.Value)).ToList();
            if (verdict.Flatten) parts.Add(verdict.Stone > 0 ? string.Format(L("$vc_site_flatten_stone"), verdict.Stone) : L("$vc_site_flatten"));
            string what = string.Join(", ", parts);
            if (verdict.Tools.Count == 0) return string.Format(L("$vc_site_builder_clears"), what);
            return string.Format(L("$vc_site_needs_tools"), what, Join(verdict.Tools.Select(t => L(BuilderTools.ToolToken(t))).ToList()));
        }

        /// <summary>
        /// "1 tree needs a level 2 Builder's Hut — or clear it yourself": what on the site needs a better tool than the
        /// colony's best Builder's Hut allows; "" when nothing does.
        /// </summary>
        public static string AboveLevel(SiteVerdict verdict)
        {
            if (verdict?.AboveLevel == null || verdict.AboveLevel.Count == 0) return "";
            string what = string.Join(", ", verdict.AboveLevel.Select(c => string.Format(L(KindToken(c.Key, c.Value)), c.Value)));
            bool many = verdict.AboveLevel.Sum(c => c.Value) > 1;
            if (verdict.NeedsLevel <= 0) return string.Format(L(many ? "$vc_site_no_level_many" : "$vc_site_no_level"), what);
            return string.Format(L(many ? "$vc_site_above_level_many" : "$vc_site_above_level"), what, verdict.NeedsLevel);
        }

        /// <summary>
        /// "Your Bench is where the new level's Wood wall goes: move it", "Empty your Chest first": the player's piece an
        /// upgrade waits for (1.6), in each player's own language.
        /// </summary>
        public static string OwnPieceLine(SiteVerdict verdict)
        {
            string piece = L(verdict.OwnPiece);
            switch (verdict.OwnPieceWhy)
            {
                case OwnPieceWhy.InTheWay: return string.Format(L("$vc_site_own_in_the_way"), piece, L(verdict.OwnPieceNewPiece));
                case OwnPieceWhy.Full: return string.Format(L("$vc_site_own_full"), piece);
                case OwnPieceWhy.HasItem: return string.Format(L("$vc_site_own_has_item"), piece);
                case OwnPieceWhy.PlayersBed: return string.Format(L("$vc_site_own_bed"), piece);
                case OwnPieceWhy.KeepsWhat: return string.Format(L("$vc_site_own_keeps"), piece);
                default: return string.Format(L("$vc_site_own_unheld"), piece);
            }
        }

        /// <summary>"The builder puts back 2 of your pieces after": an upgrade's put-backs (1.6); "" when there are none.</summary>
        public static string PutBackLine(SiteVerdict verdict)
        {
            if (verdict == null || verdict.PutBacks <= 0) return "";
            return verdict.PutBacks == 1 ? L("$vc_site_put_back_one") : string.Format(L("$vc_site_put_back_many"), verdict.PutBacks);
        }

        /// <summary>"By level 5 it grows about 12 m toward you: leave room there" (GrowRoom), in grey under the site's line.</summary>
        public static string GrowsBy(GrowRoom.Growth seen)
        {
            string[] tokens = { "$vc_site_grow_far", "$vc_site_grow_right", "$vc_site_grow_near", "$vc_site_grow_left" };
            var parts = new List<string>();
            for (int side = 0; side < 4; side++)
                if (seen.Sides[side] >= GrowRoom.Worth) parts.Add(string.Format(L(tokens[side]), Mathf.Round(seen.Sides[side]).ToString("0")));
            return Colour("#c8c8c8", string.Format(L("$vc_site_grows"), seen.Level, Join(parts)));
        }

        /// <summary>"an axe", "an axe and a hoe", "an axe, a pickaxe and a hoe".</summary>
        private static string Join(IReadOnlyList<string> items)
        {
            if (items.Count <= 1) return items.FirstOrDefault() ?? "";
            return string.Join(", ", items.Take(items.Count - 1)) + " " + L("$vc_and") + " " + items[items.Count - 1];
        }

        private static string KindToken(ClearKind kind, int count)
        {
            string one;
            switch (kind)
            {
                case ClearKind.Item: one = "item"; break;
                case ClearKind.Plant: one = "plant"; break;
                case ClearKind.Tree: one = "tree"; break;
                case ClearKind.Log: one = "log"; break;
                case ClearKind.Stump: one = "stump"; break;
                case ClearKind.Rock: one = "rock"; break;
                default: one = "leftover"; break;
            }
            return "$vc_site_" + one + (count == 1 ? "" : "s");
        }

        public static string RedToken(RedReason reason)
        {
            switch (reason)
            {
                case RedReason.Water: return "$vc_site_red_water";
                case RedReason.NoBuild: return "$vc_site_red_nobuild";
                case RedReason.Ward: return "$vc_site_red_ward";
                case RedReason.OutsideColony: return "$vc_site_red_colony";
                case RedReason.NotYourColony: return "$vc_site_red_not_yours";
                case RedReason.OtherBuilding: return "$vc_site_red_building";
                case RedReason.OtherSite: return "$vc_site_red_site";
                case RedReason.Grave: return "$vc_site_red_grave";
                case RedReason.Unbreakable: return "$vc_site_red_unbreakable";
                case RedReason.TooSteep: return "$vc_site_red_steep";
                case RedReason.TooHigh: return "$vc_site_red_high";
                case RedReason.OnlyOneStorehouse: return "$vc_site_red_one_storehouse";
                case RedReason.NeedsBuilderHut: return "$vc_site_red_needs_builder";
                case RedReason.OnlyOneMeadHall: return "$vc_site_red_one_meadhall";
                case RedReason.OwnPiece: return "$vc_site_red_own_piece";
                default: return "$vc_site_red_float";
            }
        }

        private static string Colour(string hex, string text) => $"<color={hex}>{text}</color>";

        private static string L(string token) => Localization.instance.Localize(token);
    }
}
