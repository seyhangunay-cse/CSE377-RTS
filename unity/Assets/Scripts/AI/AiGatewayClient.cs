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
            string q = (userPrompt ?? "").ToLowerInvariant().Trim();
            bool isRedTeam = snapshot != null && snapshot.team_id == "RED";
            string cmdPrefix = isRedTeam ? "[KIRMIZI AI KOMUTAN] " : "[MAVİ AI KOMUTAN] ";
            string enemyDirTr = isRedTeam ? "Güneydeki Mavi" : "Kuzeydeki Kırmızı";
            var plan = new RTSActionPlan();

            // 1. KUŞATMA & KARARGAH YIKIMI (Siege & Assault Enemy Base)
            if (q.Contains("kuşat") || q.Contains("kusat") || q.Contains("karargah") || q.Contains("üs") || q.Contains("üssü") || q.Contains("yık") || q.Contains("kale"))
            {
                plan.summary = $"{cmdPrefix}Kuşatma Doktrini: {enemyDirTr} düşman karargahı ve savunma tahkimatlarını imha etmek üzere ana taarruz başlatılıyor.";
                plan.confidence = 0.94f;

                plan.actions.Add(new RTSAction
                {
                    action_type = "TRAIN_UNIT",
                    unit_type = "spearman",
                    count = 4,
                    target_zone = "north_gate",
                    reason = "Kuşatma sırasında piyade koruma hattı sağlamak için mızrakçı eğitimi."
                });

                plan.actions.Add(new RTSAction
                {
                    action_type = "ATTACK",
                    unit_type = "soldier",
                    count = 10,
                    target_zone = "enemy_base",
                    reason = "Düşman ana kalesini ve karargahını doğrudan kuşatıp imha et."
                });

                return plan;
            }

            // 2. TAARRUZ / KARŞI SALDIRI (Counter-Attack / Assault)
            if (q.Contains("saldır") || q.Contains("saldir") || q.Contains("taarruz") || q.Contains("hücum") || q.Contains("hucum") || q.Contains("karşı") || q.Contains("karsi") || q.Contains("yok et") || q.Contains("vur"))
            {
                plan.summary = "Karşı Taarruz Emri: Düşman hatlarını yarmak ve inisiyatifi ele geçirmek amacıyla tüm muharip birliklerimiz ileri hatta taarruza kaldırılıyor.";
                plan.confidence = 0.92f;

                plan.actions.Add(new RTSAction
                {
                    action_type = "ATTACK",
                    unit_type = "soldier",
                    count = 8,
                    target_zone = "north_gate",
                    reason = "Kuzey koridorundaki düşman birliklerine doğrudan taarruz et."
                });

                plan.actions.Add(new RTSAction
                {
                    action_type = "TRAIN_UNIT",
                    unit_type = "archer",
                    count = 3,
                    target_zone = "north_gate",
                    reason = "Taarruz eden piyadelere geriden menzilli okçu desteği sağla."
                });

                return plan;
            }

            // 3. EKONOMİ / TARLA / KAYNAK / KÖYLÜ (Economy & Agriculture)
            if (q.Contains("ekonomi") || q.Contains("kaynak") || q.Contains("gıda") || q.Contains("gida") || q.Contains("odun") || q.Contains("tarla") || q.Contains("çiftlik") || q.Contains("ciftlik") || q.Contains("köylü") || q.Contains("koylu") || q.Contains("işçi") || q.Contains("isci"))
            {
                plan.summary = "Ekonomik Büyüme Programı: Kaynak gelirlerini artırmak, gıda üretimini güvenceye almak ve köylü nüfusunu genişletmek için acil plan devrede.";
                plan.confidence = 0.88f;

                plan.actions.Add(new RTSAction
                {
                    action_type = "TRAIN_UNIT",
                    unit_type = "villager",
                    count = 2,
                    target_zone = "town_center",
                    reason = "Ekonomik işgücünü ve kaynak toplama hızını artırmak için köylü eğitimi."
                });

                plan.actions.Add(new RTSAction
                {
                    action_type = "BUILD",
                    building_type = "farm",
                    target_zone = "town_center",
                    reason = "Düzenli gıda hasadı için Şehir Merkezi yanına tarla kurulumu."
                });

                plan.actions.Add(new RTSAction
                {
                    action_type = "RESEARCH",
                    building_type = "Wheelbarrow",
                    target_zone = "town_center",
                    reason = "Köylülerin taşıma kapasitesini ve hareket süratini %25 artıran El Arabası araştırması."
                });

                return plan;
            }

            // 4. ÇAĞ ATLAMA & TEKNOLOJİ (Age Advancement & Tech)
            if (q.Contains("çağ") || q.Contains("cag") || q.Contains("feodal") || q.Contains("atla") || q.Contains("geliş") || q.Contains("gelis") || q.Contains("demirci") || q.Contains("teknoloji") || q.Contains("silah") || q.Contains("zırh") || q.Contains("zirh"))
            {
                plan.summary = "Teknolojik Gelişim ve Çağ Atlama Hamlesi: Demirci atölyesi kurularak ordu teçhizatı güçlendiriliyor ve II. Feodal Çağ'a geçiş altyapısı hazırlanıyor.";
                plan.confidence = 0.90f;

                plan.actions.Add(new RTSAction
                {
                    action_type = "BUILD",
                    building_type = "blacksmith",
                    target_zone = "town_center",
                    reason = "Metalurji ve zırh araştırmalarını açmak için Demirci Atölyesi inşası."
                });

                plan.actions.Add(new RTSAction
                {
                    action_type = "RESEARCH",
                    building_type = "Iron_Weapons",
                    target_zone = "town_center",
                    reason = "Tüm piyadelerin saldırı gücünü artıran Demir Silahlar teknolojisini araştır."
                });

                plan.actions.Add(new RTSAction
                {
                    action_type = "RESEARCH",
                    building_type = "Scale_Armor",
                    target_zone = "town_center",
                    reason = "Birliklerin hayatta kalma kabiliyetini artıran Pul Zırh geliştirmesi."
                });

                return plan;
            }

            // 5. TAHKİMAT & KULE & SUR & KAPI (Fortifications, Towers, Gates)
            if (q.Contains("kule") || q.Contains("sur") || q.Contains("duvar") || q.Contains("tahkim") || q.Contains("kapı") || q.Contains("kapi") || q.Contains("savunma kulesi"))
            {
                plan.summary = "Savunma ve Garnizon Planı: Düşman akın güzergahlarına gözetleme kuleleri çekilerek savunma hattı kuruluyor.";
                plan.confidence = 0.91f;

                plan.actions.Add(new RTSAction
                {
                    action_type = "BUILD",
                    building_type = "tower",
                    target_zone = "north_gate",
                    reason = "Kuzey geçidine düşmanı otomatik oklayan Savunma Kulesi inşası."
                });

                plan.actions.Add(new RTSAction
                {
                    action_type = "TRAIN_UNIT",
                    unit_type = "archer",
                    count = 2,
                    target_zone = "north_gate",
                    reason = "Kule arkasında mevzilenip düşmanı menzilden avlayacak okçular."
                });

                return plan;
            }

            // 6. KEŞİF / HARİTA / SİS / ALTIN (Scouting & Map Exploration)
            if (q.Contains("keşif") || q.Contains("kesif") || q.Contains("scout") || q.Contains("harita") || q.Contains("sis") || q.Contains("altın") || q.Contains("altin") || q.Contains("maden"))
            {
                string focus = (q.Contains("altın") || q.Contains("altin") || q.Contains("maden")) ? "gold" : "unexplored";
                string focusTr = focus == "gold" ? "zengin altın madeni yataklarını" : "haritadaki sisli karanlık bölgeleri";

                plan.summary = $"Stratejik Keşif Görevi: Savaş sisini dağıtmak ve {focusTr} tespit etmek için öncü keşif birliği görevlendirildi.";
                plan.confidence = 0.95f;

                plan.actions.Add(new RTSAction
                {
                    action_type = "SCOUT",
                    unit_type = "soldier",
                    count = 1,
                    target_zone = focus,
                    reason = $"Bölgeyi tarayarak {focus} odaklı keşif rotasını icra et."
                });

                return plan;
            }

            // 7. EV & NÜFUS KAPASİTESİ (Houses & Population)
            if (q.Contains("ev") || q.Contains("nüfus") || q.Contains("nufus") || q.Contains("kapasite") || q.Contains("barınak"))
            {
                plan.summary = "Konut ve Nüfus Genişletme Planı: Nüfus tıkanıklığını engellemek ve ordu limitini artırmak amacıyla yeni evler inşa ediliyor.";
                plan.confidence = 0.89f;

                plan.actions.Add(new RTSAction
                {
                    action_type = "BUILD",
                    building_type = "house",
                    target_zone = "town_center",
                    reason = "Maksimum nüfus limitini yükseltmek için 2 adet Ev inşası."
                });

                return plan;
            }

            // 8. OKÇU / SÜVARİ / ÖZEL BİRLİK İSTEĞİ (Specific Unit Training)
            if (q.Contains("okçu") || q.Contains("okcu"))
            {
                plan.summary = "Menzilli Birlik Seferberliği: Düşman piyadelerini uzaktan eritmek için kışladan okçu taburu yetiştiriliyor.";
                plan.confidence = 0.90f;

                plan.actions.Add(new RTSAction
                {
                    action_type = "TRAIN_UNIT",
                    unit_type = "archer",
                    count = 4,
                    target_zone = "north_gate",
                    reason = "Menzilli üstünlük için 4 okçu eğit ve kuzey kapısına sevk et."
                });

                return plan;
            }

            if (q.Contains("süvari") || q.Contains("suvari") || q.Contains("atlı") || q.Contains("atli"))
            {
                plan.summary = "Hafif Süvari Akını: Hızlı hareket kabiliyetiyle düşman okçularını ve köylülerini ezmek için atlı süvari birliği tertipleniyor.";
                plan.confidence = 0.90f;

                plan.actions.Add(new RTSAction
                {
                    action_type = "TRAIN_UNIT",
                    unit_type = "cavalry",
                    count = 3,
                    target_zone = "north_gate",
                    reason = "Düşman hatlarına ani baskınlar düzenleyecek 3 süvari eğitimi."
                });

                return plan;
            }

            // 9. DİNAMİK SAVUNMA & GENEL TAKTİK ANALİZİ (Default Dynamic Defense Plan)
            plan.summary = $"{cmdPrefix}Kraliyet Savunma Hattı Doktrini: Sınır geçidinden gelebilecek akınlara karşı mızrakçı duvarı örülüyor ve okçu mevzisi kuruluyor.";
            plan.confidence = 0.88f;

            plan.actions.Add(new RTSAction
            {
                action_type = "TRAIN_UNIT",
                unit_type = "spearman",
                count = 3,
                target_zone = "north_gate",
                reason = "Süvarilere karşı mızrakçılar ağır bonus hasar verir."
            });

            plan.actions.Add(new RTSAction
            {
                action_type = "MOVE_GROUP",
                unit_type = "spearman",
                count = 4,
                target_zone = "north_gate",
                reason = "Mevcut mızrakçı birliklerini kuzey kapısı savunma hattına yerleştir."
            });

            plan.actions.Add(new RTSAction
            {
                action_type = "TRAIN_UNIT",
                unit_type = "archer",
                count = 2,
                target_zone = "north_gate",
                reason = "Mızrakçı hattını arkadan yaylım ateşiyle koruyacak 2 okçu."
            });

            return plan;
        }
    }
}
