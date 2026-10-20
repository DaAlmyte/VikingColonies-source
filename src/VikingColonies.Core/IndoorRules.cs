using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>Why a settler should be indoors now.</summary>
    public enum IndoorReason { None, Night, Attack, Raid }

    /// <summary>A colony hut that could take settlers in: its id, where its chest stands, and whether it can now (finished building, door standing).</summary>
    public struct Refuge
    {
        public Refuge(string id, GroundPoint chest, bool usable) : this(id, chest, usable, chest) { }

        /// <param name="door">Where its door stands; settlers stand on that side of the chest.</param>
        public Refuge(string id, GroundPoint chest, bool usable, GroundPoint door) : this(id, chest, usable, door, new GroundPoint(0f, 0f)) { }

        /// <param name="inward">The way into the hut through its door (a unit direction).</param>
        public Refuge(string id, GroundPoint chest, bool usable, GroundPoint door, GroundPoint inward)
        {
            Id = id ?? "";
            Chest = chest;
            Usable = usable;
            Door = door;
            Inward = inward;
        }

        public string Id { get; }
        public GroundPoint Chest { get; }
        public bool Usable { get; }
        public GroundPoint Door { get; }
        public GroundPoint Inward { get; }
    }

    /// <summary>
    /// Going indoors: at night, when attacked, and during a raid at the colony, settlers go into a colony hut (its chest
    /// stands inside the building) and stay till it's over.
    /// </summary>
    public static class IndoorRules
    {
        /// <summary>How far from the chest a settler stands inside.</summary>
        public const float SpotDistance = 1.2f;
        public const int SpotCount = 6;
        /// <summary>The spots fan out this far either side of the way from the chest to the door.</summary>
        public const double SpotFanDegrees = 75;
        /// <summary>The way in: from the door this far into the hut, where a settler steps through.</summary>
        public const float LaneLength = 2f;
        /// <summary>A floor spot stays this far from the way in, so a settler standing there doesn't block the next one.</summary>
        public const float LaneClearance = 0.8f;
        /// <summary>Where a settler's step through the door lands, this far inside it.</summary>
        public const float StepLanding = DoorRules.StepBeyond;
        /// <summary>A floor spot keeps this far from the door's middle (someone nearer counts as in the doorway)...</summary>
        public const float DoorClearance = 1.5f;
        /// <summary>... and this far from where a step through lands (the next one waits while someone stands there).</summary>
        public const float LandingClearance = 1.3f;
        /// <summary>A hut he couldn't get into is passed over for this long.</summary>
        public const double SkipSeconds = 300;
        /// <summary>How long he tries to get into a hut before he gives up on it.</summary>
        public const float GiveUpSeconds = 45f;
        /// <summary>Within his refuge's walls this long without getting closer to his spot, he stays where he is.</summary>
        public const float SettleAnywhereSeconds = 8f;
        /// <summary>Getting this much closer to his spot counts as getting on.</summary>
        public const float SpotProgress = 0.3f;
        /// <summary>
        /// He stays where he is only this far from the door, or more: past where the next one's step through lands and
        /// where a door he opened closes behind him (2 m).
        /// </summary>
        public const float SettleDoorClearance = 2.5f;

        public static bool RoomToSettle(float fromDoor) => fromDoor >= SettleDoorClearance;

        /// <summary>
        /// Within a hut's walls: on its ground, with no wall between him and its chest, and out of its doorway (its ground
        /// reaches a little past the door, where someone staying would block the way in).
        /// </summary>
        public static bool WithinWalls(bool onItsGround, bool wallToChest, float fromDoor) =>
            onItsGround && !wallToChest && fromDoor >= DoorClearance;

        /// <summary>An attack first (he's in danger now), then a raid at the colony, then night; the last two only with their setting on.</summary>
        public static IndoorReason Reason(bool fleeing, bool raidAtColony, bool shelterInRaids, bool night, bool indoorsAtNight)
        {
            if (fleeing) return IndoorReason.Attack;
            if (raidAtColony && shelterInRaids) return IndoorReason.Raid;
            if (night && indoorsAtNight) return IndoorReason.Night;
            return IndoorReason.None;
        }

        /// <summary>Valheim's own rule for a spot in a raid's area: within its range on the ground, and not high up (dungeons are above 3000 m).</summary>
        public static bool RaidCovers(GroundPoint raid, float range, GroundPoint spot, float spotHeight) =>
            spotHeight <= 3000f && ColonyRules.Distance(raid, spot) < range;

        /// <summary>
        /// The hut he goes into: the one he already chose (<paramref name="current"/>) while it still counts, else his own
        /// if usable, else the nearest usable one. A hut he was told to skip doesn't count, and in an attack neither does one
        /// the monster is nearer to than he is (running there is running at it). -1: none. Keeping his choice stops him
        /// running between two huts as a monster moves about.
        /// </summary>
        public static int ChooseRefuge(IReadOnlyList<Refuge> huts, string ownId, GroundPoint me, GroundPoint? threat, Func<string, bool> skip = null,
            string current = null)
        {
            int best = -1, own = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < huts.Count; i++)
            {
                Refuge hut = huts[i];
                if (!hut.Usable || (skip != null && skip(hut.Id))) continue;
                float distance = ColonyRules.Distance(me, hut.Chest);
                if (threat.HasValue && ColonyRules.Distance(hut.Chest, threat.Value) < distance) continue;
                if (!string.IsNullOrEmpty(current) && hut.Id == current) return i;
                if (!string.IsNullOrEmpty(ownId) && hut.Id == ownId) own = i;
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            return own >= 0 ? own : best;
        }

        /// <summary>
        /// Where on the floor he stands: one of six spots by the chest, on its door side (a chest may stand against the
        /// back wall, and behind it is outside), always the same for him, so settlers spread out.
        /// </summary>
        /// <param name="inward">The way into the hut through the door (a unit direction); a spot in that way is passed over
        /// for the next one, as a settler standing there would block the next one coming in.</param>
        public static GroundPoint Spot(GroundPoint chest, GroundPoint door, GroundPoint inward, string settlerId)
        {
            uint hash = StableHash(settlerId);
            int clear = 0, best = 0;
            float bestScore = float.MinValue;
            for (int slot = 0; slot < SpotCount; slot++)
            {
                float score = Clearness(SpotAt(chest, door, slot), door, inward);
                if (score >= 1f) clear++;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = slot;
                }
            }
            if (clear == 0) return SpotAt(chest, door, best);   // a hut too small for a clear one: the one least in the way
            int pick = (int)(hash % (uint)clear);   // settlers share out the clear spots evenly
            for (int slot = 0; slot < SpotCount; slot++)
            {
                GroundPoint spot = SpotAt(chest, door, slot);
                if (Clearness(spot, door, inward) < 1f) continue;
                if (pick-- == 0) return spot;
            }
            return SpotAt(chest, door, best);
        }

        public static GroundPoint Spot(GroundPoint chest, GroundPoint door, string settlerId) =>
            SpotAt(chest, door, (int)(StableHash(settlerId) % SpotCount));

        private static GroundPoint SpotAt(GroundPoint chest, GroundPoint door, int slot)
        {
            double fan = SpotFanDegrees * Math.PI / 180;
            double angle = Math.Atan2(door.Z - chest.Z, door.X - chest.X) - fan + slot * (2 * fan / (SpotCount - 1));
            return new GroundPoint(chest.X + (float)(Math.Cos(angle) * SpotDistance), chest.Z + (float)(Math.Sin(angle) * SpotDistance));
        }

        /// <summary>
        /// How clear of the way in a spot is: 1 or more when it keeps its distance from the lane, the door and where a step
        /// through lands; less the nearer it comes to one of them.
        /// </summary>
        private static float Clearness(GroundPoint spot, GroundPoint door, GroundPoint inward)
        {
            var landing = new GroundPoint(door.X + inward.X * StepLanding, door.Z + inward.Z * StepLanding);
            return Math.Min(FromLane(spot, door, inward) / LaneClearance,
                Math.Min(ColonyRules.Distance(spot, door) / DoorClearance, ColonyRules.Distance(spot, landing) / LandingClearance));
        }

        /// <summary>How far a point is from the way in: the stretch from the door LaneLength into the hut.</summary>
        private static float FromLane(GroundPoint point, GroundPoint door, GroundPoint inward)
        {
            float dx = point.X - door.X, dz = point.Z - door.Z;
            float along = Math.Max(0f, Math.Min(LaneLength, dx * inward.X + dz * inward.Z));
            float ex = dx - inward.X * along, ez = dz - inward.Z * along;
            return (float)Math.Sqrt(ex * ex + ez * ez);
        }

        private static uint StableHash(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text ?? "")
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }
    }
}
