using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using CSE377.AI;

#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
using Photon.Realtime;
#endif

namespace CSE377.Gameplay
{
    /// <summary>
    /// Photon PUN 2 Oda Sistemi (Room Lobby), 2 Kişilik Çok Oyunculu Eşitleme (Multiplayer RPC Sync)
    /// ve Tek Oyunculu (PvE) Karşıt AI Komutanı Yöneticisi.
    /// - Tek Oyunculu Modda (PvE): Oyuncu Mavi Takım + Mavi AI Komutanını yönetir; Kırmızı Takım otonom akınlar yapar.
    /// - Photon 2 Kişilik Oda Modunda (PvP):
    ///   * 1. Oyuncu (Host): Mavi Krallık (UnitTeam.Player) + Mavi AI Komutanı (BLUE)
    ///   * 2. Oyuncu (Client): Kırmızı Krallık (UnitTeam.Enemy) + Kırmızı AI Komutanı (RED)
    ///   * Onaylanan AI Komutan planları, birim üretimleri ve bina inşaatları Photon RPC / UDP Oda Soketi üzerinden senkronize edilir.
    /// </summary>
    public class RTSNetworkSyncManager : MonoBehaviour
    {
        public static RTSNetworkSyncManager Instance { get; private set; }

        [Header("Photon Oda & Eşitleme Ayarları")]
        public string matchRoomId = "ODA-101";
        public bool isMultiplayerMode = false;
        public bool isRoomConnected = false;
        public bool isHost = true;
        public UnitTeam assignedTeam = UnitTeam.Player;
        public int connectedPlayersCount = 1;
        public int pingMs = 15;
        public string roomStatusMessage = "🎮 MOD: TEK OYUNCULU (PvE) — Mavi Krallık + Mavi AI Komutanı";

        [Header("Otonom Düşman AI Komutanı (Yalnızca Tek Oyunculu PvE Modu)")]
        public bool enableRivalAi = true;
        public float rivalActionInterval = 25f;
        private float nextRivalActionTime = 0f;

        // Ağ Olayları
        public event Action<RTSAction> OnActionDispatched;
        public event Action OnRoomStateChanged;

        // Çift İstemci (Dual-Instance) Yerel/LAN Photon Oda Senkronizasyonu
        private UdpClient udpPeer;
        private int localPort = 47777;
        private int remotePort = 47778;
        private float nextHeartbeatTime = 0f;
        private float lastRemoteHeartbeatTime = -999f;
        private readonly ConcurrentQueue<string> incomingPackets = new ConcurrentQueue<string>();

        [Serializable]
        private class NetworkPacket
        {
            public string room;
            public string type;     // HELLO, PLAN, SPAWN_UNIT, SPAWN_BLD
            public string team;     // Player / Enemy
            public string payload;  // JSON veya parametre dizgesi
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        private void Start()
        {
            nextRivalActionTime = Time.time + 15f;
            RefreshStatusMessage();
        }

        private void OnDestroy()
        {
            CloseUdpSocket();
        }

        private void Update()
        {
            // Gelen ağ paketlerini ana iş parçacığında (Main Thread) işle
            while (incomingPackets.TryDequeue(out string rawJson))
            {
                ProcessIncomingPacket(rawJson);
            }

            if (isMultiplayerMode && isRoomConnected)
            {
                // Oda kalp atışı (Heartbeat) gönder
                if (Time.unscaledTime >= nextHeartbeatTime)
                {
                    nextHeartbeatTime = Time.unscaledTime + 2.0f;
                    SendRawPacket("HELLO", "");
                }

                int expectedCount = (Time.unscaledTime - lastRemoteHeartbeatTime < 6.5f) ? 2 : 1;
                if (connectedPlayersCount != expectedCount)
                {
                    connectedPlayersCount = expectedCount;
                    RefreshStatusMessage();
                    OnRoomStateChanged?.Invoke();
                }
            }
            else if (enableRivalAi && !isMultiplayerMode)
            {
                HandleRivalAiCommander();
            }
        }

        /// <summary>
        /// Tek Oyunculu (PvE) ve Photon 2 Kişilik Oda (PvP) modları arasında geçiş yapar.
        /// </summary>
        public void ToggleMultiplayerMode()
        {
            if (isMultiplayerMode)
            {
                SetSinglePlayerMode();
            }
            else
            {
                ConnectToPhotonRoom(matchRoomId, assignedTeam);
            }
        }

