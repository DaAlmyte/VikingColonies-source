using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Colony;
using VikingColonies.Config;
using VikingColonies.Core;
using VikingColonies.Jobs;

namespace VikingColonies.Citizens
{
    /// <summary>
    /// A settler's body. Its data lives in one saved <see cref="CitizenRecord"/>: the owner keeps it in memory and
    /// writes it back; other computers read the latest saved copy.
    /// </summary>
    public class Citizen : MonoBehaviour
    {
        private const string RecordKey = SaveKeys.Citizen;
        private const string RpcEmploy = "vc_Employ", RpcUnemploy = "vc_Unemploy", RpcJoinColony = "vc_JoinColony", RpcHome = "vc_Home";
        private const string RpcRecall = "vc_Recall", RpcRecallResult = "vc_RecallResult", RpcRecallToHut = "vc_RecallToHut";
        // v1 keys, read once to convert old settlers
        private const string V1Colony = "vc_colony_id", V1Name = "vc_name", V1Model = "vc_model", V1Hair = "vc_hair",
            V1Beard = "vc_beard", V1Skin = "vc_skin", V1HairColor = "vc_hair_color", V1Carry = "vc_carry", V1Status = "vc_status";

        private static readonly List<Citizen> s_all = new List<Citizen>();

        private static readonly float[][] SkinTones =
        {
            new[] { 1f, 1f, 1f }, new[] { 0.9f, 0.78f, 0.68f }, new[] { 0.72f, 0.56f, 0.45f }, new[] { 0.5f, 0.37f, 0.28f },
        };

        private static readonly float[][] HairColors =
        {
            new[] { 0.95f, 0.8f, 0.5f }, new[] { 0.55f, 0.32f, 0.16f }, new[] { 0.18f, 0.12f, 0.08f },
            new[] { 0.65f, 0.25f, 0.1f }, new[] { 0.6f, 0.6f, 0.6f },
        };

        private ZNetView _nview;
        private VisEquipment _vis;
        private CitizenRecord _record;
        private bool _recordIsFresh;   // _record was loaded while this computer ran the citizen

        public static IReadOnlyList<Citizen> All => s_all;

        public Humanoid Body { get; private set; }
        public CarryLoad Carry { get; private set; } = new CarryLoad();

        /// <summary>
        /// What he holds for a free-time errand (fuel for a fire, stock to carry; 0.21): apart from <see cref="Carry"/> so his
        /// job doesn't deliver it, but saved with it, so a reload, another computer taking him over or his cairn keeps it
        /// (review of 0.21: it was in memory only, and lost).
        /// </summary>
        public CarryLoad InHand { get; } = new CarryLoad();

        /// <summary>When a monster last hit this settler (Time.time); negative infinity if never. Set on the owner.</summary>
        public double LastMonsterHitTime { get; private set; } = double.NegativeInfinity;

        /// <summary>Self-test: as if a monster had just hit him.</summary>
        internal void NoteMonsterHitForTest() => LastMonsterHitTime = Time.time;

        /// <summary>When this settler last took damage (Time.time); negative infinity if never. Set on the owner.</summary>
        public double LastDamageTime { get; private set; } = double.NegativeInfinity;

        private ZDO Zdo => _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
        public bool IsOwner => Zdo != null && _nview.IsOwner();

        public CitizenRecord Record => IsOwner && _record != null ? _record : ReadRecord();

        public string Id => Record.Id;
        public string ColonyId => Record.ColonyId;
        public string CitizenName => Record.Name;

        public string DisplayName
        {
            get
            {
                CitizenRecord r = Record;
                return DisplayNameFor(r.Name, r.Job);
            }
        }

        /// <summary>"Ingrid the Lumberjack" in this player's words; the job title, or "Jobless", for a settler without a name.</summary>
        public static string DisplayNameFor(string name, string jobId) =>
            NorseNames.Display(name, JobDefinitions.TitleOf(jobId), Localization.instance.Localize("$vc_jobless"));

        /// <summary>What he does in free time and with whom (<see cref="ActivityWords.Encode"/>); "" when not in free time. Every computer reads it.</summary>
        public string Doing => Zdo != null ? Zdo.GetString(SaveKeys.Doing, "") : "";

        /// <summary>A $vc_status_* token, shown when players hover over the settler. Only the owner can set it.</summary>
        public string Status
        {
            get => Record.Status;
            set
            {
                if (!IsOwner || _record == null || _record.Status == value) return;
                _record.Status = value;
                History.Add(value, Time.time);   // for a bug report (0.27)
                Save();
            }
        }

