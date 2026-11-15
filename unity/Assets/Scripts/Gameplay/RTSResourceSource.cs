using System.Collections.Generic;
using UnityEngine;

namespace CSE377.Gameplay
{
    public enum RTSResourceType
    {
        Wood,
        Food,
        Gold
    }

    /// <summary>
    /// Harita üzerindeki toplanabilir kaynak noktası (Ağaç, Altın Madeni, Tarla).
    /// Köylüler bu nesneye sağ tıklayarak kaynak toplar ve Şehir Merkezine taşır.
    /// </summary>
    public class RTSResourceSource : MonoBehaviour
    {
        public static readonly List<RTSResourceSource> AllSources = new List<RTSResourceSource>();

        [Header("Kaynak Özellikleri")]
        public RTSResourceType resourceType = RTSResourceType.Wood;
        public int remainingAmount = 400;
        public int maxAmount = 400;
        public float harvestInterval = 1.0f; // 1 saniyede bir toplama
        public int gatherPerTick = 2;

        private void OnEnable()
        {
            AllSources.Add(this);
        }

        private void OnDisable()
        {
            AllSources.Remove(this);
        }

        private Renderer[] cachedRenderers;
        private Collider cachedCollider;
        private Light[] cachedLights;
        public bool isDiscovered { get; private set; } = false;
        public bool isDimmed { get; private set; } = false;
        private readonly Dictionary<Renderer, Color> baseColors = new Dictionary<Renderer, Color>();

        private void Awake()
        {
            // Collider kontrolü (Raycast ile tıklanabilmesi için)
            if (GetComponent<Collider>() == null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(2f, 3f, 2f);
            }
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
            cachedCollider = GetComponent<Collider>();
            cachedLights = GetComponentsInChildren<Light>(true);
            // Başlangıçta 3D dünyada gizli başla; FogOfWarManager görüş alanındakileri ilk frame'de anında açacak
            ApplyRenderersVisibility(false);
        }

        /// <summary>
        /// Savaş Sisi (Fog-of-War) durumuna göre kaynağın görünürlüğünü ve seçilebilirliğini ayarlar.
        /// RTS Kuralı: Bir kez keşfedilen kaynak (Ağaç, Altın, Böğürtlen) bir daha asla sis arkasına gizlenmez.
        /// </summary>
        public void SetFogVisibility(bool discovered)
        {
            // Eğer daha önce keşfedildiyse bir daha asla kapatma
            if (isDiscovered && !discovered) return;

            if (isDiscovered == discovered) return;

            bool newlyDiscovered = (!isDiscovered && discovered);
            isDiscovered = discovered;

            if (newlyDiscovered && resourceType == RTSResourceType.Gold)
            {
                RTSHUDController.Instance?.SetAiStatus("⭐ KEŞİF RAPORU: Yeni bir Altın Madeni keşfedildi!", Color.yellow);
                RTSAudioManager.Instance?.PlaySound(RTSSoundType.TechComplete);
            }

            ApplyRenderersVisibility(discovered);
        }

        public void ApplyRenderersVisibility(bool visible)
        {
            if (cachedRenderers == null || cachedRenderers.Length == 0)
            {
                cachedRenderers = GetComponentsInChildren<Renderer>(true);
            }
            if (cachedCollider == null)
            {
                cachedCollider = GetComponent<Collider>();
            }
            if (cachedLights == null || cachedLights.Length == 0)
            {
                cachedLights = GetComponentsInChildren<Light>(true);
            }

            if (cachedRenderers != null)
            {
                foreach (var r in cachedRenderers)
                {
                    if (r != null) r.enabled = visible;
                }
            }

            if (cachedCollider != null)
            {
                cachedCollider.enabled = visible;
            }

            if (cachedLights != null)
            {
                foreach (var l in cachedLights)
                {
                    if (l != null) l.enabled = visible && !isDimmed;
                }
            }
        }