        /// <summary>
        /// Oyunu Tek Oyunculu (PvE) moda geçirir.
        /// </summary>
        public void SetSinglePlayerMode()
        {
            CloseUdpSocket();
            isMultiplayerMode = false;
            isRoomConnected = false;
            isHost = true;
            assignedTeam = UnitTeam.Player;
            connectedPlayersCount = 1;
            enableRivalAi = true;

            RTSGameManager.LocalPlayerTeam = UnitTeam.Player;
            RTSGameManager.IsMultiplayerPvP = false;
            if (RTSGameManager.Instance != null)
            {
                RTSGameManager.Instance.enableEnemyRaids = true;
            }

            RefreshStatusMessage();
            OnRoomStateChanged?.Invoke();
            RTSAutoBootstrap.Instance?.RefreshPerspectiveForLocalTeam();
        }

        /// <summary>
        /// Photon Oda Sisteminde oyuncu takımını (1. Oyuncu Mavi Host <-> 2. Oyuncu Kırmızı Client) değiştirir.
        /// </summary>
        public void CycleAssignedTeam()
        {
            UnitTeam nextTeam = (assignedTeam == UnitTeam.Player) ? UnitTeam.Enemy : UnitTeam.Player;
            assignedTeam = nextTeam;
            isHost = (assignedTeam == UnitTeam.Player);

            if (isMultiplayerMode)
            {
                ConnectToPhotonRoom(matchRoomId, assignedTeam);
            }
            else
            {
                // Takım seçildiğinde otomatik olarak Photon 2 Kişilik Oda modunu aktif et
                ConnectToPhotonRoom(matchRoomId, assignedTeam);
            }
        }

        /// <summary>
        /// Belirtilen oda kodu ve takımla (Mavi Host veya Kırmızı Client) Photon odasına bağlanır.
        /// </summary>
        public void ConnectToPhotonRoom(string roomCode, UnitTeam teamRole)
        {
            matchRoomId = string.IsNullOrWhiteSpace(roomCode) ? "ODA-101" : roomCode.Trim().ToUpperInvariant();
            assignedTeam = teamRole;
            isHost = (teamRole == UnitTeam.Player);
            isMultiplayerMode = true;
            isRoomConnected = true;
            enableRivalAi = false;

            RTSGameManager.LocalPlayerTeam = assignedTeam;
            RTSGameManager.IsMultiplayerPvP = true;
            if (RTSGameManager.Instance != null)
            {
                RTSGameManager.Instance.enableEnemyRaids = false; // 2. Oyuncu Kırmızı Takımı bizzat + kendi AI Komutanıyla yönetir
            }

#if PHOTON_UNITY_NETWORKING
            if (!PhotonNetwork.IsConnected)
            {
                PhotonNetwork.AutomaticallySyncScene = true;
                PhotonNetwork.ConnectUsingSettings();
            }
            else
            {
                RoomOptions opts = new RoomOptions { MaxPlayers = 2, IsVisible = true, IsOpen = true };
                PhotonNetwork.JoinOrCreateRoom(matchRoomId, opts, TypedLobby.Default);
            }
#endif

            StartUdpRoomSocket();
            SendRawPacket("HELLO", "");
            RefreshStatusMessage();
            OnRoomStateChanged?.Invoke();

            // Harita görüşünü ve kamera konumunu seçilen oyuncunun üssüne (Mavi Güney veya Kırmızı Kuzey) göre güncelle
            RTSAutoBootstrap.Instance?.RefreshPerspectiveForLocalTeam();

            string teamTitle = (assignedTeam == UnitTeam.Player)
                ? "1. OYUNCU (MAVİ KRALLIK + MAVİ AI KOMUTAN)"
                : "2. OYUNCU (KIRMIZI KRALLIK + KIRMIZI AI KOMUTAN)";
            Debug.Log($"[PHOTON ODA SİSTEMİ] '{matchRoomId}' odasına bağlanıldı! Rol: {teamTitle}");
        }