        /// <summary>His last states on this computer (vc_bugreport, 0.27); not saved.</summary>
        internal readonly StateHistory History = new StateHistory();

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _vis = GetComponent<VisEquipment>();
            Body = GetComponent<Humanoid>();
            if (Zdo == null) return;
            LoadRecord();
            s_all.Add(this);
            _nview.Register<ZPackage>(RpcEmploy, RPC_Employ);
            _nview.Register<string>(RpcUnemploy, RPC_Unemploy);
            _nview.Register<string, Vector3>(RpcJoinColony, RPC_JoinColony);
            _nview.Register<ZPackage>(RpcHome, RPC_Home);
            _nview.Register(RpcRecall, RPC_Recall);
            _nview.Register(RpcGuardKill, RPC_GuardKill);
            _nview.Register(RpcRecallToHut, RPC_RecallToHut);
            _nview.Register<string>(RpcRecallResult, RPC_RecallResult);
            Body.m_onDeath += () => ColonyEvents.RaiseCitizenDied(this);
            Body.m_onDamaged += OnDamaged;
        }

        private void Start()
        {
            ApplyAppearance();
            ApplyClothes();
        }

        private static readonly HashSet<string> s_clothes = new HashSet<string>(Wardrobe.AllItems);

        private Dictionary<string, string> _armour = new Dictionary<string, string>();
        private readonly HashSet<string> _armourWorn = new HashSet<string>();

        /// <summary>
        /// Owner: armour a guard wears (1.2, slot → item, copies from the colony's chests) in place of his trade's clothes in
        /// those slots; empty for none. Put on at once.
        /// </summary>
        internal void SetArmour(Dictionary<string, string> armour)
        {
            _armour = armour != null ? new Dictionary<string, string>(armour) : new Dictionary<string, string>();
            _armourWorn.UnionWith(_armour.Values);
            ApplyClothes();
        }

        /// <summary>What he wears: his trade's clothes, with a guard's armour in place of the pieces in its slots (1.2).</summary>
        private string[] Outfit()
        {
            string[] trade = Wardrobe.For(_record.Job);
            if (_armour.Count == 0) return trade;
            return Wardrobe.Merge(trade.Select(n => new KeyValuePair<string, string>(SlotOf(n), n)), _armour);
        }

        private static string SlotOf(string prefab)
        {
            GameObject go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData.m_shared.m_itemType.ToString() : "";
        }

        /// <summary>Something he wears for his trade, or armour he wore as a guard (taken off when no longer wanted).</summary>
        private bool IsOutfit(string name) => s_clothes.Contains(name) || _armourWorn.Contains(name);

