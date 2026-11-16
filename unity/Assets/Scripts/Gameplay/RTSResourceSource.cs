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

        private static readonly Dictionary<string, Material> envMaterialCache = new Dictionary<string, Material>();

        private static string ResolveEnvMaterialKey(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return "granite";
            string s = rawName.ToLowerInvariant();
            if (s.Contains("foliage_berry")) return "foliage_berry";
            if (s.Contains("foliage_oak")) return "foliage_oak";
            if (s.Contains("foliage_pine")) return "foliage_pine";
            if (s.Contains("bark_oak")) return "bark_oak";
            if (s.Contains("bark_pine")) return "bark_pine";
            if (s.Contains("dark_stone")) return "dark_stone";
            if (s.Contains("window_glow")) return "window_glow";
            if (s.Contains("granite")) return "granite";
            if (s.Contains("cobble")) return "cobble";
            if (s.Contains("stone")) return "stone";
            if (s.Contains("timber")) return "timber";
            if (s.Contains("iron")) return "iron";
            if (s.Contains("gold")) return "gold";
            if (s.Contains("soil")) return "soil";
            if (s.Contains("berry")) return "berry";
            if (s.Contains("blossom")) return "blossom";
            if (s.Contains("wicker")) return "wicker";
            return "granite";
        }

        public static Material GetEnvironmentMaterial(string matKey)
        {
            if (envMaterialCache.TryGetValue(matKey, out var cached) && cached != null)
            {
                return cached;
            }

            var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            var mat = new Material(shader)
            {
                name = $"Mat_Env_{matKey}",
                color = Color.white
            };

            string albedoPath = null;
            string normalPath = null;
            float metallic = 0.0f;
            float smoothness = 0.18f;
            Color tint = Color.white;
            Color emission = Color.black;

            switch (matKey)
            {
                case "dark_stone":
                    albedoPath = "Textures/Buildings/stone_dark_albedo";
                    normalPath = "Textures/Buildings/stone_dark_normal";
                    smoothness = 0.20f;
                    break;
                case "stone":
                    albedoPath = "Textures/Buildings/stone_ashlar_albedo";
                    normalPath = "Textures/Buildings/stone_ashlar_normal";
                    smoothness = 0.22f;
                    break;
                case "cobble":
                    albedoPath = "Textures/Buildings/courtyard_cobble_albedo";
                    normalPath = "Textures/Buildings/courtyard_cobble_normal";
                    smoothness = 0.18f;
                    break;
                case "timber":
                    albedoPath = "Textures/Buildings/timber_oak_albedo";
                    normalPath = "Textures/Buildings/timber_oak_normal";
                    smoothness = 0.20f;
                    break;
                case "iron":
                    albedoPath = "Textures/Buildings/wrought_iron_albedo";
                    normalPath = "Textures/Buildings/wrought_iron_normal";
                    metallic = 0.80f;
                    smoothness = 0.55f;
                    break;
                case "soil":
                    albedoPath = "Textures/Buildings/farm_soil_albedo";
                    normalPath = "Textures/Buildings/farm_soil_normal";
                    smoothness = 0.08f;
                    break;
                case "window_glow":
                    albedoPath = "Textures/Buildings/window_glow_albedo";
                    smoothness = 0.45f;
                    emission = new Color(1.0f, 0.72f, 0.28f) * 1.15f;
                    break;
                case "granite":
                    albedoPath = "Textures/Environment/granite_rock_albedo";
                    normalPath = "Textures/Environment/granite_rock_normal";
                    smoothness = 0.16f;
                    break;
                case "gold":
                    albedoPath = "Textures/Environment/gold_ore_albedo";
                    normalPath = "Textures/Environment/gold_ore_normal";
                    metallic = 0.88f;
                    smoothness = 0.82f;
                    emission = new Color(0.28f, 0.20f, 0.02f);
                    break;
                case "bark_oak":
                    albedoPath = "Textures/Environment/bark_oak_albedo";
                    normalPath = "Textures/Environment/bark_oak_normal";
                    smoothness = 0.12f;
                    break;
                case "bark_pine":
                    albedoPath = "Textures/Environment/bark_pine_albedo";
                    normalPath = "Textures/Environment/bark_pine_normal";
                    smoothness = 0.12f;
                    break;
                case "foliage_oak":
                    albedoPath = "Textures/Environment/foliage_oak_albedo";
                    normalPath = "Textures/Environment/foliage_oak_normal";
                    smoothness = 0.20f;
                    break;
                case "foliage_pine":
                    albedoPath = "Textures/Environment/foliage_pine_albedo";
                    normalPath = "Textures/Environment/foliage_pine_normal";
                    smoothness = 0.18f;
                    break;
                case "foliage_berry":
                    albedoPath = "Textures/Environment/foliage_berry_albedo";
                    normalPath = "Textures/Environment/foliage_berry_normal";
                    smoothness = 0.22f;
                    break;
                case "berry":
                    albedoPath = "Textures/Environment/strawberry_albedo";
                    normalPath = "Textures/Environment/strawberry_normal";
                    smoothness = 0.48f;
                    break;
                case "blossom":
                    tint = new Color(0.98f, 0.96f, 0.90f);
                    smoothness = 0.25f;
                    break;
                case "wicker":
                    albedoPath = "Textures/Environment/wicker_basket_albedo";
                    normalPath = "Textures/Environment/wicker_basket_normal";
                    smoothness = 0.18f;
                    break;
            }

            if (!string.IsNullOrEmpty(albedoPath))
            {
                var tex = Resources.Load<Texture2D>(albedoPath);
                if (tex != null) mat.mainTexture = tex;
            }
            mat.color = tint;

            if (shader != null && shader.name == "Standard")
            {
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Glossiness", smoothness);

                if (!string.IsNullOrEmpty(normalPath))
                {
                    var nTex = Resources.Load<Texture2D>(normalPath);
                    if (nTex != null)
                    {
                        mat.EnableKeyword("_NORMALMAP");
                        mat.SetTexture("_BumpMap", nTex);
                        mat.SetFloat("_BumpScale", 0.75f);
                    }
                }

                if (emission != Color.black && mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", emission);
                }
            }

            envMaterialCache[matKey] = mat;
            return mat;
        }

        public static void ApplyEnvironmentMaterials(GameObject root)
        {
            if (root == null) return;
            foreach (var ren in root.GetComponentsInChildren<Renderer>(true))
            {
                if (ren == null) continue;
                var shared = ren.sharedMaterials;
                if (shared == null || shared.Length <= 1)
                {
                    string srcName = (shared != null && shared.Length == 1 && shared[0] != null)
                        ? $"{ren.gameObject.name}_{shared[0].name}"
                        : ren.gameObject.name;
                    ren.sharedMaterial = GetEnvironmentMaterial(ResolveEnvMaterialKey(srcName));
                }
                else
                {
                    var mats = new Material[shared.Length];
                    for (int i = 0; i < shared.Length; i++)
                    {
                        string slotName = (shared[i] != null) ? shared[i].name : ren.gameObject.name;
                        mats[i] = GetEnvironmentMaterial(ResolveEnvMaterialKey(slotName));
                    }
                    ren.sharedMaterials = mats;
                }
            }
        }

        public static RTSResourceSource CreateGoldRock(Vector3 position, int amount = 800)
        {
            var mineRoot = new GameObject("Resource_GoldMine");
            mineRoot.transform.position = new Vector3(position.x, 0f, position.z);
            mineRoot.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

            GameObject customGoldPrefab = Resources.Load<GameObject>("Models/Environment/GoldMine");
            if (customGoldPrefab != null)
            {
                var model = Object.Instantiate(customGoldPrefab, mineRoot.transform);
                model.name = "GoldMine_Model";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                foreach (var col in model.GetComponentsInChildren<Collider>(true))
                {
                    Object.DestroyImmediate(col);
                }
                ApplyEnvironmentMaterials(model);
            }
            else
            {
                // Güvenli yedek (Resources/Models/Environment/GoldMine yoksa)
                GameObject rockPrefab = Resources.Load<GameObject>("Models/rocks-large") ?? Resources.Load<GameObject>("Models/RocksLarge");
                if (rockPrefab != null)
                {
                    var baseRock = Object.Instantiate(rockPrefab, mineRoot.transform);
                    baseRock.transform.localScale = new Vector3(1.7f, 1.5f, 1.7f);
                    foreach (var col in baseRock.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
                }
            }

            // 4. Altın Yatağı Sıcak Işıltı Halosu
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
            bushGo.transform.position = new Vector3(position.x, 0f, position.z);
            bushGo.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);

            GameObject customBerryPrefab = Resources.Load<GameObject>("Models/Environment/BerryBush");
            if (customBerryPrefab != null)
            {
                var model = Object.Instantiate(customBerryPrefab, bushGo.transform);
                model.name = "BerryBush_Model";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                foreach (var col in model.GetComponentsInChildren<Collider>(true))
                {
                    Object.DestroyImmediate(col);
                }
                ApplyEnvironmentMaterials(model);
            }
            else
            {
                // Güvenli yedek (Resources/Models/Environment/BerryBush yoksa)
                var foliage = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(foliage.GetComponent<Collider>());
                foliage.transform.SetParent(bushGo.transform, false);
                foliage.transform.localPosition = new Vector3(0, 0.65f, 0);
                foliage.transform.localScale = new Vector3(2.4f, 1.4f, 2.4f);
                foliage.GetComponent<Renderer>().material.color = new Color(0.12f, 0.44f, 0.16f);
            }

            var colBox = bushGo.AddComponent<BoxCollider>();
            colBox.center = new Vector3(0, 0.75f, 0);
            colBox.size = new Vector3(2.6f, 1.8f, 2.6f);

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