        /// <summary>
        /// Keşfedilmiş ancak şu an aktif görüş alanı dışında kalan (Explored) kaynakları (Ağaç, Altın, Çalı)
        /// gri/silik tonda gösterir; tekrar aktif görüşe (Visible) girdiğinde canlandırır.
        /// </summary>
        public void SetShroudDimmed(bool dimmed)
        {
            if (!isDiscovered) return;
            if (isDimmed == dimmed && baseColors.Count > 0) return;
            isDimmed = dimmed;

            if (cachedRenderers == null || cachedRenderers.Length == 0)
            {
                cachedRenderers = GetComponentsInChildren<Renderer>(true);
            }

            if (cachedRenderers != null)
            {
                foreach (var r in cachedRenderers)
                {
                    if (r == null || r.sharedMaterial == null) continue;
                    if (!r.sharedMaterial.HasProperty("_Color")) continue;

                    if (!baseColors.TryGetValue(r, out Color origCol))
                    {
                        origCol = r.material.color;
                        baseColors[r] = origCol;
                    }

                    if (dimmed)
                    {
                        float lum = origCol.r * 0.3f + origCol.g * 0.59f + origCol.b * 0.11f;
                        Color greySilik = new Color(lum * 0.45f, lum * 0.47f, lum * 0.50f, origCol.a);
                        r.material.color = Color.Lerp(origCol * 0.48f, greySilik, 0.55f);
                    }
                    else
                    {
                        r.material.color = origCol;
                    }
                }
            }

            if (cachedLights != null)
            {
                foreach (var l in cachedLights)
                {
                    if (l != null) l.enabled = !dimmed;
                }
            }
        }

        private Vector3 initialScale = Vector3.zero;

        private void Start()
        {
            if (initialScale == Vector3.zero)
            {
                initialScale = transform.localScale;
            }
        }

        /// <summary>
        /// Köylü kaynaktan belirli miktar toplar.
        /// </summary>
        public int ExtractResource(int requestedAmount)
        {
            if (initialScale == Vector3.zero)
            {
                initialScale = (transform.localScale != Vector3.zero) ? transform.localScale : Vector3.one;
            }

            int gathered = Mathf.Min(requestedAmount, remainingAmount);
            remainingAmount -= gathered;

            // Görsel olarak küçülme veya tükenme
            float pct = Mathf.Clamp01((float)remainingAmount / Mathf.Max(1, maxAmount));
            if (resourceType == RTSResourceType.Gold)
            {
                transform.localScale = initialScale * Mathf.Lerp(0.45f, 1.0f, pct);
                RTSVFXManager.Instance?.SpawnCombatText(transform.position + Vector3.up * 2.2f, $"+{gathered} Altın", new Color(1.0f, 0.86f, 0.2f));
            }
            else if (resourceType == RTSResourceType.Wood)
            {
                transform.localScale = new Vector3(
                    initialScale.x * Mathf.Lerp(0.70f, 1.0f, pct),
                    initialScale.y * Mathf.Lerp(0.55f, 1.0f, pct),
                    initialScale.z * Mathf.Lerp(0.70f, 1.0f, pct)
                );
                RTSVFXManager.Instance?.SpawnCombatText(transform.position + Vector3.up * 2.8f, $"+{gathered} Odun", new Color(0.85f, 0.65f, 0.35f));
            }
            else if (resourceType == RTSResourceType.Food)
            {
                transform.localScale = initialScale * Mathf.Lerp(0.40f, 1.0f, pct);
                RTSVFXManager.Instance?.SpawnCombatText(transform.position + Vector3.up * 1.6f, $"+{gathered} Gıda", new Color(0.35f, 0.90f, 0.4f));
            }

            if (remainingAmount <= 0)
            {
                Debug.Log($"[Kaynak: {resourceType}] Kaynağı tamamen tükendi.");
                RTSVFXManager.Instance?.SpawnCombatText(transform.position + Vector3.up * 1.8f, "TÜKENDİ!", new Color(0.75f, 0.75f, 0.75f));
                Destroy(gameObject);
            }

            return gathered;
        }

