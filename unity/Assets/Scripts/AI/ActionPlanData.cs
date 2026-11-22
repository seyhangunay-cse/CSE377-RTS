using System;
using System.Collections.Generic;
using UnityEngine;

namespace CSE377.AI
{
    /// <summary>
    /// LLM Agent tarafından üretilen ve action_plan.schema.json şemasına uyan eylem planı.
    /// </summary>
    [Serializable]
    public class RTSActionPlan
    {
        public string summary;
        public float confidence;
        public List<RTSAction> actions = new List<RTSAction>();

        public static RTSActionPlan FromJson(string jsonString)
        {
            try
            {
                return JsonUtility.FromJson<RTSActionPlan>(jsonString);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RTSActionPlan] JSON parse hatası: {ex.Message}\nJSON: {jsonString}");
                return null;
            }
        }

        public string ToJson(bool prettyPrint = false)
        {
            return JsonUtility.ToJson(this, prettyPrint);
        }
    }

    /// <summary>
    /// Plandaki münferit eylem emri (TRAIN_UNIT, BUILD, MOVE_GROUP, RESEARCH).
    /// </summary>
    [Serializable]
    public class RTSAction
    {
        public string action_type;    // "TRAIN_UNIT", "BUILD", "MOVE_GROUP", "RESEARCH"
        public string unit_type;      // "spearman", "archer", "cavalry" vb.
        public string building_type;  // "barracks", "archery_range" vb.
        public int count;             // 1-20
        public string target_zone;    // "north_gate", "south_bridge" vb.
        public string reason;         // Eylemin stratejik gerekçesi

        public override string ToString()
        {
            switch (action_type)
            {
                case "TRAIN_UNIT":
                    return $"[ÜRET] {count}x {unit_type} -> Hedef: {target_zone} ({reason})";
                case "MOVE_GROUP":
                    return $"[KAYDIR] {count}x {unit_type} -> Hedef: {target_zone} ({reason})";
                case "SCOUT":
                case "EXPLORE":
                    string targetLabel = (target_zone == "gold") ? "Altın Madenleri" : "Sisli Bölgeler";
                    return $"[KEŞİF] {count}x Asker -> Hedef: {targetLabel} ({reason})";
                case "ASSIGN_GATHER":
                    string resTr = (!string.IsNullOrEmpty(target_zone) && target_zone.ToLower().Contains("wood")) ? "Odun (Orman)"
                        : (!string.IsNullOrEmpty(target_zone) && target_zone.ToLower().Contains("gold")) ? "Altın Madeni"
                        : "Gıda (Tarla/Çilek)";
                    return $"[İŞÇİ ODAKLA] {count}x Köylü -> {resTr} ({reason})";
                case "BUILD":
                    string bLabel = !string.IsNullOrEmpty(building_type) ? building_type : (!string.IsNullOrEmpty(unit_type) ? unit_type : "Bina");
                    return $"[İNŞA ET] {bLabel} -> Bölge: {target_zone} ({reason})";
                case "RESEARCH":
                    string tLabel = !string.IsNullOrEmpty(building_type) ? building_type : (!string.IsNullOrEmpty(unit_type) ? unit_type : "Teknoloji");
                    return $"[ARAŞTIR] {tLabel} ({reason})";
                default:
                    return $"[{action_type}] {reason}";
            }
        }
    }
}
