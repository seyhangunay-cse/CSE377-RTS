using System;
using System.Collections.Generic;
using UnityEngine;

namespace CSE377.AI
{
    /// <summary>
    /// LLM tarafından üretilen ve oyuncu tarafından onaylanan eylemleri
    /// Photon ağına göndermeden önce yerel oyun kuralları ve kaynak sınırlarına göre doğrular.
    /// Hileli, bütçeyi aşan veya geçersiz emirlerin ağa sızmasını engeller.
    /// </summary>
    public class GameActionValidator : MonoBehaviour
    {
        public struct ValidationResult
        {
            public bool IsValid;
            public string ErrorMessage;

            public static ValidationResult Success() => new ValidationResult { IsValid = true, ErrorMessage = string.Empty };
            public static ValidationResult Fail(string error) => new ValidationResult { IsValid = false, ErrorMessage = error };
        }

        // Birim maliyet tablosu (units.json ile uyumlu)
        private static readonly Dictionary<string, (int wood, int food, int gold)> UnitCosts = 
            new Dictionary<string, (int wood, int food, int gold)>(StringComparer.OrdinalIgnoreCase)
            {
                { "villager", (0, 50, 0) },
                { "spearman", (0, 60, 20) },
                { "archer", (50, 0, 30) },
                { "cavalry", (0, 100, 80) },
                { "swordsman", (0, 60, 30) },
                { "crossbowman", (60, 0, 40) },
                { "trebuchet", (200, 0, 150) }
            };

        // Bina maliyet tablosu
        public static readonly Dictionary<string, (int wood, int food, int gold)> BuildingCosts =
            new Dictionary<string, (int wood, int food, int gold)>(StringComparer.OrdinalIgnoreCase)
            {
                { "barracks", (150, 0, 0) },
                { "tower", (125, 0, 50) },
                { "blacksmith", (150, 0, 0) },
                { "towncenter", (350, 0, 100) },
                { "town_center", (350, 0, 100) },
                { "village_center", (350, 0, 100) },
                { "house", (30, 0, 0) },
                { "farm", (60, 0, 0) },
                { "stonewall", (20, 0, 10) },
                { "stone_wall", (20, 0, 10) },
                { "wall", (20, 0, 10) },
                { "citygate", (50, 0, 20) },
                { "city_gate", (50, 0, 20) },
                { "gate", (50, 0, 20) },
                { "castle", (350, 0, 250) },
                { "archeryrange", (175, 0, 0) },
                { "archery_range", (175, 0, 0) },
                { "siegeworkshop", (200, 0, 100) },
                { "siege_workshop", (200, 0, 100) },
                { "market", (150, 0, 0) }
            };

        // Teknoloji maliyet tablosu (technologies.json ile uyumlu)
        public static readonly Dictionary<string, (int wood, int food, int gold)> TechCosts =
            new Dictionary<string, (int wood, int food, int gold)>(StringComparer.OrdinalIgnoreCase)
            {
                { "Iron_Weapons", (0, 100, 50) },
                { "Scale_Armor", (0, 100, 80) },
                { "Fletching", (50, 100, 0) },
                { "Wheelbarrow", (50, 175, 0) },
                { "Ballistics", (300, 0, 175) },
                { "Feudal_Age", (200, 500, 0) },
                { "Castle_Age", (0, 800, 200) }
            };

        /// <summary>
        /// Tüm eylem planını kümülatif kaynak tüketimi ve geçerlilik kurallarıyla test eder.
        /// </summary>
        public static ValidationResult ValidatePlan(RTSActionPlan plan, AiSafeSnapshot currentSnapshot)
        {
            if (plan == null || plan.actions == null || plan.actions.Count == 0)
            {
                return ValidationResult.Fail("Plan boş veya geçersiz formatta.");
            }

            // Kümülatif harcama takibi
            int remainingWood = currentSnapshot.resources.wood;
            int remainingFood = currentSnapshot.resources.food;
            int remainingGold = currentSnapshot.resources.gold;

            for (int i = 0; i < plan.actions.Count; i++)
            {
                var action = plan.actions[i];
                var actionResult = ValidateSingleAction(action, currentSnapshot, ref remainingWood, ref remainingFood, ref remainingGold);
                
                if (!actionResult.IsValid)
                {
                    return ValidationResult.Fail($"Eylem #{i + 1} ({action.action_type}) reddedildi: {actionResult.ErrorMessage}");
                }
            }

            return ValidationResult.Success();
        }

