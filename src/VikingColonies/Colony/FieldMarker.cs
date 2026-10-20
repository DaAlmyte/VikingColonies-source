using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;
using VikingColonies.Jobs;

namespace VikingColonies.Colony
{
    /// <summary>
    /// A field marker (worker polish, 0.20; MineColonies' farmer works fields marked with a scarecrow): a sign the player
    /// places inside a colony's area that marks a square field round it, with its crop and its size, worked by the Farm it
    /// is assigned to. Saved on its own network object; changed through its owner, who checks the player may manage the
    /// colony. The sign shows the field's crop.
    /// </summary>
    public class FieldMarker : MonoBehaviour, Hoverable, Interactable
    {
        private const string RpcSetCrop = "vc_FieldSetCrop", RpcSetSize = "vc_FieldSetSize", RpcAssign = "vc_FieldAssign", RpcMessage = "vc_FieldMessage";
        private const float TickSeconds = 5f;
        private const float TextSeconds = 1f;

        private static readonly List<FieldMarker> s_all = new List<FieldMarker>();
        public static IReadOnlyList<FieldMarker> All => s_all;

        /// <summary>The sign's text, set on the prefab (the vanilla sign's own component is taken off).</summary>
        public TMPro.TMP_Text TextWidget;

        private ZNetView _nview;
        private float _timer = 2f, _textTimer;
        private string _shown;
        private readonly GoneTracker _farmGone = new GoneTracker(ColonyRules.GoneChecksFor(TickSeconds));

        public bool IsValid => _nview != null && _nview.IsValid();
        public bool IsOwner => IsValid && _nview.IsOwner();
        private ZDO Zdo => IsValid ? _nview.GetZDO() : null;

        /// <summary>Its network object's id: stable for its life, the same on every computer.</summary>
        public string Id => Zdo != null ? Zdo.m_uid.ToString() : "";
        public string ColonyId => Zdo?.GetString(SaveKeys.ColonyId, "") ?? "";
        /// <summary>The planted piece of its crop; "" = the Farm's ticked crops in turn.</summary>
        public string Crop => Zdo?.GetString(SaveKeys.FieldCrop, "") ?? "";
        public int Size => FieldSizes.Valid(Zdo?.GetInt(SaveKeys.FieldSize, FieldSizes.New) ?? FieldSizes.New);
        public string FarmId => Zdo?.GetString(SaveKeys.FieldFarm, "") ?? "";
        /// <summary>Changes when its crop, size or Farm change (the farmer looks at his plots again).</summary>
        public string Signature => $"{Crop}|{Size}|{FarmId}|{transform.position.x:0.0},{transform.position.z:0.0}";

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            if (!IsValid) return;   // the hammer's placement ghost
            _nview.Register<string>(RpcSetCrop, RPC_SetCrop);
            _nview.Register<int>(RpcSetSize, RPC_SetSize);
            _nview.Register<string>(RpcAssign, RPC_Assign);
            _nview.Register<string>(RpcMessage, RPC_Message);
            s_all.Add(this);
            WearNTear wear = GetComponent<WearNTear>();
            if (wear != null) wear.m_onDestroyed += () => Log.Info($"Field {Id} at {transform.position}: its sign broke (health or support)");
        }

        private void OnDestroy()
        {
            s_all.Remove(this);
            // Unloaded, or gone from the world (removed or broken): the log says which.
            if (_nview != null && _nview.GetZDO() is ZDO zdo && ZDOMan.instance != null && ZDOMan.instance.GetZDO(zdo.m_uid) == null)
                Log.Info($"Field {zdo.m_uid} at {transform.position} is gone from the world");
        }

        private void Update()
        {
            if (!IsValid) return;
            _textTimer -= Time.deltaTime;
            if (_textTimer <= 0f)
            {
                _textTimer = TextSeconds;
                ShowCrop();
            }
            if (!IsOwner) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = TickSeconds;
            JoinColony();
            KeepFarm();
        }

