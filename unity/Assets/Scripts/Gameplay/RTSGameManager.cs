using System;
using System.Collections.Generic;
using UnityEngine;
using CSE377.AI;
using Random = UnityEngine.Random;

namespace CSE377.Gameplay
{
    /// <summary>
    /// RTS Oyun ve Simülasyon Yöneticisi.
    /// Canlı kaynakları yönetir, sahadaki duruma göre AiSafeSnapshot üretir
    /// ve onaylanan eylem planlarını (TRAIN_UNIT, MOVE_GROUP) fiziksel dünyaya uygular.
    /// </summary>
    public class RTSGameManager : MonoBehaviour
    {
        public static RTSGameManager Instance { get; private set; }

        /// <summary>
        /// Bu istemcideki oyuncunun yönettiği takım ve kendi AI Komutanı (1. Oyuncu: Mavi / Player, 2. Oyuncu: Kırmızı / Enemy).
        /// </summary>
        public static UnitTeam LocalPlayerTeam = UnitTeam.Player;
        public static bool IsMultiplayerPvP = false;

        [Header("Oyuncu Kaynakları")]
        public int wood = 650;
        public int food = 400;
        public int gold = 250;

        [Header("Maç İstatistikleri")]
        public int totalWoodGathered = 0;
        public int totalFoodGathered = 0;
        public int totalGoldGathered = 0;
        public int enemiesKilled = 0;
        public int unitsLost = 0;
        public int buildingsConstructed = 0;
        public float matchStartTime;
        public bool isGameOver = false;

        [Header("Birim Üretim Noktası (Kışla / Üs)")]
        public Transform playerSpawnPoint;

        [Header("Prefab / Prefab Şablonu")]
        public GameObject unitBasePrefab;

        [Header("HUD Kontrolcüsü")]
        public RTSHUDController hudController;

        [Header("Dinamik Düşman Akın & Baskın Sistemi")]
        public bool enableEnemyRaids = true;
        public int currentRaidWave = 0;
        private float nextRaidTime = 0f;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            matchStartTime = Time.time;
        }

        public void StartMatch()
        {
            matchStartTime = Time.time;
            nextRaidTime = Time.time + GetInitialRaidDelay();
            currentRaidWave = 0;
            enemiesKilled = 0;
            unitsLost = 0;
            buildingsConstructed = 0;
            totalWoodGathered = 0;
            totalFoodGathered = 0;
            totalGoldGathered = 0;
            isGameOver = false;
            UpdateHUD();
            Debug.Log($"[RTSGameManager] Yeni Sefer Başlatıldı! İlk akın {GetInitialRaidDelay():F0}s sonra gerçekleşecek.");
        }

        public float GetTimeUntilNextRaid()
        {
            if (isGameOver || !enableEnemyRaids) return 0f;
            return Mathf.Max(0f, nextRaidTime - Time.time);
        }

        public int GetCurrentRaidWave() => currentRaidWave;
        public float GetNextRaidTime() => nextRaidTime;

        private void Start()
        {
            UpdateHUD();
            nextRaidTime = Time.time + GetInitialRaidDelay();
        }

        private void Update()
        {
            if (isGameOver || !enableEnemyRaids || IsMultiplayerPvP || (RTSNetworkSyncManager.Instance != null && RTSNetworkSyncManager.Instance.isMultiplayerMode)) return;

            // Başlangıç ekranı modalı açıkken veya oyun duraklatılmışken sayaç beklemede kalır
            if (Time.timeScale == 0f || (hudController != null && hudController.startScreenModal != null && hudController.startScreenModal.activeSelf))
            {
                return;
            }

            if (Time.time >= nextRaidTime)
            {
                TriggerEnemyRaid();
            }
        }

        private float GetInitialRaidDelay()
        {
            switch (RTSHUDController.CurrentDifficulty)
            {
                case RTSDifficulty.Easy: return 210f;   // 3.5 dakika
                case RTSDifficulty.Hard: return 90f;    // 1.5 dakika
                default: return 150f;                   // 2.5 dakika (Normal)
            }
        }

        private float GetRaidInterval()
        {
            switch (RTSHUDController.CurrentDifficulty)
            {
                case RTSDifficulty.Easy: return 180f;   // 3 dakika
                case RTSDifficulty.Hard: return 85f;    // 1 dakika 25 saniye
                default: return 120f;                   // 2 dakika (Normal)
            }
        }

        public void ResetEconomyForNewMatch()
        {
            wood = 650;
            food = 400;
            gold = 250;
            currentRaidWave = 0;
            enemiesKilled = 0;
            unitsLost = 0;
            buildingsConstructed = 0;
            totalWoodGathered = 0;
            totalFoodGathered = 0;
            totalGoldGathered = 0;
            isGameOver = false;
            matchStartTime = Time.time;
            nextRaidTime = Time.time + GetInitialRaidDelay();
            RTSTechManager.Instance?.ResetTechsForNewMatch();
            UpdateHUD();
        }

        public void TriggerEnemyRaid()
        {
            currentRaidWave++;
            nextRaidTime = Time.time + GetRaidInterval();

            int wave = currentRaidWave;
            var diff = RTSHUDController.CurrentDifficulty;
            string waveTitle = "";
            string waveDetails = "";

            if (RTSAutoBootstrap.SelectedMap == RTSMapType.OpenSteppe)
            {
                TriggerEnemyRaid_OpenSteppe(wave, diff, out waveTitle, out waveDetails);
            }
            else if (RTSAutoBootstrap.SelectedMap == RTSMapType.BlackForest)
            {
                TriggerEnemyRaid_BlackForest(wave, diff, out waveTitle, out waveDetails);
            }
            else if (RTSAutoBootstrap.SelectedMap == RTSMapType.CrossCorners)
            {
                TriggerEnemyRaid_CrossCorners(wave, diff, out waveTitle, out waveDetails);
            }
            else
            {
                TriggerEnemyRaid_RiverValley(wave, diff, out waveTitle, out waveDetails);
            }

            // Ses ve Arayüz Alarmı
            RTSAudioManager.Instance?.PlaySound(RTSSoundType.AlarmHorn);
            RTSAudioManager.Instance?.PlaySound(RTSSoundType.WarHorn);
            RTSAudioManager.Instance?.NotifyCombat();

            // Düşmanın geldiği hatta minimap alarm dalgası gönder
            Vector3 enemyIncomingPos = (RTSAutoBootstrap.SelectedMap == RTSMapType.OpenSteppe)
                ? new Vector3(0, 0, 48f)
                : (RTSAutoBootstrap.SelectedMap == RTSMapType.CrossCorners)
                    ? new Vector3(45f, 0, 45f)
                    : new Vector3(0, 0, 0); // Nehir hattı
            RTSMinimapController.Instance?.AddPing(enemyIncomingPos, new Color32(255, 30, 30, 255), 7f);

            string raidMsg = $"[{wave}. DALGA: {waveTitle.ToUpper()}] {waveDetails} (Boşluk Tuşu: Olay Yerine Git)";
            Debug.Log($"[RTSGameManager] {raidMsg}");
            if (hudController != null)
            {
                hudController.SetAiStatus(raidMsg, Color.red);
            }
        }

        #region Harita Tabanlı Düşman Akınları (Map-Adaptive Raids)