        public static RTSResourceSource CreateGoldRock(Vector3 position, int amount = 800)
        {
            var mineRoot = new GameObject("Resource_GoldMine");
            mineRoot.transform.position = position;
            mineRoot.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

            // 1. Ana Masif Doğal Kaya Formasyonu (Rocks-Large FBX)
            GameObject rockPrefab = Resources.Load<GameObject>("Models/rocks-large") ?? Resources.Load<GameObject>("Models/RocksLarge");
            if (rockPrefab != null)
            {
                var baseRock = Object.Instantiate(rockPrefab, mineRoot.transform);
                baseRock.name = "Rock_Outcrop_Base";
                baseRock.transform.localPosition = Vector3.zero;
                baseRock.transform.localRotation = Quaternion.identity;
                baseRock.transform.localScale = new Vector3(1.7f, 1.5f, 1.7f);

                // Gerçekçi koyu granit/bazalt maden kayası materyali
                Material rockMat = new Material(Shader.Find("Standard"));
                rockMat.color = new Color(0.26f, 0.26f, 0.28f);
                rockMat.SetFloat("_Glossiness", 0.15f);

                foreach (var ren in baseRock.GetComponentsInChildren<Renderer>(true))
                {
                    ren.material = rockMat;
                }
                foreach (var col in baseRock.GetComponentsInChildren<Collider>(true))
                {
                    Object.DestroyImmediate(col);
                }
            }
            else
            {
                // Güvenli yedek kaya kütlesi
                var rockFallback = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(rockFallback.GetComponent<Collider>());
                rockFallback.transform.SetParent(mineRoot.transform, false);
                rockFallback.transform.localPosition = new Vector3(0, 0.8f, 0);
                rockFallback.transform.localScale = new Vector3(2.6f, 1.6f, 2.6f);
                rockFallback.GetComponent<Renderer>().material.color = new Color(0.26f, 0.26f, 0.28f);
            }

            // 2. Kaya Yüzeylerinden Fışkıran Parlak Altın Damarları & Kristalleri (Gold Ore Veins)
            Material goldOreMat = new Material(Shader.Find("Standard"));
            goldOreMat.color = new Color(1.0f, 0.82f, 0.12f); // Parlak asil altın
            goldOreMat.SetFloat("_Metallic", 0.90f);
            goldOreMat.SetFloat("_Glossiness", 0.82f);
            goldOreMat.EnableKeyword("_EMISSION");
            goldOreMat.SetColor("_EmissionColor", new Color(0.35f, 0.25f, 0.02f)); // Doğal altın parıltısı

            Vector3[] goldNuggetOffsets = new Vector3[]
            {
                new Vector3( 0.45f, 0.90f,  0.55f),
                new Vector3(-0.60f, 0.75f,  0.35f),
                new Vector3( 0.20f, 1.30f, -0.45f),
                new Vector3(-0.45f, 0.65f, -0.60f),
                new Vector3( 0.70f, 0.60f, -0.25f),
                new Vector3(-0.15f, 1.50f,  0.20f),
                new Vector3( 0.00f, 1.70f,  0.00f) // Tepe altın doruğu
            };

            Vector3[] nuggetScales = new Vector3[]
            {
                new Vector3(0.55f, 0.80f, 0.55f),
                new Vector3(0.65f, 0.65f, 0.65f),
                new Vector3(0.70f, 0.85f, 0.60f),
                new Vector3(0.50f, 0.70f, 0.50f),
                new Vector3(0.60f, 0.75f, 0.55f),
                new Vector3(0.65f, 0.90f, 0.65f),
                new Vector3(0.75f, 0.95f, 0.75f)
            };

            for (int i = 0; i < goldNuggetOffsets.Length; i++)
            {
                var goldChunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(goldChunk.GetComponent<Collider>());
                goldChunk.name = $"Gold_Vein_{i}";
                goldChunk.transform.SetParent(mineRoot.transform, false);
                goldChunk.transform.localPosition = goldNuggetOffsets[i];
                goldChunk.transform.localRotation = Quaternion.Euler(
                    Random.Range(20f, 65f),
                    Random.Range(0f, 360f),
                    Random.Range(20f, 65f)
                );
                goldChunk.transform.localScale = nuggetScales[i];
                goldChunk.GetComponent<Renderer>().material = goldOreMat;
            }

            // 3. Maden Çevresi Detayları: Küçük taş parçaları
            GameObject smallRockPrefab = Resources.Load<GameObject>("Models/rocks-small");
            if (smallRockPrefab != null)
            {
                var sr1 = Object.Instantiate(smallRockPrefab, mineRoot.transform);
                sr1.transform.localPosition = new Vector3(1.3f, 0, 0.8f);
                sr1.transform.localScale = Vector3.one * 0.8f;
                sr1.transform.localRotation = Quaternion.Euler(0, 45f, 0);

                var sr2 = Object.Instantiate(smallRockPrefab, mineRoot.transform);
                sr2.transform.localPosition = new Vector3(-1.2f, 0, -0.9f);
                sr2.transform.localScale = Vector3.one * 0.7f;
                sr2.transform.localRotation = Quaternion.Euler(0, 190f, 0);

                foreach (var col in mineRoot.GetComponentsInChildren<Collider>(true))
                {
                    Object.DestroyImmediate(col);
                }
            }

            // 4. Altın Yatağı Sıcak Işıltı Halosu (Subtle Golden Point Light)
            var lightGo = new GameObject("Gold_Aura_Light");
            lightGo.transform.SetParent(mineRoot.transform, false);
            lightGo.transform.localPosition = new Vector3(0, 1.2f, 0);
            var pLight = lightGo.AddComponent<Light>();
            pLight.type = LightType.Point;
            pLight.color = new Color(1.0f, 0.85f, 0.35f);
            pLight.range = 4.5f;
            pLight.intensity = 0.85f;

            // 5. Tıklama ve Seçim İçin Kök BoxCollider
            var boxCol = mineRoot.AddComponent<BoxCollider>();
            boxCol.center = new Vector3(0, 0.9f, 0);
            boxCol.size = new Vector3(3.0f, 2.2f, 3.0f);

            var src = mineRoot.AddComponent<RTSResourceSource>();
            src.resourceType = RTSResourceType.Gold;
            src.remainingAmount = amount;
            src.maxAmount = amount;
            return src;
        }