        /// <summary>The field's plots on the ground (not yet checked for water, rock or buildings: the farmer does that).</summary>
        public List<Vector3> PlotPositions() =>
            FieldGrid.Plots(CoreVectors.V(transform.position), CoreVectors.Q(transform.rotation), Size, FieldPlots.Spacing)
                .Select(p =>
                {
                    Vector3 at = CoreVectors.U(p);
                    at.y = ZoneSystem.instance.GetGroundHeight(at);
                    return at;
                }).ToList();

        /// <summary>Its crop's name ("Carrots"), or "Mixed crops" for the Farm's list.</summary>
        public string CropName()
        {
            string crop = Crop;
            if (crop.Length == 0) return Localization.instance.Localize("$vc_field_mixed");
            CropInfo info = CropList.Crops.FirstOrDefault(c => c.Piece == crop);
            return Localization.instance.Localize(info != null ? info.NameToken : crop);
        }

        private void ShowCrop()
        {
            if (TextWidget == null) return;
            string text = CropName();
            if (text == _shown) return;
            _shown = text;
            TextWidget.text = text;
        }

        // ---- Its colony and its Farm (on its owner) ----

        /// <summary>
        /// The colony whose area holds it (the Town Hall's circle first, then its Watchtowers', 1.1); kept while its Town Hall
        /// isn't loaded. Only a colony its builder
        /// may manage (final review of 0.20: a stranger's marker took a Farm's place); one placed by no player (a command) joins.
        /// </summary>
        private void JoinColony()
        {
            string colony = ColonyId;
            if (colony.Length > 0 && ColonyBuilding.FindTownHall(colony) != null) return;
            ColonyBuilding hall = ColonyAreas.BuildingAt(transform.position);
            if (hall == null || hall.ColonyId.Length == 0 || hall.ColonyId == colony) return;
            long builder = Zdo.GetLong(ZDOVars.s_creator, 0L);
            if (builder != 0L && !ColonyPermissions.MayBuildAt(hall, transform.position, builder))
            {
                if (!_strangerLogged) Log.Info($"Field {Id}: its builder may not manage colony {hall.ColonyId}; it stays out of it.");
                _strangerLogged = true;
                return;
            }
            Zdo.Set(SaveKeys.ColonyId, hall.ColonyId);
            Log.Info($"Field {Id} joined colony {hall.ColonyId}");
        }

        private bool _strangerLogged;

        /// <summary>The colony's Farms as field assignment sees them, with how many fields each works now.</summary>
        public static List<FarmSlot> FarmsOf(string colony, FieldMarker except = null) =>
            ColonyBuilding.All
                .Where(b => b != null && b.IsValid && b.TypeId == BuildingTypes.FarmId && b.ColonyId == colony && colony.Length > 0)
                .Select(b => new FarmSlot
                {
                    Id = b.Id,
                    At = CoreVectors.V(b.transform.position),
                    Level = b.Level,
                    Assigned = s_all.Count(m => m != null && m != except && m.IsValid && m.FarmId == b.Id),
                    Finished = IsFinished(b),
                })
                .ToList();

        private static bool IsFinished(ColonyBuilding farm)
        {
            ConstructionModule site = farm.GetComponent<ConstructionModule>();
            return site == null || FieldAssignment.Works(site.IsBuilt, site.IsUpgrading);
        }