        /// <summary>
        /// Owner: the clothes of his trade (0.16), from the game's own items in his own inventory (never his cairn, which
        /// holds what he carries), and a guard's armour (1.2). Items another mod removed are skipped.
        /// </summary>
        public void ApplyClothes()
        {
            if (!IsOwner || Body == null || ObjectDB.instance == null || _record == null) return;
            Inventory inventory = Body.GetInventory();
            if (inventory == null) return;
            string[] wanted = Outfit();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems().ToList())
            {
                string name = item.m_dropPrefab != null ? item.m_dropPrefab.name : "";
                if (!IsOutfit(name) || wanted.Contains(name)) continue;
                if (Body.IsItemEquiped(item)) Body.UnequipItem(item, false);
                inventory.RemoveItem(item);
            }
            foreach (string name in wanted)
            {
                ItemDrop.ItemData item = inventory.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == name);
                if (item == null)
                {
                    GameObject prefab = ObjectDB.instance.GetItemPrefab(name);
                    if (prefab == null) continue;
                    inventory.AddItem(prefab, 1);
                    item = inventory.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == name);
                }
                if (item != null && !Body.IsItemEquiped(item)) Body.EquipItem(item, false);
            }
        }

        /// <summary>He wears his trade's clothes and no other (the AI looks every few seconds: the game puts none on mid-swing).</summary>
        internal bool DressedRight()
        {
            Inventory inventory = Body != null ? Body.GetInventory() : null;
            if (inventory == null || _record == null || ObjectDB.instance == null) return true;
            string[] wanted = Outfit();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                string name = item.m_dropPrefab != null ? item.m_dropPrefab.name : "";
                if (IsOutfit(name) && (!wanted.Contains(name) || !Body.IsItemEquiped(item))) return false;
            }
            foreach (string name in wanted)
                if (ObjectDB.instance.GetItemPrefab(name) != null
                    && !inventory.GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == name)) return false;
            return true;
        }

        private void OnDestroy() => s_all.Remove(this);

        private void OnDamaged(float damage, Character attacker)
        {
            LastDamageTime = Time.time;
            if (attacker == null || attacker.IsPlayer() || attacker.GetComponent<Citizen>() != null) return;
            LastMonsterHitTime = Time.time;
            int today = Today();
            // A computer that has just taken him over reads his record first, so an old copy is never saved (review I4).
            if (!PrepareOwnerChange() || _record.HitDay == today) return;
            _record.HitDay = today;   // "a monster hit him" (mood, 0.17)
            Save();
        }

        /// <summary>Loads the saved record (converting a v1 settler if needed). The owner saves the result.</summary>
        public void LoadRecord()
        {
            ZDO zdo = Zdo;
            if (zdo == null) return;
            string saved = zdo.GetString(RecordKey, "");
            CitizenRecord record = string.IsNullOrEmpty(saved) ? FromV1Keys(zdo) : CitizenRecord.Decode(saved);
            if (string.IsNullOrEmpty(record.Id) && IsOwner) record.Id = Guid.NewGuid().ToString("N");
            int body = NorseNames.BodyFor(record.Name, record.Model);
            if (IsOwner && body != record.Model)
            {
                // Names used to be picked without looking at the body: give a Sigrid a woman's body, an Ulf a man's.
                record.Model = body;
                Log.Info($"{record.Name}'s body now matches the name");
            }
            string beard = NorseNames.BeardFor(record.Model, record.Beard);
            if (IsOwner && beard != record.Beard)
            {
                record.Beard = beard;
                Log.Info($"{record.Name}'s beard removed (women have none)");
            }
            if (IsOwner) FillInIdentity(record);
            _record = record;
            _recordIsFresh = IsOwner;
            Carry = CarryLoad.Deserialize(record.Carry);
            InHand.Clear();   // saved in his carry
            if (IsOwner && record.Encode() != saved)
            {
                zdo.Set(RecordKey, record.Encode());
                // Only a real v1 settler: a new one's record before InitNew has no name (0.18: "Converted v1 settler  to …").
                if (string.IsNullOrEmpty(saved) && record.Name.Length > 0) Log.Info($"Converted v1 settler {record.Name} to a citizen record");
            }
        }

        /// <summary>
        /// A settler from before 0.16 (or brought back from a keepsake made then): who he is comes from his id (always the
        /// same), and his days count from now. Nothing for a record with no name yet (a new settler before InitNew).
        /// </summary>
        private static void FillInIdentity(CitizenRecord record)
        {
            if (string.IsNullOrEmpty(record.Id) || string.IsNullOrEmpty(record.Name)) return;
            if (record.Calling.Length > 0 && record.Trait.Length > 0 && record.Origin.Length > 0 && record.Arrived != CitizenRecord.ArrivedUnknown) return;
            if (record.Calling.Length == 0) record.Calling = Callings.FromId(record.Id, Jobs.JobDefinitions.Ids);
            if (record.Trait.Length == 0) record.Trait = Traits.Name(Traits.FromId(record.Id));
            if (record.Origin.Length == 0) record.Origin = Origins.FromId(record.Id);
            if (record.Arrived == CitizenRecord.ArrivedUnknown) record.Arrived = Today();
            Log.Info($"{record.Name} is {record.Trait}, good at {record.Calling}, {record.Origin}");
        }

        public void Save()
        {
            if (!IsOwner || _record == null) return;
            _record.Carry = CarryLoad.Together(Carry, InHand).Serialize();
            Zdo.Set(RecordKey, _record.Encode());
        }

        public void SaveCarry() => Save();

        /// <summary>His trait (0.16), from his record; from his id for a settler from before.</summary>
        public Trait TraitKind => Traits.Parse(Record.Trait, Traits.FromId(Id));

        /// <summary>Called once, right after spawning, by the computer that spawned the citizen (its owner).</summary>
        /// <param name="calling">The trade he's good at (0.16); null: one picked for him.</param>
        public void InitNew(string colonyId, Vector3 colonyPos, System.Random rng, string calling = null)
        {
            int model = rng.Next(2);   // 0 = male body, 1 = female body
            var taken = new HashSet<string>(s_all.Where(c => c != null && c != this && c.ColonyId == colonyId).Select(c => c.CitizenName));
            _record = new CitizenRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = NorseNames.PickUnused(rng, model == 1, taken),
                Model = model,
                Hair = PickCustomization("Hair", rng),
                Beard = model == 0 ? PickCustomization("Beard", rng) : NorseNames.NoBeard,
                Skin = SkinTones[rng.Next(SkinTones.Length)],
                HairColor = HairColors[rng.Next(HairColors.Length)],
                ColonyId = colonyId,
                ColonyPos = ToFloats(colonyPos),
            };
            _record.Calling = calling ?? Callings.Pick(rng, Jobs.JobDefinitions.Ids, new string[0]);
            _record.Trait = Traits.Name(Traits.FromId(_record.Id));
            _record.Origin = Origins.Pick(rng);
            _record.Arrived = Today();
            _record.JoblessSince = Today();   // he comes without a job (mood, 0.17)
            Carry = new CarryLoad();
            InHand.Clear();
            Save();
            ApplyAppearance();
            ApplyClothes();
            ColonyEvents.RaiseCitizenSpawned(this);
        }

        /// <summary>Owner: he is walking in from the colony's edge (0.16), or has got there.</summary>
        public void MarkArriving(bool arriving)
        {
            if (!IsOwner || _record == null || _record.Arriving == arriving) return;
            _record.Arriving = arriving;
            Save();
        }

        private static int Today() => EnvMan.instance != null && ZNet.instance != null ? EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) : 0;

        /// <summary>Self-test: his calling set by hand.</summary>
        internal void SetCallingForTest(string calling)
        {
            if (!IsOwner || _record == null) return;
            _record.Calling = calling;
            Save();
        }

        /// <summary>Self-test: a record as 0.15 wrote it (no calling, trait, origin or arrival day), read again.</summary>
        internal void ForgetIdentityForTest()
        {
            if (!IsOwner || _record == null) return;
            _record.Calling = _record.Trait = _record.Origin = "";
            _record.Arrived = CitizenRecord.ArrivedUnknown;
            Zdo.Set(SaveKeys.Citizen, _record.Encode());
            LoadRecord();
        }

        /// <summary>Self-test: his clothes off and gone, as when he was fired mid-swing and the new ones didn't go on.</summary>
        internal void UndressForTest()
        {
            Inventory inventory = Body != null ? Body.GetInventory() : null;
            if (!IsOwner || inventory == null) return;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems().ToList())
            {
                if (item.m_dropPrefab == null || !s_clothes.Contains(item.m_dropPrefab.name)) continue;
                if (Body.IsItemEquiped(item)) Body.UnequipItem(item, false);
                inventory.RemoveItem(item);
            }
        }

        /// <summary>The names of the items he wears (the self-test reads his clothes).</summary>
        internal IEnumerable<string> WornItems() => Body == null || Body.GetInventory() == null ? Enumerable.Empty<string>()
            : Body.GetInventory().GetEquippedItems().Where(i => i.m_dropPrefab != null).Select(i => i.m_dropPrefab.name);

        /// <summary>
        /// The colony brings back a settler whose body was lost (Town Hall › Citizens › Bring back): the same settler from
        /// his keepsake, in this colony, empty-handed.
        /// </summary>
        public void InitRestored(CitizenRecord kept, string colonyId, Vector3 colonyPos)
        {
            _record = kept;
            _record.ColonyId = colonyId;
            _record.ColonyPos = ToFloats(colonyPos);
            FillInIdentity(_record);   // a keepsake from before 0.16 (0.18)
            _record.Status = "";
            _record.Carry = "";
            _record.HallLostAt = ColonyGrace.NotLost;
            Carry = new CarryLoad();
            InHand.Clear();
            Save();
            ApplyAppearance();
            ColonyEvents.RaiseCitizenSpawned(this);
        }

        /// <summary>
        /// Whether this computer knows a settler's body anywhere in the world (loaded or not). The world's host knows every
        /// one; another computer knows the bodies near its player.
        /// </summary>
        public static bool BodyKnown(string citizenId)
        {
            if (ZDOMan.instance == null || string.IsNullOrEmpty(citizenId)) return false;
            var zdos = new List<ZDO>();
            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(CitizenPrefab.PrefabName, zdos, ref index)) { }
            foreach (ZDO zdo in zdos)
            {
                string saved = zdo.GetString(RecordKey, "");
                if (saved.Length > 0 && saved.Contains(citizenId) && CitizenRecord.Decode(saved)?.Id == citizenId) return true;
            }
            return false;
        }

        // Other computers never write a citizen directly: the computer running it saves it many times a second, so a
        // direct write could be lost or undo itself. They ask that computer instead, which checks the change still
        // makes sense (still jobless, still the same job, still without a colony) and applies it.

        /// <summary>Asks the computer running this citizen to employ it at <paramref name="workplace"/>.</summary>
        public void RequestEmploy(ColonyBuilding workplace, string jobId)
        {
            var pkg = new ZPackage();
            pkg.Write(workplace.ColonyId);
            pkg.Write(workplace.Id);
            pkg.Write(workplace.transform.position);
            pkg.Write(jobId ?? "");
            Send(RpcEmploy, pkg);
        }

        /// <summary>Asks the computer running this citizen to fire it, if it still works at <paramref name="workplaceId"/>.</summary>
        public void RequestUnemploy(string workplaceId) => Send(RpcUnemploy, workplaceId);

        /// <summary>Asks the computer running this citizen to make that bed his home ("" for none), if he's still of the colony.</summary>
        public void RequestHome(string colonyId, string bedId, Vector3 bedPos)
        {
            var pkg = new ZPackage();
            pkg.Write(colonyId ?? "");
            pkg.Write(bedId ?? "");
            pkg.Write(bedPos);
            Send(RpcHome, pkg);
        }

        /// <summary>How fast he works: slower while hungry (<see cref="Meals"/>), faster in his calling, and by his trait (0.16).</summary>
        public float WorkSpeed
        {
            get
            {
                CitizenRecord r = Record;
                return WorkPace.Factor(ColonyConfig.SettlersEat.Value && r.Hungry, ColonyConfig.HungryWorkSpeed.Value,
                    Callings.Matches(r.Calling, r.Job), ColonyConfig.CallingBonus.Value, TraitKind, MoodLevels.WorkFactor(MoodLevel));
            }
        }

        /// <summary>How he feels (0.17); content with <c>Colony.Mood</c> off, or before it is first worked out.</summary>
        public MoodLevel MoodLevel => ColonyConfig.Mood.Value ? MoodLevels.Of(Record.Mood) : MoodLevel.Content;

        /// <summary>What he ate last (he'd rather not have it again, 0.17), or null.</summary>
        public string LastMeal => MealMemory.Last(Record.LastMeals);

        /// <summary>Owner only: his mood and its reasons, saved when they change (<see cref="MoodTracker"/>).</summary>
        public void SetMood(int mood, string why)
        {
            if (!IsOwner || _record == null || _record.Mood == mood && _record.WhyMood == (why ?? "")) return;
            _record.Mood = mood;
            _record.WhyMood = why ?? "";
            Save();
        }

        /// <summary>Owner only: he spent this evening by the colony's burning fire, and had company once two others were near.</summary>
        public void MarkEvening(int day, bool company)
        {
            if (!IsOwner || _record == null) return;
            if (_record.EveningDay == day && (_record.EveningCompany || !company)) return;
            _record.EveningCompany = company || _record.EveningDay == day && _record.EveningCompany;
            _record.EveningDay = day;
            Save();
        }

        /// <summary>Owner only: a jobless settler from before 0.17 counts his jobless days from today.</summary>
        public void NoteJobless(int day)
        {
            if (!IsOwner || _record == null || _record.IsEmployed || _record.JoblessSince != CitizenRecord.NoDay) return;
            _record.JoblessSince = day;
            Save();
        }

        /// <summary>Owner only: he had his meal of the day, <paramref name="item"/> (or found nothing and is hungry).</summary>
        public void MarkMeal(int day, bool hungry, string item = null)
        {
            if (!IsOwner || _record == null) return;
            _record.LastMealDay = day;
            _record.Hungry = hungry;
            if (!hungry) MealMemory.Add(_record.LastMeals, item);   // his last 5 meals (mood, 0.17)
            Save();
        }

        /// <summary>Self-test: he remembers no meals (the plainest-food check: a leftover settler may have had raspberries).</summary>
        internal void ForgetMealsForTest()
        {
            if (!IsOwner || _record == null) return;
            _record.LastMeals.Clear();
            Save();
        }

        /// <summary>Self-test: he found nothing to eat today, and remembers no meals.</summary>
        internal void MakeHungryForTest()
        {
            if (!IsOwner || _record == null) return;
            _record.LastMealDay = Today();
            _record.Hungry = true;
            _record.LastMeals.Clear();
            Save();
        }

        /// <summary>Self-test: his mood set by hand (<see cref="MoodTracker.HoldForTest"/> keeps it).</summary>
        internal void SetMoodForTest(int mood, string why = "")
        {
            if (!IsOwner || _record == null) return;
            _record.Mood = mood;
            _record.WhyMood = why ?? "";
            Save();
        }

        /// <summary>Self-test: his last meal was yesterday, so today's is due.</summary>
        internal void MissMealForTest()
        {
            if (!IsOwner || _record == null || EnvMan.instance == null || ZNet.instance == null) return;
            _record.LastMealDay = Math.Max(0, EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) - 1);
            Save();
        }

        /// <summary>Owner only: he has no home any more (his Longhouse is gone).</summary>
        public void LeaveHome()
        {
            if (!IsOwner || _record == null || !_record.HasHome) return;
            Log.Info($"{_record.Name}'s home is gone.");
            _record.ClearHome();
            Save();
        }

        /// <summary>The Town Hall window's Recall: asks the computer running him to bring him to the Town Hall.</summary>
        public void RequestRecall() => Send(RpcRecall);

        /// <summary>Asks the computer running this citizen to bring him back to his hut (the hut window's Recall, 0.20).</summary>
        public void RequestRecallToHut() => Send(RpcRecallToHut);

        /// <summary>Self-test: a recall request as if <paramref name="sender"/> had asked (a stranger is refused).</summary>
        internal void RecallAsForTest(long sender) => RPC_Recall(sender);

        /// <summary>Asks the computer running this citizen to join the colony, if it still has none.</summary>
        public void RequestJoinColony(string colonyId, Vector3 hallPos) => Send(RpcJoinColony, colonyId, hallPos);

        private void Send(string rpc, params object[] args)
        {
            if (Zdo == null) return;
            if (!Zdo.HasOwner()) _nview.ClaimOwnership();   // nobody runs this citizen yet, so taking it over is safe
            _nview.InvokeRPC(rpc, args);                     // runs on the owner: right here when that's us
        }

        private void RPC_Employ(long sender, ZPackage pkg)
        {
            string colonyId = pkg.ReadString();
            string workplaceId = pkg.ReadString();
            Vector3 workplacePos = pkg.ReadVector3();
            string jobId = pkg.ReadString();
            if (!PrepareOwnerChange()) return;
            if (!ColonyRules.CanEmploy(_record, colonyId))
            {
                Log.Info($"{_record.Name} wasn't hired by {workplaceId}: already working or in another colony.");
                return;
            }
            _record.Workplace = workplaceId;
            _record.WorkplacePos = ToFloats(workplacePos);
            _record.Job = jobId;
            _record.Arriving = false;   // hired on his way in: he goes to his hut
            _record.JoblessSince = CitizenRecord.NoDay;
            Save();
            ApplyClothes();
            ColonyEvents.RaiseCitizenHired(this);
            Gui.HuginTips.ShowNear(transform.position, Gui.HuginTips.Worker);
            string hutWord = Saga.HutWord(ColonyBuilding.FindById(workplaceId));
            if (hutWord.Length > 0) Saga.Record(colonyId, "hired", _record.Name, hutWord);   // a hut not loaded here: no half line
        }

        private void RPC_Unemploy(long sender, string workplaceId)
        {
            if (PrepareOwnerChange() && ColonyRules.CanUnemploy(_record, workplaceId)) Unemploy();
        }

        private void RPC_Home(long sender, ZPackage pkg)
        {
            string colonyId = pkg.ReadString();
            string bedId = pkg.ReadString();
            Vector3 bedPos = pkg.ReadVector3();
            if (!PrepareOwnerChange()) return;   // first: it reads his latest record (review of 0.20, bug 5)
            bool samePlace = bedId.Length == 0 || (_record.HomePos != null && Utils.DistanceXZ(ToVector3(_record.HomePos), bedPos) < 0.3f);
            if (_record.ColonyId != colonyId || (_record.Home == bedId && samePlace)) return;
            if (bedId.Length == 0) _record.ClearHome();
            else
            {
                _record.Home = bedId;
                _record.HomePos = ToFloats(bedPos);
            }
            Save();
            Log.Info(bedId.Length == 0 ? $"{_record.Name} has no home now." : $"{_record.Name}'s home is bed {bedId}.");
        }

        /// <summary>
        /// Whoever may manage his colony (its Town Hall's rules, as the hut windows') may call him back; the asker is told
        /// how it went. Seen in review: the window said "called back" also when he couldn't be.
        /// </summary>
        private void RPC_Recall(long sender)
        {
            string result;
            if (!PrepareOwnerChange()) result = "$vc_recall_try_again";   // he changed hands on the way
            else
            {
                ColonyBuilding hall = ColonyBuilding.FindTownHall(_record.ColonyId);
                AI.CitizenAI ai = GetComponent<AI.CitizenAI>();
                if (hall == null || ai == null) result = "$vc_recall_no_hall";
                else if (!ColonyPermissions.SenderMayManage(hall, sender)) result = "$vc_not_your_colony";
                else if (!ai.Recall()) result = "$vc_recall_no_spot";
                else result = ai.IndoorsNow ? "$vc_recalled_goes_in" : "$vc_recalled";
            }
            if (result != "$vc_recalled" && result != "$vc_recalled_goes_in") Log.Info($"{CitizenName} wasn't called back ({result}).");
            _nview.InvokeRPC(sender, RpcRecallResult, result);
        }

        /// <summary>
        /// Back to his hut (worker polish, 0.20; MineColonies' Recall Workers): whoever may manage his hut's colony may ask;
        /// he is put on a free spot just outside its door. The asker is told how it went.
        /// </summary>
        private void RPC_RecallToHut(long sender)
        {
            string result;
            if (!PrepareOwnerChange()) result = "$vc_recall_try_again";
            else
            {
                ColonyBuilding hut = ColonyBuilding.FindById(_record.Workplace);
                AI.CitizenAI ai = GetComponent<AI.CitizenAI>();
                if (hut == null || ai == null) result = "$vc_recall_no_hut";
                else if (!ColonyPermissions.SenderMayManage(hut, sender)) result = "$vc_not_your_colony";
                else if (!ai.RecallToHut()) result = "$vc_recall_no_spot_hut";
                else result = ai.IndoorsNow ? "$vc_recalled_hut_goes_in" : "$vc_recalled_to_hut";
            }
            if (result != "$vc_recalled_to_hut" && result != "$vc_recalled_hut_goes_in") Log.Info($"{CitizenName} wasn't called back to his hut ({result}).");
            _nview.InvokeRPC(sender, RpcRecallResult, result);
        }

        /// <summary>On the asker's computer: how the recall went, in the Town Hall or hut window (or the middle of the screen).</summary>
        private void RPC_RecallResult(long sender, string token)
        {
            string text = SafeText.Format(Localization.instance.Localize(token), CitizenName);   // the token is the sender's
            if (Gui.Windows.TownHall.IsOpen) Gui.Windows.TownHall.ShowMessage(text);
            else if (Gui.Windows.Hut.IsOpen) Gui.Windows.Hut.ShowMessage(text);
            else if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.Center, text);
        }

        private void RPC_JoinColony(long sender, string colonyId, Vector3 hallPos)
        {
            if (!PrepareOwnerChange() || !ColonyRules.CanJoinColony(_record, colonyId)) return;
            _record.ColonyId = colonyId;
            _record.ColonyPos = ToFloats(hallPos);
            _record.ClearWorkplace();
            Save();
            Log.Info($"{_record.Name} joined colony {colonyId}");
        }

        private const string RpcGuardKill = "vc_GuardKill";

        /// <summary>A monster this guard killed (1.3): counted by his own computer (asked from the monster's).</summary>
        internal void CountKill()
        {
            if (IsOwner) AddKill();
            else if (_nview != null && _nview.IsValid()) _nview.InvokeRPC(RpcGuardKill);
        }

        private void RPC_GuardKill(long sender)
        {
            if (PrepareOwnerChange()) AddKill();
        }

        private void AddKill()
        {
            if (_record == null) return;
            _record.Kills++;
            Save();
            Log.Info($"{_record.Name} has killed {_record.Kills} monsters");
        }

        /// <summary>True when this computer runs the citizen and holds its latest record.</summary>
        private bool PrepareOwnerChange()
        {
            if (!IsOwner) return false;   // it changed hands on the way; the asker tries again on its next check
            if (!_recordIsFresh) LoadRecord();
            return _record != null;
        }

        /// <summary>
        /// Owner only: practice from one action at his work (a woodcutting hit, a plot worked, a pick, a piece built),
        /// at <c>Colony.LearningRate</c>; logs a level-up (0.22: every worker has a skill).
        /// </summary>
        public void Practise(WorkSkill skill)
        {
            if (!IsOwner || _record == null || skill == WorkSkill.None) return;
            bool levelledUp = _record.Practise(skill, SettlerSkill.PracticePerHit(ColonyConfig.LearningRate.Value));
            Save();
            if (levelledUp) Log.Info($"{_record.Name}'s {skill} is now {_record.Level(skill):0}");
        }

        /// <summary>How fast he works at a skill's work: <see cref="WorkSpeed"/>, twice as fast at 100 (0.22).</summary>
        public float SkillSpeed(WorkSkill skill) => WorkSpeed * SettlerSkill.SpeedFactor(Record.Level(skill));

        /// <summary>Owner only (called by the citizen's own brain): drop the job.</summary>
        public void Unemploy()
        {
            if (!IsOwner || _record == null || !_record.IsEmployed) return;
            string hut = Saga.HutWord(ColonyBuilding.FindById(_record.Workplace));
            _record.ClearWorkplace();
            _record.JoblessSince = Today();
            Save();
            ApplyClothes();
            ColonyEvents.RaiseCitizenFired(this);
            if (hut.Length > 0) Saga.Record(_record.ColonyId, "left", _record.Name, hut);
        }

        /// <summary>Owner only (called by the citizen's own brain): leave a colony whose Town Hall is gone.</summary>
        public void LeaveColony()
        {
            if (!IsOwner || _record == null) return;
            _record.ClearColony();
            Save();
        }

        /// <summary>Owner only: when his Town Hall was found gone (game seconds), or NotLost once it stands again.</summary>
        public void MarkHallLost(double at)
        {
            if (!IsOwner || _record == null || Math.Abs(_record.HallLostAt - at) < 0.5) return;
            _record.HallLostAt = at;
            Save();
        }

        /// <summary>Another computer runs this citizen now; reload the record if it comes back to us.</summary>
        public void MarkStale() => _recordIsFresh = false;

        /// <summary>Owner only: remember where the Town Hall stands so "gone" can be told apart from "unloaded".</summary>
        public void RememberColonyPos(Vector3 pos)
        {
            if (!IsOwner || _record == null) return;
            float[] p = ToFloats(pos);
            if (_record.ColonyPos != null && _record.ColonyPos.SequenceEqual(p)) return;
            _record.ColonyPos = p;
            Save();
        }

        public static float[] ToFloats(Vector3 v) => new[] { v.x, v.y, v.z };

        public static Vector3 ToVector3(float[] values) =>
            values != null && values.Length == 3 ? new Vector3(values[0], values[1], values[2]) : Vector3.zero;

        private CitizenRecord ReadRecord()
        {
            ZDO zdo = Zdo;
            if (zdo == null) return new CitizenRecord();
            string saved = zdo.GetString(RecordKey, "");
            return string.IsNullOrEmpty(saved) ? FromV1Keys(zdo) : CitizenRecord.Decode(saved);
        }

        private static CitizenRecord FromV1Keys(ZDO zdo) => CitizenRecord.FromV1(
            zdo.GetString(V1Colony, ""), zdo.GetString(V1Name, ""), zdo.GetInt(V1Model, 0),
            zdo.GetString(V1Hair, ""), zdo.GetString(V1Beard, ""),
            ToFloats(zdo.GetVec3(V1Skin, Vector3.one)), ToFloats(zdo.GetVec3(V1HairColor, Vector3.one)),
            zdo.GetString(V1Carry, ""), zdo.GetString(V1Status, ""));

        /// <summary>
        /// Shows the record's looks. The game only saves a look when it changes on the computer that owns the settler,
        /// so the owner also writes them directly: a settler loaded before its owner was known would otherwise keep
        /// the default (male) body for everyone.
        /// </summary>
        public void ApplyAppearance()
        {
            if (Zdo == null || _vis == null) return;
            CitizenRecord r = Record;
            _vis.SetModel(r.Model);
            _vis.SetHairItem(Hash(r.Hair));
            _vis.SetBeardItem(Hash(NorseNames.BeardFor(r.Model, r.Beard)));
            _vis.SetSkinColor(ToVector3(r.Skin));
            _vis.SetHairColor(ToVector3(r.HairColor));
            if (!IsOwner) return;
            Zdo.Set(ZDOVars.s_modelIndex, r.Model);
            Zdo.Set(ZDOVars.s_skinColor, ToVector3(r.Skin));
            Zdo.Set(ZDOVars.s_hairColor, ToVector3(r.HairColor));
        }

        /// <summary>For the log: the body the record asks for, the one saved for everyone, and the mesh on screen.</summary>
        public string DescribeBody()
        {
            if (Zdo == null || _vis == null) return "body unknown";
            SkinnedMeshRenderer mesh = _vis.m_bodyModel;
            return $"body {Record.Model} (saved {Zdo.GetInt(ZDOVars.s_modelIndex)}, player-style {_vis.m_isPlayer}, " +
                   $"mesh {(mesh != null && mesh.sharedMesh != null ? mesh.sharedMesh.name : "none")})";
        }

        private static int Hash(string prefabName) => string.IsNullOrEmpty(prefabName) ? 0 : prefabName.GetStableHashCode();

        private static string PickCustomization(string prefix, System.Random rng)
        {
            List<string> names = ObjectDB.instance
                .GetAllItems(ItemDrop.ItemData.ItemType.Customization, prefix)
                .Select(item => Utils.GetPrefabName(item.gameObject))
                .Where(name => !name.EndsWith("None"))
                .ToList();
            return names.Count == 0 ? "" : names[rng.Next(names.Count)];
        }
    }
}