        private void TriggerEnemyRaid_RiverValley(int wave, RTSDifficulty diff, out string waveTitle, out string waveDetails)
        {
            if (wave == 1)
            {
                waveTitle = "Batı Köprüsü Öncü Akını";
                waveDetails = "Düşman öncü atlıları ve mızrakçıları Batı Taş Köprüsünden hücuma geçti!";
                Vector3 spawnOrigin = new Vector3(Random.Range(-29f, -25f), 0.1f, 45f);
                var squad = new List<RTSUnit>();
                int scouts = (diff == RTSDifficulty.Easy) ? 2 : (diff == RTSDifficulty.Hard ? 4 : 3);
                int spears = (diff == RTSDifficulty.Easy) ? 1 : (diff == RTSDifficulty.Hard ? 3 : 2);
                for (int i = 0; i < scouts; i++) squad.Add(RTSUnit.SpawnUnit("scout", spawnOrigin + new Vector3(i * 1.6f - 1.5f, 0, Random.Range(-1f, 1f)), UnitTeam.Enemy));
                for (int i = 0; i < spears; i++) squad.Add(RTSUnit.SpawnUnit("spearman", spawnOrigin + new Vector3(-i * 1.5f, 0, -2f), UnitTeam.Enemy));

                Vector3 baseTarget = new Vector3(Random.Range(-24f, -14f), 0.1f, -42f);
                foreach (var u in squad) if (u != null) u.MoveTo(baseTarget + new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f)), isManual: false, forcedCrossingX: -28f);
            }
            else if (wave == 2)
            {
                waveTitle = "Merkez Sığlık Taburu";
                waveDetails = "Düşman mızrakçı ve okçu taburu merkez nehir sığlığını aşıyor!";
                Vector3 spawnOrigin = new Vector3(Random.Range(-4f, 4f), 0.1f, 45f);
                var squad = new List<RTSUnit>();
                int spears = (diff == RTSDifficulty.Easy) ? 3 : (diff == RTSDifficulty.Hard ? 6 : 4);
                int archers = (diff == RTSDifficulty.Easy) ? 2 : (diff == RTSDifficulty.Hard ? 4 : 3);
                for (int i = 0; i < spears; i++) squad.Add(RTSUnit.SpawnUnit("spearman", spawnOrigin + new Vector3(i * 1.5f - 2f, 0, 0), UnitTeam.Enemy));
                for (int i = 0; i < archers; i++) squad.Add(RTSUnit.SpawnUnit("archer", spawnOrigin + new Vector3(-i * 1.5f + 1f, 0, -2.5f), UnitTeam.Enemy));

                Vector3 baseTarget = new Vector3(Random.Range(-6f, 6f), 0.1f, -40f);
                foreach (var u in squad) if (u != null) u.MoveTo(baseTarget + new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f)), isManual: false, forcedCrossingX: 0f);
            }
            else if (wave == 3)
            {
                waveTitle = "Doğu Köprüsü Ağır Kuşatması";
                waveDetails = "Düşman ağır süvari, kılıç ve arbalet alayı Doğu Taş Köprüsünden taarruz başlattı!";
                Vector3 spawnOrigin = new Vector3(Random.Range(25f, 29f), 0.1f, 45f);
                var squad = new List<RTSUnit>();
                int swords = (diff == RTSDifficulty.Easy) ? 3 : (diff == RTSDifficulty.Hard ? 6 : 4);
                int crossbows = (diff == RTSDifficulty.Easy) ? 2 : (diff == RTSDifficulty.Hard ? 4 : 3);
                int knights = (diff == RTSDifficulty.Easy) ? 1 : (diff == RTSDifficulty.Hard ? 3 : 2);
                for (int i = 0; i < swords; i++) squad.Add(RTSUnit.SpawnUnit("swordsman", spawnOrigin + new Vector3(i * 1.5f - 2f, 0, 0), UnitTeam.Enemy));
                for (int i = 0; i < crossbows; i++) squad.Add(RTSUnit.SpawnUnit("crossbowman", spawnOrigin + new Vector3(-i * 1.5f + 1f, 0, -2.5f), UnitTeam.Enemy));
                for (int i = 0; i < knights; i++) squad.Add(RTSUnit.SpawnUnit("cavalry", spawnOrigin + new Vector3(i * 2f - 1f, 0, -4.5f), UnitTeam.Enemy));

                Vector3 baseTarget = new Vector3(Random.Range(10f, 22f), 0.1f, -42f);
                foreach (var u in squad) if (u != null) u.MoveTo(baseTarget + new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f)), isManual: false, forcedCrossingX: 28f);
            }
            else
            {
                waveTitle = "Kıskaç Harekâtı & Büyük Mancınık Kuşatması";
                waveDetails = "Düşman hem Batı hem Doğu köprüsünden eş zamanlı mancınık ve süvari kıskaç harekâtı başlattı!";
                Vector3 westSpawn = new Vector3(Random.Range(-29f, -25f), 0.1f, 45f);
                var westSquad = new List<RTSUnit>();
                int cavCount = (diff == RTSDifficulty.Hard) ? 4 : 3;
                int swordCount = (diff == RTSDifficulty.Hard) ? 4 : 2;
                for (int i = 0; i < cavCount; i++) westSquad.Add(RTSUnit.SpawnUnit("cavalry", westSpawn + new Vector3(i * 2f - 2f, 0, 0), UnitTeam.Enemy));
                for (int i = 0; i < swordCount; i++) westSquad.Add(RTSUnit.SpawnUnit("swordsman", westSpawn + new Vector3(-i * 1.5f, 0, -3f), UnitTeam.Enemy));

                Vector3 westTarget = new Vector3(Random.Range(-22f, -12f), 0.1f, -42f);
                foreach (var u in westSquad) if (u != null) u.MoveTo(westTarget + new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f)), isManual: false, forcedCrossingX: -28f);

                Vector3 eastSpawn = new Vector3(Random.Range(25f, 29f), 0.1f, 45f);
                var eastSquad = new List<RTSUnit>();
                int trebuchetCount = (diff == RTSDifficulty.Hard && wave >= 5) ? 2 : 1;
                int crossbowCount = (diff == RTSDifficulty.Hard) ? 4 : 3;
                int guardCount = (diff == RTSDifficulty.Hard) ? 4 : 3;
                for (int i = 0; i < trebuchetCount; i++) eastSquad.Add(RTSUnit.SpawnUnit("trebuchet", eastSpawn + new Vector3(i * 3.5f, 0, -4f), UnitTeam.Enemy));
                for (int i = 0; i < crossbowCount; i++) eastSquad.Add(RTSUnit.SpawnUnit("crossbowman", eastSpawn + new Vector3(i * 1.5f - 1.5f, 0, -2f), UnitTeam.Enemy));
                for (int i = 0; i < guardCount; i++) eastSquad.Add(RTSUnit.SpawnUnit("swordsman", eastSpawn + new Vector3(-i * 1.5f, 0, 0), UnitTeam.Enemy));

                Vector3 townCenterTarget = new Vector3(Random.Range(-4f, 4f), 0.1f, -46f);
                foreach (var u in eastSquad) if (u != null) u.MoveTo(townCenterTarget + new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f)), isManual: false, forcedCrossingX: 28f);
            }
        }

        private void TriggerEnemyRaid_OpenSteppe(int wave, RTSDifficulty diff, out string waveTitle, out string waveDetails)
        {
            Vector3 pBase = RTSAutoBootstrap.CurrentPlayerBasePos;

            if (wave == 1)
            {
                waveTitle = "Bozkır Batı Kanadı Süvari Akını";
                waveDetails = "Düşman öncü atlıları ve mızrakçıları açık bozkırın batısından yıldırım hücumuna geçti!";
                Vector3 spawnOrigin = new Vector3(Random.Range(-36f, -30f), 0.1f, 38f);
                var squad = new List<RTSUnit>();
                int scouts = (diff == RTSDifficulty.Easy) ? 3 : (diff == RTSDifficulty.Hard ? 5 : 4);
                int spears = (diff == RTSDifficulty.Easy) ? 1 : (diff == RTSDifficulty.Hard ? 3 : 2);
                for (int i = 0; i < scouts; i++) squad.Add(RTSUnit.SpawnUnit("scout", spawnOrigin + new Vector3(i * 1.6f - 1.5f, 0, Random.Range(-1f, 1f)), UnitTeam.Enemy));
                for (int i = 0; i < spears; i++) squad.Add(RTSUnit.SpawnUnit("spearman", spawnOrigin + new Vector3(-i * 1.5f, 0, -2f), UnitTeam.Enemy));

                Vector3 target = pBase + new Vector3(Random.Range(-10f, -4f), 0, Random.Range(2f, 8f));
                foreach (var u in squad) if (u != null) u.MoveTo(target, isManual: false);
            }
            else if (wave == 2)
            {
                waveTitle = "Açık Meydan Piyade Taburu";
                waveDetails = "Düşman düzenli mızrakçı ve okçu safları bozkırın ortasından üssünüze doğru ilerliyor!";
                Vector3 spawnOrigin = new Vector3(Random.Range(-6f, 6f), 0.1f, 38f);
                var squad = new List<RTSUnit>();
                int spears = (diff == RTSDifficulty.Easy) ? 3 : (diff == RTSDifficulty.Hard ? 6 : 4);
                int archers = (diff == RTSDifficulty.Easy) ? 3 : (diff == RTSDifficulty.Hard ? 5 : 4);
                for (int i = 0; i < spears; i++) squad.Add(RTSUnit.SpawnUnit("spearman", spawnOrigin + new Vector3(i * 1.5f - 2f, 0, 0), UnitTeam.Enemy));
                for (int i = 0; i < archers; i++) squad.Add(RTSUnit.SpawnUnit("archer", spawnOrigin + new Vector3(-i * 1.5f + 1f, 0, -2.5f), UnitTeam.Enemy));

                Vector3 target = pBase + new Vector3(Random.Range(-4f, 4f), 0, Random.Range(4f, 10f));
                foreach (var u in squad) if (u != null) u.MoveTo(target, isManual: false);
            }
            else if (wave == 3)
            {
                waveTitle = "Doğu Bozkır Ağır Şövalye Hücumu";
                waveDetails = "Düşman ağır şövalyeleri ve zırh delici arbaletçileri doğu kanadından taarruz başlattı!";
                Vector3 spawnOrigin = new Vector3(Random.Range(30f, 36f), 0.1f, 38f);
                var squad = new List<RTSUnit>();
                int knights = (diff == RTSDifficulty.Easy) ? 2 : (diff == RTSDifficulty.Hard ? 4 : 3);
                int crossbows = (diff == RTSDifficulty.Easy) ? 2 : (diff == RTSDifficulty.Hard ? 4 : 3);
                int swords = (diff == RTSDifficulty.Easy) ? 2 : (diff == RTSDifficulty.Hard ? 4 : 3);
                for (int i = 0; i < knights; i++) squad.Add(RTSUnit.SpawnUnit("cavalry", spawnOrigin + new Vector3(i * 2f - 1f, 0, 0), UnitTeam.Enemy));
                for (int i = 0; i < crossbows; i++) squad.Add(RTSUnit.SpawnUnit("crossbowman", spawnOrigin + new Vector3(-i * 1.5f, 0, -2.5f), UnitTeam.Enemy));
                for (int i = 0; i < swords; i++) squad.Add(RTSUnit.SpawnUnit("swordsman", spawnOrigin + new Vector3(i * 1.5f, 0, -2f), UnitTeam.Enemy));

                Vector3 target = pBase + new Vector3(Random.Range(4f, 10f), 0, Random.Range(2f, 8f));
                foreach (var u in squad) if (u != null) u.MoveTo(target, isManual: false);
            }
            else
            {
                waveTitle = "Büyük Bozkır Kıskaç & Kuşatma Seferi";
                waveDetails = "Düşman açık arazinin iki kanadından süvarilerle kıskaç harekâtı ve merkezden mancınık kuşatması başlattı!";

                // Batı Kanat
                Vector3 westSpawn = new Vector3(-32f, 0.1f, 38f);
                for (int i = 0; i < 3; i++) RTSUnit.SpawnUnit("cavalry", westSpawn + new Vector3(i * 2f, 0, 0), UnitTeam.Enemy)?.MoveTo(pBase + new Vector3(-8f, 0, 4f), false);

                // Doğu Kanat
                Vector3 eastSpawn = new Vector3(32f, 0.1f, 38f);
                for (int i = 0; i < 3; i++) RTSUnit.SpawnUnit("cavalry", eastSpawn + new Vector3(-i * 2f, 0, 0), UnitTeam.Enemy)?.MoveTo(pBase + new Vector3(8f, 0, 4f), false);

                // Merkez Kuşatma
                Vector3 centerSpawn = new Vector3(0f, 0.1f, 40f);
                RTSUnit.SpawnUnit("trebuchet", centerSpawn, UnitTeam.Enemy)?.MoveTo(pBase + new Vector3(0, 0, 8f), false);
                for (int i = 0; i < 3; i++) RTSUnit.SpawnUnit("swordsman", centerSpawn + new Vector3(i * 1.8f - 1.8f, 0, -2f), UnitTeam.Enemy)?.MoveTo(pBase + new Vector3(0, 0, 6f), false);
                for (int i = 0; i < 3; i++) RTSUnit.SpawnUnit("crossbowman", centerSpawn + new Vector3(i * 1.8f - 1.8f, 0, -4f), UnitTeam.Enemy)?.MoveTo(pBase + new Vector3(0, 0, 6f), false);
            }
        }

        private void TriggerEnemyRaid_BlackForest(int wave, RTSDifficulty diff, out string waveTitle, out string waveDetails)
        {
            Vector3 pBase = RTSAutoBootstrap.CurrentPlayerBasePos;

            if (wave == 1)
            {
                waveTitle = "Kara Orman Batı Boğazı Sızması";
                waveDetails = "Düşman öncü birlikleri yoğun ağaçların arasından, Batı Boğazından sızma harekâtı başlattı!";
                Vector3 spawnOrigin = new Vector3(-21f, 0.1f, 26f);
                var squad = new List<RTSUnit>();
                int scouts = (diff == RTSDifficulty.Easy) ? 2 : 4;
                int spears = (diff == RTSDifficulty.Easy) ? 2 : 3;
                for (int i = 0; i < scouts; i++) squad.Add(RTSUnit.SpawnUnit("scout", spawnOrigin + new Vector3(Random.Range(-1.5f, 1.5f), 0, i * 1.5f), UnitTeam.Enemy));
                for (int i = 0; i < spears; i++) squad.Add(RTSUnit.SpawnUnit("spearman", spawnOrigin + new Vector3(Random.Range(-1.5f, 1.5f), 0, -i * 1.5f), UnitTeam.Enemy));

                foreach (var u in squad) if (u != null) u.MoveTo(pBase + new Vector3(-4f, 0, 6f), isManual: false);
            }
            else if (wave == 2)
            {
                waveTitle = "Doğu Boğazı Ağır Piyade Taburu";
                waveDetails = "Düşman kılıçlı ve mızrakçı taburu Doğu Orman Boğazını zorlayarak üssünüze akıyor!";
                Vector3 spawnOrigin = new Vector3(21f, 0.1f, 26f);
                var squad = new List<RTSUnit>();
                int swords = (diff == RTSDifficulty.Easy) ? 3 : 5;
                int archers = (diff == RTSDifficulty.Easy) ? 2 : 4;
                for (int i = 0; i < swords; i++) squad.Add(RTSUnit.SpawnUnit("swordsman", spawnOrigin + new Vector3(Random.Range(-1.5f, 1.5f), 0, i * 1.5f), UnitTeam.Enemy));
                for (int i = 0; i < archers; i++) squad.Add(RTSUnit.SpawnUnit("archer", spawnOrigin + new Vector3(Random.Range(-1.5f, 1.5f), 0, -i * 1.5f), UnitTeam.Enemy));

                foreach (var u in squad) if (u != null) u.MoveTo(pBase + new Vector3(4f, 0, 6f), isManual: false);
            }
            else if (wave == 3)
            {
                waveTitle = "Kara Orman Sur Delen Ağır Kuşatma Kolu";
                waveDetails = "Düşman Batı Boğazından devasa Kuşatma Mancınığı ve arbaletçilerle savunma hatlarınızı yarmaya geliyor!";
                Vector3 spawnOrigin = new Vector3(-21f, 0.1f, 28f);
                RTSUnit.SpawnUnit("trebuchet", spawnOrigin, UnitTeam.Enemy)?.MoveTo(pBase + new Vector3(-6f, 0, 10f), false);
                for (int i = 0; i < 3; i++) RTSUnit.SpawnUnit("crossbowman", spawnOrigin + new Vector3(i * 1.5f - 1.5f, 0, -2f), UnitTeam.Enemy)?.MoveTo(pBase + new Vector3(-4f, 0, 8f), false);
                for (int i = 0; i < 3; i++) RTSUnit.SpawnUnit("swordsman", spawnOrigin + new Vector3(i * 1.5f - 1.5f, 0, -4f), UnitTeam.Enemy)?.MoveTo(pBase + new Vector3(-4f, 0, 8f), false);
            }
            else
            {
                waveTitle = "Çift Boğaz Koordineli Yarma Harekâtı";
                waveDetails = "Düşman hem Batı hem Doğu vadi boğazından eş zamanlı mancınık ve şövalye ordusuyla topyekün taarruz başlattı!";
                // Batı
                RTSUnit.SpawnUnit("trebuchet", new Vector3(-21f, 0.1f, 30f), UnitTeam.Enemy)?.MoveTo(pBase + new Vector3(-6f, 0, 10f), false);
                for (int i = 0; i < 3; i++) RTSUnit.SpawnUnit("cavalry", new Vector3(-21f + i, 0.1f, 26f), UnitTeam.Enemy)?.MoveTo(pBase + new Vector3(-4f, 0, 6f), false);
                // Doğu
                RTSUnit.SpawnUnit("trebuchet", new Vector3(21f, 0.1f, 30f), UnitTeam.Enemy)?.MoveTo(pBase + new Vector3(6f, 0, 10f), false);
                for (int i = 0; i < 4; i++) RTSUnit.SpawnUnit("swordsman", new Vector3(21f + i, 0.1f, 26f), UnitTeam.Enemy)?.MoveTo(pBase + new Vector3(4f, 0, 6f), false);
            }
        }

        private void TriggerEnemyRaid_CrossCorners(int wave, RTSDifficulty diff, out string waveTitle, out string waveDetails)
        {
            Vector3 eBase = RTSAutoBootstrap.CurrentEnemyBasePos;
            Vector3 pBase = RTSAutoBootstrap.CurrentPlayerBasePos;

            if (wave == 1)
            {
                waveTitle = "Çapraz Yayla Öncü Taarruzu";
                waveDetails = "Düşman öncü atlıları Kuzey-Doğu kalesinden hareket ederek merkez harabeler üzerinden Güney-Batı'ya hücum başlattı!";
                Vector3 spawnOrigin = eBase + new Vector3(-6f, 0.1f, -6f);
                var squad = new List<RTSUnit>();
                int scouts = (diff == RTSDifficulty.Easy) ? 2 : 4;
                int spears = (diff == RTSDifficulty.Easy) ? 2 : 3;
                for (int i = 0; i < scouts; i++) squad.Add(RTSUnit.SpawnUnit("scout", spawnOrigin + new Vector3(i * 1.5f, 0, -i * 1.5f), UnitTeam.Enemy));
                for (int i = 0; i < spears; i++) squad.Add(RTSUnit.SpawnUnit("spearman", spawnOrigin + new Vector3(-i * 1.5f, 0, -i * 1.5f), UnitTeam.Enemy));

                foreach (var u in squad) if (u != null) u.MoveTo(pBase + new Vector3(4f, 0, 4f), isManual: false);
            }
            else if (wave == 2)
            {
                waveTitle = "Antik Mabet Zapt Kolu";
                waveDetails = "Düşman mızrakçı ve okçu alayı haritanın merkezindeki antik harabeleri aşarak üssünüze doğru yaklaşıyor!";
                Vector3 spawnOrigin = eBase + new Vector3(-8f, 0.1f, -8f);
                var squad = new List<RTSUnit>();
                int spears = (diff == RTSDifficulty.Easy) ? 3 : 5;
                int archers = (diff == RTSDifficulty.Easy) ? 3 : 5;
                for (int i = 0; i < spears; i++) squad.Add(RTSUnit.SpawnUnit("spearman", spawnOrigin + new Vector3(i * 1.5f, 0, 0), UnitTeam.Enemy));
                for (int i = 0; i < archers; i++) squad.Add(RTSUnit.SpawnUnit("archer", spawnOrigin + new Vector3(0, 0, -i * 1.5f), UnitTeam.Enemy));

                foreach (var u in squad) if (u != null) u.MoveTo(pBase + new Vector3(4f, 0, 4f), isManual: false);
            }
            else if (wave == 3)
            {
                waveTitle = "Güneydoğu Vadisi Ağır Şövalye Seferi";
                waveDetails = "Düşman ağır şövalyeleri güneydoğu dağ koridorundan dolanarak üssünüze arkadan çevirme harekâtı düzenliyor!";
                Vector3 spawnOrigin = eBase + new Vector3(2f, 0.1f, -12f);
                var squad = new List<RTSUnit>();
                int cavCount = (diff == RTSDifficulty.Easy) ? 2 : 4;
                int xbowCount = (diff == RTSDifficulty.Easy) ? 2 : 4;
                for (int i = 0; i < cavCount; i++) squad.Add(RTSUnit.SpawnUnit("cavalry", spawnOrigin + new Vector3(i * 2f, 0, -i * 1.5f), UnitTeam.Enemy));
                for (int i = 0; i < xbowCount; i++) squad.Add(RTSUnit.SpawnUnit("crossbowman", spawnOrigin + new Vector3(-i * 1.5f, 0, -i * 1.5f), UnitTeam.Enemy));

                foreach (var u in squad) if (u != null) u.MoveTo(pBase + new Vector3(6f, 0, 2f), isManual: false);
            }
            else
            {
                waveTitle = "Büyük Çapraz Kuşatma & Mancınık Seferi";
                waveDetails = "Düşman merkez mabedi ele geçiren dev Trebuchet mancınıkları ve şövalye ordusuyla karargahınıza taarruza geçti!";
                Vector3 spawnOrigin = eBase + new Vector3(-8f, 0.1f, -8f);
                RTSUnit.SpawnUnit("trebuchet", spawnOrigin, UnitTeam.Enemy)?.MoveTo(pBase + new Vector3(8f, 0, 8f), false);
                for (int i = 0; i < 3; i++) RTSUnit.SpawnUnit("cavalry", spawnOrigin + new Vector3(i * 2f, 0, 0), UnitTeam.Enemy)?.MoveTo(pBase + new Vector3(4f, 0, 4f), false);
                for (int i = 0; i < 3; i++) RTSUnit.SpawnUnit("swordsman", spawnOrigin + new Vector3(0, 0, -i * 2f), UnitTeam.Enemy)?.MoveTo(pBase + new Vector3(4f, 0, 4f), false);
                for (int i = 0; i < 3; i++) RTSUnit.SpawnUnit("crossbowman", spawnOrigin + new Vector3(i * 1.5f, 0, -i * 1.5f), UnitTeam.Enemy)?.MoveTo(pBase + new Vector3(4f, 0, 4f), false);
            }
        }

        #endregion

        public void RecordBuildingConstructed()
        {
            buildingsConstructed++;
        }

        /// <summary>
        /// Sahadaki tüm canlı oyuncu birimlerini (asker + köylü) sayarak mevcut nüfusu döndürür.
        /// </summary>
        public int CalculateCurrentPopulation(UnitTeam? teamOverride = null)
        {
            UnitTeam targetTeam = teamOverride ?? LocalPlayerTeam;
            int count = 0;
            foreach (var u in RTSUnit.AllUnits)
            {
                if (u != null && u.team == targetTeam && u.currentHp > 0)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Şehir Merkezi (10 taban nüfus) ve tamamlanmış her Evden (+5 nüfus) azami nüfus tavanını hesaplar.
        /// </summary>
        public int CalculateMaxPopulation(UnitTeam? teamOverride = null)
        {
            UnitTeam targetTeam = teamOverride ?? LocalPlayerTeam;
            // Taban başlangıç nüfus kapasitesi: Her iki takım (Mavi ve Kırmızı) için eşit 10 nüfus
            int cap = 10;

            foreach (var b in RTSBuilding.AllBuildings)
            {
                if (b != null && b.team == targetTeam && b.isConstructed)
                {
                    if (b.buildingType == RTSBuildingType.House)
                    {
                        cap += 5;
                    }
                    else if (b.buildingType == RTSBuildingType.TownCenter)
                    {
                        // İkinci bir Şehir Merkezi kurulursa ek +10 nüfus
                    }
                }
            }

            return Mathf.Clamp(cap, 10, 60);
        }

        public void UpdateHUD()
        {
            int currentPop = CalculateCurrentPopulation();
            int maxPop = CalculateMaxPopulation();

            if (hudController != null)
            {
                hudController.UpdateResourceUI(wood, food, gold, currentPop, maxPop);
            }
        }

        public void OnUnitDied(RTSUnit unit)
        {
            if (unit.team != LocalPlayerTeam)
            {
                enemiesKilled++;
            }
            else
            {
                unitsLost++;
            }

            UpdateHUD();
        }

        public void OnBuildingDestroyed(RTSBuilding building)
        {
            if (isGameOver || building == null) return;

            float matchTime = Time.time - matchStartTime;
            if (matchTime < 1.5f) return;

            if (building.buildingType == RTSBuildingType.TownCenter)
            {
                UnitTeam winnerTeam = (building.team == UnitTeam.Player) ? UnitTeam.Enemy : UnitTeam.Player;
                isGameOver = true;

                // Çok oyunculu odadaki diğer oyuncuya da maçın bittiğini (Kazanan: ZAFER / Kaybeden: YENİLGİ) anında bildir!
                RTSNetworkSyncManager.Instance?.BroadcastGameOver(winnerTeam);

                TriggerGameOver(isVictory: (winnerTeam == LocalPlayerTeam));
            }
        }

        /// <summary>
        /// Ağ üzerinden maç sonu (GAME_OVER) sinyali alındığında kazanan oyuncuda ZAFER, kaybeden oyuncuda YENİLGİ ekranını açar.
        /// </summary>
        public void ForceGameOverFromNetwork(UnitTeam winnerTeam)
        {
            if (isGameOver) return;
            isGameOver = true;

            UnitTeam loserTeam = (winnerTeam == UnitTeam.Player) ? UnitTeam.Enemy : UnitTeam.Player;
            var allBlds = new List<RTSBuilding>(RTSBuilding.AllBuildings);
            foreach (var b in allBlds)
            {
                if (b != null && b.team == loserTeam && b.buildingType == RTSBuildingType.TownCenter && b.currentHp > 0)
                {
                    b.ApplyNetworkHp(0);
                }
            }

            TriggerGameOver(isVictory: (winnerTeam == LocalPlayerTeam));
        }

        private void TriggerGameOver(bool isVictory)
        {
            float elapsedSeconds = Time.time - matchStartTime;
            int minutes = Mathf.FloorToInt(elapsedSeconds / 60f);
            int seconds = Mathf.FloorToInt(elapsedSeconds % 60f);
            string timeStr = $"{minutes:00}:{seconds:00}";

            // AI Başarı Puanı (% hesaplama)
            int score = Mathf.Clamp(70 + (enemiesKilled * 5) - (unitsLost * 4) + (buildingsConstructed * 6), 35, 99);
            string rankTitle = (score >= 90) ? "Büyük Mareşal (S+)" :
                               (score >= 80) ? "Usta Stratejist (A)" :
                               (score >= 65) ? "Kıdemli Komutan (B)" : "Acemi Muhafız (C)";

            string stats = 
                $"Maç Süresi: {timeStr}\n" +
                $"Toplanan Kaynaklar: {totalWoodGathered} Odun  |  {totalFoodGathered} Yiyecek  |  {totalGoldGathered} Altın\n" +
                $"Düşman Kayıpları: {enemiesKilled} Birlik İmha Edildi\n" +
                $"Şehit Olan Birlikler: {unitsLost} Kayıp\n" +
                $"Tamamlanan İnşaatlar: {buildingsConstructed} Yapı\n" +
                $"AI Komutanı Strateji Puanı: %{score} - {rankTitle}";

            if (isVictory)
            {
                hudController?.SetAiStatus("[DESTANSI ZAFER] Düşman Karargahı Düştü! Harita Fethedildi!", Color.yellow);
                RTSAudioManager.Instance?.PlaySound(RTSSoundType.VictoryFanfare);
                RTSAudioManager.Instance?.PlayVoice("victory", force: true);
            }
            else
            {
                hudController?.SetAiStatus("☠️ [AĞIR YENİLGİ] Şehir Merkeziniz Yıkıldı! Krallık Düştü!", Color.red);
                RTSAudioManager.Instance?.PlaySound(RTSSoundType.AlarmHorn);
            }

            hudController?.ShowGameOverModal(isVictory, stats);
        }

        /// <summary>
        /// Köylülerin Şehir Merkezine getirdiği kaynakları canlı bütçeye ekler ve HUD'ı günceller.
        /// </summary>
        public void DepositResources(RTSResourceType type, int amount)
        {
            switch (type)
            {
                case RTSResourceType.Wood: wood += amount; totalWoodGathered += amount; break;
                case RTSResourceType.Food: food += amount; totalFoodGathered += amount; break;
                case RTSResourceType.Gold: gold += amount; totalGoldGathered += amount; break;
            }
            UpdateHUD();
        }

        /// <summary>
        /// Sahnedeki canlı nesnelerden yerel oyuncunun takımına (BLUE veya RED) özel,
        /// hilesiz (Fog-of-War uyumlu) AiSafeSnapshot derler.
        /// </summary>
        public AiSafeSnapshot GenerateLiveSnapshot()
        {
            var snapshot = new AiSafeSnapshot();
            snapshot.team_id = (LocalPlayerTeam == UnitTeam.Enemy) ? "RED" : "BLUE";
            snapshot.match_id = (RTSNetworkSyncManager.Instance != null && RTSNetworkSyncManager.Instance.isMultiplayerMode)
                ? RTSNetworkSyncManager.Instance.matchRoomId
                : "live-rts-match";
            snapshot.resources = new AiSafeSnapshot.ResourceState(wood, food, gold);

            // Sahadaki dost orduyu ve görüş alanındaki rakip birimleri say
            var armyCounts = new Dictionary<string, int>();
            var enemyCounts = new Dictionary<string, int>();

            foreach (var u in RTSUnit.AllUnits)
            {
                if (u == null || u.currentHp <= 0) continue;

                string type = u.unitType.ToLower();
                if (u.team == LocalPlayerTeam)
                {
                    armyCounts[type] = armyCounts.getOrDefault(type, 0) + 1;
                }
                else
                {
                    // Savaş Sisi Filtresi (Hilesiz Anti-Cheat):
                    // Yalnızca bu oyuncunun görüş alanına girmiş rakip birimler kendi AI Komutanına bildirilir!
                    bool isVisible = (RTSFogOfWarManager.Instance != null)
                        ? RTSFogOfWarManager.Instance.IsUnitVisible(u)
                        : true;

                    if (isVisible)
                    {
                        enemyCounts[type] = enemyCounts.getOrDefault(type, 0) + 1;
                    }
                }
            }

            foreach (var pair in armyCounts)
            {
                snapshot.army.Add(new AiSafeSnapshot.UnitGroup(pair.Key, pair.Value));
            }

            foreach (var pair in enemyCounts)
            {
                snapshot.visible_enemies.Add(new AiSafeSnapshot.EnemyGroup(pair.Key, pair.Value, "north_gate"));
            }

            if (RTSTechManager.Instance != null)
            {
                foreach (var tech in RTSTechManager.Instance.unlockedTechs)
                {
                    if (!snapshot.unlocked_tech.Contains(tech))
                    {
                        snapshot.unlocked_tech.Add(tech);
                    }
                }
            }
            else
            {
                snapshot.unlocked_tech.Add("Age_1");
            }

            // Eğer görüş alanında düşman varsa saldırı alarmı ekle
            if (enemyCounts.Count > 0)
            {
                snapshot.recent_events.Add(new AiSafeSnapshot.GameEventSnapshot("UNDER_ATTACK", "north_gate", 5));
            }

            return snapshot;
        }

        /// <summary>
        /// Oyuncunun onayladığı eylem planını oyun dünyasında icra eder ve Photon odasına senkronize eder.
        /// </summary>
        public void ExecuteApprovedPlan(RTSActionPlan plan)
        {
            ExecuteApprovedPlanForTeam(plan, LocalPlayerTeam, broadcastOverNetwork: true);
        }

        /// <summary>
        /// Belirtilen takımın (Mavi veya Kırmızı AI Komutanı) onaylanan eylem planını sahada icra eder.
        /// </summary>
        public void ExecuteApprovedPlanForTeam(RTSActionPlan plan, UnitTeam execTeam, bool broadcastOverNetwork = false)
        {
            if (plan == null || plan.actions == null) return;

            Debug.Log($"[RTSGameManager] ({execTeam}) Onaylanan AI Komutan planı sahada icra ediliyor: '{plan.summary}'");
            RTSAudioManager.Instance?.PlaySound(RTSSoundType.TechComplete);

            if (broadcastOverNetwork)
            {
                RTSNetworkSyncManager.Instance?.BroadcastApprovedPlan(execTeam, plan);
            }

            foreach (var action in plan.actions)
            {
                if (broadcastOverNetwork)
                {
                    RTSNetworkSyncManager.Instance?.DispatchActionRpc(action);
                }

                switch (action.action_type.ToUpper())
                {
                    case "TRAIN_UNIT":
                        if (broadcastOverNetwork)
                        {
                            TrainUnits(action.unit_type, action.count, action.target_zone, execTeam);
                        }
                        break;

                    case "MOVE_GROUP":
                        MoveUnits(action.unit_type, action.count, action.target_zone, execTeam);
                        break;

                    case "ATTACK":
                    case "ATTACK_MOVE":
                        ExecuteAttackAction(action.unit_type, action.count, action.target_zone, execTeam);
                        break;

                    case "SCOUT":
                    case "EXPLORE":
                        ExecuteScoutAction(action.unit_type, action.count, action.target_zone, execTeam);
                        break;

                    case "BUILD":
                        if (broadcastOverNetwork)
                        {
                            string bTypeToBuild = !string.IsNullOrEmpty(action.building_type) ? action.building_type : action.unit_type;
                            ExecuteBuildAction(bTypeToBuild, action.target_zone, execTeam);
                        }
                        break;

                    case "RESEARCH":
                        if (execTeam == LocalPlayerTeam)
                        {
                            string tIdToResearch = !string.IsNullOrEmpty(action.building_type) ? action.building_type : action.unit_type;
                            if (string.IsNullOrEmpty(tIdToResearch) || !GameActionValidator.TechCosts.ContainsKey(tIdToResearch))
                            {
                                foreach (var key in GameActionValidator.TechCosts.Keys)
                                {
                                    if (!string.IsNullOrEmpty(action.reason) && action.reason.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        tIdToResearch = key;
                                        break;
                                    }
                                }
                            }
                            if (string.IsNullOrEmpty(tIdToResearch) || !GameActionValidator.TechCosts.ContainsKey(tIdToResearch))
                            {
                                tIdToResearch = "Wheelbarrow";
                            }
                            ExecuteResearchAction(tIdToResearch);
                        }
                        break;
                }
            }

            UpdateHUD();
        }

        private void ExecuteBuildAction(string buildingTypeStr, string targetZone, UnitTeam execTeam)
        {
            string bKey = string.IsNullOrEmpty(buildingTypeStr) ? "barracks" : buildingTypeStr.ToLower().Trim();
            if (bKey == "village_center" || bKey == "villagecenter" || bKey == "town_center") bKey = "towncenter";
            RTSBuildingType bType = RTSBuildingType.Barracks;
            if (bKey == "tower" || bKey == "defensetower") bType = RTSBuildingType.DefenseTower;
            else if (bKey == "blacksmith") bType = RTSBuildingType.Blacksmith;
            else if (bKey == "towncenter") bType = RTSBuildingType.TownCenter;
            else if (bKey == "house") bType = RTSBuildingType.House;
            else if (bKey == "farm") bType = RTSBuildingType.Farm;
            else if (bKey == "stonewall" || bKey == "stone_wall" || bKey == "wall") bType = RTSBuildingType.StoneWall;
            else if (bKey == "citygate" || bKey == "city_gate" || bKey == "gate") bType = RTSBuildingType.CityGate;
            else if (bKey == "castle") bType = RTSBuildingType.Castle;

            if (execTeam == LocalPlayerTeam && GameActionValidator.BuildingCosts.TryGetValue(bKey, out var cost))
            {
                wood -= cost.wood;
                food -= cost.food;
                gold -= cost.gold;
            }

            Vector3 fallbackBase = (execTeam == UnitTeam.Enemy) ? RTSAutoBootstrap.CurrentEnemyBasePos : RTSAutoBootstrap.CurrentPlayerBasePos;
            Vector3 zoneCenter = RTSZoneMarker.GetZonePosition(targetZone, fallbackBase, execTeam);
            Vector3 buildPos = zoneCenter + new Vector3(Random.Range(-4.5f, 4.5f), 0, Random.Range(-4.5f, 4.5f));

            var building = RTSBuilding.SpawnBuilding(bType, buildPos, execTeam, startBuilt: false);
            if (execTeam == LocalPlayerTeam)
            {
                RTSNetworkSyncManager.Instance?.BroadcastBuildingSpawn(bType, buildPos, execTeam, false);
            }

            // İlgili takımın uygun bir köylüsünü inşaat için görevlendir
            foreach (var u in RTSUnit.AllUnits)
            {
                if (u != null && u.team == execTeam && u.unitType.ToLower() == "villager" && u.currentHp > 0)
                {
                    var v = u.GetComponent<RTSVillager>();
                    if (v != null)
                    {
                        v.AssignBuildTask(building);
                        break;
                    }
                }
            }

            Debug.Log($"[RTSGameManager] ({execTeam}) AI Onaylı İnşaat Başlatıldı: {bType} @ {targetZone}");
        }

        private void ExecuteResearchAction(string techId)
        {
            if (RTSTechManager.Instance != null)
            {
                RTSTechManager.Instance.TryResearchTech(techId);
            }
        }

        private Vector3 GetTeamUnitSpawnOrigin(string unitType, UnitTeam execTeam)
        {
            string uLower = string.IsNullOrEmpty(unitType) ? "spearman" : unitType.ToLower();
            RTSBuilding targetBld = null;

            foreach (var b in RTSBuilding.AllBuildings)
            {
                if (b == null || b.team != execTeam || b.currentHp <= 0) continue;
                if (uLower == "villager" && b.buildingType == RTSBuildingType.TownCenter)
                {
                    targetBld = b;
                    break;
                }
                else if (uLower != "villager" && (b.buildingType == RTSBuildingType.Barracks || b.buildingType == RTSBuildingType.ArcheryRange || b.buildingType == RTSBuildingType.Castle))
                {
                    targetBld = b;
                    break;
                }
            }

            if (targetBld != null)
            {
                Vector3 fwd = (Vector3.zero - targetBld.transform.position);
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.01f) fwd = (execTeam == UnitTeam.Enemy) ? Vector3.back : Vector3.forward;
                else fwd.Normalize();
                return targetBld.transform.position + fwd * 5.2f + Vector3.up * 0.1f;
            }

            Vector3 basePos = (execTeam == UnitTeam.Enemy) ? RTSAutoBootstrap.CurrentEnemyBasePos : RTSAutoBootstrap.CurrentPlayerBasePos;
            Vector3 dirToCenter = (Vector3.zero - basePos).normalized;
            return basePos + dirToCenter * 6.5f + Vector3.up * 0.1f;
        }

        private void TrainUnits(string unitType, int count, string targetZone, UnitTeam execTeam)
        {
            int currentPop = CalculateCurrentPopulation(execTeam);
            int maxPop = CalculateMaxPopulation(execTeam);

            if (currentPop >= maxPop)
            {
                if (execTeam == LocalPlayerTeam)
                {
                    hudController?.SetAiStatus($"[UYARI] Nüfus dolu ({currentPop}/{maxPop})! Yeni birlik üretilemedi. Ev (House) inşa edin.", Color.red);
                }
                return;
            }

            int allowedCount = Mathf.Min(count, maxPop - currentPop);
            if (allowedCount < count && execTeam == LocalPlayerTeam && hudController != null)
            {
                hudController.SetAiStatus($"[UYARI] Nüfus sınırı ({maxPop}) nedeniyle {count} yerine {allowedCount} birlik üretildi.", Color.yellow);
            }
            count = allowedCount;
            if (count <= 0) return;

            if (execTeam == LocalPlayerTeam)
            {
                var (costWood, costFood, costGold) = GetUnitCost(unitType);
                wood -= costWood * count;
                food -= costFood * count;
                gold -= costGold * count;
            }

            Vector3 spawnOrigin = GetTeamUnitSpawnOrigin(unitType, execTeam);

            for (int i = 0; i < count; i++)
            {
                Vector3 spawnPos = spawnOrigin + new Vector3(Random.Range(-1.5f, 1.5f), 0, Random.Range(-1.5f, 1.5f));
                var unitComp = RTSUnit.SpawnUnit(unitType, spawnPos, execTeam);
                if (execTeam == LocalPlayerTeam)
                {
                    RTSNetworkSyncManager.Instance?.BroadcastUnitSpawn(unitType, spawnPos, execTeam);
                }

                if (unitComp != null && !string.IsNullOrEmpty(targetZone))
                {
                    Vector3 targetPos = RTSZoneMarker.GetZonePosition(targetZone, unitComp.transform.position, execTeam);
                    unitComp.MoveTo(targetPos);
                }
            }

            Debug.Log($"[RTSGameManager] ({execTeam}) {count}x {unitType} üretildi ve {targetZone} bölgesine intikal emri verildi!");
        }

        private void MoveUnits(string unitType, int count, string targetZone, UnitTeam execTeam)
        {
            string zLower = string.IsNullOrEmpty(targetZone) ? "" : targetZone.ToLower().Trim();
            if (zLower == "unexplored" || zLower == "scout" || zLower == "explore" || zLower.Contains("gold"))
            {
                ExecuteScoutAction(unitType, count, targetZone, execTeam);
                return;
            }

            string targetKey = string.IsNullOrEmpty(unitType) ? "" : unitType.ToLower();
            int maxCount = (count > 0) ? count : int.MaxValue;
            int movedCount = 0;
            bool isHostileZone = zLower.Contains("gate") || zLower.Contains("enemy") || zLower.Contains("north");

            foreach (var u in RTSUnit.AllUnits)
            {
                if (u != null && u.team == execTeam && (string.IsNullOrEmpty(targetKey) || targetKey == "soldier" || targetKey == "army" || u.unitType.ToLower() == targetKey) && u.currentHp > 0)
                {
                    Vector3 targetPos = RTSZoneMarker.GetZonePosition(targetZone, u.transform.position, execTeam);
                    Vector3 spread = new Vector3((movedCount % 3 - 1) * 1.8f, 0, (movedCount / 3) * 1.8f);
                    u.MoveTo(targetPos + spread, isManual: !isHostileZone);
                    movedCount++;
                    if (movedCount >= maxCount) break;
                }
            }

            Debug.Log($"[RTSGameManager] ({execTeam}) {movedCount} adet {unitType} birimi {targetZone} hedefine intikal ettiriliyor.");
        }

        private void ExecuteAttackAction(string unitType, int count, string targetZone, UnitTeam execTeam)
        {
            string targetKey = string.IsNullOrEmpty(unitType) ? "" : unitType.ToLower();
            int maxCount = (count > 0) ? count : int.MaxValue;
            int assignedCount = 0;

            Vector3 zonePos = RTSZoneMarker.GetZonePosition(string.IsNullOrEmpty(targetZone) ? "north_gate" : targetZone, Vector3.zero, execTeam);

            // Bölgeye veya haritadaki en yakın rakip birimi tespit et
            RTSUnit targetEnemy = null;
            float bestDist = float.MaxValue;
            foreach (var u in RTSUnit.AllUnits)
            {
                if (u != null && u.team != execTeam && u.currentHp > 0)
                {
                    float dist = (zonePos != Vector3.zero) ? Vector3.Distance(zonePos, u.transform.position) : 0f;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        targetEnemy = u;
                    }
                }
            }

            // Rakip birliği yoksa ve hedef bölgede rakip binası varsa onu hedef al
            RTSBuilding targetBuilding = null;
            if (targetEnemy == null && zonePos != Vector3.zero)
            {
                float bestBldDist = 18f;
                foreach (var b in RTSBuilding.AllBuildings)
                {
                    if (b != null && b.team != execTeam && b.currentHp > 0)
                    {
                        float dist = Vector3.Distance(zonePos, b.transform.position);
                        if (dist < bestBldDist)
                        {
                            bestBldDist = dist;
                            targetBuilding = b;
                        }
                    }
                }
            }

            foreach (var u in RTSUnit.AllUnits)
            {
                if (u != null && u.team == execTeam && u.currentHp > 0 && u.unitType.ToLower() != "villager")
                {
                    if (string.IsNullOrEmpty(targetKey) || targetKey == "soldier" || targetKey == "army" || u.unitType.ToLower() == targetKey)
                    {
                        if (targetEnemy != null)
                        {
                            u.AttackTarget(targetEnemy);
                        }
                        else if (targetBuilding != null && (zonePos != Vector3.zero || Vector3.Distance(u.transform.position, targetBuilding.transform.position) <= 22f))
                        {
                            u.AttackBuilding(targetBuilding);
                        }
                        else if (zonePos != Vector3.zero)
                        {
                            u.MoveTo(zonePos, isManual: false);
                        }

                        assignedCount++;
                        if (assignedCount >= maxCount) break;
                    }
                }
            }

            RTSAudioManager.Instance?.PlaySound(RTSSoundType.WarHorn);
            RTSAudioManager.Instance?.PlaySound(RTSSoundType.AttackOrder);
            RTSAudioManager.Instance?.NotifyCombat();

            if (execTeam == LocalPlayerTeam)
            {
                string attackVoice = "spearman_attack";
                if (!string.IsNullOrEmpty(targetKey))
                {
                    if (targetKey == "cavalry" || targetKey == "scout") attackVoice = "cavalry_attack";
                    else if (targetKey == "archer") attackVoice = "archer_attack";
                    else if (targetKey == "trebuchet") attackVoice = "trebuchet_attack";
                }
                else
                {
                    bool hasCav = RTSUnit.AllUnits.Exists(u => u != null && u.team == execTeam && (u.unitType == "cavalry" || u.unitType == "scout") && u.currentHp > 0);
                    if (hasCav) attackVoice = "cavalry_attack";
                }
                RTSAudioManager.Instance?.PlayVoice(attackVoice, force: true);

                if (RTSHUDController.Instance != null)
                {
                    RTSHUDController.Instance.SetAiStatus($" [TAARRUZ BAŞLADI] {assignedCount} muharip askerimiz düşmana hücum ediyor!", Color.red);
                }
            }
            Debug.Log($"[RTSGameManager] ({execTeam}) {assignedCount} asker {targetZone} bölgesine taarruz için harekete geçirildi.");
        }

        private void ExecuteScoutAction(string unitType, int count, string targetZone, UnitTeam execTeam)
        {
            int requested = (count > 0) ? count : 1;
            int assigned = 0;
            string zLower = string.IsNullOrEmpty(targetZone) ? "unexplored" : targetZone.ToLower().Trim();
            string focus = zLower.Contains("gold") ? "gold" : "";

            string preferredType = string.IsNullOrEmpty(unitType) ? "" : unitType.ToLower();

            if (!string.IsNullOrEmpty(preferredType) && preferredType != "scout" && preferredType != "soldier" && preferredType != "any")
            {
                foreach (var u in RTSUnit.AllUnits)
                {
                    if (u != null && u.team == execTeam && u.unitType.ToLower() == preferredType && u.currentHp > 0 && !u.isAutoScouting)
                    {
                        u.StartAutoScout(focus);
                        assigned++;
                        if (assigned >= requested) break;
                    }
                }
            }

            if (assigned < requested)
            {
                foreach (var u in RTSUnit.AllUnits)
                {
                    if (u != null && u.team == execTeam && u.unitType.ToLower() != "villager" && u.currentHp > 0 && !u.isAutoScouting)
                    {
                        u.StartAutoScout(focus);
                        assigned++;
                        if (assigned >= requested) break;
                    }
                }
            }

            if (execTeam == LocalPlayerTeam)
            {
                if (assigned > 0)
                {
                    string msg = (focus == "gold") ? "altın kaynaklarını" : "sisli bölgeleri";
                    hudController?.SetAiStatus($"[GÖZCÜ EMRİ] {assigned} asker {msg} keşfetmek üzere haritaya açıldı!", Color.cyan);
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.MoveOrder);
                }
                else
                {
                    hudController?.SetAiStatus("[UYARI] Keşfe göndermek için canlı askeri birliğiniz bulunmuyor!", Color.yellow);
                }
            }
        }

        public bool TrainSingleUnit(string unitType, Vector3 spawnOrigin)
        {
            int currentPop = CalculateCurrentPopulation();
            int maxPop = CalculateMaxPopulation();

            if (currentPop >= maxPop)
            {
                if (hudController != null)
                {
                    hudController.SetAiStatus($"⚠️ Nüfus sınırına ({maxPop}) ulaşıldı! Yeni birim basmak için Ev (House) inşa etmelisiniz.", Color.red);
                }
                RTSAudioManager.Instance?.PlaySound(RTSSoundType.AlarmHorn);
                return false;
            }

            var (costWood, costFood, costGold) = GetUnitCost(unitType);
            if (wood < costWood || food < costFood || gold < costGold)
            {
                if (hudController != null)
                {
                    hudController.SetAiStatus($"⚠️ Yetersiz kaynak! {unitType} için {costFood} Yiyecek, {costGold} Altın gerekir.", Color.yellow);
                }
                return false;
            }

            wood -= costWood;
            food -= costFood;
            gold -= costGold;
            UpdateHUD();

            Vector3 spawnPos = spawnOrigin + new Vector3(Random.Range(-1.0f, 1.0f), 0, Random.Range(-1.0f, 1.0f));
            var rtsUnit = RTSUnit.SpawnUnit(unitType, spawnPos, LocalPlayerTeam);
            RTSNetworkSyncManager.Instance?.BroadcastUnitSpawn(unitType, spawnPos, LocalPlayerTeam);

            // Canlı HUD ve nüfus sayımını yeni birim doğduktan sonra anında güncelle!
            UpdateHUD();

            if (hudController != null)
            {
                hudController.SetAiStatus($" {unitType.ToUpper()} başarıyla üretildi!", Color.green);
            }

            return true;
        }

        private (int wood, int food, int gold) GetUnitCost(string unitType)
        {
            switch (unitType.ToLower())
            {
                case "spearman": return (0, 60, 20);
                case "villager": return (0, 50, 0);
                case "archer": return (50, 0, 30);
                case "cavalry": return (0, 100, 80);
                case "swordsman": return (0, 60, 30);
                case "crossbowman": return (60, 0, 40);
                case "trebuchet": return (200, 0, 150);
                default: return (0, 50, 20);
            }
        }
    }

    public static class DictionaryExtensions
    {
        public static TV getOrDefault<TK, TV>(this Dictionary<TK, TV> dict, TK key, TV defaultVal)
        {
            return dict.TryGetValue(key, out var val) ? val : defaultVal;
        }
    }
}
