using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>
    /// Everything saved about one citizen, kept apart from the walking body (which can unload or respawn).
    /// Stored on the citizen as one versioned string.
    /// </summary>
    public sealed class CitizenRecord
    {
        public const int CurrentVersion = 2;

        public string Id = "";
        public string Name = "";
        public int Model;
        public string Hair = "";
        public string Beard = "";
        public float[] Skin = { 1f, 1f, 1f };
        public float[] HairColor = { 1f, 1f, 1f };
        public string ColonyId = "";
        public float[] ColonyPos;      // where the Town Hall stands; null when unknown or colony-less
        public string Workplace = "";  // building id; "" when jobless
        public float[] WorkplacePos;   // null when jobless
        public string Job = "";        // job id; "" when jobless
        public string Carry = "";      // CarryLoad.Serialize()
        public string Status = "";     // $vc_status_* token
        public float Woodcutting;      // skill level 0-100 (see SettlerSkill)
        public float WoodcuttingXp;    // practice towards the next level
        /// <summary>The farmer's and forager's skill (0.22), and its practice; written only when set.</summary>
        public float Farming, FarmingXp;
        /// <summary>The builder's skill (0.22), and its practice; written only when set.</summary>
        public float Crafting, CraftingXp;
        /// <summary>The guard's skill (1.1), and its practice; written only when set.</summary>
        public float Fighting, FightingXp;
        public float Hauling, HaulingXp;   // a courier's (1.5)
        /// <summary>A guard's tally of monsters killed (1.3); written only when set.</summary>
        public int Kills;
        public int LastMealDay = Meals.NeverAte;   // the day he last ate
        public bool Hungry;            // he found nothing to eat at his last meal
        public string Home = "";       // his bed: "<longhouse id>#<plan index>"; "" when homeless
        public float[] HomePos;        // where that bed stands; null when homeless
        public double HallLostAt = ColonyGrace.NotLost;   // game time his Town Hall was found gone; NotLost while it stands
        /// <summary>How many times the colony brought him back (Bring back): a body with a lower number is an old copy.</summary>
        public int Life;
        // Who he is (settler life, 0.16): written only when set, so records from before read back unchanged.
        public const int ArrivedUnknown = -1;
        /// <summary>The trade he's good at (a job id), <see cref="Callings.None"/>, or "" when not given yet (before 0.16).</summary>
        public string Calling = "";
        /// <summary>His trait's name (<see cref="Traits.Name"/>), or "" when not given yet.</summary>
        public string Trait = "";
        /// <summary>Where he came from: a translation token, or "" when not given yet.</summary>
        public string Origin = "";
        /// <summary>The day he came to the colony, or <see cref="ArrivedUnknown"/>.</summary>
        public int Arrived = ArrivedUnknown;
        /// <summary>He is still walking in from the colony's edge.</summary>
        public bool Arriving;
        // Mood (0.17): written only when set, the same way.
        public const int NoDay = -1;
        /// <summary>His mood 0-100, or <see cref="Core.Mood.Unknown"/> before it is first worked out.</summary>
        public int Mood = global::VikingColonies.Core.Mood.Unknown;
        /// <summary>Its reasons (<see cref="MoodLevels.Encode"/>), so every computer shows the same.</summary>
        public string WhyMood = "";
        /// <summary>His last meals, newest last (at most <see cref="MealMemory.Remembered"/>).</summary>
        public List<string> LastMeals = new List<string>();
        /// <summary>The last day he spent the evening by the colony's burning fire, and whether two others were there.</summary>
        public int EveningDay = NoDay;
        public bool EveningCompany;
        /// <summary>The last day a monster hit him.</summary>
        public int HitDay = NoDay;
        /// <summary>The day he became jobless (fired, or came without a job); <see cref="NoDay"/> while he has one.</summary>
        public int JoblessSince = NoDay;

        public bool HasColony => !string.IsNullOrEmpty(ColonyId);
        public bool IsEmployed => !string.IsNullOrEmpty(Workplace);
        public bool HasHome => !string.IsNullOrEmpty(Home);

        public void ClearHome()
        {
            Home = "";
            HomePos = null;
        }

        public void ClearWorkplace()
        {
            Workplace = "";
            WorkplacePos = null;
            Job = "";
        }

        public void ClearColony()
        {
            ColonyId = "";
            ColonyPos = null;
            ClearWorkplace();
            ClearHome();
        }

        public string Encode()
        {
            var fields = new List<KeyValuePair<string, string>>
        {
            Pair("v", RecordCodec.FormatInt(CurrentVersion)),
            Pair("id", Id),
            Pair("name", Name),
            Pair("model", RecordCodec.FormatInt(Model)),
            Pair("hair", Hair),
            Pair("beard", Beard),
            Pair("skin", RecordCodec.FormatFloats(Skin)),
            Pair("haircolor", RecordCodec.FormatFloats(HairColor)),
            Pair("colony", ColonyId),
            Pair("colonypos", RecordCodec.FormatFloats(ColonyPos)),
            Pair("work", Workplace),
            Pair("workpos", RecordCodec.FormatFloats(WorkplacePos)),
            Pair("job", Job),
            Pair("carry", Carry),
            Pair("status", Status),
            Pair("woodcutting", RecordCodec.FormatFloats(new[] { Woodcutting, WoodcuttingXp })),
            Pair("meal", RecordCodec.FormatInt(LastMealDay)),
            Pair("hungry", Hungry ? "1" : ""),
            Pair("home", Home),
            Pair("homepos", RecordCodec.FormatFloats(HomePos)),
            Pair("halllost", ColonyGrace.IsLost(HallLostAt) ? HallLostAt.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : ""),
            Pair("life", Life > 0 ? RecordCodec.FormatInt(Life) : ""),
        };
            if (Calling.Length > 0) fields.Add(Pair("calling", Calling));
            if (Trait.Length > 0) fields.Add(Pair("trait", Trait));
            if (Origin.Length > 0) fields.Add(Pair("origin", Origin));
            if (Arrived != ArrivedUnknown) fields.Add(Pair("arrived", RecordCodec.FormatInt(Arrived)));
            if (Arriving) fields.Add(Pair("arriving", "1"));
            if (Mood != global::VikingColonies.Core.Mood.Unknown) fields.Add(Pair("mood", RecordCodec.FormatInt(Mood)));
            if (WhyMood.Length > 0) fields.Add(Pair("whymood", WhyMood));
            if (LastMeals.Count > 0) fields.Add(Pair("meals", string.Join(",", LastMeals.Select(m => (m ?? "").Replace(',', ' ')))));
            if (EveningDay != NoDay) fields.Add(Pair("evening", RecordCodec.FormatInt(EveningDay)));
            if (EveningCompany) fields.Add(Pair("company", "1"));
            if (HitDay != NoDay) fields.Add(Pair("hitday", RecordCodec.FormatInt(HitDay)));
            if (JoblessSince != NoDay) fields.Add(Pair("joblesssince", RecordCodec.FormatInt(JoblessSince)));
            if (Farming > 0f || FarmingXp > 0f) fields.Add(Pair("farming", RecordCodec.FormatFloats(new[] { Farming, FarmingXp })));
            if (Crafting > 0f || CraftingXp > 0f) fields.Add(Pair("crafting", RecordCodec.FormatFloats(new[] { Crafting, CraftingXp })));
            if (Fighting > 0f || FightingXp > 0f) fields.Add(Pair("fighting", RecordCodec.FormatFloats(new[] { Fighting, FightingXp })));
            if (Hauling > 0f || HaulingXp > 0f) fields.Add(Pair("hauling", RecordCodec.FormatFloats(new[] { Hauling, HaulingXp })));
            if (Kills > 0) fields.Add(Pair("kills", RecordCodec.FormatInt(Kills)));
            return RecordCodec.Encode(fields);
        }

        /// <summary>Never throws; unknown fields (e.g. from a newer version) are ignored.</summary>
        public static CitizenRecord Decode(string text)
        {
            Dictionary<string, string> f = RecordCodec.Decode(text);
            float[] woodcutting = RecordCodec.ParseFloats(RecordCodec.Get(f, "woodcutting"), 2) ?? new[] { 0f, 0f };
            float[] farming = RecordCodec.ParseFloats(RecordCodec.Get(f, "farming"), 2) ?? new[] { 0f, 0f };
            float[] crafting = RecordCodec.ParseFloats(RecordCodec.Get(f, "crafting"), 2) ?? new[] { 0f, 0f };
            float[] fighting = RecordCodec.ParseFloats(RecordCodec.Get(f, "fighting"), 2) ?? new[] { 0f, 0f };
            float[] hauling = RecordCodec.ParseFloats(RecordCodec.Get(f, "hauling"), 2) ?? new[] { 0f, 0f };
            return new CitizenRecord
            {
                Id = RecordCodec.Get(f, "id"),
                Name = RecordCodec.Get(f, "name"),
                Model = RecordCodec.ParseInt(RecordCodec.Get(f, "model"), 0),
                Hair = RecordCodec.Get(f, "hair"),
                Beard = RecordCodec.Get(f, "beard"),
                Skin = RecordCodec.ParseFloats(RecordCodec.Get(f, "skin"), 3) ?? new[] { 1f, 1f, 1f },
                HairColor = RecordCodec.ParseFloats(RecordCodec.Get(f, "haircolor"), 3) ?? new[] { 1f, 1f, 1f },
                ColonyId = RecordCodec.Get(f, "colony"),
                ColonyPos = RecordCodec.ParseFloats(RecordCodec.Get(f, "colonypos"), 3),
                Workplace = RecordCodec.Get(f, "work"),
                WorkplacePos = RecordCodec.ParseFloats(RecordCodec.Get(f, "workpos"), 3),
                Job = RecordCodec.Get(f, "job"),
                Carry = RecordCodec.Get(f, "carry"),
                Status = RecordCodec.Get(f, "status"),
                Woodcutting = woodcutting[0],
                WoodcuttingXp = woodcutting[1],
                LastMealDay = RecordCodec.ParseInt(RecordCodec.Get(f, "meal"), Meals.NeverAte),
                Hungry = RecordCodec.Get(f, "hungry") == "1",
                Home = RecordCodec.Get(f, "home"),
                HomePos = RecordCodec.ParseFloats(RecordCodec.Get(f, "homepos"), 3),
                HallLostAt = RecordCodec.TryParseFinite(RecordCodec.Get(f, "halllost"), out double lost) ? lost : ColonyGrace.NotLost,
                Life = System.Math.Max(0, RecordCodec.ParseInt(RecordCodec.Get(f, "life"), 0)),
                Calling = RecordCodec.Get(f, "calling"),
                Trait = RecordCodec.Get(f, "trait"),
                Origin = RecordCodec.Get(f, "origin"),
                Arrived = RecordCodec.ParseInt(RecordCodec.Get(f, "arrived"), ArrivedUnknown),
                Arriving = RecordCodec.Get(f, "arriving") == "1",
                Mood = ValidMood(RecordCodec.ParseInt(RecordCodec.Get(f, "mood"), global::VikingColonies.Core.Mood.Unknown)),
                WhyMood = RecordCodec.Get(f, "whymood"),
                LastMeals = RecordCodec.Get(f, "meals").Split(',').Where(m => m.Length > 0).Reverse().Take(MealMemory.Remembered).Reverse().ToList(),
                EveningDay = RecordCodec.ParseInt(RecordCodec.Get(f, "evening"), NoDay),
                EveningCompany = RecordCodec.Get(f, "company") == "1",
                HitDay = RecordCodec.ParseInt(RecordCodec.Get(f, "hitday"), NoDay),
                JoblessSince = RecordCodec.ParseInt(RecordCodec.Get(f, "joblesssince"), NoDay),
                Farming = farming[0],
                FarmingXp = farming[1],
                Crafting = crafting[0],
                CraftingXp = crafting[1],
                Fighting = fighting[0],
                FightingXp = fighting[1],
                Hauling = hauling[0],
                HaulingXp = hauling[1],
                Kills = System.Math.Max(0, RecordCodec.ParseInt(RecordCodec.Get(f, "kills"), 0)),
            };
        }

        /// <summary>His level in a work skill (0.22).</summary>
        public float Level(WorkSkill skill)
        {
            switch (skill)
            {
                case WorkSkill.Woodcutting: return Woodcutting;
                case WorkSkill.Farming: return Farming;
                case WorkSkill.Crafting: return Crafting;
                case WorkSkill.Fighting: return Fighting;
                case WorkSkill.Hauling: return Hauling;
                default: return 0f;
            }
        }

        /// <summary>Practice in a work skill (0.22); true on a level-up.</summary>
        public bool Practise(WorkSkill skill, float amount)
        {
            switch (skill)
            {
                case WorkSkill.Woodcutting: return SettlerSkill.Raise(ref Woodcutting, ref WoodcuttingXp, amount);
                case WorkSkill.Farming: return SettlerSkill.Raise(ref Farming, ref FarmingXp, amount);
                case WorkSkill.Crafting: return SettlerSkill.Raise(ref Crafting, ref CraftingXp, amount);
                case WorkSkill.Fighting: return SettlerSkill.Raise(ref Fighting, ref FightingXp, amount);
                case WorkSkill.Hauling: return SettlerSkill.Raise(ref Hauling, ref HaulingXp, amount);
                default: return false;
            }
        }

        /// <summary>A damaged saved mood outside 0-100 reads as not known (0.18).</summary>
        private static int ValidMood(int mood) => mood >= 0 && mood <= 100 ? mood : global::VikingColonies.Core.Mood.Unknown;

        /// <summary>Converts a v1 settler (flat saved keys). v1 lumberjacks had no hut, so they become jobless.</summary>
        public static CitizenRecord FromV1(string colonyId, string name, int model, string hair, string beard,
            float[] skin, float[] hairColor, string carry, string status) => new CitizenRecord
        {
            Name = name ?? "",
            Model = model,
            Hair = hair ?? "",
            Beard = beard ?? "",
            Skin = skin != null && skin.Length == 3 ? skin : new[] { 1f, 1f, 1f },
            HairColor = hairColor != null && hairColor.Length == 3 ? hairColor : new[] { 1f, 1f, 1f },
            ColonyId = colonyId ?? "",
            Carry = carry ?? "",
            Status = status ?? "",
        };

        private static KeyValuePair<string, string> Pair(string key, string value) => new KeyValuePair<string, string>(key, value);
    }
}