        /// <summary>
        /// Keeps its Farm while it stands in the colony and has room; with no Farm, joins the nearest one that takes new
        /// fields by itself and has room, unless the player took it from its Farm or chose it one (<see cref="FieldKeeping"/>).
        /// A missing Farm counts as gone after half a minute, and only where its ground is loaded here.
        /// </summary>
        private void KeepFarm()
        {
            string colony = ColonyId;
            ColonyBuilding hall = colony.Length > 0 ? ColonyBuilding.FindTownHall(colony) : null;
            if (hall == null) return;   // its colony isn't loaded: nothing to judge
            string farmId = FarmId;
            ColonyBuilding farm = farmId.Length > 0 ? ColonyBuilding.FindById(farmId) : null;
            bool there = farm != null && farm.ColonyId == colony;
            if (there) _farmSeenAt = farm.transform.position;
            bool groundLoaded = farmId.Length > 0 && !there && FarmGroundLoaded(hall);
            bool missingLong = farmId.Length > 0 && _farmGone.Observe(there ? Presence.Present : groundLoaded ? Presence.Gone : Presence.Unknown);
            // More fields than it has room for (assigned on two computers at once): the last ones by id leave.
            bool over = there && s_all.Where(m => m != null && m.IsValid && m.FarmId == farmId).OrderBy(m => m.Id).ToList().IndexOf(this)
                                 >= FieldAssignment.Capacity(farm.Level);
            switch (FieldKeeping.Next(farmId.Length > 0, there, groundLoaded, missingLong, over, ByHand))
            {
                case FieldFarmStep.Keep:
                case FieldFarmStep.Wait:
                    return;
                case FieldFarmStep.Leave:
                case FieldFarmStep.FarmGone:
                    Log.Info($"Field {Id}: its Farm {farmId} is {(there ? "full" : "gone")}; it looks for another.");
                    Zdo.Set(SaveKeys.FieldFarm, "");
                    if (ByHand) Zdo.Set(SaveKeys.FieldByHand, false);   // nobody's choice now: it may join another by itself
                    _farmSeenAt = null;
                    break;
            }
            List<FarmSlot> farms = FarmsOf(colony, this).Where(f => ColonyBuilding.FindById(f.Id)?.GetComponent<FieldsModule>()?.AutoAssign ?? false).ToList();
            string chosen = FieldAssignment.Choose(farms, CoreVectors.V(transform.position));
            if (chosen.Length == 0) return;
            Zdo.Set(SaveKeys.FieldFarm, chosen);
            Log.Info($"Field {Id} ({CropName()}, {Size}x{Size}) joined the Farm {chosen}");
        }

        /// <summary>The player assigned it, or took it from its Farm: auto-assign leaves it as he set it.</summary>
        public bool ByHand => Zdo != null && Zdo.GetBool(SaveKeys.FieldByHand, false);

        private Vector3? _farmSeenAt;   // where its Farm stood when last loaded here

