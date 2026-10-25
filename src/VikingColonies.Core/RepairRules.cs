using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>
    /// Repair (0.13): a builder with nothing to build mends his colony's damaged pieces, and a finished hut whose pieces
    /// are gone has its order reopened as a repair so a builder puts them back.
    /// </summary>
    public static class RepairRules
    {
        /// <summary>A piece below this share of its health is mended.</summary>
        public const float MendBelow = 0.75f;
        /// <summary>How often an idle builder looks for damaged pieces.</summary>
        public const float LookSeconds = 10f;
        /// <summary>How often the computer that owns a finished hut checks its plan against what stands.</summary>
        public const float CheckSeconds = 60f;
        /// <summary>Monsters this close to a hut hold its rebuilding off.</summary>
        public const float MonsterRange = 30f;

        /// <summary>Damaged enough to mend (a piece at no health is gone, not damaged).</summary>
        public static bool NeedsMending(float healthShare) => healthShare > 0f && healthShare < MendBelow;

        /// <summary>
        /// A finished order's pieces that were up and stand no more. Skipped pieces never stood and aren't asked for
        /// again; an order still under way is the builder's to see to.
        /// </summary>
        public static List<int> Missing(BuildOrderRecord order, Func<int, bool> stands)
        {
            if (order == null || order.State != BuildState.Done) return new List<int>();
            return order.Done.Where(i => !stands(i)).ToList();
        }

        /// <summary>Gone pieces are put back with the setting on, and not during a raid or with monsters by the hut.</summary>
        public static bool MayRebuild(bool settingOn, bool raid, bool monstersNear) => settingOn && !raid && !monstersNear;

        /// <summary>A builder who mended at least this many pieces of one building in a go has it told in the Saga (1.5.0).</summary>
        public const int SagaMendsAtLeast = 10;

        /// <summary>A big repair (a troll's or a fire's work), not the daily wear the rain does: told in the Saga.</summary>
        public static bool SagaWorthy(int mended) => mended >= SagaMendsAtLeast;

        /// <summary>
        /// Rain wears uncovered wood down to half its health, never below: a piece mended from below half was damaged by
        /// something (a troll, a fire), and only those count for the Saga (its review: rain alone filled it with lines).
        /// </summary>
        public static bool CountsForSaga(float healthBefore) => healthBefore < 0.49f;

        /// <summary>
        /// The next piece to mend (1.5.0, review 9): the building being mended is finished first (no back and forth); else the
        /// building whose weakest piece is in the worst state; within the building, the nearest piece. -1 for none.
        /// </summary>
        public static int MendNext(IReadOnlyList<Vec3> pieces, IReadOnlyList<float> health, IReadOnlyList<string> sites, Vec3 from, string current)
        {
            string site = null;
            if (!string.IsNullOrEmpty(current))
                for (int i = 0; i < pieces.Count && site == null; i++)
                    if (sites[i] == current) site = current;
            if (site == null)
            {
                float worst = float.MaxValue;
                for (int i = 0; i < pieces.Count; i++)
                {
                    if (health[i] >= worst) continue;
                    worst = health[i];
                    site = sites[i];
                }
            }
            if (site == null) return -1;
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < pieces.Count; i++)
            {
                if (sites[i] != site) continue;
                float distance = Vec3.Distance(pieces[i], from);
                if (distance >= bestDistance) continue;
                best = i;
                bestDistance = distance;
            }
            return best;
        }

        /// <summary>The index of the nearest of <paramref name="pieces"/>; -1 for none.</summary>
        public static int Nearest(IReadOnlyList<Vec3> pieces, Vec3 from)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < pieces.Count; i++)
            {
                float distance = Vec3.Distance(pieces[i], from);
                if (distance >= bestDistance) continue;
                best = i;
                bestDistance = distance;
            }
            return best;
        }
    }

    /// <summary>The word for an order's kind on the Town Hall's Work orders tab.</summary>
    public static class OrderWords
    {
        /// <summary>"new", "level {0}" or "repair", as a translation token (after the hut's name and a comma: no brackets).</summary>
        public static string KindToken(OrderKind kind)
        {
            switch (kind)
            {
                case OrderKind.Upgrade: return "$vc_order_level";
                case OrderKind.Repair: return "$vc_order_repair";
                case OrderKind.Remove: return "$vc_order_take_down";
                default: return "$vc_order_new";
            }
        }

        private static readonly System.Text.RegularExpressions.Regex s_materials =
            new System.Text.RegularExpressions.Regex(@"^\d+ [^,\s]+(, \d+ [^,\s]+)*$");

        /// <summary>
        /// The need an order last said is one of materials ("12 $item_wood, 4 $item_stone"), not a tool or a tree (1.3.1:
        /// set aside for wood, it went on saying so after the player had brought some).
        /// </summary>
        public static bool IsMaterials(string needs) => !string.IsNullOrEmpty(needs) && s_materials.IsMatch(needs);
    }
}
