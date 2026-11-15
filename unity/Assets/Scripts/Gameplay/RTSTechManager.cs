using System;
using System.Collections.Generic;
using UnityEngine;
using CSE377.AI;

namespace CSE377.Gameplay
{
    public enum RTSAge
    {
        DarkAge,
        FeudalAge,
        CastleAge
    }

    /// <summary>
    /// Age of Empires tarzı Teknoloji Araştırma Sistemi (Tech Tree).
    /// RAG Knowledge Base ('technologies.json') ile tam uyumludur.
    /// Araştırılan teknolojileri sahadaki tüm mevcut ve gelecekte üretilecek birimlere kalıcı olarak uygular.
    /// AiSafeSnapshot içine 'unlocked_tech' listesini besler.
    /// </summary>
    public class RTSTechManager : MonoBehaviour
    {
        public static RTSTechManager Instance { get; private set; }

        [Header("Mevcut Çağ")]
        public RTSAge currentAge = RTSAge.DarkAge;

        [Header("Açılmış Teknolojiler")]
        public HashSet<string> unlockedTechs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Olay bildirimleri (UI ve birim güncellemeleri için)
        public event Action<string> OnTechnologyResearched;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            // Başlangıç çağı
            unlockedTechs.Add("Age_1");
            currentAge = RTSAge.DarkAge;
        }

        public bool HasTech(string techId)
        {
            return !string.IsNullOrEmpty(techId) && unlockedTechs.Contains(techId);
        }

        /// <summary>
        /// Teknoloji araştırma isteği. Kaynakları kontrol eder, düşer ve geliştirmeleri uygular.
        /// </summary>
        public bool TryResearchTech(string techId)
        {
            if (string.IsNullOrEmpty(techId)) return false;

            // Canonical key eşleştirmesi (büyük/küçük harf duyarsız)
            string canonicalId = techId;
            foreach (var key in GameActionValidator.TechCosts.Keys)
            {
                if (string.Equals(key, techId, StringComparison.OrdinalIgnoreCase))
                {
                    canonicalId = key;
                    break;
                }
            }
            techId = canonicalId;

            if (HasTech(techId))
            {
                Debug.LogWarning($"[RTSTechManager] '{techId}' zaten araştırılmış!");
                return false;
            }

            var gameMgr = RTSGameManager.Instance;
            if (gameMgr == null) return false;

            if (!GameActionValidator.TechCosts.TryGetValue(techId, out var cost))
            {
                Debug.LogError($"[RTSTechManager] Bilinmeyen teknoloji ID: '{techId}'");
                return false;
            }

            if (gameMgr.wood < cost.wood || gameMgr.food < cost.food || gameMgr.gold < cost.gold)
            {
                string msg = $"Yetersiz kaynak! {techId} için {cost.food} Yiyecek, {cost.gold} Altın gerekli.";
                if (gameMgr.hudController != null)
                {
                    gameMgr.hudController.SetAiStatus(msg, Color.yellow);
                }
                Debug.LogWarning($"[RTSTechManager] {msg}");
                return false;
            }

            // Kaynakları harca
            gameMgr.wood -= cost.wood;
            gameMgr.food -= cost.food;
            gameMgr.gold -= cost.gold;
            gameMgr.UpdateHUD();

            // Teknolojiyi aç
            unlockedTechs.Add(techId);
            ApplyTechnologyEffects(techId);

            string successMsg = $"[TEKNOLOJİ ARAŞTIRILDI] '{techId}' ordunuza kalıcı olarak uygulandı!";
            if (techId.Equals("Feudal_Age", StringComparison.OrdinalIgnoreCase))
            {
                currentAge = RTSAge.FeudalAge;
                unlockedTechs.Add("Age_2");
                successMsg = "ÇAĞ ATLANDI: FEODAL ÇAĞ'A GEÇİLDİ! Kışlada Okçu ve Kılıçlı, köylüler için Savunma Kulesi açıldı!";
                RTSAudioManager.Instance?.PlaySound(RTSSoundType.WarHorn);
                RTSAudioManager.Instance?.PlaySound(RTSSoundType.VictoryFanfare);
            }
            else if (techId.Equals("Castle_Age", StringComparison.OrdinalIgnoreCase))
            {
                currentAge = RTSAge.CastleAge;
                unlockedTechs.Add("Age_3");
                successMsg = "ÇAĞ ATLANDI: KALE ÇAĞI'NA GEÇİLDİ! Süvariler ve dev Kuşatma Mancınıkları açıldı!";
                RTSAudioManager.Instance?.PlaySound(RTSSoundType.WarHorn);
                RTSAudioManager.Instance?.PlaySound(RTSSoundType.VictoryFanfare);
            }
            else
            {
                RTSAudioManager.Instance?.PlaySound(RTSSoundType.TechComplete);
            }

            Debug.Log($"[RTSTechManager] {successMsg}");
            if (gameMgr.hudController != null)
            {
                gameMgr.hudController.SetAiStatus(successMsg, Color.green);
                gameMgr.hudController.UpdateAgeBadge();
            }

            OnTechnologyResearched?.Invoke(techId);
            return true;
        }