        private void RefreshStatusMessage()
        {
            if (!isMultiplayerMode)
            {
                roomStatusMessage = "🟢 Tek Oyunculu (PvE)  •  Mavi Krallık + Mavi AI Komutanı  •  Kırmızı Bot Akınları Aktif";
                return;
            }

            string roleStr = (assignedTeam == UnitTeam.Player)
                ? "🔵 Mavi Krallık (Host / 1. Oyuncu + Mavi AI)"
                : "🔴 Kırmızı Krallık (Client / 2. Oyuncu + Kırmızı AI)";

            string peerStr = (connectedPlayersCount >= 2)
                ? "2/2 Oyuncu Bağlı (Eşzamanlı Senkron)"
                : "1/2 Oyuncu (Diğer Oyuncu Bekleniyor / Hazır)";

            roomStatusMessage = $"🌐 PHOTON ODA: [{matchRoomId}]  •  {roleStr}  •  {peerStr}";
        }

        private void StartUdpRoomSocket()
        {
            CloseUdpSocket();
            try
            {
                localPort = (assignedTeam == UnitTeam.Player) ? 47777 : 47778;
                remotePort = (assignedTeam == UnitTeam.Player) ? 47778 : 47777;

                udpPeer = new UdpClient(localPort);
                udpPeer.BeginReceive(OnUdpDataReceived, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PHOTON ODA SOKETİ] Port {localPort} dinlenemedi (Tek PC'de diğer port kullanılıyor olabilir): {ex.Message}");
            }
        }

        private void CloseUdpSocket()
        {
            if (udpPeer != null)
            {
                try { udpPeer.Close(); } catch { }
                udpPeer = null;
            }
        }

        private void OnUdpDataReceived(IAsyncResult ar)
        {
            if (udpPeer == null) return;
            try
            {
                IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = udpPeer.EndReceive(ar, ref ep);
                if (data != null && data.Length > 0)
                {
                    string json = Encoding.UTF8.GetString(data);
                    incomingPackets.Enqueue(json);
                }
                udpPeer?.BeginReceive(OnUdpDataReceived, null);
            }
            catch
            {
                // Soket kapatıldığında sessizce çık
            }
        }

        private void SendRawPacket(string packetType, string payloadStr)
        {
            if (!isMultiplayerMode || !isRoomConnected || udpPeer == null) return;
            try
            {
                var pkt = new NetworkPacket
                {
                    room = matchRoomId,
                    type = packetType,
                    team = assignedTeam.ToString(),
                    payload = payloadStr
                };
                string json = JsonUtility.ToJson(pkt);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                udpPeer.Send(bytes, bytes.Length, "127.0.0.1", remotePort);
            }
            catch { }
        }

        private void ProcessIncomingPacket(string rawJson)
        {
            if (string.IsNullOrEmpty(rawJson)) return;
            try
            {
                var pkt = JsonUtility.FromJson<NetworkPacket>(rawJson);
                if (pkt == null || pkt.room != matchRoomId) return;

                lastRemoteHeartbeatTime = Time.unscaledTime;
                UnitTeam senderTeam = (pkt.team == "Enemy") ? UnitTeam.Enemy : UnitTeam.Player;

                if (pkt.type == "HELLO")
                {
                    if (connectedPlayersCount < 2)
                    {
                        connectedPlayersCount = 2;
                        RefreshStatusMessage();
                        OnRoomStateChanged?.Invoke();
                    }
                }
                else if (pkt.type == "PLAN" && !string.IsNullOrEmpty(pkt.payload))
                {
                    var plan = JsonUtility.FromJson<RTSActionPlan>(pkt.payload);
                    if (plan != null && RTSGameManager.Instance != null)
                    {
                        Debug.Log($"[PHOTON RPC] Karşı Oyuncunun ({senderTeam}) AI Komutan Planı Alındı: {plan.summary}");
                        RTSGameManager.Instance.ExecuteApprovedPlanForTeam(plan, senderTeam, broadcastOverNetwork: false);
                    }
                }
                else if (pkt.type == "SPAWN_UNIT" && !string.IsNullOrEmpty(pkt.payload))
                {
                    string[] parts = pkt.payload.Split('|');
                    if (parts.Length >= 4)
                    {
                        string uType = parts[0];
                        float x = float.Parse(parts[1], CultureInfo.InvariantCulture);
                        float y = float.Parse(parts[2], CultureInfo.InvariantCulture);
                        float z = float.Parse(parts[3], CultureInfo.InvariantCulture);
                        RTSUnit.SpawnUnit(uType, new Vector3(x, y, z), senderTeam);
                    }
                }
                else if (pkt.type == "SPAWN_BLD" && !string.IsNullOrEmpty(pkt.payload))
                {
                    string[] parts = pkt.payload.Split('|');
                    if (parts.Length >= 5 && Enum.TryParse(parts[0], out RTSBuildingType bType))
                    {
                        float x = float.Parse(parts[1], CultureInfo.InvariantCulture);
                        float y = float.Parse(parts[2], CultureInfo.InvariantCulture);
                        float z = float.Parse(parts[3], CultureInfo.InvariantCulture);
                        bool startBuilt = parts[4] == "1";
                        var bld = RTSBuilding.SpawnBuilding(bType, new Vector3(x, y, z), senderTeam, startBuilt);
                        if (!startBuilt && bld != null)
                        {
                            foreach (var u in RTSUnit.AllUnits)
                            {
                                if (u != null && u.team == senderTeam && u.unitType.ToLower() == "villager" && u.currentHp > 0)
                                {
                                    var v = u.GetComponent<RTSVillager>();
                                    if (v != null) { v.AssignBuildTask(bld); break; }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PHOTON RPC] Paket işleme uyarısı: {ex.Message}");
            }
        }

        /// <summary>
        /// Doğrulanmış eylem emrini ağ katmanına duyurur.
        /// </summary>
        public void DispatchActionRpc(RTSAction action)
        {
            if (action == null) return;
            Debug.Log($"[AĞ SENKRONİZASYONU] Eylem RPC Paketi İletildi ({assignedTeam}): {action.action_type} - {action.unit_type ?? action.building_type}");
            OnActionDispatched?.Invoke(action);
        }

        /// <summary>
        /// Onaylanan AI Komutanı planını Photon odasındaki diğer oyuncuya yayınlar.
        /// </summary>
        public void BroadcastApprovedPlan(UnitTeam team, RTSActionPlan plan)
        {
            if (!isMultiplayerMode || !isRoomConnected || plan == null) return;
            string planJson = JsonUtility.ToJson(plan);
            SendRawPacket("PLAN", planJson);
        }

        /// <summary>
        /// Manuel birim üretimini Photon odasındaki diğer oyuncuya yayınlar.
        /// </summary>
        public void BroadcastUnitSpawn(string unitType, Vector3 pos, UnitTeam team)
        {
            if (!isMultiplayerMode || !isRoomConnected) return;
            string payload = string.Format(CultureInfo.InvariantCulture, "{0}|{1:F2}|{2:F2}|{3:F2}", unitType, pos.x, pos.y, pos.z);
            SendRawPacket("SPAWN_UNIT", payload);
        }

        /// <summary>
        /// Manuel bina inşaatını Photon odasındaki diğer oyuncuya yayınlar.
        /// </summary>
        public void BroadcastBuildingSpawn(RTSBuildingType bType, Vector3 pos, UnitTeam team, bool startBuilt)
        {
            if (!isMultiplayerMode || !isRoomConnected) return;
            string payload = string.Format(CultureInfo.InvariantCulture, "{0}|{1:F2}|{2:F2}|{3:F2}|{4}", bType, pos.x, pos.y, pos.z, startBuilt ? 1 : 0);
            SendRawPacket("SPAWN_BLD", payload);
        }

        /// <summary>
        /// Tek Oyunculu (PvE) modda Kırmızı takımın otonom stratejik komutanı.
        /// </summary>
        private void HandleRivalAiCommander()
        {
            if (Time.time < nextRivalActionTime) return;
            nextRivalActionTime = Time.time + rivalActionInterval;

            if (RTSGameManager.Instance != null && RTSGameManager.Instance.isGameOver) return;

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

            if (enemyBase == null) return;

            int enemyCount = 0;
            foreach (var u in RTSUnit.AllUnits)
            {
                if (u != null && u.team == UnitTeam.Enemy && u.currentHp > 0)
                {
                    enemyCount++;
                }
            }

            Vector3 spawnOrigin = enemyBase.transform.position + new Vector3(0, 0, -3f);

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
                Vector3 playerBasePos = RTSAutoBootstrap.CurrentPlayerBasePos;
                foreach (var u in RTSUnit.AllUnits)
                {
                    if (u != null && u.team == UnitTeam.Enemy && u.currentHp > 0 && u.unitType.ToLower() != "villager")
                    {
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
