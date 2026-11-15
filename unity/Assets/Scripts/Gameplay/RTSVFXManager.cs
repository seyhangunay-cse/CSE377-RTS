using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CSE377.Gameplay
{
    public enum RTSWaypointType
    {
        Move,       // Yeşil halka & yön oku (Hareket emri)
        Attack,     // Kırmızı hedef imleci & çapraz kılıçlar (Saldırı emri)
        Gather,     // Altın sarısı dişli/çember (Kaynak toplama emri)
        Construct   // Mavi / turkuaz çekiç & temel halkası (İnşaat emri)
    }

    /// <summary>
    /// Age of Empires tarzı görsel ve atmosferik zenginleştirme motoru (VFX Engine).
    /// 1. Dinamik animasyonlu hedef tıklama halkaları (Waypoint VFX).
    /// 2. Kılıç çarpışma kıvılcımları, ok darbe tozları ve kuşatma patlamaları.
    /// 3. Hasarlı binalardan tüten dumanlar ve bina çöküşünde yanan enkaz/harabe efektleri.
    /// 4. Köylü inşaat çekiçleme talaş ve toz efektleri.
    /// </summary>
    public class RTSVFXManager : MonoBehaviour
    {
        public static RTSVFXManager Instance { get; private set; }

        private Material sparkMaterial;
        private Material smokeMaterial;
        private Material fireMaterial;
        private Material waypointMoveMat;
        private Material waypointAttackMat;
        private Material waypointGatherMat;
        private Material waypointConstructMat;
        private Material rubbleCharredMat;

        private Dictionary<RTSBuilding, GameObject> activeBuildingSmokes = new Dictionary<RTSBuilding, GameObject>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }

            InitMaterials();
        }

        private void InitMaterials()
        {
            var standardShader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            var unlitShader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? standardShader;

            sparkMaterial = new Material(unlitShader) { color = new Color(1.0f, 0.85f, 0.25f, 1.0f) };
            
            smokeMaterial = new Material(standardShader) { color = new Color(0.22f, 0.22f, 0.22f, 0.85f) };
            smokeMaterial.SetFloat("_Glossiness", 0.05f);

            fireMaterial = new Material(unlitShader) { color = new Color(1.0f, 0.45f, 0.05f, 1.0f) };
            if (fireMaterial.HasProperty("_EmissionColor"))
            {
                fireMaterial.EnableKeyword("_EMISSION");
                fireMaterial.SetColor("_EmissionColor", new Color(1.0f, 0.40f, 0.05f) * 2.5f);
            }

            waypointMoveMat = new Material(unlitShader) { color = new Color(0.2f, 0.95f, 0.35f, 0.95f) };
            waypointAttackMat = new Material(unlitShader) { color = new Color(0.95f, 0.22f, 0.22f, 0.95f) };
            waypointGatherMat = new Material(unlitShader) { color = new Color(1.0f, 0.82f, 0.15f, 0.95f) };
            waypointConstructMat = new Material(unlitShader) { color = new Color(0.25f, 0.75f, 1.0f, 0.95f) };

            rubbleCharredMat = new Material(standardShader) { color = new Color(0.18f, 0.16f, 0.15f, 1.0f) };
            rubbleCharredMat.SetFloat("_Glossiness", 0.05f);
        }

        #region Animasyonlu Sağ Tık Hedef Halkası (Waypoint VFX)

        /// <summary>
        /// Sağ tıklandığında zeminde dönen, küçülüp açılan ve şık şekilde sönen taktiksel halka oluşturur.
        /// </summary>
        public void SpawnWaypointVFX(Vector3 position, RTSWaypointType type)
        {
            var root = new GameObject($"Waypoint_{type}");
            root.transform.position = position + Vector3.up * 0.06f;

            Color baseColor;
            Material baseMat;

            switch (type)
            {
                case RTSWaypointType.Attack:
                    baseColor = new Color(0.95f, 0.22f, 0.22f, 0.95f);
                    baseMat = waypointAttackMat;
                    break;
                case RTSWaypointType.Gather:
                    baseColor = new Color(1.0f, 0.82f, 0.15f, 0.95f);
                    baseMat = waypointGatherMat;
                    break;
                case RTSWaypointType.Construct:
                    baseColor = new Color(0.25f, 0.75f, 1.0f, 0.95f);
                    baseMat = waypointConstructMat;
                    break;
                default:
                    baseColor = new Color(0.2f, 0.95f, 0.35f, 0.95f);
                    baseMat = waypointMoveMat;
                    break;
            }

            // Dış Ana Çember (Outer Ring)
            var outerRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(outerRing.GetComponent<Collider>());
            outerRing.transform.SetParent(root.transform);
            outerRing.transform.localPosition = Vector3.zero;
            outerRing.transform.localScale = new Vector3(1.6f, 0.015f, 1.6f);
            outerRing.GetComponent<Renderer>().material = baseMat;

            // İç Merkez Halkası (Inner Ring)
            var innerRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(innerRing.GetComponent<Collider>());
            innerRing.transform.SetParent(root.transform);
            innerRing.transform.localPosition = new Vector3(0, 0.01f, 0);
            innerRing.transform.localScale = new Vector3(0.5f, 0.02f, 0.5f);
            innerRing.GetComponent<Renderer>().material = baseMat;

            // Taktiksel Çapraz İşaretçiler (4 Köşe Nişangah Çentiği)
            for (int i = 0; i < 4; i++)
            {
                var tick = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(tick.GetComponent<Collider>());
                tick.transform.SetParent(root.transform);
                float angle = i * 90f;
                Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                tick.transform.localPosition = dir * 0.95f + Vector3.up * 0.015f;
                tick.transform.localRotation = Quaternion.Euler(0, angle, 0);
                tick.transform.localScale = new Vector3(0.12f, 0.02f, 0.45f);
                tick.GetComponent<Renderer>().material = baseMat;
            }

            StartCoroutine(AnimateWaypoint(root, outerRing, innerRing));
        }

        private IEnumerator AnimateWaypoint(GameObject root, GameObject outerRing, GameObject innerRing)
        {
            float duration = 0.52f;
            float elapsed = 0f;

            Vector3 startOuterScale = new Vector3(0.4f, 0.015f, 0.4f);
            Vector3 targetOuterScale = new Vector3(1.8f, 0.015f, 1.8f);

            Vector3 startInnerScale = new Vector3(0.8f, 0.02f, 0.8f);
            Vector3 targetInnerScale = new Vector3(0.2f, 0.02f, 0.2f);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                if (root != null)
                {
                    // Hafif saat yönünde dönüş animasyonu
                    root.transform.Rotate(Vector3.up * (Time.deltaTime * 180f), Space.World);

                    // Dış halkanın genişleyip sönmesi
                    if (outerRing != null)
                    {
                        outerRing.transform.localScale = Vector3.Lerp(startOuterScale, targetOuterScale, Mathf.Sin(t * Mathf.PI * 0.5f));
                    }

                    // İç halkanın merkeze doğru daralması
                    if (innerRing != null)
                    {
                        innerRing.transform.localScale = Vector3.Lerp(startInnerScale, targetInnerScale, t);
                    }
                }

                yield return null;
            }

            if (root != null) Destroy(root);
        }

        #endregion

        #region Savaş Kıvılcım & Darbe Parçacıkları (Combat VFX)

        /// <summary>
        /// Kılıç ve mızrak temas anında etrafa fırlayan metal kıvılcımları üretir.
        /// </summary>
        public void SpawnMeleeSparkVFX(Vector3 position, Vector3 hitNormal)
        {
            int sparkCount = Random.Range(5, 8);
            for (int i = 0; i < sparkCount; i++)
            {
                var spark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(spark.GetComponent<Collider>());
                spark.transform.position = position + Random.insideUnitSphere * 0.25f + Vector3.up * 0.5f;
                spark.transform.localScale = Vector3.one * Random.Range(0.08f, 0.16f);
                spark.GetComponent<Renderer>().material = sparkMaterial;

                Vector3 velocity = (hitNormal + Random.insideUnitSphere * 0.8f).normalized * Random.Range(2.5f, 5.0f);
                StartCoroutine(AnimateSparkParticle(spark, velocity, Random.Range(0.20f, 0.35f)));
            }
        }

        /// <summary>
        /// Ok isabet ettiğinde küçük kıymık ve toz patlaması üretir.
        /// </summary>
        public void SpawnArrowImpactVFX(Vector3 position)
        {
            int debrisCount = Random.Range(3, 5);
            for (int i = 0; i < debrisCount; i++)
            {
                var splinter = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(splinter.GetComponent<Collider>());
                splinter.transform.position = position + Random.insideUnitSphere * 0.15f;
                splinter.transform.localScale = new Vector3(0.06f, 0.18f, 0.06f);
                splinter.transform.localRotation = Random.rotation;
                splinter.GetComponent<Renderer>().material = smokeMaterial;

                Vector3 velocity = (Vector3.up * 1.5f + Random.insideUnitSphere).normalized * Random.Range(1.8f, 3.2f);
                StartCoroutine(AnimateSparkParticle(splinter, velocity, Random.Range(0.25f, 0.40f)));
            }
        }

        /// <summary>
        /// Köylü bina inşa ederken çekiç vuruş noktasında uçuşan talaş ve toz üretir.
        /// </summary>
        public void SpawnHammerDustVFX(Vector3 position)
        {
            for (int i = 0; i < 3; i++)
            {
                var chip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(chip.GetComponent<Collider>());
                chip.transform.position = position + Random.insideUnitSphere * 0.15f + Vector3.up * 0.3f;
                chip.transform.localScale = Vector3.one * Random.Range(0.08f, 0.14f);
                chip.GetComponent<Renderer>().material = sparkMaterial;

                Vector3 velocity = (Vector3.up * 2f + Random.insideUnitSphere).normalized * Random.Range(1.2f, 2.5f);
                StartCoroutine(AnimateSparkParticle(chip, velocity, 0.28f));
            }
        }

        private IEnumerator AnimateSparkParticle(GameObject particle, Vector3 velocity, float lifetime)
        {
            float elapsed = 0f;
            Vector3 pos = particle.transform.position;
            Vector3 originalScale = particle.transform.localScale;

            while (elapsed < lifetime && particle != null)
            {
                elapsed += Time.deltaTime;
                velocity += Vector3.down * (9.8f * Time.deltaTime); // Yerçekimi
                pos += velocity * Time.deltaTime;
                particle.transform.position = pos;

                // Sona doğru küçülme
                float progress = elapsed / lifetime;
                particle.transform.localScale = Vector3.Lerp(originalScale, Vector3.zero, progress);

                yield return null;
            }

            if (particle != null) Destroy(particle);
        }

        #endregion

        #region Hasarlı Bina Dumanı & Yıkım Yangını (Building Fire & Smoke)

        /// <summary>
        /// %50 canın altındaki hasarlı binaların çatısında yükselen duman ve kor ateş efektini yönetir.
        /// %25 canın altında alev ışığı ve yoğun kor parçacıklarıyla yangın modu devreye girer.
        /// </summary>
        public void SetBuildingDamagedSmoke(RTSBuilding building, bool active, bool isCritical = false)
        {
            if (building == null) return;

            if (active)
            {
                if (!activeBuildingSmokes.ContainsKey(building) || activeBuildingSmokes[building] == null)
                {
                    var smokeGo = new GameObject("DamageSmoke_Root");
                    smokeGo.transform.SetParent(building.transform);
                    Vector3 fp = building.GetBuildingFootprint();
                    smokeGo.transform.localPosition = new Vector3(0, fp.y * 0.75f, 0);

                    // Duman simülasyonu bileşeni ekle
                    var smokeSim = smokeGo.AddComponent<RTSProceduralSmokePlume>();
                    smokeSim.smokeMat = smokeMaterial;
                    smokeSim.fireMat = fireMaterial;
                    smokeSim.isCriticalDamage = isCritical;

                    activeBuildingSmokes[building] = smokeGo;
                }
                else
                {
                    var plume = activeBuildingSmokes[building].GetComponent<RTSProceduralSmokePlume>();
                    if (plume != null)
                    {
                        plume.isCriticalDamage = isCritical;
                    }
                }
            }
            else
            {
                if (activeBuildingSmokes.TryGetValue(building, out var smokeGo) && smokeGo != null)
                {
                    Destroy(smokeGo);
                    activeBuildingSmokes.Remove(building);
                }
            }
        }

        /// <summary>
        /// Bina yıkıldığında geride kalan yanan enkaz harabesi (Rubble), göğe yükselen duman sütunu ve alevler üretir.
        /// </summary>
        public void SpawnBuildingCollapseVFX(Vector3 position, Vector3 size, RTSBuildingType type)
        {
            var ruinRoot = new GameObject($"Ruin_{type}");
            ruinRoot.transform.position = position;

            // 1. Yanmış temel tabanı (Charred Foundation Pad)
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(pad.GetComponent<Collider>());
            pad.transform.SetParent(ruinRoot.transform);
            pad.transform.localPosition = new Vector3(0, 0.05f, 0);
            pad.transform.localScale = new Vector3(size.x * 0.95f, 0.12f, size.z * 0.95f);
            pad.GetComponent<Renderer>().material = rubbleCharredMat;

            // 2. Yıkılmış taş ve ahşap blok yığınları (Broken Rubble Piles)
            int rubbleCount = Mathf.Clamp(Mathf.RoundToInt(size.x * 2.5f), 6, 14);
            for (int i = 0; i < rubbleCount; i++)
            {
                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(block.GetComponent<Collider>());
                block.transform.SetParent(ruinRoot.transform);
                block.transform.localPosition = new Vector3(
                    Random.Range(-size.x * 0.42f, size.x * 0.42f),
                    Random.Range(0.2f, 0.8f),
                    Random.Range(-size.z * 0.42f, size.z * 0.42f));
                block.transform.localScale = new Vector3(Random.Range(0.6f, 1.4f), Random.Range(0.4f, 1.0f), Random.Range(0.6f, 1.4f));
                block.transform.localRotation = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0, 360), Random.Range(-25f, 25f));
                block.GetComponent<Renderer>().material = (i % 2 == 0) ? rubbleCharredMat : smokeMaterial;
            }

            // 3. Kalıcı Yükselen Yangın & Duman Sütunu (12 saniye tüter)
            var plumeGo = new GameObject("Ruin_Smoke_Plume");
            plumeGo.transform.SetParent(ruinRoot.transform);
            plumeGo.transform.localPosition = new Vector3(0, 0.6f, 0);
            var plume = plumeGo.AddComponent<RTSProceduralSmokePlume>();
            plume.smokeMat = smokeMaterial;
            plume.fireMat = fireMaterial;
            plume.isHeavyRuin = true;

            // 4. Titreşen sıcak alev ışığı (Flickering Fire Point Light)
            var fireLightGo = new GameObject("FireLight");
            fireLightGo.transform.SetParent(ruinRoot.transform);
            fireLightGo.transform.localPosition = new Vector3(0, 1.2f, 0);
            var fireLight = fireLightGo.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = new Color(1.0f, 0.55f, 0.15f);
            fireLight.range = 7f;
            fireLight.intensity = 2.2f;

            // 12 saniye sonra yavaşça toprağa batarak kaybolur
            StartCoroutine(FadeAndDestroyRuin(ruinRoot, fireLight, 12f));
        }

        private IEnumerator FadeAndDestroyRuin(GameObject ruin, Light fireLight, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                // Alev ışığı titreşimi
                if (fireLight != null)
                {
                    fireLight.intensity = Mathf.Lerp(2.2f, 0.2f, elapsed / duration) + Mathf.Sin(Time.time * 16f) * 0.4f;
                }

                yield return null;
            }

            // Yavaşça toprağa batma (Sink into ground)
            float sinkTime = 3.0f;
            float sinkElapsed = 0f;
            Vector3 startPos = ruin != null ? ruin.transform.position : Vector3.zero;

            while (sinkElapsed < sinkTime && ruin != null)
            {
                sinkElapsed += Time.deltaTime;
                ruin.transform.position = startPos + Vector3.down * Mathf.Lerp(0f, 1.2f, sinkElapsed / sinkTime);
                yield return null;
            }

            if (ruin != null) Destroy(ruin);
        }

        #endregion

        #region Yüzen Savaş Hasar Metinleri (Floating Combat Text)

        /// <summary>
        /// Hedefin üzerinde göğe doğru yükselen ve sönen hasar / tamirat metni gösterir.
        /// </summary>
        public void SpawnCombatText(Vector3 position, string text, Color color)
        {
            var textGo = new GameObject("Combat_Text");
            textGo.transform.position = position + Vector3.up * 1.5f + Random.insideUnitSphere * 0.15f;

            var tm = textGo.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 28;
            tm.characterSize = 0.09f;
            tm.color = color;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) tm.font = font;

            var fct = textGo.AddComponent<RTSFloatingCombatText>();
            fct.textMesh = tm;
        }

        #endregion
    }

    /// <summary>
    /// Harici doku veya Unity Particle paketi gerektirmeden saf matematiksel dalga ve ölçeklemeyle
    /// binalardan göğe yükselen gerçekçi ortaçağ duman bulutları simüle eden bileşen.
    /// </summary>
    public class RTSProceduralSmokePlume : MonoBehaviour
    {
        public Material smokeMat;
        public Material fireMat;
        public bool isHeavyRuin = false;
        public bool isCriticalDamage = false;

        private List<Transform> smokePuffs = new List<Transform>();
        private List<float> puffBirthTimes = new List<float>();
        private List<Vector3> puffDrifts = new List<Vector3>();

        private float spawnInterval = 0.22f;
        private float nextSpawnTime = 0f;
        private float maxPuffLifetime = 2.2f;
        private Light flameLight;

        private void Start()
        {
            if (isCriticalDamage || isHeavyRuin)
            {
                var lightGo = new GameObject("PlumeFlameLight");
                lightGo.transform.SetParent(transform, false);
                lightGo.transform.localPosition = Vector3.up * 0.2f;
                flameLight = lightGo.AddComponent<Light>();
                flameLight.type = LightType.Point;
                flameLight.color = new Color(1.0f, 0.55f, 0.15f);
                flameLight.range = isHeavyRuin ? 8f : 5.5f;
                flameLight.intensity = isHeavyRuin ? 2.5f : 1.6f;
            }
        }

        private void Update()
        {
            if (smokeMat == null) return;

            if (flameLight != null)
            {
                flameLight.intensity = (isHeavyRuin ? 2.2f : 1.4f) + Mathf.Sin(Time.time * 18f) * 0.45f;
            }

            float currentInterval = (isCriticalDamage || isHeavyRuin) ? 0.15f : spawnInterval;
            if (Time.time >= nextSpawnTime)
            {
                nextSpawnTime = Time.time + currentInterval;
                SpawnPuff();
            }

            // Mevcut pufları yukarı doğru yükselt, sinüs zarfı ile ölçekle ve söndür
            for (int i = smokePuffs.Count - 1; i >= 0; i--)
            {
                var p = smokePuffs[i];
                if (p == null)
                {
                    smokePuffs.RemoveAt(i);
                    puffBirthTimes.RemoveAt(i);
                    puffDrifts.RemoveAt(i);
                    continue;
                }

                float age = Time.time - puffBirthTimes[i];
                if (age >= maxPuffLifetime)
                {
                    Destroy(p.gameObject);
                    smokePuffs.RemoveAt(i);
                    puffBirthTimes.RemoveAt(i);
                    puffDrifts.RemoveAt(i);
                    continue;
                }

                float t = age / maxPuffLifetime;

                // Yukarı doğru yükseliş ve hafif rüzgar savrulması
                float riseSpeed = isHeavyRuin ? 2.0f : 1.3f;
                p.position += (Vector3.up * riseSpeed + puffDrifts[i]) * Time.deltaTime;

                // Doğal sinüs zarfı: 0'dan başlar, ortada zirveye ulaşır, sona doğru 0'a inerek pürüzsüz kaybolur!
                float scaleEnvelope = Mathf.Sin(t * Mathf.PI);
                float maxScale = isHeavyRuin ? 1.1f : (isCriticalDamage ? 0.60f : 0.40f);
                p.localScale = Vector3.one * (maxScale * scaleEnvelope);

                // Dumanın kendi etrafında yavaşça dönmesi
                p.Rotate(Vector3.up * (Time.deltaTime * 30f), Space.World);
            }
        }

        private void SpawnPuff()
        {
            // Low-poly kor ve duman küp parçacığı (Billiard ball küre yerine şık ortaçağ parçacığı)
            var puff = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(puff.GetComponent<Collider>());
            puff.transform.SetParent(transform);
            puff.transform.localPosition = Random.insideUnitSphere * (isHeavyRuin ? 0.6f : 0.25f);
            puff.transform.localScale = Vector3.zero;
            puff.transform.localRotation = Random.rotation;

            bool isFireSpark = (Random.value < 0.35f || isCriticalDamage || isHeavyRuin);
            puff.GetComponent<Renderer>().material = (isFireSpark && fireMat != null) ? fireMat : smokeMat;

            smokePuffs.Add(puff.transform);
            puffBirthTimes.Add(Time.time);
            puffDrifts.Add(new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(0f, 0.15f), Random.Range(-0.25f, 0.25f)));
        }

        private void OnDestroy()
        {
            foreach (var p in smokePuffs)
            {
                if (p != null) Destroy(p.gameObject);
            }
            smokePuffs.Clear();
        }
    }

    #region Yüzen Savaş Hasar Metinleri (Floating Combat Text)

    public class RTSFloatingCombatText : MonoBehaviour
    {
        public TextMesh textMesh;
        private float lifetime = 0.85f;
        private float elapsed = 0f;
        private Vector3 driftVelocity;
        private Color startColor;

        private void Start()
        {
            driftVelocity = new Vector3(Random.Range(-0.25f, 0.25f), 2.0f, Random.Range(-0.25f, 0.25f));
            if (textMesh != null) startColor = textMesh.color;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            transform.position += driftVelocity * Time.deltaTime;

            Camera cam = Camera.main;
            if (cam != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
            }

            float t = elapsed / lifetime;
            if (textMesh != null)
            {
                textMesh.color = new Color(startColor.r, startColor.g, startColor.b, 1f - (t * t));
            }

            if (elapsed >= lifetime)
            {
                Destroy(gameObject);
            }
        }
    }

    #endregion
}