        public static RTSResourceSource CreateBerryBush(Vector3 position, int amount = 600)
        {
            var bushGo = new GameObject("Resource_BerryBush");
            bushGo.transform.position = position;

            // Ana Çalı Yaprak Gövdesi
            var foliage = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(foliage.GetComponent<Collider>());
            foliage.transform.SetParent(bushGo.transform, false);
            foliage.transform.localPosition = new Vector3(0, 0.65f, 0);
            foliage.transform.localScale = new Vector3(2.4f, 1.4f, 2.4f);
            var folRen = foliage.GetComponent<Renderer>();
            folRen.material.color = new Color(0.12f, 0.44f, 0.16f); // Doğal çalı yeşili

            var foliage2 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(foliage2.GetComponent<Collider>());
            foliage2.transform.SetParent(bushGo.transform, false);
            foliage2.transform.localPosition = new Vector3(0.35f, 0.95f, -0.25f);
            foliage2.transform.localScale = new Vector3(1.7f, 1.2f, 1.7f);
            var folRen2 = foliage2.GetComponent<Renderer>();
            folRen2.material.color = new Color(0.16f, 0.52f, 0.20f);

            // Kırmızı/Bordo Böğürtlen Meyve Taneleri
            Vector3[] berryOffsets = new Vector3[]
            {
                new Vector3( 0.65f, 0.90f,  0.55f),
                new Vector3(-0.65f, 0.85f,  0.45f),
                new Vector3( 0.25f, 1.15f, -0.65f),
                new Vector3(-0.55f, 0.95f, -0.55f),
                new Vector3( 0.75f, 0.80f, -0.35f),
                new Vector3(-0.10f, 1.35f,  0.25f)
            };

            foreach (var bOffset in berryOffsets)
            {
                var berry = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(berry.GetComponent<Collider>());
                berry.transform.SetParent(bushGo.transform, false);
                berry.transform.localPosition = bOffset;
                berry.transform.localScale = Vector3.one * 0.38f;
                var bRen = berry.GetComponent<Renderer>();
                bRen.material.color = new Color(0.88f, 0.12f, 0.22f); // Kırmızı böğürtlen
            }

            var col = bushGo.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.75f, 0);
            col.size = new Vector3(2.6f, 1.8f, 2.6f);

            var src = bushGo.AddComponent<RTSResourceSource>();
            src.resourceType = RTSResourceType.Food;
            src.remainingAmount = amount;
            src.maxAmount = amount;
            src.harvestInterval = 1.0f;
            src.gatherPerTick = 2;
            return src;
        }
    }
}
