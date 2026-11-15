using System;
using System.Collections.Generic;
using UnityEngine;
using CSE377.AI;

namespace CSE377.Gameplay
{
    /// <summary>
    /// Ağ Eşitlemesi (Network Sync) ve Karşıt AI Komutanı (Autonomous Rival AI).
    /// 1. Oyuncunun onayladığı eylemleri yerel veya çok oyunculu ağa (Photon RPC) duyurur.
    /// 2. Karşıt taraf (Red Takımı) için otonom bir AI komutanı çalıştırarak
    ///    Kuzey Kapısından periyodik süvari ve asker akınları başlatır.
    /// </summary>
    public class RTSNetworkSyncManager : MonoBehaviour
    {
        public static RTSNetworkSyncManager Instance { get; private set; }

        [Header("Ağ ve Eşitleme Ayarları")]
        public string matchRoomId = "CSE377-Match-Alpha";
        public bool isHost = true;
        public int pingMs = 15;

        [Header("Otonom Düşman AI Komutanı (PVE / Rival AI)")]
        public bool enableRivalAi = true;
        public float rivalActionInterval = 25f; // Her 25 saniyede bir düşman komutanı hamle yapar
        private float nextRivalActionTime = 0f;

        // Olaylar
        public event Action<RTSAction> OnActionDispatched;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void Start()
        {
            nextRivalActionTime = Time.time + 15f; // İlk akın 15 saniye sonra
        }

        private void Update()
        {
            if (enableRivalAi)
            {
                HandleRivalAiCommander();
            }
        }

        /// <summary>
        /// Doğrulanmış eylem emrini ağ katmanına duyurur (Photon RPC Hazırlığı).
        /// </summary>
        public void DispatchActionRpc(RTSAction action)
        {
            if (action == null) return;

            Debug.Log($"[AĞ SENKRONİZASYONU] Eylem RPC Paketi İletildi: {action.action_type} - {action.unit_type ?? action.building_type}");
            OnActionDispatched?.Invoke(action);
        }

        /// <summary>
        /// Kırmızı takımın otonom stratejik komutanı:
        /// Kuzey Kapısında yeni birlikler üretir ve Mavi Üsse taarruz dalgaları düzenler.
        /// </summary>
        private void HandleRivalAiCommander()
        {
            if (Time.time < nextRivalActionTime) return;
            nextRivalActionTime = Time.time + rivalActionInterval;

            if (RTSGameManager.Instance != null && RTSGameManager.Instance.isGameOver) return;

            // Düşman Karargahı veya Kışlası ayakta mı kontrol et
            RTSBuilding enemyBase = null;
            foreach (var b in RTSBuilding.AllBuildings)
            {
                if (b != null && b.team == UnitTeam.Enemy && b.currentHp > 0)
                {
                    if (b.buildingType == RTSBuildingType.Barracks || b.buildingType == RTSBuildingType.TownCenter)
                    {
                        enemyBase = b;
                        break;
                    }
                }
            }

            if (enemyBase == null)
            {
                // Düşman altyapısı çöktü, takviye üretilemez
                return;
            }

            // 1. Sahadaki canlı düşman birimlerini say
            int enemyCount = 0;
            foreach (var u in RTSUnit.AllUnits)
            {
                if (u != null && u.team == UnitTeam.Enemy && u.currentHp > 0)
                {
                    enemyCount++;
                }
            }

            Vector3 spawnOrigin = enemyBase.transform.position + new Vector3(0, 0, -3f);

            // 2. Eğer düşman sayısı 5'in altındaysa: Kışladan takviye üret
            if (enemyCount < 5)
            {
                string[] possibleTypes = new string[] { "cavalry", "spearman", "archer" };
                string chosenType = possibleTypes[UnityEngine.Random.Range(0, possibleTypes.Length)];
                SpawnRivalUnit(chosenType, spawnOrigin + new Vector3(-2f, 0, 0));
                SpawnRivalUnit("cavalry", spawnOrigin + new Vector3(2f, 0, 0));

                Debug.Log($"[DÜŞMAN KOMUTANI] Düşman Kışlasından takviye ({chosenType} + süvari) sevk edildi.");
            }
            else
            {
                // 3. Düşman ordusu yeterliyse: Mavi Üsse veya Savunma Boğazına Taarruz Emri Ver!
                Vector3 playerBasePos = new Vector3(0, 0.1f, -10f);
                foreach (var u in RTSUnit.AllUnits)
                {
                    if (u != null && u.team == UnitTeam.Enemy && u.currentHp > 0)
                    {
                        // Sadece boşta olan düşman askerlerini taarruza sevk et
                        if (u.currentAttackTarget == null && !u.isMoving)
                        {
                            u.MoveTo(playerBasePos + new Vector3(UnityEngine.Random.Range(-5f, 5f), 0, UnityEngine.Random.Range(-5f, 5f)));
                        }
                    }
                }

                Debug.Log("⚔️ [DÜŞMAN KOMUTANI] Düşman birliği Mavi Üsse doğru taarruz intikaline geçti.");
            }
        }

        private void SpawnRivalUnit(string type, Vector3 pos)
        {
            RTSUnit.SpawnUnit(type, pos, UnitTeam.Enemy);
        }
    }
}
