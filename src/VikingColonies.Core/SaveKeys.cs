namespace VikingColonies.Core
{
    /// <summary>
    /// Every name the mod saves into a world (frozen at 1.0, see SaveFormatTests): the keys on a building's, settler's or
    /// cairn's network object, the player's custom data key, building kinds and their pieces, and job names. Renaming one
    /// makes every saved world lose what it held under the old name: add new ones, never change these.
    /// </summary>
    public static class SaveKeys
    {
        // A colony building (its chest's network object).
        public const string BuildingId = "vc_building_id";
        public const string ColonyId = "vc_colony_id";   // the same key v1's Colony Hall used
        public const string HallLostAt = "vc_hall_lost_at";
        public const string TookOver = "vc_took_over";
        public const string ColonyOwner = "vc_colony_owner";
        public const string ColonyPos = "vc_colony_pos";
        public const string ColonyLevel = "vc_colony_level";
        public const string HallRaisedAt = "vc_hall_raised_at";
        public const string BossHeard = "vc_boss_heard";   // the latest boss's fall the colony heard of (1.4.2's small talk)
        public const string BuildingType = "vc_building_type";
        public const string Level = "vc_level";
        public const string DataVersion = "vc_data_version";
        public const string AutoHire = "vc_autohire";
        public const string Unticked = "vc_unticked";
        public const string Unpicked = "vc_unpicked";
        public const string Ungrown = "vc_ungrown";
        public const string Order = "vc_order";
        public const string Plan = "vc_plan";
        public const string TakeAway = "vc_takeaway";
        public const string PlanPrev = "vc_plan_prev";
        public const string OrderPrev = "vc_order_prev";

        // The Town Hall.
        public const string Founded = "vc_founded";
        public const string Roster = "vc_roster";
        public const string NewcomerDay = "vc_newcomerday";
        public const string Saga = "vc_saga";
        public const string Keepsakes = "vc_keepsakes";

        // A piece the builder put up: the building it belongs to.
        public const string Site = "vc_site";

        // A field marker (0.20): its crop (the planted piece; "" = the Farm's list), size (plots a side) and Farm (its building
        // id; "" = none); its colony under ColonyId. On a Farm: assign new fields by themselves.
        public const string FieldCrop = "vc_field_crop";
        public const string FieldSize = "vc_field_size";
        public const string FieldFarm = "vc_field_farm";
        public const string FieldsAuto = "vc_fields_auto";
        /// <summary>On a field marker: the player assigned it or took it from its Farm, so auto-assign leaves it (written only when set).</summary>
        public const string FieldByHand = "vc_field_by_hand";
        /// <summary>On a colony building: its Keep in stock list (<see cref="StockList.Encode"/>), written only when set.</summary>
        public const string Keep = "vc_keep";
        /// <summary>On a Lumberjack's Hut: the tree kinds unticked from Fell on its Trees list (0.22), written only when set.</summary>
        public const string Unfelled = "vc_unfelled";
        /// <summary>On a Lumberjack's Hut: its "Stop at" amount of wood, 0 for no limit (0.22), written only when set.</summary>
        public const string WoodLimit = "vc_wood_limit";

        // A settler, and the dead remembered in each player's custom data.
        public const string Citizen = "vc_citizen";
        public const string Dead = "vc_dead";
        // A settler's pose (0.14): none, seat, ground, bed; the seat or bed he's on; what he's doing in free time.
        public const string Pose = "vc_pose";
        public const string PoseTarget = "vc_pose_at";
        public const string Doing = "vc_doing";

        // A settler's cairn.
        public const string CairnName = "vc_cairn_name";
        public const string CairnJob = "vc_cairn_job";
        public const string CairnCause = "vc_cairn_cause";
        public const string CairnCauseArg = "vc_cairn_cause_arg";
        public const string CairnDay = "vc_cairn_day";
        public const string CairnSpawn = "vc_cairn_spawn";

        // Building kinds (saved under BuildingType) and their pieces (saved by the game as prefab names).
        public const string TownHallType = "townhall";
        public const string LumberjackHutType = "lumberjack_hut";
        public const string BuilderHutType = "builder_hut";
        public const string LonghouseType = "longhouse";
        public const string ForagerHutType = "forager_hut";
        public const string FarmType = "farm";
        public const string WatchtowerType = "watchtower";
        public const string TownHallPiece = "vc_colony_hall";   // v1's Colony Hall name, so v1 worlds load it
        public const string LumberjackHutPiece = "vc_hut_lumberjack";
        public const string BuilderHutPiece = "vc_hut_builder";
        public const string LonghousePiece = "vc_hut_longhouse";
        public const string ForagerHutPiece = "vc_hut_forager";
        public const string FarmPiece = "vc_hut_farm";
        public const string WatchtowerPiece = "vc_hut_watchtower";
        public const string FieldMarkerPiece = "vc_field_marker";
        /// <summary>The Town Hall's camp (huts round 2): its level (0 until built), and why its next level waits.</summary>
        public const string CampLevel = "vc_camp_level";
        public const string CampWaits = "vc_camp_waits";

        // Jobs (saved in a settler's record).
        public const string LumberjackJob = "lumberjack";
        public const string BuilderJob = "builder";
        public const string ForagerJob = "forager";
        public const string FarmerJob = "farmer";
        public const string GuardJob = "guard";
        /// <summary>A Watchtower's role for its guards (1.2): 0 warrior, 1 archer.</summary>
        public const string GuardRole = "vc_guard_role";
        /// <summary>A Watchtower's orders (1.3): 0 patrol, 1 hold post.</summary>
        public const string GuardOrders = "vc_guard_orders";
        /// <summary>Where a Watchtower's guards hold their post (1.3); zero: by the tower.</summary>
        public const string GuardPost = "vc_guard_post";
        /// <summary>The Town Hall's alarm (1.3): sounding until this game time; where it was sounded.</summary>
        public const string AlarmUntil = "vc_alarm_until";
        public const string AlarmAt = "vc_alarm_at";
    }
}
