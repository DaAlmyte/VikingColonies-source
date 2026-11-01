using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.DevTools
{
    /// <summary>vc_ownpiecetest (test worlds): the player's own pieces through a hut's upgrade (1.6) checked in the game, with the real builder.</summary>
    internal sealed class OwnPieceTestCommand : ConsoleCommand
    {
        public override string Name => "vc_ownpiecetest";
        public override string Help => "VikingColonies (test worlds): place a Town Hall and a Builder's Hut here, put the player's own pieces in and on " +
                                       "the hut, and check its upgrade to level 2 by its builder keeps them (1.6). Run it at vc_speed 5. Removes what it made.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_ownpiecetest: test worlds only");
                return;
            }
            Plugin.Instance.StartCoroutine(OwnPieceTest.Run(p));
        }
    }

    /// <summary>
    /// The own-pieces design's game check (docs/superpowers/specs/2026-10-25-vikingcolonies-own-pieces-through-upgrades-design.md):
    /// a Builder's Hut at level 1, faced along the world's axes, and the player's pieces placed as a player would (creator set).
    /// Its level-2 design replaces the porch's and the corners' poles (a thin pole by a log one at the same spot), keeps
    /// every floor and wall, and builds a workbench on the ground beside it; the spots are found from the two plans. A bench
    /// and a chest of Stone on kept floors stay; a wall torch and an item stand on a replaced porch pole are taken down
    /// before it and put back after; an empty chest on the ground where the new workbench goes, then a trophy on the pole's
    /// item stand, make the upgrade wait, the window naming each.
    /// </summary>
    internal static class OwnPieceTest
    {
        private static int s_pass, s_fail;

        public static IEnumerator Run(Player player)
        {
            s_pass = s_fail = 0;
            Log.Event("vc_ownpiecetest: starting");
            if (!player.InGodMode()) player.SetGodMode(true);
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = 0.4f;   // morning: the builder works on
            Vector3 origin = player.transform.position;
            Quaternion facing = Quaternion.identity;   // the hut's frame is the world's: the test's boxes are the rule's
            long me = player.GetPlayerID();
            var made = new List<GameObject>();
            bool walkInWas = ColonyConfig.NewcomersWalkIn.Value;
            try
            {
                ColonyConfig.NewcomersWalkIn.Value = false;
                DevHelpers.ClearMonsters(origin, 80f, animalsToo: true);
                ColonyBuilding hall = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, origin + Vector3.back * 14f, facing, me);
                TownHallModule townHall = hall != null ? hall.GetComponent<TownHallModule>() : null;
                for (float t = 0f; townHall != null && !townHall.Founded && t < 20f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (hall == null || townHall == null || !townHall.Founded)
                {
                    Check($"A Town Hall is founded (hall {hall != null})", false);
                    yield break;
                }
                // Huts go as high as the camp: level 2. The camp's own order for its level is finished at once, so the hut's
                // upgrade is the builder's only work (the first run: at level 5 the camp's order took turns with it for the
                // materials, and the hut waited on "Needs 1 Resin").
                hall.SetLevel(2);
                ConstructionModule camp = hall.GetComponent<ConstructionModule>();
                for (float t = 0f; camp != null && t < 6f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (camp != null && camp.Order != null && camp.Order.State != BuildState.Done) yield return Finish(camp);
                Inventory hallChest = hall.GetComponent<Container>()?.GetInventory();
                // Tools a level-1 Builder's Hut may use (up to the Flint Axe and the Antler Pickaxe): iron ones are past its level, and on a
                // site with a bush or a stump the builder waited for an axe all along (29 Oct, the self-test in the meadow).
                foreach (KeyValuePair<string, int> tool in new Dictionary<string, int> { { "Hoe", 1 }, { "PickaxeAntler", 1 }, { "AxeFlint", 1 }, { "Stone", 100 } })
                    Give(hallChest, tool.Key, tool.Value);
                Vector3 hutAt = OnGround(origin + Vector3.forward * 10f);
                ColonyBuilding hut = DevHelpers.Place(BuildingTypes.BuilderHut.PrefabName, hutAt, facing, me);
                for (float t = 0f; hut != null && hut.ColonyId != hall.ColonyId && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                ConstructionModule site = hut != null ? hut.GetComponent<ConstructionModule>() : null;
                if (site == null)
                {
                    Check("A Builder's Hut is placed", false);
                    yield break;
                }
                yield return Finish(site);
                yield return new WaitForSeconds(2f);
                site.MendNow();   // built at once: a piece that fell is put back, or its repair holds the upgrade off
                Vector3 chestAt = hut.transform.position;

                // The two plans and what the upgrade does to each piece (the hut faced along the world's axes, so the planned
                // boxes are in its frame).
                IReadOnlyList<PlannedPiece> oldPlan = site.Plan;
                List<PlannedPiece> newPlan = site.NextLevelPlanForTest();
                if (newPlan == null)
                {
                    Check("The Builder's Hut has a level-2 design", false);
                    yield break;
                }
                DesignDiff diff = DesignDiff.Compare(oldPlan, newPlan);
                List<PieceBox> built = diff.Build.Select(i => newPlan[i]).Where(p => p.IsValid).Select(PiecePlacer.PlannedBox).ToList();
                List<PieceBox> oldSolid = oldPlan.Where(p => p.IsValid && !p.Prefab.Contains("floor")).Select(PiecePlacer.PlannedBox).ToList();
                PlannedPiece oldPole = Nearest(diff.TakeAway.Select(i => oldPlan[i]), "wood_pole2", chestAt + new Vector3(-1.2f, 1f, 3.65f));
                PlannedPiece newPole = Nearest(diff.Build.Select(i => newPlan[i]), "wood_pole_log", chestAt + new Vector3(-1.2f, 1f, 3.65f));
                PlannedPiece newBench = Nearest(diff.Build.Select(i => newPlan[i]), "piece_workbench", chestAt + new Vector3(-2.75f, 0f, 1.75f));
                PlannedPiece floor = Nearest(diff.Kept.Select(i => newPlan[i]), "wood_floor", chestAt + new Vector3(3f, 0f, 2.4f));
                PlannedPiece cornerFloor = Nearest(diff.Kept.Select(i => newPlan[i]), "wood_floor", chestAt + new Vector3(-1f, 0f, 0.4f));
                PlannedPiece cornerPole = Nearest(diff.TakeAway.Select(i => oldPlan[i]), "wood_pole2", chestAt + new Vector3(-2f, 1f, -0.6f));
                if (oldPole == null || newPole == null || newBench == null || floor == null || cornerFloor == null || cornerPole == null)
                {
                    Check($"The level-2 design replaces the porch pole and builds a workbench beside the hut (old pole {oldPole != null}, new pole " +
                          $"{newPole != null}, workbench {newBench != null}, floors {floor != null}/{cornerFloor != null}, corner pole {cornerPole != null})", false);
                    yield break;
                }
                PieceBox oldPoleBox = PiecePlacer.PlannedBox(oldPole), newPoleBox = PiecePlacer.PlannedBox(newPole);

                // The player's pieces that stay: a bench on a kept floor by the front wall, a chest of Stone in the back corner
                // on a kept floor, against the corner's replaced pole.
                Quaternion benchTurn = Quaternion.identity;
                Vector3 benchAt = OnTop(floor, "piece_bench01", chestAt + new Vector3(2.6f, 0f, 2.9f), benchTurn);
                if (StorehouseRules.InTheWayOf(PiecePlacer.PlannedBox(new PlannedPiece(0, "piece_bench01", V(benchAt), Q(benchTurn))), oldSolid) >= 0)
                {
                    benchTurn = Quaternion.Euler(0f, 90f, 0f);   // its length along the wall, whichever way the prefab runs
                    benchAt = OnTop(floor, "piece_bench01", chestAt + new Vector3(2.6f, 0f, 2.9f), benchTurn);
                }
                GameObject bench = PlaceOwn("piece_bench01", benchAt, benchTurn, made);
                Vector3 chestSpot = CornerSpot(cornerFloor, cornerPole, built, oldSolid, chestAt, out bool chestTouchesPole);
                GameObject chest = PlaceOwn("piece_chest_wood", chestSpot, Quaternion.identity, made);
                Inventory chestItems = chest != null ? chest.GetComponent<Container>()?.GetInventory() : null;
                Give(chestItems, "Stone", 10);
                // On the porch pole the upgrade replaces: an empty wall torch, and below it a wall item stand.
                Quaternion onPole = new Quaternion(0f, 0.7071068f, 0f, -0.7071068f);   // as the level-2 design's own torch on the other porch pole
                Vector3 torchAt = OnPole("piece_walltorch", oldPole, oldPoleBox, newPoleBox, built, 1.4f, onPole, null, out bool torchFits);
                GameObject torch = PlaceOwn("piece_walltorch", torchAt, onPole, made);
                string torchToken = NameOf(torch);   // the builder takes the piece itself off and puts a new one back
                // Its fuel set to a value no new torch starts with: the one put back keeps it (the review's finding 1).
                const float torchFuel = 0.7f;
                ZNetView torchView = torch != null ? torch.GetComponent<ZNetView>() : null;
                if (torchView != null && torchView.IsValid()) torchView.GetZDO().Set(ZDOVars.s_fuel, torchFuel);
                PieceBox torchBox = PiecePlacer.PlannedBox(new PlannedPiece(0, "piece_walltorch", V(torchAt), Q(onPole)));
                Vector3 standAt = OnPole("itemstand", oldPole, oldPoleBox, newPoleBox, built, 0.6f, onPole, torchBox, out bool standFits);
                // On the ground where the new level's workbench goes: an empty chest (a solid piece: a floor item stand is too
                // low to run 0.1 m into the workbench every way, the rule's measure, as a rug sunk into a floor; the first run).
                Vector3 inTheWayAt = OnGround(U(newBench.Position));
                GameObject inTheWay = PlaceOwn("piece_chest_wood", inTheWayAt, Quaternion.identity, made);
                Log.Event($"vc_ownpiecetest: the spots: bench {benchAt}, chest {chestSpot} (touches the corner pole {chestTouchesPole}), torch {torchAt} " +
                          $"(fits {torchFits}), item stand {standAt} (fits {standFits}), in the new workbench's way {inTheWayAt}");
                yield return new WaitForSeconds(3f);   // the pieces' support settles
                Check($"The player's pieces stand where they were put (bench {bench != null}, chest {chest != null}, torch {torch != null}, " +
                      $"chest on the ground {inTheWay != null}; torch and stand spots found {torchFits}/{standFits})",
                    bench != null && chest != null && torch != null && inTheWay != null && torchFits && standFits);

                // 1. A chest where a new piece goes: the upgrade waits, the window naming it and the new piece.
                UpgradePreview first = site.PreviewUpgrade();
                SiteVerdict v1 = first?.Site;
                string line1 = v1 != null ? DevHelpers.Plain(SiteText.OwnPieceLine(v1)) : "";
                string wayToken = NameOf(inTheWay), workbenchToken = ZNetScene.instance.GetPrefab("piece_workbench")?.GetComponent<Piece>()?.m_name ?? "";
                string wayName = Localization.instance.Localize(wayToken), workbenchName = Localization.instance.Localize(workbenchToken);
                Check($"A chest where the new level's workbench goes makes the upgrade wait and is named ('{line1}'; red {v1?.Red}, " +
                      $"{v1?.OwnPiece} {v1?.OwnPieceWhy} of {v1?.OwnPieceNewPiece})",
                    v1 != null && v1.Red == RedReason.OwnPiece && v1.OwnPieceWhy == OwnPieceWhy.InTheWay && v1.OwnPiece == wayToken && v1.OwnPieceNewPiece == workbenchToken
                    && line1.Contains(wayName) && line1.Contains(workbenchName));

                // 2. Moved: a trophy on the porch pole's item stand makes it wait, the window saying to take it off.
                Remove(inTheWay);
                GameObject stand = PlaceOwn("itemstand", standAt, onPole, made);
                string poleStandToken = NameOf(stand);
                ZNetView standView = stand != null ? stand.GetComponent<ZNetView>() : null;
                if (standView != null && standView.IsValid()) standView.GetZDO().Set(ZDOVars.s_item, "TrophyDeer".GetStableHashCode());   // as the game attaches it
                yield return new WaitForSeconds(2f);
                ItemStand hanging = stand != null ? stand.GetComponentInChildren<ItemStand>() : null;
                SiteVerdict v2 = site.PreviewUpgrade()?.Site;
                string line2 = v2 != null ? DevHelpers.Plain(SiteText.OwnPieceLine(v2)) : "";
                Check($"A trophy on the item stand on a replaced pole makes the upgrade wait, saying to take it off ('{line2}'; red {v2?.Red}, " +
                      $"{v2?.OwnPiece} {v2?.OwnPieceWhy}; the stand holds it {hanging != null && hanging.HaveAttachment()})",
                    v2 != null && v2.Red == RedReason.OwnPiece && v2.OwnPieceWhy == OwnPieceWhy.HasItem && v2.OwnPiece == NameOf(stand)
                    && line2.Contains(Localization.instance.Localize(NameOf(stand))));

                // 3. Taken off: the upgrade can go, and says the builder puts back two of the player's pieces after.
                if (standView != null && standView.IsValid()) standView.GetZDO().Set(ZDOVars.s_item, 0);
                yield return new WaitForSeconds(2f);
                SiteVerdict v3 = site.PreviewUpgrade()?.Site;
                string line3 = v3 != null ? DevHelpers.Plain(SiteText.PutBackLine(v3)) : "";
                Check($"With the trophy off the upgrade is green and says the builder puts back 2 of the player's pieces ('{line3}'; colour " +
                      $"{v3?.Colour}, red {v3?.Red}, put-backs {v3?.PutBacks})",
                    v3 != null && v3.Colour != SiteColour.Red && v3.PutBacks == 2 && line3.Contains("2"));

                // 4. The upgrade, by the real builder: its order keeps the two put-backs, the player's.
                Citizen builder = null;
                for (float t = 0f; t < 60f && (builder = Builder(hut)) == null; t += 1f) yield return new WaitForSeconds(1f);
                Inventory hutChest = hut.GetComponent<Container>()?.GetInventory();
                site.RequestUpgrade();
                BuildOrderRecord order = null;
                for (float t = 0f; t < 20f; t += 0.5f)
                {
                    order = site.Order;
                    if (order != null && order.TargetLevel == 2 && order.State != BuildState.Done) break;
                    yield return new WaitForSeconds(0.5f);
                }
                bool ordered = order != null && order.TargetLevel == 2 && order.State != BuildState.Done;
                if (ordered)
                    foreach (KeyValuePair<string, int> need in site.RemainingNeeds())
                    {
                        int left = Give(hutChest, need.Key, need.Value + Mathf.CeilToInt(need.Value * 0.2f));
                        if (left > 0) Give(hallChest, need.Key, left);
                    }
                List<PutBackRecord> putBacks = ordered ? order.PutBacks.ToList() : new List<PutBackRecord>();
                Check($"The upgrade's order keeps the torch and the item stand to put back, the player's (ordered {ordered}, builder " +
                      $"{builder?.CitizenName ?? "none"}; put-backs: {string.Join(", ", putBacks.Select(p => $"{p.Prefab} by {p.Creator}"))})",
                    ordered && builder != null && putBacks.Count == 2 && putBacks.All(p => p.Creator == me)
                    && putBacks.Any(p => p.Prefab == "piece_walltorch") && putBacks.Any(p => p.Prefab == "itemstand"));
                if (!ordered)
                {
                    Check("The upgrade is ordered", false);
                    yield break;
                }

                // 5-6. The builder takes both down before the old pole, and puts them back once the new pole stands.
                string downWhenPoleWent = null;
                var backWhileNewPoleStood = new Dictionary<int, bool>();
                bool sawOldPole = PiecePlacer.FindStanding(oldPole) != null;
                float waited = 0f;
                for (; waited < 4000f; waited += 0.5f)   // game seconds: about 13 minutes at vc_speed 5
                {
                    BuildOrderRecord now = site.Order;
                    if (now == null) break;
                    if (downWhenPoleWent == null && sawOldPole && PiecePlacer.FindStanding(oldPole) == null)
                        downWhenPoleWent = string.Join("/", now.PutBacks.Select(p => p.State.ToString()));
                    for (int i = 0; i < now.PutBacks.Count; i++)
                        if (now.PutBacks[i].State == PutBackState.Back && !backWhileNewPoleStood.ContainsKey(i))
                            backWhileNewPoleStood[i] = PiecePlacer.FindStanding(newPole) != null;
                    if (now.State == BuildState.Done && hut.Level == 2) break;
                    if (waited % 120f < 0.25f)   // a stall shows what the builder is doing (29 Oct: 40 silent minutes at normal speed)
                        Log.Event($"vc_ownpiecetest: after {waited:0} s: order {now.State}, {now.Done.Count} done, builder {builder?.CitizenName} " +
                                  $"'{builder?.Status}' at {(builder != null ? builder.transform.position.ToString() : "-")}");
                    yield return new WaitForSeconds(0.5f);
                }
                BuildOrderRecord done = site.Order;
                Check($"The builder takes the torch and the item stand down before the porch pole they hang on (their states when it went: " +
                      $"{downWhenPoleWent ?? "it never went"})",
                    downWhenPoleWent == "Down/Down");
                Check($"The builder puts them back once the new pole stands (level {hut.Level}, order {done?.State}, after {waited:0} s; put-backs " +
                      $"{(done != null ? string.Join("/", done.PutBacks.Select(p => p.State.ToString())) : "none")}, the new pole stood: " +
                      $"{string.Join("/", backWhileNewPoleStood.Values)})",
                    hut.Level == 2 && done != null && done.State == BuildState.Done && done.PutBacks.Count == 2
                    && done.PutBacks.All(p => p.State == PutBackState.Back) && backWhileNewPoleStood.Count == 2 && backWhileNewPoleStood.Values.All(b => b));

                // 7. Where they were: the bench and the chest never moved (the very same pieces) and the chest keeps its Stone;
                //    the torch and the item stand stand at their spots, as the player's, not the hut's.
                yield return new WaitForSeconds(3f);   // the new pieces' support settles
                var back = new List<Piece>();
                if (done != null)
                    foreach (PutBackRecord putBack in done.PutBacks)
                    {
                        Piece found = PiecePlacer.FindPutBack(putBack);
                        if (found == null) continue;
                        back.Add(found);
                        made.Add(found.gameObject);
                    }
                int stoneKept = CountIn(chestItems, "Stone");
                Piece torchBack = back.FirstOrDefault(p => Utils.GetPrefabName(p.gameObject) == "piece_walltorch");
                ZNetView torchBackView = torchBack != null ? torchBack.GetComponent<ZNetView>() : null;
                float fuelBack = torchBackView != null && torchBackView.IsValid() ? torchBackView.GetZDO().GetFloat(ZDOVars.s_fuel, -1f) : -1f;
                Fireplace torchFire = ZNetScene.instance.GetPrefab("piece_walltorch")?.GetComponent<Fireplace>();
                Check($"The bench and the chest stay where they were and the chest keeps its things; the torch and the item stand are back " +
                      $"as the player's, the torch with its fuel (bench {bench != null}, chest {chest != null}, stone {stoneKept} of 10; back: " +
                      $"{string.Join(", ", back.Select(p => $"{Utils.GetPrefabName(p.gameObject)} by {p.GetCreator()}"))}; the torch's fuel " +
                      $"{fuelBack:0.00} of {torchFuel:0.00}, a new one starts with {(torchFire != null ? torchFire.m_startFuel : -1f):0.00})",
                    bench != null && Utils.DistanceXZ(bench.transform.position, benchAt) < 0.01f && chest != null && stoneKept == 10
                    && back.Count == 2 && back.All(p => p.GetCreator() == me && PiecePlacer.SiteOf(p.gameObject).Length == 0)
                    && fuelBack > torchFuel - 0.2f && fuelBack <= torchFuel + 0.01f);

                // 8. The take-down's first click: the torch and the item stand hang on the building and would fall, the first
                //    named. The bench and the chest stay: the hut's floor lies on the ground, and the rule counts what stands
                //    that low as on the ground (the first run).
                int falls = site.OwnPiecesThatWouldFall(out string firstFalls);
                Check($"The take-down's first click names the player's pieces that would fall ({falls}, the first {firstFalls})",
                    falls == 2 && (firstFalls == torchToken || firstFalls == poleStandToken));
            }
            finally
            {
                ColonyConfig.NewcomersWalkIn.Value = walkInWas;
                EnvMan.instance.m_debugTimeOfDay = false;
                foreach (GameObject go in made.Where(g => g != null)) Remove(go);
                Log.Event($"vc_ownpiecetest: removed what it made: {DevHelpers.WipeColonies(origin, 60f)}");
                Log.Event($"vc_ownpiecetest: done — {s_pass} passed, {s_fail} failed");
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"vc_ownpiecetest: {s_pass} passed, {s_fail} failed");
            }
        }

        /// <summary>The planned piece of this prefab nearest a spot, or null.</summary>
        private static PlannedPiece Nearest(IEnumerable<PlannedPiece> pieces, string prefab, Vector3 near) => pieces
            .Where(p => p.IsValid && p.Prefab == prefab)
            .OrderBy(p => Vector3.Distance(U(p.Position), near))
            .FirstOrDefault(p => Vector3.Distance(U(p.Position), near) < 0.5f);

        /// <summary>A spot for a piece with its box's bottom on the floor's top.</summary>
        private static Vector3 OnTop(PlannedPiece floor, string prefab, Vector3 at, Quaternion rotation)
        {
            float top = PiecePlacer.PlannedBox(floor).Max.Y;
            float bottom = PiecePlacer.PlannedBox(new PlannedPiece(0, prefab, V(new Vector3(at.x, 0f, at.z)), Q(rotation))).Min.Y;
            return new Vector3(at.x, top - bottom + 0.01f, at.z);
        }

        /// <summary>
        /// The chest's spot in the back corner, on the kept floor: as close to the corner's pole as it goes without running
        /// into a piece of either level.
        /// </summary>
        private static Vector3 CornerSpot(PlannedPiece floor, PlannedPiece pole, List<PieceBox> built, List<PieceBox> oldSolid, Vector3 chestAt, out bool touches)
        {
            PieceBox poleBox = PiecePlacer.PlannedBox(pole);
            for (float d = 0.2f; d < 1.5f; d += 0.05f)
            {
                Vector3 at = OnTop(floor, "piece_chest_wood", U(pole.Position) + new Vector3(d, 0f, d), Quaternion.identity);
                PieceBox box = PiecePlacer.PlannedBox(new PlannedPiece(0, "piece_chest_wood", V(at), Q(Quaternion.identity)));
                // Clear of every new piece by the rule's touch (the builder's own check is the colliders'), and of the old walls.
                if (built.Any(b => OwnPieceRules.Touches(box, b)) || StorehouseRules.InTheWayOf(box, oldSolid) >= 0) continue;
                touches = OwnPieceRules.Touches(box, poleBox);
                return at;
            }
            touches = false;
            return OnTop(floor, "piece_chest_wood", chestAt + new Vector3(-0.5f, 0f, 0.4f), Quaternion.identity);
        }

        /// <summary>
        /// A spot on the porch pole's outer side at this height: the nearest the pole that touches both the old pole and the
        /// new one, runs into neither (nor into a built piece), and keeps clear of <paramref name="clearOf"/>.
        /// </summary>
        private static Vector3 OnPole(string prefab, PlannedPiece pole, PieceBox oldBox, PieceBox newBox, List<PieceBox> built, float height,
            Quaternion rotation, PieceBox? clearOf, out bool fits)
        {
            Vector3 centre = U(pole.Position);
            var oldOnly = new List<PieceBox> { oldBox };
            for (float dz = 0.05f; dz < 0.7f; dz += 0.01f)
            {
                var at = new Vector3(centre.x, centre.y - 1f + height, centre.z + dz);
                PieceBox box = PiecePlacer.PlannedBox(new PlannedPiece(0, prefab, V(at), Q(rotation)));
                if (StorehouseRules.InTheWayOf(box, oldOnly) >= 0 || StorehouseRules.InTheWayOf(box, built) >= 0) continue;
                if (!OwnPieceRules.Touches(box, oldBox) || !OwnPieceRules.Touches(box, newBox)) continue;
                if (clearOf.HasValue && OwnPieceRules.Touches(box, clearOf.Value)) continue;
                fits = true;
                return at;
            }
            fits = false;
            return new Vector3(centre.x, centre.y - 1f + height, centre.z + 0.3f);
        }

        /// <summary>A placed piece's name token ("$piece_itemstand"), or "".</summary>
        private static string NameOf(GameObject go) => go != null ? go.GetComponent<Piece>()?.m_name ?? "" : "";

        /// <summary>The hut's builder, once one is hired, or null.</summary>
        private static Citizen Builder(ColonyBuilding hut) => Citizen.All.FirstOrDefault(c => c != null && c.ColonyId == hut.ColonyId
            && c.Record.Job == Jobs.JobDefinitions.BuilderId);

        /// <summary>A piece of the game's placed as the player places it with the hammer: the player as its creator.</summary>
        private static GameObject PlaceOwn(string prefabName, Vector3 at, Quaternion rotation, List<GameObject> made)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null) return null;
            GameObject go = Object.Instantiate(prefab, at, rotation);
            Piece piece = go.GetComponent<Piece>();
            if (piece != null && Player.m_localPlayer != null) piece.SetCreator(Player.m_localPlayer.GetPlayerID(), default(Splatform.PlatformUserID));
            made.Add(go);
            return go;
        }

        private static Vector3 OnGround(Vector3 p) => new Vector3(p.x, ZoneSystem.instance.GetGroundHeight(p), p.z);

        /// <summary>Puts up to <paramref name="amount"/> of an item into the inventory; returns how many didn't fit.</summary>
        private static int Give(Inventory inventory, string item, int amount)
        {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(item) : null;
            if (inventory == null || prefab == null) return amount;
            int put = 0;
            while (put < amount && inventory.AddItem(prefab, 1)) put++;
            return amount - put;
        }

        /// <summary>How many of an item (by its prefab) a chest holds; -1 without the chest.</summary>
        private static int CountIn(Inventory items, string prefab) =>
            items == null ? -1 : items.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab).Sum(i => i.m_stack);

        private static IEnumerator Finish(ConstructionModule site)
        {
            for (float t = 0f; site.Order == null && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
            if (site.Order != null) site.FinishNow();
            for (float t = 0f; !site.IsBuilt && t < 40f; t += 0.5f) yield return new WaitForSeconds(0.5f);
        }

        private static void Remove(GameObject go)
        {
            if (go == null) return;
            ZNetView nview = go.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
        }

        private static void Check(string what, bool ok)
        {
            if (ok) s_pass++;
            else s_fail++;
            Log.Event($"vc_ownpiecetest: {(ok ? "PASS" : "FAIL")} {what}");
        }
    }
}