        /// <summary>
        /// Araştırılan teknolojinin sağladığı bonusları sahadaki mevcut birimlere uygular.
        /// </summary>
        private void ApplyTechnologyEffects(string techId)
        {
            switch (techId?.ToLowerInvariant())
            {
                case "iron_weapons":
                    // Piyade ve Süvarilere +2 Saldırı
                    foreach (var u in RTSUnit.AllUnits)
                    {
                        if (u != null && u.team == UnitTeam.Player)
                        {
                            string t = u.unitType.ToLower();
                            if (t == "spearman" || t == "swordsman" || t == "cavalry")
                            {
                                u.attack += 2;
                            }
                        }
                    }
                    break;

                case "scale_armor":
                    // Tüm dost birimlere +2 Zırh / +20 Can Dayanıklılığı
                    foreach (var u in RTSUnit.AllUnits)
                    {
                        if (u != null && u.team == UnitTeam.Player)
                        {
                            u.armor += 2;
                            u.maxHp += 20;
                            u.currentHp += 20;
                            u.UpdateHealthBar();
                        }
                    }
                    break;

                case "fletching":
                    // Okçulara +1 Saldırı ve +1.5 Menzil
                    foreach (var u in RTSUnit.AllUnits)
                    {
                        if (u != null && u.team == UnitTeam.Player)
                        {
                            string t = u.unitType.ToLower();
                            if (t == "archer" || t == "crossbowman")
                            {
                                u.attack += 1;
                                u.attackRange += 1.5f;
                            }
                        }
                    }
                    break;

                case "wheelbarrow":
                    // Köylülerin hızı %25 artar, taşıma kapasitesi 10'dan 15'e çıkar
                    foreach (var v in FindObjectsByType<RTSVillager>(FindObjectsSortMode.None))
                    {
                        if (v != null)
                        {
                            v.maxCapacity = 15;
                            var u = v.GetComponent<RTSUnit>();
                            if (u != null) u.moveSpeed *= 1.25f;
                        }
                    }
                    break;

                case "feudal_age":
                    currentAge = RTSAge.FeudalAge;
                    unlockedTechs.Add("Age_2");
                    // Şehir Merkezine +800 Can Takviyesi
                    foreach (var b in RTSBuilding.AllBuildings)
                    {
                        if (b != null && b.team == UnitTeam.Player && b.buildingType == RTSBuildingType.TownCenter)
                        {
                            b.maxHp += 800;
                            b.currentHp += 800;
                        }
                    }
                    // Tüm mevcut dost birliklere +10 Can
                    foreach (var u in RTSUnit.AllUnits)
                    {
                        if (u != null && u.team == UnitTeam.Player)
                        {
                            u.maxHp += 10;
                            u.currentHp += 10;
                            u.UpdateHealthBar();
                        }
                    }
                    break;

                case "castle_age":
                    currentAge = RTSAge.CastleAge;
                    unlockedTechs.Add("Age_3");
                    // Şehir Merkezine +1200 Can Takviyesi
                    foreach (var b in RTSBuilding.AllBuildings)
                    {
                        if (b != null && b.team == UnitTeam.Player && b.buildingType == RTSBuildingType.TownCenter)
                        {
                            b.maxHp += 1200;
                            b.currentHp += 1200;
                        }
                    }
                    // Tüm mevcut dost birliklere +15 Can ve +1 Zırh
                    foreach (var u in RTSUnit.AllUnits)
                    {
                        if (u != null && u.team == UnitTeam.Player)
                        {
                            u.maxHp += 15;
                            u.currentHp += 15;
                            u.armor += 1;
                            u.UpdateHealthBar();
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Yeni üretilen birimlere daha önceden açılmış teknolojilerin bonuslarını yansıtır.
        /// </summary>
        public void ApplyResearchedTechsToNewUnit(RTSUnit unit)
        {
            if (unit == null || unit.team != UnitTeam.Player) return;

            string type = unit.unitType.ToLower();

            if (HasTech("Feudal_Age"))
            {
                unit.maxHp += 10;
                unit.currentHp = unit.maxHp;
            }

            if (HasTech("Castle_Age"))
            {
                unit.maxHp += 15;
                unit.armor += 1;
                unit.currentHp = unit.maxHp;
            }

            if (HasTech("Scale_Armor"))
            {
                unit.armor += 2;
                unit.maxHp += 20;
                unit.currentHp = unit.maxHp;
            }

            if (HasTech("Iron_Weapons") && (type == "spearman" || type == "swordsman" || type == "cavalry"))
            {
                unit.attack += 2;
            }

            if (HasTech("Fletching") && (type == "archer" || type == "crossbowman"))
            {
                unit.attack += 1;
                unit.attackRange += 1.5f;
            }

            if (HasTech("Wheelbarrow") && type == "villager")
            {
                var v = unit.GetComponent<RTSVillager>();
                if (v != null) v.maxCapacity = 15;
                unit.moveSpeed *= 1.25f;
            }
        }
    }
}
