using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Colony;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.Visuals
{
    /// <summary>
    /// A field's square on the ground (worker polish, 0.20), drawn with the same little marks as the colony rings (the
    /// workbench's area marker): while placing a field marker (a new field's size), and while looking at one with the
    /// hammer or the Cultivator in hand (its size), for two seconds after looking away.
    /// </summary>
    internal sealed class FieldOutline : MonoBehaviour
    {
        private const float MarkSpacing = 0.6f;
        private const float LingerSeconds = 2f;

        private static readonly FieldInfo PlacementGhost = AccessTools.Field(typeof(Player), "m_placementGhost");

        private readonly List<GameObject> _marks = new List<GameObject>();
        private GameObject _markPrefab;
        private bool _warned;
        private FieldMarker _looked;
        private float _linger;
        private int _used;

        private void Update()
        {
            _used = 0;
            Player player = Player.m_localPlayer;
            if (player != null && !ShowPlacing(player)) ShowLooked(player);
            for (int i = _used; i < _marks.Count; i++)
                if (_marks[i] != null && _marks[i].activeSelf) _marks[i].SetActive(false);
        }

        private bool ShowPlacing(Player player)
        {
            if (!player.InPlaceMode()) return false;
            Piece selected = player.GetSelectedPiece();
            if (selected == null || Utils.GetPrefabName(selected.gameObject) != SaveKeys.FieldMarkerPiece) return false;
            GameObject ghost = PlacementGhost?.GetValue(player) as GameObject;
            if (ghost == null || !ghost.activeInHierarchy) return false;
            Square(ghost.transform.position, ghost.transform.rotation, FieldSizes.New);
            return true;
        }

        private void ShowLooked(Player player)
        {
            GameObject hover = player.GetHoverObject();
            FieldMarker marker = hover != null ? hover.GetComponentInParent<FieldMarker>() : null;
            bool tool = false;
            foreach (ItemDrop.ItemData item in player.GetInventory().GetEquippedItems())
                if (item != null && (item.m_shared.m_name == "$item_hammer" || item.m_shared.m_name == "$item_cultivator")) tool = true;
            if (marker != null && tool)
            {
                _looked = marker;
                _linger = LingerSeconds;
            }
            else _linger -= Time.deltaTime;
            if (_looked != null && _linger > 0f) Square(_looked.transform.position, _looked.transform.rotation, _looked.Size);
        }

        private void Square(Vector3 centre, Quaternion rotation, int size)
        {
            Vec3[] corners = FieldGrid.Corners(CoreVectors.V(centre), CoreVectors.Q(rotation), size, FieldPlots.Spacing);
            for (int c = 0; c < corners.Length; c++)
            {
                Vector3 a = CoreVectors.U(corners[c]), b = CoreVectors.U(corners[(c + 1) % corners.Length]);
                Vector3 along = b - a;
                along.y = 0f;
                int count = Mathf.Max(1, Mathf.RoundToInt(along.magnitude / MarkSpacing));
                Quaternion facing = along.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(along) : Quaternion.identity;
                for (int i = 0; i < count; i++)
                {
                    Vector3 at = Vector3.Lerp(a, b, (i + 0.5f) / count);
                    at.y = ZoneSystem.instance.GetGroundHeight(at) + 0.05f;
                    GameObject mark = Mark();
                    if (mark == null) return;
                    mark.transform.SetPositionAndRotation(at, facing);
                }
            }
        }

        private GameObject Mark()
        {
            if (_markPrefab == null)
            {
                GameObject bench = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("piece_workbench") : null;
                CraftingStation station = bench != null ? bench.GetComponent<CraftingStation>() : null;
                CircleProjector projector = station != null && station.m_areaMarker != null ? station.m_areaMarker.GetComponent<CircleProjector>() : null;
                _markPrefab = projector != null ? projector.m_prefab : null;
                if (_markPrefab == null)
                {
                    if (!_warned) Log.Warn("Workbench area marker not found; field outlines are disabled.");
                    _warned = true;
                    return null;
                }
            }
            if (_used >= _marks.Count || _marks[_used] == null)
            {
                GameObject made = Instantiate(_markPrefab, transform);
                made.name = "vc_field_mark";
                if (_used < _marks.Count) _marks[_used] = made;
                else _marks.Add(made);
            }
            GameObject mark = _marks[_used++];
            if (!mark.activeSelf) mark.SetActive(true);
            return mark;
        }
    }
}
