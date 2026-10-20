using UnityEngine;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Buildings
{
    /// <summary>
    /// Every hut's place in a colony, on the hut's owner: it joins the colony whose area holds it (the nearest Town Hall's
    /// circle, else the nearest Watchtower's, 1.1), keeps its colony for the grace days when the Town Hall is lost, and leaves
    /// once they're over. (Huts with a job, the Longhouse and the Watchtower alike.)
    /// </summary>
    public class HutMembership : MonoBehaviour
    {
        private const float TickSeconds = 5f;

        private readonly GoneTracker _colonyGone = new GoneTracker(ColonyRules.GoneChecksFor(TickSeconds));
        private ColonyBuilding _building;
        private ZNetView _nview;
        private float _timer = 1f;

        private void Awake()
        {
            _building = GetComponent<ColonyBuilding>();
            _nview = GetComponent<ZNetView>();
        }

        private void Update()
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = TickSeconds;
            CheckMembership();
        }

        /// <summary>Joins the colony whose area holds it; leaves only when the Town Hall is confirmed gone.</summary>
        private void CheckMembership()
        {
            string colony = _building.ColonyId;
            if (!string.IsNullOrEmpty(colony))
            {
                ColonyBuilding hall = ColonyBuilding.FindTownHall(colony);
                bool found = hall != null;
                if (found)
                {
                    _building.SetHallLostAt(ColonyGrace.NotLost);   // it stands (again, or a new one took over)
                    if (Vector3.Distance(_building.ColonyPos, hall.transform.position) > 0.5f) _building.SetColony(colony, hall.transform.position);
                    Piece hallPiece = hall.GetComponent<Piece>();
                    if (hallPiece != null) _building.SetColonyOwner(hallPiece.GetCreator());
                    _building.SetColonyLevel(hall.Level);   // 1.4: the colony's area while the Town Hall isn't loaded here
                }
                bool areaLoaded = _building.HasColonyPos && ZNetScene.instance.IsAreaReady(_building.ColonyPos);
                if (!_colonyGone.Observe(ColonyRules.PresenceOf(found, areaLoaded))) return;
                // Gone: the colony lives on for the grace days, so a new Town Hall built among the huts can take it over.
                double now = ZNet.instance.GetTimeSeconds();
                if (!ColonyGrace.IsLost(_building.HallLostAt))
                {
                    _building.SetHallLostAt(now);
                    Log.Info($"{_building.TypeId} {_building.Id}: its Town Hall is gone; the colony lives on for {ColonyConfig.TownHallGraceDays.Value} days.");
                }
                if (!ColonyGrace.Expired(_building.HallLostAt, now, ColonyConfig.TownHallGraceDays.Value, ColonyBuilding.GraceDaySeconds)) return;
                Log.Info($"{_building.TypeId} {_building.Id}: its Town Hall is gone{(ColonyConfig.TownHallGraceDays.Value > 0 ? " and no new one came" : "")}.");
                _building.SetColony("", Vector3.zero);
                _building.SetHallLostAt(ColonyGrace.NotLost);
                _colonyGone.Reset();
            }
            string chosen = ColonyAreas.ColonyAt(transform.position);
            Vector3? hallAt = ColonyAreas.HallPosition(chosen);
            if (chosen == null || hallAt == null) return;
            _building.SetColony(chosen, hallAt.Value);
            _building.SetColonyLevel(HallRules.LevelOf(chosen));
            Log.Info($"{_building.TypeId} {_building.Id} joined colony {chosen}");
        }
    }
}
