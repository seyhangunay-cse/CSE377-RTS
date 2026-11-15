using System.Collections.Generic;
using UnityEngine;

namespace CSE377.Gameplay
{
    /// <summary>
    /// Harita üzerindeki kritik taktiksel bölgeleri (north_gate, main_base vb.) tanımlar.
    /// Birimlerin intikal hedeflerini belirlemek için kullanılır.
    /// </summary>
    public class RTSZoneMarker : MonoBehaviour
    {
        public static readonly Dictionary<string, RTSZoneMarker> AllZones = new Dictionary<string, RTSZoneMarker>();

        [Header("Bölge Tanımlayıcıları")]
        [Tooltip("MCP ve AI tarafından kullanılan bölge kimliği (örn: north_gate, main_base)")]
        public string zoneId = "north_gate";
        public string displayName = "Kuzey Kapısı";
        public float radius = 5f;
        public Color gizmoColor = Color.yellow;

        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(zoneId))
            {
                AllZones[zoneId.ToLower()] = this;
            }
        }

        private void OnDisable()
        {
            if (!string.IsNullOrEmpty(zoneId) && AllZones.ContainsKey(zoneId.ToLower()))
            {
                AllZones.Remove(zoneId.ToLower());
            }
        }

        public Vector3 GetRandomPositionInZone()
        {
            Vector2 randCircle = Random.insideUnitCircle * (radius * 0.7f);
            return transform.position + new Vector3(randCircle.x, 0, randCircle.y);
        }

        public static Vector3 GetZonePosition(string targetZoneId, Vector3 fallback)
        {
            if (!string.IsNullOrEmpty(targetZoneId))
            {
                string key = targetZoneId.ToLower().Trim();
                if (key == "town_center" || key == "towncenter" || key == "village_center" || key == "base") key = "main_base";
                if (key == "north" || key == "gate" || key == "northgate") key = "north_gate";

                if (AllZones.TryGetValue(key, out var marker))
                {
                    return marker.GetRandomPositionInZone();
                }
            }
            return fallback;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(transform.position, radius);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, $"Zone: {displayName} ({zoneId})");
#endif
        }
    }
}