        /// <summary>
        /// The ground its Farm stood on is loaded here, so a missing Farm really is gone (not merely unloaded); a Farm not
        /// seen this session: the colony's whole area must be (final review of 0.20).
        /// </summary>
        private bool FarmGroundLoaded(ColonyBuilding hall)
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return false;
            if (_farmSeenAt.HasValue) return scene.IsAreaReady(_farmSeenAt.Value);
            Vector3 c = hall.transform.position;
            float r = HallRules.PerksAt(hall.Level).Radius;
            return scene.IsAreaReady(c) && scene.IsAreaReady(c + Vector3.forward * r) && scene.IsAreaReady(c - Vector3.forward * r)
                   && scene.IsAreaReady(c + Vector3.right * r) && scene.IsAreaReady(c - Vector3.right * r);
        }

        /// <summary>Whether the player at this computer may manage its colony (the windows grey out the rest).</summary>
        public bool LocalMayManage()
        {
            ColonyBuilding hall = ColonyBuilding.FindTownHall(ColonyId);
            return hall != null && ColonyPermissions.LocalMayManage(hall);
        }

        // ---- Requests (any computer asks; the owner checks and changes) ----

        public void RequestSetCrop(string crop) => _nview.InvokeRPC(RpcSetCrop, crop ?? "");
        public void RequestSetSize(int size) => _nview.InvokeRPC(RpcSetSize, size);
        /// <summary>Assign it to a Farm, or "" to take it from its Farm.</summary>
        public void RequestAssign(string farmId) => _nview.InvokeRPC(RpcAssign, farmId ?? "");

        private bool Allowed(long sender)
        {
            ColonyBuilding hall = ColonyBuilding.FindTownHall(ColonyId);
            if (hall != null && ColonyPermissions.SenderMayManage(hall, sender)) return true;
            Reply(sender, hall == null ? "$vc_field_no_colony" : "$vc_not_your_colony");
            return false;
        }

        private void RPC_SetCrop(long sender, string crop)
        {
            if (!IsOwner || !Allowed(sender)) return;
            Zdo.Set(SaveKeys.FieldCrop, crop ?? "");
        }

        private void RPC_SetSize(long sender, int size)
        {
            if (!IsOwner || !Allowed(sender)) return;
            Zdo.Set(SaveKeys.FieldSize, FieldSizes.Valid(size));
        }

        private void RPC_Assign(long sender, string farmId)
        {
            if (!IsOwner || !Allowed(sender)) return;
            if (string.IsNullOrEmpty(farmId))
            {
                Zdo.Set(SaveKeys.FieldFarm, "");
                Zdo.Set(SaveKeys.FieldByHand, true);   // he took it away: it stays without a Farm (final review of 0.20)
                Log.Info($"Field {Id} taken from its Farm by hand");
                return;
            }
            FarmSlot farm = FarmsOf(ColonyId, this).FirstOrDefault(f => f.Id == farmId);
            if (farm == null || !FieldAssignment.HasRoom(farm))
            {
                Reply(sender, "$vc_field_farm_full");
                return;
            }
            Zdo.Set(SaveKeys.FieldFarm, farmId);
            Zdo.Set(SaveKeys.FieldByHand, true);
            Log.Info($"Field {Id} assigned to the Farm {farmId}");
        }

        private void Reply(long sender, string token) => _nview.InvokeRPC(sender, RpcMessage, token);

        /// <summary>Worker test: a size request as if <paramref name="sender"/> had asked (a stranger is refused).</summary>
        internal void SetSizeAsForTest(long sender, int size) => RPC_SetSize(sender, size);

        private void RPC_Message(long sender, string token)
        {
            string text = Localization.instance.Localize(token);
            if (Gui.Windows.Field.IsOpen) Gui.Windows.Field.ShowMessage(text);
            else if (Gui.Windows.Hut.IsOpen) Gui.Windows.Hut.ShowMessage(text);
            else if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.Center, text);
        }

        // ---- Hover and E ----

        /// <summary>"Freydis's Farm", "a Farm", or "No Farm".</summary>
        public static string FarmLabel(ColonyBuilding farm)
        {
            if (farm == null) return Localization.instance.Localize("$vc_field_no_farm_short");
            WorkerModule worker = farm.GetComponent<WorkerModule>();
            if (worker != null && worker.FindWorker(out Citizen loaded, out RosterEntry entry) is WorkerModule.WorkerState state)
            {
                string name = state == WorkerModule.WorkerState.Loaded ? loaded.CitizenName : state == WorkerModule.WorkerState.Elsewhere ? entry.Name : "";
                if (name.Length > 0) return string.Format(Localization.instance.Localize("$vc_field_farm_of"), name);
            }
            return Localization.instance.Localize("$vc_hut_farm");
        }

        public string GetHoverText()
        {
            if (!IsValid) return "";
            string farm = FarmLabel(ColonyBuilding.FindById(FarmId));
            return Localization.instance.Localize(string.Format(Localization.instance.Localize("$vc_field_hover"), CropName(), Size, farm)
                + "\n[<color=yellow><b>$KEY_Use</b></color>] $vc_field_open");
        }

        public string GetHoverName() => Localization.instance.Localize("$vc_field_marker");

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !IsValid) return false;
            Gui.Windows.Field.OpenOn(this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
