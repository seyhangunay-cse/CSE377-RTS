using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    [Serializable]
    public class RoomListingInfo
    {
        public string roomName;
        public string hostName;
        public string hostInstanceId;
        public string guestName;
        public string guestInstanceId;
        public string mapType;
        public int playerCount;
        public long lastUpdatedTicks;
        public bool matchStarted;
    }

    [Serializable]
    public class RoomRegistryData
    {
        public List<RoomListingInfo> rooms = new List<RoomListingInfo>();
    }

    /// <summary>
    /// Photon PUN 2 Oda Sistemi (Room Lobby), Açık Odalar Tarayıcısı (Room Browser),
    /// 2 Kişilik Çok Oyunculu Eşitleme (Multiplayer RPC Sync) ve Tek Oyunculu (PvE) Karşıt AI Komutanı Yöneticisi.
    /// - Tek Oyunculu Modda (PvE): Oyuncu Mavi Takım + Mavi AI Komutanını yönetir; Kırmızı Takım otonom akınlar yapar.
    /// - Photon 2 Kişilik Oda Modunda (PvP):
    ///   * 1. Oyuncu (Host / Oda Kurucu): Mavi Krallık (UnitTeam.Player) + Mavi AI Komutanı (BLUE)
    ///   * 2. Oyuncu (Client / Katılımcı): Kırmızı Krallık (UnitTeam.Enemy) + Kırmızı AI Komutanı (RED)
    ///   * Odaya 2. oyuncu bağlanmadan (2/2 olmadan) çok oyunculu sefer başlatılamaz.
    /// </summary>
    public class RTSNetworkSyncManager : MonoBehaviour
    {
        public static RTSNetworkSyncManager Instance { get; private set; }

        [Header("Photon Oda & Oyuncu Kimliği Ayarları")]
        public string photonAppId = "7c859265-f091-4544-8c74-d8cd7710b387";
        public string clientInstanceId = "";
        public string localPlayerName = "Komutan";
        public string hostPlayerName = "";
        public string hostInstanceId = "";
        public string guestPlayerName = "";
        public string guestInstanceId = "";
        public string matchRoomId = "ODA-101";
        public bool isMultiplayerMode = false;
        public bool isRoomConnected = false;
        public bool isHost = true;
        public UnitTeam assignedTeam = UnitTeam.Player;
        public int connectedPlayersCount = 0;
        public int pingMs = 15;
        public string roomStatusMessage = "🟢 Tek Oyunculu (PvE) — Mavi Krallık + Mavi AI Komutanı";

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
        private float nextRegistryPollTime = 0f;
        private float lastRemoteHeartbeatTime = -999f;
        private readonly ConcurrentQueue<string> incomingPackets = new ConcurrentQueue<string>();
        private readonly List<RoomListingInfo> activeOpenRooms = new List<RoomListingInfo>();

        public IReadOnlyList<RoomListingInfo> ActiveOpenRooms => activeOpenRooms;

        private string RegistryFilePath => Path.Combine(Path.GetTempPath(), "CSE377_PhotonLobby_Rooms.json");

        [Serializable]
        private class NetworkPacket
        {
            public string appId;
            public string room;
            public string senderName;
            public string senderInstanceId;
            public string type;     // HELLO, PLAN, SPAWN_UNIT, SPAWN_BLD, MOVE_ORDER, ATTACK_ORDER, VILLAGER_ORDER, MAP_SYNC, MATCH_START
            public string team;     // Player / Enemy
            public string payload;  // JSON veya parametre dizgesi
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                clientInstanceId = Guid.NewGuid().ToString("N").Substring(0, 8);
                localPlayerName = $"Komutan_{UnityEngine.Random.Range(100, 999)}";
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        private void Start()
        {
            nextRivalActionTime = Time.time + 15f;
            RefreshOpenRoomsRegistry();
            RefreshStatusMessage();
        }

        private void OnDestroy()
        {
            RemoveOwnRoomFromRegistry();
            CloseUdpSocket();
        }

        private void Update()
        {
            // Gelen ağ paketlerini ana iş parçacığında (Main Thread) işle
            while (incomingPackets.TryDequeue(out string rawJson))
            {
                ProcessIncomingPacket(rawJson);
            }

            // Açık odalar listesini ve oda durumunu periyodik olarak yenile
            if (Time.unscaledTime >= nextRegistryPollTime)
            {
                nextRegistryPollTime = Time.unscaledTime + 1.2f;
                RefreshOpenRoomsRegistry();
            }

            if (isMultiplayerMode && isRoomConnected)
            {
                // Oda kalp atışı (Heartbeat) gönder
                if (Time.unscaledTime >= nextHeartbeatTime)
                {
                    nextHeartbeatTime = Time.unscaledTime + 1.5f;
                    SendRawPacket("HELLO", localPlayerName);
                }

                bool udpPeerAlive = (Time.unscaledTime - lastRemoteHeartbeatTime < 6.0f);
                bool registryPeerAlive = !string.IsNullOrEmpty(guestPlayerName)
                    && !string.IsNullOrEmpty(hostPlayerName)
                    && !string.IsNullOrEmpty(hostInstanceId)
                    && !string.IsNullOrEmpty(guestInstanceId)
                    && hostInstanceId != guestInstanceId;
                int expectedCount = (udpPeerAlive || registryPeerAlive) ? 2 : 1;

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

        public bool CanStartMultiplayerMatch()
        {
            return isMultiplayerMode && isRoomConnected && connectedPlayersCount >= 2;
        }

        public void SetPlayerName(string newName)
        {
            if (!string.IsNullOrWhiteSpace(newName))
            {
                localPlayerName = newName.Trim();
                if (isRoomConnected)
                {
                    if (isHost) hostPlayerName = localPlayerName;
                    else guestPlayerName = localPlayerName;
                    RefreshOpenRoomsRegistry();
                    RefreshStatusMessage();
                    OnRoomStateChanged?.Invoke();
                }
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
                CreatePhotonRoom(localPlayerName, matchRoomId);
            }
        }

        /// <summary>
        /// Açık olan odayı kapatır (Host ise) veya mevcut odadan ayrılır (Guest ise) ve Oda Listesi ekranına döner.
        /// </summary>
        public void LeaveCurrentRoom()
        {
            RemoveOwnRoomFromRegistry();
            CloseUdpSocket();
            isRoomConnected = false;
            isHost = true;
            assignedTeam = UnitTeam.Player;
            connectedPlayersCount = 0;
            hostPlayerName = "";
            hostInstanceId = "";
            guestPlayerName = "";
            guestInstanceId = "";
            lastRemoteHeartbeatTime = -999f;

            RefreshOpenRoomsRegistry();
            RefreshStatusMessage();
            OnRoomStateChanged?.Invoke();
        }

        /// <summary>
        /// Oyunu Tek Oyunculu (PvE) moda geçirir ve varsa açık odadan ayrılır.
        /// </summary>
        public void SetSinglePlayerMode()
        {
            RemoveOwnRoomFromRegistry();
            CloseUdpSocket();
            isMultiplayerMode = false;
            isRoomConnected = false;
            isHost = true;
            assignedTeam = UnitTeam.Player;
            connectedPlayersCount = 0;
            hostPlayerName = "";
            hostInstanceId = "";
            guestPlayerName = "";
            guestInstanceId = "";
            lastRemoteHeartbeatTime = -999f;
            enableRivalAi = true;

            RTSGameManager.LocalPlayerTeam = UnitTeam.Player;
            RTSGameManager.IsMultiplayerPvP = false;
            if (RTSGameManager.Instance != null)
            {
                RTSGameManager.Instance.enableEnemyRaids = true;
            }

            RefreshStatusMessage();
            OnRoomStateChanged?.Invoke();
            var hud = RTSHUDController.Instance;
            if (hud == null || hud.startScreenModal == null || !hud.startScreenModal.activeSelf)
            {
                RTSAutoBootstrap.Instance?.RefreshPerspectiveForLocalTeam();
            }
        }

        /// <summary>
        /// Photon Oda Sisteminde oyuncu takımını (1. Oyuncu Mavi Host <-> 2. Oyuncu Kırmızı Client) değiştirir.
        /// </summary>
        public void CycleAssignedTeam()
        {
            UnitTeam nextTeam = (assignedTeam == UnitTeam.Player) ? UnitTeam.Enemy : UnitTeam.Player;
            ConnectToPhotonRoom(matchRoomId, nextTeam);
        }

        /// <summary>
        /// Yeni bir Photon Odası açar (1. Oyuncu / Host / Mavi Krallık + Mavi AI Komutan).
        /// </summary>
        public void CreatePhotonRoom(string playerName, string roomCode)
        {
            if (isRoomConnected)
            {
                RemoveOwnRoomFromRegistry();
            }
            if (!string.IsNullOrWhiteSpace(playerName)) localPlayerName = playerName.Trim();
            hostPlayerName = localPlayerName;
            hostInstanceId = clientInstanceId;
            guestPlayerName = "";
            guestInstanceId = "";
            lastRemoteHeartbeatTime = -999f;
            ConnectToPhotonRoom(roomCode, UnitTeam.Player);
        }

        /// <summary>
        /// Açık bir Photon Odasına 2. Oyuncu olarak katılır (Client / Kırmızı Krallık + Kırmızı AI Komutan).
        /// Eğer oyuncu kendi açtığı odaya tıklarsa kendi odasında 2. oyuncu olarak kopyalanmaz (1/2 Host olarak kalır).
        /// </summary>
        public void JoinPhotonRoom(string playerName, string roomCode)
        {
            if (!string.IsNullOrWhiteSpace(playerName)) localPlayerName = playerName.Trim();
            string normalizedRoom = string.IsNullOrWhiteSpace(roomCode) ? "ODA-101" : roomCode.Trim().ToUpperInvariant();

            // Kendi açtığımız odaya aynı istemciden 2. oyuncu olarak girmeyi engelle!
            var existingRoom = activeOpenRooms.Find(r => r != null && string.Equals(r.roomName, normalizedRoom, StringComparison.OrdinalIgnoreCase));
            bool isOwnHostedRoom = (existingRoom != null && existingRoom.hostInstanceId == clientInstanceId)
                || (isRoomConnected && isHost && string.Equals(matchRoomId, normalizedRoom, StringComparison.OrdinalIgnoreCase));

            if (isOwnHostedRoom)
            {
                hostPlayerName = localPlayerName;
                hostInstanceId = clientInstanceId;
                ConnectToPhotonRoom(normalizedRoom, UnitTeam.Player);
                return;
            }

            if (isRoomConnected && !string.Equals(matchRoomId, normalizedRoom, StringComparison.OrdinalIgnoreCase))
            {
                RemoveOwnRoomFromRegistry();
            }

            if (existingRoom != null)
            {
                hostPlayerName = existingRoom.hostName;
                hostInstanceId = existingRoom.hostInstanceId;
            }

            guestPlayerName = localPlayerName;
            guestInstanceId = clientInstanceId;
            lastRemoteHeartbeatTime = -999f;
            ConnectToPhotonRoom(normalizedRoom, UnitTeam.Enemy);
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

            if (isHost)
            {
                hostPlayerName = localPlayerName;
                hostInstanceId = clientInstanceId;
                if (guestInstanceId == clientInstanceId)
                {
                    guestPlayerName = "";
                    guestInstanceId = "";
                }
            }
            else
            {
                guestPlayerName = localPlayerName;
                guestInstanceId = clientInstanceId;
            }

            bool hasDistinctBoth = !string.IsNullOrEmpty(hostPlayerName)
                && !string.IsNullOrEmpty(guestPlayerName)
                && !string.IsNullOrEmpty(hostInstanceId)
                && !string.IsNullOrEmpty(guestInstanceId)
                && hostInstanceId != guestInstanceId;
            connectedPlayersCount = hasDistinctBoth ? 2 : 1;

            RTSGameManager.LocalPlayerTeam = assignedTeam;
            RTSGameManager.IsMultiplayerPvP = true;
            if (RTSGameManager.Instance != null)
            {
                RTSGameManager.Instance.enableEnemyRaids = false; // 2. Oyuncu Kırmızı Takımı bizzat + kendi AI Komutanıyla yönetir
            }

#if PHOTON_UNITY_NETWORKING
            if (PhotonNetwork.PhotonServerSettings != null && PhotonNetwork.PhotonServerSettings.AppSettings != null)
            {
                PhotonNetwork.PhotonServerSettings.AppSettings.AppIdRealtime = photonAppId;
            }
            PhotonNetwork.NickName = localPlayerName;
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
            RefreshOpenRoomsRegistry();
            SendRawPacket("HELLO", localPlayerName);
            RefreshStatusMessage();
            OnRoomStateChanged?.Invoke();

            // Menü açık değilse harita görüşünü ve kamera konumunu seçilen oyuncunun üssüne göre güncelle (menüdeyken arkaplan oynamaz)
            var hud = RTSHUDController.Instance;
            if (hud == null || hud.startScreenModal == null || !hud.startScreenModal.activeSelf)
            {
                RTSAutoBootstrap.Instance?.RefreshPerspectiveForLocalTeam();
            }

            string teamTitle = (assignedTeam == UnitTeam.Player)
                ? $"1. OYUNCU ({localPlayerName} - MAVİ KRALLIK + MAVİ AI)"
                : $"2. OYUNCU ({localPlayerName} - KIRMIZI KRALLIK + KIRMIZI AI)";
            Debug.Log($"[PHOTON ODA SİSTEMİ] AppID ({photonAppId}) ile '{matchRoomId}' odasına bağlanıldı! Rol: {teamTitle}");
        }

        public void RefreshOpenRoomsRegistry()
        {
            try
            {
                RoomRegistryData reg = new RoomRegistryData();
                if (File.Exists(RegistryFilePath))
                {
                    string raw = File.ReadAllText(RegistryFilePath, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        reg = JsonUtility.FromJson<RoomRegistryData>(raw) ?? new RoomRegistryData();
                    }
                }

                long nowTicks = DateTime.UtcNow.Ticks;
                long expireWindowTicks = TimeSpan.FromSeconds(7.5).Ticks;
                reg.rooms.RemoveAll(r => r == null || string.IsNullOrEmpty(r.roomName) || string.IsNullOrEmpty(r.hostInstanceId) || (nowTicks - r.lastUpdatedTicks) > expireWindowTicks);

                if (isMultiplayerMode && isRoomConnected)
                {
                    RoomListingInfo myRoom = reg.rooms.Find(r => string.Equals(r.roomName, matchRoomId, StringComparison.OrdinalIgnoreCase));
                    if (myRoom == null)
                    {
                        myRoom = new RoomListingInfo
                        {
                            roomName = matchRoomId,
                            hostName = isHost ? localPlayerName : (string.IsNullOrEmpty(hostPlayerName) ? "Kurucu" : hostPlayerName),
                            hostInstanceId = isHost ? clientInstanceId : hostInstanceId,
                            guestName = !isHost ? localPlayerName : "",
                            guestInstanceId = !isHost ? clientInstanceId : "",
                            mapType = RTSAutoBootstrap.SelectedMap.ToString(),
                            playerCount = 1,
                            lastUpdatedTicks = nowTicks,
                            matchStarted = false
                        };
                        if (isHost)
                        {
                            reg.rooms.Add(myRoom);
                        }
                    }
                    else
                    {
                        myRoom.lastUpdatedTicks = nowTicks;
                        if (isHost)
                        {
                            myRoom.hostName = localPlayerName;
                            myRoom.hostInstanceId = clientInstanceId;
                            myRoom.mapType = RTSAutoBootstrap.SelectedMap.ToString();

                            if (myRoom.guestInstanceId == clientInstanceId)
                            {
                                myRoom.guestName = "";
                                myRoom.guestInstanceId = "";
                            }

                            if (!string.IsNullOrEmpty(myRoom.guestName) && !string.IsNullOrEmpty(myRoom.guestInstanceId) && myRoom.guestInstanceId != clientInstanceId)
                            {
                                guestPlayerName = myRoom.guestName;
                                guestInstanceId = myRoom.guestInstanceId;
                            }
                            else
                            {
                                guestPlayerName = "";
                                guestInstanceId = "";
                            }
                        }
                        else
                        {
                            myRoom.guestName = localPlayerName;
                            myRoom.guestInstanceId = clientInstanceId;
                            if (!string.IsNullOrEmpty(myRoom.hostName))
                            {
                                hostPlayerName = myRoom.hostName;
                                hostInstanceId = myRoom.hostInstanceId;
                            }
                            if (!string.IsNullOrEmpty(myRoom.mapType) && Enum.TryParse(myRoom.mapType, out RTSMapType hostMap) && RTSAutoBootstrap.SelectedMap != hostMap)
                            {
                                RTSAutoBootstrap.SelectedMap = hostMap;
                                RTSHUDController.Instance?.UpdateMapSelectionUI();
                            }
                        }

                        bool hasValidGuest = !string.IsNullOrEmpty(myRoom.guestName)
                            && !string.IsNullOrEmpty(myRoom.guestInstanceId)
                            && myRoom.guestInstanceId != myRoom.hostInstanceId;
                        myRoom.playerCount = (!string.IsNullOrEmpty(myRoom.hostName) && hasValidGuest) ? 2 : 1;
                    }

                    int newCount = myRoom.playerCount;
                    if (connectedPlayersCount != newCount)
                    {
                        connectedPlayersCount = newCount;
                        RefreshStatusMessage();
                    }

                    // Eğer kurucu maçı başlattıysa (matchStarted == true), odaya bağlı 2. oyuncuda da maçı otomatik başlat!
                    if (!isHost && myRoom.matchStarted)
                    {
                        var hud = RTSHUDController.Instance;
                        if (hud != null && ((hud.startScreenModal != null && hud.startScreenModal.activeSelf)
                            || (hud.multiplayerLobbyModal != null && hud.multiplayerLobbyModal.activeSelf)
                            || (hud.activeRoomModal != null && hud.activeRoomModal.activeSelf)))
                        {
                            hud.LaunchMatchFromMenu(fromNetworkRpc: true);
                        }
                    }
                }

                File.WriteAllText(RegistryFilePath, JsonUtility.ToJson(reg), Encoding.UTF8);

                activeOpenRooms.Clear();
                activeOpenRooms.AddRange(reg.rooms);
                RefreshStatusMessage();
                OnRoomStateChanged?.Invoke();
            }
            catch
            {
                // Dosya erişim çakışmasında bir sonraki döngüde devam et
            }
        }

        private void RemoveOwnRoomFromRegistry()
        {
            if (!isRoomConnected) return;
            try
            {
                if (!File.Exists(RegistryFilePath)) return;
                string raw = File.ReadAllText(RegistryFilePath, Encoding.UTF8);
                var reg = JsonUtility.FromJson<RoomRegistryData>(raw);
                if (reg == null || reg.rooms == null) return;

                if (isHost)
                {
                    reg.rooms.RemoveAll(r => r != null && string.Equals(r.roomName, matchRoomId, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    var r = reg.rooms.Find(x => x != null && string.Equals(x.roomName, matchRoomId, StringComparison.OrdinalIgnoreCase));
                    if (r != null && r.guestInstanceId == clientInstanceId)
                    {
                        r.guestName = "";
                        r.guestInstanceId = "";
                        r.playerCount = 1;
                    }
                }
                File.WriteAllText(RegistryFilePath, JsonUtility.ToJson(reg), Encoding.UTF8);
            }
            catch { }
        }

        private void RefreshStatusMessage()
        {
            if (!isMultiplayerMode || !isRoomConnected)
            {
                roomStatusMessage = "ℹ️ Odaya girmek için yeni bir oda açın veya aşağıdaki açık odalardan birine katılın.";
                return;
            }

            string p1Name = !string.IsNullOrEmpty(hostPlayerName) ? hostPlayerName : (isHost ? localPlayerName : "Bekleniyor...");
            string p2Name = (connectedPlayersCount >= 2 && !string.IsNullOrEmpty(guestPlayerName)) ? guestPlayerName : "Bekleniyor...";

            string readyStr = (connectedPlayersCount >= 2)
                ? "✅ 2/2 OYUNCU HAZIR (Oyun Başlatılabilir!)"
                : "⏳ 1/2 OYUNCU (Rakip Oyuncu Bağlanmadan Başlatılamaz)";

            roomStatusMessage = $"🏰 ODA: [{matchRoomId}]  •  🔵 Mavi: {p1Name}  ⚔️  🔴 Kırmızı: {p2Name}  •  {readyStr}";
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
                Debug.LogWarning($"[PHOTON ODA SOKETİ] Port {localPort} dinlenemedi: {ex.Message}");
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
                    appId = photonAppId,
                    room = matchRoomId,
                    senderName = localPlayerName,
                    senderInstanceId = clientInstanceId,
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
                if (!string.IsNullOrEmpty(pkt.senderInstanceId) && pkt.senderInstanceId == clientInstanceId) return;

                lastRemoteHeartbeatTime = Time.unscaledTime;
                UnitTeam senderTeam = (pkt.team == "Enemy") ? UnitTeam.Enemy : UnitTeam.Player;
                if (!string.IsNullOrEmpty(pkt.senderName))
                {
                    if (senderTeam == UnitTeam.Player)
                    {
                        hostPlayerName = pkt.senderName;
                        if (!string.IsNullOrEmpty(pkt.senderInstanceId)) hostInstanceId = pkt.senderInstanceId;
                    }
                    else
                    {
                        guestPlayerName = pkt.senderName;
                        if (!string.IsNullOrEmpty(pkt.senderInstanceId)) guestInstanceId = pkt.senderInstanceId;
                    }
                }

                if (pkt.type == "HELLO")
                {
                    if (connectedPlayersCount < 2)
                    {
                        connectedPlayersCount = 2;
                    }
                    RefreshStatusMessage();
                    OnRoomStateChanged?.Invoke();
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
                else if (pkt.type == "MOVE_ORDER" && !string.IsNullOrEmpty(pkt.payload))
                {
                    // Format: uType,fromX,fromZ,toX,toZ;uType,fromX,fromZ,toX,toZ...
                    string[] entries = pkt.payload.Split(';');
                    var usedUnits = new HashSet<RTSUnit>();
                    foreach (string entry in entries)
                    {
                        if (string.IsNullOrWhiteSpace(entry)) continue;
                        string[] f = entry.Split(',');
                        if (f.Length >= 5)
                        {
                            string uType = f[0].ToLowerInvariant();
                            float fromX = float.Parse(f[1], CultureInfo.InvariantCulture);
                            float fromZ = float.Parse(f[2], CultureInfo.InvariantCulture);
                            float toX = float.Parse(f[3], CultureInfo.InvariantCulture);
                            float toZ = float.Parse(f[4], CultureInfo.InvariantCulture);

                            RTSUnit bestMatch = FindClosestTeamUnit(senderTeam, uType, new Vector3(fromX, 0f, fromZ), usedUnits);
                            if (bestMatch != null)
                            {
                                usedUnits.Add(bestMatch);
                                var vil = bestMatch.GetComponent<RTSVillager>();
                                if (vil != null) vil.CancelTask();
                                bestMatch.MoveTo(new Vector3(toX, 0f, toZ), isManual: true);
                            }
                        }
                    }
                }
                else if (pkt.type == "ATTACK_ORDER" && !string.IsNullOrEmpty(pkt.payload))
                {
                    // Format: isBld|targetX|targetZ|uType,fromX,fromZ;uType,fromX,fromZ...
                    string[] mainParts = pkt.payload.Split('|');
                    if (mainParts.Length >= 4)
                    {
                        bool isBld = mainParts[0] == "1";
                        float tx = float.Parse(mainParts[1], CultureInfo.InvariantCulture);
                        float tz = float.Parse(mainParts[2], CultureInfo.InvariantCulture);
                        Vector3 targetPos = new Vector3(tx, 0f, tz);

                        RTSBuilding targetBld = null;
                        RTSUnit targetUnit = null;
                        if (isBld)
                        {
                            float minD = 12f;
                            foreach (var b in RTSBuilding.AllBuildings)
                            {
                                if (b != null && b.team != senderTeam && b.currentHp > 0)
                                {
                                    float d = Vector3.Distance(new Vector3(b.transform.position.x, 0f, b.transform.position.z), targetPos);
                                    if (d < minD) { minD = d; targetBld = b; }
                                }
                            }
                        }
                        else
                        {
                            float minD = 12f;
                            foreach (var u in RTSUnit.AllUnits)
                            {
                                if (u != null && u.team != senderTeam && u.currentHp > 0)
                                {
                                    float d = Vector3.Distance(new Vector3(u.transform.position.x, 0f, u.transform.position.z), targetPos);
                                    if (d < minD) { minD = d; targetUnit = u; }
                                }
                            }
                        }

                        var usedUnits = new HashSet<RTSUnit>();
                        string[] unitEntries = mainParts[3].Split(';');
                        foreach (string entry in unitEntries)
                        {
                            if (string.IsNullOrWhiteSpace(entry)) continue;
                            string[] f = entry.Split(',');
                            if (f.Length >= 3)
                            {
                                string uType = f[0].ToLowerInvariant();
                                float fx = float.Parse(f[1], CultureInfo.InvariantCulture);
                                float fz = float.Parse(f[2], CultureInfo.InvariantCulture);
                                RTSUnit attacker = FindClosestTeamUnit(senderTeam, uType, new Vector3(fx, 0f, fz), usedUnits);
                                if (attacker != null)
                                {
                                    usedUnits.Add(attacker);
                                    if (isBld && targetBld != null) attacker.AttackBuilding(targetBld);
                                    else if (!isBld && targetUnit != null) attacker.AttackTarget(targetUnit);
                                    else attacker.MoveTo(targetPos, isManual: true);
                                }
                            }
                        }
                    }
                }
                else if (pkt.type == "VILLAGER_ORDER" && !string.IsNullOrEmpty(pkt.payload))
                {
                    // Format: kind|targetX|targetZ|fromX,fromZ;fromX,fromZ...
                    string[] mainParts = pkt.payload.Split('|');
                    if (mainParts.Length >= 4)
                    {
                        string kind = mainParts[0];
                        float tx = float.Parse(mainParts[1], CultureInfo.InvariantCulture);
                        float tz = float.Parse(mainParts[2], CultureInfo.InvariantCulture);
                        Vector3 targetPos = new Vector3(tx, 0f, tz);

                        RTSResourceSource targetRes = null;
                        RTSBuilding targetBld = null;
                        if (kind == "HARVEST")
                        {
                            float minD = 10f;
                            foreach (var r in RTSResourceSource.AllSources)
                            {
                                if (r != null && r.remainingAmount > 0)
                                {
                                    float d = Vector3.Distance(new Vector3(r.transform.position.x, 0f, r.transform.position.z), targetPos);
                                    if (d < minD) { minD = d; targetRes = r; }
                                }
                            }
                        }
                        else
                        {
                            float minD = 12f;
                            foreach (var b in RTSBuilding.AllBuildings)
                            {
                                if (b != null && b.team == senderTeam && b.currentHp > 0)
                                {
                                    float d = Vector3.Distance(new Vector3(b.transform.position.x, 0f, b.transform.position.z), targetPos);
                                    if (d < minD) { minD = d; targetBld = b; }
                                }
                            }
                        }

                        var usedUnits = new HashSet<RTSUnit>();
                        string[] entries = mainParts[3].Split(';');
                        foreach (string entry in entries)
                        {
                            if (string.IsNullOrWhiteSpace(entry)) continue;
                            string[] f = entry.Split(',');
                            if (f.Length >= 2)
                            {
                                float fx = float.Parse(f[0], CultureInfo.InvariantCulture);
                                float fz = float.Parse(f[1], CultureInfo.InvariantCulture);
                                RTSUnit vilUnit = FindClosestTeamUnit(senderTeam, "villager", new Vector3(fx, 0f, fz), usedUnits);
                                if (vilUnit != null)
                                {
                                    usedUnits.Add(vilUnit);
                                    var vil = vilUnit.GetComponent<RTSVillager>();
                                    if (vil != null)
                                    {
                                        if (kind == "HARVEST" && targetRes != null) vil.AssignHarvestTask(targetRes);
                                        else if (kind == "BUILD" && targetBld != null) vil.AssignBuildTask(targetBld);
                                        else if (kind == "REPAIR" && targetBld != null) vil.AssignRepairTask(targetBld);
                                    }
                                }
                            }
                        }
                    }
                }
                else if (pkt.type == "MAP_SYNC" && !string.IsNullOrEmpty(pkt.payload))
                {
                    if (Enum.TryParse(pkt.payload, out RTSMapType syncedMap) && RTSAutoBootstrap.SelectedMap != syncedMap)
                    {
                        RTSAutoBootstrap.SelectedMap = syncedMap;
                        RTSHUDController.Instance?.UpdateMapSelectionUI();
                        var hud = RTSHUDController.Instance;
                        if (hud == null || hud.startScreenModal == null || !hud.startScreenModal.activeSelf)
                        {
                            RTSAutoBootstrap.Instance?.RebuildMapForSelectedType();
                        }
                    }
                }
                else if (pkt.type == "MATCH_START")
                {
                    var hud = RTSHUDController.Instance;
                    if (hud != null && ((hud.startScreenModal != null && hud.startScreenModal.activeSelf) || (hud.multiplayerLobbyModal != null && hud.multiplayerLobbyModal.activeSelf)))
                    {
                        hud.LaunchMatchFromMenu(fromNetworkRpc: true);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PHOTON RPC] Paket işleme uyarısı: {ex.Message}");
            }
        }

        private RTSUnit FindClosestTeamUnit(UnitTeam team, string unitType, Vector3 origin, HashSet<RTSUnit> exclude)
        {
            RTSUnit best = null;
            float bestDist = float.MaxValue;
            foreach (var u in RTSUnit.AllUnits)
            {
                if (u == null || u.currentHp <= 0 || u.team != team) continue;
                if (exclude != null && exclude.Contains(u)) continue;
                if (!string.IsNullOrEmpty(unitType) && u.unitType.ToLowerInvariant() != unitType) continue;

                float d = Vector3.Distance(new Vector3(u.transform.position.x, 0f, u.transform.position.z), origin);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = u;
                }
            }
            return best;
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
        /// Manuel intikal (Move) emirlerini birim bazlı koordinatlarla Photon odasındaki diğer oyuncuya yayınlar.
        /// </summary>
        public void BroadcastMoveOrders(List<(RTSUnit unit, Vector3 dest)> unitMoves)
        {
            if (!isMultiplayerMode || !isRoomConnected || unitMoves == null || unitMoves.Count == 0) return;
            var sb = new StringBuilder();
            for (int i = 0; i < unitMoves.Count; i++)
            {
                var item = unitMoves[i];
                if (item.unit == null) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0},{1:F1},{2:F1},{3:F1},{4:F1}",
                    item.unit.unitType.ToLowerInvariant(),
                    item.unit.transform.position.x,
                    item.unit.transform.position.z,
                    item.dest.x,
                    item.dest.z);
            }
            if (sb.Length > 0) SendRawPacket("MOVE_ORDER", sb.ToString());
        }

        /// <summary>
        /// Manuel birim veya bina saldırı emirlerini Photon odasındaki diğer oyuncuya yayınlar.
        /// </summary>
        public void BroadcastAttackOrder(List<RTSUnit> attackers, Vector3 targetPos, bool isBuildingTarget)
        {
            if (!isMultiplayerMode || !isRoomConnected || attackers == null || attackers.Count == 0) return;
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0}|{1:F1}|{2:F1}|", isBuildingTarget ? 1 : 0, targetPos.x, targetPos.z);
            bool any = false;
            for (int i = 0; i < attackers.Count; i++)
            {
                var u = attackers[i];
                if (u == null) continue;
                if (any) sb.Append(';');
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0},{1:F1},{2:F1}",
                    u.unitType.ToLowerInvariant(),
                    u.transform.position.x,
                    u.transform.position.z);
                any = true;
            }
            if (any) SendRawPacket("ATTACK_ORDER", sb.ToString());
        }

        /// <summary>
        /// Manuel köylü kaynak toplama / inşaat / tamirat emirlerini Photon odasındaki diğer oyuncuya yayınlar.
        /// </summary>
        public void BroadcastVillagerOrder(List<RTSUnit> selectedUnits, Vector3 targetPos, string kind)
        {
            if (!isMultiplayerMode || !isRoomConnected || selectedUnits == null || selectedUnits.Count == 0) return;
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0}|{1:F1}|{2:F1}|", kind, targetPos.x, targetPos.z);
            bool any = false;
            for (int i = 0; i < selectedUnits.Count; i++)
            {
                var u = selectedUnits[i];
                if (u == null || u.GetComponent<RTSVillager>() == null) continue;
                if (any) sb.Append(';');
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F1},{1:F1}", u.transform.position.x, u.transform.position.z);
                any = true;
            }
            if (any) SendRawPacket("VILLAGER_ORDER", sb.ToString());
        }

        /// <summary>
        /// Lobi ekranında seçilen haritayı odadaki diğer oyuncuya senkronize eder.
        /// </summary>
        public void BroadcastMapSelection(RTSMapType mapType)
        {
            if (!isMultiplayerMode || !isRoomConnected) return;
            SendRawPacket("MAP_SYNC", mapType.ToString());
        }

        /// <summary>
        /// Sefer başlatıldığında odadaki diğer oyuncunun da maça girmesini sağlar.
        /// </summary>
        public void BroadcastMatchStart()
        {
            if (!isMultiplayerMode || !isRoomConnected) return;
            try
            {
                if (File.Exists(RegistryFilePath))
                {
                    string raw = File.ReadAllText(RegistryFilePath, Encoding.UTF8);
                    var reg = JsonUtility.FromJson<RoomRegistryData>(raw);
                    var r = reg?.rooms?.Find(x => x != null && string.Equals(x.roomName, matchRoomId, StringComparison.OrdinalIgnoreCase));
                    if (r != null)
                    {
                        r.matchStarted = true;
                        File.WriteAllText(RegistryFilePath, JsonUtility.ToJson(reg), Encoding.UTF8);
                    }
                }
            }
            catch { }
            SendRawPacket("MATCH_START", "1");
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
