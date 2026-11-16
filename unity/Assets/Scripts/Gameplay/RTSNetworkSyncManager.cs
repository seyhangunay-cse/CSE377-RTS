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
        public int matchSeed = 133742;
        public int playerCount;
        public long lastUpdatedTicks;
        public bool matchStarted;
        public string winnerTeam = "";
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
        private float nextPacketLogPollTime = 0f;
        private float nextLiveWorldSyncTime = 0f;
        private float lastRemoteHeartbeatTime = -999f;
        private bool hasLaunchedCurrentRoomMatch = false;
        private long roomConnectUtcTicks = 0;
        private readonly ConcurrentQueue<string> incomingPackets = new ConcurrentQueue<string>();
        private readonly List<RoomListingInfo> activeOpenRooms = new List<RoomListingInfo>();
        private readonly HashSet<string> processedPacketIds = new HashSet<string>();

        public IReadOnlyList<RoomListingInfo> ActiveOpenRooms => activeOpenRooms;

        private string RegistryFilePath => Path.Combine(Path.GetTempPath(), "CSE377_PhotonLobby_Rooms.json");
        private string OutboundPacketFilePath => Path.Combine(Path.GetTempPath(), $"CSE377_PhotonRoom_{matchRoomId}_Out_{assignedTeam}.json");
        private string InboundPacketFilePath => Path.Combine(Path.GetTempPath(), $"CSE377_PhotonRoom_{matchRoomId}_Out_{(assignedTeam == UnitTeam.Player ? UnitTeam.Enemy : UnitTeam.Player)}.json");

        [Serializable]
        private class NetworkPacket
        {
            public string packetId;
            public long sentTicks;
            public string appId;
            public string room;
            public string senderName;
            public string senderInstanceId;
            public string type;     // HELLO, PLAN, SPAWN_UNIT, SPAWN_BLD, MOVE_ORDER, ATTACK_ORDER, VILLAGER_ORDER, MAP_SYNC, MATCH_START, TEAM_STATE_SYNC
            public string team;     // Player / Enemy
            public string payload;  // JSON veya parametre dizgesi
        }

        [Serializable]
        private class RoomPacketLogData
        {
            public List<NetworkPacket> packets = new List<NetworkPacket>();
        }

        private void Awake()
        {
            Application.runInBackground = true;
#if UNITY_EDITOR
            UnityEditor.PlayerSettings.runInBackground = true;
#endif
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
            Application.runInBackground = true;
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

            // Çift kanallı (Dual-Channel) paylaşımlı oda paket kuyruğunu her 0.12 saniyede bir kontrol et (UDP paket kaybını %100 önler)
            if (isMultiplayerMode && isRoomConnected && Time.unscaledTime >= nextPacketLogPollTime)
            {
                nextPacketLogPollTime = Time.unscaledTime + 0.12f;
                PollSharedRoomPackets();
            }

            // Açık odalar listesini ve oda durumunu periyodik olarak yenile
            if (Time.unscaledTime >= nextRegistryPollTime)
            {
                nextRegistryPollTime = Time.unscaledTime + 0.6f;
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

                // Maç başladıysa her iki sahnenin (yeni binalar, birim konumları, inşaat ilerlemeleri ve can değerleri) birebir eşit ilerlemesi için canlı durum senkronizasyonu gönder
                bool isInActiveMatch = hasLaunchedCurrentRoomMatch
                    || (RTSHUDController.Instance != null && RTSHUDController.Instance.startScreenModal != null && !RTSHUDController.Instance.startScreenModal.activeSelf);
                if (isInActiveMatch)
                {
                    hasLaunchedCurrentRoomMatch = true;
                    if (Time.unscaledTime >= nextLiveWorldSyncTime)
                    {
                        nextLiveWorldSyncTime = Time.unscaledTime + 0.20f;
                        BroadcastTeamLiveState();
                    }
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

                // Eğer tek editörde Sanal 2. Oyuncu (SIM_BOT_999) ile test yapılıyorsa Kırmızı AI Komutanı aktif çalışsın
                if (enableRivalAi && guestInstanceId == "SIM_BOT_999")
                {
                    HandleRivalAiCommander();
                }
            }
            else if (enableRivalAi && !isMultiplayerMode)
            {
                HandleRivalAiCommander();
            }
        }

        /// <summary>
        /// Tek bir Unity Editörü açıkken Build almadan 2 kişilik oda kilidini (1/2 -> 2/2) ve maç başlatmayı
        /// anında test edebilmek için odaya sanal bir 2. Oyuncu (Kırmızı Komutan + Kırmızı AI) bağlar.
        /// </summary>
        public void SimulateSecondPlayerForTest()
        {
            if (!isMultiplayerMode || !isRoomConnected)
            {
                CreatePhotonRoom(localPlayerName, matchRoomId);
            }

            guestPlayerName = "Kırmızı_Test_Komutanı";
            guestInstanceId = "SIM_BOT_999";
            connectedPlayersCount = 2;
            enableRivalAi = true;

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
                var myRoom = reg.rooms.Find(r => r != null && string.Equals(r.roomName, matchRoomId, StringComparison.OrdinalIgnoreCase));
                if (myRoom != null)
                {
                    myRoom.guestName = guestPlayerName;
                    myRoom.guestInstanceId = guestInstanceId;
                    myRoom.playerCount = 2;
                    myRoom.lastUpdatedTicks = DateTime.UtcNow.Ticks;
                    File.WriteAllText(RegistryFilePath, JsonUtility.ToJson(reg), Encoding.UTF8);
                }
            }
            catch { }

            RefreshOpenRoomsRegistry();
            RefreshStatusMessage();
            OnRoomStateChanged?.Invoke();
            Debug.Log($"[PHOTON TEST] '{matchRoomId}' odasına Sanal 2. Oyuncu ({guestPlayerName}) bağlandı! Oda 2/2 Hazır.");
        }

        /// <summary>
        /// Yalnızca odayı kuran oyuncu (isHost == true) ve odada 2/2 oyuncu hazır olduğunda oyunu başlatabilir!
        /// </summary>
        public bool CanStartMultiplayerMatch()
        {
            return isMultiplayerMode && isRoomConnected && isHost && connectedPlayersCount >= 2;
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
            hasLaunchedCurrentRoomMatch = false;
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
            hasLaunchedCurrentRoomMatch = false;
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
            hasLaunchedCurrentRoomMatch = false;
            enableRivalAi = false;
            roomConnectUtcTicks = DateTime.UtcNow.Ticks;
            processedPacketIds.Clear();

            try
            {
                if (File.Exists(OutboundPacketFilePath))
                {
                    File.Delete(OutboundPacketFilePath);
                }
            }
            catch { }

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
                            myRoom.matchSeed = RTSAutoBootstrap.MatchRandomSeed;

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
                            if (myRoom.matchSeed != 0)
                            {
                                RTSAutoBootstrap.MatchRandomSeed = myRoom.matchSeed;
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

                    // Eğer kurucu maçı başlattıysa (matchStarted == true), odaya bağlı 2. oyuncuda da maçı aynı harita ve tohumla otomatik başlat!
                    if (!isHost && myRoom.matchStarted && !hasLaunchedCurrentRoomMatch)
                    {
                        hasLaunchedCurrentRoomMatch = true;
                        if (myRoom.matchSeed != 0)
                        {
                            RTSAutoBootstrap.MatchRandomSeed = myRoom.matchSeed;
                        }
                        if (!string.IsNullOrEmpty(myRoom.mapType) && Enum.TryParse(myRoom.mapType, out RTSMapType startMap))
                        {
                            RTSAutoBootstrap.SelectedMap = startMap;
                        }
                        var hud = RTSHUDController.Instance;
                        if (hud != null)
                        {
                            hud.LaunchMatchFromMenu(fromNetworkRpc: true);
                        }
                    }

                    // Eğer odadaki oyunculardan biri maçı kazandıysa (winnerTeam doluysa), diğer oyuncuda da ZAFER/YENİLGİ ekranını anında aç!
                    if (hasLaunchedCurrentRoomMatch && !string.IsNullOrEmpty(myRoom.winnerTeam) && Enum.TryParse(myRoom.winnerTeam, out UnitTeam regWinner))
                    {
                        if (RTSGameManager.Instance != null && !RTSGameManager.Instance.isGameOver)
                        {
                            RTSGameManager.Instance.ForceGameOverFromNetwork(regWinner);
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
            if (!isMultiplayerMode || !isRoomConnected) return;
            var pkt = new NetworkPacket
            {
                packetId = Guid.NewGuid().ToString("N").Substring(0, 12),
                sentTicks = DateTime.UtcNow.Ticks,
                appId = photonAppId,
                room = matchRoomId,
                senderName = localPlayerName,
                senderInstanceId = clientInstanceId,
                type = packetType,
                team = assignedTeam.ToString(),
                payload = payloadStr
            };
            processedPacketIds.Add(pkt.packetId);

            // 1. Kanal: Düşük gecikmeli UDP Soketi
            if (udpPeer != null)
            {
                try
                {
                    string json = JsonUtility.ToJson(pkt);
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    udpPeer.Send(bytes, bytes.Length, "127.0.0.1", remotePort);
                }
                catch { }
            }

            // 2. Kanal: %100 Güvenilir Takım Bazlı Oda Paket Kuyruğu (Her takım kendi dosyasına yazar, karşı takımın dosyasını okur; çakışma ve paket kaybı %100 önlenir)
            if (packetType != "HELLO")
            {
                try
                {
                    string outPath = OutboundPacketFilePath;
                    RoomPacketLogData log = new RoomPacketLogData();
                    if (File.Exists(outPath))
                    {
                        string raw = File.ReadAllText(outPath, Encoding.UTF8);
                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            log = JsonUtility.FromJson<RoomPacketLogData>(raw) ?? new RoomPacketLogData();
                        }
                    }
                    long cutoffTicks = DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(10.0).Ticks;
                    log.packets.RemoveAll(p => p == null || p.sentTicks < cutoffTicks || (packetType == "TEAM_STATE_SYNC" && p.type == "TEAM_STATE_SYNC"));
                    log.packets.Add(pkt);
                    if (log.packets.Count > 50)
                    {
                        log.packets.RemoveRange(0, log.packets.Count - 50);
                    }
                    File.WriteAllText(outPath, JsonUtility.ToJson(log), Encoding.UTF8);
                }
                catch { }
            }
        }

        private void PollSharedRoomPackets()
        {
            try
            {
                string inPath = InboundPacketFilePath;
                if (!File.Exists(inPath)) return;
                string raw = File.ReadAllText(inPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(raw)) return;
                var log = JsonUtility.FromJson<RoomPacketLogData>(raw);
                if (log == null || log.packets == null) return;

                for (int i = 0; i < log.packets.Count; i++)
                {
                    var pkt = log.packets[i];
                    if (pkt == null || pkt.room != matchRoomId) continue;
                    if (pkt.sentTicks > 0 && pkt.sentTicks < roomConnectUtcTicks) continue;
                    if (!string.IsNullOrEmpty(pkt.senderInstanceId) && pkt.senderInstanceId == clientInstanceId) continue;
                    if (!string.IsNullOrEmpty(pkt.packetId) && processedPacketIds.Contains(pkt.packetId)) continue;
                    ProcessParsedPacket(pkt);
                }
            }
            catch { }
        }

        private void ProcessIncomingPacket(string rawJson)
        {
            if (string.IsNullOrEmpty(rawJson)) return;
            try
            {
                var pkt = JsonUtility.FromJson<NetworkPacket>(rawJson);
                if (pkt == null) return;
                ProcessParsedPacket(pkt);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PHOTON RPC] Paket işleme uyarısı: {ex.Message}");
            }
        }

        private void ProcessParsedPacket(NetworkPacket pkt)
        {
            if (pkt == null || pkt.room != matchRoomId) return;
            if (!string.IsNullOrEmpty(pkt.senderInstanceId) && pkt.senderInstanceId == clientInstanceId) return;
            if (!string.IsNullOrEmpty(pkt.packetId) && !processedPacketIds.Add(pkt.packetId)) return;

            try
            {
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
                        RTSMinimapController.Instance?.DrawMinimap();
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

                        bool alreadyExists = false;
                        foreach (var existing in RTSBuilding.AllBuildings)
                        {
                            if (existing != null && existing.currentHp > 0 && existing.team == senderTeam && existing.buildingType == bType)
                            {
                                if (Vector2.Distance(new Vector2(existing.transform.position.x, existing.transform.position.z), new Vector2(x, z)) < 3.0f)
                                {
                                    alreadyExists = true;
                                    break;
                                }
                            }
                        }

                        if (!alreadyExists)
                        {
                            var bld = RTSBuilding.SpawnBuilding(bType, new Vector3(x, y, z), senderTeam, startBuilt);
                            if (!startBuilt && bld != null)
                            {
                                AssignRemoteVillagerToBuild(senderTeam, bld);
                            }
                            RTSMinimapController.Instance?.DrawMinimap();
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
                else if (pkt.type == "TEAM_STATE_SYNC" && !string.IsNullOrEmpty(pkt.payload))
                {
                    ApplyRemoteTeamLiveState(senderTeam, pkt.payload);
                }
                else if (pkt.type == "BLD_HP_SYNC" && !string.IsNullOrEmpty(pkt.payload))
                {
                    // Format: targetTeam|bType|x|z|hp
                    string[] p = pkt.payload.Split('|');
                    if (p.Length >= 5 && Enum.TryParse(p[0], out UnitTeam bTeam) && Enum.TryParse(p[1], out RTSBuildingType bType))
                    {
                        float bx = float.Parse(p[2], CultureInfo.InvariantCulture);
                        float bz = float.Parse(p[3], CultureInfo.InvariantCulture);
                        int syncedHp = int.Parse(p[4], CultureInfo.InvariantCulture);

                        RTSBuilding targetBld = null;
                        float bestD = 8f;
                        foreach (var b in RTSBuilding.AllBuildings)
                        {
                            if (b == null || b.isDestroyed || b.team != bTeam || b.buildingType != bType) continue;
                            float d = Vector2.Distance(new Vector2(b.transform.position.x, b.transform.position.z), new Vector2(bx, bz));
                            if (d < bestD)
                            {
                                bestD = d;
                                targetBld = b;
                            }
                        }
                        if (targetBld != null)
                        {
                            targetBld.ApplyNetworkHp(syncedHp);
                        }
                    }
                }
                else if (pkt.type == "UNIT_HP_SYNC" && !string.IsNullOrEmpty(pkt.payload))
                {
                    // Format: targetTeam|uType|x|z|hp
                    string[] p = pkt.payload.Split('|');
                    if (p.Length >= 5 && Enum.TryParse(p[0], out UnitTeam uTeam))
                    {
                        string uType = p[1].ToLowerInvariant();
                        float ux = float.Parse(p[2], CultureInfo.InvariantCulture);
                        float uz = float.Parse(p[3], CultureInfo.InvariantCulture);
                        int syncedHp = int.Parse(p[4], CultureInfo.InvariantCulture);

                        RTSUnit targetUnit = FindClosestTeamUnit(uTeam, uType, new Vector3(ux, 0f, uz), null);
                        if (targetUnit != null && Vector2.Distance(new Vector2(targetUnit.transform.position.x, targetUnit.transform.position.z), new Vector2(ux, uz)) < 8f)
                        {
                            targetUnit.ApplyNetworkHp(syncedHp);
                        }
                    }
                }
                else if (pkt.type == "TECH_SYNC" && !string.IsNullOrEmpty(pkt.payload))
                {
                    RTSTechManager.Instance?.ApplyRemoteTech(pkt.payload, senderTeam);
                }
                else if (pkt.type == "GAME_OVER" && !string.IsNullOrEmpty(pkt.payload))
                {
                    if (Enum.TryParse(pkt.payload, out UnitTeam winnerTeam))
                    {
                        RTSGameManager.Instance?.ForceGameOverFromNetwork(winnerTeam);
                    }
                }
                else if (pkt.type == "MATCH_RESTART")
                {
                    if (!string.IsNullOrEmpty(pkt.payload))
                    {
                        string[] parts = pkt.payload.Split('|');
                        if (parts.Length >= 1 && Enum.TryParse(parts[0], out RTSMapType rMap))
                        {
                            RTSAutoBootstrap.SelectedMap = rMap;
                        }
                        if (parts.Length >= 2 && int.TryParse(parts[1], out int rSeed) && rSeed != 0)
                        {
                            RTSAutoBootstrap.MatchRandomSeed = rSeed;
                        }
                    }
                    hasLaunchedCurrentRoomMatch = true;
                    RTSHUDController.Instance?.ExecuteLocalMatchRestart();
                }
                else if (pkt.type == "MAP_SYNC" && !string.IsNullOrEmpty(pkt.payload))
                {
                    if (Enum.TryParse(pkt.payload, out RTSMapType syncedMap) && RTSAutoBootstrap.SelectedMap != syncedMap)
                    {
                        RTSAutoBootstrap.SelectedMap = syncedMap;
                        RTSHUDController.Instance?.UpdateMapSelectionUI();
                    }
                }
                else if (pkt.type == "MATCH_START")
                {
                    if (!string.IsNullOrEmpty(pkt.payload))
                    {
                        string[] parts = pkt.payload.Split('|');
                        if (parts.Length >= 1 && Enum.TryParse(parts[0], out RTSMapType startMap))
                        {
                            RTSAutoBootstrap.SelectedMap = startMap;
                        }
                        if (parts.Length >= 2 && int.TryParse(parts[1], out int seed) && seed != 0)
                        {
                            RTSAutoBootstrap.MatchRandomSeed = seed;
                        }
                    }
                    if (!hasLaunchedCurrentRoomMatch)
                    {
                        hasLaunchedCurrentRoomMatch = true;
                        RTSHUDController.Instance?.LaunchMatchFromMenu(fromNetworkRpc: true);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PHOTON RPC] Paket işleme uyarısı: {ex.Message}");
            }
        }

        private void BroadcastTeamLiveState()
        {
            if (!isMultiplayerMode || !isRoomConnected || !hasLaunchedCurrentRoomMatch) return;

            var sb = new StringBuilder();
            bool anyUnit = false;
            foreach (var u in RTSUnit.AllUnits)
            {
                if (u == null || u.currentHp <= 0 || u.team != assignedTeam) continue;
                if (anyUnit) sb.Append(';');
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0},{1:F2},{2:F2},{3:F1},{4},{5},{6:F2},{7:F2}",
                    u.unitType.ToLowerInvariant(),
                    u.transform.position.x,
                    u.transform.position.z,
                    u.transform.eulerAngles.y,
                    u.currentHp,
                    u.isMoving ? 1 : 0,
                    u.commandDestination.x,
                    u.commandDestination.z);
                anyUnit = true;
            }

            sb.Append('|');
            bool anyBld = false;
            foreach (var b in RTSBuilding.AllBuildings)
            {
                if (b == null || b.currentHp <= 0 || b.team != assignedTeam) continue;
                if (anyBld) sb.Append(';');
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0},{1:F2},{2:F2},{3},{4},{5:F1}",
                    b.buildingType,
                    b.transform.position.x,
                    b.transform.position.z,
                    b.currentHp,
                    b.isConstructed ? 1 : 0,
                    b.constructionProgress);
                anyBld = true;
            }

            SendRawPacket("TEAM_STATE_SYNC", sb.ToString());
        }

        private void AssignRemoteVillagerToBuild(UnitTeam remoteTeam, RTSBuilding bld)
        {
            if (bld == null) return;
            RTSVillager bestVil = null;
            float bestDist = float.MaxValue;
            foreach (var u in RTSUnit.AllUnits)
            {
                if (u != null && u.team == remoteTeam && u.unitType.ToLower() == "villager" && u.currentHp > 0)
                {
                    var v = u.GetComponent<RTSVillager>();
                    if (v != null)
                    {
                        float d = Vector3.Distance(u.transform.position, bld.transform.position);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            bestVil = v;
                        }
                    }
                }
            }
            if (bestVil != null)
            {
                bestVil.AssignBuildTask(bld);
            }
        }

        private void ApplyRemoteTeamLiveState(UnitTeam remoteTeam, string payload)
        {
            if (string.IsNullOrEmpty(payload)) return;
            bool isInActiveMatch = hasLaunchedCurrentRoomMatch
                || (RTSHUDController.Instance != null && RTSHUDController.Instance.startScreenModal != null && !RTSHUDController.Instance.startScreenModal.activeSelf);
            if (!isInActiveMatch) return;
            hasLaunchedCurrentRoomMatch = true;

            string[] sections = payload.Split('|');

            // 1. Karşı takımın canlı birim konumlarını, yeni basılan askerlerini, hareket hedeflerini ve canlarını eşitle
            var usedUnits = new HashSet<RTSUnit>();
            if (sections.Length >= 1 && !string.IsNullOrWhiteSpace(sections[0]))
            {
                string[] unitEntries = sections[0].Split(';');
                foreach (string entry in unitEntries)
                {
                    if (string.IsNullOrWhiteSpace(entry)) continue;
                    string[] f = entry.Split(',');
                    if (f.Length >= 8)
                    {
                        string uType = f[0].ToLowerInvariant();
                        float px = float.Parse(f[1], CultureInfo.InvariantCulture);
                        float pz = float.Parse(f[2], CultureInfo.InvariantCulture);
                        float rotY = float.Parse(f[3], CultureInfo.InvariantCulture);
                        int hp = int.Parse(f[4], CultureInfo.InvariantCulture);
                        bool isMov = f[5] == "1";
                        float dx = float.Parse(f[6], CultureInfo.InvariantCulture);
                        float dz = float.Parse(f[7], CultureInfo.InvariantCulture);

                        Vector3 targetWorldPos = new Vector3(px, 0.1f, pz);
                        RTSUnit match = FindClosestTeamUnit(remoteTeam, uType, targetWorldPos, usedUnits);

                        // Eğer karşı oyuncunun ürettiği bu birim sahnede henüz yoksa anında oluştur!
                        if (match == null)
                        {
                            match = RTSUnit.SpawnUnit(uType, targetWorldPos, remoteTeam);
                        }

                        if (match != null)
                        {
                            usedUnits.Add(match);
                            if (hp < match.currentHp)
                            {
                                match.ApplyNetworkHp(hp);
                            }

                            Vector3 destPos = new Vector3(dx, 0.1f, dz);
                            if (isMov && (!match.isMoving || Vector3.Distance(new Vector3(match.commandDestination.x, 0f, match.commandDestination.z), new Vector3(dx, 0f, dz)) > 1.2f))
                            {
                                match.MoveTo(destPos, isManual: false);
                            }

                            float posDiff = Vector2.Distance(new Vector2(match.transform.position.x, match.transform.position.z), new Vector2(px, pz));
                            if (!isMov && match.isMoving && posDiff < 1.5f)
                            {
                                match.StopMovementFromNetwork();
                            }
                            if (posDiff > 2.2f)
                            {
                                match.transform.position = new Vector3(px, match.transform.position.y, pz);
                            }
                            else if (posDiff > 0.15f && !isMov)
                            {
                                match.transform.position = Vector3.Lerp(match.transform.position, new Vector3(px, match.transform.position.y, pz), 0.6f);
                                match.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
                            }
                        }
                    }
                }
            }

            // Karşı takımın sahnesinde ölmüş (canlı listesinde artık yer almayan) birimleri bu sahnede de temizle
            var currentUnitsSnapshot = new List<RTSUnit>(RTSUnit.AllUnits);
            foreach (var u in currentUnitsSnapshot)
            {
                if (u != null && !u.isDead && u.currentHp > 0 && u.team == remoteTeam && !usedUnits.Contains(u))
                {
                    u.ApplyNetworkHp(0);
                }
            }

            // 2. Karşı takımın yeni yaptığı binaları, inşaat ilerlemelerini ve can değerlerini anında eşitle!
            var usedBlds = new HashSet<RTSBuilding>();
            if (sections.Length >= 2 && !string.IsNullOrWhiteSpace(sections[1]))
            {
                string[] bldEntries = sections[1].Split(';');
                foreach (string entry in bldEntries)
                {
                    if (string.IsNullOrWhiteSpace(entry)) continue;
                    string[] f = entry.Split(',');
                    if (f.Length >= 6 && Enum.TryParse(f[0], out RTSBuildingType bType))
                    {
                        float bx = float.Parse(f[1], CultureInfo.InvariantCulture);
                        float bz = float.Parse(f[2], CultureInfo.InvariantCulture);
                        int hp = int.Parse(f[3], CultureInfo.InvariantCulture);
                        bool isConst = f[4] == "1";
                        float prog = float.Parse(f[5], CultureInfo.InvariantCulture);

                        RTSBuilding bestBld = null;
                        float bestD = 4.5f;
                        foreach (var b in RTSBuilding.AllBuildings)
                        {
                            if (b == null || b.currentHp <= 0 || b.team != remoteTeam || b.buildingType != bType || usedBlds.Contains(b)) continue;
                            float d = Vector2.Distance(new Vector2(b.transform.position.x, b.transform.position.z), new Vector2(bx, bz));
                            if (d < bestD)
                            {
                                bestD = d;
                                bestBld = b;
                            }
                        }

                        // Eğer rakip oyuncu yeni bir inşaat yaptıysa ve bu sahnede henüz yoksa anında oluştur!
                        if (bestBld == null)
                        {
                            bestBld = RTSBuilding.SpawnBuilding(bType, new Vector3(bx, 0f, bz), remoteTeam, isConst);
                            if (!isConst && bestBld != null)
                            {
                                if (prog > 0f) bestBld.AddConstructionProgress(prog);
                                AssignRemoteVillagerToBuild(remoteTeam, bestBld);
                            }
                            RTSMinimapController.Instance?.DrawMinimap();
                        }

                        if (bestBld != null)
                        {
                            usedBlds.Add(bestBld);
                            if (isConst && !bestBld.isConstructed)
                            {
                                bestBld.AddConstructionProgress(100f);
                            }
                            else if (!bestBld.isConstructed && prog > bestBld.constructionProgress)
                            {
                                bestBld.AddConstructionProgress(prog - bestBld.constructionProgress);
                            }
                            if (hp < bestBld.currentHp)
                            {
                                bestBld.ApplyNetworkHp(hp);
                            }
                        }
                    }
                }
            }

            // Karşı takımın sahnesinde yıkılmış (canlı listesinde artık yer almayan) binaları bu sahnede de yık
            var currentBldsSnapshot = new List<RTSBuilding>(RTSBuilding.AllBuildings);
            foreach (var b in currentBldsSnapshot)
            {
                if (b != null && !b.isDestroyed && b.currentHp > 0 && b.team == remoteTeam && !usedBlds.Contains(b))
                {
                    b.ApplyNetworkHp(0);
                }
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
        /// Sefer başlatıldığında odadaki diğer oyuncunun da birebir aynı harita ve deterministik tohum (Seed) ile maça girmesini sağlar.
        /// </summary>
        public void BroadcastMatchStart()
        {
            if (!isMultiplayerMode || !isRoomConnected) return;
            hasLaunchedCurrentRoomMatch = true;
            try
            {
                if (File.Exists(RegistryFilePath))
                {
                    string raw = File.ReadAllText(RegistryFilePath, Encoding.UTF8);
                    var reg = JsonUtility.FromJson<RoomRegistryData>(raw);
                    var r = reg?.rooms?.Find(x => x != null && string.Equals(x.roomName, matchRoomId, StringComparison.OrdinalIgnoreCase));
                    if (r != null)
                    {
                        r.mapType = RTSAutoBootstrap.SelectedMap.ToString();
                        r.matchSeed = RTSAutoBootstrap.MatchRandomSeed;
                        r.matchStarted = true;
                        r.winnerTeam = "";
                        r.lastUpdatedTicks = DateTime.UtcNow.Ticks;
                        File.WriteAllText(RegistryFilePath, JsonUtility.ToJson(reg), Encoding.UTF8);
                    }
                }
            }
            catch { }
            SendRawPacket("MATCH_START", $"{RTSAutoBootstrap.SelectedMap}|{RTSAutoBootstrap.MatchRandomSeed}");
        }

        /// <summary>
        /// Herhangi bir binanın aldığı hasarı veya yıkımını odadaki diğer oyuncuya anında senkronize eder.
        /// </summary>
        public void BroadcastBuildingHpSync(RTSBuilding bld, bool forceZero = false)
        {
            if (!isMultiplayerMode || !isRoomConnected || bld == null) return;
            int hpToSync = forceZero ? 0 : bld.currentHp;
            string payload = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2:F2}|{3:F2}|{4}",
                bld.team, bld.buildingType, bld.transform.position.x, bld.transform.position.z, hpToSync);
            SendRawPacket("BLD_HP_SYNC", payload);
        }

        /// <summary>
        /// Herhangi bir askerin/köylünün aldığı hasarı veya ölümünü odadaki diğer oyuncuya anında senkronize eder.
        /// </summary>
        public void BroadcastUnitHpSync(RTSUnit unit, bool forceZero = false)
        {
            if (!isMultiplayerMode || !isRoomConnected || unit == null) return;
            int hpToSync = forceZero ? 0 : unit.currentHp;
            string payload = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2:F2}|{3:F2}|{4}",
                unit.team, unit.unitType.ToLowerInvariant(), unit.transform.position.x, unit.transform.position.z, hpToSync);
            SendRawPacket("UNIT_HP_SYNC", payload);
        }

        /// <summary>
        /// Araştırılan teknoloji veya çağ ilerlemesini (Feodal Çağ, Kale Çağı, Demirci geliştirmeleri) diğer oyuncuya senkronize eder.
        /// </summary>
        public void BroadcastTechResearch(string techId, UnitTeam team)
        {
            if (!isMultiplayerMode || !isRoomConnected || string.IsNullOrEmpty(techId)) return;
            SendRawPacket("TECH_SYNC", techId);
        }

        /// <summary>
        /// Şehir Merkezi yıkıldığında maç sonucunu (Kazanan: ZAFER / Kaybeden: YENİLGİ) her iki oyuncuda aynı anda tetikler!
        /// </summary>
        public void BroadcastGameOver(UnitTeam winnerTeam)
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
                        r.winnerTeam = winnerTeam.ToString();
                        r.lastUpdatedTicks = DateTime.UtcNow.Ticks;
                        File.WriteAllText(RegistryFilePath, JsonUtility.ToJson(reg), Encoding.UTF8);
                    }
                }
            }
            catch { }
            SendRawPacket("GAME_OVER", winnerTeam.ToString());
        }

        /// <summary>
        /// Oyun sonu ekranından "Tekrar Oyna"ya basıldığında her iki oyuncunun sahnesini aynı tohumla yeniden başlatır.
        /// </summary>
        public void BroadcastMatchRestart()
        {
            if (!isMultiplayerMode || !isRoomConnected) return;
            hasLaunchedCurrentRoomMatch = true;
            try
            {
                if (File.Exists(RegistryFilePath))
                {
                    string raw = File.ReadAllText(RegistryFilePath, Encoding.UTF8);
                    var reg = JsonUtility.FromJson<RoomRegistryData>(raw);
                    var r = reg?.rooms?.Find(x => x != null && string.Equals(x.roomName, matchRoomId, StringComparison.OrdinalIgnoreCase));
                    if (r != null)
                    {
                        r.mapType = RTSAutoBootstrap.SelectedMap.ToString();
                        r.matchSeed = RTSAutoBootstrap.MatchRandomSeed;
                        r.matchStarted = true;
                        r.winnerTeam = "";
                        r.lastUpdatedTicks = DateTime.UtcNow.Ticks;
                        File.WriteAllText(RegistryFilePath, JsonUtility.ToJson(reg), Encoding.UTF8);
                    }
                }
            }
            catch { }
            SendRawPacket("MATCH_RESTART", $"{RTSAutoBootstrap.SelectedMap}|{RTSAutoBootstrap.MatchRandomSeed}");
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

#if UNITY_EDITOR
        [UnityEditor.MenuItem("CSE377 RTS/👥 1. Klon Editör Aç (Build Almadan 2. Oyuncu - Kodlar Otomatik Güncellenir)")]
        public static void OpenCloneEditorForSecondPlayer()
        {
            try
            {
                string mainProjectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string parentDir = Directory.GetParent(mainProjectPath).FullName;
                string cloneProjectPath = Path.Combine(parentDir, "unity_Player2_Clone");

                if (!Directory.Exists(cloneProjectPath))
                {
                    Directory.CreateDirectory(cloneProjectPath);
                }

                // Assets, Packages ve ProjectSettings klasörlerini Windows Junction (mklink /J) ile bağla!
                // Böylece ana projede yazılan her kod anında Klon Editörde de otomatik güncellenir (Build almaya gerek kalmaz!)
                string[] linkedFolders = new string[] { "Assets", "Packages", "ProjectSettings" };
                foreach (string folder in linkedFolders)
                {
                    string src = Path.Combine(mainProjectPath, folder);
                    string dst = Path.Combine(cloneProjectPath, folder);
                    if (!Directory.Exists(dst))
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{dst}\" \"{src}\"")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        var p = System.Diagnostics.Process.Start(psi);
                        p?.WaitForExit(4000);
                    }
                }

                string editorExe = UnityEditor.EditorApplication.applicationPath;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = editorExe,
                    Arguments = $"-projectPath \"{cloneProjectPath}\"",
                    UseShellExecute = true
                });

                Debug.Log($"[CSE377 KLON EDİTÖR] 2. Oyuncu Klon Editörü başlatıldı: {cloneProjectPath} (Kodlar otomatik senkronize edilir, Build gerekmez!)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CSE377 KLON EDİTÖR HATASI] {ex.Message}");
            }
        }

        [UnityEditor.MenuItem("CSE377 RTS/🤖 2. Açık Odaya Sanal 2. Oyuncu Bağla (Tek Editörde 2/2 Hızlı Test)")]
        public static void MenuSimulateSecondPlayer()
        {
            if (!Application.isPlaying || Instance == null)
            {
                Debug.LogWarning("[CSE377 TEST] Önce oyunu Play (▶️) ile başlatıp Çok Oyunculu menüsünden bir oda açın.");
                return;
            }
            Instance.SimulateSecondPlayerForTest();
        }
#endif
    }
}
