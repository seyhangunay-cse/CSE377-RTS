using System.Collections.Generic;
using UnityEngine;

namespace CSE377.Gameplay
{
    public enum RTSBuildingType
    {
        TownCenter,
        Barracks,
        DefenseTower,
        Blacksmith,
        House,
        Farm,
        StoneWall,
        CityGate,
        Castle,
        ArcheryRange,
        SiegeWorkshop,
        Market
    }

    /// <summary>
    /// RTS binalarını (Kışla, Savunma Kulesi, Demirci, Şehir Merkezi) yönetir.
    /// İnşaat süreci (Progress: %0 -> %100), can barları, köylülerin inşa etmesi
    /// ve Savunma Kulesi'nin otomatik ok atışı mekaniğini içerir.
    /// </summary>
    public class RTSBuilding : MonoBehaviour
    {
        public static readonly List<RTSBuilding> AllBuildings = new List<RTSBuilding>();

        [Header("Bina Kimliği")]
        public RTSBuildingType buildingType = RTSBuildingType.Barracks;
        public UnitTeam team = UnitTeam.Player;
        public string displayName = "Bina";

        [Header("Can ve İnşaat")]
        public int maxHp = 500;
        public int currentHp = 500;
        public bool isConstructed = true;
        public float constructionProgress = 100f; // 0..100

        [Header("Savunma Kulesi Ayarları")]
        public float attackRange = 14f;
        public int towerDamage = 12;
        public float attackCooldown = 1.6f;
        public float sightRadius = 12f;
        private float nextAttackTime = 0f;

        [Header("Görsel ve UI")]
        public Transform visualRoot;
        private Renderer mainRenderer;
        private Color baseColor;

        private static Material playerBuildingMaterial;
        private static Material enemyBuildingMaterial;

        private void OnEnable()
        {
            AllBuildings.Add(this);
        }

        private void OnDisable()
        {
            AllBuildings.Remove(this);
        }

        private void Awake()
        {
            if (visualRoot == null)
            {
                var v = transform.Find("VisualRoot");
                if (v != null) visualRoot = v;
            }

            mainRenderer = GetComponentInChildren<Renderer>();
            if (mainRenderer != null)
            {
                baseColor = mainRenderer.material.color;
            }

            if (GetComponent<Collider>() == null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(3.5f, 4f, 3.5f);
                box.center = new Vector3(0, 2f, 0);
            }

            if (isConstructed && buildingType == RTSBuildingType.Farm)
            {
                var res = gameObject.GetComponent<RTSResourceSource>() ?? gameObject.AddComponent<RTSResourceSource>();
                res.resourceType = RTSResourceType.Food;
                res.remainingAmount = 1500;
                res.maxAmount = 1500;
                res.harvestInterval = 0.9f;
                res.gatherPerTick = 2;
            }

            UpdateVisualProgress();

            if (!isConstructed)
            {
                gameObject.AddComponent<RTSConstructionScaffold>();
            }
        }

        public void AddConstructionProgress(float amount)
        {
            if (isConstructed) return;

            constructionProgress = Mathf.Min(100f, constructionProgress + amount);
            currentHp = Mathf.Max(1, Mathf.RoundToInt((constructionProgress / 100f) * maxHp));
            UpdateVisualProgress();

            if (constructionProgress >= 100f)
            {
                CompleteConstruction();
            }
        }

        private void CompleteConstruction()
        {
            isConstructed = true;
            constructionProgress = 100f;
            currentHp = maxHp;
            UpdateVisualProgress();

            var scaffold = GetComponent<RTSConstructionScaffold>();
            if (scaffold != null)
            {
                scaffold.DismantleScaffold();
            }

            // Tamamlanma parlaması / kutlama efekti
            var spark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(spark.GetComponent<Collider>());
            spark.transform.position = transform.position + Vector3.up * 2.5f;
            spark.transform.localScale = Vector3.one * 3f;
            var ren = spark.GetComponent<Renderer>();
            if (ren != null) ren.material.color = Color.yellow;
            Destroy(spark, 0.45f);

            Debug.Log($"[İNŞAAT TAMAMLANDI] '{displayName}' 3D yapısı başarıyla tamamlandı!");

            if (team == RTSGameManager.LocalPlayerTeam)
            {
                RTSAudioManager.Instance?.PlaySound(RTSSoundType.TechComplete);
                RTSGameManager.Instance?.RecordBuildingConstructed();
            }

            // Nüfus ve ekonomi HUD güncellemesi
            RTSGameManager.Instance?.UpdateHUD();

            if (buildingType == RTSBuildingType.House && team == RTSGameManager.LocalPlayerTeam)
            {
                RTSHUDController.Instance?.SetAiStatus("[EV İNŞA EDİLDİ] Nüfus tavanınız +5 artırıldı!", Color.green);
            }
            else if (buildingType == RTSBuildingType.Farm)
            {
                var res = gameObject.GetComponent<RTSResourceSource>() ?? gameObject.AddComponent<RTSResourceSource>();
                res.resourceType = RTSResourceType.Food;
                res.remainingAmount = 1500;
                res.maxAmount = 1500;
                res.harvestInterval = 0.9f;
                res.gatherPerTick = 2;

                if (team == RTSGameManager.LocalPlayerTeam)
                {
                    RTSHUDController.Instance?.SetAiStatus("[TARLA İNŞA EDİLDİ] Buğday tarlanız hazır! Hasat başladı.", Color.green);
                }

                // Tarlayı inşa eden işçiyi doğrudan tarlanın yanında hasada ata
                foreach (var u in RTSUnit.AllUnits)
                {
                    if (u != null && u.team == this.team && u.unitType.ToLower() == "villager" && u.currentHp > 0)
                    {
                        var v = u.GetComponent<RTSVillager>();
                        if (v != null && (v.currentBuildingTask == this || Vector3.Distance(u.transform.position, transform.position) <= 5.5f))
                        {
                            v.AssignHarvestTask(res);
                        }
                    }
                }
            }
        }

        public void CheckDamageVisualState()
        {
            if (currentHp <= 0 || !isConstructed)
            {
                RTSVFXManager.Instance?.SetBuildingDamagedSmoke(this, false);
            }
            else if (currentHp <= maxHp * 0.25f)
            {
                RTSVFXManager.Instance?.SetBuildingDamagedSmoke(this, true, isCritical: true);
            }
            else if (currentHp <= maxHp * 0.5f)
            {
                RTSVFXManager.Instance?.SetBuildingDamagedSmoke(this, true, isCritical: false);
            }
            else
            {
                RTSVFXManager.Instance?.SetBuildingDamagedSmoke(this, false);
            }
        }

        public bool isDestroyed { get; private set; } = false;

        public void TakeDamage(int damage, UnitTeam? attackerTeam = null)
        {
            if (isDestroyed || currentHp <= 0) return;

            // Binalar kendi takımlarından hasar alamaz (Dost ateşi ve kendi kendine yıkılma önlendi)
            if (attackerTeam.HasValue && attackerTeam.Value == this.team)
            {
                return;
            }

            currentHp = Mathf.Max(0, currentHp - damage);

            // Yüzen Kuşatma Hasar Metni (Floating Siege Combat Text)
            RTSVFXManager.Instance?.SpawnCombatText(transform.position + Vector3.up * 2.8f, $"-{damage}", new Color(1.0f, 0.45f, 0.20f));

            if (team == RTSGameManager.LocalPlayerTeam)
            {
                RTSMinimapController.Instance?.AddPing(transform.position, new Color32(255, 45, 45, 255), 5.5f);
                RTSAudioManager.Instance?.NotifyCombat();
            }

            // Hasar durumuna göre duman/yangın efektini dinamik güncelle
            CheckDamageVisualState();

            // Çok oyunculu modda bina hasarını ve yıkımını karşı oyuncuya anında senkronize et
            RTSNetworkSyncManager.Instance?.BroadcastBuildingHpSync(this, currentHp <= 0);

            if (currentHp <= 0)
            {
                Die(fromNetwork: false);
            }
        }

        public void ApplyNetworkHp(int syncedHp)
        {
            if (isDestroyed) return;
            if (syncedHp < currentHp)
            {
                int diff = currentHp - Mathf.Max(0, syncedHp);
                currentHp = Mathf.Max(0, syncedHp);
                if (diff > 0)
                {
                    RTSVFXManager.Instance?.SpawnCombatText(transform.position + Vector3.up * 2.8f, $"-{diff}", new Color(1.0f, 0.45f, 0.20f));
                }
                if (team == RTSGameManager.LocalPlayerTeam)
                {
                    RTSMinimapController.Instance?.AddPing(transform.position, new Color32(255, 45, 45, 255), 5.5f);
                    RTSAudioManager.Instance?.NotifyCombat();
                }
                CheckDamageVisualState();
            }
            if (currentHp <= 0 || syncedHp <= 0)
            {
                currentHp = 0;
                Die(fromNetwork: true);
            }
        }