        private static ValidationResult ValidateSingleAction(
            RTSAction action, 
            AiSafeSnapshot snapshot, 
            ref int currentWood, 
            ref int currentFood, 
            ref int currentGold)
        {
            if (string.IsNullOrEmpty(action.action_type))
            {
                return ValidationResult.Fail("Aksiyon tipi (action_type) belirtilmemiş.");
            }

            switch (action.action_type.ToUpper())
            {
                case "TRAIN_UNIT":
                    if (action.count < 1 || action.count > 20)
                    {
                        return ValidationResult.Fail($"Geçersiz üretim adedi ({action.count}). Sınır: 1-20.");
                    }

                    if (string.IsNullOrEmpty(action.unit_type) || !UnitCosts.ContainsKey(action.unit_type))
                    {
                        return ValidationResult.Fail($"Bilinmeyen birim tipi: {action.unit_type}");
                    }

                    var cost = UnitCosts[action.unit_type];
                    int totalWoodReq = cost.wood * action.count;
                    int totalFoodReq = cost.food * action.count;
                    int totalGoldReq = cost.gold * action.count;

                    if (currentWood < totalWoodReq || currentFood < totalFoodReq || currentGold < totalGoldReq)
                    {
                        return ValidationResult.Fail(
                            $"Yetersiz kaynak! Gerekli: {totalWoodReq} Odun, {totalFoodReq} Yiyecek, {totalGoldReq} Altın. " +
                            $"Mevcut Kalan: {currentWood} Odun, {currentFood} Yiyecek, {currentGold} Altın."
                        );
                    }

                    // Kaynakları sanal olarak düş
                    currentWood -= totalWoodReq;
                    currentFood -= totalFoodReq;
                    currentGold -= totalGoldReq;
                    return ValidationResult.Success();

                case "MOVE_GROUP":
                    if (string.IsNullOrEmpty(action.target_zone))
                    {
                        return ValidationResult.Fail("Taşıma hedefi (target_zone) belirtilmemiş.");
                    }

                    // Oyuncunun bu birimden ordusunda olup olmadığını denetle
                    bool hasUnit = false;
                    foreach (var u in snapshot.army)
                    {
                        if (string.Equals(u.type, action.unit_type, StringComparison.OrdinalIgnoreCase))
                        {
                            hasUnit = true;
                            break;
                        }
                    }

                    if (!hasUnit)
                    {
                        return ValidationResult.Fail($"Orduda '{action.unit_type}' tipinde birim bulunamadı.");
                    }

                    return ValidationResult.Success();

                case "BUILD":
                    string bKey = !string.IsNullOrEmpty(action.building_type) ? action.building_type : (!string.IsNullOrEmpty(action.unit_type) ? action.unit_type : "");
                    bKey = bKey.ToLower().Trim();
                    if (string.IsNullOrEmpty(bKey)) bKey = "barracks";
                    if (bKey == "village_center" || bKey == "villagecenter" || bKey == "town_center") bKey = "towncenter";
                    if (!BuildingCosts.ContainsKey(bKey)) bKey = "barracks";
                    action.building_type = bKey;

                    var bCost = BuildingCosts[bKey];
                    if (currentWood < bCost.wood || currentFood < bCost.food || currentGold < bCost.gold)
                    {
                        return ValidationResult.Fail(
                            $"Bina inşası için yetersiz kaynak! Gerekli: {bCost.wood} Odun, {bCost.gold} Altın. " +
                            $"Mevcut: {currentWood} Odun, {currentGold} Altın."
                        );
                    }
                    currentWood -= bCost.wood;
                    currentFood -= bCost.food;
                    currentGold -= bCost.gold;
                    return ValidationResult.Success();

                case "RESEARCH":
                    string tKey = !string.IsNullOrEmpty(action.building_type) ? action.building_type : (!string.IsNullOrEmpty(action.unit_type) ? action.unit_type : "");
                    if (string.IsNullOrEmpty(tKey) || !TechCosts.ContainsKey(tKey))
                    {
                        // Check reason text for known tech names
                        foreach (var key in TechCosts.Keys)
                        {
                            if (!string.IsNullOrEmpty(action.reason) && action.reason.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                tKey = key;
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(tKey) || !TechCosts.ContainsKey(tKey))
                    {
                        tKey = "Wheelbarrow"; // Akıllı varsayılan
                    }

                    action.building_type = tKey;
                    var tCost = TechCosts[tKey];
                    if (currentWood < tCost.wood || currentFood < tCost.food || currentGold < tCost.gold)
                    {
                        return ValidationResult.Fail(
                            $"Teknoloji araştırması için yetersiz kaynak! Gerekli: {tCost.food} Yiyecek, {tCost.gold} Altın. " +
                            $"Mevcut: {currentFood} Yiyecek, {currentGold} Altın."
                        );
                    }
                    currentWood -= tCost.wood;
                    currentFood -= tCost.food;
                    currentGold -= tCost.gold;
                    return ValidationResult.Success();

                case "SCOUT":
                case "EXPLORE":
                    // Keşif eylemi kaynak tüketmez; sahadaki herhangi bir normal asker (mızrakçı, okçu, süvari vb.) gidebilir
                    return ValidationResult.Success();

                case "ATTACK":
                case "ATTACK_MOVE":
                    // Taarruz ve hücum eylemleri kaynak tüketmez; sahadaki muharip birlikler doğrudan hedefe yönlendirilir
                    return ValidationResult.Success();

                default:
                    return ValidationResult.Fail($"Bilinmeyen action_type: {action.action_type}");
            }
        }
    }
}
