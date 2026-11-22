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
            return GetZonePosition(targetZoneId, fallback, RTSGameManager.LocalPlayerTeam);
        }

        public static Vector3 GetZonePosition(string targetZoneId, Vector3 fallback, UnitTeam requesterTeam)
        {
            if (!string.IsNullOrEmpty(targetZoneId))
            {
                string key = targetZoneId.ToLower().Trim();
                bool isHomeBase = (key == "town_center" || key == "towncenter" || key == "village_center" || key == "base" || key == "main_base");
                bool isEnemyBase = (key == "enemy_base" || key == "enemy_camp" || key == "enemy" || key == "enemybase");

                if (isHomeBase)
                {
                    key = (requesterTeam == UnitTeam.Enemy) ? "enemy_base" : "main_base";
                }
                else if (isEnemyBase)
                {
                    key = (requesterTeam == UnitTeam.Enemy) ? "main_base" : "enemy_base";
                }
                else if (key == "north" || key == "gate" || key == "northgate" || key == "bridge" || key == "kopru" || key == "center" || key == "north_gate")
                {
                    key = "north_gate";
                }
                else if (key == "west_flank" || key == "west" || key == "bati")
                {
                    return new Vector3(-28f, 0f, 0f) + new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
                }
                else if (key == "east_flank" || key == "east" || key == "dogu")
                {
                    return new Vector3(28f, 0f, 0f) + new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
                }

                if (AllZones.TryGetValue(key, out var marker) && marker != null)
                {
                    return marker.GetRandomPositionInZone();
                }
                if (key == "north_gate")
                {
                    return new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
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
