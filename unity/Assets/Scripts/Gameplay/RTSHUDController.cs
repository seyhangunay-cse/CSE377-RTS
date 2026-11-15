using System;
using UnityEngine;
using UnityEngine.UI;
using CSE377.AI;

namespace CSE377.Gameplay
{
    public enum RTSDifficulty
    {
        Easy,
        Normal,
        Hard
    }

    /// <summary>
    /// RTS Üst Bilgi Çubuğu (Kaynaklar), AI Komutanı Paneli ve
    /// Alt Aksiyon Menüsünü (Köylü İnşaat, Kışla Birlik Basımı, Demirci Teknoloji Araştırması) yönetir.
    /// </summary>
    public class RTSHUDController : MonoBehaviour
    {
        public static RTSHUDController Instance { get; private set; }

        [Header("Kaynak Metinleri (Top Bar)")]
        public Text woodText;
        public Text foodText;
        public Text goldText;
        public Text armyText;
        public Text ageBadgeText;

        [Header("Global Alt Metin (Tooltip) Sistemi")]
        public GameObject globalTooltipBox;
        public Text globalTooltipTitle;
        public Text globalTooltipBody;
        public RectTransform globalTooltipRt;
        public RectTransform canvasRectTransform;

        private System.Func<string> currentTooltipBodyFunc;
        private System.Func<string> currentTooltipTitleFunc;

        public int currentWood { get; private set; } = 450;
        public int currentFood { get; private set; } = 200;
        public int currentGold { get; private set; } = 150;
        public int currentPop { get; private set; } = 4;
        public int currentMaxPop { get; private set; } = 10;

        public int villagerTotal { get; private set; } = 0;
        public int villagerFood { get; private set; } = 0;
        public int villagerWood { get; private set; } = 0;
        public int villagerGold { get; private set; } = 0;
        public int villagerBuild { get; private set; } = 0;
        public int villagerIdle { get; private set; } = 0;

        [Header("Akın Geri Sayım Rozeti (Top Bar Raid Timer)")]
        public GameObject raidTimerBadge;
        public Text raidTimerText;

        [Header("İnşaat / Yerleştirme Bilgi Çubuğu (Placement Banner)")]
        public GameObject placementBanner;
        public Text placementTitleText;
        public Text placementDescText;

        public void ShowPlacementBanner(string title, string details)
        {
            if (placementBanner != null)
            {
                placementBanner.SetActive(true);
                if (placementTitleText != null) placementTitleText.text = title;
                if (placementDescText != null) placementDescText.text = details;
            }
        }

        public void HidePlacementBanner()
        {
            if (placementBanner != null)
            {
                placementBanner.SetActive(false);
            }
        }

        [Header("AI Komutanı Butonları")]
        public Button defendNorthGateBtn;
        public Button economyPlanBtn;
        public Button counterAttackBtn;
        public Button scoutExplorationBtn;
        public Button fortificationBtn;
        public Button technologyAdvancementBtn;
        public InputField customPromptInput;
        public Button sendCustomPromptBtn;
        public Text aiStatusText;

        [Header("AI Komutanı Çekmecesi & Üst Bar Menü")]
        public GameObject aiCommanderPanel;
        public Button toggleAiCommanderBtn;
        public Button closeAiCommanderBtn;
        public Button topBarMenuBtn;

        [Header("Alt Aksiyon Paneli (Bina & Köylü İşlemleri)")]
        public GameObject bottomActionPanel;
        public Text actionPanelTitle;
        public Button actionBtn1;
        public Button actionBtn2;
        public Button actionBtn3;
        public Button actionBtn4;
        public Button actionBtn5;
        public Text actionBtn1Text;
        public Text actionBtn2Text;
        public Text actionBtn3Text;
        public Text actionBtn4Text;
        public Text actionBtn5Text;

        [Header("Seçili Birim / Bina Bilgi Kartı (Inspector)")]
        public GameObject inspectorPanel;
        public Text inspectorHeraldicBadgeText;
        public Text inspectorTitleText;
        public Text inspectorSubtitleText;
        public Text inspectorHpText;
        public RectTransform inspectorHpFill;
        public Text inspectorStatsText;
        public Text inspectorExtraText;

        [Header("Boşta Kalan Köylü Uyarısı")]
        public Button idleVillagerBtn;
        public Text idleVillagerBtnText;

        [Header("Hızlı Birlik Seçimi ve Köylü Faaliyet Durumu")]
        public Button selectAllMilitaryBtn;
        public Button selectAllVillagersBtn;
        public Text villagerActivityText;

        [Header("Zafer ve Yenilgi Modalı")]
        public GameObject gameOverModal;
        public Text gameOverTitleText;
        public Text gameOverSubtitleText;
        public Text gameOverStatsText;
        public Button gameOverRestartBtn;
        public Button gameOverMainMenuBtn;

        [Header("Başlangıç Ekranı (Start Screen / Main Menu)")]
        public GameObject startScreenModal;
        public Button startScreenPlayBtn;
        public Button startScreenMapBtn;
        public Text startScreenMapBtnText;
        public Text startScreenMapDescText;
        public Button startScreenDifficultyBtn;
        public Text startScreenDifficultyBtnText;
        public Button startScreenGuideBtn;
        public Button startScreenAudioBtn;
        public Text startScreenAudioBtnText;
        public Button startScreenModeBtn;
        public Text startScreenModeBtnText;

        [Header("Çok Oyunculu Photon Oda Lobisi Modalı (Multiplayer Lobby Modal)")]
        public GameObject multiplayerLobbyModal;
        public InputField mpPlayerNameInput;
        public InputField startScreenRoomInput;
        public Button mpCreateRoomBtn;
        public Button startScreenJoinRoomBtn;
        public Button mpRefreshRoomsBtn;
        public Button[] mpOpenRoomRowBtns;
        public Text[] mpOpenRoomRowTexts;
        public Text mpNoOpenRoomsText;
        public Button startScreenTeamBtn;
        public Text startScreenTeamBtnText;
        public Text startScreenRoomStatusText;
        public Button mpLobbyMapBtn;
        public Text mpLobbyMapBtnText;
        public Button mpLobbyStartMatchBtn;
        public Text mpLobbyStartMatchBtnText;
        public Button mpLobbyBackBtn;

        [Header("Açılan Oda Penceresi (Active Room Modal - Oyun Başlat / Oyun Ayarları / Odayı Kapat)")]
        public GameObject activeRoomModal;
        public Text activeRoomTitleText;
        public Text activeRoomBlueSlotText;
        public Text activeRoomRedSlotText;
        public Button activeRoomSettingsBtn;
        public Button activeRoomCloseBtn;
        public Button activeRoomSimulatePlayerBtn;

        [Header("Nasıl Oynanır / Kısayollar Modalı")]
        public GameObject controlsGuideModal;
        public Button controlsGuideCloseBtn;

        [Header("Oyun İçi Duraklatma (Pause Menu / ESC Modalı)")]
        public GameObject pauseMenuModal;
        public Button pauseResumeBtn;
        public Button pauseSettingsBtn;
        public Button pauseRestartBtn;
        public Button pauseGuideBtn;
        public Button pauseMainMenuBtn;

        [Header("Ses & Oyun Ayarları Modalı (Settings Modal)")]
        public GameObject settingsModal;
        public Button settingsCloseBtn;
        public Button settingsResetBtn;
        public Button settingsMuteToggleBtn;
        public Text settingsMuteToggleText;

        public Button masterVolDownBtn;
        public Button masterVolUpBtn;
        public Text masterVolText;
        public RectTransform masterVolBar;

        public Button musicVolDownBtn;
        public Button musicVolUpBtn;
        public Text musicVolText;
        public RectTransform musicVolBar;

        [Header("Müzik Parçası Seçici UI")]
        public Button musicTrackPrevBtn;
        public Button musicTrackNextBtn;
        public Text musicTrackTitleText;
        public Text musicTrackSubText;

        public Button sfxVolDownBtn;
        public Button sfxVolUpBtn;
        public Text sfxVolText;
        public RectTransform sfxVolBar;

        public Button voiceVolDownBtn;
        public Button voiceVolUpBtn;
        public Text voiceVolText;
        public RectTransform voiceVolBar;

        public static bool SkipStartScreenOnNextLoad = false;
        public static RTSDifficulty CurrentDifficulty = RTSDifficulty.Normal;
        public static bool IsAudioMuted = false;

        [Header("Bağlantılar")]
        public AiGatewayClient gatewayClient;
        public RTSGameManager gameManager;

        private float nextInspectorUpdateTime = 0f;
        private int villagerBuildPage = 0; // 0: Ekonomi, 1: Askeri, 2: Tahkimat

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void Start()
        {
            if (defendNorthGateBtn != null)
                defendNorthGateBtn.onClick.AddListener(() => RequestPlanFromAI("Kuzey kapısından atlılar yaklaşıyor. Durumumuzu kontrol et ve savunma için ne yapmam gerektiğini planla."));

            if (economyPlanBtn != null)
                economyPlanBtn.onClick.AddListener(() => RequestPlanFromAI("Kaynaklarımız az. Ekonomiyi güçlendirmek, köylü basıp tarla kurmak için ne yapmalıyım?"));

            if (counterAttackBtn != null)
                counterAttackBtn.onClick.AddListener(() => RequestPlanFromAI("Düşman zayıfladı. Karşı saldırı için hangi birimleri üretip nereye yönlendirmeliyim?"));

            if (scoutExplorationBtn != null)
                scoutExplorationBtn.onClick.AddListener(() => RequestPlanFromAI("Savaş sisini dağıtmak ve gizli altın yataklarını keşfetmek için öncü birlik görevlendir."));

            if (fortificationBtn != null)
                fortificationBtn.onClick.AddListener(() => RequestPlanFromAI("Savunma kuleleri ve surlar inşa ederek karargahımızı tahkim et."));

            if (technologyAdvancementBtn != null)
                technologyAdvancementBtn.onClick.AddListener(() => RequestPlanFromAI("Feodal Çağ'a geçmek için demirci kur ve silah teknolojilerini geliştir."));

            if (sendCustomPromptBtn != null && customPromptInput != null)
                sendCustomPromptBtn.onClick.AddListener(() =>
                {
                    if (!string.IsNullOrEmpty(customPromptInput.text))
                    {
                        RequestPlanFromAI(customPromptInput.text);
                        customPromptInput.text = "";
                    }
                });

            if (customPromptInput != null)
            {
                customPromptInput.onEndEdit.AddListener((val) =>
                {
                    if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                    {
                        if (!string.IsNullOrEmpty(val))
                        {
                            RequestPlanFromAI(val);
                            customPromptInput.text = "";
                            customPromptInput.ActivateInputField();
                        }
                    }
                });
            }

            if (selectAllMilitaryBtn != null)
            {
                selectAllMilitaryBtn.onClick.AddListener(() => RTSSelectionManager.Instance?.SelectAllMilitaryUnits());
            }

            if (selectAllVillagersBtn != null)
            {
                selectAllVillagersBtn.onClick.AddListener(() => RTSSelectionManager.Instance?.SelectAllVillagers());
            }

            if (idleVillagerBtn != null)
            {
                idleVillagerBtn.onClick.AddListener(() => RTSSelectionManager.Instance?.SelectNextIdleVillager());
            }

            if (toggleAiCommanderBtn != null)
            {
                toggleAiCommanderBtn.onClick.AddListener(() =>
                {
                    if (aiCommanderPanel != null)
                    {
                        aiCommanderPanel.SetActive(!aiCommanderPanel.activeSelf);
                    }
                });
            }

            if (closeAiCommanderBtn != null)
            {
                closeAiCommanderBtn.onClick.AddListener(() =>
                {
                    if (aiCommanderPanel != null)
                    {
                        aiCommanderPanel.SetActive(false);
                    }
                });
            }

            if (topBarMenuBtn != null)
            {
                topBarMenuBtn.onClick.AddListener(() =>
                {
                    if (pauseMenuModal != null)
                    {
                        bool isPaused = pauseMenuModal.activeSelf;
                        pauseMenuModal.SetActive(!isPaused);
                        Time.timeScale = !isPaused ? 0f : 1f;
                    }
                });
            }

            if (gatewayClient != null)
            {
                gatewayClient.OnRequestStarted += () =>
                {
                    string cmdTag = (RTSGameManager.LocalPlayerTeam == UnitTeam.Enemy) ? "🔴 Kırmızı AI Komutanı" : "🔵 Mavi AI Komutanı";
                    SetAiStatus($"{cmdTag} (Ollama) stratejik plan hazırlıyor...", Color.cyan);
                };
                gatewayClient.OnPlanReceived += (plan) =>
                {
                    string cmdTag = (RTSGameManager.LocalPlayerTeam == UnitTeam.Enemy) ? "🔴 Kırmızı AI Komutanı" : "🔵 Mavi AI Komutanı";
                    SetAiStatus($"{cmdTag}: Plan Hazır! Onayınız bekleniyor.", Color.green);
                };
                gatewayClient.OnPlanRequestFailed += (err) => SetAiStatus($"Hata: {err}", Color.red);
            }

            // --- Başlangıç Ekranı (Start Screen / Main Menu) Buton Bağlantıları ---
            if (startScreenPlayBtn != null)
            {
                startScreenPlayBtn.onClick.AddListener(() =>
                {
                    RTSNetworkSyncManager.Instance?.SetSinglePlayerMode();
                    LaunchMatchFromMenu();
                });
            }

            // Ana Menüdeki "ÇOK OYUNCULU" butonu -> Çok Oyunculu Oda Lobisi Modalını açar
            if (startScreenModeBtn != null)
            {
                startScreenModeBtn.onClick.AddListener(() =>
                {
                    if (multiplayerLobbyModal != null)
                    {
                        multiplayerLobbyModal.SetActive(true);
                    }
                    if (RTSNetworkSyncManager.Instance != null)
                    {
                        if (mpPlayerNameInput != null && string.IsNullOrWhiteSpace(mpPlayerNameInput.text))
                        {
                            mpPlayerNameInput.text = RTSNetworkSyncManager.Instance.localPlayerName;
                        }
                        RTSNetworkSyncManager.Instance.RefreshOpenRoomsRegistry();
                    }
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.Click);
                    UpdatePhotonRoomLobbyUI();
                    UpdateMapSelectionUI();
                });
            }

