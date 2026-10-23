using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Colony;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.Jobs
{
    /// <summary>
    /// The farmer (0.12): works his Farm's fields as a player farms (0.20.1: only its fields) — cultivates the ground with a Cultivator from
    /// the Farm's chest, plants the ticked crops from the seeds in it, picks what's ripe and carries the harvest home.
    /// Every tick: look at the field (every few seconds), ask <see cref="FarmerBrain"/> what to do, do one small piece of it.
    /// </summary>
    public class FarmerJob : Job
    {
        private const float RescanSeconds = 3f;
        private const float IdleRescanSeconds = 8f;
        private const float HutReach = 2.5f;
        private const float WorkReach = 1.4f;
        private const float DepositRetrySeconds = 2f;
        private const float StorageFullRetrySeconds = 10f;
        /// <summary>How long cultivating, planting or picking takes him (twice as long hungry).</summary>
        private const float WorkSeconds = 1.5f;
        private const float DropRescanSeconds = 0.5f;
        private const float PlotsRefreshSeconds = 60f;
        public const string CultivatorItem = "Cultivator";

        private static readonly ClaimBoard s_claims = new ClaimBoard();
        private static readonly AccessTools.FieldRef<Pickable, ZNetView> s_pickableView = AccessTools.FieldRefAccess<Pickable, ZNetView>("m_nview");

        private readonly ColonyBuilding _hut;
        private readonly TargetBlacklist _blacklist = new TargetBlacklist(300);
        private readonly TargetBlacklist _picked = new TargetBlacklist(30);
        /// <summary>Ripe crops he couldn't get to: tried again after a minute (from elsewhere the way may be clear).</summary>
        private readonly TargetBlacklist _unreachable = new TargetBlacklist(60);
        /// <summary>Plots he leaves for a while (a planting or cultivating failed there, or a plant couldn't grow), by position.</summary>
        private readonly Dictionary<string, float> _badPlotUntil = new Dictionary<string, float>();
        /// <summary>Since when a plant on a plot hasn't been able to grow, by position.</summary>
        private readonly Dictionary<string, float> _sickSince = new Dictionary<string, float>();
        private const float SickSeconds = 90f, BadPlotSeconds = 1800f, FailedPlotSeconds = 600f;
        /// <summary>How many times in a row a crop couldn't be planted at a plot (<see cref="PlotRetry"/>).</summary>
        private readonly Dictionary<string, int> _plantFailures = new Dictionary<string, int>();
        private readonly HashSet<string> _dropsBefore = new HashSet<string>();
        private readonly StuckDetector _walkStuck = new StuckDetector();
        private readonly StuckDetector _depositStuck = new StuckDetector();
        private readonly FullChestRun _fullChest = new FullChestRun();   // his chest full: the load goes elsewhere (0.21)
        private List<Vector3> _plots = new List<Vector3>();
        /// <summary>The plots in a grid (0.25): what stands on or near each, in one look per thing (about 600 plots at level 5).</summary>
        private PlotIndex _plotIndex = new PlotIndex(new List<Vec3>(), FieldPlots.OnPlotTolerance);
        /// <summary>The Farm's chest, read once per look at the field (0.25: it was read for every empty plot).</summary>
        private Dictionary<string, int> _rescanStock;
        /// <summary>Each plot's crop rule, by the same index: "" the Farm's list, else a field's crop (0.20).</summary>
        private List<string> _plotCrops = new List<string>();
        private string _plotsSignature = "";
        /// <summary>How far from the Farm his plots reach.</summary>
        private float _reach;
        /// <summary>A cultivated plot stands empty (though perhaps no seed for it is at hand).</summary>
        private bool _emptyCultivated;
        private int _plotsLevel = -1;
        private float _plotsAt = float.NegativeInfinity;
        private PlotTally _tally = new PlotTally();
        private string _noGroundSaid = "";
        private Pickable _ripe;
        private int _plantPlot = -1, _cultivatePlot = -1;
        /// <summary>Cultivated empty plots he may plant now, by index (for how many seeds to take at once).</summary>
        private readonly List<int> _plantable = new List<int>();
        /// <summary>Seeds he took from the chest to plant, by item, less those planted (0.20): not harvest.</summary>
        private readonly Dictionary<string, int> _seedsInHand = new Dictionary<string, int>();

        /// <summary>Self-test: visits to the chest for seeds, and plots planted.</summary>
        internal int SeedFetches { get; private set; }
        internal int Planted { get; private set; }
        private bool _seedToPlant;
        private CropInfo _crop;   // the crop he is planting now
        private int _turn;
        private ItemDrop _drop;
        private string _dropId = "";
        private Vector3 _pickedAt;
        private float _pickedTime = float.NegativeInfinity;
        private string _pickedItem = "";
        private bool _awaitingDrop;
        private float _workTimer, _rescanTimer, _depositTimer;
        private bool _storageFull;
        private bool _hasCultivator;   // looked for on each rescan, not each step (1.4's review: every colony chest, every step)
        private FarmerAction? _lastAction;

        public FarmerJob(Citizen citizen, CitizenAI ai, ColonyBuilding hut) : base(citizen, ai)
        {
            _hut = hut;
        }


        /// <summary>
        /// Every plot the Farm's farmer works and what grows on it: each field assigned to it, with its crop (0.20); a Farm
        /// works only its fields, MineColonies' way (0.20.1). A spot belongs to one field only, and counts when it is on dry
        /// ground near its marker's height, off buildings, rocks and trees.
        /// </summary>
        public static List<PlotRule> RulesOf(ColonyBuilding farm) => RulesOf(farm, out _);

        /// <summary>As <see cref="RulesOf(ColonyBuilding)"/>, with where its fields' plots went (the farmer's words with none, 1.4.3).</summary>
        public static List<PlotRule> RulesOf(ColonyBuilding farm, out PlotTally tally)
        {
            tally = new PlotTally();
            var fields = new List<PlotRule>();
            if (farm == null) return fields;
            FieldsModule module = farm.GetComponent<FieldsModule>();
            if (module != null)
            {
                float water = ZoneSystem.instance.m_waterLevel;
                foreach (FieldMarker marker in module.Fields())
                {
                    tally.Fields++;
                    float markerY = marker.transform.position.y;
                    foreach (Vector3 at in marker.PlotPositions())
                    {
                        tally.Plots++;
                        if (at.y < water + 0.3f)
                        {
                            tally.Water++;
                            continue;
                        }
                        if (Mathf.Abs(at.y - markerY) > 3f)
                        {
                            tally.Slope++;
                            continue;
                        }
                        Vec3 spot = CoreVectors.V(at);
                        if (ConstructionModule.All.Any(s => s != null && s.SiteFootprint != null && FieldPlots.LeftOut(
                                s.SiteFootprint.Grow(1f).Contains(spot), s.RoomFootprint != null && s.RoomFootprint.Contains(spot),
                                s.Building == farm, s.Order != null && FieldPlots.SiteBusy(s.Order.Kind, s.Order.State == BuildState.Done))))
                        {
                            tally.Building++;
                            continue;
                        }
                        if (Blocked(at))
                        {
                            tally.Blocked++;
                            continue;
                        }
                        fields.Add(new PlotRule { At = CoreVectors.V(at), Crop = marker.Crop, FieldId = marker.Id });
                    }
                }
            }
            return FieldPlots.Merge(new List<PlotRule>(), fields);
        }

        /// <summary>Changes when what his plots come from changes: a field's crop, size, place or Farm.</summary>
        private static string FieldsSignature(ColonyBuilding farm)
        {
            FieldsModule module = farm != null ? farm.GetComponent<FieldsModule>() : null;
            if (module == null) return "";
            return string.Join(";", module.Fields().Select(m => m.Id + "=" + m.Signature));
        }

        /// <summary>The crop a field's plot wants (null for the Farm's list, or a crop no longer in the game).</summary>
        private static CropInfo RuleCrop(string rule) => rule.Length == 0 ? null : CropList.Crops.FirstOrDefault(c => c.Piece == rule);



        private static string Key(Vector3 plot) => $"{plot.x:0.0},{plot.z:0.0}";

        private bool IsBad(Vector3 plot) => _badPlotUntil.TryGetValue(Key(plot), out float until) && Time.time < until;

        private void MarkBad(Vector3 plot, float seconds, string why)
        {
            _badPlotUntil[Key(plot)] = Time.time + seconds;
            Log.Info($"{Citizen.CitizenName} leaves the plot at ({plot.x:0.0}, {plot.z:0.0}) for {seconds / 60f:0} minutes: {why}");
        }

        /// <summary>The crop grows at this very plot (its biome; a Farm by a biome's edge has plots in two).</summary>
        private static bool GrowsAt(CropInfo crop, Vector3 plot) =>
            crop.Biomes == 0 || WorldGenerator.instance == null || (crop.Biomes & (int)WorldGenerator.instance.GetBiome(plot)) != 0;

        /// <summary>A building piece, a tree or a rock on the plot (crops and plants don't block it).</summary>
        private static bool Blocked(Vector3 at)
        {
            foreach (Collider collider in Physics.OverlapSphere(at + Vector3.up * 0.5f, 0.45f))
            {
                if (collider == null || collider.isTrigger) continue;
                if (collider.GetComponentInParent<Plant>() != null || collider.GetComponentInParent<Pickable>() != null) continue;
                if (collider.GetComponentInParent<Character>() != null || collider.GetComponentInParent<ItemDrop>() != null) continue;
                if (collider.GetComponentInParent<Heightmap>() != null) continue;   // the ground itself
                if (collider.GetComponentInParent<Piece>() != null || collider.GetComponentInParent<TreeBase>() != null
                    || collider.GetComponentInParent<Destructible>() != null || collider.gameObject.layer == LayerMask.NameToLayer("static_solid")) return true;
            }
            return false;
        }

        public override void Tick(float dt)
        {
            ColonyBuilding hut = _hut;
            if (Citizen.Carry.Total == 0) _storageFull = false;
            if (_fullChest.Tick(Citizen, Ai, hut)) return;
            if (!ReferenceEquals(_ripe, null) && _ripe == null) _ripe = null;
            if (!ReferenceEquals(_drop, null) && _drop == null) ForgetDrop();

            _rescanTimer -= dt;
            if (_rescanTimer <= 0f) Rescan(hut);

            FarmerAction action = FarmerBrain.Decide(new FarmerSituation
            {
                HasHut = hut != null && hut.Storage != null,
                HasField = _plots.Count > 0,
                IsNight = EnvMan.IsNight(),
                CarryTotal = Citizen.Carry.Total,
                SeedsInHand = SeedsHeld(),
                CarryCapacity = ColonyConfig.FarmerCarryCapacity.Value,
                StorageFull = _storageFull,
                DropsNearby = _drop != null || (_awaitingDrop && Time.time - _pickedTime < Foraging.DropWaitSeconds),
                RipeCrop = _ripe != null,
                PlantablePlot = _plantPlot >= 0 || _emptyCultivated,
                SeedToPlant = _seedToPlant,
                UncultivatedPlot = _cultivatePlot >= 0,
                HasCultivator = _hasCultivator,
            });
            if (action != _lastAction)
            {
                Log.Info($"{Citizen.CitizenName}: {_lastAction?.ToString() ?? "start"} -> {action} (carrying {Citizen.Carry.Total})");
                _lastAction = action;
                _workTimer = 0f;
            }

            switch (action)
            {
                case FarmerAction.Unemployed:
                    Citizen.Status = "$vc_status_unemployed";
                    Ai.Idle();
                    break;
                case FarmerAction.Rest:
                    Citizen.Status = "$vc_status_resting";
                    if (Ai.GoTo(hut.transform.position, 4f)) Ai.Idle();
                    break;
                case FarmerAction.Deposit:
                    Citizen.Status = "$vc_status_delivering";
                    if (ReachHut(hut)) Deliver(hut, dt, DepositRetrySeconds);
                    break;
                case FarmerAction.WaitStorageFull:
                    Citizen.Status = "$vc_status_storage_full";
                    if (ReachHut(hut)) Deliver(hut, dt, StorageFullRetrySeconds);
                    break;
                case FarmerAction.Gather:
                    Citizen.Status = "$vc_status_harvesting";
                    Gather();
                    break;
                case FarmerAction.Harvest:
                    Citizen.Status = "$vc_status_harvesting";
                    Harvest(dt);
                    break;
                case FarmerAction.Plant:
                    Citizen.Status = "$vc_status_planting";
                    Plant(hut, dt);
                    break;
                case FarmerAction.Cultivate:
                    Citizen.Status = "$vc_status_cultivating";
                    Cultivate(dt);
                    break;
                case FarmerAction.WaitNeedsCultivator:
                    Citizen.Status = "$vc_status_needs_cultivator_farm";   // where it may be (the 1.0 play-through: one in the Town Hall left the player guessing)
                    if (Ai.GoTo(hut.transform.position, 4f)) Ai.Idle();
                    break;
                case FarmerAction.WaitNeedsSeeds:
                    Citizen.Status = "$vc_status_needs $vc_seeds";
                    if (Ai.GoTo(hut.transform.position, 4f)) Ai.Idle();
                    break;
                case FarmerAction.WaitGrowing:
                    Citizen.Status = "$vc_status_crops_growing";
                    Ai.FreeTime(hut.transform.position);
                    break;
                case FarmerAction.WaitNoField:
                    // A Farm works only its fields (0.20.1): Hugin says how to mark one, once.
                    Citizen.Status = FieldWords.NoFieldStatus(_tally.Fields);   // fields but no free ground say so (1.4.3)
                    if (FieldWords.ShowsFieldTip(_tally.Fields)) Gui.HuginTips.ShowNear(hut.transform.position, Gui.HuginTips.Field);
                    Ai.FreeTime(hut.transform.position);
                    break;
            }
        }

        public override void Resume()
        {
            _walkStuck.Reset();
            _depositStuck.Reset();
            _rescanTimer = 0f;
        }

        public override bool FinishingBeforeNight(float dt)
        {
            ColonyBuilding hut = _hut;
            if (_fullChest.Tick(Citizen, Ai, hut)) return true;
            if (hut == null || hut.Storage == null || _storageFull || Citizen.Carry.Total <= 0) return false;
            Citizen.Status = "$vc_status_delivering";
            if (ReachHut(hut)) Deliver(hut, dt, DepositRetrySeconds);
            return true;
        }

        public override void Stop() => ForgetDrop();

        /// <summary>The seeds he took to plant that he still holds (not harvest while plots wait for them).</summary>
        private int SeedsHeld()
        {
            int held = 0;
            foreach (KeyValuePair<string, int> seed in _seedsInHand) held += Mathf.Min(seed.Value, Citizen.Carry.Count(seed.Key));
            return held;
        }

        // ---- Looking at the field ----

        private void Rescan(ColonyBuilding hut)
        {
            _rescanTimer = RescanSeconds;
            _hasCultivator = Construction.BuilderTools.InColony(hut, CultivatorItem);   // the Farm's chest or another of the colony's (1.3.1)
            _ripe = null;
            _plantPlot = _cultivatePlot = -1;
            _plantable.Clear();
            if (hut == null || hut.Storage == null) return;
            long timer = Perf.Start();
            string signature = FieldsSignature(hut);
            if (_plotsLevel != hut.Level || Time.time - _plotsAt > PlotsRefreshSeconds || signature != _plotsSignature)
            {
                List<PlotRule> rules = RulesOf(hut, out _tally);
                // Fields with no free ground: said once a change, with where the plots went (1.4.3, the player's watch).
                string why = _tally.Fields > 0 && rules.Count == 0 ? FieldWords.Tally(_tally) : "";
                if (why.Length > 0 && why != _noGroundSaid)
                    Log.Info($"{Citizen.CitizenName}'s Farm has fields but no free ground to work: {why}");
                _noGroundSaid = why;
                _plots = rules.Select(r => CoreVectors.U(r.At)).ToList();
                _plotIndex = new PlotIndex(rules.Select(r => r.At).ToList(), FieldPlots.OnPlotTolerance);
                _plotCrops = rules.Select(r => r.Crop ?? "").ToList();
                _reach = (_plots.Count > 0 ? _plots.Max(p => Utils.DistanceXZ(p, hut.transform.position)) : 0f) + 2f;
                _plotsLevel = hut.Level;
                _plotsAt = Time.time;
                _plotsSignature = signature;
            }
            if (_drop == null && Time.time - _pickedTime < Foraging.GatherSeconds) FindDrop();

            // What stands on each plot: a crop (ripe or not), a growing plant, or nothing.
            ICollection<string> crops = CropList.GrownPrefabs;
            float reach = _reach;
            Vector3 hutPos = hut.transform.position, me = Citizen.transform.position;
            _plotIndex.ClearStanding();
            float bestRipe = float.MaxValue;
            foreach (Pickable pick in WorldScan.Pickables())
            {
                if (pick == null || Utils.DistanceXZ(pick.transform.position, hutPos) > reach) continue;
                Vector3 at = pick.transform.position;
                if (_plotIndex.IndexOf(CoreVectors.V(at)) < 0) continue;
                _plotIndex.MarkStanding(CoreVectors.V(at));
                if (!crops.Contains(Utils.GetPrefabName(pick.gameObject)) || !pick.CanBePicked()) continue;
                string id = IdOf(pick.gameObject);
                if (_blacklist.IsBlocked(id, Time.time) || _picked.IsBlocked(id, Time.time) || _unreachable.IsBlocked(id, Time.time)
                    || s_claims.IsTakenByOther(id, Ground(at), Citizen.Id, Time.time)) continue;
                float d = Vector3.Distance(me, at);
                if (d < bestRipe)
                {
                    bestRipe = d;
                    _ripe = pick;
                }
            }
            foreach (Plant plant in WorldScan.Plants())
            {
                if (plant == null || Utils.DistanceXZ(plant.transform.position, hutPos) > reach) continue;
                _plotIndex.MarkStanding(CoreVectors.V(plant.transform.position));
                // A plant that can't grow there (biome, cold, a roof, no room) is cleared after a while and its plot rested
                // (review of 0.12: a dead seedling blocked its plot for good).
                string key = Key(plant.transform.position);
                if (plant.GetStatus() == global::Plant.Status.Healthy) _sickSince.Remove(key);
                else if (!_sickSince.ContainsKey(key)) _sickSince[key] = Time.time;
                else if (Time.time - _sickSince[key] > SickSeconds && _plotIndex.IndexOf(CoreVectors.V(plant.transform.position)) >= 0)
                {
                    MarkBad(plant.transform.position, BadPlotSeconds, $"the {Utils.GetPrefabName(plant.gameObject)} couldn't grow there ({plant.GetStatus()})");
                    _sickSince.Remove(key);
                    ZNetView view = plant.GetComponent<ZNetView>();
                    if (view != null && view.IsValid())
                    {
                        if (!view.IsOwner()) view.ClaimOwnership();
                        ZNetScene.instance.Destroy(plant.gameObject);
                    }
                }
            }

            // The Farm's ticked crops (for fields on its list); a field's own crop for the rest (0.20).
            List<CropInfo> ticked = CropList.For(hut).Where(l => l.Ticked).Select(l => l.Crop).ToList();
            _emptyCultivated = false;

            float bestPlant = float.MaxValue, bestCultivate = float.MaxValue;
            _rescanStock = hut.Storage.Contents();
            try
            {
                for (int i = 0; i < _plots.Count; i++)
                {
                    Vector3 plot = _plots[i];
                    if (IsBad(plot) || _plotIndex.IsTaken(i)) continue;   // taken or resting
                    Heightmap ground = Heightmap.FindHeightmap(plot);
                    bool cultivated = ground != null && ground.IsCultivated(plot);
                    float d = Vector3.Distance(me, plot);
                    if (cultivated)
                    {
                        _emptyCultivated = true;
                        if (!CanPlantAt(i, ticked)) continue;   // nothing for it at hand: "Needs seeds" if nothing else to do
                        _plantable.Add(i);
                        if (d < bestPlant)
                        {
                            bestPlant = d;
                            _plantPlot = i;
                        }
                    }
                    else if (d < bestCultivate)
                    {
                        bestCultivate = d;
                        _cultivatePlot = i;
                    }
                }
            }
            finally
            {
                _rescanStock = null;
            }
            _seedToPlant = _plantPlot >= 0;
            if (_ripe == null && _plantPlot < 0 && _cultivatePlot < 0 && _drop == null) _rescanTimer = IdleRescanSeconds;
            if (_awaitingDrop && _drop == null && Time.time - _pickedTime < Foraging.DropWaitSeconds) _rescanTimer = DropRescanSeconds;
            Perf.Stop("farmer: looking at the field", timer);
        }

        /// <summary>A seed for this plot's crop is at hand and it grows there: a field's crop, or one of the Farm's ticked.</summary>
        private bool CanPlantAt(int i, List<CropInfo> ticked)
        {
            Vector3 plot = _plots[i];
            string rule = i < _plotCrops.Count ? _plotCrops[i] : "";
            CropInfo crop = RuleCrop(rule);
            if (crop != null) return GrowsAt(crop, plot) && Stock(crop.Seed) >= crop.SeedCount;
            return ticked.Any(c => GrowsAt(c, plot) && Stock(c.Seed) >= c.SeedCount && CropChoice.WorthPlanting(c, CropList.Crops, Stock));
        }

        // ---- Work ----

        private bool Reach(Vector3 at)
        {
            if (Ai.GoTo(at, WorkReach))
            {
                _walkStuck.Reset();
                return true;
            }
            if (_walkStuck.Update(Vector3.Distance(Citizen.transform.position, at), Time.time))
            {
                // Rested like a plot he couldn't plant (0.20: removed, it came back with the plot list every minute).
                MarkBad(at, FailedPlotSeconds, "they can't get to it");
                _walkStuck.Reset();
                _rescanTimer = 0f;
            }
            return false;
        }

        /// <summary>Works on the spot for a moment (twice as long hungry); true when done. Practice comes with what it did (0.26).</summary>
        private bool Worked(Vector3 at, float dt)
        {
            Ai.Face(at);
            _workTimer += dt;
            if (_workTimer < WorkSeconds / Citizen.SkillSpeed(WorkSkill.Farming)) return false;   // faster with Farming (0.22)
            _workTimer = 0f;
            return true;
        }

        private void Cultivate(float dt)
        {
            if (_cultivatePlot < 0 || _cultivatePlot >= _plots.Count) return;
            Vector3 plot = _plots[_cultivatePlot];
            if (!Reach(plot) || !Worked(plot, dt)) return;
            plot.y = ZoneSystem.instance.GetGroundHeight(plot);
            if (!GroundShaper.Cultivate(plot))
            {
                MarkBad(plot, FailedPlotSeconds, "the Cultivator has no way to cultivate");
                _cultivatePlot = -1;
                return;
            }
            Log.Info($"{Citizen.CitizenName} cultivates the plot at ({plot.x:0.0}, {plot.z:0.0})");
            Citizen.Practise(WorkSkill.Farming);
            _cultivatePlot = -1;
            _rescanTimer = 0.5f;   // the ground changes a moment later
        }

        private void Plant(ColonyBuilding hut, float dt)
        {
            if (_plantPlot < 0 || _plantPlot >= _plots.Count) return;
            string rule = _plantPlot < _plotCrops.Count ? _plotCrops[_plantPlot] : "";
            CropInfo fieldCrop = RuleCrop(rule);
            Vector3 target = _plots[_plantPlot];
            List<CropInfo> ticked = CropList.For(hut).Where(l => l.Ticked && GrowsAt(l.Crop, target) && CropChoice.WorthPlanting(l.Crop, CropList.Crops, Stock))
                .Select(l => l.Crop).ToList();
            // The seed in his hand must be for this plot: a field's own crop, or one of the Farm's list (0.20).
            if (_crop != null && (fieldCrop != null ? _crop != fieldCrop : !ticked.Contains(_crop))) _crop = null;
            if (_crop == null || Citizen.Carry.Count(_crop.Seed) < _crop.SeedCount)
            {
                // Which crop next (a field's own, or the Farm's in turn, each while its seed is in his hand or the chest), and
                // fetch its seed.
                CropInfo next = fieldCrop != null
                    ? (Stock(fieldCrop.Seed) >= fieldCrop.SeedCount ? fieldCrop : null)
                    : CropChoice.Next(ticked, seed => ticked.Any(c => c.Seed == seed && Stock(seed) >= c.SeedCount), ref _turn);
                if (next == null)
                {
                    _seedToPlant = false;
                    return;
                }
                _crop = next;
                if (Citizen.Carry.Count(next.Seed) < next.SeedCount)
                {
                    if (!ReachHut(hut)) return;
                    // Seeds for every plot waiting for this crop at once, up to 20 (0.20: he walked to the chest for each); a
                    // bush takes its price (5 raspberries), a crop one seed.
                    int wanting = _plantable.Count(i => i < _plotCrops.Count &&
                        (_plotCrops[i] == next.Piece || (_plotCrops[i].Length == 0 && GrowsAt(next, _plots[i]))));
                    int inChest = hut.Storage.Contents().TryGetValue(next.Seed, out int stored) ? stored : 0;
                    int take = Mathf.Max(next.SeedCount - Citizen.Carry.Count(next.Seed),
                        SeedBatch.ToTake(Mathf.Max(1, wanting), next.SeedCount, inChest, Citizen.Carry.Count(next.Seed)));
                    int taken = 0;
                    while (taken < take && hut.Storage.TryTakeItem(next.Seed, Citizen.Carry, ownUse: true)) taken++;
                    Citizen.SaveCarry();
                    if (taken > 0)
                    {
                        _seedsInHand[next.Seed] = (_seedsInHand.TryGetValue(next.Seed, out int had) ? had : 0) + taken;
                        SeedFetches++;
                    }
                    if (Citizen.Carry.Count(next.Seed) < next.SeedCount)
                    {
                        _crop = null;
                        _rescanTimer = SeedBatch.LookAgainIn(taken);
                        return;
                    }
                    Log.Info($"{Citizen.CitizenName} takes {taken} {next.Seed} from the Farm to plant {next.Piece} ({wanting} plots wait for it)");
                }
            }
            Vector3 plot = _plots[_plantPlot];
            if (!Reach(plot) || !Worked(plot, dt)) return;
            plot.y = ZoneSystem.instance.GetGroundHeight(plot);   // cultivating levels the ground a little
            GameObject prefab = ZNetScene.instance.GetPrefab(_crop.Piece);
            Heightmap ground = Heightmap.FindHeightmap(plot);
            // Which rule stops it, for the log (1.4.1: two plots of the worker test's field "can't be planted there" and
            // nothing said why).
            string why = prefab == null ? "no such crop" : ground == null ? "no ground" : !ground.IsCultivated(plot) ? "not tilled"
                : !HasRoom(_crop, plot) ? "no room" : !GrowsAt(_crop, plot) ? "it doesn't grow there" : null;
            if (why != null)
            {
                int failures = (_plantFailures.TryGetValue(Key(plot), out int before) ? before : 0) + 1;
                _plantFailures[Key(plot)] = failures;
                MarkBad(plot, PlotRetry.Seconds(failures), $"{_crop.Piece} can't be planted there ({why})");   // longer each time (0.22; review of 0.12: again and again)
                _plantPlot = -1;
                _rescanTimer = 0f;
                return;
            }
            Object.Instantiate(prefab, plot, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            _plantFailures.Remove(Key(plot));
            Citizen.Carry.Remove(_crop.Seed, _crop.SeedCount);
            Citizen.SaveCarry();
            if (_seedsInHand.TryGetValue(_crop.Seed, out int held)) _seedsInHand[_crop.Seed] = Mathf.Max(0, held - _crop.SeedCount);
            Planted++;
            Citizen.Practise(WorkSkill.Farming);
            Log.Info($"{Citizen.CitizenName} plants {_crop.Piece} at ({plot.x:0.0}, {plot.z:0.0})");
            _crop = null;
            _plantPlot = -1;
            WorldScan.InvalidatePlants();
            _rescanTimer = 0.5f;
        }

        private static int s_growSpaceMask;

        /// <summary>As the game's own room check for a plant (its layers; anything but another plant's planted piece in the way).</summary>
        private static bool HasRoom(CropInfo crop, Vector3 at)
        {
            if (s_growSpaceMask == 0) s_growSpaceMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
            float radius = Mathf.Max(0.3f, crop.GrowRadius);
            foreach (Collider collider in Physics.OverlapSphere(at, radius, s_growSpaceMask))
                if (collider != null && collider.GetComponentInParent<Plant>() == null) return false;
            return true;
        }

        /// <summary>How many of an item the Farm's chest and his hands hold (whether a seed crop is worth planting).</summary>
        private int Stock(string item)
        {
            Dictionary<string, int> chest = _rescanStock ?? (_hut != null && _hut.Storage != null ? _hut.Storage.Contents() : null);
            return Citizen.Carry.Count(item) + (chest != null && chest.TryGetValue(item, out int n) ? n : 0);
        }

        private void Harvest(float dt)
        {
            Pickable crop = _ripe;
            if (crop == null) return;
            if (!crop.CanBePicked())
            {
                _ripe = null;
                _rescanTimer = 0f;
                return;
            }
            Vector3 at = crop.transform.position;
            string id = IdOf(crop.gameObject);
            s_claims.TryClaim(id, Ground(at), Citizen.Id, Time.time);
            if (!Ai.GoTo(at, WorkReach))
            {
                if (_walkStuck.Update(Vector3.Distance(Citizen.transform.position, at), Time.time))
                {
                    // Review of 0.12: an unreachable crop was chosen again and again; now it waits a minute.
                    Log.Info($"{Citizen.CitizenName} can't get to the {Utils.GetPrefabName(crop.gameObject)} at ({at.x:0.0}, {at.z:0.0}); they try again later");
                    _unreachable.Add(id, Time.time);
                    s_claims.Release(id, Citizen.Id);
                    _ripe = null;
                    _walkStuck.Reset();
                }
                return;
            }
            _walkStuck.Reset();
            if (!Worked(at, dt)) return;
            ZNetView nview = s_pickableView(crop);
            if (nview == null || !nview.IsValid()) return;
            _pickedItem = crop.m_itemPrefab != null ? crop.m_itemPrefab.name : "";
            _dropsBefore.Clear();
            foreach (ItemDrop lying in WorldScan.Drops())
                if (lying != null && Vector3.Distance(lying.transform.position, at) <= Foraging.GatherRadius) _dropsBefore.Add(IdOf(lying.gameObject));
            nview.InvokeRPC("RPC_Pick", 0);
            Citizen.Practise(WorkSkill.Farming);
            Log.Info($"{Citizen.CitizenName} harvests the {Utils.GetPrefabName(crop.gameObject)} at ({at.x:0.0}, {at.z:0.0})");
            s_claims.Release(id, Citizen.Id);
            _picked.Add(id, Time.time);
            _awaitingDrop = true;
            _pickedAt = at;
            _pickedTime = Time.time;
            _ripe = null;
            WorldScan.InvalidateDrops();
            _rescanTimer = DropRescanSeconds;
        }

        /// <summary>What dropped from the crop he just picked, lying by it, that wasn't there before.</summary>
        private void FindDrop()
        {
            ItemDrop best = null;
            float bestDistance = float.MaxValue;
            foreach (ItemDrop drop in WorldScan.Drops())
            {
                if (drop == null || drop.IsPiece()) continue;
                Vector3 pos = drop.transform.position;
                if (Vector3.Distance(pos, _pickedAt) > Foraging.GatherRadius) continue;
                if (Utils.GetPrefabName(drop.gameObject) != _pickedItem) continue;
                string id = IdOf(drop.gameObject);
                if (_dropsBefore.Contains(id) || _blacklist.IsBlocked(id, Time.time) || s_claims.IsTakenByOther(id, Ground(pos), Citizen.Id, Time.time)) continue;
                float distance = Vector3.Distance(Citizen.transform.position, pos);
                if (distance >= bestDistance) continue;
                best = drop;
                bestDistance = distance;
            }
            if (best == null) return;
            _awaitingDrop = false;
            _drop = best;
            _dropId = IdOf(best.gameObject);
            if (!s_claims.TryClaim(_dropId, Ground(best.transform.position), Citizen.Id, Time.time)) ForgetDrop();
        }

        private void Gather()
        {
            if (_drop == null)
            {
                Ai.Idle();   // what he just picked is about to drop
                return;
            }
            Vector3 pos = _drop.transform.position;
            if (!Ai.GoTo(pos, 1.2f))
            {
                if (_walkStuck.Update(Vector3.Distance(Citizen.transform.position, pos), Time.time))
                {
                    _blacklist.Add(_dropId, Time.time);
                    ForgetDrop();
                    _walkStuck.Reset();
                }
                return;
            }
            _walkStuck.Reset();
            ZNetView nview = _drop.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                ForgetDrop();
                return;
            }
            if (!nview.IsOwner())
            {
                _drop.RequestOwn();
                return;
            }
            _drop.Load();
            Citizen.Carry.Add(Utils.GetPrefabName(_drop.gameObject), _drop.m_itemData.m_stack);
            Citizen.SaveCarry();
            ZNetScene.instance.Destroy(_drop.gameObject);
            ForgetDrop();
            _rescanTimer = 0f;
        }

        private bool ReachHut(ColonyBuilding hut)
        {
            Vector3 hutPos = hut.transform.position;
            if (Ai.GoTo(hutPos, HutReach))
            {
                _depositStuck.Reset();
                return true;
            }
            if (Ai.UsingDoor)
            {
                _depositStuck.Reset();
                return false;
            }
            float distance = Utils.DistanceXZ(Citizen.transform.position, hutPos);
            bool stuck = _depositStuck.Update(distance, Time.time);
            if (MoveRules.DeliverFromHere(stuck, distance)) return true;
            if (stuck) Citizen.Status = "$vc_status_cant_reach_hut";
            return false;
        }

        private void Deliver(ColonyBuilding hut, float dt, float retrySeconds)
        {
            Ai.Idle();
            _depositTimer -= dt;
            if (_depositTimer > 0f) return;
            _depositTimer = retrySeconds;
            StorageModule.DepositResult result = hut.Storage.TryDeposit(Citizen.Carry);
            if (result == StorageModule.DepositResult.Busy) return;
            // Full: the rest goes to the Town Hall or another chest with room; he waits only when every chest is full.
            _storageFull = result == StorageModule.DepositResult.StorageFull && !_fullChest.Choose(Citizen, hut);
            Citizen.SaveCarry();
            _crop = null;   // a seed in his hand went back in the chest
            if (!_storageFull) _seedsInHand.Clear();
            if (!_storageFull) _depositTimer = 0f;
        }

        private void ForgetDrop()
        {
            if (_dropId.Length > 0) s_claims.Release(_dropId, Citizen.Id);
            _drop = null;
            _dropId = "";
        }

        private static GroundPoint Ground(Vector3 v) => new GroundPoint(v.x, v.z);

        private static string IdOf(GameObject go)
        {
            ZNetView nview = go != null ? go.GetComponent<ZNetView>() : null;
            if (nview != null && nview.IsValid()) return nview.GetZDO().m_uid.ToString();
            return go != null ? go.GetInstanceID().ToString() : "";
        }
    }
}