        private void Die(bool fromNetwork = false)
        {
            if (isDestroyed) return;
            isDestroyed = true;

            if (!fromNetwork)
            {
                RTSNetworkSyncManager.Instance?.BroadcastBuildingHpSync(this, forceZero: true);
            }

            Debug.LogWarning($"[{team}] '{displayName}' binası imha edildi!");
            AllBuildings.Remove(this);

            Vector3 footprint = GetBuildingFootprint();
            RTSVFXManager.Instance?.SetBuildingDamagedSmoke(this, false);
            RTSVFXManager.Instance?.SpawnBuildingCollapseVFX(transform.position, footprint, buildingType);

            RTSAudioManager.Instance?.PlaySound(RTSSoundType.BuildingCollapse);

            RTSGameManager.Instance?.OnBuildingDestroyed(this);
            RTSGameManager.Instance?.UpdateHUD();
            Destroy(gameObject, 0.25f);
        }

        public Vector3 GetBuildingFootprint()
        {
            switch (buildingType)
            {
                case RTSBuildingType.TownCenter: return new Vector3(7f, 5f, 7f);
                case RTSBuildingType.Castle: return new Vector3(8.5f, 8f, 8.5f);
                case RTSBuildingType.Barracks: return new Vector3(5f, 4f, 5f);
                case RTSBuildingType.ArcheryRange: return new Vector3(5f, 3.5f, 5f);
                case RTSBuildingType.SiegeWorkshop: return new Vector3(5.5f, 4f, 5.5f);
                case RTSBuildingType.Market: return new Vector3(5f, 3.5f, 5f);
                case RTSBuildingType.Blacksmith: return new Vector3(4.5f, 3.5f, 4.5f);
                case RTSBuildingType.DefenseTower: return new Vector3(3f, 6.5f, 3f);
                case RTSBuildingType.House: return new Vector3(3.8f, 3f, 3.8f);
                case RTSBuildingType.Farm: return new Vector3(4.5f, 1f, 4.5f);
                default: return new Vector3(4f, 3f, 4f);
            }
        }

        private void Update()
        {
            // İnşaatı tamamlanmış Savunma Kuleleri ve Şanlı Kaleler otomatik savunma atışı yapar
            if ((buildingType == RTSBuildingType.DefenseTower || buildingType == RTSBuildingType.Castle) && isConstructed)
            {
                HandleTowerDefense();
            }
        }