            if (mpPlayerNameInput != null)
            {
                mpPlayerNameInput.onEndEdit.AddListener((val) =>
                {
                    RTSNetworkSyncManager.Instance?.SetPlayerName(val);
                    UpdatePhotonRoomLobbyUI();
                });
            }

            if (mpCreateRoomBtn != null)
            {
                mpCreateRoomBtn.onClick.AddListener(() =>
                {
                    if (RTSNetworkSyncManager.Instance != null)
                    {
                        string pName = (mpPlayerNameInput != null && !string.IsNullOrWhiteSpace(mpPlayerNameInput.text))
                            ? mpPlayerNameInput.text.Trim()
                            : RTSNetworkSyncManager.Instance.localPlayerName;
                        string rCode = (startScreenRoomInput != null && !string.IsNullOrWhiteSpace(startScreenRoomInput.text))
                            ? startScreenRoomInput.text.Trim().ToUpperInvariant()
                            : "ODA-101";
                        RTSNetworkSyncManager.Instance.CreatePhotonRoom(pName, rCode);
                    }
                    if (activeRoomModal != null)
                    {
                        activeRoomModal.SetActive(true);
                    }
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.TechComplete);
                    UpdatePhotonRoomLobbyUI();
                    UpdateMapSelectionUI();
                });
            }

