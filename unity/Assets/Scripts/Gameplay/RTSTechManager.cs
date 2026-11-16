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
        public HashSet<string> remoteTeamUnlockedTechs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Olay bildirimleri (UI ve birim güncellemeleri için)
        public event Action<string> OnTechnologyResearched;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            ResetTechsForNewMatch();
        }

        public void ResetTechsForNewMatch()
        {
            unlockedTechs.Clear();
            remoteTeamUnlockedTechs.Clear();
            unlockedTechs.Add("Age_1");
            remoteTeamUnlockedTechs.Add("Age_1");
            currentAge = RTSAge.DarkAge;
        }

        public bool HasTech(string techId, UnitTeam? teamOverride = null)
        {
            if (string.IsNullOrEmpty(techId)) return false;
            UnitTeam targetTeam = teamOverride ?? RTSGameManager.LocalPlayerTeam;
            if (targetTeam == RTSGameManager.LocalPlayerTeam)
            {
                return unlockedTechs.Contains(techId);
            }
            return remoteTeamUnlockedTechs.Contains(techId);
        }

        /// <summary>
        /// Karşı oyuncunun (ağ üzerinden) araştırdığı teknolojiyi ilgili takıma uygular.
        /// </summary>
        public void ApplyRemoteTech(string techId, UnitTeam remoteTeam)
        {
            if (string.IsNullOrEmpty(techId)) return;
            if (remoteTeam == RTSGameManager.LocalPlayerTeam) return;
            if (!remoteTeamUnlockedTechs.Add(techId)) return;

            ApplyTechnologyEffectsForTeam(techId, remoteTeam, isLocalTeam: false);
            Debug.Log($"[RTSTechManager] Karşı takım ({remoteTeam}) '{techId}' teknolojisini araştırdı ve sahaya uygulandı.");
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

            // Teknolojiyi aç (Yerel oyuncunun takımına uygula ve ağ üzerinden karşı oyuncuya senkronize et)
            UnitTeam myTeam = RTSGameManager.LocalPlayerTeam;
            unlockedTechs.Add(techId);
            ApplyTechnologyEffectsForTeam(techId, myTeam, isLocalTeam: true);
            RTSNetworkSyncManager.Instance?.BroadcastTechResearch(techId, myTeam);

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
        /// Araştırılan teknolojinin sağladığı bonusları belirtilen takımın sahadaki mevcut birimlerine ve binalarına uygular.
        /// </summary>
        private void ApplyTechnologyEffectsForTeam(string techId, UnitTeam targetTeam, bool isLocalTeam)
        {
            switch (techId?.ToLowerInvariant())
            {
                case "iron_weapons":
                    foreach (var u in RTSUnit.AllUnits)
                    {
                        if (u != null && u.team == targetTeam)
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
                    foreach (var u in RTSUnit.AllUnits)
                    {
                        if (u != null && u.team == targetTeam)
                        {
                            u.armor += 2;
                            u.maxHp += 20;
                            u.currentHp += 20;
                            u.UpdateHealthBar();
                        }
                    }
                    break;

                case "fletching":
                    foreach (var u in RTSUnit.AllUnits)
                    {
                        if (u != null && u.team == targetTeam)
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
                    foreach (var v in FindObjectsByType<RTSVillager>(FindObjectsSortMode.None))
                    {
                        if (v != null)
                        {
                            var u = v.GetComponent<RTSUnit>();
                            if (u != null && u.team == targetTeam)
                            {
                                v.maxCapacity = 15;
                                u.moveSpeed *= 1.25f;
                            }
                        }
                    }
                    break;

                case "feudal_age":
                    if (isLocalTeam)
                    {
                        currentAge = RTSAge.FeudalAge;
                        unlockedTechs.Add("Age_2");
                    }
                    else
                    {
                        remoteTeamUnlockedTechs.Add("Age_2");
                    }
                    foreach (var b in RTSBuilding.AllBuildings)
                    {
                        if (b != null && b.team == targetTeam && b.buildingType == RTSBuildingType.TownCenter)
                        {
                            b.maxHp += 800;
                            b.currentHp += 800;
                        }
                    }
                    foreach (var u in RTSUnit.AllUnits)
                    {
                        if (u != null && u.team == targetTeam)
                        {
                            u.maxHp += 10;
                            u.currentHp += 10;
                            u.UpdateHealthBar();
                        }
                    }
                    break;

                case "castle_age":
                    if (isLocalTeam)
                    {
                        currentAge = RTSAge.CastleAge;
                        unlockedTechs.Add("Age_3");
                    }
                    else
                    {
                        remoteTeamUnlockedTechs.Add("Age_3");
                    }
                    foreach (var b in RTSBuilding.AllBuildings)
                    {
                        if (b != null && b.team == targetTeam && b.buildingType == RTSBuildingType.TownCenter)
                        {
                            b.maxHp += 1200;
                            b.currentHp += 1200;
                        }
                    }
                    foreach (var u in RTSUnit.AllUnits)
                    {
                        if (u != null && u.team == targetTeam)
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
        /// Yeni üretilen birimlere kendi takımının daha önceden açılmış teknolojilerinin bonuslarını yansıtır.
        /// </summary>
        public void ApplyResearchedTechsToNewUnit(RTSUnit unit)
        {
            if (unit == null) return;
            UnitTeam uTeam = unit.team;
            string type = unit.unitType.ToLower();

            if (HasTech("Feudal_Age", uTeam))
            {
                unit.maxHp += 10;
                unit.currentHp = unit.maxHp;
            }

            if (HasTech("Castle_Age", uTeam))
            {
                unit.maxHp += 15;
                unit.armor += 1;
                unit.currentHp = unit.maxHp;
            }

            if (HasTech("Scale_Armor", uTeam))
            {
                unit.armor += 2;
                unit.maxHp += 20;
                unit.currentHp = unit.maxHp;
            }

            if (HasTech("Iron_Weapons", uTeam) && (type == "spearman" || type == "swordsman" || type == "cavalry"))
            {
                unit.attack += 2;
            }

            if (HasTech("Fletching", uTeam) && (type == "archer" || type == "crossbowman"))
            {
                unit.attack += 1;
                unit.attackRange += 1.5f;
            }

            if (HasTech("Wheelbarrow", uTeam) && type == "villager")
            {
                var v = unit.GetComponent<RTSVillager>();
                if (v != null) v.maxCapacity = 15;
                unit.moveSpeed *= 1.25f;
            }
        }
    }
}
