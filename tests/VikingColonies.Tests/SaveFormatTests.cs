using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The save format is frozen at 1.0: what the mod writes into a world is read back the same way by every later
    /// version. These are the exact texts 0.13 writes, and texts older versions wrote. A failing test here means a saved
    /// world would read differently: add a new field instead of changing one, and never reuse a number.
    /// </summary>
    public class SaveFormatTests
    {
        // ---- A hut's build order (ZDO "vc_order") ----

        private const string Order013 = "v=1|state=1|total=4|done=0,2|fail=3:1|builder=cit1|claim=1234.5|created=1000.25|needs=6 $item_wood|" +
            "blocked=$piece_beech|ground=31.75|kind=2|level=2|take=0|taken=|changed=0|first=1100|paused=1|watched=1|rebuilt=1:2";

        private static BuildOrderRecord SampleOrder()
        {
            var order = new BuildOrderRecord
            {
                State = BuildState.Building, Total = 4, Builder = "cit1", ClaimTime = 1234.5, Created = 1000.25, Needs = "6 $item_wood",
                Blocked = "$piece_beech", Ground = 31.75f, Kind = OrderKind.Repair, TargetLevel = 2, First = 1100, Paused = true, Watched = true,
            };
            order.Rebuilt[1] = 2;
            order.MarkDone(0);
            order.MarkDone(2);
            order.MarkFailed(3);
            order.Changed = false;
            return order;
        }

        [Fact]
        public void A_build_order_is_written_as_0_13_writes_it()
        {
            Assert.Equal(Order013, SampleOrder().Encode());
        }

        [Fact]
        public void Every_pinned_text_reads_back_to_itself()
        {
            // Every field, not only the ones the other tests look at (review before 1.0).
            Assert.Equal(Order013, BuildOrderRecord.Decode(Order013).Encode());
            Assert.Equal(Upgrade013, BuildOrderRecord.Decode(Upgrade013).Encode());
            Assert.Equal(Citizen013, CitizenRecord.Decode(Citizen013).Encode());
            Assert.Equal(CitizenLost013, CitizenRecord.Decode(CitizenLost013).Encode());
            Assert.Equal(Citizen016, CitizenRecord.Decode(Citizen016).Encode());
            Assert.Equal(Roster013, ColonyRoster.Decode(Roster013).Encode());
            Assert.Equal(Keepsakes013, Keepsakes.Decode(Keepsakes013).Encode());
        }

        private const string Upgrade013 = "v=1|state=1|total=5|done=0|fail=|builder=|claim=0|created=10|needs=|blocked=|ground=|kind=1|level=3|" +
            "take=4|taken=1,3|changed=1|first=|paused=0|watched=1|rebuilt=";

        [Fact]
        public void An_upgrade_s_old_pieces_to_take_away_read_back()
        {
            BuildOrderRecord order = BuildOrderRecord.Decode(Upgrade013);
            Assert.Equal(OrderKind.Upgrade, order.Kind);
            Assert.Equal(4, order.TakeAwayTotal);
            Assert.Equal(new[] { 1, 3 }, order.TakenAway.ToArray());
            Assert.True(order.Changed);
            Assert.True(float.IsNaN(order.Ground));
        }

        [Fact]
        public void A_build_order_written_by_0_13_reads_back_the_same()
        {
            BuildOrderRecord order = BuildOrderRecord.Decode(Order013);
            Assert.Equal(BuildState.Building, order.State);
            Assert.Equal(OrderKind.Repair, order.Kind);
            Assert.Equal(new[] { 0, 2 }, order.Done.ToArray());
            Assert.Equal(1, order.Failures[3]);
            Assert.Equal("cit1", order.Builder);
            Assert.Equal(31.75f, order.Ground);
            Assert.Equal(2, order.TargetLevel);
            Assert.True(order.Paused);
            Assert.Equal(1100, order.First);
            Assert.True(order.Watched);
            Assert.Equal(2, order.Rebuilt[1]);
        }

        [Fact]
        public void The_saved_numbers_of_states_and_kinds_never_change()
        {
            Assert.Equal(0, (int)BuildState.Waiting);
            Assert.Equal(1, (int)BuildState.Building);
            Assert.Equal(2, (int)BuildState.Done);
            Assert.Equal(3, (int)BuildState.Clearing);
            Assert.Equal(4, (int)BuildState.Shaping);
            Assert.Equal(0, (int)OrderKind.Build);
            Assert.Equal(1, (int)OrderKind.Upgrade);
            Assert.Equal(2, (int)OrderKind.Repair);
        }

        [Fact]
        public void A_build_order_from_before_upgrades_reads_as_a_new_building()
        {
            // 0.3: no kind, level, take-away, first or paused fields yet.
            BuildOrderRecord order = BuildOrderRecord.Decode("v=1|state=1|total=3|done=0,1|fail=|builder=|claim=0|created=50|needs=|blocked=");
            Assert.Equal(OrderKind.Build, order.Kind);
            Assert.Equal(1, order.TargetLevel);
            Assert.True(float.IsNaN(order.Ground));
            Assert.False(order.Paused);
            Assert.Equal(new[] { 0, 1 }, order.Done.ToArray());
        }

        [Fact]
        public void An_order_of_a_kind_from_a_newer_version_reads_as_a_new_building()
        {
            Assert.Equal(OrderKind.Build, BuildOrderRecord.Decode("v=1|state=1|total=3|kind=9").Kind);
        }

        // ---- A settler (ZDO "vc_citizen") ----

        private const string Citizen013 = "v=2|id=abc|name=Ivar|model=0|hair=Hair3|beard=Beard2|skin=1,0.5,0.25|haircolor=0.1,0.2,0.3|colony=col1|" +
            "colonypos=1,2,3|work=hut1|workpos=4,5,6|job=lumberjack|carry=Wood:12|status=$vc_status_chopping|woodcutting=12,0.5|meal=7|hungry=1|" +
            "home=long1#3|homepos=7,8,9|halllost=|life=2";

        private static CitizenRecord SampleCitizen() => new CitizenRecord
        {
            Id = "abc", Name = "Ivar", Model = 0, Hair = "Hair3", Beard = "Beard2", Skin = new[] { 1f, 0.5f, 0.25f }, HairColor = new[] { 0.1f, 0.2f, 0.3f },
            ColonyId = "col1", ColonyPos = new[] { 1f, 2f, 3f }, Workplace = "hut1", WorkplacePos = new[] { 4f, 5f, 6f }, Job = "lumberjack",
            Carry = "Wood:12", Status = "$vc_status_chopping", Woodcutting = 12f, WoodcuttingXp = 0.5f, LastMealDay = 7, Hungry = true,
            Home = "long1#3", HomePos = new[] { 7f, 8f, 9f }, Life = 2,
        };

        [Fact]
        public void A_settler_is_written_as_0_13_writes_him()
        {
            Assert.Equal(Citizen013, SampleCitizen().Encode());
        }

        [Fact]
        public void A_settler_written_by_0_13_reads_back_the_same()
        {
            CitizenRecord r = CitizenRecord.Decode(Citizen013);
            Assert.Equal("Ivar", r.Name);
            Assert.Equal("lumberjack", r.Job);
            Assert.Equal("long1#3", r.Home);
            Assert.Equal(12f, r.Woodcutting);
            Assert.Equal(7, r.LastMealDay);
            Assert.True(r.Hungry);
            Assert.Equal(2, r.Life);
            Assert.Equal(new[] { 4f, 5f, 6f }, r.WorkplacePos);
        }

        // 0.16 added who he is: his calling, trait, origin, the day he came, and whether he is still walking in. Written only
        // when set, so a 0.13 text reads back to itself.
        private const string Citizen016 = Citizen013 + "|calling=farmer|trait=cheerful|origin=$vc_origin_coast|arrived=12|arriving=1";

        [Fact]
        public void A_settler_written_by_0_16_keeps_who_he_is()
        {
            CitizenRecord r = CitizenRecord.Decode(Citizen016);
            Assert.Equal("farmer", r.Calling);
            Assert.Equal("cheerful", r.Trait);
            Assert.Equal("$vc_origin_coast", r.Origin);
            Assert.Equal(12, r.Arrived);
            Assert.True(r.Arriving);
            Assert.Equal(Citizen016, r.Encode());
        }

        [Fact]
        public void A_settler_from_before_0_16_has_none_of_it_yet()
        {
            CitizenRecord r = CitizenRecord.Decode(Citizen013);
            Assert.Equal("", r.Calling);
            Assert.Equal("", r.Trait);
            Assert.Equal("", r.Origin);
            Assert.Equal(CitizenRecord.ArrivedUnknown, r.Arrived);
            Assert.False(r.Arriving);
        }

        // 0.17 added his mood: the last worked out, its reasons, his last meals, his last evening (and whether in company),
        // the day a monster last hit him and since when he has been jobless. Written only when set.
        private const string Citizen017 = Citizen016 + "|mood=62|whymood=$vc_mood_home:5,$vc_mood_death:-10:Ingrid" +
            "|meals=Raspberry,Mushroom,Raspberry|evening=11|company=1|hitday=10|joblesssince=9";

        [Fact]
        public void A_settler_written_by_0_17_keeps_his_mood()
        {
            CitizenRecord r = CitizenRecord.Decode(Citizen017);
            Assert.Equal(62, r.Mood);
            Assert.Equal("$vc_mood_home:5,$vc_mood_death:-10:Ingrid", r.WhyMood);
            Assert.Equal(new[] { "Raspberry", "Mushroom", "Raspberry" }, r.LastMeals);
            Assert.Equal(11, r.EveningDay);
            Assert.True(r.EveningCompany);
            Assert.Equal(10, r.HitDay);
            Assert.Equal(9, r.JoblessSince);
            Assert.Equal(Citizen017, r.Encode());
            IReadOnlyList<MoodReason> why = MoodLevels.Decode(r.WhyMood);
            Assert.Equal("Ingrid", why[1].Name);
            Assert.Equal(-10, why[1].Change);
        }

        [Fact]
        public void A_settler_from_before_0_17_has_no_mood_yet()
        {
            CitizenRecord r = CitizenRecord.Decode(Citizen016);
            Assert.Equal(Mood.Unknown, r.Mood);
            Assert.Empty(r.LastMeals);
            Assert.Equal(CitizenRecord.NoDay, r.EveningDay);
            Assert.Equal(CitizenRecord.NoDay, r.HitDay);
            Assert.Equal(CitizenRecord.NoDay, r.JoblessSince);
            Assert.Equal(Citizen016, r.Encode());
        }

        private const string CitizenLost013 ="v=2|id=def|name=Astrid|model=1|hair=Hair1|beard=|skin=1,1,1|haircolor=1,1,1|colony=col1|" +
            "colonypos=1,2,3|work=|workpos=|job=|carry=|status=|woodcutting=0,0|meal=0|hungry=|home=|homepos=|halllost=5400.5|life=";

        [Fact]
        public void A_settler_whose_Town_Hall_was_lost_reads_back_when_it_was()
        {
            CitizenRecord r = CitizenRecord.Decode(CitizenLost013);
            Assert.Equal(5400.5, r.HallLostAt);
            Assert.Equal(1, r.Model);
            Assert.Equal(0, r.Life);
        }

        // ---- The Town Hall's roster, saga and keepsakes ----

        private const string Roster013 = "v=1\nid=abc|name=Ivar|job=farmer|work=farm1|status=$vc_status_planting|seen=12|pos=1,2,3|missing=-1|home=long1#2";

        [Fact]
        public void The_roster_is_written_and_read_as_0_13_does()
        {
            var roster = new ColonyRoster();
            roster.Seen("abc", "Ivar", "farmer", "farm1", "$vc_status_planting", new[] { 1f, 2f, 3f }, 12, "long1#2");
            Assert.Equal(Roster013, roster.Encode());
            RosterEntry back = ColonyRoster.Decode(Roster013).Get("abc");
            Assert.Equal("farmer", back.Job);
            Assert.Equal(12, back.LastSeenDay);
            Assert.Equal(-1, back.MissingSinceDay);
        }

        // 0.19 keeps each member's last worked-out mood on the roster (the colony's mood the same on every computer).
        private const string Roster019 = Roster013 + "|mood=62";

        [Fact]
        public void The_roster_keeps_each_members_mood_as_0_19_does()
        {
            var roster = new ColonyRoster();
            roster.Seen("abc", "Ivar", "farmer", "farm1", "$vc_status_planting", new[] { 1f, 2f, 3f }, 12, "long1#2", 62);
            Assert.Equal(Roster019, roster.Encode());
            Assert.Equal(62, ColonyRoster.Decode(Roster019).Get("abc").Mood);
            Assert.Equal(Mood.Unknown, ColonyRoster.Decode(Roster013).Get("abc").Mood);
        }

        [Fact]
        public void The_saga_keeps_a_raid_held_off_as_0_17_does()
        {
            var written = new SagaLog();
            written.Add(new SagaEntry(8, "repelled"));
            const string saved = "8|repelled";
            Assert.Equal(saved, written.Encode());
            SagaLog saga = SagaLog.Decode(saved);
            Assert.Equal("repelled", saga.Page(0, 10)[0].Kind);
            Assert.Equal(8, saga.Page(0, 10)[0].Day);
            Assert.True(MoodSaga.Repelled(saga, 9));
        }

        [Fact]
        public void The_saga_is_written_and_read_as_0_13_does()
        {
            var saga = new SagaLog();
            saga.Add(new SagaEntry(3, "built", "$vc_hut_farm"));
            saga.Add(new SagaEntry(4, "died", "Ivar", "a|pipe"));
            const string saved = "3|built|$vc_hut_farm\n4|died|Ivar|a\\|pipe";
            Assert.Equal(saved, saga.Encode());
            Assert.Equal("a|pipe", SagaLog.Decode(saved).Page(0, 10)[0].Args[1]);
        }

        [Fact]
        public void A_plan_line_is_prefab_position_and_rotation()
        {
            // Values that print the same on every runtime (the game's Mono prints some floats with more digits).
            const string saved = "wood_wall_half;1.5;2;-3.25;0;0;0;1";
            PlannedPiece piece = PlanCodec.Decode(saved)[0];
            Assert.Equal("wood_wall_half", piece.Prefab);
            Assert.Equal(-3.25f, piece.Position.Z);
            Assert.Equal(saved, PlanCodec.Encode(new[] { piece }));
            // As the game writes a quarter turn: read the same.
            PlannedPiece turned = PlanCodec.Decode("wood_wall_half;1.5;2;-3.25;0;0.707106769;0;0.707106769")[0];
            Assert.True(turned.IsValid);
            Assert.Equal(0.70710677f, turned.Rotation.Y, 6);
        }

        [Fact]
        public void A_saved_plan_is_its_text_gzipped()
        {
            // A blob packed by another gzip (Python's): what any 0.13 world holds reads back.
            byte[] blob = System.Convert.FromBase64String("H4sIAAAAAAACCivPz0+JL0/MybE21DO1NrLWNdYzMrU2AENDrnKQbFpOfn4RVAgI9cDSehZmZgZGpiYAs8HmVUAAAAA=");
            Assert.True(PlanBlob.TryUnpack(blob, out string text, out _));
            Assert.Equal("wood_wall;1.5;2;-3.25;0;0;0;1\nwood_floor;0;0;0;0;0.5;0;0.8660254", text);
            Assert.True(PlanBlob.TryUnpack(PlanBlob.Pack(text), out string again, out _));
            Assert.Equal(text, again);
            Assert.False(PlanBlob.TryUnpack(new byte[] { 1, 2, 3 }, out _, out _));
            Assert.False(PlanBlob.TryUnpack(null, out _, out _));
        }

        [Fact]
        public void A_record_s_escapes_never_change()
        {
            // Keepsakes are records inside a record: they depend on these exactly (review before 1.0).
            Assert.Equal("a\\pb\\ec\\\\d\\ne", RecordCodec.Escape("a|b=c\\d\ne"));
            Assert.Equal("a|b=c\\d\ne", RecordCodec.Unescape("a\\pb\\ec\\\\d\\ne"));
        }

        private const string Keepsakes013 =
            @"abc=v\e2\pid\eabc\pname\eIvar\pmodel\e0\phair\eHair3\pbeard\eBeard2\pskin\e1,0.5,0.25\phaircolor\e0.1,0.2,0.3\pcolony\ecol1\pcolonypos\e1,2,3\pwork\ehut1\pworkpos\e4,5,6\pjob\elumberjack\pcarry\e\pstatus\e\pwoodcutting\e12,0\pmeal\e-1\phungry\e\phome\elong1#3\phomepos\e7,8,9\phalllost\e\plife\e2";

        [Fact]
        public void The_keepsakes_are_written_and_read_as_0_13_does()
        {
            var keep = new Keepsakes();
            keep.Set("abc", Keepsake.Of(SampleCitizen()));
            Assert.Equal(Keepsakes013, keep.Encode());
            CitizenRecord back = CitizenRecord.Decode(Keepsakes.Decode(Keepsakes013).Get("abc"));
            Assert.Equal("Ivar", back.Name);
            Assert.Equal("long1#3", back.Home);
            Assert.Equal(2, back.Life);
        }

        [Fact]
        public void Every_saved_name_never_changes()
        {
            var expected = new System.Collections.Generic.Dictionary<string, string>
            {
                ["BuildingId"] = "vc_building_id", ["ColonyId"] = "vc_colony_id", ["HallLostAt"] = "vc_hall_lost_at", ["TookOver"] = "vc_took_over",
                ["ColonyOwner"] = "vc_colony_owner", ["ColonyPos"] = "vc_colony_pos", ["ColonyLevel"] = "vc_colony_level", ["HallRaisedAt"] = "vc_hall_raised_at", ["BossHeard"] = "vc_boss_heard", ["BuildingType"] = "vc_building_type", ["Level"] = "vc_level",
                ["DataVersion"] = "vc_data_version", ["AutoHire"] = "vc_autohire", ["Unticked"] = "vc_unticked", ["Unpicked"] = "vc_unpicked",
                ["Ungrown"] = "vc_ungrown", ["Order"] = "vc_order", ["Plan"] = "vc_plan", ["TakeAway"] = "vc_takeaway", ["PlanPrev"] = "vc_plan_prev",
                ["OrderPrev"] = "vc_order_prev", ["EarthLevel"] = "vc_earth_level", ["Founded"] = "vc_founded", ["Roster"] = "vc_roster", ["NewcomerDay"] = "vc_newcomerday",
                ["Saga"] = "vc_saga", ["Keepsakes"] = "vc_keepsakes", ["Site"] = "vc_site", ["Citizen"] = "vc_citizen", ["Dead"] = "vc_dead",
                ["Pose"] = "vc_pose", ["PoseTarget"] = "vc_pose_at", ["Doing"] = "vc_doing",
                ["CairnName"] = "vc_cairn_name", ["CairnJob"] = "vc_cairn_job", ["CairnCause"] = "vc_cairn_cause", ["CairnCauseArg"] = "vc_cairn_cause_arg",
                ["CairnDay"] = "vc_cairn_day", ["CairnSpawn"] = "vc_cairn_spawn",
                ["CampLevel"] = "vc_camp_level", ["CampWaits"] = "vc_camp_waits", ["ColonyEatOff"] = "vc_colony_eat_off",
                ["TownHallType"] = "townhall", ["LumberjackHutType"] = "lumberjack_hut", ["BuilderHutType"] = "builder_hut", ["LonghouseType"] = "longhouse",
                ["ForagerHutType"] = "forager_hut", ["FarmType"] = "farm", ["TownHallPiece"] = "vc_colony_hall", ["LumberjackHutPiece"] = "vc_hut_lumberjack",
                ["BuilderHutPiece"] = "vc_hut_builder", ["LonghousePiece"] = "vc_hut_longhouse", ["ForagerHutPiece"] = "vc_hut_forager",
                ["FarmPiece"] = "vc_hut_farm", ["LumberjackJob"] = "lumberjack", ["BuilderJob"] = "builder", ["ForagerJob"] = "forager", ["FarmerJob"] = "farmer", ["WatchtowerType"] = "watchtower", ["WatchtowerPiece"] = "vc_hut_watchtower", ["GuardJob"] = "guard", ["GuardRole"] = "vc_guard_role", ["GuardOrders"] = "vc_guard_orders", ["GuardPost"] = "vc_guard_post", ["AlarmUntil"] = "vc_alarm_until", ["AlarmAt"] = "vc_alarm_at",
                ["FieldCrop"] = "vc_field_crop", ["FieldSize"] = "vc_field_size", ["FieldFarm"] = "vc_field_farm", ["FieldsAuto"] = "vc_fields_auto", ["FieldByHand"] = "vc_field_by_hand", ["Keep"] = "vc_keep",
                ["Unfelled"] = "vc_unfelled", ["WoodLimit"] = "vc_wood_limit",
                ["FieldMarkerPiece"] = "vc_field_marker", ["StorehouseType"] = "storehouse", ["StorehousePiece"] = "vc_hut_storehouse", ["CourierJob"] = "courier", ["CourierTasks"] = "vc_courier_tasks",
                ["CouriersLeave"] = "vc_couriers_leave", ["StoreClosing"] = "vc_store_closing", ["StoreFull"] = "vc_store_full",
            };
            var actual = typeof(SaveKeys).GetFields().ToDictionary(f => f.Name, f => (string?)f.GetRawConstantValue() ?? "");
            Assert.Equal(expected.OrderBy(k => k.Key), actual.OrderBy(k => k.Key));
        }

        [Fact]
        public void A_hut_s_unticked_list_is_names_in_order_with_commas()
        {
            Assert.Equal("Dandelion,Thistle", TickList.Save(new[] { "Thistle", "Dandelion", "Thistle" }));
            Assert.Equal(new[] { "Dandelion", "Thistle" }, TickList.Parse("Dandelion, Thistle,").OrderBy(s => s).ToArray());
            // A kind unticked to start (flint, branches) is saved with a plus once ticked.
            Assert.Equal("+Flint,Dandelion", TickList.Save(new[] { "Dandelion", "+Flint" }));
        }

        [Fact]
        public void The_dead_list_is_newest_first_with_commas()
        {
            Assert.Equal(new[] { "b", "a" }, DeathList.Parse(DeathList.Add(DeathList.Add("", "a"), "b")).ToArray());
            Assert.Equal(new[] { "x", "y" }, DeathList.Parse("x,y").ToArray());
        }
    }
}