            if (mpRefreshRoomsBtn != null)
            {
                mpRefreshRoomsBtn.onClick.AddListener(() =>
                {
                    RTSNetworkSyncManager.Instance?.RefreshOpenRoomsRegistry();
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.Click);
                    UpdatePhotonRoomLobbyUI();
                });
            }

            if (mpLobbyBackBtn != null)
            {
                mpLobbyBackBtn.onClick.AddListener(() =>
                {
                    if (activeRoomModal != null)
                    {
                        activeRoomModal.SetActive(false);
                    }
                    if (multiplayerLobbyModal != null)
                    {
                        multiplayerLobbyModal.SetActive(false);
                    }
                    RTSNetworkSyncManager.Instance?.SetSinglePlayerMode();
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.Click);
                    UpdatePhotonRoomLobbyUI();
                });
            }

            if (activeRoomCloseBtn != null)
            {
                activeRoomCloseBtn.onClick.AddListener(() =>
                {
                    RTSNetworkSyncManager.Instance?.LeaveCurrentRoom();
                    if (activeRoomModal != null)
                    {
                        activeRoomModal.SetActive(false);
                    }
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.Click);
                    UpdatePhotonRoomLobbyUI();
                });
            }

            if (activeRoomSettingsBtn != null)
            {
                activeRoomSettingsBtn.onClick.AddListener(OpenSettingsModal);
            }

            if (activeRoomSimulatePlayerBtn != null)
            {
                activeRoomSimulatePlayerBtn.onClick.AddListener(() =>
                {
                    RTSNetworkSyncManager.Instance?.SimulateSecondPlayerForTest();
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.TechComplete);
                    UpdatePhotonRoomLobbyUI();
                });
            }

            if (mpLobbyStartMatchBtn != null)
            {
                mpLobbyStartMatchBtn.onClick.AddListener(() =>
                {
                    var net = RTSNetworkSyncManager.Instance;
                    if (net == null || !net.CanStartMultiplayerMatch())
                    {
                        RTSAudioManager.Instance?.PlaySound(RTSSoundType.Click);
                        if (startScreenRoomStatusText != null)
                        {
                            startScreenRoomStatusText.text = "⚠️ Odaya 2. oyuncu bağlanmadan (2/2 olmadan) Çok Oyunculu Oyun başlatılamaz!";
                            startScreenRoomStatusText.color = new Color(1f, 0.45f, 0.35f);
                        }
                        return;
                    }
                    LaunchMatchFromMenu();
                });
            }

            if (mpLobbyMapBtn != null)
            {
                mpLobbyMapBtn.onClick.AddListener(CycleMapSelection);
            }

            if (startScreenJoinRoomBtn != null)
            {
                startScreenJoinRoomBtn.onClick.AddListener(() =>
                {
                    if (RTSNetworkSyncManager.Instance != null)
                    {
                        string pName = (mpPlayerNameInput != null && !string.IsNullOrWhiteSpace(mpPlayerNameInput.text))
                            ? mpPlayerNameInput.text.Trim()
                            : RTSNetworkSyncManager.Instance.localPlayerName;
                        string roomCode = (startScreenRoomInput != null && !string.IsNullOrWhiteSpace(startScreenRoomInput.text))
                            ? startScreenRoomInput.text.Trim().ToUpperInvariant()
                            : "ODA-101";
                        RTSNetworkSyncManager.Instance.JoinPhotonRoom(pName, roomCode);
                    }
                    if (activeRoomModal != null)
                    {
                        activeRoomModal.SetActive(true);
                    }
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.TechComplete);
                    UpdatePhotonRoomLobbyUI();
                    UpdateMapSelectionUI();
                });
            }

            if (startScreenTeamBtn != null)
            {
                startScreenTeamBtn.onClick.AddListener(() =>
                {
                    if (RTSNetworkSyncManager.Instance != null)
                    {
                        if (startScreenRoomInput != null && !string.IsNullOrWhiteSpace(startScreenRoomInput.text))
                        {
                            RTSNetworkSyncManager.Instance.matchRoomId = startScreenRoomInput.text.Trim().ToUpperInvariant();
                        }
                        RTSNetworkSyncManager.Instance.CycleAssignedTeam();
                    }
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.Click);
                    UpdatePhotonRoomLobbyUI();
                });
            }

            if (RTSNetworkSyncManager.Instance != null)
            {
                RTSNetworkSyncManager.Instance.OnRoomStateChanged += UpdatePhotonRoomLobbyUI;
            }
            UpdatePhotonRoomLobbyUI();

            if (startScreenMapBtn != null)
            {
                startScreenMapBtn.onClick.AddListener(CycleMapSelection);
                UpdateMapSelectionUI();
            }

            if (startScreenDifficultyBtn != null)
            {
                startScreenDifficultyBtn.onClick.AddListener(() =>
                {
                    if (CurrentDifficulty == RTSDifficulty.Easy) CurrentDifficulty = RTSDifficulty.Normal;
                    else if (CurrentDifficulty == RTSDifficulty.Normal) CurrentDifficulty = RTSDifficulty.Hard;
                    else CurrentDifficulty = RTSDifficulty.Easy;
                    UpdateDifficultyButtonText();
                });
                UpdateDifficultyButtonText();
            }

            if (startScreenGuideBtn != null)
            {
                startScreenGuideBtn.onClick.AddListener(() =>
                {
                    if (controlsGuideModal != null) controlsGuideModal.SetActive(true);
                });
            }

            if (controlsGuideCloseBtn != null)
            {
                controlsGuideCloseBtn.onClick.AddListener(() =>
                {
                    if (controlsGuideModal != null) controlsGuideModal.SetActive(false);
                });
            }

            if (startScreenAudioBtn != null)
            {
                startScreenAudioBtn.onClick.AddListener(OpenSettingsModal);
            }

            // --- Pause Menüsü & Ses Ayarları Buton Bağlantıları ---
            if (pauseSettingsBtn != null)
            {
                pauseSettingsBtn.onClick.RemoveAllListeners();
                pauseSettingsBtn.onClick.AddListener(OpenSettingsModal);
            }

            if (settingsCloseBtn != null)
            {
                settingsCloseBtn.onClick.RemoveAllListeners();
                settingsCloseBtn.onClick.AddListener(CloseSettingsModal);
            }

            if (settingsResetBtn != null)
            {
                settingsResetBtn.onClick.RemoveAllListeners();
                settingsResetBtn.onClick.AddListener(() =>
                {
                    RTSAudioManager.Instance?.ResetToDefaults();
                    UpdateAudioSettingsUI();
                });
            }

            if (settingsMuteToggleBtn != null)
            {
                settingsMuteToggleBtn.onClick.RemoveAllListeners();
                settingsMuteToggleBtn.onClick.AddListener(() =>
                {
                    RTSAudioManager.Instance?.ToggleMute();
                    UpdateAudioSettingsUI();
                });
            }

            // Genel Ses (Master)
            if (masterVolDownBtn != null)
            {
                masterVolDownBtn.onClick.RemoveAllListeners();
                masterVolDownBtn.onClick.AddListener(() =>
                {
                    if (RTSAudioManager.Instance != null)
                    {
                        RTSAudioManager.Instance.SetMasterVolume(RTSAudioManager.Instance.masterVolume - 0.10f);
                        UpdateAudioSettingsUI();
                    }
                });
            }
            if (masterVolUpBtn != null)
            {
                masterVolUpBtn.onClick.RemoveAllListeners();
                masterVolUpBtn.onClick.AddListener(() =>
                {
                    if (RTSAudioManager.Instance != null)
                    {
                        RTSAudioManager.Instance.SetMasterVolume(RTSAudioManager.Instance.masterVolume + 0.10f);
                        UpdateAudioSettingsUI();
                    }
                });
            }

            // Müzik Sesi (Music)
            if (musicVolDownBtn != null)
            {
                musicVolDownBtn.onClick.RemoveAllListeners();
                musicVolDownBtn.onClick.AddListener(() =>
                {
                    if (RTSAudioManager.Instance != null)
                    {
                        RTSAudioManager.Instance.SetMusicVolume(RTSAudioManager.Instance.musicVolume - 0.10f);
                        UpdateAudioSettingsUI();
                    }
                });
            }
            if (musicVolUpBtn != null)
            {
                musicVolUpBtn.onClick.RemoveAllListeners();
                musicVolUpBtn.onClick.AddListener(() =>
                {
                    if (RTSAudioManager.Instance != null)
                    {
                        RTSAudioManager.Instance.SetMusicVolume(RTSAudioManager.Instance.musicVolume + 0.10f);
                        UpdateAudioSettingsUI();
                    }
                });
            }

            // Müzik Parçası Değiştirici (Önceki / Sonraki)
            if (musicTrackPrevBtn != null)
            {
                musicTrackPrevBtn.onClick.RemoveAllListeners();
                musicTrackPrevBtn.onClick.AddListener(() =>
                {
                    RTSAudioManager.Instance?.PreviousMusicTrack();
                    UpdateAudioSettingsUI();
                });
            }
            if (musicTrackNextBtn != null)
            {
                musicTrackNextBtn.onClick.RemoveAllListeners();
                musicTrackNextBtn.onClick.AddListener(() =>
                {
                    RTSAudioManager.Instance?.NextMusicTrack();
                    UpdateAudioSettingsUI();
                });
            }

            // Efekt Sesleri (SFX)
            if (sfxVolDownBtn != null)
            {
                sfxVolDownBtn.onClick.RemoveAllListeners();
                sfxVolDownBtn.onClick.AddListener(() =>
                {
                    if (RTSAudioManager.Instance != null)
                    {
                        RTSAudioManager.Instance.SetSfxVolume(RTSAudioManager.Instance.sfxVolume - 0.10f);
                        RTSAudioManager.Instance.PlaySound(RTSSoundType.Click);
                        UpdateAudioSettingsUI();
                    }
                });
            }
            if (sfxVolUpBtn != null)
            {
                sfxVolUpBtn.onClick.RemoveAllListeners();
                sfxVolUpBtn.onClick.AddListener(() =>
                {
                    if (RTSAudioManager.Instance != null)
                    {
                        RTSAudioManager.Instance.SetSfxVolume(RTSAudioManager.Instance.sfxVolume + 0.10f);
                        RTSAudioManager.Instance.PlaySound(RTSSoundType.SwordClash);
                        UpdateAudioSettingsUI();
                    }
                });
            }

            // Birlik Seslendirmesi (Voice)
            if (voiceVolDownBtn != null)
            {
                voiceVolDownBtn.onClick.RemoveAllListeners();
                voiceVolDownBtn.onClick.AddListener(() =>
                {
                    if (RTSAudioManager.Instance != null)
                    {
                        RTSAudioManager.Instance.SetVoiceVolume(RTSAudioManager.Instance.voiceVolume - 0.10f);
                        RTSAudioManager.Instance.PlayUnitVoice("villager", "work", true);
                        UpdateAudioSettingsUI();
                    }
                });
            }
            if (voiceVolUpBtn != null)
            {
                voiceVolUpBtn.onClick.RemoveAllListeners();
                voiceVolUpBtn.onClick.AddListener(() =>
                {
                    if (RTSAudioManager.Instance != null)
                    {
                        RTSAudioManager.Instance.SetVoiceVolume(RTSAudioManager.Instance.voiceVolume + 0.10f);
                        RTSAudioManager.Instance.PlayUnitVoice("spearman", "select", true);
                        UpdateAudioSettingsUI();
                    }
                });
            }

            UpdateAudioSettingsUI();

            // --- Pause Menüsü Buton Bağlantıları ---
            if (SkipStartScreenOnNextLoad)
            {
                SkipStartScreenOnNextLoad = false;
                if (startScreenModal != null) startScreenModal.SetActive(false);
                Time.timeScale = 1f;
                gameManager?.StartMatch();
                SetAiStatus("[SEFER BAŞLADI] Şehir Merkezinizi koruyun, köylülerle ekonomi kurun ve düşmanı fethedin!", Color.white);
            }
            else
            {
                if (startScreenModal != null)
                {
                    startScreenModal.SetActive(true);
                    Time.timeScale = 0f;
                }
            }

            if (pauseResumeBtn != null)
            {
                pauseResumeBtn.onClick.RemoveAllListeners();
                pauseResumeBtn.onClick.AddListener(ResumeGame);
            }

            if (pauseRestartBtn != null)
            {
                pauseRestartBtn.onClick.RemoveAllListeners();
                pauseRestartBtn.onClick.AddListener(RestartCurrentMatch);
            }

            if (pauseGuideBtn != null)
            {
                pauseGuideBtn.onClick.RemoveAllListeners();
                pauseGuideBtn.onClick.AddListener(() =>
                {
                    if (controlsGuideModal != null) controlsGuideModal.SetActive(true);
                });
            }

            if (pauseMainMenuBtn != null)
            {
                pauseMainMenuBtn.onClick.RemoveAllListeners();
                pauseMainMenuBtn.onClick.AddListener(ReturnToMainMenu);
            }

            HideActionPanel();
            if (inspectorPanel != null) inspectorPanel.SetActive(false);
        }

        private void UpdateDifficultyButtonText()
        {
            if (startScreenDifficultyBtnText == null) return;
            switch (CurrentDifficulty)
            {
                case RTSDifficulty.Easy:
                    startScreenDifficultyBtnText.text = "⚔️ ZORLUK: ACEMİ (Sakin Gelişim)";
                    break;
                case RTSDifficulty.Normal:
                    startScreenDifficultyBtnText.text = "⚔️ ZORLUK: STANDART (Dengeli AI)";
                    break;
                case RTSDifficulty.Hard:
                    startScreenDifficultyBtnText.text = "⚔️ ZORLUK: FATİH (Agresif Kuşatma)";
                    break;
            }
        }

        public void LaunchMatchFromMenu(bool fromNetworkRpc = false)
        {
            var net = RTSNetworkSyncManager.Instance;
            if (!fromNetworkRpc && net != null && net.isMultiplayerMode && !net.CanStartMultiplayerMatch())
            {
                string lockReason = (!net.isHost)
                    ? "⚠️ [ODA KİLİDİ] Yalnızca odayı kuran oyuncu (1. Oyuncu / Kurucu) oyunu başlatabilir!"
                    : "⚠️ [ODA KİLİDİ] Odaya 2. oyuncu bağlanmadan (2/2 olmadan) Çok Oyunculu Oyun başlatılamaz!";
                SetAiStatus(lockReason, new Color(1f, 0.45f, 0.35f));
                return;
            }

            // Eğer Kurucu (Host) maçı başlatıyorsa ve "Rastgele Harita" seçiliyse, her iki oyuncunun da
            // birebir aynı haritayı ve aynı ağaç/maden yerleşimini yüklemesi için önce haritayı ve deterministik tohumu (Seed) sabitle!
            if (!fromNetworkRpc)
            {
                if (RTSAutoBootstrap.SelectedMap == RTSMapType.RandomMap)
                {
                    int r = UnityEngine.Random.Range(0, 4);
                    RTSAutoBootstrap.SelectedMap = (r == 0) ? RTSMapType.RiverValley
                        : (r == 1) ? RTSMapType.OpenSteppe
                        : (r == 2) ? RTSMapType.BlackForest
                        : RTSMapType.CrossCorners;
                }
                RTSAutoBootstrap.MatchRandomSeed = UnityEngine.Random.Range(100000, 999999);
                RTSNetworkSyncManager.Instance?.BroadcastMapSelection(RTSAutoBootstrap.SelectedMap);
                RTSNetworkSyncManager.Instance?.BroadcastMatchStart();
            }

            if (activeRoomModal != null) activeRoomModal.SetActive(false);
            if (multiplayerLobbyModal != null) multiplayerLobbyModal.SetActive(false);
            if (startScreenModal != null) startScreenModal.SetActive(false);
            Time.timeScale = 1f;

            // Haritayı ve oyuncu perspektifini (Mavi Güney / Kırmızı Kuzey) ortak deterministik tohumla oluştur
            RTSAutoBootstrap.Instance?.RebuildMapForSelectedType();
            RTSAutoBootstrap.Instance?.RefreshPerspectiveForLocalTeam();

            gameManager?.StartMatch();
            RTSAudioManager.Instance?.PlaySound(RTSSoundType.WarHorn);

            string teamBanner = (RTSGameManager.LocalPlayerTeam == UnitTeam.Enemy)
                ? "🔴 [KIRMIZI KRALLIK + KIRMIZI AI KOMUTAN]"
                : "🔵 [MAVİ KRALLIK + MAVİ AI KOMUTAN]";
            SetAiStatus($"{teamBanner} Sefer Başladı! Ekonominizi kurun, AI Komutanınızla plan yapın ve zaferi kazanın!", Color.white);
        }

        public void UpdatePhotonRoomLobbyUI()
        {
            var net = RTSNetworkSyncManager.Instance;
            bool inRoom = (net != null && net.isMultiplayerMode && net.isRoomConnected);
            UnitTeam team = (net != null) ? net.assignedTeam : RTSGameManager.LocalPlayerTeam;
            string roomCode = (net != null && !string.IsNullOrEmpty(net.matchRoomId)) ? net.matchRoomId : "ODA-101";

            if (mpPlayerNameInput != null && string.IsNullOrEmpty(mpPlayerNameInput.text) && net != null)
            {
                mpPlayerNameInput.text = net.localPlayerName;
            }

            if (startScreenRoomInput != null && string.IsNullOrEmpty(startScreenRoomInput.text))
            {
                startScreenRoomInput.text = roomCode;
            }

            // Açık Odalar Listesini Güncelle
            var openRooms = (net != null) ? net.ActiveOpenRooms : null;
            int roomCount = (openRooms != null) ? openRooms.Count : 0;

            if (mpNoOpenRoomsText != null)
            {
                mpNoOpenRoomsText.gameObject.SetActive(roomCount == 0);
            }

            if (mpOpenRoomRowBtns != null && mpOpenRoomRowTexts != null)
            {
                for (int i = 0; i < mpOpenRoomRowBtns.Length; i++)
                {
                    var rowBtn = mpOpenRoomRowBtns[i];
                    var rowTxt = (i < mpOpenRoomRowTexts.Length) ? mpOpenRoomRowTexts[i] : null;
                    if (rowBtn == null) continue;

                    if (i < roomCount)
                    {
                        var rInfo = openRooms[i];
                        rowBtn.gameObject.SetActive(true);
                        bool isOwnHosted = (net != null && rInfo.hostInstanceId == net.clientInstanceId);
                        string stateTag = isOwnHosted
                            ? (rInfo.playerCount >= 2 ? "[SİZİN ODANIZ - 2/2 HAZIR • ODAYI AÇ ►]" : "[SİZİN ODANIZ - 1/2 BEKLENİYOR • ODAYI AÇ ►]")
                            : (rInfo.playerCount >= 2 ? "[DOLU - 2/2]" : $"[{rInfo.playerCount}/2 AÇIK]  •  ODAYA KATIL ►");

                        if (rowTxt != null)
                        {
                            rowTxt.text = $"🏰 {rInfo.roomName}   •   Kurucu: {rInfo.hostName}   •   Harita: {rInfo.mapType}   •   {stateTag}";
                        }

                        var rowImg = rowBtn.GetComponent<Image>();
                        if (rowImg != null)
                        {
                            rowImg.color = isOwnHosted
                                ? new Color(0.14f, 0.42f, 0.26f, 0.95f)
                                : new Color(0.14f, 0.22f, 0.36f, 0.95f);
                        }

                        string targetRoomName = rInfo.roomName;
                        rowBtn.onClick.RemoveAllListeners();
                        rowBtn.onClick.AddListener(() =>
                        {
                            if (RTSNetworkSyncManager.Instance != null)
                            {
                                string pName = (mpPlayerNameInput != null && !string.IsNullOrWhiteSpace(mpPlayerNameInput.text))
                                    ? mpPlayerNameInput.text.Trim()
                                    : RTSNetworkSyncManager.Instance.localPlayerName;
                                if (startScreenRoomInput != null) startScreenRoomInput.text = targetRoomName;
                                RTSNetworkSyncManager.Instance.JoinPhotonRoom(pName, targetRoomName);
                                if (activeRoomModal != null)
                                {
                                    activeRoomModal.SetActive(true);
                                }
                                RTSAudioManager.Instance?.PlaySound(RTSSoundType.TechComplete);
                                UpdatePhotonRoomLobbyUI();
                                UpdateMapSelectionUI();
                            }
                        });
                    }
                    else
                    {
                        rowBtn.gameObject.SetActive(false);
                    }
                }
            }

            // Oda İçi Penceresi (ActiveRoom_Modal) Bilgilerini Güncelle
            if (activeRoomTitleText != null)
            {
                activeRoomTitleText.text = $"🏰  ODA: [{roomCode}]  —  SAVAŞ DİVANI & OYUN AYARLARI";
            }

            string p1Name = (net != null && !string.IsNullOrEmpty(net.hostPlayerName))
                ? net.hostPlayerName
                : (net != null && net.isHost ? net.localPlayerName : "Bekleniyor...");

            bool hasSecondPlayer = (net != null && net.connectedPlayersCount >= 2 && !string.IsNullOrEmpty(net.guestPlayerName));
            string p2Name = hasSecondPlayer ? net.guestPlayerName : "Rakip Oyuncu Bekleniyor...";

            if (activeRoomBlueSlotText != null)
            {
                string youTag = (net != null && net.isHost) ? " (SİZ - KURUCU)" : " (ODA KURUCUSU)";
                activeRoomBlueSlotText.text = $"🔵 1. OYUNCU: MAVİ KRALLIK + MAVİ AI KOMUTAN\n👑 Komutan: {p1Name}{youTag}   •   ✅ HAZIR";
            }

            if (activeRoomRedSlotText != null)
            {
                if (hasSecondPlayer)
                {
                    string youTag = (net != null && !net.isHost) ? " (SİZ - KATILIMCI)" : " (RAKİP KOMUTAN)";
                    activeRoomRedSlotText.text = $"🔴 2. OYUNCU: KIRMIZI KRALLIK + KIRMIZI AI KOMUTAN\n⚔️ Komutan: {p2Name}{youTag}   •   ✅ BAĞLANDI (2/2 HAZIR)";
                    activeRoomRedSlotText.color = new Color(1.0f, 0.88f, 0.88f);
                }
                else
                {
                    activeRoomRedSlotText.text = "🔴 2. OYUNCU: KIRMIZI KRALLIK + KIRMIZI AI KOMUTAN\n⏳ Rakip Oyuncu Bekleniyor... (1/2 — Başka Oyuncu Odaya Katıldığında Dolacak)";
                    activeRoomRedSlotText.color = new Color(0.95f, 0.82f, 0.55f);
                }
            }

            if (activeRoomSimulatePlayerBtn != null)
            {
                activeRoomSimulatePlayerBtn.gameObject.SetActive(!hasSecondPlayer && (net == null || net.isHost));
            }

            if (mpLobbyMapBtn != null)
            {
                mpLobbyMapBtn.interactable = (net == null || !inRoom || net.isHost);
            }

            if (startScreenTeamBtnText != null)
            {
                if (!inRoom)
                {
                    startScreenTeamBtnText.text = "⚖️ ROLÜNÜZ: Oda Açarsanız 🔵 Mavi (Host)  |  Katılırsanız 🔴 Kırmızı (Client)";
                }
                else
                {
                    startScreenTeamBtnText.text = (team == UnitTeam.Enemy)
                        ? $"🔴 SİZİN ROLÜNÜZ ({net.localPlayerName}): 2. OYUNCU — KIRMIZI KRALLIK + KIRMIZI AI KOMUTAN"
                        : $"🔵 SİZİN ROLÜNÜZ ({net.localPlayerName}): 1. OYUNCU (KURUCU) — MAVİ KRALLIK + MAVİ AI KOMUTAN";
                }
            }

            if (startScreenTeamBtn != null)
            {
                // Odadayken roller sabittir (1. Oyuncu Kurucu Mavi, 2. Oyuncu Katılımcı Kırmızı)
                startScreenTeamBtn.interactable = false;
                var img = startScreenTeamBtn.GetComponent<Image>();
                if (img != null)
                {
                    img.color = !inRoom
                        ? new Color(0.18f, 0.20f, 0.26f)
                        : ((team == UnitTeam.Enemy) ? new Color(0.55f, 0.16f, 0.16f) : new Color(0.12f, 0.34f, 0.68f));
                }
            }

            if (startScreenRoomStatusText != null)
            {
                startScreenRoomStatusText.text = (net != null)
                    ? net.roomStatusMessage
                    : "ℹ️ Odaya girmek için yeni bir oda açın veya açık odalardan birine katılın.";
                startScreenRoomStatusText.color = (net != null && net.connectedPlayersCount >= 2)
                    ? new Color(0.45f, 1.0f, 0.55f)
                    : new Color(0.95f, 0.85f, 0.55f);
            }

            // Yalnızca Odayı Kuran Oyuncu (isHost == true) ve 2/2 Hazır olduğunda Oyunu Başlat Butonu aktif olur!
            bool canStartMp = (net != null && net.CanStartMultiplayerMatch());
            if (mpLobbyStartMatchBtn != null)
            {
                mpLobbyStartMatchBtn.interactable = canStartMp;
                var btnImg = mpLobbyStartMatchBtn.GetComponent<Image>();
                if (btnImg != null)
                {
                    btnImg.color = canStartMp
                        ? new Color(0.12f, 0.58f, 0.26f, 1f)
                        : new Color(0.25f, 0.25f, 0.28f, 0.85f);
                }
            }

            if (mpLobbyStartMatchBtnText != null)
            {
                if (!inRoom)
                {
                    mpLobbyStartMatchBtnText.text = "🔒 ÖNCE ODA AÇIN VEYA KATILIN";
                    mpLobbyStartMatchBtnText.color = new Color(0.75f, 0.75f, 0.78f);
                }
                else if (net != null && !net.isHost)
                {
                    mpLobbyStartMatchBtnText.text = (net.connectedPlayersCount >= 2)
                        ? $"⏳ KURUCUNUN ({p1Name}) BAŞLATMASI BEKLENİYOR (2/2)"
                        : "⏳ KURUCU BEKLENİYOR (1/2)";
                    mpLobbyStartMatchBtnText.color = new Color(0.85f, 0.92f, 1.0f);
                }
                else if (!canStartMp)
                {
                    mpLobbyStartMatchBtnText.text = "⏳ RAKİP OYUNCU BEKLENİYOR (1/2)";
                    mpLobbyStartMatchBtnText.color = new Color(1.0f, 0.85f, 0.45f);
                }
                else
                {
                    mpLobbyStartMatchBtnText.text = "⚔️ OYUNU BAŞLAT (2/2 HAZIR) ⚔️";
                    mpLobbyStartMatchBtnText.color = Color.white;
                }
            }
        }

        public void CycleMapSelection()
        {
            var net = RTSNetworkSyncManager.Instance;
            if (net != null && net.isMultiplayerMode && net.isRoomConnected && !net.isHost)
            {
                return; // Çok oyunculu odada haritayı yalnızca odayı kuran oyuncu değiştirebilir
            }
            switch (RTSAutoBootstrap.SelectedMap)
            {
                case RTSMapType.RiverValley:
                    RTSAutoBootstrap.SelectedMap = RTSMapType.OpenSteppe;
                    break;
                case RTSMapType.OpenSteppe:
                    RTSAutoBootstrap.SelectedMap = RTSMapType.BlackForest;
                    break;
                case RTSMapType.BlackForest:
                    RTSAutoBootstrap.SelectedMap = RTSMapType.CrossCorners;
                    break;
                case RTSMapType.CrossCorners:
                    RTSAutoBootstrap.SelectedMap = RTSMapType.RandomMap;
                    break;
                default:
                    RTSAutoBootstrap.SelectedMap = RTSMapType.RiverValley;
                    break;
            }
            UpdateMapSelectionUI();
            RTSAudioManager.Instance?.PlaySound(RTSSoundType.Click);
            // Menüdeyken 3D arkaplanı yeniden inşa etme (yalnızca lobi bilgisini senkronla; harita SEFERİ BAŞLAT'a basılınca kurulur)
            RTSNetworkSyncManager.Instance?.BroadcastMapSelection(RTSAutoBootstrap.SelectedMap);
        }

        public void UpdateMapSelectionUI()
        {
            if (startScreenMapBtnText == null) return;
            switch (RTSAutoBootstrap.SelectedMap)
            {
                case RTSMapType.RiverValley:
                    startScreenMapBtnText.text = "◄   HARİTA: NEHİR VADİSİ   ►";
                    if (startScreenMapDescText != null)
                        startScreenMapDescText.text = "• Coğrafya: 3 Nehir Geçidi (2 Taş Köprü + 1 Sığlık)\n• Engel: Nehir suyu geçilmez doğal kalkandır.\n• Taktik: Boğazları kuleyle tutun, köprü başı savunması ve mancınıkla taarruz yapın!";
                    break;
                case RTSMapType.OpenSteppe:
                    startScreenMapBtnText.text = "◄   HARİTA: AÇIK BOZKIR   ►";
                    if (startScreenMapDescText != null)
                        startScreenMapDescText.text = "• Coğrafya: Geniş, nehirsiz ve engelsiz ova.\n• Hareket: Süvariler serbestçe manevra yapabilir.\n• Taktik: Hızlı akınlar, kanatlardan çevirme ve açık meydan muharebesi yapın!";
                    break;
                case RTSMapType.BlackForest:
                    startScreenMapBtnText.text = "◄   HARİTA: KARA ORMAN   ►";
                    if (startScreenMapDescText != null)
                        startScreenMapDescText.text = "• Coğrafya: Geçit vermez devasa orman kuşağı.\n• Geçitler: Yalnızca 2 dar vadi boğazı mevcuttur.\n• Taktik: Boğazlara sur ve kapı örün, kuleler dikip ağır kuşatmaya hazırlanın!";
                    break;
                case RTSMapType.CrossCorners:
                    startScreenMapBtnText.text = "◄   HARİTA: ÇAPRAZ YAYLA   ►";
                    if (startScreenMapDescText != null)
                        startScreenMapDescText.text = "• Coğrafya: Güney-Batı ve Kuzey-Doğu çapraz üsler.\n• Merkez: Zengin antik tapınak harabeleri & altın.\n• Taktik: Merkez madenlerini erkenden emniyete alın ve uzun intikal seferi başlatın!";
                    break;
                case RTSMapType.RandomMap:
                    startScreenMapBtnText.text = "◄   HARİTA: RASTGELE HARİTA   ►";
                    if (startScreenMapDescText != null)
                        startScreenMapDescText.text = "• Coğrafya: Bilinmeyen sürpriz arazi ve koşullar!\n• Saha: Prosedürel taktiksel coğrafya seçilir.\n• Taktik: Keşif atlısıyla haritayı hızla tarayıp duruma göre strateji kurun!";
                    break;
            }
            if (mpLobbyMapBtnText != null && startScreenMapBtnText != null)
            {
                mpLobbyMapBtnText.text = startScreenMapBtnText.text;
            }
        }

        private void Update()
        {
            // ESC tuşu: Duraklatma menüsü veya açık pencereleri kapatma
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (settingsModal != null && settingsModal.activeSelf)
                {
                    CloseSettingsModal();
                }
                else if (controlsGuideModal != null && controlsGuideModal.activeSelf)
                {
                    controlsGuideModal.SetActive(false);
                }
                else if (activeRoomModal != null && activeRoomModal.activeSelf)
                {
                    RTSNetworkSyncManager.Instance?.LeaveCurrentRoom();
                    activeRoomModal.SetActive(false);
                    UpdatePhotonRoomLobbyUI();
                }
                else if (multiplayerLobbyModal != null && multiplayerLobbyModal.activeSelf)
                {
                    multiplayerLobbyModal.SetActive(false);
                    RTSNetworkSyncManager.Instance?.SetSinglePlayerMode();
                    UpdatePhotonRoomLobbyUI();
                }
                else if (RTSSelectionManager.Instance != null && RTSSelectionManager.Instance.isPlacingBuilding)
                {
                    RTSSelectionManager.Instance.CancelBuildingPlacement();
                }
                else if ((startScreenModal == null || !startScreenModal.activeSelf) &&
                         (gameOverModal == null || !gameOverModal.activeSelf))
                {
                    TogglePause();
                }
            }

            UpdateRaidCountdown();
            UpdateActiveTooltip();

            if (Time.unscaledTime < nextInspectorUpdateTime) return;
            nextInspectorUpdateTime = Time.unscaledTime + 0.1f; // Saniyede 10 kez canlı bilgi güncellemesi

            UpdateInspectorCard();
            UpdateIdleVillagerNotification();
            UpdateVillagerActivityTracker();
        }

        private void UpdateRaidCountdown()
        {
            if (raidTimerText == null) return;

            if (gameManager == null) gameManager = RTSGameManager.Instance;
            if (gameManager == null) return;

            if (startScreenModal != null && startScreenModal.activeSelf)
            {
                raidTimerText.text = "Sefer Beklemede";
                raidTimerText.color = new Color(0.85f, 0.85f, 0.85f);
                return;
            }

            if (gameManager.isGameOver)
            {
                raidTimerText.text = "Savaş Sona Erdi";
                raidTimerText.color = Color.gray;
                return;
            }

            if (RTSGameManager.IsMultiplayerPvP)
            {
                string rCode = (RTSNetworkSyncManager.Instance != null) ? RTSNetworkSyncManager.Instance.matchRoomId : "ODA-101";
                string aiTag = (RTSGameManager.LocalPlayerTeam == UnitTeam.Enemy) ? "Kırmızı AI" : "Mavi AI";
                raidTimerText.text = $"🌐 {rCode} ({aiTag})";
                raidTimerText.color = (RTSGameManager.LocalPlayerTeam == UnitTeam.Enemy)
                    ? new Color(1.0f, 0.55f, 0.50f)
                    : new Color(0.50f, 0.88f, 1.0f);
                return;
            }

            float timeLeft = gameManager.GetTimeUntilNextRaid();
            int nextWave = gameManager.GetCurrentRaidWave() + 1;

            if (timeLeft <= 0.05f)
            {
                raidTimerText.text = $"AKIN BAŞLADI! (Dalga {gameManager.GetCurrentRaidWave()})";
                float pulse = Mathf.PingPong(Time.unscaledTime * 4.5f, 1f);
                raidTimerText.color = Color.Lerp(Color.red, Color.yellow, pulse);
            }
            else
            {
                int min = Mathf.FloorToInt(timeLeft / 60f);
                int sec = Mathf.FloorToInt(timeLeft % 60f);
                raidTimerText.text = $"AKIN: {min:00}:{sec:00} (Dalga {nextWave})";

                if (timeLeft <= 15f)
                {
                    float pulse = Mathf.PingPong(Time.unscaledTime * 5f, 1f);
                    raidTimerText.color = Color.Lerp(new Color(1f, 0.2f, 0.2f), new Color(1f, 0.85f, 0.2f), pulse);
                }
                else if (timeLeft <= 45f)
                {
                    raidTimerText.color = new Color(1f, 0.65f, 0.2f);
                }
                else
                {
                    raidTimerText.color = new Color(1f, 0.88f, 0.35f);
                }
            }
        }

        public void TogglePause()
        {
            if (pauseMenuModal == null) return;
            if (pauseMenuModal.activeSelf)
            {
                ResumeGame();
            }
            else
            {
                pauseMenuModal.SetActive(true);
                Time.timeScale = 0f;
            }
        }

        public void ResumeGame()
        {
            if (settingsModal != null) settingsModal.SetActive(false);
            if (controlsGuideModal != null) controlsGuideModal.SetActive(false);
            if (pauseMenuModal != null) pauseMenuModal.SetActive(false);
            Time.timeScale = 1f;
        }

        /// <summary>
        /// Maçı sonlandırıp ana menüye (Başlangıç Ekranına) döner. Haritayı ve ekonomiyi temiz bir şekilde sıfırlar.
        /// </summary>
        public void ReturnToMainMenu()
        {
            if (settingsModal != null) settingsModal.SetActive(false);
            if (pauseMenuModal != null) pauseMenuModal.SetActive(false);
            if (gameOverModal != null) gameOverModal.SetActive(false);
            if (controlsGuideModal != null) controlsGuideModal.SetActive(false);
            if (activeRoomModal != null) activeRoomModal.SetActive(false);
            if (multiplayerLobbyModal != null) multiplayerLobbyModal.SetActive(false);
            if (inspectorPanel != null) inspectorPanel.SetActive(false);
            HideActionPanel();
            HidePlacementBanner();

            FindFirstObjectByType<ApprovalPanelUI>()?.ClosePanel();

            RTSSelectionManager.Instance?.CancelBuildingPlacement();
            RTSSelectionManager.Instance?.DeselectAll();

            RTSAudioManager.Instance?.ResetCombatAudio();
            RTSAutoBootstrap.Instance?.RebuildMapForSelectedType();
            gameManager?.ResetEconomyForNewMatch();

            Time.timeScale = 0f;
            if (startScreenModal != null)
            {
                startScreenModal.SetActive(true);
            }

            UpdateMapSelectionUI();
            UpdateDifficultyButtonText();
            SetAiStatus("[ANA MENÜ] Harita ve zorluk seçip Seferi Başlat'a tıklayın.", Color.cyan);
        }

        /// <summary>
        /// Mevcut haritada maçı sıfırdan yeniden başlatır.
        /// </summary>
        public void RestartCurrentMatch()
        {
            if (pauseMenuModal != null) pauseMenuModal.SetActive(false);
            if (gameOverModal != null) gameOverModal.SetActive(false);
            if (controlsGuideModal != null) controlsGuideModal.SetActive(false);
            if (inspectorPanel != null) inspectorPanel.SetActive(false);
            HideActionPanel();
            HidePlacementBanner();

            FindFirstObjectByType<ApprovalPanelUI>()?.ClosePanel();

            RTSSelectionManager.Instance?.CancelBuildingPlacement();
            RTSSelectionManager.Instance?.DeselectAll();

            RTSAudioManager.Instance?.ResetCombatAudio();
            RTSAutoBootstrap.Instance?.RebuildMapForSelectedType();
            Time.timeScale = 1f;
            gameManager?.StartMatch();
            RTSAudioManager.Instance?.PlaySound(RTSSoundType.WarHorn);
            SetAiStatus("[SEFER YENİDEN BAŞLATILDI] Şehir Merkezinizi koruyun, köylülerle ekonomi kurun ve düşmanı fethedin!", Color.white);
        }

        private void UpdateInspectorCard()
        {
            if (inspectorPanel == null) return;

            var selMgr = RTSSelectionManager.Instance;
            if (selMgr == null)
            {
                inspectorPanel.SetActive(false);
                return;
            }

            // Ölü/geçersiz birimleri seçimden temizle
            selMgr.selectedUnits.RemoveAll(u => u == null || u.currentHp <= 0);

            // Seçim varsa Can Barı aktif olsun (boşta iken aşağıda gizlenecek)
            if (inspectorHpFill != null && inspectorHpFill.parent != null)
            {
                inspectorHpFill.parent.gameObject.SetActive(true);
            }

            // 1. Durum: Tek dost birim seçili
            if (selMgr.selectedUnits.Count == 1)
            {
                var unit = selMgr.selectedUnits[0];
                inspectorPanel.SetActive(true);
                string uType = unit.unitType.ToLower();

                if (inspectorHeraldicBadgeText != null)
                {
                    inspectorHeraldicBadgeText.text = (uType == "spearman") ? "MIZRAK\nMUHAFIZI" :
                                                      (uType == "swordsman") ? "KILIÇ\nUSTASI" :
                                                      (uType == "archer") ? "KRALİYET\nOKÇUSU" :
                                                      (uType == "crossbowman") ? "AĞIR\nARBALET" :
                                                      (uType == "cavalry") ? "ŞÖVALYE\nSÜVARİ" :
                                                      (uType == "scout") ? "KEŞİF\nATLISI" :
                                                      (uType == "trebuchet") ? "AĞIR\nMANCINIK" :
                                                      (uType == "villager") ? "KÖY\nİŞÇİSİ" : "SAVAŞÇI\nBİRLİK";
                }

                if (inspectorTitleText != null)
                {
                    inspectorTitleText.color = Color.cyan;
                    inspectorTitleText.text = (uType == "spearman") ? "Mızrak Muhafızı" :
                                              (uType == "swordsman") ? "Kılıç Ustası" :
                                              (uType == "archer") ? "İmparatorluk Okçusu" :
                                              (uType == "crossbowman") ? "Ağır Arbaletçi" :
                                              (uType == "cavalry") ? "Şövalye Süvarisi" :
                                              (uType == "scout") ? "Keşif Atlısı" :
                                              (uType == "trebuchet") ? "Kuşatma Mancınığı" :
                                              (uType == "villager") ? "Köy İşçisi" : $"{char.ToUpper(uType[0]) + uType.Substring(1)}";
                }

                if (inspectorSubtitleText != null)
                {
                    inspectorSubtitleText.text = (uType == "spearman") ? "Piyade • Süvarilere Karşı +18 Bonus Hasar" :
                                                 (uType == "swordsman") ? "Ağır Piyade • Yüksek Zırh ve Yakın Dövüş Gücü" :
                                                 (uType == "archer") ? "Menzilli • Mızrakçılara Karşı +8 Bonus Hasar" :
                                                 (uType == "crossbowman") ? "Ağır Menzilli • Yüksek Delici Hasar (Zırh Delici)" :
                                                 (uType == "cavalry") ? "Hücum • Okçulara Karşı +12 Bonus Hasar" :
                                                 (uType == "scout") ? "Hafif Süvari • 24m Geniş Keşif Görüş Alanı" :
                                                 (uType == "trebuchet") ? "Kuşatma • Binalara Karşı +60 Devasa Yıkım Hasarı" :
                                                 (uType == "villager") ? "İşçi • Kaynak Toplama & İnşaat Uzmanı" : "Askeri Birlik";
                }

                float pct = Mathf.Clamp01((float)unit.currentHp / unit.maxHp);
                if (inspectorHpText != null)
                {
                    int pctInt = Mathf.RoundToInt(pct * 100f);
                    inspectorHpText.text = $"Can: {unit.currentHp} / {unit.maxHp}  (%{pctInt})";
                }

                if (inspectorHpFill != null)
                {
                    inspectorHpFill.localScale = new Vector3(pct, 1f, 1f);
                    var img = inspectorHpFill.GetComponent<Image>();
                    if (img != null)
                    {
                        img.color = (pct > 0.6f) ? new Color(0.2f, 0.85f, 0.35f, 0.95f) :
                                    (pct > 0.25f) ? new Color(0.95f, 0.7f, 0.15f, 0.95f) :
                                    new Color(0.95f, 0.2f, 0.2f, 0.95f);
                    }
                }

                if (inspectorStatsText != null)
                {
                    inspectorStatsText.text = $"Hasar: {unit.attack}   |   Zırh: {unit.armor}   |   Menzil: {unit.attackRange:F1}m   |   Hız: {unit.moveSpeed:F1}";
                }

                if (inspectorExtraText != null)
                {
                    var villager = unit.GetComponent<RTSVillager>();
                    if (villager != null)
                    {
                        string rName = (villager.carryingType == RTSResourceType.Wood) ? "Odun" : (villager.carryingType == RTSResourceType.Gold) ? "Altın" : "Yiyecek";
                        string stateName = (villager.currentState == VillagerState.Harvesting) ? "Kaynak Topluyor" :
                                           (villager.currentState == VillagerState.ReturningToTownCenter) ? "Şehir Merkezine Taşıyor" :
                                           (villager.currentState == VillagerState.Building) ? "İnşa Ediyor" :
                                           (villager.currentState == VillagerState.Repairing) ? "Tamir Ediyor" :
                                           (villager.currentState == VillagerState.WalkingToRepair) ? "Tamirata Gidiyor" :
                                           (villager.currentState == VillagerState.WalkingToResource || villager.currentState == VillagerState.WalkingToBuild) ? "Yolda" : "Boşta (Bekliyor)";
                        inspectorExtraText.text = $"Çanta: {villager.carriedAmount}/{villager.maxCapacity} {rName}   •   Durum: {stateName}";
                    }
                    else
                    {
                        inspectorExtraText.text = "Haritada veya mini-haritada sağ tıklayarak intikal ve taarruz emri verin.";
                    }
                }
            }
            // 2. Durum: Birden fazla birim seçili (Müfreze / Ordu)
            else if (selMgr.selectedUnits.Count > 1)
            {
                inspectorPanel.SetActive(true);
                int totalHp = 0;
                int totalMaxHp = 0;
                int spearCount = 0, swordCount = 0, archerCount = 0, xbowCount = 0, cavCount = 0, scoutCount = 0, trebCount = 0, vilCount = 0;

                foreach (var u in selMgr.selectedUnits)
                {
                    if (u != null && u.currentHp > 0)
                    {
                        totalHp += u.currentHp;
                        totalMaxHp += u.maxHp;
                        string t = u.unitType.ToLower();
                        if (t == "spearman") spearCount++;
                        else if (t == "swordsman") swordCount++;
                        else if (t == "archer") archerCount++;
                        else if (t == "crossbowman") xbowCount++;
                        else if (t == "cavalry") cavCount++;
                        else if (t == "scout") scoutCount++;
                        else if (t == "trebuchet") trebCount++;
                        else if (t == "villager") vilCount++;
                    }
                }

                if (inspectorHeraldicBadgeText != null)
                {
                    inspectorHeraldicBadgeText.text = "ORDU\nGRUBU";
                }

                if (inspectorTitleText != null)
                {
                    inspectorTitleText.color = Color.cyan;
                    inspectorTitleText.text = $"Birlik Müfrezesi ({selMgr.selectedUnits.Count} Asker)";
                }

                if (inspectorSubtitleText != null)
                {
                    string breakdown = "";
                    if (spearCount > 0) breakdown += $"{spearCount} Mızrakçı  ";
                    if (swordCount > 0) breakdown += $"{swordCount} Kılıçlı  ";
                    if (archerCount > 0) breakdown += $"{archerCount} Okçu  ";
                    if (xbowCount > 0) breakdown += $"{xbowCount} Arbaletçi  ";
                    if (cavCount > 0) breakdown += $"{cavCount} Süvari  ";
                    if (scoutCount > 0) breakdown += $"{scoutCount} Keşif Atlısı  ";
                    if (trebCount > 0) breakdown += $"{trebCount} Mancınık  ";
                    if (vilCount > 0) breakdown += $"{vilCount} Köylü  ";
                    inspectorSubtitleText.text = breakdown.Trim();
                }

                float pct = totalMaxHp > 0 ? Mathf.Clamp01((float)totalHp / totalMaxHp) : 0;
                if (inspectorHpText != null)
                {
                    int pctInt = Mathf.RoundToInt(pct * 100f);
                    inspectorHpText.text = $"Toplam Ordu Gücü: {totalHp} / {totalMaxHp}  (%{pctInt})";
                }

                if (inspectorHpFill != null)
                {
                    inspectorHpFill.localScale = new Vector3(pct, 1f, 1f);
                    var img = inspectorHpFill.GetComponent<Image>();
                    if (img != null)
                    {
                        img.color = (pct > 0.6f) ? new Color(0.2f, 0.85f, 0.35f, 0.95f) :
                                    (pct > 0.25f) ? new Color(0.95f, 0.7f, 0.15f, 0.95f) :
                                    new Color(0.95f, 0.2f, 0.2f, 0.95f);
                    }
                }

                if (inspectorStatsText != null)
                {
                    inspectorStatsText.text = "Karma Askeri Formasyon   |   Toplu Taarruz ve Savunma";
                }

                if (inspectorExtraText != null)
                {
                    inspectorExtraText.text = "Mini-haritaya veya araziye sağ tıklayarak tüm müfrezeyi sevk edin.";
                }
            }
            // 3. Durum: Bir dost bina seçili
            else if (selMgr.selectedBuilding != null)
            {
                var b = selMgr.selectedBuilding;
                if (b == null || b.currentHp <= 0)
                {
                    inspectorPanel.SetActive(false);
                    return;
                }

                inspectorPanel.SetActive(true);

                if (inspectorHeraldicBadgeText != null)
                {
                    inspectorHeraldicBadgeText.text = (b.buildingType == RTSBuildingType.TownCenter) ? "ŞEHİR\nMERKEZİ" :
                                                      (b.buildingType == RTSBuildingType.Barracks) ? "ASKERİ\nKIŞLA" :
                                                      (b.buildingType == RTSBuildingType.DefenseTower) ? "SAVUNMA\nKULESİ" :
                                                      (b.buildingType == RTSBuildingType.House) ? "AHŞAP\nEV" :
                                                      (b.buildingType == RTSBuildingType.Farm) ? "BUĞDAY\nTARLASI" :
                                                      (b.buildingType == RTSBuildingType.StoneWall) ? "TAŞ\nDUVAR" :
                                                      (b.buildingType == RTSBuildingType.CityGate) ? "ŞEHİR\nKAPISI" :
                                                      (b.buildingType == RTSBuildingType.Castle) ? "BÜYÜK\nKALE" :
                                                      (b.buildingType == RTSBuildingType.ArcheryRange) ? "OKÇU\nTALİMGÂH" :
                                                      (b.buildingType == RTSBuildingType.SiegeWorkshop) ? "KUŞATMA\nATÖLYESİ" :
                                                      (b.buildingType == RTSBuildingType.Market) ? "PAZAR\nYERİ" : "DEMİRCİ\nOCAĞI";
                }

                if (inspectorTitleText != null)
                {
                    inspectorTitleText.color = Color.cyan;
                    inspectorTitleText.text = (b.buildingType == RTSBuildingType.TownCenter) ? "Şehir Merkezi (Karargah)" :
                                              (b.buildingType == RTSBuildingType.Barracks) ? "Kışla (Askeri Üretim)" :
                                              (b.buildingType == RTSBuildingType.DefenseTower) ? "Savunma Kulesi (Ok Atar)" :
                                              (b.buildingType == RTSBuildingType.House) ? "Ahşap Ev (Nüfus Barınağı)" :
                                              (b.buildingType == RTSBuildingType.Farm) ? "Buğday Tarlası (Gıda)" :
                                              (b.buildingType == RTSBuildingType.StoneWall) ? "Taş Savunma Duvarı" :
                                              (b.buildingType == RTSBuildingType.CityGate) ? "Şehir ve Kale Kapısı" :
                                              (b.buildingType == RTSBuildingType.Castle) ? "Şanlı İmparatorluk Kalesi" : "Demirci Atölyesi";
                }

                if (inspectorSubtitleText != null)
                {
                    string sub = b.isConstructed ? "Tamamlanmış Askeri / Sivil Yapı" : $"İnşa Halinde: %{b.constructionProgress:F0}";
                    if (b.buildingType == RTSBuildingType.House) sub = b.isConstructed ? "Sivil Yapı • Nüfus Kapasitesini +5 Artırır" : $"İnşa Halinde: %{b.constructionProgress:F0}";
                    else if (b.buildingType == RTSBuildingType.Farm) sub = b.isConstructed ? "Tarım Parseli • Sürekli Gıda Üretim Kaynağı" : $"İnşa Halinde: %{b.constructionProgress:F0}";
                    else if (b.buildingType == RTSBuildingType.StoneWall) sub = b.isConstructed ? "Taş Tahkimat • Düşman Geçişini Engeller" : $"İnşa Halinde: %{b.constructionProgress:F0}";
                    else if (b.buildingType == RTSBuildingType.CityGate) sub = b.isConstructed ? "Demir Parmaklıklı Kapı • Giriş/Çıkış Geçidi" : $"İnşa Halinde: %{b.constructionProgress:F0}";
                    else if (b.buildingType == RTSBuildingType.Castle) sub = b.isConstructed ? "Büyük Kale • Şövalye & Mancınık Karargahı" : $"İnşa Halinde: %{b.constructionProgress:F0}";
                    inspectorSubtitleText.text = sub;
                }

                float pct = Mathf.Clamp01((float)b.currentHp / b.maxHp);
                if (inspectorHpText != null)
                {
                    int pctInt = Mathf.RoundToInt(pct * 100f);
                    inspectorHpText.text = $"Dayanıklılık: {b.currentHp} / {b.maxHp}  (%{pctInt})";
                }

                if (inspectorHpFill != null)
                {
                    inspectorHpFill.localScale = new Vector3(pct, 1f, 1f);
                    var img = inspectorHpFill.GetComponent<Image>();
                    if (img != null)
                    {
                        img.color = (pct > 0.6f) ? new Color(0.2f, 0.85f, 0.35f, 0.95f) :
                                    (pct > 0.25f) ? new Color(0.95f, 0.7f, 0.15f, 0.95f) :
                                    new Color(0.95f, 0.2f, 0.2f, 0.95f);
                    }
                }

                if (inspectorStatsText != null)
                {
                    if (b.buildingType == RTSBuildingType.DefenseTower || b.buildingType == RTSBuildingType.Castle)
                    {
                        inspectorStatsText.text = $"Ok Hasarı: {b.towerDamage}   |   Menzil: {b.attackRange:F0}m   |   Atış Hızı: {b.attackCooldown:F1}s";
                    }
                    else if (b.buildingType == RTSBuildingType.StoneWall)
                    {
                        inspectorStatsText.text = "Zırh: Masif Taş   |   Geçiş: Kapalı   |   Tür: Savunma Duvarı";
                    }
                    else if (b.buildingType == RTSBuildingType.CityGate)
                    {
                        inspectorStatsText.text = "Geçiş: Kontrollü Kapı   |   Zırh: Demir & Taş   |   Tür: Şehir Kapısı";
                    }
                    else if (b.buildingType == RTSBuildingType.House)
                    {
                        inspectorStatsText.text = "Nüfus Katkısı: +5 Nüfus   |   Takım: Dost   |   Tür: Barınak";
                    }
                    else if (b.buildingType == RTSBuildingType.ArcheryRange)
                    {
                        inspectorStatsText.text = "Tür: Okçu Kışlası   |   Üretim: Okçu & Arbaletçi   |   Geliştirme: Ok Temreni";
                    }
                    else if (b.buildingType == RTSBuildingType.SiegeWorkshop)
                    {
                        inspectorStatsText.text = "Tür: Kuşatma Atölyesi   |   Üretim: Kuşatma Mancınığı   |   Hasar: Ağır Bina Yıkımı";
                    }
                    else if (b.buildingType == RTSBuildingType.Market)
                    {
                        inspectorStatsText.text = "Tür: Pazar Yeri   |   İşlev: Odun, Yiyecek & Altın Ticareti / Kaynak Takası";
                    }
                    else
                    {
                        inspectorStatsText.text = $"Görüş Alanı: {b.sightRadius:F0}m   |   Takım: {b.team}";
                    }
                }

                if (inspectorExtraText != null)
                {
                    if (b.buildingType == RTSBuildingType.Castle)
                    {
                        inspectorExtraText.text = "Krallığın en görkemli kalesi. Ağır Şövalye ve Kuşatma Mancınığı eğitebilir.";
                    }
                    else if (b.buildingType == RTSBuildingType.StoneWall)
                    {
                        inspectorExtraText.text = "Düşman akıncılarını durdurmak için stratejik boğazlara ve üs çevresine inşa edin.";
                    }
                    else if (b.buildingType == RTSBuildingType.CityGate)
                    {
                        inspectorExtraText.text = "Taş duvar hatlarınızın ortasında ordunuzun çıkışı için korunaklı bir kapı sağlar.";
                    }
                    else if (b.buildingType == RTSBuildingType.House)
                    {
                        inspectorExtraText.text = "Her tamamlanan ev, ordunuz ve ekonominiz için +5 nüfus tavanı sağlar.";
                    }
                    else if (b.buildingType == RTSBuildingType.Farm)
                    {
                        var res = b.GetComponent<RTSResourceSource>();
                        int rem = (res != null) ? res.remainingAmount : 1500;
                        inspectorExtraText.text = $"Tarla Rezervi: {rem} Yiyecek. Köylüleri sağ tıklatarak hasada atayın.";
                    }
                    else if (b.buildingType == RTSBuildingType.TownCenter)
                    {
                        inspectorExtraText.text = "Köylülerin kaynak depolama ve üs merkezi. Buradan yeni köylü eğitebilirsiniz.";
                    }
                    else if (b.buildingType == RTSBuildingType.ArcheryRange)
                    {
                        inspectorExtraText.text = "Düşman piyadelerini ve köylülerini uzaktan yıpratmak için menzilli okçular eğitin.";
                    }
                    else if (b.buildingType == RTSBuildingType.SiegeWorkshop)
                    {
                        inspectorExtraText.text = "Düşman surlarını ve kulelerini güvenli menzilden yerle bir etmek için mancınık üretin.";
                    }
                    else if (b.buildingType == RTSBuildingType.Market)
                    {
                        inspectorExtraText.text = "Fazla kaynaklarınızı altına çevirin veya eksik kaynakları anında satın alın.";
                    }
                    else
                    {
                        inspectorExtraText.text = "Üretim ve araştırmalar için alttaki Aksiyon Menüsünü kullanın.";
                    }
                }
            }
            // 4. Durum: Düşman birimi inceleniyor
            else if (selMgr.inspectedEnemyUnit != null && selMgr.inspectedEnemyUnit.currentHp > 0)
            {
                var enemy = selMgr.inspectedEnemyUnit;
                inspectorPanel.SetActive(true);
                string uType = enemy.unitType.ToLower();

                if (inspectorHeraldicBadgeText != null)
                {
                    inspectorHeraldicBadgeText.text = "DÜŞMAN\nBİRLİK";
                }

                if (inspectorTitleText != null)
                {
                    string name = (uType == "cavalry") ? "Düşman Akıncı Süvarisi" :
                                  (uType == "scout") ? "Düşman Keşif Atlısı" :
                                  (uType == "archer") ? "Düşman Okçusu" :
                                  (uType == "crossbowman") ? "Düşman Arbaletçisi" :
                                  (uType == "swordsman") ? "Düşman Kılıç Ustası" :
                                  (uType == "spearman") ? "Düşman Mızrakçısı" :
                                  (uType == "trebuchet") ? "Düşman Mancınığı" :
                                  (uType == "villager") ? "Düşman Köylüsü" : "Düşman Askeri";
                    inspectorTitleText.text = $"[DÜŞMAN] {name}";
                    inspectorTitleText.color = new Color(1f, 0.35f, 0.35f);
                }

                if (inspectorSubtitleText != null)
                {
                    inspectorSubtitleText.text = (uType == "cavalry") ? "Kırmızı Takım • Hızlı & Tehlikeli Baskın Birlikleri" :
                                                 (uType == "trebuchet") ? "Kırmızı Takım • Ağır Kuşatma Silahı" :
                                                 (uType == "crossbowman" || uType == "archer") ? "Kırmızı Takım • Menzilli Okçu Birlikleri" :
                                                 "Kırmızı Takım • Düşman Kuvvetleri";
                }

                float pct = Mathf.Clamp01((float)enemy.currentHp / enemy.maxHp);
                if (inspectorHpText != null)
                {
                    int pctInt = Mathf.RoundToInt(pct * 100f);
                    inspectorHpText.text = $"Düşman Canı: {enemy.currentHp} / {enemy.maxHp}  (%{pctInt})";
                }

                if (inspectorHpFill != null)
                {
                    inspectorHpFill.localScale = new Vector3(pct, 1f, 1f);
                    var img = inspectorHpFill.GetComponent<Image>();
                    if (img != null) img.color = new Color(0.95f, 0.2f, 0.2f, 0.95f);
                }

                if (inspectorStatsText != null)
                {
                    inspectorStatsText.text = $"Hasar: {enemy.attack}   |   Zırh: {enemy.armor}   |   Menzil: {enemy.attackRange:F1}m   |   Hız: {enemy.moveSpeed:F1}";
                }

                if (inspectorExtraText != null)
                {
                    inspectorExtraText.text = "Bu birim düşmandır! Askerlerinizi seçip üzerine sağ tıklayarak saldırı emri verin.";
                }
            }
            // 5. Durum: Düşman binası inceleniyor
            else if (selMgr.inspectedEnemyBuilding != null)
            {
                var eb = selMgr.inspectedEnemyBuilding;
                if (eb == null || eb.currentHp <= 0)
                {
                    inspectorPanel.SetActive(false);
                    return;
                }

                inspectorPanel.SetActive(true);

                if (inspectorHeraldicBadgeText != null)
                {
                    inspectorHeraldicBadgeText.text = "DÜŞMAN\nYAPI";
                }

                if (inspectorTitleText != null)
                {
                    inspectorTitleText.text = $"[DÜŞMAN] {eb.displayName}";
                    inspectorTitleText.color = new Color(1f, 0.35f, 0.35f);
                }

                if (inspectorSubtitleText != null)
                {
                    inspectorSubtitleText.text = "Kırmızı Takım Askeri Yapısı";
                }

                float pct = Mathf.Clamp01((float)eb.currentHp / eb.maxHp);
                if (inspectorHpText != null)
                {
                    int pctInt = Mathf.RoundToInt(pct * 100f);
                    inspectorHpText.text = $"Bina Dayanıklılığı: {eb.currentHp} / {eb.maxHp}  (%{pctInt})";
                }

                if (inspectorHpFill != null)
                {
                    inspectorHpFill.localScale = new Vector3(pct, 1f, 1f);
                    var img = inspectorHpFill.GetComponent<Image>();
                    if (img != null) img.color = new Color(0.95f, 0.2f, 0.2f, 0.95f);
                }

                if (inspectorStatsText != null)
                {
                    inspectorStatsText.text = $"Tip: {eb.buildingType}   |   Takım: Düşman";
                }

                if (inspectorExtraText != null)
                {
                    inspectorExtraText.text = "Kuşatma için askerlerinizi binaya yönlendirip imha edin!";
                }
            }
            else
            {
                // Hiçbir şey seçili değilse: Krallık Genel Durumu
                if (inspectorPanel != null) inspectorPanel.SetActive(true);
                if (inspectorHpFill != null && inspectorHpFill.parent != null)
                {
                    inspectorHpFill.parent.gameObject.SetActive(false);
                }

                if (inspectorHeraldicBadgeText != null)
                {
                    inspectorHeraldicBadgeText.text = "KRALİYET\nORDUSU";
                }

                int milCount = 0;
                int vilCount = 0;
                UnitTeam myTeam = RTSGameManager.LocalPlayerTeam;
                foreach (var u in RTSUnit.AllUnits)
                {
                    if (u != null && u.team == myTeam && u.currentHp > 0)
                    {
                        if (u.unitType.ToLower() == "villager") vilCount++;
                        else milCount++;
                    }
                }

                if (inspectorTitleText != null)
                {
                    inspectorTitleText.text = "İMPARATORLUK KARARGAHI";
                    inspectorTitleText.color = new Color(1f, 0.88f, 0.35f);
                }
                if (inspectorSubtitleText != null)
                {
                    inspectorSubtitleText.text = $"Nüfus Dağılımı: {vilCount} İşçi  •  {milCount} Askeri Birlik  •  (Seçim yapmak için birim veya binaya tıklayın)";
                    inspectorSubtitleText.color = new Color(0.85f, 0.90f, 0.98f);
                }
                if (inspectorStatsText != null)
                {
                    inspectorStatsText.text = "KONTROLLER: [Sol Tık]: Seçim  •  [Sürükle]: Çoklu Seçim  •  [Sağ Tık]: Hareket / Hasat / İnşa";
                }
                if (inspectorExtraText != null)
                {
                    inspectorExtraText.text = "Kuzey ve Güney toprakları nehirle ayrılmıştır. Köprü ve sığlıkları kontrol edin!";
                }
            }
        }

        private void UpdateIdleVillagerNotification()
        {
            if (idleVillagerBtn == null) return;

            int idleCount = 0;
            UnitTeam myTeam = RTSGameManager.LocalPlayerTeam;
            foreach (var u in RTSUnit.AllUnits)
            {
                if (u != null && u.team == myTeam && u.unitType.ToLower() == "villager" && u.currentHp > 0)
                {
                    var v = u.GetComponent<RTSVillager>();
                    if (v != null && v.currentState == VillagerState.Idle)
                    {
                        idleCount++;
                    }
                }
            }

            idleVillagerBtn.gameObject.SetActive(true);
            if (idleVillagerBtnText != null)
            {
                idleVillagerBtnText.text = $"[F4] Boşta: {idleCount}";
            }

            var btnImg = idleVillagerBtn.GetComponent<Image>();
            if (btnImg != null)
            {
                btnImg.color = (idleCount > 0) 
                    ? new Color(0.95f, 0.45f, 0.1f, 0.95f) 
                    : new Color(0.2f, 0.25f, 0.3f, 0.85f);
            }
        }

        private void UpdateVillagerActivityTracker()
        {
            if (villagerActivityText == null) return;

            int countFood = 0;
            int countWood = 0;
            int countGold = 0;
            int countBuild = 0;
            int countIdle = 0;
            int total = 0;

            UnitTeam myTeam = RTSGameManager.LocalPlayerTeam;
            foreach (var u in RTSUnit.AllUnits)
            {
                if (u != null && u.team == myTeam && u.unitType.ToLower() == "villager" && u.currentHp > 0)
                {
                    total++;
                    var v = u.GetComponent<RTSVillager>();
                    if (v == null || v.currentState == VillagerState.Idle)
                    {
                        countIdle++;
                    }
                    else if (v.currentState == VillagerState.Building || v.currentState == VillagerState.WalkingToBuild)
                    {
                        countBuild++;
                    }
                    else if (v.currentState == VillagerState.Harvesting || v.currentState == VillagerState.WalkingToResource || v.currentState == VillagerState.ReturningToTownCenter)
                    {
                        if (v.carryingType == RTSResourceType.Wood) countWood++;
                        else if (v.carryingType == RTSResourceType.Gold) countGold++;
                        else countFood++;
                    }
                    else
                    {
                        countIdle++;
                    }
                }
            }

            this.villagerTotal = total;
            this.villagerFood = countFood;
            this.villagerWood = countWood;
            this.villagerGold = countGold;
            this.villagerBuild = countBuild;
            this.villagerIdle = countIdle;

            villagerActivityText.text = $"{total}";
        }

        public void RequestPlanFromAI(string prompt)
        {
            if (gatewayClient == null)
            {
                Debug.LogError("[RTSHUDController] AiGatewayClient referansı eksik!");
                return;
            }

            AiSafeSnapshot liveSnapshot = (gameManager != null) ? gameManager.GenerateLiveSnapshot() : AiSafeSnapshot.CreateMockSnapshot();
            gatewayClient.RequestPlan(prompt, liveSnapshot);
        }

        public void UpdateResourceUI(int wood, int food, int gold, int currentPop, int maxPop = 10)
        {
            this.currentWood = wood;
            this.currentFood = food;
            this.currentGold = gold;
            this.currentPop = currentPop;
            this.currentMaxPop = maxPop;

            if (woodText != null) woodText.text = $"{wood}";
            if (foodText != null) foodText.text = $"{food}";
            if (goldText != null) goldText.text = $"{gold}";
            if (armyText != null)
            {
                armyText.text = $"{currentPop}/{maxPop}";
                armyText.color = (currentPop >= maxPop) ? new Color(1f, 0.35f, 0.35f) : new Color(0.92f, 0.95f, 1f);
            }
            UpdateAgeBadge();
        }

        public void UpdateAgeBadge()
        {
            if (ageBadgeText == null) return;
            var techMgr = RTSTechManager.Instance;
            var age = (techMgr != null) ? techMgr.currentAge : RTSAge.DarkAge;
            switch (age)
            {
                case RTSAge.DarkAge:
                    ageBadgeText.text = "I. KARANLIK ÇAĞ";
                    ageBadgeText.color = new Color(0.92f, 0.88f, 0.80f);
                    break;
                case RTSAge.FeudalAge:
                    ageBadgeText.text = "II. FEODAL ÇAĞ";
                    ageBadgeText.color = new Color(1.0f, 0.85f, 0.28f);
                    break;
                case RTSAge.CastleAge:
                    ageBadgeText.text = "III. KALE ÇAĞI";
                    ageBadgeText.color = new Color(0.45f, 0.95f, 1.0f);
                    break;
            }
        }

        public void SetAiStatus(string status, Color color)
        {
            if (aiStatusText != null)
            {
                aiStatusText.text = status;
                aiStatusText.color = color;
            }
        }

        #region Global Alt Metin (Hover Tooltip) Sistemi

        private void UpdateActiveTooltip()
        {
            if (globalTooltipBox != null && globalTooltipBox.activeSelf)
            {
                if (currentTooltipTitleFunc != null && globalTooltipTitle != null)
                {
                    string newTitle = currentTooltipTitleFunc();
                    if (globalTooltipTitle.text != newTitle) globalTooltipTitle.text = newTitle;
                }
                if (currentTooltipBodyFunc != null && globalTooltipBody != null)
                {
                    string newBody = currentTooltipBodyFunc();
                    if (globalTooltipBody.text != newBody) globalTooltipBody.text = newBody;
                }
            }
        }

        public void ShowTooltip(string title, string body, RectTransform targetRt = null, System.Func<string> dynamicTitle = null, System.Func<string> dynamicBody = null)
        {
            if (globalTooltipBox == null) return;

            currentTooltipTitleFunc = dynamicTitle;
            currentTooltipBodyFunc = dynamicBody;

            string initialTitle = dynamicTitle != null ? dynamicTitle() : title;
            string initialBody = dynamicBody != null ? dynamicBody() : body;

            if (globalTooltipTitle != null) globalTooltipTitle.text = initialTitle;
            if (globalTooltipBody != null) globalTooltipBody.text = initialBody;

            // Kompakt ve şık yükseklik hesaplama (Oyun alanını kapatmayacak ince şerit)
            int lines = string.IsNullOrEmpty(initialBody) ? 1 : initialBody.Split('\n').Length;
            float targetHeight = 28f + (lines * 16f) + 8f;
            if (globalTooltipRt != null)
            {
                float targetWidth = 300f;
                globalTooltipRt.sizeDelta = new Vector2(targetWidth, targetHeight);
                if (globalTooltipBody != null)
                {
                    globalTooltipBody.rectTransform.sizeDelta = new Vector2(targetWidth - 16f, targetHeight - 26f);
                }

                // Canvas RectTransform referansını güvenle al (Tooltip'in doğrudan ebeveyni Canvas'tır)
                RectTransform canvasRt = canvasRectTransform;
                if (canvasRt == null && globalTooltipRt != null && globalTooltipRt.parent != null)
                {
                    canvasRt = globalTooltipRt.parent as RectTransform;
                }
                if (canvasRt == null && globalTooltipRt != null)
                {
                    var c = globalTooltipRt.GetComponentInParent<Canvas>();
                    if (c != null) canvasRt = c.GetComponent<RectTransform>();
                }
                if (canvasRt == null)
                {
                    var anyCanvas = FindFirstObjectByType<Canvas>();
                    if (anyCanvas != null) canvasRt = anyCanvas.GetComponent<RectTransform>();
                }

                float targetX = 0f;
                if (targetRt != null && canvasRt != null)
                {
                    // Hedef UI bileşeninin 4 köşe noktasını (Screen Pixels) al -> (0+2)/2 tam ekran merkezi
                    Vector3[] corners = new Vector3[4];
                    targetRt.GetWorldCorners(corners);
                    Vector2 screenCenter = (corners[0] + corners[2]) * 0.5f;

                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screenCenter, null, out Vector2 localCenter))
                    {
                        targetX = localCenter.x;
                    }
                    else
                    {
                        Vector3 worldCenter = targetRt.TransformPoint(targetRt.rect.center);
                        targetX = canvasRt.InverseTransformPoint(worldCenter).x;
                    }
                }
                else if (canvasRt != null)
                {
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, Input.mousePosition, null, out Vector2 mousePoint))
                    {
                        targetX = mousePoint.x;
                    }
                }

                float canvasHalfWidth = (canvasRt != null && canvasRt.rect.width > 0) ? (canvasRt.rect.width * 0.5f) : 960f;
                float halfW = targetWidth * 0.5f;
                float clampedX = Mathf.Clamp(targetX, -canvasHalfWidth + halfW + 12f, canvasHalfWidth - halfW - 12f);

                // DAİMA ÜST BARIN HEMEN ALTINA SABİTLENİR (-56f), BAKILAN BİLGİNİN TAM ALTINDA HİZALANIR
                globalTooltipRt.anchorMin = new Vector2(0.5f, 1f);
                globalTooltipRt.anchorMax = new Vector2(0.5f, 1f);
                globalTooltipRt.pivot = new Vector2(0.5f, 1f);
                globalTooltipRt.anchoredPosition = new Vector2(clampedX, -56f);
                globalTooltipRt.SetAsLastSibling();
            }

            globalTooltipBox.SetActive(true);
        }

        public void HideTooltip()
        {
            if (globalTooltipBox != null)
            {
                globalTooltipBox.SetActive(false);
            }
            currentTooltipTitleFunc = null;
            currentTooltipBodyFunc = null;
        }

        #endregion

        #region Alt Aksiyon Menüsü (İnşaat / Eğitim / Araştırma)

        public void HideActionPanel()
        {
            if (bottomActionPanel != null)
            {
                bottomActionPanel.SetActive(true);
                if (actionPanelTitle != null) actionPanelTitle.text = "İMPARATORLUK EMRİ BEKLENİYOR";
                if (actionBtn1 != null) actionBtn1.gameObject.SetActive(false);
                if (actionBtn2 != null) actionBtn2.gameObject.SetActive(false);
                if (actionBtn3 != null) actionBtn3.gameObject.SetActive(false);
                if (actionBtn4 != null) actionBtn4.gameObject.SetActive(false);
                if (actionBtn5 != null) actionBtn5.gameObject.SetActive(false);
            }
        }

        public void ShowVillagerBuildMenu()
        {
            if (bottomActionPanel == null) return;
            bottomActionPanel.SetActive(true);

            var techMgr = RTSTechManager.Instance;
            bool isFeudalOrHigher = (techMgr != null && techMgr.currentAge >= RTSAge.FeudalAge);
            bool isCastleAge = (techMgr != null && techMgr.currentAge >= RTSAge.CastleAge);

            // Sayfa 0: Temel & Ekonomi (Ekonomi & Barınma)
            // Sayfa 0: Temel Ekonomi, Nüfus & Ticaret Binaları
            if (villagerBuildPage == 0)
            {
                if (actionPanelTitle != null) actionPanelTitle.text = "KÖYLÜ İNŞAAT - TEMEL VE EKONOMİ YAPILARI (Sayfa 1/2)";

                SetupActionButton(actionBtn1, actionBtn1Text, "Tarla\n60 Odun", true, () =>
                {
                    RTSSelectionManager.Instance?.StartBuildingPlacement(RTSBuildingType.Farm);
                });

                SetupActionButton(actionBtn2, actionBtn2Text, "Ahşap Ev\n30O (+5 Nüf)", true, () =>
                {
                    RTSSelectionManager.Instance?.StartBuildingPlacement(RTSBuildingType.House);
                });

                SetupActionButton(actionBtn3, actionBtn3Text, "Demirci\n150 Odun", true, () =>
                {
                    RTSSelectionManager.Instance?.StartBuildingPlacement(RTSBuildingType.Blacksmith);
                });

                SetupActionButton(actionBtn4, actionBtn4Text, "Pazar Yeri\n150 Odun", true, () =>
                {
                    RTSSelectionManager.Instance?.StartBuildingPlacement(RTSBuildingType.Market);
                });

                SetupActionButton(actionBtn5, actionBtn5Text, "Askeri >>\n(Sayfa 2/2)", true, () =>
                {
                    villagerBuildPage = 1;
                    ShowVillagerBuildMenu();
                });
            }
            // Sayfa 1: Askeri & Kuşatma Kışlaları
            else
            {
                if (actionPanelTitle != null) actionPanelTitle.text = "KÖYLÜ İNŞAAT - ASKERİ KIŞLA VE KUŞATMA (Sayfa 2/2)";

                SetupActionButton(actionBtn1, actionBtn1Text, "Kışla\n150 Odun", true, () =>
                {
                    RTSSelectionManager.Instance?.StartBuildingPlacement(RTSBuildingType.Barracks);
                });

                SetupActionButton(actionBtn2, actionBtn2Text, "Okçu Talimi\n175 Odun", true, () =>
                {
                    RTSSelectionManager.Instance?.StartBuildingPlacement(RTSBuildingType.ArcheryRange);
                });

                string siegeLabel = isFeudalOrHigher ? "Kuşatma Atölyesi\n200O, 100A" : "Kuşatma\n(Feodal Çağ)";
                SetupActionButton(actionBtn3, actionBtn3Text, siegeLabel, isFeudalOrHigher, () =>
                {
                    RTSSelectionManager.Instance?.StartBuildingPlacement(RTSBuildingType.SiegeWorkshop);
                });

                string castleLabel = isCastleAge ? "Şanlı Kale\n350O, 250A" : "Şanlı Kale\n(Kale Çağı)";
                SetupActionButton(actionBtn4, actionBtn4Text, castleLabel, isCastleAge, () =>
                {
                    RTSSelectionManager.Instance?.StartBuildingPlacement(RTSBuildingType.Castle);
                });

                SetupActionButton(actionBtn5, actionBtn5Text, "<< Ekonomi\n(Sayfa 1/2)", true, () =>
                {
                    villagerBuildPage = 0;
                    ShowVillagerBuildMenu();
                });
            }
        }

        private Vector3 GetBuildingFrontSpawnPos(RTSBuilding b, float dist = 5.5f)
        {
            if (b == null) return Vector3.zero;
            Vector3 fwd = (Vector3.zero - b.transform.position);
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = (b.team == UnitTeam.Enemy) ? Vector3.back : Vector3.forward;
            else fwd.Normalize();
            return b.transform.position + fwd * dist;
        }

        public void ShowArcheryRangeMenu(RTSBuilding archeryRange)
        {
            if (bottomActionPanel == null || archeryRange == null) return;
            bottomActionPanel.SetActive(true);

            if (actionPanelTitle != null) actionPanelTitle.text = "OKÇU TALİMGÂHI - MENZİLLİ BİRLİKLER";

            Vector3 spawnPos = GetBuildingFrontSpawnPos(archeryRange, 5.0f);

            var techMgr = RTSTechManager.Instance;
            bool isFeudalOrHigher = (techMgr != null && techMgr.currentAge >= RTSAge.FeudalAge);

            // 1. İmparatorluk Okçusu (50 Odun, 30 Altın)
            SetupActionButton(actionBtn1, actionBtn1Text, "Okçu\n50O, 30A", true, () =>
            {
                RTSGameManager.Instance?.TrainSingleUnit("archer", spawnPos);
            });

            // 2. Arbaletçi / Crossbowman (60 Odun, 40 Altın - Feodal Çağ)
            string xbowLabel = isFeudalOrHigher ? "Arbaletçi\n60O, 40A" : "Arbaletçi\n(Feodal Çağ)";
            SetupActionButton(actionBtn2, actionBtn2Text, xbowLabel, isFeudalOrHigher, () =>
            {
                RTSGameManager.Instance?.TrainSingleUnit("crossbowman", spawnPos);
            });

            // 3. Ok Temreni / Fletching Teknolojisi (+2 Ok Hasarı, +2m Menzil)
            bool hasFletch = techMgr != null && techMgr.HasTech("Fletching");
            string fletchLabel = hasFletch ? "Ok Temreni\n(AÇIK)" : "Ok Temreni\n50O, 100Y";
            SetupActionButton(actionBtn3, actionBtn3Text, fletchLabel, !hasFletch, () =>
            {
                if (techMgr != null && techMgr.TryResearchTech("Fletching"))
                {
                    ShowArcheryRangeMenu(archeryRange);
                }
            });

            if (actionBtn4 != null) actionBtn4.gameObject.SetActive(false);
            if (actionBtn5 != null) actionBtn5.gameObject.SetActive(false);
        }

        public void ShowSiegeWorkshopMenu(RTSBuilding workshop)
        {
            if (bottomActionPanel == null || workshop == null) return;
            bottomActionPanel.SetActive(true);

            if (actionPanelTitle != null) actionPanelTitle.text = "KUŞATMA ATÖLYESİ - AĞIR KUŞATMA SİLAHLARI";

            Vector3 spawnPos = GetBuildingFrontSpawnPos(workshop, 5.5f);

            // 1. Kuşatma Mancınığı / Catapult (200 Odun, 150 Altın)
            SetupActionButton(actionBtn1, actionBtn1Text, "Mancınık\n200O, 150A", true, () =>
            {
                RTSGameManager.Instance?.TrainSingleUnit("trebuchet", spawnPos);
            });

            // 2. Ağır Mancınık Bataryası
            SetupActionButton(actionBtn2, actionBtn2Text, "Ağır Mancınık\n200O, 150A", true, () =>
            {
                RTSGameManager.Instance?.TrainSingleUnit("trebuchet", spawnPos);
            });

            if (actionBtn3 != null) actionBtn3.gameObject.SetActive(false);
            if (actionBtn4 != null) actionBtn4.gameObject.SetActive(false);
            if (actionBtn5 != null) actionBtn5.gameObject.SetActive(false);
        }

        public void ShowMarketMenu(RTSBuilding market)
        {
            if (bottomActionPanel == null || market == null) return;
            bottomActionPanel.SetActive(true);

            if (actionPanelTitle != null) actionPanelTitle.text = "PAZAR YERİ - SERBEST TİCARET & KAYNAK TAKASI";

            var gm = RTSGameManager.Instance;

            // 1. Odun Sat: 100 Odun -> +70 Altın
            SetupActionButton(actionBtn1, actionBtn1Text, "Odun Sat\n100O -> +70A", true, () =>
            {
                if (gm != null && gm.wood >= 100)
                {
                    gm.wood -= 100;
                    gm.gold += 70;
                    gm.UpdateHUD();
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.Click);
                    SetAiStatus("[TİCARET] 100 Odun satıldı, +70 Altın kazanıldı!", Color.yellow);
                }
                else
                {
                    SetAiStatus("[UYARI] Ticaret için en az 100 Odun gerekiyor!", Color.red);
                }
            });

            // 2. Odun Satın Al: 100 Altın -> +100 Odun
            SetupActionButton(actionBtn2, actionBtn2Text, "Odun Al\n100A -> +100O", true, () =>
            {
                if (gm != null && gm.gold >= 100)
                {
                    gm.gold -= 100;
                    gm.wood += 100;
                    gm.UpdateHUD();
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.Click);
                    SetAiStatus("[TİCARET] 100 Altın karşılığı 100 Odun satın alındı!", Color.green);
                }
                else
                {
                    SetAiStatus("[UYARI] Ticaret için en az 100 Altın gerekiyor!", Color.red);
                }
            });

            // 3. Yiyecek Sat: 100 Yiyecek -> +70 Altın
            SetupActionButton(actionBtn3, actionBtn3Text, "Yiyecek Sat\n100Y -> +70A", true, () =>
            {
                if (gm != null && gm.food >= 100)
                {
                    gm.food -= 100;
                    gm.gold += 70;
                    gm.UpdateHUD();
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.Click);
                    SetAiStatus("[TİCARET] 100 Yiyecek satıldı, +70 Altın kazanıldı!", Color.yellow);
                }
                else
                {
                    SetAiStatus("[UYARI] Ticaret için en az 100 Yiyecek gerekiyor!", Color.red);
                }
            });

            // 4. Yiyecek Satın Al: 100 Altın -> +100 Yiyecek
            SetupActionButton(actionBtn4, actionBtn4Text, "Yiyecek Al\n100A -> +100Y", true, () =>
            {
                if (gm != null && gm.gold >= 100)
                {
                    gm.gold -= 100;
                    gm.food += 100;
                    gm.UpdateHUD();
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.Click);
                    SetAiStatus("[TİCARET] 100 Altın karşılığı 100 Yiyecek satın alındı!", Color.green);
                }
                else
                {
                    SetAiStatus("[UYARI] Ticaret için en az 100 Altın gerekiyor!", Color.red);
                }
            });

            if (actionBtn5 != null) actionBtn5.gameObject.SetActive(false);
        }

        public void ShowCastleMenu(RTSBuilding castle)
        {
            if (bottomActionPanel == null || castle == null) return;
            bottomActionPanel.SetActive(true);

            if (actionPanelTitle != null) actionPanelTitle.text = "ŞANLI KALE - KRALİYET ORDUSU & KUŞATMA";

            Vector3 spawnPos = GetBuildingFrontSpawnPos(castle, 6.0f);

            // 1. Ağır Şövalye / Kraliyet Süvarisi (100Y, 80A)
            SetupActionButton(actionBtn1, actionBtn1Text, "Şövalye\n100Y, 80A", true, () =>
            {
                RTSGameManager.Instance?.TrainSingleUnit("cavalry", spawnPos);
            });

            // 2. Kuşatma Mancınığı / Trebuchet (200O, 150A)
            SetupActionButton(actionBtn2, actionBtn2Text, "Mancınık\n200O, 150A", true, () =>
            {
                RTSGameManager.Instance?.TrainSingleUnit("trebuchet", spawnPos);
            });

            // 3. Kale Savunma Teknolojisi (Ok Temreni / Fletching)
            var techMgr = RTSTechManager.Instance;
            bool hasFletch = techMgr != null && techMgr.HasTech("Fletching");
            string fletchLabel = hasFletch ? "Ok Temreni\n(AÇIK)" : "Ok Temreni\n50O, 100Y";
            SetupActionButton(actionBtn3, actionBtn3Text, fletchLabel, !hasFletch, () =>
            {
                if (techMgr != null && techMgr.TryResearchTech("Fletching"))
                {
                    ShowCastleMenu(castle);
                }
            });

            SetupActionButton(actionBtn4, actionBtn4Text, "", false, null);
            SetupActionButton(actionBtn5, actionBtn5Text, "", false, null);
        }

        public void ShowTownCenterMenu(RTSBuilding townCenter)
        {
            if (bottomActionPanel == null || townCenter == null) return;
            bottomActionPanel.SetActive(true);

            if (actionPanelTitle != null) actionPanelTitle.text = "ŞEHİR MERKEZİ - NÜFUS & ÇAĞ GELİŞİMİ";

            Vector3 spawnPos = GetBuildingFrontSpawnPos(townCenter, 5.5f);

            SetupActionButton(actionBtn1, actionBtn1Text, "Köylü Eğit (Q)\n50 Yiyecek", true, () =>
            {
                RTSGameManager.Instance?.TrainSingleUnit("villager", spawnPos);
            });

            var techMgr = RTSTechManager.Instance;
            var currentAge = (techMgr != null) ? techMgr.currentAge : RTSAge.DarkAge;

            if (currentAge == RTSAge.DarkAge)
            {
                SetupActionButton(actionBtn2, actionBtn2Text, "Feodal Çağ\n500Y, 200O", true, () =>
                {
                    if (techMgr != null && techMgr.TryResearchTech("Feudal_Age"))
                    {
                        UpdateAgeBadge();
                        ShowTownCenterMenu(townCenter);
                    }
                });
            }
            else if (currentAge == RTSAge.FeudalAge)
            {
                SetupActionButton(actionBtn2, actionBtn2Text, "Kale Çağı\n800Y, 200A", true, () =>
                {
                    if (techMgr != null && techMgr.TryResearchTech("Castle_Age"))
                    {
                        UpdateAgeBadge();
                        ShowTownCenterMenu(townCenter);
                    }
                });
            }
            else
            {
                SetupActionButton(actionBtn2, actionBtn2Text, "Kale Çağı\n(Maksimum)", false, null);
            }

            SetupActionButton(actionBtn3, actionBtn3Text, "", false, null);
            SetupActionButton(actionBtn4, actionBtn4Text, "", false, null);
            SetupActionButton(actionBtn5, actionBtn5Text, "", false, null);
        }

        public void ShowBarracksMenu(RTSBuilding barracks)
        {
            if (bottomActionPanel == null || barracks == null) return;
            bottomActionPanel.SetActive(true);

            if (actionPanelTitle != null) actionPanelTitle.text = "KIŞLA - ASKER EĞİTİMİ";

            Vector3 spawnPos = GetBuildingFrontSpawnPos(barracks, 5.5f);

            var techMgr = RTSTechManager.Instance;
            bool isFeudal = (techMgr != null && techMgr.currentAge >= RTSAge.FeudalAge);
            bool isCastle = (techMgr != null && techMgr.currentAge >= RTSAge.CastleAge);

            // 1. Mızrakçı: Karanlık Çağ (Her zaman açık)
            SetupActionButton(actionBtn1, actionBtn1Text, "Mızrakçı\n60Y, 20A", true, () =>
            {
                RTSGameManager.Instance?.TrainSingleUnit("spearman", spawnPos);
            });

            // 2. Kılıçlı: Feodal Çağ gerekir
            string swordLabel = isFeudal ? "Kılıçlı\n60Y, 30A" : "Kılıçlı\n(Feodal Çağ)";
            SetupActionButton(actionBtn2, actionBtn2Text, swordLabel, isFeudal, () =>
            {
                RTSGameManager.Instance?.TrainSingleUnit("swordsman", spawnPos);
            });

            // 3. Okçu: Feodal Çağ gerekir
            string archerLabel = isFeudal ? "Okçu\n50O, 30A" : "Okçu\n(Feodal Çağ)";
            SetupActionButton(actionBtn3, actionBtn3Text, archerLabel, isFeudal, () =>
            {
                RTSGameManager.Instance?.TrainSingleUnit("archer", spawnPos);
            });

            // 4. Kuşatma Silahı / Mancınık: Kale Çağı gerekir
            string trebLabel = isCastle ? "Mancınık\n200O, 150A" : "Mancınık\n(Kale Çağı)";
            SetupActionButton(actionBtn4, actionBtn4Text, trebLabel, isCastle, () =>
            {
                RTSGameManager.Instance?.TrainSingleUnit("trebuchet", spawnPos);
            });

            // 5. Şövalye / Süvari: Kale Çağı gerekir
            string cavLabel = isCastle ? "Süvari\n100Y, 80A" : "Süvari\n(Kale Çağı)";
            SetupActionButton(actionBtn5, actionBtn5Text, cavLabel, isCastle, () =>
            {
                RTSGameManager.Instance?.TrainSingleUnit("cavalry", spawnPos);
            });
        }

        public void ShowBlacksmithMenu(RTSBuilding blacksmith)
        {
            if (bottomActionPanel == null || blacksmith == null) return;
            bottomActionPanel.SetActive(true);

            if (actionPanelTitle != null) actionPanelTitle.text = "DEMİRCİ - TEKNOLOJİ ARAŞTIRMASI";

            var techMgr = RTSTechManager.Instance;

            bool hasIron = techMgr != null && techMgr.HasTech("Iron_Weapons");
            string ironLabel = hasIron ? "Demir Silah\n(AÇIK)" : "Demir Silah\n100Y, 50A";
            SetupActionButton(actionBtn1, actionBtn1Text, ironLabel, !hasIron, () =>
            {
                if (techMgr != null && techMgr.TryResearchTech("Iron_Weapons"))
                {
                    ShowBlacksmithMenu(blacksmith);
                }
            });

            bool hasArmor = techMgr != null && techMgr.HasTech("Scale_Armor");
            string armorLabel = hasArmor ? "Pullu Zırh\n(AÇIK)" : "Pullu Zırh\n100Y, 80A";
            SetupActionButton(actionBtn2, actionBtn2Text, armorLabel, !hasArmor, () =>
            {
                if (techMgr != null && techMgr.TryResearchTech("Scale_Armor"))
                {
                    ShowBlacksmithMenu(blacksmith);
                }
            });

            bool hasFletching = techMgr != null && techMgr.HasTech("Fletching");
            string fletchLabel = hasFletching ? "Ok Temreni\n(AÇIK)" : "Ok Temreni\n100Y, 50O";
            SetupActionButton(actionBtn3, actionBtn3Text, fletchLabel, !hasFletching, () =>
            {
                if (techMgr != null && techMgr.TryResearchTech("Fletching"))
                {
                    ShowBlacksmithMenu(blacksmith);
                }
            });

            bool hasCart = techMgr != null && techMgr.HasTech("Wheelbarrow");
            string cartLabel = hasCart ? "El Arabası\n(AÇIK)" : "El Arabası\n175Y, 50O";
            SetupActionButton(actionBtn4, actionBtn4Text, cartLabel, !hasCart, () =>
            {
                if (techMgr != null && techMgr.TryResearchTech("Wheelbarrow"))
                {
                    ShowBlacksmithMenu(blacksmith);
                }
            });

            SetupActionButton(actionBtn5, actionBtn5Text, "", false, null);
        }

        private void SetupActionButton(Button btn, Text btnText, string label, bool interactable, Action onClick)
        {
            if (btn == null) return;
            btn.gameObject.SetActive(!string.IsNullOrEmpty(label));
            btn.interactable = interactable;
            if (btnText != null) btnText.text = label;

            btn.onClick.RemoveAllListeners();
            if (onClick != null)
            {
                btn.onClick.AddListener(() => onClick());
            }
        }

        public void ShowGameOverModal(bool isVictory, string statsSummary)
        {
            Time.timeScale = 0f;
            if (gameOverModal != null)
            {
                gameOverModal.SetActive(true);

                if (gameOverTitleText != null)
                {
                    gameOverTitleText.text = isVictory ? "ZAFER! HARİTA FETHEDİLDİ!" : "YENİLGİ! ŞEHİR MERKEZİ DÜŞTÜ!";
                    gameOverTitleText.color = isVictory ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 0.35f, 0.35f);
                }

                if (gameOverSubtitleText != null)
                {
                    gameOverSubtitleText.text = isVictory
                        ? "Düşman Karargahı tamamen imha edildi. Kuzey toprakları artık senin hükümdarlığında!"
                        : "Ana Şehir Merkeziniz düşman kuşatmasında yıkıldı. Krallığınız haritadan silindi.";
                }

                if (gameOverStatsText != null)
                {
                    gameOverStatsText.text = statsSummary;
                }

                if (gameOverRestartBtn != null)
                {
                    gameOverRestartBtn.onClick.RemoveAllListeners();
                    gameOverRestartBtn.onClick.AddListener(RestartCurrentMatch);
                }

                if (gameOverMainMenuBtn != null)
                {
                    gameOverMainMenuBtn.onClick.RemoveAllListeners();
                    gameOverMainMenuBtn.onClick.AddListener(ReturnToMainMenu);
                }
            }
        }

        #region Oyun ve Ses Ayarları Sistemi (Audio Settings)

        public void OpenSettingsModal()
        {
            if (settingsModal != null)
            {
                settingsModal.SetActive(true);
                UpdateAudioSettingsUI();
            }
        }

        public void CloseSettingsModal()
        {
            if (settingsModal != null)
            {
                settingsModal.SetActive(false);
            }
        }

        public void UpdateAudioSettingsUI()
        {
            var audio = RTSAudioManager.Instance;
            float mVol = audio != null ? audio.masterVolume : 0.85f;
            float musVol = audio != null ? audio.musicVolume : 0.40f;
            float sfxVol = audio != null ? audio.sfxVolume : 0.85f;
            float vocVol = audio != null ? audio.voiceVolume : 0.95f;
            bool muted = audio != null ? audio.isMuted : false;

            if (masterVolText != null) masterVolText.text = $"% {Mathf.RoundToInt(mVol * 100f)}";
            if (masterVolBar != null) masterVolBar.sizeDelta = new Vector2(180f * mVol, 14f);

            if (musicVolText != null) musicVolText.text = $"% {Mathf.RoundToInt(musVol * 100f)}";
            if (musicVolBar != null) musicVolBar.sizeDelta = new Vector2(180f * musVol, 14f);

            if (sfxVolText != null) sfxVolText.text = $"% {Mathf.RoundToInt(sfxVol * 100f)}";
            if (sfxVolBar != null) sfxVolBar.sizeDelta = new Vector2(180f * sfxVol, 14f);

            if (voiceVolText != null) voiceVolText.text = $"% {Mathf.RoundToInt(vocVol * 100f)}";
            if (voiceVolBar != null) voiceVolBar.sizeDelta = new Vector2(180f * vocVol, 14f);

            var curTrack = RTSAudioManager.Instance?.GetCurrentTrackInfo();
            if (curTrack != null)
            {
                if (musicTrackTitleText != null) musicTrackTitleText.text = curTrack.title;
                if (musicTrackSubText != null) musicTrackSubText.text = curTrack.subtitle;
            }

            if (settingsMuteToggleText != null)
            {
                settingsMuteToggleText.text = muted ? "🔈 SES DURUMU: SUSTURULDU (KAPALI)" : "🔊 SES DURUMU: AKTİF (AÇIK)";
                settingsMuteToggleText.color = muted ? new Color(1f, 0.45f, 0.4f) : new Color(0.55f, 0.95f, 0.55f);
            }

            if (startScreenAudioBtnText != null)
            {
                startScreenAudioBtnText.text = muted ? "SES: KAPALI" : "SES & MÜZİK AYARLARI";
            }
        }

        #endregion

        #endregion
    }
}