        private void HandleTowerDefense()
        {
            if (Time.time < nextAttackTime) return;

            int bonusDmg = 0;
            if (RTSTechManager.Instance != null && RTSTechManager.Instance.HasTech("Fletching"))
            {
                bonusDmg = 2;
            }

            if (buildingType == RTSBuildingType.Castle)
            {
                // Şanlı Kale Çoklu Ok Yaylımı (Castle Multi-Arrow Barrage):
                // Menzildeki en fazla 3 farklı düşmana aynı anda ok yağdırır!
                int arrowsFired = 0;
                foreach (var u in RTSUnit.AllUnits)
                {
                    if (u != null && u.team != this.team && u.currentHp > 0)
                    {
                        float d = Vector3.Distance(transform.position, u.transform.position);
                        if (d <= attackRange)
                        {
                            Vector3 shootPos = transform.position + Vector3.up * 8.5f + Random.insideUnitSphere * 1.5f;
                            shootPos.y = transform.position.y + 8.5f;
                            RTSProjectile.FireArrow(shootPos, u, towerDamage + bonusDmg, null, this.team);
                            arrowsFired++;
                            if (arrowsFired >= 3) break;
                        }
                    }
                }

                if (arrowsFired > 0)
                {
                    nextAttackTime = Time.time + attackCooldown;
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.ArrowShoot);
                }
            }
            else
            {
                // Savunma Kulesi: Tekli ok atışı
                RTSUnit nearestEnemy = null;
                float nearestDist = attackRange;

                foreach (var u in RTSUnit.AllUnits)
                {
                    if (u != null && u.team != this.team && u.currentHp > 0)
                    {
                        float d = Vector3.Distance(transform.position, u.transform.position);
                        if (d < nearestDist)
                        {
                            nearestDist = d;
                            nearestEnemy = u;
                        }
                    }
                }

                if (nearestEnemy != null)
                {
                    nextAttackTime = Time.time + attackCooldown;
                    Vector3 shootPos = transform.position + Vector3.up * 6.0f;
                    RTSProjectile.FireArrow(shootPos, nearestEnemy, towerDamage + bonusDmg, null, this.team);
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.ArrowShoot);
                }
            }
        }

        public void UpdateHealthBar()
        {
            // Can barları temiz performans ve fizik stabilitesi için kaldırıldı
        }

        private void UpdateVisualProgress()
        {
            if (visualRoot != null)
            {
                if (!isConstructed)
                {
                    float prog = Mathf.Clamp01(constructionProgress / 100f);
                    visualRoot.localScale = new Vector3(1f, Mathf.Lerp(0.2f, 1.0f, prog), 1f);
                }
                else
                {
                    visualRoot.localScale = Vector3.one;
                }
            }
        }

        private static Material kenneyCastleMaterial;

        public static Material GetKenneyMaterial()
        {
            if (kenneyCastleMaterial == null)
            {
                var tex = Resources.Load<Texture2D>("Textures/colormap") 
                       ?? Resources.Load<Texture2D>("Models/Textures/colormap");
                var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
                kenneyCastleMaterial = new Material(shader)
                {
                    name = "Mat_KenneyCastleAtlas",
                    mainTexture = tex,
                    color = Color.white
                };
                if (shader.name == "Standard")
                {
                    kenneyCastleMaterial.SetFloat("_Glossiness", 0.2f);
                }
            }
            return kenneyCastleMaterial;
        }

        public static Material GetBuildingMaterial(UnitTeam team)
        {
            if (team == UnitTeam.Player)
            {
                if (playerBuildingMaterial == null)
                {
                    // Mavi Takım: Kenney 'colormap' dokusunda tüm çatı ve kubbeler Mavi renktedir
                    var tex = Resources.Load<Texture2D>("Textures/colormap") 
                           ?? Resources.Load<Texture2D>("Models/Textures/colormap");
                    var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
                    playerBuildingMaterial = new Material(shader)
                    {
                        name = "Mat_KenneyCastle_PlayerBlue"
                    };
                    if (tex != null)
                    {
                        playerBuildingMaterial.mainTexture = tex;
                        playerBuildingMaterial.color = Color.white;
                    }
                    else
                    {
                        playerBuildingMaterial.color = new Color(0.35f, 0.5f, 0.85f);
                    }
                    if (shader != null && shader.name == "Standard")
                    {
                        playerBuildingMaterial.SetFloat("_Glossiness", 0.2f);
                    }
                }
                return playerBuildingMaterial;
            }
            else
            {
                if (enemyBuildingMaterial == null)
                {
                    // Kırmızı Takım: Kenney 'variation-a' dokusunda tüm çatı ve kubbeler Kırmızı renktedir
                    var tex = Resources.Load<Texture2D>("Textures/variation-a") 
                           ?? Resources.Load<Texture2D>("Textures/variation-e") 
                           ?? Resources.Load<Texture2D>("Textures/colormap");
                    var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
                    enemyBuildingMaterial = new Material(shader)
                    {
                        name = "Mat_KenneyCastle_EnemyRed"
                    };
                    if (tex != null)
                    {
                        enemyBuildingMaterial.mainTexture = tex;
                        enemyBuildingMaterial.color = Color.white;
                    }
                    else
                    {
                        enemyBuildingMaterial.color = new Color(0.85f, 0.25f, 0.25f);
                    }
                    if (shader != null && shader.name == "Standard")
                    {
                        enemyBuildingMaterial.SetFloat("_Glossiness", 0.2f);
                    }
                }
                return enemyBuildingMaterial;
            }
        }

        private static GameObject InstantiatePart(string modelName, Transform parent, Vector3 localPos, Quaternion localRot, Vector3 localScale)
        {
            GameObject prefab = Resources.Load<GameObject>($"Models/{modelName}");
            if (prefab == null)
            {
                string altName = modelName.Replace("-", "");
                prefab = Resources.Load<GameObject>($"Models/{altName}");
            }

            if (prefab == null)
            {
                return null;
            }

            GameObject go = Instantiate(prefab, parent);
            go.name = $"KenneyModel_{modelName}";
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;

            // Alt parçalardaki tüm iç collider'ları sil - Kök nesnede tek temiz BoxCollider kullanılır
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
            {
                DestroyImmediate(col);
            }

            return go;
        }

        private static void TintFlagOrBanner(GameObject go, UnitTeam team)
        {
            if (go == null) return;
            Color accentColor = (team == UnitTeam.Player)
                ? new Color(0.12f, 0.48f, 0.98f) // Asil Kraliyet Mavisi
                : new Color(0.94f, 0.16f, 0.16f); // Savaşçı Kızıl Kırmızı

            var flagShader = Shader.Find("Custom/MedievalDoubleSidedBanner")
                          ?? Shader.Find("Unlit/Color")
                          ?? Shader.Find("Sprites/Default");

            foreach (var ren in go.GetComponentsInChildren<Renderer>(true))
            {
                if (ren != null)
                {
                    if (flagShader != null)
                    {
                        var mat = new Material(flagShader);
                        mat.color = accentColor;
                        ren.material = mat;
                    }
                    else
                    {
                        ren.material.color = accentColor;
                    }
                }
            }
        }

        private static void ApplyTeamStyling(Transform visualRoot, UnitTeam team)
        {
            Color teamColor = (team == UnitTeam.Player)
                ? new Color(0.12f, 0.48f, 0.98f) // Asil Kraliyet Mavisi
                : new Color(0.94f, 0.16f, 0.16f); // Savaşçı Kızıl Kırmızı

            // Takıma özel Kenney doku atlası (Player -> Mavi çatı/kubbe 'colormap', Enemy -> Kırmızı çatı/kubbe 'variation-a')
            var teamAtlasMat = GetBuildingMaterial(team);
            var flagShader = Shader.Find("Custom/MedievalDoubleSidedBanner")
                          ?? Shader.Find("Unlit/Color")
                          ?? Shader.Find("Sprites/Default");

            foreach (var ren in visualRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (ren == null) continue;
                string lowerName = ren.gameObject.name.ToLower();

                // Bayraklar, flamalar ve sancaklar takım rengiyle ışıldasın (Asla gölgede kararma yapmaz)
                if (lowerName.Contains("flag") || lowerName.Contains("banner") || lowerName.Contains("pennant"))
                {
                    if (flagShader != null)
                    {
                        var mat = new Material(flagShader);
                        mat.color = teamColor;
                        ren.material = mat;
                    }
                    else
                    {
                        ren.material.color = teamColor;
                    }
                }
                else
                {
                    // Kenney 3D model parçası mı kontrol et (kendisi veya üst nesnesi KenneyModel_ ise ya da FBX dokusu varsa)
                    bool isKenneyPart = lowerName.StartsWith("kenneymodel_") ||
                                        (ren.transform.parent != null && ren.transform.parent.name.StartsWith("KenneyModel_")) ||
                                        (ren.sharedMaterial != null && ren.sharedMaterial.mainTexture != null);

                    if (isKenneyPart && teamAtlasMat != null)
                    {
                        int matCount = ren.sharedMaterials != null ? Mathf.Max(1, ren.sharedMaterials.Length) : 1;
                        if (matCount <= 1)
                        {
                            ren.sharedMaterial = teamAtlasMat;
                        }
                        else
                        {
                            Material[] mats = new Material[matCount];
                            for (int i = 0; i < matCount; i++) mats[i] = teamAtlasMat;
                            ren.sharedMaterials = mats;
                        }
                    }
                }
            }
        }

        private static void ConstructTownCenter3D(Transform parent, UnitTeam team)
        {
            // --- 1. ZEMİN KAT ANA KALE BLOKU (Masif 2.5 x 2.0 x 2.5, Yükseklik: 2.02m) ---
            InstantiatePart("tower-square-base", parent, Vector3.zero, Quaternion.identity, new Vector3(2.5f, 2.0f, 2.5f));

            // --- 2. İKİNCİ KAT PENCERELİ BURÇ (Boşluksuz: Y = 2.00m, Yükseklik: 1.82m, Zirve: 3.82m) ---
            InstantiatePart("tower-square-mid-windows", parent, new Vector3(0, 2.00f, 0), Quaternion.identity, new Vector3(2.4f, 1.8f, 2.4f));

            // --- 3. YÜKSEK SİVRİ İMPARATORLUK ÇATISI (Boşluksuz: Y = 3.80m, Zirve: 6.50m) ---
            InstantiatePart("tower-square-top-roof-high", parent, new Vector3(0, 3.80f, 0), Quaternion.identity, new Vector3(2.5f, 2.0f, 2.5f));

            // --- 4. 4 KÖŞEYE BÜTÜNLEŞİK NÖBET KULELERİ (Boşluksuz Masif Burçlar) ---
            Vector3[] towerOffsets = new Vector3[]
            {
                new Vector3(-1.55f, 0, -1.55f),
                new Vector3( 1.55f, 0, -1.55f),
                new Vector3(-1.55f, 0,  1.55f),
                new Vector3( 1.55f, 0,  1.55f)
            };
            foreach (var offset in towerOffsets)
            {
                InstantiatePart("tower-square", parent, offset, Quaternion.identity, new Vector3(1.2f, 2.0f, 1.2f));
                InstantiatePart("tower-square-roof", parent, offset + new Vector3(0, 2.60f, 0), Quaternion.identity, new Vector3(1.25f, 1.2f, 1.25f));
            }

            // --- 5. GİRİŞ KEMERLİ KAPI, DEMİR PARMAKLIK & TAŞ MERDİVENLER (Ön Duvara Bitişik) ---
            InstantiatePart("wall-doorway", parent, new Vector3(0, 0, -1.35f), Quaternion.identity, new Vector3(1.4f, 1.4f, 1.4f));
            InstantiatePart("metal-gate", parent, new Vector3(0, 0.3f, -1.35f), Quaternion.identity, new Vector3(1.25f, 1.25f, 1.25f));
            InstantiatePart("stairs-stone-square", parent, new Vector3(0, 0, -1.95f), Quaternion.identity, new Vector3(1.6f, 0.9f, 1.4f));

            // --- 6. GİRİŞ KANAT FLAMALARI (Zirve bayrağı RTSBuildingBanner ile dalgalanır) ---
            var b1 = InstantiatePart("flag-banner-long", parent, new Vector3(-1.6f, 2.8f, -1.55f), Quaternion.identity, new Vector3(1.2f, 1.2f, 1.2f));
            var b2 = InstantiatePart("flag-banner-long", parent, new Vector3( 1.6f, 2.8f, -1.55f), Quaternion.identity, new Vector3(1.2f, 1.2f, 1.2f));
            TintFlagOrBanner(b1, team);
            TintFlagOrBanner(b2, team);
        }

        private static void ConstructBarracks3D(Transform parent, UnitTeam team)
        {
            // --- A. GENİŞ YATAY ASKERİ KIŞLA YATAKHANESİ ---
            // İki salon bloğunun yan yana birleşimi ile elde edilen 5.5m genişliğinde yatay garnizon binası
            // Sol Salon: Taban yüksekliği 1.41m (Y: 0.0 -> 1.41m)
            InstantiatePart("tower-square-base", parent, new Vector3(-1.6f, 0, 0), Quaternion.identity, new Vector3(2.2f, 1.4f, 2.4f));
            // Çatı: Tam tabanın üstüne oturur (Y: 1.40m, Zirve: 2.60m)
            InstantiatePart("tower-square-roof", parent, new Vector3(-1.6f, 1.40f, 0), Quaternion.identity, new Vector3(2.3f, 1.2f, 2.5f));

            // Sağ Salon: Taban yüksekliği 1.41m (Y: 0.0 -> 1.41m)
            InstantiatePart("tower-square-base", parent, new Vector3(0.5f, 0, 0), Quaternion.identity, new Vector3(2.0f, 1.4f, 2.4f));
            // Çatı: Tam tabanın üstüne oturur (Y: 1.40m, Zirve: 2.60m)
            InstantiatePart("tower-square-roof", parent, new Vector3(0.5f, 1.40f, 0), Quaternion.identity, new Vector3(2.1f, 1.2f, 2.5f));

            // Kışla Giriş Kapısı & Merdivenler
            InstantiatePart("door", parent, new Vector3(-0.6f, 0, -1.25f), Quaternion.identity, new Vector3(1.4f, 1.3f, 1.4f));
            InstantiatePart("stairs-stone", parent, new Vector3(-0.6f, 0, -1.8f), Quaternion.identity, new Vector3(1.3f, 0.9f, 1.2f));

            // --- B. ÇİTLE ÇEVRİLİ ASKERİ TALİM / EĞİTİM AVLU SU (DRILL YARD) ---
            // Sağ tarafta açık hava eğitim arenası
            InstantiatePart("wall-narrow-wood-fence", parent, new Vector3(2.6f, 0, -1.2f), Quaternion.identity, new Vector3(1.5f, 1.2f, 1.5f));
            InstantiatePart("wall-narrow-wood-fence", parent, new Vector3(3.8f, 0, 0), Quaternion.Euler(0, 90f, 0), new Vector3(1.6f, 1.2f, 1.6f));
            InstantiatePart("wall-narrow-wood-fence", parent, new Vector3(2.6f, 0, 1.2f), Quaternion.identity, new Vector3(1.5f, 1.2f, 1.5f));

            // 1. Talim Mankeni (Training Combat Dummy)
            var dummyRoot = new GameObject("Training_Dummy");
            dummyRoot.transform.SetParent(parent, false);
            dummyRoot.transform.localPosition = new Vector3(2.4f, 0, 0);

            // Ahşap direk
            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(post.GetComponent<Collider>());
            post.transform.SetParent(dummyRoot.transform, false);
            post.transform.localPosition = new Vector3(0, 0.9f, 0);
            post.transform.localScale = new Vector3(0.18f, 0.9f, 0.18f);
            post.GetComponent<Renderer>().material = RTSUnitVisuals.GetWoodMat();

            // Saman/kumaş hedef gövdesi
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(dummyRoot.transform, false);
            body.transform.localPosition = new Vector3(0, 1.0f, 0);
            body.transform.localScale = new Vector3(0.55f, 0.6f, 0.4f);
            body.GetComponent<Renderer>().material = RTSUnitVisuals.GetLinenMat();

            // Hedef mankeni zırhı & kalkanı
            var targetShield = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(targetShield.GetComponent<Collider>());
            targetShield.transform.SetParent(dummyRoot.transform, false);
            targetShield.transform.localPosition = new Vector3(0, 1.0f, -0.22f);
            targetShield.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            targetShield.transform.localScale = new Vector3(0.35f, 0.05f, 0.35f);
            targetShield.GetComponent<Renderer>().material = RTSUnitVisuals.GetTeamMat(team);

            // Çelik miğfer
            var dummyHelmet = InstantiatePart("Helmet1", dummyRoot.transform, new Vector3(0, 1.55f, 0), Quaternion.identity, new Vector3(1.2f, 1.2f, 1.2f))
                           ?? InstantiatePart("Helmet2", dummyRoot.transform, new Vector3(0, 1.55f, 0), Quaternion.identity, new Vector3(1.2f, 1.2f, 1.2f));

            // 2. Silah Standı / Mızrak Rafı
            var rack = new GameObject("Weapon_Rack");
            rack.transform.SetParent(parent, false);
            rack.transform.localPosition = new Vector3(1.6f, 0, -1.0f);

            var s1 = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(s1.GetComponent<Collider>());
            s1.transform.SetParent(rack.transform, false);
            s1.transform.localPosition = new Vector3(0, 0.8f, 0);
            s1.transform.localRotation = Quaternion.Euler(12f, 0, 10f);
            s1.transform.localScale = new Vector3(0.06f, 0.85f, 0.06f);
            s1.GetComponent<Renderer>().material = RTSUnitVisuals.GetWoodMat();

            var s2 = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(s2.GetComponent<Collider>());
            s2.transform.SetParent(rack.transform, false);
            s2.transform.localPosition = new Vector3(0.18f, 0.8f, 0.05f);
            s2.transform.localRotation = Quaternion.Euler(-8f, 0, -12f);
            s2.transform.localScale = new Vector3(0.06f, 0.85f, 0.06f);
            s2.GetComponent<Renderer>().material = RTSUnitVisuals.GetWoodMat();

            // 3. Alay Askeri Flaması
            var barracksBanner = InstantiatePart("flag-banner-long", parent, new Vector3(3.6f, 2.4f, -1.1f), Quaternion.identity, new Vector3(1.2f, 1.2f, 1.2f))
                              ?? InstantiatePart("flag", parent, new Vector3(3.6f, 2.6f, -1.1f), Quaternion.identity, new Vector3(1.1f, 1.1f, 1.1f));
            TintFlagOrBanner(barracksBanner, team);
        }

        private static void ConstructBlacksmith3D(Transform parent, UnitTeam team)
        {
            // --- A. AÇIK CEPHELİ TAŞ & AHŞAP ATÖLYE GÖVDESİ ---
            // Taş duvar yüksekliği: 1.4 * 1.31m = 1.83m
            InstantiatePart("wall-half-modular", parent, new Vector3(-0.8f, 0, 1.4f), Quaternion.identity, new Vector3(1.6f, 1.4f, 1.4f));
            InstantiatePart("wall-half-modular", parent, new Vector3(-2.2f, 0, 0), Quaternion.Euler(0, 90f, 0), new Vector3(1.6f, 1.4f, 1.4f));
            
            // Ahşap Eğimli Atölye Çatısı (Boşluksuz: Y = 1.80m)
            InstantiatePart("tower-slant-roof", parent, new Vector3(-0.8f, 1.80f, 0), Quaternion.identity, new Vector3(2.2f, 1.0f, 2.2f));

            // Ahşap Çatı Taşıyıcı Dikmeleri (Pillars)
            InstantiatePart("wall-pillar", parent, new Vector3(0.6f, 0, -1.2f), Quaternion.identity, new Vector3(1.2f, 1.4f, 1.2f));
            InstantiatePart("wall-pillar", parent, new Vector3(-2.2f, 0, -1.2f), Quaternion.identity, new Vector3(1.2f, 1.4f, 1.2f));

            // --- B. YÜKSEK TAŞ OCAK BACASI (SMOKESTACK) & KÖRLÜK OCAĞI ---
            // İnce Taş Baca Gövdesi: 2.6 * 1.31m = 3.40m
            InstantiatePart("tower-square", parent, new Vector3(-1.8f, 0, 1.2f), Quaternion.identity, new Vector3(0.85f, 2.6f, 0.85f));
            // Baca Başlığı (Boşluksuz: Y = 3.38m)
            InstantiatePart("tower-square-top", parent, new Vector3(-1.8f, 3.38f, 1.2f), Quaternion.identity, new Vector3(0.9f, 0.8f, 0.9f));

            // Taş Eritme Ocağı (Furnace Hearth)
            InstantiatePart("tower-square-base", parent, new Vector3(-1.2f, 0, 0.8f), Quaternion.identity, new Vector3(1.0f, 0.6f, 1.0f));

            // GERÇEK KOR ATEŞ VE PARILDAYAN IŞIK (Glowing Forge Fire & Light)
            var fireObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(fireObj.GetComponent<Collider>());
            fireObj.transform.SetParent(parent, false);
            fireObj.transform.localPosition = new Vector3(-1.2f, 0.65f, 0.8f);
            fireObj.transform.localScale = new Vector3(0.55f, 0.25f, 0.55f);

            var fireMat = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse"));
            fireMat.color = new Color(1.0f, 0.4f, 0.05f);
            if (fireMat.HasProperty("_EmissionColor"))
            {
                fireMat.EnableKeyword("_EMISSION");
                fireMat.SetColor("_EmissionColor", new Color(1.0f, 0.45f, 0.05f) * 2.5f);
            }
            fireObj.GetComponent<Renderer>().material = fireMat;

            // Ateşin etrafa saçtığı gerçek dinamik nokta ışık (Point Light)
            var lightObj = new GameObject("Forge_Light");
            lightObj.transform.SetParent(fireObj.transform, false);
            lightObj.transform.localPosition = Vector3.up * 0.4f;
            var pointLight = lightObj.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.color = new Color(1.0f, 0.5f, 0.12f);
            pointLight.range = 5.0f;
            pointLight.intensity = 2.2f;

            // --- C. HEYBETLİ ÇELİK ÖRS & KÜTÜK (ANVIL ON OAK STUMP) ---
            var anvilStump = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(anvilStump.GetComponent<Collider>());
            anvilStump.transform.SetParent(parent, false);
            anvilStump.transform.localPosition = new Vector3(0.2f, 0.25f, -0.3f);
            anvilStump.transform.localScale = new Vector3(0.65f, 0.25f, 0.65f);
            anvilStump.GetComponent<Renderer>().material = RTSUnitVisuals.GetWoodMat();

            // Çelik Örs Tabanı & Çalışma Yüzeyi
            var anvilBase = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(anvilBase.GetComponent<Collider>());
            anvilBase.transform.SetParent(anvilStump.transform, false);
            anvilBase.transform.localPosition = new Vector3(0, 1.2f, 0);
            anvilBase.transform.localScale = new Vector3(0.6f, 0.4f, 0.35f);
            anvilBase.GetComponent<Renderer>().material = RTSUnitVisuals.GetSteelMat();

            var anvilHorn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(anvilHorn.GetComponent<Collider>());
            anvilHorn.transform.SetParent(anvilStump.transform, false);
            anvilHorn.transform.localPosition = new Vector3(0.35f, 1.35f, 0);
            anvilHorn.transform.localScale = new Vector3(0.35f, 0.2f, 0.22f);
            anvilHorn.GetComponent<Renderer>().material = RTSUnitVisuals.GetSteelMat();

            // --- D. SU SOĞUTMA YALAĞI (WATER QUENCHING TROUGH) ---
            var trough = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(trough.GetComponent<Collider>());
            trough.transform.SetParent(parent, false);
            trough.transform.localPosition = new Vector3(-0.6f, 0.3f, -1.0f);
            trough.transform.localScale = new Vector3(0.6f, 0.45f, 1.0f);
            trough.GetComponent<Renderer>().material = RTSUnitVisuals.GetWoodMat();

            var water = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            water.transform.SetParent(trough.transform, false);
            water.transform.localPosition = new Vector3(0, 0.45f, 0);
            water.transform.localScale = new Vector3(0.85f, 0.2f, 0.85f);
            var waterMat = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse"));
            waterMat.color = new Color(0.15f, 0.45f, 0.75f, 0.8f);
            water.GetComponent<Renderer>().material = waterMat;

            // --- E. DEMİR CEVHERİ & KÖMÜR YIĞINLARI ---
            InstantiatePart("rocks-small", parent, new Vector3(1.6f, 0, 1.0f), Quaternion.identity, new Vector3(1.3f, 1.0f, 1.3f));
            InstantiatePart("rocks-large", parent, new Vector3(1.8f, 0, -0.4f), Quaternion.identity, new Vector3(1.1f, 0.9f, 1.1f));

            // Demirci Lonca Flaması
            var banner = InstantiatePart("flag-pennant", parent, new Vector3(0.6f, 2.2f, -1.2f), Quaternion.identity, new Vector3(1.1f, 1.1f, 1.1f));
            TintFlagOrBanner(banner, team);
        }

        private static void ConstructHouse3D(Transform parent, UnitTeam team)
        {
            // 1. Taş/Ahşap Ev Gövdesi (Y: 0.0 -> 1.21m)
            InstantiatePart("tower-square-base", parent, Vector3.zero, Quaternion.identity, new Vector3(1.4f, 1.2f, 1.4f));

            // 2. Kiremitli Dik Çatı (Boşluksuz: Y = 1.20m, Zirve: 2.82m)
            InstantiatePart("tower-square-top-roof-high", parent, new Vector3(0, 1.20f, 0), Quaternion.identity, new Vector3(1.45f, 1.2f, 1.45f));

            // 3. Kemerli Ahşap Giriş Kapısı
            InstantiatePart("door", parent, new Vector3(0, 0, -1.42f), Quaternion.identity, new Vector3(1.2f, 1.2f, 1.2f));

            // 4. Giriş Taş Basamağı
            InstantiatePart("stairs-stone", parent, new Vector3(0, 0, -1.9f), Quaternion.identity, new Vector3(1.0f, 0.8f, 1.0f));

            // 5. Küçük Bahçe Ahşap Çiti
            InstantiatePart("wall-narrow-wood-fence", parent, new Vector3(1.6f, 0, 0), Quaternion.Euler(0, 90f, 0), new Vector3(1.2f, 1.2f, 1.2f));
        }

        private static void ConstructDefenseTower3D(Transform parent, UnitTeam team)
        {
            // 1. Masif Taş Taban (Y: 0.0 -> 1.50m, Genişlik: 2.0m x 2.0m — Duvar ve Şehir Kapısı ile aynı taban yüksekliği ve sıfır boşluk)
            InstantiatePart("tower-square-base", parent, Vector3.zero, Quaternion.identity, new Vector3(2.0f, 1.5f, 2.0f));

            // 2. Yüksek Siperlikli Gözetleme Kulesi Gövdesi (Boşluksuz: Y = 1.50m, Zirve: 4.10m)
            InstantiatePart("tower-square", parent, new Vector3(0, 1.50f, 0), Quaternion.identity, new Vector3(1.8f, 2.0f, 1.8f));

            // 3. Zirve Mazgallı Siperlik & Nöbetçi Korkulukları (Boşluksuz: Y = 4.10m, Zirve: 4.46m)
            InstantiatePart("tower-square-top", parent, new Vector3(0, 4.10f, 0), Quaternion.identity, new Vector3(1.85f, 1.2f, 1.85f));
        }

        private static void ConstructFarm3D(Transform parent, UnitTeam team)
        {
            // 1. Ekili Toprak Parseli (Koyu Kahverengi Verimli Toprak)
            var soil = GameObject.CreatePrimitive(PrimitiveType.Cube);
            soil.name = "Farm_Soil";
            soil.transform.SetParent(parent, false);
            soil.transform.localPosition = new Vector3(0, 0.08f, 0);
            soil.transform.localScale = new Vector3(5.2f, 0.16f, 5.2f);
            DestroyImmediate(soil.GetComponent<Collider>());
            var soilRen = soil.GetComponent<Renderer>();
            soilRen.material.color = new Color(0.32f, 0.20f, 0.10f); // Nemli tarla toprağı

            // 2. Altın Sarısı Buğday Sıraları (4 Sıra Başak)
            for (int row = -1; row <= 2; row++)
            {
                float z = (row - 0.5f) * 1.15f;
                var wheatRow = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wheatRow.name = $"Farm_WheatRow_{row}";
                wheatRow.transform.SetParent(parent, false);
                wheatRow.transform.localPosition = new Vector3(0, 0.45f, z);
                wheatRow.transform.localScale = new Vector3(4.4f, 0.65f, 0.55f);
                DestroyImmediate(wheatRow.GetComponent<Collider>());
                var wRen = wheatRow.GetComponent<Renderer>();
                wRen.material.color = new Color(0.92f, 0.78f, 0.22f); // Hasada hazır altın sarısı buğday
            }

            // 3. Kenar Ahşap Çitler
            InstantiatePart("wall-narrow-wood-fence", parent, new Vector3(0, 0, 2.5f), Quaternion.identity, new Vector3(1.4f, 1.2f, 1.2f));
            InstantiatePart("wall-narrow-wood-fence", parent, new Vector3(0, 0, -2.5f), Quaternion.identity, new Vector3(1.4f, 1.2f, 1.2f));

            // 4. Saman Balyası
            var hay = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hay.name = "Farm_HayBale";
            hay.transform.SetParent(parent, false);
            hay.transform.localPosition = new Vector3(1.9f, 0.4f, 1.9f);
            hay.transform.localScale = new Vector3(0.9f, 0.5f, 0.9f);
            DestroyImmediate(hay.GetComponent<Collider>());
            hay.GetComponent<Renderer>().material.color = new Color(0.88f, 0.75f, 0.25f);
        }

        private static void ConstructStoneWall3D(Transform parent, UnitTeam team)
        {
            // 1. Ana Masif Taş Duvar Kanatları (Sol ve Sağ - Toplam 4.0m Kesintisiz Taş Siperlik)
            // Sol Kanat: X = -1.0m, Ölçek X = 2.0m (X: -2.0m ile 0.0m arasını tam kapatır)
            InstantiatePart("wall", parent, new Vector3(-1.0f, 0, 0), Quaternion.identity, new Vector3(2.0f, 1.5f, 1.2f));
            // Sağ Kanat: X = +1.0m, Ölçek X = 2.0m (X: 0.0m ile +2.0m arasını tam kapatır)
            InstantiatePart("wall", parent, new Vector3( 1.0f, 0, 0), Quaternion.identity, new Vector3(2.0f, 1.5f, 1.2f));

            // 2. İki Uçta ve Ortada Bütünleştirici Masif Taş Destek Sütunları (Duvarların birbirine tam kenetlenmesini sağlar)
            InstantiatePart("wall-pillar", parent, new Vector3(-2.0f, 0, 0), Quaternion.identity, new Vector3(1.2f, 1.55f, 1.3f));
            InstantiatePart("wall-pillar", parent, new Vector3( 2.0f, 0, 0), Quaternion.identity, new Vector3(1.2f, 1.55f, 1.3f));
            InstantiatePart("wall-half", parent, Vector3.zero, Quaternion.identity, new Vector3(1.2f, 1.52f, 1.25f));
        }

        private static void ConstructCityGate3D(Transform parent, UnitTeam team)
        {
            // 1. Kemerli Masif Geçit Girişi (Sol ve Sağ Kemer Parçaları Karşılıklı Birleşerek 3.0m Taş Kemer Oluşturur: X: -1.5m ile +1.5m)
            InstantiatePart("wall-doorway", parent, new Vector3(-0.75f, 0, 0), Quaternion.identity, new Vector3(1.5f, 1.7f, 1.4f));
            InstantiatePart("wall-doorway", parent, new Vector3( 0.75f, 0, 0), Quaternion.Euler(0, 180f, 0), new Vector3(1.5f, 1.7f, 1.4f));

            // 2. Ağır Demir Parmaklıklı Kapı / Portcullis (Dost geçişi için kemer altında açık/yukarı konumda)
            InstantiatePart("metal-gate", parent, new Vector3(0, 1.45f, 0), Quaternion.identity, new Vector3(2.1f, 1.3f, 1.5f));

            // 3. Kapı Üstü Muhafız Yolu & Taş Siperlik
            InstantiatePart("wall-half", parent, new Vector3(0, 2.22f, 0), Quaternion.identity, new Vector3(1.6f, 0.8f, 1.25f));

            // 4. İki Yan Masif Muhafız Burçları (X = -2.25 ve +2.25 -> Kapıyı -3.0 ile +3.0 Arasına Tam Kilitler)
            InstantiatePart("wall-corner-half-tower", parent, new Vector3(-2.25f, 0, 0), Quaternion.identity, new Vector3(1.5f, 1.65f, 1.4f));
            InstantiatePart("wall-corner-half-tower", parent, new Vector3( 2.25f, 0, 0), Quaternion.Euler(0, 180f, 0), new Vector3(1.5f, 1.65f, 1.4f));

            // Kapı uçlarında Taş Duvar ile sıfır boşluklu birleşme sağlayan kenetleme sütunları (X = ±3.0m)
            InstantiatePart("wall-pillar", parent, new Vector3(-3.0f, 0, 0), Quaternion.identity, new Vector3(1.2f, 1.55f, 1.3f));
            InstantiatePart("wall-pillar", parent, new Vector3( 3.0f, 0, 0), Quaternion.identity, new Vector3(1.2f, 1.55f, 1.3f));

            // 5. Muhafız Burçları Flamaları
            var fLeft = InstantiatePart("flag-pennant", parent, new Vector3(-2.25f, 2.6f, 0), Quaternion.identity, new Vector3(1.2f, 1.2f, 1.2f));
            var fRight = InstantiatePart("flag-pennant", parent, new Vector3( 2.25f, 2.6f, 0), Quaternion.identity, new Vector3(1.2f, 1.2f, 1.2f));
            TintFlagOrBanner(fLeft, team);
            TintFlagOrBanner(fRight, team);
        }

        private static void ConstructCastle3D(Transform parent, UnitTeam team)
        {
            // --- 1. MERKEZİ BÜYÜK KALE KULESİ (GREAT KEEP) ---
            // Zemin Kat Masif Blok (Yükseklik: 2.02m)
            InstantiatePart("CastleBase", parent, Vector3.zero, Quaternion.identity, new Vector3(2.8f, 2.0f, 2.8f));
            // İkinci Kat Pencereli Burç (Boşluksuz: Y = 2.00m, Yükseklik: 1.82m, Zirve: 3.82m)
            InstantiatePart("CastleMidWindows", parent, new Vector3(0, 2.00f, 0), Quaternion.identity, new Vector3(2.6f, 1.8f, 2.6f));
            // Sivri Kale Çatısı (Boşluksuz: Y = 3.80m, Zirve: 6.50m)
            InstantiatePart("CastleRoofHigh", parent, new Vector3(0, 3.80f, 0), Quaternion.identity, new Vector3(2.6f, 2.0f, 2.6f));

            // --- 2. 4 KÖŞEDE BÜTÜNLEŞİK SAVUNMA BURÇLARI (HEXAGON BASTION TOWERS) ---
            Vector3[] cornerOffsets = new Vector3[]
            {
                new Vector3(-1.9f, 0, -1.9f),
                new Vector3( 1.9f, 0, -1.9f),
                new Vector3(-1.9f, 0,  1.9f),
                new Vector3( 1.9f, 0,  1.9f)
            };
            foreach (var offset in cornerOffsets)
            {
                InstantiatePart("tower-hexagon-base", parent, offset, Quaternion.identity, new Vector3(1.4f, 2.0f, 1.4f));
                InstantiatePart("tower-hexagon-roof", parent, offset + new Vector3(0, 2.60f, 0), Quaternion.identity, new Vector3(1.4f, 1.4f, 1.4f));
            }

            // --- 3. ÖN TAHKİMATLI GİRİŞ VE MERDİVENLER ---
            InstantiatePart("wall-doorway", parent, new Vector3(0, 0, -1.5f), Quaternion.identity, new Vector3(1.6f, 1.6f, 1.6f));
            InstantiatePart("metal-gate", parent, new Vector3(0, 0.4f, -1.5f), Quaternion.identity, new Vector3(1.4f, 1.4f, 1.4f));
            InstantiatePart("stairs-stone-square", parent, new Vector3(0, 0, -2.4f), Quaternion.identity, new Vector3(2.0f, 1.2f, 1.8f));

            // --- 4. GİRİŞ SANCAKLARI VE FLAMALARI (Zirve bayrağı RTSBuildingBanner ile dalgalanır) ---
            var bLeft = InstantiatePart("flag-banner-long", parent, new Vector3(-1.8f, 2.6f, -1.6f), Quaternion.identity, new Vector3(1.4f, 1.4f, 1.4f));
            var bRight = InstantiatePart("flag-banner-long", parent, new Vector3( 1.8f, 2.6f, -1.6f), Quaternion.identity, new Vector3(1.4f, 1.4f, 1.4f));
            TintFlagOrBanner(bLeft, team);
            TintFlagOrBanner(bRight, team);
        }

        private static void ConstructArcheryRange3D(Transform parent, UnitTeam team)
        {
            // --- A. OKÇU BİNASI (ARCHERY LODGE) ---
            // Zemin Kat (Yükseklik: 1.41m)
            InstantiatePart("tower-square-base", parent, new Vector3(-1.6f, 0, 0), Quaternion.identity, new Vector3(2.2f, 1.4f, 2.4f));
            // Çatı (Boşluksuz: Y = 1.40m, Zirve: 2.60m)
            InstantiatePart("tower-square-roof", parent, new Vector3(-1.6f, 1.40f, 0), Quaternion.identity, new Vector3(2.3f, 1.2f, 2.5f));
            InstantiatePart("door", parent, new Vector3(-0.6f, 0, -1.25f), Quaternion.identity, new Vector3(1.4f, 1.3f, 1.4f));
            InstantiatePart("stairs-stone", parent, new Vector3(-0.6f, 0, -1.8f), Quaternion.identity, new Vector3(1.3f, 0.9f, 1.2f));

            // --- B. OKÇULUK TALİM ALANI VE ÇİTLER (ARCHERY SHOOTING RANGE) ---
            InstantiatePart("wall-narrow-wood-fence", parent, new Vector3(2.6f, 0, -1.2f), Quaternion.identity, new Vector3(1.5f, 1.2f, 1.5f));
            InstantiatePart("wall-narrow-wood-fence", parent, new Vector3(3.8f, 0, 0), Quaternion.Euler(0, 90f, 0), new Vector3(1.6f, 1.2f, 1.6f));
            InstantiatePart("wall-narrow-wood-fence", parent, new Vector3(2.6f, 0, 1.2f), Quaternion.identity, new Vector3(1.5f, 1.2f, 1.5f));

            // 1. & 2. Klasik Dairesel Hedef Tahtaları (Archery Targets)
            Vector3[] targetPositions = new Vector3[] { new Vector3(3.4f, 0, -0.6f), new Vector3(3.4f, 0, 0.6f) };
            foreach (var tPos in targetPositions)
            {
                var targetRoot = new GameObject("Archery_Target_Stand");
                targetRoot.transform.SetParent(parent, false);
                targetRoot.transform.localPosition = tPos;

                var leg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(leg.GetComponent<Collider>());
                leg.transform.SetParent(targetRoot.transform, false);
                leg.transform.localPosition = new Vector3(0, 0.75f, 0);
                leg.transform.localScale = new Vector3(0.12f, 0.75f, 0.12f);
                leg.GetComponent<Renderer>().material = RTSUnitVisuals.GetWoodMat();

                var disk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(disk.GetComponent<Collider>());
                disk.transform.SetParent(targetRoot.transform, false);
                disk.transform.localPosition = new Vector3(-0.1f, 1.15f, 0);
                disk.transform.localRotation = Quaternion.Euler(0, 0, 90f);
                disk.transform.localScale = new Vector3(0.65f, 0.06f, 0.65f);
                var diskRen = disk.GetComponent<Renderer>();
                diskRen.material.color = new Color(0.95f, 0.92f, 0.85f);

                var bullseye = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(bullseye.GetComponent<Collider>());
                bullseye.transform.SetParent(targetRoot.transform, false);
                bullseye.transform.localPosition = new Vector3(-0.14f, 1.15f, 0);
                bullseye.transform.localRotation = Quaternion.Euler(0, 0, 90f);
                bullseye.transform.localScale = new Vector3(0.32f, 0.07f, 0.32f);
                var bullRen = bullseye.GetComponent<Renderer>();
                bullRen.material.color = new Color(0.85f, 0.15f, 0.15f);

                var arrow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(arrow.GetComponent<Collider>());
                arrow.transform.SetParent(targetRoot.transform, false);
                arrow.transform.localPosition = new Vector3(-0.35f, 1.2f, 0.05f);
                arrow.transform.localRotation = Quaternion.Euler(5f, 0, 95f);
                arrow.transform.localScale = new Vector3(0.04f, 0.30f, 0.04f);
                arrow.GetComponent<Renderer>().material = RTSUnitVisuals.GetWoodMat();
            }

            var flag = InstantiatePart("flag-pennant", parent, new Vector3(-1.6f, 2.6f, 0), Quaternion.identity, new Vector3(1.2f, 1.2f, 1.2f));
            TintFlagOrBanner(flag, team);
        }

        private static void ConstructSiegeWorkshop3D(Transform parent, UnitTeam team)
        {
            // --- A. KUŞATMA İŞLİĞİ VE DÖKÜMHANESİ ---
            // Zemin Kat (Yükseklik: 1.62m)
            InstantiatePart("tower-square-base", parent, new Vector3(-1.8f, 0, 0), Quaternion.identity, new Vector3(2.4f, 1.6f, 2.8f));
            // Çatı (Boşluksuz: Y = 1.60m, Zirve: 2.90m)
            InstantiatePart("tower-square-roof", parent, new Vector3(-1.8f, 1.60f, 0), Quaternion.identity, new Vector3(2.5f, 1.3f, 2.9f));
            InstantiatePart("stairs-stone-square", parent, new Vector3(-1.8f, 0, -1.8f), Quaternion.identity, new Vector3(1.8f, 1.0f, 1.4f));

            // --- B. MONTAJ AVLUSU VE 3D SERGİ MANCINIĞI ---
            var catapultModel = InstantiatePart("siege-catapult", parent, new Vector3(1.8f, 0, -0.2f), Quaternion.Euler(0, -30f, 0), new Vector3(1.3f, 1.3f, 1.3f))
                             ?? InstantiatePart("Catapult", parent, new Vector3(1.8f, 0, -0.2f), Quaternion.Euler(0, -30f, 0), new Vector3(1.3f, 1.3f, 1.3f))
                             ?? InstantiatePart("siege-ballista", parent, new Vector3(1.8f, 0, -0.2f), Quaternion.Euler(0, -30f, 0), new Vector3(1.3f, 1.3f, 1.3f));

            Vector3[] stoneOffsets = new Vector3[]
            {
                new Vector3(0.5f, 0.25f, 1.4f),
                new Vector3(0.9f, 0.25f, 1.5f),
                new Vector3(0.7f, 0.65f, 1.45f)
            };
            foreach (var sOff in stoneOffsets)
            {
                var stone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(stone.GetComponent<Collider>());
                stone.name = "Siege_Ammo_Stone";
                stone.transform.SetParent(parent, false);
                stone.transform.localPosition = sOff;
                stone.transform.localScale = Vector3.one * 0.55f;
                stone.GetComponent<Renderer>().material.color = new Color(0.40f, 0.40f, 0.42f);
            }

            InstantiatePart("tree-log", parent, new Vector3(2.8f, 0, 1.5f), Quaternion.Euler(0, 45f, 0), new Vector3(1.4f, 1.4f, 1.4f));

            var banner = InstantiatePart("flag-banner-long", parent, new Vector3(-0.6f, 2.2f, -1.5f), Quaternion.identity, new Vector3(1.2f, 1.2f, 1.2f));
            TintFlagOrBanner(banner, team);
        }

        private static void ConstructMarket3D(Transform parent, UnitTeam team)
        {
            // --- A. PAZAR MEYDANI TAŞ PLATFORMU ---
            InstantiatePart("stairs-stone-square", parent, new Vector3(0, 0, 0), Quaternion.identity, new Vector3(3.2f, 0.5f, 3.2f));

            // --- B. TİCARET ÇADIRLARI & RENKLİ TENTELER (MARKET STALLS) ---
            var tent1Root = new GameObject("Tent_Food");
            tent1Root.transform.SetParent(parent, false);
            tent1Root.transform.localPosition = new Vector3(-1.4f, 0.5f, 0);

            for (int i = -1; i <= 1; i += 2)
            {
                for (int j = -1; j <= 1; j += 2)
                {
                    var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Object.DestroyImmediate(post.GetComponent<Collider>());
                    post.transform.SetParent(tent1Root.transform, false);
                    post.transform.localPosition = new Vector3(i * 0.8f, 0.9f, j * 0.8f);
                    post.transform.localScale = new Vector3(0.08f, 0.9f, 0.08f);
                    post.GetComponent<Renderer>().material = RTSUnitVisuals.GetWoodMat();
                }
            }

            var roof1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(roof1.GetComponent<Collider>());
            roof1.transform.SetParent(tent1Root.transform, false);
            roof1.transform.localPosition = new Vector3(0, 1.85f, 0);
            roof1.transform.localRotation = Quaternion.Euler(15f, 0, 0);
            roof1.transform.localScale = new Vector3(1.9f, 0.12f, 1.9f);
            roof1.GetComponent<Renderer>().material.color = (team == UnitTeam.Player)
                ? new Color(0.15f, 0.45f, 0.88f)
                : new Color(0.85f, 0.18f, 0.18f);

            var tent2Root = new GameObject("Tent_Goods");
            tent2Root.transform.SetParent(parent, false);
            tent2Root.transform.localPosition = new Vector3(1.4f, 0.5f, 0);

            for (int i = -1; i <= 1; i += 2)
            {
                for (int j = -1; j <= 1; j += 2)
                {
                    var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Object.DestroyImmediate(post.GetComponent<Collider>());
                    post.transform.SetParent(tent2Root.transform, false);
                    post.transform.localPosition = new Vector3(i * 0.8f, 0.9f, j * 0.8f);
                    post.transform.localScale = new Vector3(0.08f, 0.9f, 0.08f);
                    post.GetComponent<Renderer>().material = RTSUnitVisuals.GetWoodMat();
                }
            }

            var roof2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(roof2.GetComponent<Collider>());
            roof2.transform.SetParent(tent2Root.transform, false);
            roof2.transform.localPosition = new Vector3(0, 1.85f, 0);
            roof2.transform.localRotation = Quaternion.Euler(-15f, 0, 0);
            roof2.transform.localScale = new Vector3(1.9f, 0.12f, 1.9f);
            roof2.GetComponent<Renderer>().material.color = (team == UnitTeam.Player)
                ? new Color(0.24f, 0.60f, 0.95f)
                : new Color(0.72f, 0.14f, 0.14f);

            // --- C. TİCARİ SANDIKLAR & ÇUVALLAR ---
            Vector3[] crateOffsets = new Vector3[]
            {
                new Vector3(-1.3f, 0.8f,  0.3f),
                new Vector3(-1.5f, 0.8f, -0.3f),
                new Vector3( 1.3f, 0.8f,  0.2f),
                new Vector3( 1.5f, 0.8f, -0.3f),
                new Vector3( 0.0f, 0.7f, -1.2f)
            };
            foreach (var cOff in crateOffsets)
            {
                var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(crate.GetComponent<Collider>());
                crate.transform.SetParent(parent, false);
                crate.transform.localPosition = cOff;
                crate.transform.localScale = new Vector3(0.55f, 0.55f, 0.55f);
                crate.GetComponent<Renderer>().material.color = new Color(0.55f, 0.38f, 0.22f);
            }

            var marketFlag = InstantiatePart("flag-pennant", parent, new Vector3(0, 2.5f, 0), Quaternion.identity, new Vector3(1.8f, 1.8f, 1.8f));
            TintFlagOrBanner(marketFlag, team);
        }

        /// <summary>
        /// Haritada yeni bir 3D Kenney CastleKit modüler binası inşa eder.
        /// </summary>
        public static RTSBuilding SpawnBuilding(RTSBuildingType type, Vector3 position, UnitTeam team, bool startBuilt = false, float rotationY = 0f)
        {
            string bName = type.ToString();
            GameObject bGo = new GameObject($"Building_{bName}");
            bGo.transform.position = position;
            bGo.transform.rotation = Quaternion.Euler(0f, rotationY, 0f);

            // 3D Modüler görsel kök nesnesi
            GameObject vGo = new GameObject("VisualRoot");
            vGo.transform.SetParent(bGo.transform, false);

            BoxCollider rootBox = bGo.AddComponent<BoxCollider>();

            switch (type)
            {
                case RTSBuildingType.TownCenter:
                    ConstructTownCenter3D(vGo.transform, team);
                    rootBox.size = new Vector3(6.8f, 7.0f, 6.8f);
                    rootBox.center = new Vector3(0, 3.5f, 0);
                    break;

                case RTSBuildingType.Barracks:
                    ConstructBarracks3D(vGo.transform, team);
                    rootBox.size = new Vector3(7.5f, 3.6f, 4.5f);
                    rootBox.center = new Vector3(0.5f, 1.8f, 0);
                    break;

                case RTSBuildingType.Blacksmith:
                    ConstructBlacksmith3D(vGo.transform, team);
                    rootBox.size = new Vector3(5.5f, 4.5f, 5.5f);
                    rootBox.center = new Vector3(0, 2.25f, 0);
                    break;

                case RTSBuildingType.House:
                    ConstructHouse3D(vGo.transform, team);
                    rootBox.size = new Vector3(4.2f, 3.2f, 4.2f);
                    rootBox.center = new Vector3(0.4f, 1.6f, 0);
                    break;

                case RTSBuildingType.Farm:
                    ConstructFarm3D(vGo.transform, team);
                    rootBox.size = new Vector3(5.4f, 2.0f, 5.4f);
                    rootBox.center = new Vector3(0, 1.0f, 0);
                    break;

                case RTSBuildingType.DefenseTower:
                    ConstructDefenseTower3D(vGo.transform, team);
                    rootBox.size = new Vector3(2.4f, 4.5f, 2.4f);
                    rootBox.center = new Vector3(0, 2.25f, 0);
                    break;

                case RTSBuildingType.StoneWall:
                    ConstructStoneWall3D(vGo.transform, team);
                    rootBox.size = new Vector3(4.0f, 2.4f, 1.4f);
                    rootBox.center = new Vector3(0, 1.2f, 0);
                    break;

                case RTSBuildingType.CityGate:
                    ConstructCityGate3D(vGo.transform, team);
                    rootBox.size = new Vector3(6.0f, 3.2f, 1.8f);
                    rootBox.center = new Vector3(0, 1.6f, 0);
                    break;

                case RTSBuildingType.Castle:
                    ConstructCastle3D(vGo.transform, team);
                    rootBox.size = new Vector3(8.5f, 7.5f, 8.5f);
                    rootBox.center = new Vector3(0, 3.75f, 0);
                    break;

                case RTSBuildingType.ArcheryRange:
                    ConstructArcheryRange3D(vGo.transform, team);
                    rootBox.size = new Vector3(7.5f, 3.6f, 4.5f);
                    rootBox.center = new Vector3(0.5f, 1.8f, 0);
                    break;

                case RTSBuildingType.SiegeWorkshop:
                    ConstructSiegeWorkshop3D(vGo.transform, team);
                    rootBox.size = new Vector3(7.5f, 3.8f, 5.0f);
                    rootBox.center = new Vector3(0, 1.9f, 0);
                    break;

                case RTSBuildingType.Market:
                    ConstructMarket3D(vGo.transform, team);
                    rootBox.size = new Vector3(6.5f, 4.2f, 6.0f);
                    rootBox.center = new Vector3(0, 2.1f, 0);
                    break;
            }

            // Güvenlik yedek kontrolü: Eğer hiçbir 3D model yüklenemediyse prosedürel fallback
            if (vGo.transform.childCount == 0)
            {
                var fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fallback.transform.SetParent(vGo.transform, false);
                fallback.transform.localPosition = new Vector3(0, 2f, 0);
                fallback.transform.localScale = (type == RTSBuildingType.DefenseTower) ? new Vector3(3f, 6f, 3f) : new Vector3(5f, 4f, 5f);
                var fbCol = fallback.GetComponent<Collider>();
                if (fbCol != null) DestroyImmediate(fbCol);
            }

            // Takım dokusunu ve renklerini tüm görsel parçalara uygula
            ApplyTeamStyling(vGo.transform, team);

            var building = bGo.AddComponent<RTSBuilding>();
            building.buildingType = type;
            building.team = team;
            building.visualRoot = vGo.transform;

            building.displayName = (type == RTSBuildingType.DefenseTower) ? "Savunma Kulesi" :
                                   (type == RTSBuildingType.Barracks) ? "Kışla" :
                                   (type == RTSBuildingType.Blacksmith) ? "Demirci" :
                                   (type == RTSBuildingType.House) ? "Ev (House)" :
                                   (type == RTSBuildingType.Farm) ? "Buğday Tarlası" :
                                   (type == RTSBuildingType.StoneWall) ? "Taş Duvar" :
                                   (type == RTSBuildingType.CityGate) ? "Şehir Kapısı" :
                                   (type == RTSBuildingType.Castle) ? "Şanlı Kale" :
                                   (type == RTSBuildingType.ArcheryRange) ? "Okçu Talimgahı" :
                                   (type == RTSBuildingType.SiegeWorkshop) ? "Kuşatma Atölyesi" :
                                   (type == RTSBuildingType.Market) ? "Pazar Yeri" : "Şehir Merkezi";

            switch (type)
            {
                case RTSBuildingType.DefenseTower:
                    building.maxHp = 450;
                    building.sightRadius = 16f;
                    building.attackRange = 15f;
                    building.towerDamage = 14;
                    break;
                case RTSBuildingType.Barracks:
                    building.maxHp = 600;
                    building.sightRadius = 12f;
                    break;
                case RTSBuildingType.Blacksmith:
                    building.maxHp = 500;
                    building.sightRadius = 11f;
                    break;
                case RTSBuildingType.House:
                    building.maxHp = 350;
                    building.sightRadius = 10f;
                    break;
                case RTSBuildingType.Farm:
                    building.maxHp = 300;
                    building.sightRadius = 9f;
                    break;
                case RTSBuildingType.StoneWall:
                    building.maxHp = 1200;
                    building.sightRadius = 8f;
                    break;
                case RTSBuildingType.CityGate:
                    building.maxHp = 1800;
                    building.sightRadius = 12f;
                    break;
                case RTSBuildingType.Castle:
                    building.maxHp = 3500;
                    building.sightRadius = 26f;
                    building.attackRange = 18f;
                    building.towerDamage = 28;
                    building.attackCooldown = 1.0f;
                    break;
                case RTSBuildingType.ArcheryRange:
                    building.maxHp = 650;
                    building.sightRadius = 14f;
                    break;
                case RTSBuildingType.SiegeWorkshop:
                    building.maxHp = 850;
                    building.sightRadius = 13f;
                    break;
                case RTSBuildingType.Market:
                    building.maxHp = 600;
                    building.sightRadius = 12f;
                    break;
                case RTSBuildingType.TownCenter:
                    building.maxHp = 1200;
                    building.sightRadius = 22f; // Dengeli dairesel görüş alanı: üs çevresini açar, harita sonuna taşmaz
                    break;
            }

            if (startBuilt)
            {
                building.isConstructed = true;
                building.constructionProgress = 100f;
                building.currentHp = building.maxHp;
                vGo.transform.localScale = Vector3.one;

                if (type == RTSBuildingType.Farm)
                {
                    var res = bGo.GetComponent<RTSResourceSource>() ?? bGo.AddComponent<RTSResourceSource>();
                    res.resourceType = RTSResourceType.Food;
                    res.remainingAmount = 1500;
                    res.maxAmount = 1500;
                    res.harvestInterval = 0.9f;
                    res.gatherPerTick = 2;
                }
            }
            else
            {
                building.isConstructed = false;
                building.constructionProgress = 0f;
                building.currentHp = 1;
                vGo.transform.localScale = new Vector3(1f, 0.35f, 1f);
                if (bGo.GetComponent<RTSConstructionScaffold>() == null)
                {
                    bGo.AddComponent<RTSConstructionScaffold>();
                }
            }

            building.UpdateVisualProgress();

            // Dalgalanan Hanedanlık Sancakları ve Atmosferik Meşaleler (Medieval Atmosphere)
            if (type == RTSBuildingType.TownCenter)
            {
                RTSBuildingBanner.AttachBanner(building, new Vector3(0, 6.45f, 0), height: 2.8f, width: 2.0f);
                RTSAtmosphericLight.CreateTorch(building.transform, new Vector3(-1.2f, 1.6f, -1.6f), new Color(1.0f, 0.75f, 0.35f), range: 7.5f, intensity: 1.25f);
                RTSAtmosphericLight.CreateTorch(building.transform, new Vector3( 1.2f, 1.6f, -1.6f), new Color(1.0f, 0.75f, 0.35f), range: 7.5f, intensity: 1.25f);
            }
            else if (type == RTSBuildingType.Castle)
            {
                RTSBuildingBanner.AttachBanner(building, new Vector3(0, 6.45f, 0), height: 3.0f, width: 2.2f);
                RTSAtmosphericLight.CreateTorch(building.transform, new Vector3(-1.8f, 2.0f, -2.0f), new Color(1.0f, 0.72f, 0.30f), range: 8f, intensity: 1.4f);
                RTSAtmosphericLight.CreateTorch(building.transform, new Vector3( 1.8f, 2.0f, -2.0f), new Color(1.0f, 0.72f, 0.30f), range: 8f, intensity: 1.4f);
            }
            else if (type == RTSBuildingType.Barracks)
            {
                RTSBuildingBanner.AttachBanner(building, new Vector3(-0.6f, 2.62f, 0), height: 2.2f, width: 1.5f);
                RTSAtmosphericLight.CreateTorch(building.transform, new Vector3(1.6f, 1.8f, -1.8f), new Color(1.0f, 0.70f, 0.30f), range: 6.5f, intensity: 1.15f);
            }
            else if (type == RTSBuildingType.DefenseTower)
            {
                RTSBuildingBanner.AttachBanner(building, new Vector3(0, 4.15f, 0), height: 2.4f, width: 1.5f);
                RTSAtmosphericLight.CreateTorch(building.transform, new Vector3(0, 3.8f, -0.9f), new Color(1.0f, 0.80f, 0.35f), range: 9f, intensity: 1.5f);
            }
            else if (type == RTSBuildingType.ArcheryRange)
            {
                RTSBuildingBanner.AttachBanner(building, new Vector3(-1.6f, 2.62f, 0), height: 2.2f, width: 1.5f);
                RTSAtmosphericLight.CreateTorch(building.transform, new Vector3(-1.4f, 1.5f, -1.5f), new Color(1.0f, 0.75f, 0.32f), range: 6f, intensity: 1.1f);
            }
            else if (type == RTSBuildingType.SiegeWorkshop)
            {
                RTSBuildingBanner.AttachBanner(building, new Vector3(-1.8f, 2.92f, 0), height: 2.2f, width: 1.5f);
                RTSAtmosphericLight.CreateTorch(building.transform, new Vector3(1.5f, 1.6f, -1.8f), new Color(1.0f, 0.65f, 0.25f), range: 6.5f, intensity: 1.2f);
            }
            else if (type == RTSBuildingType.Blacksmith)
            {
                RTSAtmosphericLight.CreateTorch(building.transform, new Vector3(0, 1.6f, -1.5f), new Color(1.0f, 0.55f, 0.15f), range: 7f, intensity: 1.35f);
            }

            // Her iki oyuncuda da inşa edilen tüm binalar (sancak, iskele ve meşaleler dahil) 3D sahnede görünür kalır
            building.RefreshFogPerspective();

            return building;
        }

        /// <summary>
        /// Yerel oyuncunun takımına (Mavi veya Kırmızı) göre binanın Savaş Sisi (Fog-of-War) görünürlüğünü günceller.
        /// Dost binalar her zaman tam görünür; rakip binalar sis altında gizlenir ve keşfedildiğinde açılır.
        /// </summary>
        public void RefreshFogPerspective()
        {
            var disc = gameObject.GetComponent<RTSFogDiscoverable>();
            if (team == RTSGameManager.LocalPlayerTeam)
            {
                if (disc != null)
                {
                    disc.SetDiscovered(true);
                    disc.SetShroudDimmed(false);
                    RTSFogDiscoverable.AllDiscoverables.Remove(disc);
                    DestroyImmediate(disc);
                }
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r != null) r.enabled = true;
                }
                foreach (var l in GetComponentsInChildren<Light>(true))
                {
                    if (l != null) l.enabled = true;
                }
            }
            else
            {
                if (disc == null)
                {
                    disc = gameObject.AddComponent<RTSFogDiscoverable>();
                }
                bool vis = RTSFogOfWarManager.Instance != null
                    && (RTSFogOfWarManager.Instance.IsBuildingVisible(this)
                        || RTSFogOfWarManager.Instance.GetCellStateAtWorldPos(transform.position) != FogCellState.Unexplored);
                disc.ApplyVisibility(vis);
            }
        }
    }
}
