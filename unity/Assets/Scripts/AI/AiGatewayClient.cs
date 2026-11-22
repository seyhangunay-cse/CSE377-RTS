using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CSE377.AI
{
    /// <summary>
    /// Unity istemcisi ile Python tabanlı AI Gateway / Agent Runner arasındaki HTTP iletişim köprüsü.
    /// Oyuncunun komutunu veya snapshot verisini Python servisine gönderir ve doğrulanmış ActionPlan yanıtı alır.
    /// </summary>
    public class AiGatewayClient : MonoBehaviour
    {
        [Header("Sunucu Yapılandırması")]
        [Tooltip("Python AI Gateway API adresi")]
        [SerializeField] private string gatewayUrl = "http://localhost:5000/api/plan";

        [Tooltip("Python servisi kapalıysa test için mock plan üret")]
        [SerializeField] private bool useMockFallback = true;

        [Tooltip("İstek zaman aşımı süresi (saniye)")]
        [SerializeField] private int timeoutSeconds = 120;

        // Olaylar (Events)
        public event Action<RTSActionPlan> OnPlanReceived;
        public event Action<string> OnPlanRequestFailed;
        public event Action OnRequestStarted;

        [Serializable]
        public class StrategyRequestPayload
        {
            public string query;
            public AiSafeSnapshot snapshot;

            public StrategyRequestPayload(string query, AiSafeSnapshot snapshot)
            {
                this.query = query;
                this.snapshot = snapshot;
            }
        }

        private void Awake()
        {
            if (timeoutSeconds < 120)
            {
                timeoutSeconds = 120;
            }
        }

        /// <summary>
        /// Yapay zekâ komutanından stratejik plan talep eder.
        /// </summary>
        public void RequestPlan(string userPrompt, AiSafeSnapshot currentSnapshot = null)
        {
            if (currentSnapshot == null)
            {
                currentSnapshot = AiSafeSnapshot.CreateMockSnapshot();
            }

            StartCoroutine(SendStrategyRequestCoroutine(userPrompt, currentSnapshot));
        }

        private IEnumerator SendStrategyRequestCoroutine(string userPrompt, AiSafeSnapshot snapshot)
        {
            OnRequestStarted?.Invoke();
            Debug.Log($"[AiGatewayClient] Python AI Gateway'e istek gönderiliyor: '{userPrompt}'");

            var payload = new StrategyRequestPayload(userPrompt, snapshot);
            string jsonPayload = JsonUtility.ToJson(payload);

            using (UnityWebRequest request = new UnityWebRequest(gatewayUrl, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = timeoutSeconds;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseJson = request.downloadHandler.text;
                    Debug.Log($"[AiGatewayClient] Başarılı yanıt alındı:\n{responseJson}");

                    var plan = RTSActionPlan.FromJson(responseJson);
                    if (plan != null)
                    {
                        OnPlanReceived?.Invoke(plan);
                    }
                    else
                    {
                        string err = "Gelen JSON, RTSActionPlan formatına dönüştürülemedi.";
                        Debug.LogError($"[AiGatewayClient] {err}");
                        OnPlanRequestFailed?.Invoke(err);
                    }
                }
                else
                {
                    string errorMsg = $"HTTP Hatası ({request.responseCode}): {request.error}";
                    Debug.LogWarning($"[AiGatewayClient] {errorMsg}");

                    if (useMockFallback)
                    {
                        Debug.LogWarning("[AiGatewayClient] Python Gateway erişilemediği için YEREL STRATEJİK DANIŞMAN MOTORU (Intelligent Heuristic) devreye girdi...");
                        var mockPlan = CreateFallbackPlan(userPrompt, snapshot);
                        OnPlanReceived?.Invoke(mockPlan);
                    }
                    else
                    {
                        OnPlanRequestFailed?.Invoke(errorMsg);
                    }
                }
            }
        }

        public RTSActionPlan CreateFallbackPlan(string userPrompt, AiSafeSnapshot snapshot)
        {
            return RTSAiAdvisor.BuildIntelligentPlan(userPrompt, snapshot);
        }
    }
}
