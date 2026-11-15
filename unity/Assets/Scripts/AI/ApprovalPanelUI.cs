using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CSE377.AI
{
    /// <summary>
    /// Human-in-the-Loop (İnsan Onayı) Karar Paneli.
    /// Yapay zekanın önerdiği eylem planını oyuncuya görsel olarak sunar.
    /// Oyuncu 'Onayla' butonuna basarsa GameActionValidator kontrolünden sonra ağ katmanına iletilir.
    /// </summary>
    public class ApprovalPanelUI : MonoBehaviour
    {
        [Header("Arayüz Bileşenleri")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Text summaryText;
        [SerializeField] private Text confidenceText;
        [SerializeField] private Text validationStatusText;
        [SerializeField] private Transform actionsListContainer;
#pragma warning disable CS0649
        [SerializeField] private GameObject actionItemPrefab;
#pragma warning restore CS0649

        [Header("Butonlar")]
        [SerializeField] private Button approveButton;
        [SerializeField] private Button rejectButton;

        [Header("Bağlantılar")]
        [SerializeField] private AiGatewayClient gatewayClient;

        private RTSActionPlan activePlan;
        private AiSafeSnapshot activeSnapshot;

        public void Initialize(
            GameObject root,
            Text summary,
            Text confidence,
            Text status,
            Transform container,
            Button approveBtn,
            Button rejectBtn,
            AiGatewayClient client)
        {
            panelRoot = root;
            summaryText = summary;
            confidenceText = confidence;
            validationStatusText = status;
            actionsListContainer = container;
            approveButton = approveBtn;
            rejectButton = rejectBtn;
            gatewayClient = client;

            if (panelRoot != null) panelRoot.SetActive(false);

            if (approveButton != null)
            {
                approveButton.onClick.RemoveAllListeners();
                approveButton.onClick.AddListener(OnApproveClicked);
            }

            if (rejectButton != null)
            {
                rejectButton.onClick.RemoveAllListeners();
                rejectButton.onClick.AddListener(OnRejectClicked);
            }

            if (gatewayClient != null)
            {
                gatewayClient.OnPlanReceived -= ShowPlan;
                gatewayClient.OnPlanReceived += ShowPlan;
            }
        }

        private void Awake()
        {
            if (panelRoot != null) panelRoot.SetActive(false);

            if (approveButton != null) approveButton.onClick.AddListener(OnApproveClicked);
            if (rejectButton != null) rejectButton.onClick.AddListener(OnRejectClicked);
        }

        private void OnEnable()
        {
            if (gatewayClient != null)
            {
                gatewayClient.OnPlanReceived -= ShowPlan;
                gatewayClient.OnPlanReceived += ShowPlan;
            }
        }

        private void OnDisable()
        {
            if (gatewayClient != null)
            {
                gatewayClient.OnPlanReceived -= ShowPlan;
            }
        }

        /// <summary>
        /// Yapay zekadan gelen yeni planı ekranda görüntüler.
        /// </summary>
        public void ShowPlan(RTSActionPlan plan)
        {
            activePlan = plan;
            var gm = FindFirstObjectByType<CSE377.Gameplay.RTSGameManager>();
            activeSnapshot = (gm != null) ? gm.GenerateLiveSnapshot() : AiSafeSnapshot.CreateMockSnapshot();

            if (panelRoot != null) panelRoot.SetActive(true);

            if (summaryText != null)
                summaryText.text = plan.summary;

            if (confidenceText != null)
                confidenceText.text = $"Güven Seviyesi: %{(int)(plan.confidence * 100)}";

            if (validationStatusText != null)
                validationStatusText.text = "Oyuncu onayı bekleniyor...";

            PopulateActionsList(plan.actions);
        }

        private void PopulateActionsList(List<RTSAction> actions)
        {
            if (actionsListContainer == null) return;

            // Eski maddeleri temizle
            foreach (Transform child in actionsListContainer)
            {
                Destroy(child.gameObject);
            }

            if (actions == null) return;

            foreach (var action in actions)
            {
                if (actionItemPrefab != null)
                {
                    var item = Instantiate(actionItemPrefab, actionsListContainer);
                    var txt = item.GetComponentInChildren<Text>();
                    if (txt != null) txt.text = action.ToString();
                }
                else
                {
                    // Prefab atanmamışsa dinamik basit Text UI oluştur
                    GameObject itemGo = new GameObject("ActionItem", typeof(RectTransform), typeof(Text));
                    itemGo.transform.SetParent(actionsListContainer, false);
                    var t = itemGo.GetComponent<Text>();
                    t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    t.fontSize = 14;
                    t.color = Color.white;
                    t.text = $"• {action}";
                }
            }
        }

        private void OnApproveClicked()
        {
            if (activePlan == null) return;

            // 1. GameActionValidator ile yerel kural ve kaynak denetimi yap
            var validation = GameActionValidator.ValidatePlan(activePlan, activeSnapshot);

            if (validation.IsValid)
            {
                Debug.Log($"[ApprovalPanelUI] Plan onaylandı ve kurallardan geçti! Photon ağına iletiliyor...");
                if (validationStatusText != null)
                {
                    validationStatusText.color = Color.green;
                    validationStatusText.text = "Plan Onaylandı -> Photon Ağına İletildi!";
                }

                // 2. RTSGameManager üzerinden yerel simülasyonda icra et
                var gameMgr = FindFirstObjectByType<CSE377.Gameplay.RTSGameManager>();
                if (gameMgr != null)
                {
                    gameMgr.ExecuteApprovedPlan(activePlan);
                }

                Invoke(nameof(ClosePanel), 1.5f);
            }
            else
            {
                Debug.LogWarning($"[ApprovalPanelUI] Doğrulama hatası: {validation.ErrorMessage}");
                if (validationStatusText != null)
                {
                    validationStatusText.color = Color.red;
                    validationStatusText.text = $"HATA: {validation.ErrorMessage}";
                }
            }
        }

        private void OnRejectClicked()
        {
            Debug.Log("[ApprovalPanelUI] Eylem planı oyuncu tarafından reddedildi.");
            ClosePanel();
        }

        public void ClosePanel()
        {
            activePlan = null;
            if (panelRoot != null) panelRoot.SetActive(false);
        }
    }
}
