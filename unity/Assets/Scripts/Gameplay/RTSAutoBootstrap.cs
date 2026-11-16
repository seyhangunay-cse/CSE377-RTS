using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using CSE377.AI;

namespace CSE377.Gameplay
{
    public enum RTSMapType
    {
        RiverValley,     // Nehir Vadisi: 2 tas kopru, 1 siglik, nehir engeli
        OpenSteppe,      // Acik Bozkir: Nehirsiz, acik ova, koruluklar, serbest manevra
        BlackForest,     // Kara Orman: Yogun gecilmez ormanlar, 2 dar vadi bogazi
        CrossCorners,    // Capraz Yayla: Oyuncu Guney-Bati, Dusman Kuzey-Dogu, merkez antik mabet
        RandomMap        // Rastgele Harita: Her macta rastgele secim
    }

    /// <summary>
    /// Unity sahnesi bos olsa bile Play tusuna basildiginda
    /// tum 3D savas alanini, izometrik kamerayi, birlikleri ve UI arayuzunu otomatik insa eder.
    /// Elle Inspector baglama ihtiyacini ortadan kaldirir.
    /// </summary>
    public class RTSAutoBootstrap : MonoBehaviour
    {
        [Header("Otomatik Kurulum Ayarları")]
        public bool buildOnStartIfEmpty = true;

        public static RTSAutoBootstrap Instance { get; private set; }
        public static RTSMapType SelectedMap = RTSMapType.RiverValley;
        public static int MatchRandomSeed = 133742;
        public static bool HasRiver => (SelectedMap == RTSMapType.RiverValley || (SelectedMap == RTSMapType.RandomMap && activeMapType == RTSMapType.RiverValley));
        public static Vector3 CurrentPlayerBasePos = new Vector3(0, 0, -52f);
        public static Vector3 CurrentEnemyBasePos = new Vector3(0, 0, 55f);
        private static RTSMapType activeMapType = RTSMapType.RiverValley;
        private GameObject mapEnvironmentRoot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitializeScene()
        {
            Application.runInBackground = true;
            if (FindFirstObjectByType<RTSAutoBootstrap>() == null && FindFirstObjectByType<RTSGameManager>() == null)
            {
                var bootGo = new GameObject("[AUTO_BOOTSTRAP]");
                var bootstrap = bootGo.AddComponent<RTSAutoBootstrap>();
                bootstrap.EnsureEnvironment();
            }
        }

        private void Awake()
        {
            Application.runInBackground = true;
            if (Instance == null) Instance = this;
            if (buildOnStartIfEmpty)
            {
                EnsureEnvironment();
            }
        }

        [ContextMenu("Sahneyi Otomatik İnşa Et")]
        public void EnsureEnvironment()
        {
            // Eğer sahne zaten kurulmuşsa tekrar oluşturma
            if (FindFirstObjectByType<RTSGameManager>() != null && FindFirstObjectByType<RTSHUDController>() != null)
            {
                return;
            }

            Debug.Log("[RTSAutoBootstrap] Sahne taranıyor ve RTS savaş alanı otomatik inşa ediliyor...");

            // 1. EventSystem & Işıklandırma
            EnsureLightingAndEvents();

            // 2. Kamera Kurulumu
            EnsureRTSCamera();

            // 3. Zemin ve Taktiksel Bölgeler (North Gate, Main Base)
            EnsureTerrainAndZones(out Transform playerSpawnPoint);

            // 4. Yönetici Nesneleri (AI Manager, Selection, Tech, Fog, Audio, Network & Game Manager)
            var mgrGo = new GameObject("[RTS_Managers]");
            var gameMgr = mgrGo.AddComponent<RTSGameManager>();
            var techMgr = mgrGo.AddComponent<RTSTechManager>();
            var fowMgr = mgrGo.AddComponent<RTSFogOfWarManager>();
            var selectionMgr = mgrGo.AddComponent<RTSSelectionManager>();
            var audioMgr = mgrGo.AddComponent<RTSAudioManager>();
            var vfxMgr = mgrGo.AddComponent<RTSVFXManager>();
            var netMgr = mgrGo.AddComponent<RTSNetworkSyncManager>();
            var gatewayClient = mgrGo.AddComponent<AiGatewayClient>();
            var actionValidator = mgrGo.AddComponent<GameActionValidator>();
            gameMgr.playerSpawnPoint = playerSpawnPoint;

            // 5. Canvas ve UI (HUD + Approval Panel)
            BuildCompleteUI(mgrGo, gameMgr, gatewayClient, out RTSHUDController hud, out ApprovalPanelUI approvalUI);
            gameMgr.hudController = hud;

            // 6. Başlangıç Birlikleri (4 Mızrakçı, 2 Okçu, 3 Düşman Süvarisi)
            SpawnInitialArmies(playerSpawnPoint);

            Debug.Log("✅ [RTSAutoBootstrap] RTS Savaş Alanı ve AI Komutan Arayüzü Başarıyla Kuruldu!");
        }

        private void EnsureLightingAndEvents()
        {
            var light = FindFirstObjectByType<Light>();
            if (light == null)
            {
                var lightGo = new GameObject("Directional Light", typeof(Light));
                light = lightGo.GetComponent<Light>();
                light.type = LightType.Directional;
            }

            // Harita türüne özel dinamik atmosfer ve ışıklandırma (Siyah sisin ve harita sonunun grileşmemesi için RenderSettings.fog kapalı tutulur)
            RenderSettings.fog = false;
            if (activeMapType == RTSMapType.BlackForest)
            {
                light.color = new Color(0.85f, 0.92f, 0.80f);
                light.intensity = 1.05f;
                light.transform.rotation = Quaternion.Euler(46f, -38f, 0);
                RenderSettings.ambientLight = new Color(0.38f, 0.44f, 0.40f);
            }
            else if (activeMapType == RTSMapType.OpenSteppe)
            {
                light.color = new Color(1.05f, 0.98f, 0.82f); // Sıcak altın güneş
                light.intensity = 1.38f;
                light.transform.rotation = Quaternion.Euler(54f, -28f, 0);
                RenderSettings.ambientLight = new Color(0.52f, 0.52f, 0.50f);
            }
            else if (activeMapType == RTSMapType.CrossCorners)
            {
                light.color = new Color(0.96f, 0.98f, 1.05f); // Ferah dağ güneşi
                light.intensity = 1.30f;
                light.transform.rotation = Quaternion.Euler(50f, -45f, 0);
                RenderSettings.ambientLight = new Color(0.46f, 0.50f, 0.58f);
            }
            else // RiverValley
            {
                light.color = new Color(1.0f, 0.95f, 0.86f);
                light.intensity = 1.30f;
                light.transform.rotation = Quaternion.Euler(52f, -32f, 0);
                RenderSettings.ambientLight = new Color(0.48f, 0.52f, 0.58f);
            }

            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.82f;

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }
        }

        private void EnsureRTSCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cam = camGo.GetComponent<Camera>();
                cam.tag = "MainCamera";
            }

            cam.backgroundColor = Color.black;
            cam.clearFlags = CameraClearFlags.SolidColor;

            // Oyuncunun Şehir Merkezine tam ortadan açılı bakış
            cam.transform.position = CurrentPlayerBasePos + new Vector3(0, 26f, -20f);
            cam.transform.rotation = Quaternion.Euler(52f, 0, 0);

            var camCtrl = cam.GetComponent<RTSCameraController>();
            if (camCtrl == null)
            {
                camCtrl = cam.gameObject.AddComponent<RTSCameraController>();
            }
            camCtrl.mapGroundLimitX = 66f;
            camCtrl.mapGroundLimitZ = 66f;
            camCtrl.maxZoomY = 55f;
            camCtrl.enableEdgeScrolling = true;
        }

        private void EnsureTerrainAndZones(out Transform playerSpawn)
        {
            if (SelectedMap == RTSMapType.RandomMap)
            {
                int r = Random.Range(0, 4);
                activeMapType = (r == 0) ? RTSMapType.RiverValley : (r == 1) ? RTSMapType.OpenSteppe : (r == 2) ? RTSMapType.BlackForest : RTSMapType.CrossCorners;
            }
            else
            {
                activeMapType = SelectedMap;
            }

            if (mapEnvironmentRoot == null)
            {
                mapEnvironmentRoot = new GameObject("Map_Environment_Root");
            }

            // 1. Harita türüne göre üs koordinatlarını belirle
            if (activeMapType == RTSMapType.CrossCorners)
            {
                CurrentPlayerBasePos = new Vector3(-46f, 0, -46f);
                CurrentEnemyBasePos = new Vector3(46f, 0, 46f);
            }
            else if (activeMapType == RTSMapType.OpenSteppe)
            {
                CurrentPlayerBasePos = new Vector3(0, 0, -50f);
                CurrentEnemyBasePos = new Vector3(0, 0, 50f);
            }
            else if (activeMapType == RTSMapType.BlackForest)
            {
                CurrentPlayerBasePos = new Vector3(0, 0, -52f);
                CurrentEnemyBasePos = new Vector3(0, 0, 52f);
            }
            else // RiverValley
            {
                CurrentPlayerBasePos = new Vector3(0, 0, -52f);
                CurrentEnemyBasePos = new Vector3(0, 0, 52f);
            }

            // 2. Büyük Zemin (160x160 alan) ve Harita Sonu Simsiyah Sınır Kuşağı
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Terrain_Ground";
            ground.transform.SetParent(mapEnvironmentRoot.transform);
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(16f, 1f, 16f);
            var groundRen = ground.GetComponent<Renderer>();

            Color groundColor = (activeMapType == RTSMapType.OpenSteppe) ? new Color(0.48f, 0.50f, 0.22f) :
                                (activeMapType == RTSMapType.BlackForest) ? new Color(0.18f, 0.35f, 0.16f) :
                                (activeMapType == RTSMapType.CrossCorners) ? new Color(0.28f, 0.44f, 0.24f) :
                                new Color(0.26f, 0.50f, 0.22f); // RiverValley
            groundRen.material.color = groundColor;

            CreateMapBlackAbyssAndBorders(mapEnvironmentRoot.transform);

            // 3. Işıklandırmayı haritaya göre yenile
            EnsureLightingAndEvents();

            // 4. Harita türüne göre coğrafi oluşumlar
            if (activeMapType == RTSMapType.RiverValley)
            {
                BuildRiverAndBridges();
                SpawnDenseForests();
                SpawnMapResources();
                SpawnEnvironmentalDetails();
                CreateMeadowFlora();
            }
            else if (activeMapType == RTSMapType.OpenSteppe)
            {
                SpawnDenseForests_OpenSteppe();
                SpawnMapResources_OpenSteppe();
                CreateSteppeDetails();
                CreateAncientRuins(Vector3.zero, mapEnvironmentRoot.transform);
                CreateSteppeWildflowers();
            }
            else if (activeMapType == RTSMapType.BlackForest)
            {
                SpawnDenseForests_BlackForest();
                SpawnMapResources_BlackForest();
                CreateForestFlora();
            }
            else if (activeMapType == RTSMapType.CrossCorners)
            {
                SpawnDenseForests_CrossCorners();
                SpawnMapResources_CrossCorners();
                CreateAncientRuins(Vector3.zero, mapEnvironmentRoot.transform);
                CreateCrossCornersFlora();
            }

            // 5. Oyuncunun Başlangıç Üssü
            CreateCobblestonePlaza(CurrentPlayerBasePos, new Vector2(11f, 11f), mapEnvironmentRoot.transform);
            CreateBaseCobblestonePathways(CurrentPlayerBasePos, mapEnvironmentRoot.transform);

            var baseZoneGo = new GameObject("Zone_MainBase", typeof(RTSZoneMarker));
            baseZoneGo.transform.SetParent(mapEnvironmentRoot.transform);
            var baseMarker = baseZoneGo.GetComponent<RTSZoneMarker>();
            baseMarker.zoneId = "main_base";
            baseMarker.displayName = "Ana Üs (Şehir Merkezi)";
            baseMarker.radius = 15f;
            baseMarker.gizmoColor = Color.blue;
            baseZoneGo.transform.position = CurrentPlayerBasePos;

            var tc = RTSBuilding.SpawnBuilding(RTSBuildingType.TownCenter, CurrentPlayerBasePos, UnitTeam.Player, startBuilt: true);
            tc.displayName = "Şehir Merkezi";

            var rallyPoint = new GameObject("TownCenter_RallyPoint");
            rallyPoint.transform.SetParent(mapEnvironmentRoot.transform);
            Vector3 rallyOffset = (activeMapType == RTSMapType.CrossCorners) ? new Vector3(4f, 0.1f, 4f) : new Vector3(0, 0.1f, 6f);
            rallyPoint.transform.position = CurrentPlayerBasePos + rallyOffset;
            playerSpawn = rallyPoint.transform;

            // 6. Düşman Karargahı
            CreateCobblestonePlaza(CurrentEnemyBasePos, new Vector2(12f, 12f), mapEnvironmentRoot.transform);
            SpawnEnemyOutpost();

            // 7. Tüm haritalarda bina, maden/çalı ve ağaç çakışmalarını otomatik denetle ve temizle
            ResolveAllMapOverlaps();
        }


        public void RebuildMapForSelectedType()
        {
            if (mapEnvironmentRoot != null)
            {
                DestroyImmediate(mapEnvironmentRoot);
            }

            var existingUnits = new List<RTSUnit>(RTSUnit.AllUnits);
            foreach (var u in existingUnits)
            {
                if (u != null) DestroyImmediate(u.gameObject);
            }
            RTSUnit.AllUnits.Clear();

            var existingBuildings = new List<RTSBuilding>(RTSBuilding.AllBuildings);
            foreach (var b in existingBuildings)
            {
                if (b != null) DestroyImmediate(b.gameObject);
            }
            RTSBuilding.AllBuildings.Clear();

            var existingResources = FindObjectsByType<RTSResourceSource>(FindObjectsSortMode.None);
            foreach (var r in existingResources)
            {
                if (r != null) DestroyImmediate(r.gameObject);
            }

            var existingZones = FindObjectsByType<RTSZoneMarker>(FindObjectsSortMode.None);
            foreach (var z in existingZones)
            {
                if (z != null) DestroyImmediate(z.gameObject);
            }

            var existingProjectiles = FindObjectsByType<RTSProjectile>(FindObjectsSortMode.None);
            foreach (var p in existingProjectiles)
            {
                if (p != null) DestroyImmediate(p.gameObject);
            }

            // Her iki oyuncuda (Host & Client) tüm ağaçların, altın madenlerinin ve başlangıç birimlerinin
            // milimetrik olarak birebir aynı konumda oluşması için ortak deterministik tohum (Seed) kullan!
            UnityEngine.Random.InitState(MatchRandomSeed);

            EnsureTerrainAndZones(out Transform newPlayerSpawn);

            var gameMgr = RTSGameManager.Instance;
            if (gameMgr != null)
            {
                gameMgr.playerSpawnPoint = newPlayerSpawn;
                gameMgr.ResetEconomyForNewMatch();
            }

            SpawnInitialArmies(newPlayerSpawn);

            // Eski haritaya ait keşif kalıntılarını temizle ve savaş sisini sıfırla
            RefreshPerspectiveForLocalTeam();
        }

        /// <summary>
        /// Yerel oyuncunun seçtiği takıma (1. Oyuncu Mavi veya 2. Oyuncu Kırmızı) göre
        /// kamera odak noktasını, bina/asker sis görünürlüğünü ve Savaş Sisini (Fog of War) günceller.
        /// </summary>
        public void RefreshPerspectiveForLocalTeam()
        {
            foreach (var b in RTSBuilding.AllBuildings)
            {
                if (b != null) b.RefreshFogPerspective();
            }

            if (RTSFogOfWarManager.Instance != null)
            {
                RTSFogOfWarManager.Instance.ResetFog();
            }

            foreach (var u in RTSUnit.AllUnits)
            {
                if (u != null)
                {
                    bool vis = (u.team == RTSGameManager.LocalPlayerTeam) ||
                               (RTSFogOfWarManager.Instance != null && RTSFogOfWarManager.Instance.IsUnitVisible(u));
                    u.SetFogOfWarVisibility(vis);
                }
            }

            Vector3 myBasePos = (RTSGameManager.LocalPlayerTeam == UnitTeam.Enemy) ? CurrentEnemyBasePos : CurrentPlayerBasePos;
            var camCtrl = Camera.main != null ? Camera.main.GetComponent<RTSCameraController>() : null;
            if (camCtrl != null)
            {
                camCtrl.FocusOnPosition(myBasePos);
            }

            var minimapCtrl = FindFirstObjectByType<RTSMinimapController>();
            if (minimapCtrl != null)
            {
                minimapCtrl.DrawMinimap();
            }

            RTSGameManager.Instance?.UpdateHUD();
        }

        /// <summary>
        /// Harita sonunu (sınır dışını) tamamen simsiyah (Age of Empires Abyss) gösterir
        /// ve birimlerin harita dışına çıkmasını engelleyen fiziksel sınır duvarları oluşturur.
        /// </summary>
        private void CreateMapBlackAbyssAndBorders(Transform parent)
        {
            var borderRoot = new GameObject("Map_BlackAbyss_Borders");
            borderRoot.transform.SetParent(parent);

            var blackShader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            var blackMat = new Material(blackShader) { color = Color.black };

            // 1. Devasa Dış Simsiyah Boşluk Düzlemi (Harita kenarının ötesinde gökyüzü yerine zifiri siyah görünmesini sağlar)
            var outerAbyss = GameObject.CreatePrimitive(PrimitiveType.Plane);
            outerAbyss.name = "Map_OuterBlackAbyss";
            outerAbyss.transform.SetParent(borderRoot.transform);
            outerAbyss.transform.position = new Vector3(0, -0.06f, 0);
            outerAbyss.transform.localScale = new Vector3(80f, 1f, 80f); // 800x800m simsiyah sonsuzluk
            DestroyImmediate(outerAbyss.GetComponent<Collider>());
            var abyssRen = outerAbyss.GetComponent<Renderer>();
            abyssRen.material = blackMat;
            abyssRen.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            abyssRen.receiveShadows = false;

            // 2. Harita Sonu (|X| >= 80m, |Z| >= 80m) Simsiyah Çerçeve Bantları ve Fiziksel Engelleyiciler
            // Siyah çerçeve bantları harita zemininin (±80m) tam dış sınırından başlar, böylece harita içindeki ağaçların üstünü kesmez!
            CreateBlackBorderStrip(borderRoot.transform, "Map_Border_North", new Vector3(0, 0.30f, 85f), new Vector3(184f, 0.1f, 12f), blackMat);
            CreateBlackBorderStrip(borderRoot.transform, "Map_Border_South", new Vector3(0, 0.30f, -85f), new Vector3(184f, 0.1f, 12f), blackMat);
            CreateBlackBorderStrip(borderRoot.transform, "Map_Border_West", new Vector3(-85f, 0.30f, 0), new Vector3(12f, 0.1f, 184f), blackMat);
            CreateBlackBorderStrip(borderRoot.transform, "Map_Border_East", new Vector3(85f, 0.30f, 0), new Vector3(12f, 0.1f, 184f), blackMat);

            // Fiziksel görünmez sınır duvarları (±70.5m) - Birimlerin geçişini fiziksel olarak da kapatır
            CreateBoundaryBlocker(borderRoot.transform, "Map_Boundary_Blocker_North", new Vector3(0, 2f, 70.5f), new Vector3(160f, 6f, 2f));
            CreateBoundaryBlocker(borderRoot.transform, "Map_Boundary_Blocker_South", new Vector3(0, 2f, -70.5f), new Vector3(160f, 6f, 2f));
            CreateBoundaryBlocker(borderRoot.transform, "Map_Boundary_Blocker_West", new Vector3(-70.5f, 2f, 0), new Vector3(2f, 6f, 160f));
            CreateBoundaryBlocker(borderRoot.transform, "Map_Boundary_Blocker_East", new Vector3(70.5f, 2f, 0), new Vector3(2f, 6f, 160f));
        }

        private void CreateBlackBorderStrip(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = name;
            strip.transform.SetParent(parent);
            strip.transform.position = pos;
            strip.transform.localScale = scale;
            DestroyImmediate(strip.GetComponent<Collider>());
            var ren = strip.GetComponent<Renderer>();
            ren.material = mat;
            ren.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ren.receiveShadows = false;
        }

        private void CreateBoundaryBlocker(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            var blocker = new GameObject(name, typeof(BoxCollider));
            blocker.transform.SetParent(parent);
            blocker.transform.position = pos;
            var box = blocker.GetComponent<BoxCollider>();
            box.size = size;
        }

        private void CreateCobblestonePlaza(Vector3 center, Vector2 size, Transform parent)
        {
            var plaza = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plaza.name = "Cobblestone_Plaza";
            plaza.transform.SetParent(parent);
            plaza.transform.position = new Vector3(center.x, 0.008f, center.z);
            plaza.transform.localScale = new Vector3(size.x, 0.016f, size.y);
            DestroyImmediate(plaza.GetComponent<Collider>());

            var ren = plaza.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse"));
            mat.color = new Color(0.46f, 0.43f, 0.38f);
            mat.SetFloat("_Glossiness", 0.15f);
            ren.material = mat;

            var border = GameObject.CreatePrimitive(PrimitiveType.Cube);
            border.name = "Plaza_Border";
            border.transform.SetParent(plaza.transform);
            border.transform.localPosition = Vector3.zero;
            border.transform.localScale = new Vector3(1.02f, 0.9f, 1.02f);
            DestroyImmediate(border.GetComponent<Collider>());
            var bRen = border.GetComponent<Renderer>();
            var bMat = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse"));
            bMat.color = new Color(0.35f, 0.32f, 0.28f);
            bRen.material = bMat;

            plaza.AddComponent<RTSFogDiscoverable>();
        }

        private void CreateAncientRuins(Vector3 center, Transform parent)
        {
            var ruinsRoot = new GameObject("Ancient_Ruins_Center");
            ruinsRoot.transform.SetParent(parent);
            ruinsRoot.transform.position = center;

            var stoneMat = new Material(Shader.Find("Standard"));
            stoneMat.color = new Color(0.55f, 0.52f, 0.46f);
            stoneMat.SetFloat("_Glossiness", 0.18f);

            Vector3[] pillarOffsets = new Vector3[]
            {
                new Vector3(-6f, 0, -6f),
                new Vector3(6f, 0, -6f),
                new Vector3(-6f, 0, 6f),
                new Vector3(6f, 0, 6f)
            };

            foreach (var offset in pillarOffsets)
            {
                var col = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                col.name = "Ruined_Pillar";
                col.transform.SetParent(ruinsRoot.transform);
                col.transform.position = center + offset + new Vector3(0, 2.2f, 0);
                col.transform.localScale = new Vector3(1.4f, 2.2f, 1.4f);
                col.GetComponent<Renderer>().material = stoneMat;

                var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cap.name = "Pillar_Cap";
                cap.transform.SetParent(col.transform);
                cap.transform.localPosition = new Vector3(0, 1.02f, 0);
                cap.transform.localScale = new Vector3(1.3f, 0.2f, 1.3f);
                cap.GetComponent<Renderer>().material = stoneMat;
            }

            ruinsRoot.AddComponent<RTSFogDiscoverable>();

            RTSResourceSource.CreateGoldRock(center + new Vector3(-3.5f, 0.9f, 0), 1200);
            RTSResourceSource.CreateGoldRock(center + new Vector3(3.5f, 0.9f, 0), 1200);
            RTSResourceSource.CreateBerryBush(center + new Vector3(0, 0.4f, 3.5f), 800);
        }

        private void SpawnWaterLilies(Transform parent)
        {
            var liliesRoot = new GameObject("River_Water_Lilies");
            liliesRoot.transform.SetParent(parent);

            var padMat = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse"));
            padMat.color = new Color(0.14f, 0.46f, 0.18f);
            padMat.SetFloat("_Glossiness", 0.6f);

            var flowerMat = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse"));
            flowerMat.color = new Color(0.96f, 0.88f, 0.94f);

            var pollenMat = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse"));
            pollenMat.color = new Color(0.98f, 0.82f, 0.15f);

            float[] lilyX = new float[] { -64f, -52f, -42f, -14f, 14f, 42f, 54f, 66f };
            foreach (var x in lilyX)
            {
                SpawnLilyCluster(new Vector3(x, 0.078f, -3.8f + Random.Range(-0.4f, 0.4f)), liliesRoot.transform, padMat, flowerMat, pollenMat);
                SpawnLilyCluster(new Vector3(x + 2.5f, 0.078f, 3.8f + Random.Range(-0.4f, 0.4f)), liliesRoot.transform, padMat, flowerMat, pollenMat);
            }

            liliesRoot.AddComponent<RTSFogDiscoverable>();
        }

        private void SpawnLilyCluster(Vector3 center, Transform parent, Material padMat, Material flowerMat, Material pollenMat)
        {
            var cluster = new GameObject("LilyCluster");
            cluster.transform.SetParent(parent);
            cluster.transform.position = center;

            int count = Random.Range(3, 6);
            for (int i = 0; i < count; i++)
            {
                Vector3 off = new Vector3(Random.Range(-0.75f, 0.75f), 0, Random.Range(-0.75f, 0.75f));
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                DestroyImmediate(pad.GetComponent<Collider>());
                pad.transform.SetParent(cluster.transform);
                pad.transform.localPosition = off;
                pad.transform.localScale = new Vector3(Random.Range(0.65f, 0.95f), 0.008f, Random.Range(0.65f, 0.95f));
                pad.GetComponent<Renderer>().material = padMat;

                if (i % 2 == 0)
                {
                    var flower = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    DestroyImmediate(flower.GetComponent<Collider>());
                    flower.transform.SetParent(pad.transform);
                    flower.transform.localPosition = new Vector3(0, 0.75f, 0);
                    flower.transform.localScale = new Vector3(0.35f, 0.45f, 0.35f);
                    flower.GetComponent<Renderer>().material = flowerMat;

                    var pollen = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    DestroyImmediate(pollen.GetComponent<Collider>());
                    pollen.transform.SetParent(flower.transform);
                    pollen.transform.localPosition = new Vector3(0, 0.65f, 0);
                    pollen.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
                    pollen.GetComponent<Renderer>().material = pollenMat;
                }
            }
        }

        private void CreateBaseCobblestonePathways(Vector3 basePos, Transform parent)
        {
            var pathsRoot = new GameObject("Base_Cobblestone_Pathways");
            pathsRoot.transform.SetParent(parent);

            var pathMat = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse"));
            pathMat.color = new Color(0.38f, 0.36f, 0.32f);
            pathMat.SetFloat("_Glossiness", 0.15f);

            for (int i = 0; i < 7; i++)
            {
                CreateStoneSteppingPad(basePos + new Vector3(Random.Range(-0.3f, 0.3f), 0, 11f + i * 2.2f), pathMat, pathsRoot.transform);
            }
            for (int i = 0; i < 6; i++)
            {
                CreateStoneSteppingPad(basePos + new Vector3(11f + i * 2.2f, 0, Random.Range(-0.3f, 0.3f)), pathMat, pathsRoot.transform);
            }
            for (int i = 0; i < 6; i++)
            {
                CreateStoneSteppingPad(basePos + new Vector3(-11f - i * 2.2f, 0, Random.Range(-0.3f, 0.3f)), pathMat, pathsRoot.transform);
            }
        }

        private void CreateStoneSteppingPad(Vector3 worldPos, Material mat, Transform parent)
        {
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(pad.GetComponent<Collider>());
            pad.transform.SetParent(parent);
            pad.transform.position = new Vector3(worldPos.x, 0.018f, worldPos.z);
            pad.transform.localScale = new Vector3(Random.Range(1.8f, 2.3f), 0.025f, Random.Range(1.6f, 2.1f));
            pad.transform.localRotation = Quaternion.Euler(0, Random.Range(-15f, 15f), 0);
            pad.GetComponent<Renderer>().material = mat;
        }

        private void CreateSteppeWildflowers()
        {
            var steppeFlora = new GameObject("Steppe_Flora_Root");
            steppeFlora.transform.SetParent(mapEnvironmentRoot.transform);

            var sageMat = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse"));
            sageMat.color = new Color(0.44f, 0.52f, 0.34f);

            var goldTuftMat = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse"));
            goldTuftMat.color = new Color(0.68f, 0.62f, 0.32f);

            Vector3[] patches = new Vector3[]
            {
                new Vector3(-35f, 0, -22f), new Vector3(35f, 0, -22f),
                new Vector3(-25f, 0, 10f), new Vector3(25f, 0, 10f),
                new Vector3(-45f, 0, 35f), new Vector3(45f, 0, 35f)
            };

            foreach (var pt in patches)
            {
                for (int i = 0; i < 8; i++)
                {
                    Vector3 pos = pt + new Vector3(Random.Range(-5f, 5f), 0.15f, Random.Range(-5f, 5f));
                    var bush = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    DestroyImmediate(bush.GetComponent<Collider>());
                    bush.transform.SetParent(steppeFlora.transform);
                    bush.transform.position = pos;
                    bush.transform.localScale = new Vector3(Random.Range(0.6f, 1.2f), Random.Range(0.3f, 0.6f), Random.Range(0.6f, 1.2f));
                    bush.GetComponent<Renderer>().material = (i % 2 == 0) ? sageMat : goldTuftMat;
                }
            }

            steppeFlora.AddComponent<RTSFogDiscoverable>();
        }

        private void CreateCrossCornersFlora()
        {
            var mountainFlora = new GameObject("Mountain_Flora_Root");
            mountainFlora.transform.SetParent(mapEnvironmentRoot.transform);

            var alpineMat = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse"));
            alpineMat.color = new Color(0.42f, 0.32f, 0.58f);

            var mossMat = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse"));
            mossMat.color = new Color(0.32f, 0.48f, 0.28f);

            for (int i = 0; i < 18; i++)
            {
                Vector3 pos = new Vector3(Random.Range(-55f, 55f), 0.12f, Random.Range(-55f, 55f));
                var m = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                DestroyImmediate(m.GetComponent<Collider>());
                m.transform.SetParent(mountainFlora.transform);
                m.transform.position = pos;
                m.transform.localScale = new Vector3(Random.Range(0.7f, 1.4f), 0.03f, Random.Range(0.7f, 1.4f));
                m.GetComponent<Renderer>().material = (i % 3 == 0) ? alpineMat : mossMat;
            }

            mountainFlora.AddComponent<RTSFogDiscoverable>();
        }

        private void SpawnDenseForests_OpenSteppe()
        {
            var treePrefab = (treePrefabs != null && treePrefabs.Length > 0) ? treePrefabs[0] : null;
            if (treePrefab == null) return;

            var forestRoot = new GameObject("Steppe_Forests");
            forestRoot.transform.SetParent(mapEnvironmentRoot.transform);

            Vector3[] groveCenters = new Vector3[]
            {
                new Vector3(-45f, 0, -32f),
                new Vector3(45f, 0, -32f),
                new Vector3(-50f, 0, 0f),
                new Vector3(50f, 0, 0f),
                new Vector3(-45f, 0, 32f),
                new Vector3(45f, 0, 32f),
                new Vector3(-28f, 0, -58f),
                new Vector3(28f, 0, -58f),
                new Vector3(-28f, 0, 58f),
                new Vector3(28f, 0, 58f)
            };

            foreach (var c in groveCenters)
            {
                int count = Random.Range(6, 10);
                for (int i = 0; i < count; i++)
                {
                    Vector3 offset = new Vector3(Random.Range(-5f, 5f), 0, Random.Range(-5f, 5f));
                    SpawnTree(treePrefab, c + offset, forestRoot.transform);
                }
            }
        }

        private void SpawnMapResources_OpenSteppe()
        {
            Vector3 pBase = CurrentPlayerBasePos;
            Vector3 eBase = CurrentEnemyBasePos;

            RTSResourceSource.CreateGoldRock(pBase + new Vector3(13.5f, 0.9f, -1f), 800);
            RTSResourceSource.CreateGoldRock(pBase + new Vector3(16.0f, 0.9f, -3f), 800);
            RTSResourceSource.CreateBerryBush(pBase + new Vector3(-11.5f, 0.4f, -1f), 600);
            RTSResourceSource.CreateBerryBush(pBase + new Vector3(-14.0f, 0.4f, -3f), 600);

            RTSResourceSource.CreateGoldRock(eBase + new Vector3(-13.5f, 0.9f, 1f), 800);
            RTSResourceSource.CreateGoldRock(eBase + new Vector3(-16.0f, 0.9f, 3f), 800);
            RTSResourceSource.CreateBerryBush(eBase + new Vector3(11.5f, 0.4f, 1f), 600);
            RTSResourceSource.CreateBerryBush(eBase + new Vector3(14.0f, 0.4f, 3f), 600);

            RTSResourceSource.CreateGoldRock(new Vector3(-40f, 0.9f, -10f), 1000);
            RTSResourceSource.CreateGoldRock(new Vector3(40f, 0.9f, 10f), 1000);
        }

        private void CreateSteppeDetails()
        {
            var envRoot = new GameObject("Steppe_Details");
            envRoot.transform.SetParent(mapEnvironmentRoot.transform);

            Vector3[] rockSpots = new Vector3[]
            {
                new Vector3(-25f, 0, -15f),
                new Vector3(25f, 0, 15f),
                new Vector3(-35f, 0, 10f),
                new Vector3(35f, 0, -10f)
            };
            foreach (var spot in rockSpots)
            {
                var rGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rGo.name = "Steppe_Rock";
                rGo.transform.SetParent(envRoot.transform);
                rGo.transform.position = spot + new Vector3(0, 0.8f, 0);
                rGo.transform.localScale = new Vector3(2.5f, 1.6f, 2.8f);
                DestroyImmediate(rGo.GetComponent<Collider>());
                rGo.GetComponent<Renderer>().material.color = new Color(0.48f, 0.45f, 0.40f);
                rGo.AddComponent<RTSFogDiscoverable>();
            }
        }

        private void SpawnDenseForests_BlackForest()
        {
            var treePrefab = (treePrefabs != null && treePrefabs.Length > 0) ? treePrefabs[0] : null;
            if (treePrefab == null) return;

            var forestRoot = new GameObject("BlackForest_DenseTrees");
            forestRoot.transform.SetParent(mapEnvironmentRoot.transform);

            var roadMat = new Material(Shader.Find("Standard"));
            roadMat.color = new Color(0.30f, 0.25f, 0.18f);

            for (int x = -62; x <= 62; x += 3)
            {
                bool isWestChoke = (x >= -24 && x <= -18);
                bool isEastChoke = (x >= 18 && x <= 24);
                if (isWestChoke || isEastChoke)
                {
                    CreateRoadSegment(new Vector3(x, 0.012f, 0), new Vector3(3.5f, 0.02f, 34f), roadMat, forestRoot.transform);
                    continue;
                }

                for (int z = -16; z <= 16; z += 3)
                {
                    Vector3 pos = new Vector3(x + Random.Range(-0.8f, 0.8f), 0, z + Random.Range(-0.8f, 0.8f));
                    SpawnTree(treePrefab, pos, forestRoot.transform);
                }
            }

            for (int x = -58; x <= 58; x += 4)
            {
                SpawnTree(treePrefab, new Vector3(x, 0, -60f), forestRoot.transform);
                SpawnTree(treePrefab, new Vector3(x, 0, -63f), forestRoot.transform);
                SpawnTree(treePrefab, new Vector3(x, 0, 60f), forestRoot.transform);
                SpawnTree(treePrefab, new Vector3(x, 0, 63f), forestRoot.transform);
            }
        }

        private void SpawnMapResources_BlackForest()
        {
            Vector3 pBase = CurrentPlayerBasePos;
            Vector3 eBase = CurrentEnemyBasePos;

            RTSResourceSource.CreateGoldRock(pBase + new Vector3(-13.5f, 0.9f, -1f), 900);
            RTSResourceSource.CreateGoldRock(pBase + new Vector3(13.5f, 0.9f, -1f), 900);
            RTSResourceSource.CreateBerryBush(pBase + new Vector3(-11.5f, 0.4f, -4f), 700);

            RTSResourceSource.CreateGoldRock(eBase + new Vector3(-13.5f, 0.9f, 1f), 900);
            RTSResourceSource.CreateGoldRock(eBase + new Vector3(13.5f, 0.9f, 1f), 900);
            RTSResourceSource.CreateBerryBush(eBase + new Vector3(11.5f, 0.4f, 4f), 700);

            RTSResourceSource.CreateGoldRock(new Vector3(-21f, 0.9f, -8f), 1200);
            RTSResourceSource.CreateGoldRock(new Vector3(21f, 0.9f, 8f), 1200);
        }

        private void CreateForestFlora()
        {
            var floraRoot = new GameObject("BlackForest_Flora");
            floraRoot.transform.SetParent(mapEnvironmentRoot.transform);

            var mushroomMat = new Material(Shader.Find("Standard"));
            mushroomMat.color = new Color(0.85f, 0.32f, 0.18f);

            for (int i = 0; i < 20; i++)
            {
                Vector3 pos = new Vector3(Random.Range(-50f, 50f), 0.15f, Random.Range(-50f, 50f));
                var m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                m.name = "ForestMushroom";
                m.transform.SetParent(floraRoot.transform);
                m.transform.position = pos;
                m.transform.localScale = new Vector3(0.5f, 0.3f, 0.5f);
                DestroyImmediate(m.GetComponent<Collider>());
                m.GetComponent<Renderer>().material = mushroomMat;
                m.AddComponent<RTSFogDiscoverable>();
            }
        }

        private void SpawnDenseForests_CrossCorners()
        {
            var treePrefab = (treePrefabs != null && treePrefabs.Length > 0) ? treePrefabs[0] : null;
            if (treePrefab == null) return;

            var forestRoot = new GameObject("CrossCorners_Forests");
            forestRoot.transform.SetParent(mapEnvironmentRoot.transform);

            for (int x = -68; x <= -15; x += 5)
            {
                for (int z = 15; z <= 68; z += 5)
                {
                    if (Random.value < 0.65f)
                    {
                        SpawnTree(treePrefab, new Vector3(x + Random.Range(-1.5f, 1.5f), 0, z + Random.Range(-1.5f, 1.5f)), forestRoot.transform);
                    }
                }
            }

            for (int x = 15; x <= 68; x += 5)
            {
                for (int z = -68; z <= -15; z += 5)
                {
                    if (Random.value < 0.65f)
                    {
                        SpawnTree(treePrefab, new Vector3(x + Random.Range(-1.5f, 1.5f), 0, z + Random.Range(-1.5f, 1.5f)), forestRoot.transform);
                    }
                }
            }

            SpawnTree(treePrefab, new Vector3(-58f, 0, -52f), forestRoot.transform);
            SpawnTree(treePrefab, new Vector3(-52f, 0, -58f), forestRoot.transform);
            SpawnTree(treePrefab, new Vector3(-58f, 0, -38f), forestRoot.transform);

            SpawnTree(treePrefab, new Vector3(58f, 0, 52f), forestRoot.transform);
            SpawnTree(treePrefab, new Vector3(52f, 0, 58f), forestRoot.transform);
            SpawnTree(treePrefab, new Vector3(58f, 0, 38f), forestRoot.transform);
        }

        private void SpawnMapResources_CrossCorners()
        {
            Vector3 pBase = CurrentPlayerBasePos;
            Vector3 eBase = CurrentEnemyBasePos;

            RTSResourceSource.CreateGoldRock(pBase + new Vector3(-10f, 0.9f, 2f), 800);
            RTSResourceSource.CreateGoldRock(pBase + new Vector3(-10f, 0.9f, -1.5f), 800);
            RTSResourceSource.CreateBerryBush(pBase + new Vector3(2f, 0.4f, -10f), 600);
            RTSResourceSource.CreateBerryBush(pBase + new Vector3(-1.5f, 0.4f, -10f), 600);

            RTSResourceSource.CreateGoldRock(eBase + new Vector3(10f, 0.9f, -2f), 800);
            RTSResourceSource.CreateGoldRock(eBase + new Vector3(10f, 0.9f, 1.5f), 800);
            RTSResourceSource.CreateBerryBush(eBase + new Vector3(-2f, 0.4f, 10f), 600);
            RTSResourceSource.CreateBerryBush(eBase + new Vector3(1.5f, 0.4f, 10f), 600);
        }

        private void BuildRiverAndBridges()
        {
            var riverGo = new GameObject("River_Parent");
            if (mapEnvironmentRoot != null) riverGo.transform.SetParent(mapEnvironmentRoot.transform);

            // 1. Nehir Su Düzlemi (X: -80 .. +80, Z: -5.5 .. +5.5)
            // Y = 0.065f yüksekliğinde tutularak Y = 0.0f'daki yeşil zeminle (Terrain_Ground) Z-fighting yapması ve yeşil çizgi çıkması %100 önlenir
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Water_River";
            water.transform.SetParent(riverGo.transform);
            water.transform.position = new Vector3(0, 0.065f, 0);
            water.transform.localScale = new Vector3(16f, 1f, 1.15f); // 160m uzunluk, 11.5m su genişliği
            Destroy(water.GetComponent<Collider>()); // Doğrudan su düzlemi yerine özel fiziksel engelleyiciler kullanılır

            var waterRen = water.GetComponent<Renderer>();
            var waterMat = new Material(Shader.Find("Standard"));
            waterMat.color = new Color(0.10f, 0.38f, 0.62f, 1.0f); // Derin berrak nehir mavisi (Tam opak, alttaki yeşil zemini asla sızdırmaz)
            waterMat.SetFloat("_Glossiness", 0.90f);
            waterMat.SetFloat("_Metallic", 0.12f);
            waterRen.material = waterMat;
            waterRen.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Prosedürel nehir suyu akıntısı ve güneş parıltısı motoru
            water.AddComponent<RTSRiverWaterFlow>();

            // 2. Fiziksel Nehir Engelleri (River Impassable Blockers)
            // Ünitelerin derin sudan yürümesini engeller; yalnızca köprülerden ve sığlıktan geçilebilir!
            CreateRiverPhysicalBlockers(riverGo.transform);

            // 3. Batı Köprüsü (X = -28, Z = 0) ve Doğu Köprüsü (X = 28, Z = 0)
            BuildStoneBridge(new Vector3(-28f, 0, 0), riverGo.transform, "WestBridge");
            BuildStoneBridge(new Vector3(28f, 0, 0), riverGo.transform, "EastBridge");

            // 4. Merkez Sığ Geçit (X = 0, Z = 0) - Doğal taş basamaklı nehir geçidi
            BuildShallows(new Vector3(0, 0, 0), riverGo.transform);

            // 5. Doğal Kıyı Şeritleri (Riverbanks)
            CreateRiverbanks(riverGo.transform);

            // 6. Nilüferler ve Su Üstü Çiçekleri (Water Lilies)
            SpawnWaterLilies(riverGo.transform);
        }

        private void CreateRiverPhysicalBlockers(Transform parent)
        {
            var blockersRoot = new GameObject("River_Blockers_Root");
            blockersRoot.transform.SetParent(parent);

            // 4 adet aşılmaz fiziksel kutu:
            // Bölüm 1: Batı ucu (-80m .. -32.5m)
            CreateSingleBlocker(new Vector3(-56.25f, 1.0f, 0), new Vector3(47.5f, 3.5f, 11.0f), blockersRoot.transform);
            // Bölüm 2: Batı Köprüsü ile Merkez Sığlık Arası (-23.5m .. -3.5m)
            CreateSingleBlocker(new Vector3(-13.5f, 1.0f, 0), new Vector3(20.0f, 3.5f, 11.0f), blockersRoot.transform);
            // Bölüm 3: Merkez Sığlık ile Doğu Köprüsü Arası (+3.5m .. +23.5m)
            CreateSingleBlocker(new Vector3(13.5f, 1.0f, 0), new Vector3(20.0f, 3.5f, 11.0f), blockersRoot.transform);
            // Bölüm 4: Doğu ucu (+32.5m .. +80m)
            CreateSingleBlocker(new Vector3(56.25f, 1.0f, 0), new Vector3(47.5f, 3.5f, 11.0f), blockersRoot.transform);
        }

        private void CreateSingleBlocker(Vector3 center, Vector3 size, Transform parent)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = "River_Water_Blocker";
            b.transform.SetParent(parent);
            b.transform.position = center;
            b.transform.localScale = size;
            // Görseli kapat, sadece fiziksel çarpışma duvarı olarak çalışsın
            Destroy(b.GetComponent<Renderer>());
        }

        private void BuildStoneBridge(Vector3 position, Transform parent, string bridgeName)
        {
            var bridgeRoot = new GameObject($"Bridge_{bridgeName}");
            bridgeRoot.transform.SetParent(parent);
            bridgeRoot.transform.position = position;

            // Ortak eskitme taş köprü materyali
            var stoneMat = new Material(Shader.Find("Standard"));
            stoneMat.color = new Color(0.42f, 0.42f, 0.44f); // Orta çağ koyu taş grisi
            stoneMat.SetFloat("_Glossiness", 0.15f);

            var parapetMat = new Material(Shader.Find("Standard"));
            parapetMat.color = new Color(0.30f, 0.30f, 0.32f); // Masif korkuluk taşı

            // ========================================================
            // BÖLÜM 1: GÜNEY YAKLAŞIM RAMPASI VE GÜNEY FENERLERİ (Z ≈ -7.5m)
            // ========================================================
            var southRampSegment = new GameObject("Bridge_SouthRamp_Segment");
            southRampSegment.transform.SetParent(bridgeRoot.transform);
            southRampSegment.transform.position = position + new Vector3(0, 0, -7.5f);

            var southRamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            southRamp.name = "Bridge_SouthRamp";
            southRamp.transform.SetParent(southRampSegment.transform);
            southRamp.transform.position = position + new Vector3(0, 0.42f, -7.8f);
            southRamp.transform.rotation = Quaternion.Euler(11f, 0, 0);
            southRamp.transform.localScale = new Vector3(7.2f, 0.38f, 3.8f);
            southRamp.GetComponent<Renderer>().material = stoneMat;
            var sRampCol = southRamp.GetComponent<Collider>();
            if (sRampCol != null) sRampCol.isTrigger = true; // Tetikleyici: Fare sağ tıkı algılar, birim fizik hareketini tıkamaz

            // 2 Güney feneri
            CreateBridgeLantern(position + new Vector3(-3.5f, 1.8f, -6.4f), southRampSegment.transform);
            CreateBridgeLantern(position + new Vector3(3.5f, 1.8f, -6.4f), southRampSegment.transform);

            southRampSegment.AddComponent<RTSFogDiscoverable>();

            // ========================================================
            // BÖLÜM 2: GÜNEY KÖPRÜ TABLİYESİ VE AYAKLARI (Z ≈ -3.25m)
            // ========================================================
            var southSpanSegment = new GameObject("Bridge_SouthSpan_Segment");
            southSpanSegment.transform.SetParent(bridgeRoot.transform);
            southSpanSegment.transform.position = position + new Vector3(0, 0, -3.25f);

            // Güney tabliye yarı parçası (Z: -6.5f .. 0f)
            var southDeck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            southDeck.name = "Bridge_SouthDeck";
            southDeck.transform.SetParent(southSpanSegment.transform);
            southDeck.transform.position = position + new Vector3(0, 0.85f, -3.25f);
            southDeck.transform.localScale = new Vector3(7.0f, 0.40f, 6.5f);
            southDeck.GetComponent<Renderer>().material = stoneMat;
            var sDeckCol = southDeck.GetComponent<Collider>();
            if (sDeckCol != null) sDeckCol.isTrigger = true; // Tetikleyici: Fare sağ tıkı algılar, birim fizik hareketini tıkamaz

            // Güney ayaklar
            CreateStonePillar(position + new Vector3(-2.6f, 0.1f, -3.5f), southSpanSegment.transform, stoneMat);
            CreateStonePillar(position + new Vector3(2.6f, 0.1f, -3.5f), southSpanSegment.transform, stoneMat);

            // Güney korkuluklar (Görsel süsleme, köprü geçiş koridorunu daraltmaması için collider'sız)
            var leftRailingSouth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftRailingSouth.name = "Bridge_LeftRailing_South";
            leftRailingSouth.transform.SetParent(southSpanSegment.transform);
            leftRailingSouth.transform.position = position + new Vector3(-3.5f, 1.38f, -3.25f);
            leftRailingSouth.transform.localScale = new Vector3(0.65f, 0.75f, 6.5f);
            leftRailingSouth.GetComponent<Renderer>().material = parapetMat;
            Destroy(leftRailingSouth.GetComponent<Collider>());

            var rightRailingSouth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightRailingSouth.name = "Bridge_RightRailing_South";
            rightRailingSouth.transform.SetParent(southSpanSegment.transform);
            rightRailingSouth.transform.position = position + new Vector3(3.5f, 1.38f, -3.25f);
            rightRailingSouth.transform.localScale = new Vector3(0.65f, 0.75f, 6.5f);
            rightRailingSouth.GetComponent<Renderer>().material = parapetMat;
            Destroy(rightRailingSouth.GetComponent<Collider>());

            southSpanSegment.AddComponent<RTSFogDiscoverable>();

            // ========================================================
            // BÖLÜM 3: KUZEY KÖPRÜ TABLİYESİ VE AYAKLARI (Z ≈ +3.25m)
            // ========================================================
            var northSpanSegment = new GameObject("Bridge_NorthSpan_Segment");
            northSpanSegment.transform.SetParent(bridgeRoot.transform);
            northSpanSegment.transform.position = position + new Vector3(0, 0, 3.25f);

            // Kuzey tabliye yarı parçası (Z: 0f .. +6.5f)
            var northDeck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            northDeck.name = "Bridge_NorthDeck";
            northDeck.transform.SetParent(northSpanSegment.transform);
            northDeck.transform.position = position + new Vector3(0, 0.85f, 3.25f);
            northDeck.transform.localScale = new Vector3(7.0f, 0.40f, 6.5f);
            northDeck.GetComponent<Renderer>().material = stoneMat;
            var nDeckCol = northDeck.GetComponent<Collider>();
            if (nDeckCol != null) nDeckCol.isTrigger = true; // Tetikleyici: Fare sağ tıkı algılar, birim fizik hareketini tıkamaz

            // Kuzey ayaklar
            CreateStonePillar(position + new Vector3(-2.6f, 0.1f, 3.5f), northSpanSegment.transform, stoneMat);
            CreateStonePillar(position + new Vector3(2.6f, 0.1f, 3.5f), northSpanSegment.transform, stoneMat);

            // Kuzey korkuluklar
            var leftRailingNorth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftRailingNorth.name = "Bridge_LeftRailing_North";
            leftRailingNorth.transform.SetParent(northSpanSegment.transform);
            leftRailingNorth.transform.position = position + new Vector3(-3.5f, 1.38f, 3.25f);
            leftRailingNorth.transform.localScale = new Vector3(0.65f, 0.75f, 6.5f);
            leftRailingNorth.GetComponent<Renderer>().material = parapetMat;
            Destroy(leftRailingNorth.GetComponent<Collider>());

            var rightRailingNorth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightRailingNorth.name = "Bridge_RightRailing_North";
            rightRailingNorth.transform.SetParent(northSpanSegment.transform);
            rightRailingNorth.transform.position = position + new Vector3(3.5f, 1.38f, 3.25f);
            rightRailingNorth.transform.localScale = new Vector3(0.65f, 0.75f, 6.5f);
            rightRailingNorth.GetComponent<Renderer>().material = parapetMat;
            Destroy(rightRailingNorth.GetComponent<Collider>());

            northSpanSegment.AddComponent<RTSFogDiscoverable>();

            // ========================================================
            // BÖLÜM 4: KUZEY YAKLAŞIM RAMPASI VE KUZEY FENERLERİ (Z ≈ +7.5m)
            // ========================================================
            var northRampSegment = new GameObject("Bridge_NorthRamp_Segment");
            northRampSegment.transform.SetParent(bridgeRoot.transform);
            northRampSegment.transform.position = position + new Vector3(0, 0, 7.5f);

            var northRamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            northRamp.name = "Bridge_NorthRamp";
            northRamp.transform.SetParent(northRampSegment.transform);
            northRamp.transform.position = position + new Vector3(0, 0.42f, 7.8f);
            northRamp.transform.rotation = Quaternion.Euler(-11f, 0, 0);
            northRamp.transform.localScale = new Vector3(7.2f, 0.38f, 3.8f);
            northRamp.GetComponent<Renderer>().material = stoneMat;
            var nRampCol = northRamp.GetComponent<Collider>();
            if (nRampCol != null) nRampCol.isTrigger = true; // Tetikleyici: Fare sağ tıkı algılar, birim fizik hareketini tıkamaz

            // 2 Kuzey feneri
            CreateBridgeLantern(position + new Vector3(-3.5f, 1.8f, 6.4f), northRampSegment.transform);
            CreateBridgeLantern(position + new Vector3(3.5f, 1.8f, 6.4f), northRampSegment.transform);

            northRampSegment.AddComponent<RTSFogDiscoverable>();
        }

        private void CreateStonePillar(Vector3 pos, Transform parent, Material mat)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = "StonePillar";
            p.transform.SetParent(parent);
            p.transform.position = pos;
            p.transform.localScale = new Vector3(1.3f, 1.6f, 1.3f);
            p.GetComponent<Renderer>().material = mat;
            Destroy(p.GetComponent<Collider>());
        }

        private void CreateRiverbanks(Transform parent)
        {
            var bankMat = new Material(Shader.Find("Standard"));
            bankMat.color = new Color(0.36f, 0.38f, 0.28f); // Kıyı çakıl/çamur tonu

            // Güney kıyı şeridi (Z = -5.6)
            var southBank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            southBank.name = "SouthBank";
            southBank.transform.SetParent(parent);
            southBank.transform.position = new Vector3(0, 0.06f, -5.6f);
            southBank.transform.localScale = new Vector3(160f, 0.10f, 1.0f);
            southBank.GetComponent<Renderer>().material = bankMat;
            Destroy(southBank.GetComponent<Collider>());

            // Kuzey kıyı şeridi (Z = +5.6)
            var northBank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            northBank.name = "NorthBank";
            northBank.transform.SetParent(parent);
            northBank.transform.position = new Vector3(0, 0.06f, 5.6f);
            northBank.transform.localScale = new Vector3(160f, 0.10f, 1.0f);
            northBank.GetComponent<Renderer>().material = bankMat;
            Destroy(northBank.GetComponent<Collider>());

            // Doğal Köpüklü Kıyı Şeritleri (Shoreline Water Foam Fringe)
            var foamMat = new Material(Shader.Find("Standard"));
            foamMat.color = new Color(0.88f, 0.94f, 1.0f);
            foamMat.SetFloat("_Metallic", 0.1f);
            foamMat.SetFloat("_Glossiness", 0.85f);

            var southFoam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            southFoam.name = "South_Shore_Foam";
            southFoam.transform.SetParent(parent);
            southFoam.transform.position = new Vector3(0, 0.075f, -5.1f);
            southFoam.transform.localScale = new Vector3(160f, 0.02f, 0.65f);
            southFoam.GetComponent<Renderer>().material = foamMat;
            Destroy(southFoam.GetComponent<Collider>());

            var northFoam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            northFoam.name = "North_Shore_Foam";
            northFoam.transform.SetParent(parent);
            northFoam.transform.position = new Vector3(0, 0.075f, 5.1f);
            northFoam.transform.localScale = new Vector3(160f, 0.02f, 0.65f);
            northFoam.GetComponent<Renderer>().material = foamMat;
            Destroy(northFoam.GetComponent<Collider>());

            // Doğal Sazlıklar (Reeds / Cattails) ve Pürüzsüz Nehir Çakılları (Karada, su kıyısında)
            var reedStemMat = new Material(Shader.Find("Standard"));
            reedStemMat.color = new Color(0.24f, 0.42f, 0.18f); // Koyu zeytuni saz yeşili

            var cattailMat = new Material(Shader.Find("Standard"));
            cattailMat.color = new Color(0.32f, 0.18f, 0.10f); // Kadife kahverengi saz başı

            var pebbleMat = new Material(Shader.Find("Standard"));
            pebbleMat.color = new Color(0.38f, 0.40f, 0.38f); // Islak nehir çakılı
            pebbleMat.SetFloat("_Glossiness", 0.7f);

            // Köprülerden (-28 ve +28) ve sığlıktan (0) uzak kıyı noktaları (Karada |Z| = 6.3m)
            float[] southReedX = new float[] { -68f, -58f, -46f, -38f, -18f, -10f, 10f, 18f, 38f, 48f, 60f, 72f };
            float[] northReedX = new float[] { -65f, -55f, -44f, -36f, -16f, -8f, 8f, 16f, 36f, 46f, 58f, 70f };

            foreach (var x in southReedX)
            {
                SpawnReedCluster(new Vector3(x, 0.04f, -6.3f), parent, reedStemMat, cattailMat);
                SpawnBankPebbles(new Vector3(x + 1.2f, 0.03f, -6.1f), parent, pebbleMat);
            }

            foreach (var x in northReedX)
            {
                SpawnReedCluster(new Vector3(x, 0.04f, 6.3f), parent, reedStemMat, cattailMat);
                SpawnBankPebbles(new Vector3(x - 1.2f, 0.03f, 6.1f), parent, pebbleMat);
            }
        }

        private void SpawnReedCluster(Vector3 basePos, Transform parent, Material stemMat, Material tipMat)
        {
            var clusterGo = new GameObject("Reed_Cluster");
            clusterGo.transform.SetParent(parent);
            clusterGo.transform.position = basePos;

            int stemCount = Random.Range(6, 10);
            for (int i = 0; i < stemCount; i++)
            {
                float h = Random.Range(1.2f, 1.8f);
                var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stem.transform.SetParent(clusterGo.transform);
                stem.transform.localPosition = new Vector3(Random.Range(-0.35f, 0.35f), h * 0.5f, Random.Range(-0.35f, 0.35f));
                stem.transform.localScale = new Vector3(0.055f, h * 0.5f, 0.055f);
                stem.transform.localRotation = Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(0, 360), Random.Range(-8f, 8f));
                stem.GetComponent<Renderer>().material = stemMat;
                Destroy(stem.GetComponent<Collider>());

                // Saz Başlığı (Cattail tip)
                var tip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tip.transform.SetParent(stem.transform);
                tip.transform.localPosition = new Vector3(0, 0.72f, 0);
                tip.transform.localScale = new Vector3(2.2f, 0.28f, 2.2f);
                tip.GetComponent<Renderer>().material = tipMat;
                Destroy(tip.GetComponent<Collider>());
            }

            clusterGo.AddComponent<RTSFogDiscoverable>();
        }

        private void SpawnBankPebbles(Vector3 basePos, Transform parent, Material pebbleMat)
        {
            var pebbleGo = new GameObject("River_Pebbles");
            pebbleGo.transform.SetParent(parent);
            pebbleGo.transform.position = basePos;

            int pebbleCount = Random.Range(4, 7);
            for (int i = 0; i < pebbleCount; i++)
            {
                var pebble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pebble.transform.SetParent(pebbleGo.transform);
                pebble.transform.localPosition = new Vector3(Random.Range(-0.6f, 0.6f), 0.05f, Random.Range(-0.4f, 0.4f));
                pebble.transform.localScale = new Vector3(Random.Range(0.25f, 0.50f), Random.Range(0.08f, 0.15f), Random.Range(0.20f, 0.40f));
                pebble.transform.localRotation = Quaternion.Euler(Random.Range(-10f, 10f), Random.Range(0, 360), Random.Range(-10f, 10f));
                pebble.GetComponent<Renderer>().material = pebbleMat;
                Destroy(pebble.GetComponent<Collider>());
            }

            pebbleGo.AddComponent<RTSFogDiscoverable>();
        }

        private void CreateMeadowFlora()
        {
            var meadowRoot = new GameObject("Meadow_Flora_Root");
            if (mapEnvironmentRoot != null) meadowRoot.transform.SetParent(mapEnvironmentRoot.transform);

            var poppyMat = new Material(Shader.Find("Standard"));
            poppyMat.color = new Color(0.88f, 0.14f, 0.12f); // Kırmızı gelincik

            var dandelionMat = new Material(Shader.Find("Standard"));
            dandelionMat.color = new Color(0.96f, 0.82f, 0.10f); // Altın karahindiba

            var daisyMat = new Material(Shader.Find("Standard"));
            daisyMat.color = new Color(0.95f, 0.95f, 0.95f); // Beyaz papatya

            var stemMat = new Material(Shader.Find("Standard"));
            stemMat.color = new Color(0.22f, 0.45f, 0.18f); // Çiçek sapı

            var grassTuftMat = new Material(Shader.Find("Standard"));
            grassTuftMat.color = new Color(0.28f, 0.54f, 0.22f); // Çim demeti

            Vector3[] meadowPatches = new Vector3[]
            {
                new Vector3(-22f, 0, -42f),
                new Vector3(20f, 0, -42f),
                new Vector3(-6f, 0, -32f),
                new Vector3(16f, 0, -30f),
                new Vector3(-45f, 0, -18f),
                new Vector3(-14f, 0, -15f),
                new Vector3(14f, 0, -15f),
                new Vector3(45f, 0, -18f),
                new Vector3(-44f, 0, 18f),
                new Vector3(-15f, 0, 16f),
                new Vector3(15f, 0, 16f),
                new Vector3(44f, 0, 18f),
                new Vector3(-20f, 0, 38f),
                new Vector3(22f, 0, 40f)
            };

            foreach (var patchCenter in meadowPatches)
            {
                var patchGo = new GameObject("Flora_Patch");
                patchGo.transform.SetParent(meadowRoot.transform);
                patchGo.transform.position = patchCenter;

                int flowerCount = Random.Range(10, 16);
                for (int i = 0; i < flowerCount; i++)
                {
                    Vector3 offset = new Vector3(Random.Range(-3.5f, 3.5f), 0, Random.Range(-3.5f, 3.5f));
                    float r = Random.value;
                    Material flowerHeadMat = (r < 0.38f) ? poppyMat : (r < 0.72f ? dandelionMat : daisyMat);

                    // Sap
                    float stemH = Random.Range(0.25f, 0.45f);
                    var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    stem.transform.SetParent(patchGo.transform);
                    stem.transform.localPosition = offset + new Vector3(0, stemH * 0.5f, 0);
                    stem.transform.localScale = new Vector3(0.03f, stemH * 0.5f, 0.03f);
                    stem.transform.localRotation = Quaternion.Euler(Random.Range(-7f, 7f), Random.Range(0, 360), Random.Range(-7f, 7f));
                    stem.GetComponent<Renderer>().material = stemMat;
                    Destroy(stem.GetComponent<Collider>());

                    // Çiçek Başı
                    var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    head.transform.SetParent(stem.transform);
                    head.transform.localPosition = new Vector3(0, 0.95f, 0);
                    head.transform.localScale = new Vector3(3.8f, 1.2f, 3.8f);
                    head.GetComponent<Renderer>().material = flowerHeadMat;
                    Destroy(head.GetComponent<Collider>());
                }

                // Çim demetleri
                int tuftCount = Random.Range(5, 9);
                for (int j = 0; j < tuftCount; j++)
                {
                    Vector3 offset = new Vector3(Random.Range(-3.2f, 3.2f), 0, Random.Range(-3.2f, 3.2f));
                    float tuftH = Random.Range(0.35f, 0.55f);
                    var tuft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    tuft.transform.SetParent(patchGo.transform);
                    tuft.transform.localPosition = offset + new Vector3(0, tuftH * 0.5f, 0);
                    tuft.transform.localScale = new Vector3(0.045f, tuftH * 0.5f, 0.045f);
                    tuft.transform.localRotation = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0, 360), Random.Range(-12f, 12f));
                    tuft.GetComponent<Renderer>().material = grassTuftMat;
                    Destroy(tuft.GetComponent<Collider>());
                }

                patchGo.AddComponent<RTSFogDiscoverable>();
            }
        }

        private void CreateBridgeLantern(Vector3 pos, Transform parent)
        {
            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.transform.SetParent(parent);
            post.transform.position = pos - new Vector3(0, 0.35f, 0);
            post.transform.localScale = new Vector3(0.22f, 0.45f, 0.22f);
            post.GetComponent<Renderer>().material.color = new Color(0.22f, 0.20f, 0.18f); // Dökme demir
            Destroy(post.GetComponent<Collider>());

            var lightGo = new GameObject("LanternLight");
            lightGo.transform.SetParent(parent);
            lightGo.transform.position = pos + new Vector3(0, 0.25f, 0);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1.0f, 0.72f, 0.30f); // Sıcak kehribar fener ışığı
            l.range = 7.5f;
            l.intensity = 1.6f;
        }

        private void BuildShallows(Vector3 position, Transform parent)
        {
            var shallowGo = new GameObject("Shallows_Center");
            shallowGo.transform.SetParent(parent);
            shallowGo.transform.position = position;

            var shallowMat = new Material(Shader.Find("Standard"));
            shallowMat.color = new Color(0.52f, 0.50f, 0.46f); // Doğal ırmak çakıl taşı

            for (int i = -3; i <= 3; i++)
            {
                var stone = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stone.transform.SetParent(shallowGo.transform);
                stone.transform.position = position + new Vector3(i * 1.4f, 0.075f, Random.Range(-0.6f, 0.6f));
                stone.transform.localScale = new Vector3(1.2f, 0.14f, 8.8f);
                stone.GetComponent<Renderer>().material = shallowMat;
                Destroy(stone.GetComponent<Collider>());
            }

            // Savaş sisi entegrasyonu: Sığlık taşları keşfedilene kadar gizli kalır
            shallowGo.AddComponent<RTSFogDiscoverable>();
        }

        private GameObject[] treePrefabs;
        private GameObject fallenLogPrefab;
        private GameObject treeTrunkPrefab;
        private GameObject rockLargePrefab;
        private GameObject rockSmallPrefab;

        private void SpawnDenseForests()
        {
            var pTreeLarge = Resources.Load<GameObject>("Models/tree-large");
            var pTreeSmall = Resources.Load<GameObject>("Models/tree-small");
            var pTree = Resources.Load<GameObject>("Models/Tree");
            var pTreeL = Resources.Load<GameObject>("Models/TreeLarge");

            var tList = new System.Collections.Generic.List<GameObject>();
            if (pTreeLarge != null) tList.Add(pTreeLarge);
            if (pTreeSmall != null) tList.Add(pTreeSmall);
            if (pTree != null) tList.Add(pTree);
            if (pTreeL != null) tList.Add(pTreeL);
            treePrefabs = tList.ToArray();

            fallenLogPrefab = Resources.Load<GameObject>("Models/tree-log");
            treeTrunkPrefab = Resources.Load<GameObject>("Models/tree-trunk");
            rockLargePrefab = Resources.Load<GameObject>("Models/rocks-large") ?? Resources.Load<GameObject>("Models/RocksLarge");
            rockSmallPrefab = Resources.Load<GameObject>("Models/rocks-small");

            var treePrefab = (treePrefabs != null && treePrefabs.Length > 0) ? treePrefabs[0] : null;
            if (treePrefab == null) return;

            var forestRoot = new GameObject("Forests_Root");
            if (mapEnvironmentRoot != null) forestRoot.transform.SetParent(mapEnvironmentRoot.transform);

            // 1. Oyuncu Üssü Orman Kuşağı
            // Klasik Age of Empires başlangıç ağaçları (Şehir Merkezi arkasında ve güvenli mesafede - kaynak taşıma koridorlarını tıkamaz)
            SpawnTree(treePrefab, new Vector3(-10f, 0, -58f), forestRoot.transform);
            SpawnTree(treePrefab, new Vector3(10f, 0, -58f), forestRoot.transform);
            SpawnTree(treePrefab, new Vector3(-14f, 0, -54f), forestRoot.transform);
            SpawnTree(treePrefab, new Vector3(14f, 0, -54f), forestRoot.transform);

            // Arka koruyucu orman (Harita sınırının dışına taşmaması için Z = -59 .. -63 aralığında)
            for (int x = -32; x <= 32; x += 4)
            {
                float z = -60f + Mathf.Sin(x * 0.2f) * 2.0f;
                SpawnTree(treePrefab, new Vector3(x + Random.Range(-0.8f, 0.8f), 0, z + Random.Range(-0.6f, 0.6f)), forestRoot.transform);
                SpawnTree(treePrefab, new Vector3(x + Random.Range(-0.8f, 0.8f), 0, z - 2.5f + Random.Range(-0.6f, 0.6f)), forestRoot.transform);
            }
            // Batı kanadı (X = -26 .. -34, Z = -44 .. -58)
            for (int z = -44; z >= -58; z -= 4)
            {
                float x = -28f - Mathf.Abs(z + 51) * 0.4f;
                SpawnTree(treePrefab, new Vector3(x + Random.Range(-0.8f, 0.8f), 0, z), forestRoot.transform);
                SpawnTree(treePrefab, new Vector3(x - 3.5f + Random.Range(-0.8f, 0.8f), 0, z), forestRoot.transform);
            }
            // Doğu kanadı (X = 26 .. 34, Z = -44 .. -58)
            for (int z = -44; z >= -58; z -= 4)
            {
                float x = 28f + Mathf.Abs(z + 51) * 0.4f;
                SpawnTree(treePrefab, new Vector3(x + Random.Range(-0.8f, 0.8f), 0, z), forestRoot.transform);
                SpawnTree(treePrefab, new Vector3(x + 3.5f + Random.Range(-0.8f, 0.8f), 0, z), forestRoot.transform);
            }

            // 2. Düşman Üssü Orman Kuşağı (Kuzey Arka & Yan Koruluklar - Harita içinde Z = 61 .. 64)
            for (int x = -32; x <= 32; x += 4)
            {
                float z = 62f + Mathf.Sin(x * 0.2f) * 2.0f;
                SpawnTree(treePrefab, new Vector3(x + Random.Range(-0.8f, 0.8f), 0, z + Random.Range(-0.6f, 0.6f)), forestRoot.transform);
                SpawnTree(treePrefab, new Vector3(x + Random.Range(-0.8f, 0.8f), 0, z + 2.5f + Random.Range(-0.6f, 0.6f)), forestRoot.transform);
            }
            for (int z = 46; z <= 60; z += 4)
            {
                SpawnTree(treePrefab, new Vector3(-28f + Random.Range(-0.8f, 0.8f), 0, z), forestRoot.transform);
                SpawnTree(treePrefab, new Vector3(28f + Random.Range(-0.8f, 0.8f), 0, z), forestRoot.transform);
            }

            // 3. Nehir Kenarı & Harita Ortası Vahşi Ormanlar
            Vector3[] wildForestSpots = new Vector3[]
            {
                new Vector3(-55f, 0, -12f), new Vector3(-50f, 0, -10f), new Vector3(-58f, 0, -15f),
                new Vector3(55f, 0, -12f), new Vector3(50f, 0, -10f), new Vector3(58f, 0, -15f),
                new Vector3(-55f, 0, 12f), new Vector3(-50f, 0, 10f), new Vector3(-58f, 0, 15f),
                new Vector3(55f, 0, 12f), new Vector3(50f, 0, 10f), new Vector3(58f, 0, 15f),
                new Vector3(-12f, 0, -12f), new Vector3(-8f, 0, -14f),
                new Vector3(12f, 0, 12f), new Vector3(8f, 0, 14f)
            };
            foreach (var spot in wildForestSpots)
            {
                SpawnTree(treePrefab, spot, forestRoot.transform);
            }
        }

        private void SpawnTree(GameObject defaultPrefab, Vector3 pos, Transform parent)
        {
            // Harita sınırı dışında veya sınıra çok yakın (±64m ötesinde) ağaç oluşturulmasını kesin olarak engelle
            if (Mathf.Abs(pos.x) > 64f || Mathf.Abs(pos.z) > 64f)
            {
                return;
            }

            var prefabToUse = (treePrefabs != null && treePrefabs.Length > 0)
                ? treePrefabs[Random.Range(0, treePrefabs.Length)]
                : defaultPrefab;
            if (prefabToUse == null) return;

            var tree = Instantiate(prefabToUse, pos, Quaternion.Euler(0, Random.Range(0, 360), 0), parent);
            tree.transform.localScale = Vector3.one * Random.Range(1.8f, 2.7f);

            // Ağaç 3D modelindeki devasa yaprak/dal mesh collider'larını temizle (köylülerin yapraklara takılmasını önler)
            foreach (var col in tree.GetComponentsInChildren<Collider>(true))
            {
                DestroyImmediate(col);
            }

            // Sadece gövdeyi (Trunk) kaplayan dar ve gerçekçi bir kapsül collider ekle
            var trunkCol = tree.AddComponent<CapsuleCollider>();
            trunkCol.center = new Vector3(0, 1.5f, 0);
            trunkCol.radius = 0.20f; // Scale ~2.2x ile dünya uzayında yaklaşık 0.44m gövde yarıçapı
            trunkCol.height = 3.0f;

            var res = tree.AddComponent<RTSResourceSource>();
            res.resourceType = RTSResourceType.Wood;
            res.remainingAmount = 400;
            res.maxAmount = 400;

            // Orman zemini zenginliği: Ara sıra yosunlu devrilmiş kütük veya kesilmiş ağaç gövdesi ekle
            if (Random.value < 0.15f && fallenLogPrefab != null)
            {
                var logPos = pos + new Vector3(Random.Range(-2f, 2f), 0.05f, Random.Range(-2f, 2f));
                var log = Instantiate(fallenLogPrefab, logPos, Quaternion.Euler(0, Random.Range(0, 360), 0), parent);
                log.transform.localScale = Vector3.one * Random.Range(1.6f, 2.2f);
                foreach (var c in log.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
                log.AddComponent<RTSFogDiscoverable>();
            }
            else if (Random.value < 0.15f && treeTrunkPrefab != null)
            {
                var trunkPos = pos + new Vector3(Random.Range(-2f, 2f), 0.05f, Random.Range(-2f, 2f));
                var trunk = Instantiate(treeTrunkPrefab, trunkPos, Quaternion.Euler(0, Random.Range(0, 360), 0), parent);
                trunk.transform.localScale = Vector3.one * Random.Range(1.6f, 2.2f);
                foreach (var c in trunk.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
                trunk.AddComponent<RTSFogDiscoverable>();
            }
        }

        private void SpawnEnvironmentalDetails()
        {
            var envRoot = new GameObject("Environment_Details_Root");
            if (mapEnvironmentRoot != null) envRoot.transform.SetParent(mapEnvironmentRoot.transform);

            // 1. Madenlerin Etrafında Doğal Kaya Oluşumları (Taş ocağı / maden yatağı hissi - Karada)
            Vector3[] rockClusterCenters = new Vector3[]
            {
                new Vector3(14f, 0, -53f),  // Oyuncu Altın Madeni
                new Vector3(-14f, 0, 53f),  // Düşman Altın Madeni
                new Vector3(-45f, 0, -16f), // Batı Güney Zengin Altın Rezervi (Karada)
                new Vector3(-45f, 0, 16f),  // Batı Kuzey Zengin Altın Rezervi (Karada)
                new Vector3(45f, 0, -16f),  // Doğu Güney Zengin Altın Rezervi (Karada)
                new Vector3(45f, 0, 16f)   // Doğu Kuzey Zengin Altın Rezervi (Karada)
            };

            foreach (var center in rockClusterCenters)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (rockLargePrefab != null)
                    {
                        var pos = center + new Vector3(Random.Range(-2.5f, 2.5f), 0, Random.Range(-2.2f, 2.2f));
                        var rock = Instantiate(rockLargePrefab, pos, Quaternion.Euler(Random.Range(-10, 10), Random.Range(0, 360), Random.Range(-10, 10)), envRoot.transform);
                        rock.transform.localScale = Vector3.one * Random.Range(1.8f, 2.5f);
                        rock.AddComponent<RTSFogDiscoverable>();
                    }
                }
                for (int i = 0; i < 4; i++)
                {
                    if (rockSmallPrefab != null)
                    {
                        var pos = center + new Vector3(Random.Range(-2.8f, 2.8f), 0, Random.Range(-2.5f, 2.5f));
                        var rock = Instantiate(rockSmallPrefab, pos, Quaternion.Euler(Random.Range(-10, 10), Random.Range(0, 360), Random.Range(-10, 10)), envRoot.transform);
                        rock.transform.localScale = Vector3.one * Random.Range(1.5f, 2.2f);
                        rock.AddComponent<RTSFogDiscoverable>();
                    }
                }
            }

            // 2. Nehir Kenarı Kıyı Kayalıkları & Köprü Temel Tahkimatı (Karada, su dışında)
            Vector3[] riverRockSpots = new Vector3[]
            {
                new Vector3(-34f, 0.1f, -7.5f), new Vector3(-22f, 0.1f, -7.5f),
                new Vector3(-34f, 0.1f, 7.5f),  new Vector3(-22f, 0.1f, 7.5f),
                new Vector3(22f, 0.1f, -7.5f),  new Vector3(34f, 0.1f, -7.5f),
                new Vector3(22f, 0.1f, 7.5f),   new Vector3(34f, 0.1f, 7.5f),
                new Vector3(-8f, 0.1f, -7.2f),  new Vector3(8f, 0.1f, -7.2f),
                new Vector3(-8f, 0.1f, 7.2f),   new Vector3(8f, 0.1f, 7.2f),
                new Vector3(-60f, 0.1f, -7.5f), new Vector3(60f, 0.1f, 7.5f)
            };
            foreach (var spot in riverRockSpots)
            {
                var p = (Random.value > 0.5f && rockLargePrefab != null) ? rockLargePrefab : rockSmallPrefab;
                if (p != null)
                {
                    var r = Instantiate(p, spot, Quaternion.Euler(0, Random.Range(0, 360), 0), envRoot.transform);
                    r.transform.localScale = Vector3.one * Random.Range(1.6f, 2.4f);
                    r.AddComponent<RTSFogDiscoverable>();
                }
            }

            // 3. Stratejik Toprak Yollar (Dirt Roads - Köprülerden Üslere Doğru)
            var roadMat = new Material(Shader.Find("Standard"));
            roadMat.color = new Color(0.38f, 0.32f, 0.22f); // Doğal killi toprak yol

            CreateRoadSegment(new Vector3(-28f, 0.02f, -22f), new Vector3(5.5f, 0.02f, 26f), roadMat, envRoot.transform);
            CreateRoadSegment(new Vector3(28f, 0.02f, -22f), new Vector3(5.5f, 0.02f, 26f), roadMat, envRoot.transform);
            CreateRoadSegment(new Vector3(0f, 0.02f, -26f), new Vector3(6f, 0.02f, 36f), roadMat, envRoot.transform);

            CreateRoadSegment(new Vector3(-28f, 0.02f, 22f), new Vector3(5.5f, 0.02f, 26f), roadMat, envRoot.transform);
            CreateRoadSegment(new Vector3(28f, 0.02f, 22f), new Vector3(5.5f, 0.02f, 26f), roadMat, envRoot.transform);
            CreateRoadSegment(new Vector3(0f, 0.02f, 26f), new Vector3(6f, 0.02f, 36f), roadMat, envRoot.transform);

            // 4. Gökyüzünde Süzülen Şahin & Kuş Sürüsü (Overhead Birds)
            SpawnOverheadWildlife(envRoot.transform);

            // 5. Doğal Çayır ve Kır Çiçekleri (Meadow Wildflowers)
            SpawnMeadowWildflowers(envRoot.transform);
        }

        private void SpawnOverheadWildlife(Transform parent)
        {
            var flockGo = new GameObject("Overhead_Bird_Flock");
            flockGo.transform.SetParent(parent, false);
            flockGo.transform.position = Vector3.zero;
            var flock = flockGo.AddComponent<RTSOverheadFlock>();
            flock.birdCount = 6;
            flock.flyAltitude = 26f;
            flock.flightSpeed = 10f;
            flock.orbitRadius = 48f;
        }

        private void SpawnMeadowWildflowers(Transform parent)
        {
            var flowersRoot = new GameObject("Meadow_Wildflowers_Root");
            flowersRoot.transform.SetParent(parent, false);

            Color[] flowerColors = new Color[]
            {
                new Color(0.92f, 0.22f, 0.25f), // Gelincik / Yaban Kırmızısı
                new Color(0.96f, 0.85f, 0.18f), // Sarı Papatya / Düğün Çiçeği
                new Color(0.38f, 0.58f, 0.95f), // Peygamber Çiçeği Mavisi
                new Color(0.88f, 0.88f, 0.92f)  // Beyaz Kır Papatyası
            };

            // 18 doğal kır çiçeği öbeği (Açık çayırlarda)
            for (int cluster = 0; cluster < 18; cluster++)
            {
                float cx = Random.Range(-65f, 65f);
                float cz = Random.Range(-65f, 65f);

                // Nehir yatağından kaçın (|Z| < 8m)
                if (RTSAutoBootstrap.HasRiver && Mathf.Abs(cz) < 8.0f)
                {
                    cz = (cz >= 0) ? 14f + Random.Range(0f, 15f) : -14f - Random.Range(0f, 15f);
                }

                Color col = flowerColors[cluster % flowerColors.Length];
                var clusterGo = new GameObject($"Flower_Cluster_{cluster}");
                clusterGo.transform.SetParent(flowersRoot.transform, false);
                clusterGo.transform.position = new Vector3(cx, 0.04f, cz);
                clusterGo.AddComponent<RTSFogDiscoverable>();

                var mat = new Material(Shader.Find("Standard"));
                mat.color = col;
                mat.SetFloat("_Glossiness", 0.1f);

                int countInCluster = Random.Range(4, 7);
                for (int f = 0; f < countInCluster; f++)
                {
                    var flower = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    DestroyImmediate(flower.GetComponent<Collider>());
                    flower.transform.SetParent(clusterGo.transform, false);
                    flower.transform.localPosition = new Vector3(
                        Random.Range(-1.2f, 1.2f),
                        Random.Range(0.08f, 0.16f),
                        Random.Range(-1.2f, 1.2f)
                    );
                    flower.transform.localScale = Vector3.one * Random.Range(0.18f, 0.28f);
                    var ren = flower.GetComponent<Renderer>();
                    if (ren != null) ren.material = mat;
                }
            }
        }

        private void CreateRoadSegment(Vector3 position, Vector3 size, Material mat, Transform parent)
        {
            var road = GameObject.CreatePrimitive(PrimitiveType.Cube);
            road.name = "DirtRoad";
            road.transform.SetParent(parent);
            road.transform.position = position;
            road.transform.localScale = size;
            road.GetComponent<Renderer>().material = mat;
            Destroy(road.GetComponent<Collider>());
            road.AddComponent<RTSFogDiscoverable>();
        }

        private void SpawnMapResources()
        {
            // 1. Oyuncu Üs Kaynakları (Güney Z = -52 civarı, yan ceplerde - binalarla asla çakışmaz)
            // Böğürtlenler (Yiyecek - 4 çalı kümesi)
            RTSResourceSource.CreateBerryBush(new Vector3(-11f, 0.4f, -51.5f), 600);
            RTSResourceSource.CreateBerryBush(new Vector3(-13.5f, 0.4f, -51.5f), 600);
            RTSResourceSource.CreateBerryBush(new Vector3(-11f, 0.4f, -54.0f), 600);
            RTSResourceSource.CreateBerryBush(new Vector3(-13.5f, 0.4f, -54.0f), 600);

            // Altın Madeni (3 altın kayası)
            RTSResourceSource.CreateGoldRock(new Vector3(13f, 0.9f, -51.5f), 800);
            RTSResourceSource.CreateGoldRock(new Vector3(15.5f, 0.9f, -52.5f), 800);
            RTSResourceSource.CreateGoldRock(new Vector3(13.5f, 0.9f, -54.5f), 800);

            // 2. Düşman Üs Kaynakları (Kuzey Z = +52 civarı, yan ceplerde - ön savunma binalarından tamamen ayrı)
            RTSResourceSource.CreateBerryBush(new Vector3(11f, 0.4f, 51.5f), 600);
            RTSResourceSource.CreateBerryBush(new Vector3(13.5f, 0.4f, 51.5f), 600);
            RTSResourceSource.CreateBerryBush(new Vector3(11f, 0.4f, 54.0f), 600);
            RTSResourceSource.CreateBerryBush(new Vector3(13.5f, 0.4f, 54.0f), 600);

            RTSResourceSource.CreateGoldRock(new Vector3(-13f, 0.9f, 51.5f), 800);
            RTSResourceSource.CreateGoldRock(new Vector3(-15.5f, 0.9f, 52.5f), 800);
            RTSResourceSource.CreateGoldRock(new Vector3(-13.5f, 0.9f, 54.5f), 800);

            // 3. Orta Bölge Nötr Zengin Madenler (Karada, nehirden güvenli mesafede |Z| = 14-16m)
            // Batı zengin altın madenleri (Güney & Kuzey çayırları)
            RTSResourceSource.CreateGoldRock(new Vector3(-44f, 0.9f, -16f), 1000);
            RTSResourceSource.CreateGoldRock(new Vector3(-47f, 0.9f, -14f), 1000);
            RTSResourceSource.CreateGoldRock(new Vector3(-44f, 0.9f, 16f), 1000);
            RTSResourceSource.CreateGoldRock(new Vector3(-47f, 0.9f, 14f), 1000);

            // Doğu zengin altın madenleri (Güney & Kuzey çayırları)
            RTSResourceSource.CreateGoldRock(new Vector3(44f, 0.9f, -16f), 1000);
            RTSResourceSource.CreateGoldRock(new Vector3(47f, 0.9f, -14f), 1000);
            RTSResourceSource.CreateGoldRock(new Vector3(44f, 0.9f, 16f), 1000);
            RTSResourceSource.CreateGoldRock(new Vector3(47f, 0.9f, 14f), 1000);

            // Nehir kenarı vahşi meyveler (Karada)
            RTSResourceSource.CreateBerryBush(new Vector3(-18f, 0.4f, -12f), 600);
            RTSResourceSource.CreateBerryBush(new Vector3(18f, 0.4f, 12f), 600);
        }

        private void SpawnEnemyOutpost()
        {
            Vector3 eBase = CurrentEnemyBasePos;

            var enemyZoneGo = new GameObject("Zone_EnemyOutpost", typeof(RTSZoneMarker));
            if (mapEnvironmentRoot != null) enemyZoneGo.transform.SetParent(mapEnvironmentRoot.transform);
            var enemyMarker = enemyZoneGo.GetComponent<RTSZoneMarker>();
            enemyMarker.zoneId = "enemy_base";
            enemyMarker.displayName = "Kırmızı Şehir Merkezi";
            enemyMarker.radius = 15f;
            enemyMarker.gizmoColor = Color.red;
            enemyZoneGo.transform.position = eBase + Vector3.up * 0.1f;

            // Kırmızı Krallık da Mavi Krallık ile %100 eşit başlar: Yalnızca 1 Şehir Merkezi!
            // (Kışla, Okçu Talimgahı, Kuşatma Atölyesi ve Kale oyun içinde köylüler veya AI Komutan tarafından inşa edilir)
            var enemyTC = RTSBuilding.SpawnBuilding(RTSBuildingType.TownCenter, eBase, UnitTeam.Enemy, startBuilt: true);
            enemyTC.displayName = "Şehir Merkezi";
        }

        /// <summary>
        /// Tüm harita modlarında (RiverValley, OpenSteppe, BlackForest, CrossCorners)
        /// binaların üzerine veya çok yakınına denk gelen ağaçları, dekoratif kayaları ve kaynak çakışmalarını temizler.
        /// </summary>
        private void ResolveAllMapOverlaps()
        {
            var allResources = FindObjectsByType<RTSResourceSource>(FindObjectsSortMode.None);

            // 1. Binalar ile Ağaç / Kaynak çakışmalarını denetle
            foreach (var bld in RTSBuilding.AllBuildings)
            {
                if (bld == null) continue;
                Vector3 bPos = bld.transform.position;
                Vector3 fp = bld.GetBuildingFootprint();
                float bRadius = Mathf.Max(fp.x, fp.z) * 0.55f + 2.8f;

                foreach (var res in allResources)
                {
                    if (res == null) continue;
                    float dist = Vector2.Distance(new Vector2(bPos.x, bPos.z), new Vector2(res.transform.position.x, res.transform.position.z));

                    if (res.resourceType == RTSResourceType.Wood)
                    {
                        // Binanın üstüne veya hemen dibine denk gelen ağaçları kaldır
                        if (dist < bRadius + 1.2f)
                        {
                            DestroyImmediate(res.gameObject);
                        }
                    }
                    else
                    {
                        // Eğer bir altın/böğürtlen kaynağı binaya çok yakınsa güvenli açık alana kaydır
                        if (dist < bRadius + 1.5f)
                        {
                            Vector3 pushDir = res.transform.position - bPos;
                            pushDir.y = 0f;
                            if (pushDir.sqrMagnitude < 0.01f) pushDir = Vector3.right;
                            else pushDir.Normalize();

                            Vector3 newPos = bPos + pushDir * (bRadius + 3.0f);
                            newPos.x = Mathf.Clamp(newPos.x, -62f, 62f);
                            newPos.z = Mathf.Clamp(newPos.z, -62f, 62f);
                            newPos.y = res.transform.position.y;
                            res.transform.position = newPos;
                        }
                    }
                }
            }

            // 2. Altın ve Böğürtlen kaynaklarının üstüne denk gelen ağaçları kaldır
            allResources = FindObjectsByType<RTSResourceSource>(FindObjectsSortMode.None);
            foreach (var mineOrBush in allResources)
            {
                if (mineOrBush == null || mineOrBush.resourceType == RTSResourceType.Wood) continue;
                Vector2 mPos = new Vector2(mineOrBush.transform.position.x, mineOrBush.transform.position.z);

                foreach (var tree in allResources)
                {
                    if (tree == null || tree.resourceType != RTSResourceType.Wood) continue;
                    float dist = Vector2.Distance(mPos, new Vector2(tree.transform.position.x, tree.transform.position.z));
                    if (dist < 3.6f)
                    {
                        DestroyImmediate(tree.gameObject);
                    }
                }
            }

            // 3. Binaların içine giren dekoratif çevre kayalarını / kütüklerini temizle
            if (mapEnvironmentRoot != null)
            {
                var discoverables = mapEnvironmentRoot.GetComponentsInChildren<RTSFogDiscoverable>(true);
                foreach (var disc in discoverables)
                {
                    if (disc == null || disc.GetComponent<RTSResourceSource>() != null) continue;
                    string n = disc.gameObject.name.ToLower();
                    if (!n.Contains("rock") && !n.Contains("log") && !n.Contains("trunk")) continue;

                    Vector2 dPos = new Vector2(disc.transform.position.x, disc.transform.position.z);
                    foreach (var bld in RTSBuilding.AllBuildings)
                    {
                        if (bld == null) continue;
                        Vector3 fp = bld.GetBuildingFootprint();
                        float clearRad = Mathf.Max(fp.x, fp.z) * 0.55f + 2.2f;
                        if (Vector2.Distance(dPos, new Vector2(bld.transform.position.x, bld.transform.position.z)) < clearRad)
                        {
                            DestroyImmediate(disc.gameObject);
                            break;
                        }
                    }
                }
            }
        }

        private void SpawnInitialArmies(Transform spawnOrigin)
        {
            Vector3 pBase = spawnOrigin != null ? spawnOrigin.position : CurrentPlayerBasePos;
            Vector3 eBase = CurrentEnemyBasePos;

            // Oyuncu üssünden harita merkezine (0,0,0) doğru yön
            Vector3 pFwd = (Vector3.zero - CurrentPlayerBasePos);
            pFwd.y = 0;
            if (pFwd.sqrMagnitude < 0.01f) pFwd = Vector3.forward;
            else pFwd.Normalize();
            Vector3 pRight = Vector3.Cross(Vector3.up, pFwd).normalized;

            // 1. Oyuncu Başlangıç Kadrosu (Tam Age of Empires Başlangıcı: 3 Köylü + 1 Keşif Atlısı)
            // Şehir merkezinin hemen önünde, merkeze bakacak şekilde konuşlandırılır
            CreateUnitInstance("villager", UnitTeam.Player, CurrentPlayerBasePos + pFwd * 5.5f - pRight * 3.5f + Vector3.up * 0.1f);
            CreateUnitInstance("villager", UnitTeam.Player, CurrentPlayerBasePos + pFwd * 5.5f + pRight * 3.5f + Vector3.up * 0.1f);
            CreateUnitInstance("villager", UnitTeam.Player, CurrentPlayerBasePos + pFwd * 7.0f + Vector3.up * 0.1f);

            // 1 Keşif Süvarisi (Scout Cavalry): Haritayı keşfetmek üzere ön cephede hazır bekler
            CreateUnitInstance("scout", UnitTeam.Player, CurrentPlayerBasePos + pFwd * 10.5f + Vector3.up * 0.1f);

            // Düşman üssünden harita merkezine doğru yön
            Vector3 eFwd = (Vector3.zero - CurrentEnemyBasePos);
            eFwd.y = 0;
            if (eFwd.sqrMagnitude < 0.01f) eFwd = Vector3.back;
            else eFwd.Normalize();
            Vector3 eRight = Vector3.Cross(Vector3.up, eFwd).normalized;

            // 2. Kırmızı Krallık Başlangıç Kadrosu (Mavi Krallık ile %100 Eşit: 3 Köylü + 1 Keşif Atlısı)
            CreateUnitInstance("villager", UnitTeam.Enemy, CurrentEnemyBasePos + eFwd * 5.5f - eRight * 3.5f + Vector3.up * 0.1f);
            CreateUnitInstance("villager", UnitTeam.Enemy, CurrentEnemyBasePos + eFwd * 5.5f + eRight * 3.5f + Vector3.up * 0.1f);
            CreateUnitInstance("villager", UnitTeam.Enemy, CurrentEnemyBasePos + eFwd * 7.0f + Vector3.up * 0.1f);
            CreateUnitInstance("scout", UnitTeam.Enemy, CurrentEnemyBasePos + eFwd * 10.5f + Vector3.up * 0.1f);
        }

        private void CreateUnitInstance(string type, UnitTeam team, Vector3 pos)
        {
            RTSUnit.SpawnUnit(type, pos, team);
        }

        private void BuildCompleteUI(
            GameObject mgrGo,
            RTSGameManager gameMgr,
            AiGatewayClient client,
            out RTSHUDController hud,
            out ApprovalPanelUI approvalUI)
        {
            // Canvas
            var canvasGo = new GameObject("UI_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            Font medievalTitleFont = LoadFontSafely("Cinzel");
            Font medievalBodyFont = LoadFontSafely("Marcellus") ?? LoadFontSafely("Cinzel");
            Font defaultFont = medievalTitleFont ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // --- 1. TOP BAR (Tek Satır Age of Empires Başlık Çubuğu) ---
            var topBar = CreateUIPanel(canvasGo.transform, "TopBar_Resources", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 52), new Color(0.10f, 0.075f, 0.05f, 0.98f));
            
            // Altın süsleme çizgileri (Bottom Gold Trim & Top Accent)
            CreateUIPanel(topBar.transform, "GoldTrimBottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(0, 3.5f), new Color(0.88f, 0.72f, 0.30f, 1f));
            CreateUIPanel(topBar.transform, "GoldTrimTop", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 1.5f), new Color(0.65f, 0.52f, 0.20f, 0.6f));

            Color podBg = new Color(0.065f, 0.048f, 0.032f, 0.96f);
            Color podBorder = new Color(0.78f, 0.62f, 0.25f, 0.65f);
            
            // Sol: 4 Kaynak Podu + 1 Köylü Dağılım Podu (Ahşap & Altın Oyma Çerçeve + Game-Icons Vektör İkonları)
            Texture2D woodIconTex = LoadTextureSafely("Resources/UI/Icons/icon_wood.png");
            Texture2D foodIconTex = LoadTextureSafely("Resources/UI/Icons/icon_food.png");
            Texture2D goldIconTex = LoadTextureSafely("Resources/UI/Icons/icon_gold.png");
            Texture2D shieldIconTex = LoadTextureSafely("Resources/UI/Icons/icon_shield.png");
            Texture2D workerIconTex = LoadTextureSafely("Resources/UI/Icons/icon_repair.png");

            // 1. Odun Podu
            var woodPod = CreateUIPanel(topBar.transform, "WoodPod", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(95, 36), podBg);
            CreateUIPanel(woodPod.transform, "WoodBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, podBorder);
            CreateUIPanel(woodPod.transform, "WoodAccent", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(4f, 0), new Color(0.72f, 0.48f, 0.24f, 0.90f));
            if (woodIconTex != null)
                CreateUIRawImage(woodPod.transform, "WoodIcon", woodIconTex, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(22, 22), Color.white);
            var woodTxt = CreateUIText(woodPod.transform, "WoodText", "450", 15, new Color(0.96f, 0.92f, 0.82f), defaultFont, new Vector2(0, 0.5f), new Vector2(56, 28));
            woodTxt.rectTransform.pivot = new Vector2(0, 0.5f);
            woodTxt.rectTransform.anchoredPosition = new Vector2(36, 0);
            woodTxt.alignment = TextAnchor.MiddleLeft;
            woodTxt.fontStyle = FontStyle.Bold;

            var woodHover = woodPod.AddComponent<RTSUIHoverTooltip>();
            woodHover.SetDynamicTooltip(
                () => "<color=#D4A373>ODUN (KERESTE)</color>",
                () => {
                    int w = (RTSHUDController.Instance != null) ? RTSHUDController.Instance.currentWood : 450;
                    return $"Mevcut: <color=#D4A373><b>{w}</b></color>  •  İnşaat, binalar, surlar ve okçular için temel kaynak.";
                }
            );

            // 2. Gıda Podu
            var foodPod = CreateUIPanel(topBar.transform, "FoodPod", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(112, 0), new Vector2(95, 36), podBg);
            CreateUIPanel(foodPod.transform, "FoodBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, podBorder);
            CreateUIPanel(foodPod.transform, "FoodAccent", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(4f, 0), new Color(0.42f, 0.72f, 0.28f, 0.90f));
            if (foodIconTex != null)
                CreateUIRawImage(foodPod.transform, "FoodIcon", foodIconTex, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(22, 22), Color.white);
            var foodTxt = CreateUIText(foodPod.transform, "FoodText", "200", 15, new Color(0.96f, 0.92f, 0.82f), defaultFont, new Vector2(0, 0.5f), new Vector2(56, 28));
            foodTxt.rectTransform.pivot = new Vector2(0, 0.5f);
            foodTxt.rectTransform.anchoredPosition = new Vector2(36, 0);
            foodTxt.alignment = TextAnchor.MiddleLeft;
            foodTxt.fontStyle = FontStyle.Bold;

            var foodHover = foodPod.AddComponent<RTSUIHoverTooltip>();
            foodHover.SetDynamicTooltip(
                () => "<color=#90BE6D>GIDA (ERZAK)</color>",
                () => {
                    int f = (RTSHUDController.Instance != null) ? RTSHUDController.Instance.currentFood : 200;
                    return $"Mevcut: <color=#90BE6D><b>{f}</b></color>  •  Köylü ve asker üretimi, çağ atlama için temel besin.";
                }
            );

            // 3. Altın Podu
            var goldPod = CreateUIPanel(topBar.transform, "GoldPod", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(214, 0), new Vector2(95, 36), podBg);
            CreateUIPanel(goldPod.transform, "GoldBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, podBorder);
            CreateUIPanel(goldPod.transform, "GoldAccent", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(4f, 0), new Color(0.96f, 0.78f, 0.22f, 0.90f));
            if (goldIconTex != null)
                CreateUIRawImage(goldPod.transform, "GoldIcon", goldIconTex, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(22, 22), Color.white);
            var goldTxt = CreateUIText(goldPod.transform, "GoldText", "150", 15, new Color(1.0f, 0.86f, 0.28f), defaultFont, new Vector2(0, 0.5f), new Vector2(56, 28));
            goldTxt.rectTransform.pivot = new Vector2(0, 0.5f);
            goldTxt.rectTransform.anchoredPosition = new Vector2(36, 0);
            goldTxt.alignment = TextAnchor.MiddleLeft;
            goldTxt.fontStyle = FontStyle.Bold;

            var goldHover = goldPod.AddComponent<RTSUIHoverTooltip>();
            goldHover.SetDynamicTooltip(
                () => "<color=#F9C74F>ALTIN (HAZİNE)</color>",
                () => {
                    int g = (RTSHUDController.Instance != null) ? RTSHUDController.Instance.currentGold : 150;
                    return $"Mevcut: <color=#F9C74F><b>{g}</b></color>  •  Şövalyeler, demirci geliştirmeleri ve çağ madeni.";
                }
            );

            // 4. Nüfus Podu
            var popPod = CreateUIPanel(topBar.transform, "PopPod", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(316, 0), new Vector2(105, 36), podBg);
            CreateUIPanel(popPod.transform, "PopBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, podBorder);
            CreateUIPanel(popPod.transform, "PopAccent", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(4f, 0), new Color(0.35f, 0.65f, 0.95f, 0.90f));
            if (shieldIconTex != null)
                CreateUIRawImage(popPod.transform, "PopIcon", shieldIconTex, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(22, 22), Color.white);
            var armyTxt = CreateUIText(popPod.transform, "ArmyText", "4/10", 14, new Color(0.85f, 0.92f, 1.0f), defaultFont, new Vector2(0, 0.5f), new Vector2(66, 28));
            armyTxt.rectTransform.pivot = new Vector2(0, 0.5f);
            armyTxt.rectTransform.anchoredPosition = new Vector2(36, 0);
            armyTxt.alignment = TextAnchor.MiddleLeft;
            armyTxt.fontStyle = FontStyle.Bold;

            var popHover = popPod.AddComponent<RTSUIHoverTooltip>();
            popHover.SetDynamicTooltip(
                () => "<color=#87CEEB>NÜFUS KAPASİTESİ</color>",
                () => {
                    int p = (RTSHUDController.Instance != null) ? RTSHUDController.Instance.currentPop : 4;
                    int mp = (RTSHUDController.Instance != null) ? RTSHUDController.Instance.currentMaxPop : 10;
                    return $"Mevcut: <color=#87CEEB><b>{p} / {mp}</b></color>  •  Kapasiteyi artırmak için yeni Ahşap Ev inşa edin.";
                }
            );

            // 5. Köylü Faaliyet Durum Podu (Sadeleştirilmiş İkon + Sayı; Detaylar Hover Alt Metninde)
            var actPanel = CreateUIPanel(topBar.transform, "VillagerActivity_Panel", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(428, 0), new Vector2(95, 36), podBg);
            CreateUIPanel(actPanel.transform, "ActBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, podBorder);
            CreateUIPanel(actPanel.transform, "ActAccent", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(4f, 0), new Color(0.65f, 0.45f, 0.85f, 0.90f));
            if (workerIconTex != null)
                CreateUIRawImage(actPanel.transform, "WorkerIcon", workerIconTex, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(22, 22), Color.white);
            var actTxt = CreateUIText(actPanel.transform, "VillagerActivityText", "3", 15, new Color(0.90f, 0.94f, 0.98f), defaultFont, new Vector2(0, 0.5f), new Vector2(56, 28));
            actTxt.rectTransform.pivot = new Vector2(0, 0.5f);
            actTxt.rectTransform.anchoredPosition = new Vector2(36, 0);
            actTxt.alignment = TextAnchor.MiddleLeft;
            actTxt.fontStyle = FontStyle.Bold;

            var actHover = actPanel.AddComponent<RTSUIHoverTooltip>();
            actHover.SetDynamicTooltip(
                () => "<color=#D8B4E2>KÖYLÜ FAALİYET DAĞILIMI</color>",
                () => {
                    var h = RTSHUDController.Instance;
                    int t = (h != null) ? h.villagerTotal : 3;
                    int f = (h != null) ? h.villagerFood : 0;
                    int w = (h != null) ? h.villagerWood : 0;
                    int g = (h != null) ? h.villagerGold : 3;
                    int b = (h != null) ? h.villagerBuild : 0;
                    int idl = (h != null) ? h.villagerIdle : 0;
                    return $"Toplam: <b>{t} Köylü</b>  (🌾 {f}  •  🪵 {w}  •  🪙 {g}  •  🔨 {b}  •  💤 {idl})\n<i>[F3] Tüm Köylüleri Seç   •   [F4] Boştakileri Seç</i>";
                }
            );

            // Orta: Çağ Arması (Kraliyet Kadife Madalyonu & Yan Sancak Kanatları)
            var agePod = CreateUIPanel(topBar.transform, "AgePod", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-75, 0), new Vector2(240, 42), new Color(0.18f, 0.11f, 0.21f, 0.98f));
            CreateUIPanel(agePod.transform, "AgeBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.95f, 0.82f, 0.35f, 0.90f));
            CreateUIPanel(agePod.transform, "AgeInnerRim", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-4, -4), new Color(0.26f, 0.16f, 0.30f, 0.45f));
            var ageTxt = CreateUIText(agePod.transform, "AgeBadgeText", "I. KARANLIK ÇAĞ", 16, new Color(1.0f, 0.88f, 0.35f), defaultFont, new Vector2(0.5f, 0.5f), new Vector2(230, 30));
            ageTxt.fontStyle = FontStyle.Bold;

            var ageHover = agePod.AddComponent<RTSUIHoverTooltip>();
            ageHover.SetDynamicTooltip(
                () => "<color=#FFD166>MEDENİYET ÇAĞI</color>",
                () => $"Mevcut Çağ: <b>{ageTxt.text}</b>\n\nŞehir Merkezinden çağ atlayarak üstün askeri teknolojileri, yeni binaları ve kuşatma silahlarını açabilirsiniz."
            );

            // Heraldic Flanking Pennant Banners (Hanedanlık Yan Sancakları)
            Texture2D bannerAsset = LoadTextureSafely("Resources/Textures/flag-pennant.png")
                                    ?? LoadTextureSafely("Models/CastleKit/Previews/flag-pennant.png")
                                    ?? LoadTextureSafely("Resources/Textures/flag-banner-long.png");

            if (bannerAsset != null)
            {
                CreateUIRawImage(topBar.transform, "HeraldicBanner_Left", bannerAsset, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-215, 0), new Vector2(40, 40), Color.white);
                var rPennant = CreateUIRawImage(topBar.transform, "HeraldicBanner_Right", bannerAsset, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(65, 0), new Vector2(40, 40), Color.white);
                rPennant.rectTransform.localScale = new Vector3(-1, 1, 1);
            }

            // Orta-Sağ: Akın Geri Sayım Rozeti (War Horn Raid Pod)
            var raidPod = CreateUIPanel(topBar.transform, "RaidPod", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(165, 0), new Vector2(205, 38), new Color(0.16f, 0.08f, 0.06f, 0.98f));
            CreateUIPanel(raidPod.transform, "RaidBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.95f, 0.45f, 0.20f, 0.80f));
            var raidTxt = CreateUIText(raidPod.transform, "RaidTimerText", "AKIN: Hazırlanıyor...", 13, new Color(1.0f, 0.85f, 0.35f), defaultFont, new Vector2(0.5f, 0.5f), new Vector2(195, 28));
            raidTxt.fontStyle = FontStyle.Bold;

            var raidHover = raidPod.AddComponent<RTSUIHoverTooltip>();
            raidHover.SetDynamicTooltip(
                () => "<color=#FF6B6B>DÜŞMAN AKINI GERİ SAYIMI</color>",
                () => $"{raidTxt.text}\n\nSüre sıfırlandığında düşman ordusu merkezinize taarruza geçecektir. Savunma hatlarınızı ve ordunuzu hazır tutun!"
            );

            // Sağ: Hızlı Seçim ve Menü Butonları (Tek Hizada Sağdan Sola)
            var menuBtn = CreateUIButton(topBar.transform, "Btn_Menu", "[ESC] Menü", defaultFont, new Vector2(1, 0.5f), new Vector2(105, 36), new Color(0.24f, 0.18f, 0.14f, 0.95f));
            var menuRt = menuBtn.GetComponent<RectTransform>();
            menuRt.pivot = new Vector2(1, 0.5f);
            menuRt.anchoredPosition = new Vector2(-10, 0);

            var menuHover = menuBtn.gameObject.AddComponent<RTSUIHoverTooltip>();
            menuHover.SetTooltip(
                "<color=#F4A261>OYUN MENÜSÜ [ESC]</color>",
                "Oyunu duraklatır; kontroller ve görev rehberini açar, maçı yeniden başlatır veya ana menüye döner."
            );

            // Danışman Butonu (Regal Sapphire & Bright Gold Filigree)
            var btnAiToggle = CreateUIButton(topBar.transform, "Btn_AiToggle", "✦ KRALİYET DANIŞMANI ✦", defaultFont, new Vector2(1, 0.5f), new Vector2(165, 36), new Color(0.14f, 0.26f, 0.48f, 0.98f));
            var aiToggleRt = btnAiToggle.GetComponent<RectTransform>();
            aiToggleRt.pivot = new Vector2(1, 0.5f);
            aiToggleRt.anchoredPosition = new Vector2(-122, 0);
            var aiOutline = btnAiToggle.GetComponent<Outline>();
            if (aiOutline != null) aiOutline.effectColor = new Color(0.98f, 0.85f, 0.35f, 0.95f);

            var aiHover = btnAiToggle.gameObject.AddComponent<RTSUIHoverTooltip>();
            aiHover.SetTooltip(
                "<color=#F1FAEE>KRALİYET DANIŞMANI (YAPAY ZEKA)</color>",
                "Stratejik tavsiyeler, ekonomik planlar ve savunma taktikleri için yapay zeka danışman panelini açar."
            );

            var btnSelMil = CreateUIButton(topBar.transform, "Btn_SelectMilitary", "[F2] Ordu", defaultFont, new Vector2(1, 0.5f), new Vector2(95, 36), new Color(0.16f, 0.32f, 0.52f, 0.95f));
            var milRt = btnSelMil.GetComponent<RectTransform>();
            milRt.pivot = new Vector2(1, 0.5f);
            milRt.anchoredPosition = new Vector2(-294, 0);

            var milHover = btnSelMil.gameObject.AddComponent<RTSUIHoverTooltip>();
            milHover.SetTooltip(
                "<color=#90E0EF>TÜM ASKERİ BİRLİKLER [F2]</color>",
                "Emrinizdeki tüm savaşçı birlikleri (Mızrakçı, Kılıçlı, Okçu, Süvari) topluca seçer."
            );

            var btnSelVil = CreateUIButton(topBar.transform, "Btn_SelectVillagers", "[F3] Köylüler", defaultFont, new Vector2(1, 0.5f), new Vector2(105, 36), new Color(0.14f, 0.38f, 0.22f, 0.95f));
            var vilRt = btnSelVil.GetComponent<RectTransform>();
            vilRt.pivot = new Vector2(1, 0.5f);
            vilRt.anchoredPosition = new Vector2(-396, 0);

            var vilHover = btnSelVil.gameObject.AddComponent<RTSUIHoverTooltip>();
            vilHover.SetTooltip(
                "<color=#A8DADC>TÜM KÖYLÜLER [F3]</color>",
                "Haritadaki tüm işçilerinizi tek tuşla topluca seçer."
            );

            var idleBtn = CreateUIButton(topBar.transform, "IdleVillager_Btn", "[F4] Boşta: 0", defaultFont, new Vector2(1, 0.5f), new Vector2(115, 36), new Color(0.25f, 0.22f, 0.20f, 0.90f));
            var idleRt = idleBtn.GetComponent<RectTransform>();
            idleRt.pivot = new Vector2(1, 0.5f);
            idleRt.anchoredPosition = new Vector2(-508, 0);
            var idleTxt = idleBtn.GetComponentInChildren<Text>();

            var idleHover = idleBtn.gameObject.AddComponent<RTSUIHoverTooltip>();
            idleHover.SetDynamicTooltip(
                () => "<color=#FFD166>BOŞTAKİ KÖYLÜLER [F4]</color>",
                () => $"{idleTxt.text}\n\nŞu anda bir işle görevlendirilmeyen köylüleri sırayla seçer ve ekranda odaklar."
            );

            // --- İNŞAAT & YERLEŞTİRME BİLGİ BANDI (Üst Barın Hemen Altı) ---
            var placeBanner = CreateUIPanel(canvasGo.transform, "Placement_Banner", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -52), new Vector2(780, 46), new Color(0.11f, 0.08f, 0.06f, 0.98f));
            CreateUIPanel(placeBanner.transform, "PlaceBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.85f, 0.70f, 0.28f, 0.65f));
            var placeTitle = CreateUIText(placeBanner.transform, "PlaceTitle", "İNŞAAT MODU PROTOKOLÜ", 15, new Color(1f, 0.88f, 0.35f), defaultFont, new Vector2(0.5f, 0.70f), new Vector2(760, 20));
            placeTitle.fontStyle = FontStyle.Bold;
            var placeDesc = CreateUIText(placeBanner.transform, "PlaceDesc", "Maliyet: 150 Odun  •  [Sol Tık]: Konumlandır  •  [Sağ Tık / ESC]: İptal", 12, new Color(0.92f, 0.94f, 0.98f), defaultFont, new Vector2(0.5f, 0.28f), new Vector2(760, 18));
            placeBanner.SetActive(false);

            // --- 2. BİRLEŞİK ALT KOMUTA KONSOLU (Unified Bottom Deck - Age of Empires) ---
            var bottomDock = CreateUIPanel(canvasGo.transform, "BottomDock_Console", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(0, 195), new Color(0.09f, 0.07f, 0.05f, 0.98f));
            
            // Üst altın süsleme bordürü
            CreateUIPanel(bottomDock.transform, "DockGoldTrim", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 3.5f), new Color(0.85f, 0.70f, 0.28f, 1f));

            // --- BAY 1: MİNİ-HARİTA (Sol Bay) ---
            var minimapBay = CreateUIPanel(bottomDock.transform, "Minimap_Bay", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(215, 185), new Color(0.06f, 0.05f, 0.04f, 0.96f));
            CreateUIPanel(minimapBay.transform, "MapBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.85f, 0.70f, 0.28f, 0.45f));
            CreateUIText(minimapBay.transform, "MapLabel", "MİNİ-HARİTA", 12, new Color(1f, 0.88f, 0.35f), defaultFont, new Vector2(0.5f, 0.93f), new Vector2(190, 16)).fontStyle = FontStyle.Bold;

            var rawImgGo = new GameObject("Minimap_Surface", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            rawImgGo.transform.SetParent(minimapBay.transform, false);
            var rawRt = rawImgGo.GetComponent<RectTransform>();
            rawRt.anchorMin = new Vector2(0.5f, 0.5f);
            rawRt.anchorMax = new Vector2(0.5f, 0.5f);
            rawRt.pivot = new Vector2(0.5f, 0.5f);
            rawRt.anchoredPosition = new Vector2(0, -6f);
            rawRt.sizeDelta = new Vector2(146, 146);

            var rawImage = rawImgGo.GetComponent<RawImage>();
            rawImage.color = Color.white;
            var minimapCtrl = rawImgGo.AddComponent<RTSMinimapController>();
            minimapCtrl.minimapImage = rawImage;
            minimapCtrl.cameraController = Camera.main != null ? Camera.main.GetComponent<RTSCameraController>() : null;
            minimapCtrl.InitMinimapTexture();
            rawImage.texture = minimapCtrl.MinimapTexture;
            minimapCtrl.DrawMinimap();

            // Pusula 4 Yön Belirteçleri
            var nTag = CreateUIText(minimapBay.transform, "NorthTag", "K", 11, new Color(1f, 0.42f, 0.42f), defaultFont, new Vector2(0.5f, 0.83f), new Vector2(30, 14));
            nTag.fontStyle = FontStyle.Bold;
            var sTag = CreateUIText(minimapBay.transform, "SouthTag", "G", 11, new Color(0.42f, 0.85f, 1f), defaultFont, new Vector2(0.5f, 0.05f), new Vector2(30, 14));
            sTag.fontStyle = FontStyle.Bold;
            var wTag = CreateUIText(minimapBay.transform, "WestTag", "B", 11, new Color(0.9f, 0.9f, 0.9f), defaultFont, new Vector2(0.08f, 0.47f), new Vector2(30, 14));
            wTag.fontStyle = FontStyle.Bold;
            var eTag = CreateUIText(minimapBay.transform, "EastTag", "D", 11, new Color(0.9f, 0.9f, 0.9f), defaultFont, new Vector2(0.92f, 0.47f), new Vector2(30, 14));
            eTag.fontStyle = FontStyle.Bold;

            // --- BAY 2: SEÇİLİ VARLIK TEFTİŞ & BİLGİ MASASI (Orta Bay) ---
            var inspBay = CreateUIPanel(bottomDock.transform, "Inspector_Bay", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(235, 0), new Vector2(980, 185), new Color(0.065f, 0.05f, 0.04f, 0.92f));
            CreateUIPanel(inspBay.transform, "InspBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.85f, 0.70f, 0.28f, 0.35f));

            // Sol: Hanedanlık Sınıf Kartı / Portre Kutusu (Heraldic Insignia Card)
            var crestOuter = CreateUIPanel(inspBay.transform, "HeraldicCrest_Box", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(85, 145), new Color(0.85f, 0.70f, 0.28f, 0.90f));
            var crestInner = CreateUIPanel(crestOuter.transform, "CrestInner", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-4, -4), new Color(0.10f, 0.08f, 0.06f, 0.98f));
            
            var badgeHeader = CreateUIText(crestInner.transform, "BadgeHeader", "[ YAPI ]", 9, new Color(0.85f, 0.70f, 0.30f), defaultFont, new Vector2(0.5f, 0.88f), new Vector2(75, 16));
            badgeHeader.fontStyle = FontStyle.Bold;

            var crestTxt = CreateUIText(crestInner.transform, "HeraldicCrestText", "ŞEHİR\nMERKEZİ", 13, new Color(1f, 0.92f, 0.65f), defaultFont, new Vector2(0.5f, 0.48f), new Vector2(75, 70));
            crestTxt.fontStyle = FontStyle.Bold;
            crestTxt.alignment = TextAnchor.MiddleCenter;

            // Sağ: Bilgi & Can & Taktik Bölümü
            var inspTitle = CreateUIText(inspBay.transform, "InspTitle", "İMPARATORLUK KARARGAHI", 18, new Color(1f, 0.88f, 0.35f), defaultFont, new Vector2(0.54f, 0.88f), new Vector2(850, 24));
            inspTitle.fontStyle = FontStyle.Bold;

            var inspSub = CreateUIText(inspBay.transform, "InspSubtitle", "Emir vermek için bir asker, köylü veya binaya tıklayın", 12, new Color(0.78f, 0.85f, 0.92f), defaultFont, new Vector2(0.54f, 0.72f), new Vector2(850, 18));

            // Can Barı Yuvası
            var hpBarRoot = CreateUIPanel(inspBay.transform, "HpBar_Root", new Vector2(0.54f, 0.54f), new Vector2(0.54f, 0.54f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 20), new Color(0.04f, 0.03f, 0.02f, 1f));
            CreateUIPanel(hpBarRoot.transform, "HpBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.85f, 0.70f, 0.28f, 0.45f));
            var hpFillGo = new GameObject("HpFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            hpFillGo.transform.SetParent(hpBarRoot.transform, false);
            var hpFillRt = hpFillGo.GetComponent<RectTransform>();
            hpFillRt.anchorMin = Vector2.zero;
            hpFillRt.anchorMax = Vector2.one;
            hpFillRt.pivot = new Vector2(0f, 0.5f);
            hpFillRt.sizeDelta = Vector2.zero;
            hpFillGo.GetComponent<Image>().color = new Color(0.2f, 0.75f, 0.30f, 0.95f);

            var hpTxt = CreateUIText(hpBarRoot.transform, "HpText", "Can: 100 / 100 (%100)", 11, Color.white, defaultFont, new Vector2(0.5f, 0.5f), new Vector2(580, 16));

            var inspStats = CreateUIText(inspBay.transform, "InspStats", "[Sol Tık]: Seç  •  [Sürükle]: Çoklu Birlik  •  [Sağ Tık]: Hareket & Taarruz", 13, new Color(0.95f, 0.95f, 0.95f), defaultFont, new Vector2(0.54f, 0.34f), new Vector2(850, 20));
            inspStats.fontStyle = FontStyle.Bold;

            var inspExtra = CreateUIText(inspBay.transform, "InspExtra", "Kuzey ve Güney toprakları nehirle ayrılmıştır. Köprü ve sığlıkları kontrol edin!", 11, new Color(0.72f, 0.82f, 0.95f), defaultFont, new Vector2(0.54f, 0.14f), new Vector2(850, 18));
            inspExtra.fontStyle = FontStyle.Italic;

            // --- BAY 3: AKSİYON & EMİR MASASI (Sağ Bay) ---
            var actionBay = CreateUIPanel(bottomDock.transform, "Action_Bay", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(680, 185), new Color(0.065f, 0.05f, 0.04f, 0.92f));
            CreateUIPanel(actionBay.transform, "ActionBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.85f, 0.70f, 0.28f, 0.35f));

            var actionTitle = CreateUIText(actionBay.transform, "ActionTitle", "İMPARATORLUK EMRİ BEKLENİYOR", 13, new Color(1f, 0.88f, 0.35f), defaultFont, new Vector2(0.5f, 0.88f), new Vector2(650, 22));
            actionTitle.fontStyle = FontStyle.Bold;

            var actBtn1 = CreateUIButton(actionBay.transform, "ActBtn1", "Eylem 1", defaultFont, new Vector2(0.10f, 0.40f), new Vector2(120, 68), new Color(0.18f, 0.14f, 0.10f));
            var actBtn2 = CreateUIButton(actionBay.transform, "ActBtn2", "Eylem 2", defaultFont, new Vector2(0.29f, 0.40f), new Vector2(120, 68), new Color(0.18f, 0.14f, 0.10f));
            var actBtn3 = CreateUIButton(actionBay.transform, "ActBtn3", "Eylem 3", defaultFont, new Vector2(0.48f, 0.40f), new Vector2(120, 68), new Color(0.18f, 0.14f, 0.10f));
            var actBtn4 = CreateUIButton(actionBay.transform, "ActBtn4", "Eylem 4", defaultFont, new Vector2(0.67f, 0.40f), new Vector2(120, 68), new Color(0.18f, 0.14f, 0.10f));
            var actBtn5 = CreateUIButton(actionBay.transform, "ActBtn5", "Eylem 5", defaultFont, new Vector2(0.86f, 0.40f), new Vector2(120, 68), new Color(0.18f, 0.14f, 0.10f));

            // Buton Kısayol Rozetleri
            CreateUIText(actBtn1.transform, "Key1", "[Q]", 10, new Color(1f, 0.85f, 0.35f), defaultFont, new Vector2(0.22f, 0.82f), new Vector2(28, 16)).fontStyle = FontStyle.Bold;
            CreateUIText(actBtn2.transform, "Key2", "[W]", 10, new Color(1f, 0.85f, 0.35f), defaultFont, new Vector2(0.22f, 0.82f), new Vector2(28, 16)).fontStyle = FontStyle.Bold;
            CreateUIText(actBtn3.transform, "Key3", "[E]", 10, new Color(1f, 0.85f, 0.35f), defaultFont, new Vector2(0.22f, 0.82f), new Vector2(28, 16)).fontStyle = FontStyle.Bold;
            CreateUIText(actBtn4.transform, "Key4", "[A]", 10, new Color(1f, 0.85f, 0.35f), defaultFont, new Vector2(0.22f, 0.82f), new Vector2(28, 16)).fontStyle = FontStyle.Bold;
            CreateUIText(actBtn5.transform, "Key5", "[S]", 10, new Color(1f, 0.85f, 0.35f), defaultFont, new Vector2(0.22f, 0.82f), new Vector2(28, 16)).fontStyle = FontStyle.Bold;

            // --- 3. AI STRATEGIC COMMANDER ÇEKMECESİ (Açılır-Kapanır Taktik Masası) ---
            var aiBox = CreateUIPanel(canvasGo.transform, "AI_Commander_Box", new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(12, 202), new Vector2(440, 315), new Color(0.09f, 0.07f, 0.05f, 0.98f));
            CreateUIPanel(aiBox.transform, "AiBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.85f, 0.70f, 0.28f, 0.45f));
            
            var aiTitle = CreateUIText(aiBox.transform, "AiTitle", "KRALİYET STRATEJİK DANIŞMANI (AI)", 15, new Color(1f, 0.88f, 0.35f), defaultFont, new Vector2(0.45f, 0.92f), new Vector2(360, 24));
            aiTitle.fontStyle = FontStyle.Bold;

            var btnCloseAi = CreateUIButton(aiBox.transform, "BtnCloseAi", "✕", defaultFont, new Vector2(0.93f, 0.92f), new Vector2(28, 24), new Color(0.4f, 0.2f, 0.2f));

            // 6 Taktiksel Hızlı Doktrin Butonu (2x3 Izgara)
            // Satır 1: Savunma & Tahkimat
            var btnDefend = CreateUIButton(aiBox.transform, "BtnDefend", "Kuzey Kapısını Savun", defaultFont, new Vector2(0.26f, 0.76f), new Vector2(200, 30), new Color(0.55f, 0.18f, 0.18f));
            var btnFortify = CreateUIButton(aiBox.transform, "BtnFortify", "Kule & Sur Tahkimatı", defaultFont, new Vector2(0.74f, 0.76f), new Vector2(200, 30), new Color(0.60f, 0.35f, 0.15f));

            // Satır 2: Ekonomi & Feodal Çağ
            var btnEconomy = CreateUIButton(aiBox.transform, "BtnEconomy", "Ekonomi & Tarla Kur", defaultFont, new Vector2(0.26f, 0.62f), new Vector2(200, 30), new Color(0.18f, 0.45f, 0.25f));
            var btnTech = CreateUIButton(aiBox.transform, "BtnTech", "Feodal Çağ & Demirci", defaultFont, new Vector2(0.74f, 0.62f), new Vector2(200, 30), new Color(0.55f, 0.45f, 0.18f));

            // Satır 3: Karşı Taarruz & Harita Keşfi
            var btnCounter = CreateUIButton(aiBox.transform, "BtnCounter", "Düşmana Karşı Taarruz", defaultFont, new Vector2(0.26f, 0.48f), new Vector2(200, 30), new Color(0.20f, 0.32f, 0.58f));
            var btnScout = CreateUIButton(aiBox.transform, "BtnScout", "Sisli Haritayı Keşfet", defaultFont, new Vector2(0.74f, 0.48f), new Vector2(200, 30), new Color(0.16f, 0.46f, 0.50f));

            // Satır 4: Serbest Taktik ve Soru Girişi
            var chatInput = CreateUIInputField(aiBox.transform, "CustomPromptInput", "Danışmana soru sorun (örn: 'Köprüye okçu gönder', 'Çağ nasıl atlarım?')...", defaultFont, new Vector2(0.39f, 0.30f), new Vector2(315, 32), new Color(0.14f, 0.12f, 0.10f, 1f));
            var btnSendChat = CreateUIButton(aiBox.transform, "BtnSendPrompt", "GÖNDER", defaultFont, new Vector2(0.86f, 0.30f), new Vector2(85, 32), new Color(0.25f, 0.45f, 0.65f));

            // Satır 5: Danışman Durumu ve Tavsiye Mesajı
            var aiStatus = CreateUIText(aiBox.transform, "AiStatus", "Komutan emirlerinizi bekliyor...", 12, new Color(0.88f, 0.90f, 0.95f), defaultFont, new Vector2(0.5f, 0.12f), new Vector2(410, 24));
            aiBox.SetActive(false); // Başlangıçta kapalı, butonla açılır!

            hud = mgrGo.AddComponent<RTSHUDController>();
            hud.canvasRectTransform = canvasGo.GetComponent<RectTransform>();
            hud.woodText = woodTxt;
            hud.foodText = foodTxt;
            hud.goldText = goldTxt;
            hud.armyText = armyTxt;
            hud.ageBadgeText = ageTxt;
            hud.raidTimerBadge = raidPod;
            hud.raidTimerText = raidTxt;
            hud.placementBanner = placeBanner;
            hud.placementTitleText = placeTitle;
            hud.placementDescText = placeDesc;
            hud.aiCommanderPanel = aiBox;
            hud.toggleAiCommanderBtn = btnAiToggle;
            hud.closeAiCommanderBtn = btnCloseAi;
            hud.topBarMenuBtn = menuBtn;
            hud.defendNorthGateBtn = btnDefend;
            hud.fortificationBtn = btnFortify;
            hud.economyPlanBtn = btnEconomy;
            hud.technologyAdvancementBtn = btnTech;
            hud.counterAttackBtn = btnCounter;
            hud.scoutExplorationBtn = btnScout;
            hud.customPromptInput = chatInput;
            hud.sendCustomPromptBtn = btnSendChat;
            hud.aiStatusText = aiStatus;
            hud.selectAllMilitaryBtn = btnSelMil;
            hud.selectAllVillagersBtn = btnSelVil;
            hud.idleVillagerBtn = idleBtn;
            hud.idleVillagerBtnText = idleTxt;
            hud.villagerActivityText = actTxt;
            hud.inspectorPanel = inspBay;
            hud.inspectorHeraldicBadgeText = crestTxt;
            hud.inspectorTitleText = inspTitle;
            hud.inspectorSubtitleText = inspSub;
            hud.inspectorHpText = hpTxt;
            hud.inspectorHpFill = hpFillRt;
            hud.inspectorStatsText = inspStats;
            hud.inspectorExtraText = inspExtra;
            hud.bottomActionPanel = actionBay;
            hud.actionPanelTitle = actionTitle;
            hud.actionBtn1 = actBtn1;
            hud.actionBtn2 = actBtn2;
            hud.actionBtn3 = actBtn3;
            hud.actionBtn4 = actBtn4;
            hud.actionBtn5 = actBtn5;
            hud.actionBtn1Text = actBtn1.GetComponentInChildren<Text>();
            hud.actionBtn2Text = actBtn2.GetComponentInChildren<Text>();
            hud.actionBtn3Text = actBtn3.GetComponentInChildren<Text>();
            hud.actionBtn4Text = actBtn4.GetComponentInChildren<Text>();
            hud.actionBtn5Text = actBtn5.GetComponentInChildren<Text>();
            hud.HideActionPanel();
            hud.gatewayClient = client;
            hud.gameManager = gameMgr;

            // --- 4. APPROVAL PANEL (AI Stratejik Plan Onay Modalı) ---
            var panelRoot = CreateUIPanel(canvasGo.transform, "ApprovalPanel_Root", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(580, 420), new Color(0.12f, 0.15f, 0.2f, 0.97f));
            CreateUIText(panelRoot.transform, "PanelTitle", "KOMUTANIN STRATEJİK EYLEM PLANI", 18, Color.white, defaultFont, new Vector2(0.5f, 0.92f), new Vector2(540, 35));

            var summary = CreateUIText(panelRoot.transform, "SummaryText", "Plan özeti burada gösterilecek...", 14, Color.white, defaultFont, new Vector2(0.5f, 0.78f), new Vector2(520, 50));
            var conf = CreateUIText(panelRoot.transform, "ConfidenceText", "Güven Seviyesi: %85", 14, Color.yellow, defaultFont, new Vector2(0.5f, 0.68f), new Vector2(520, 25));

            var listContainer = CreateUIPanel(panelRoot.transform, "ActionsContainer", new Vector2(0.5f, 0.42f), new Vector2(0.5f, 0.42f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 140), new Color(0.06f, 0.08f, 0.1f, 0.8f));
            var vlg = listContainer.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.spacing = 6;
            vlg.padding = new RectOffset(10, 10, 10, 10);

            var statusTxt = CreateUIText(panelRoot.transform, "StatusText", "Oyuncu onayı bekleniyor...", 13, Color.gray, defaultFont, new Vector2(0.5f, 0.18f), new Vector2(520, 25));

            var btnApprove = CreateUIButton(panelRoot.transform, "BtnApprove", "PLANI ONAYLA", defaultFont, new Vector2(0.28f, 0.08f), new Vector2(230, 42), new Color(0.2f, 0.65f, 0.35f));
            var btnReject = CreateUIButton(panelRoot.transform, "BtnReject", "REDDET", defaultFont, new Vector2(0.72f, 0.08f), new Vector2(230, 42), new Color(0.75f, 0.25f, 0.25f));

            approvalUI = mgrGo.AddComponent<ApprovalPanelUI>();
            approvalUI.Initialize(panelRoot, summary, conf, statusTxt, listContainer.transform, btnApprove, btnReject, client);
            panelRoot.SetActive(false);

            // --- 6. ZAFER VE YENİLGİ MODALI (Game Over Screen) ---
            var modalRoot = CreateUIPanel(canvasGo.transform, "GameOver_Modal", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.04f, 0.05f, 0.08f, 0.94f));

            var modalBox = CreateUIPanel(modalRoot.transform, "Modal_Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 480), new Color(0.1f, 0.13f, 0.18f, 0.98f));

            var titleTxt = CreateUIText(modalBox.transform, "TitleText", "ZAFER!", 26, new Color(1f, 0.85f, 0.2f), defaultFont, new Vector2(0.5f, 0.88f), new Vector2(600, 45));
            titleTxt.fontStyle = FontStyle.Bold;

            var subtitleTxt = CreateUIText(modalBox.transform, "SubtitleText", "Düşman Karargahı tamamen imha edildi.", 14, new Color(0.85f, 0.9f, 0.95f), defaultFont, new Vector2(0.5f, 0.76f), new Vector2(580, 40));

            // İstatistik tablosu kutusu
            var statsBox = CreateUIPanel(modalBox.transform, "Stats_Box", new Vector2(0.5f, 0.44f), new Vector2(0.5f, 0.44f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(580, 200), new Color(0.06f, 0.08f, 0.11f, 0.9f));
            var statsTxt = CreateUIText(statsBox.transform, "StatsText", "Maç İstatistikleri...", 14, Color.white, defaultFont, new Vector2(0.5f, 0.5f), new Vector2(550, 180));
            statsTxt.alignment = TextAnchor.MiddleLeft;

            var mainMenuBtn = CreateUIButton(modalBox.transform, "MainMenuBtn", "ANA MENÜYE DÖN", defaultFont, new Vector2(0.5f, 0.11f), new Vector2(300, 46), new Color(0.24f, 0.38f, 0.65f));

            hud.gameOverModal = modalRoot;
            hud.gameOverTitleText = titleTxt;
            hud.gameOverSubtitleText = subtitleTxt;
            hud.gameOverStatsText = statsTxt;
            hud.gameOverRestartBtn = null;
            hud.gameOverMainMenuBtn = mainMenuBtn;
            modalRoot.SetActive(false);

            // --- 7. BAŞLANGIÇ EKRANI (START SCREEN / MAIN MENU - Kraliyet Sefer Masası) ---
            // Tamamen opak (alpha = 1.0f) arkaplan sayesinde menüde harita değiştirildiğinde arkadaki 3D sahne görünmez ve parlamaz!
            var startModal = CreateUIPanel(canvasGo.transform, "StartScreen_Modal", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.015f, 0.02f, 0.03f, 1.0f));

            // UI Dokuları (Textures - Lion Wood Frame, Emerald Button, Oak Button, Parchment Plate)
            Texture2D frameTex = LoadTextureSafely("Resources/UI/StartMenu/menu_frame.jpg");
            Texture2D btnGreenTex = LoadTextureSafely("Resources/UI/StartMenu/btn_green.png");
            Texture2D btnDarkTex = LoadTextureSafely("Resources/UI/StartMenu/btn_dark.png");
            Texture2D parchTex = LoadTextureSafely("Resources/UI/StartMenu/parchment_plate.jpg");

            // Ana Kart (Center 980 x 620) - Hakiki Ortaçağ Savaş Masası Çerçevesi
            var mainCard = CreateUIPanel(startModal.transform, "Start_MainCard", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980, 620), new Color(0.08f, 0.09f, 0.12f, 0.98f));
            if (frameTex != null)
            {
                var cardImg = mainCard.GetComponent<Image>();
                cardImg.sprite = Sprite.Create(frameTex, new Rect(0, 0, frameTex.width, frameTex.height), new Vector2(0.5f, 0.5f));
                cardImg.color = Color.white;
            }
            var cardShadow = mainCard.AddComponent<Outline>();
            cardShadow.effectColor = new Color(0, 0, 0, 0.92f);
            cardShadow.effectDistance = new Vector2(4f, -4f);

            // --- A. ÜST BAŞLIK ALANI (HEADER SECTION) ---
            var hBadge = CreateUIText(mainCard.transform, "HeaderBadge", "⚜  KRALİYET SEFER MECLİSİ & KOMUTA MERKEZİ  ⚜", 11, new Color(1f, 0.85f, 0.35f), medievalTitleFont, new Vector2(0.5f, 0.935f), new Vector2(860, 20));
            hBadge.fontStyle = FontStyle.Bold;

            var sTitle = CreateUIText(mainCard.transform, "Title", "⚔️  IMPERIUM: MEDIEVAL AGENTS  ⚔️", 28, new Color(1.0f, 0.88f, 0.38f), medievalTitleFont, new Vector2(0.5f, 0.870f), new Vector2(880, 42));
            sTitle.fontStyle = FontStyle.Bold;

            var sSubtitle = CreateUIText(mainCard.transform, "Subtitle", "Age of Empires Tarzı Gerçek Zamanlı Strateji, Taktiksel AI Komutanlığı & Kuşatma Harbi", 12, new Color(0.92f, 0.90f, 0.85f), medievalBodyFont, new Vector2(0.5f, 0.810f), new Vector2(880, 20));
            sSubtitle.fontStyle = FontStyle.Italic;

            // Altın Bölücü Çizgi
            CreateUIPanel(mainCard.transform, "HeaderDivider", new Vector2(0.08f, 0.765f), new Vector2(0.92f, 0.765f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0, 1.5f), new Color(0.85f, 0.70f, 0.28f, 0.55f));

            // --- B. İKİ SÜTUNLU SEFER MASASI (TWO-COLUMN WAR COUNCIL DECK) ---

            // SOL SÜTUN: SAVAŞ SAHASI & COĞRAFYA (430px)
            var leftCol = CreateUIPanel(mainCard.transform, "Left_WarTheaterCol", new Vector2(0.30f, 0.435f), new Vector2(0.30f, 0.435f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(430, 360), new Color(0.04f, 0.05f, 0.07f, 0.75f));
            var leftBorder = leftCol.AddComponent<Outline>();
            leftBorder.effectColor = new Color(0.45f, 0.40f, 0.28f, 0.55f);
            leftBorder.effectDistance = new Vector2(1.2f, -1.2f);

            CreateUIText(leftCol.transform, "ColHeader", "🗺️  SAVAŞ SAHASI SEÇİMİ (WAR THEATER)", 12, new Color(1f, 0.88f, 0.42f), medievalTitleFont, new Vector2(0.5f, 0.93f), new Vector2(400, 22)).fontStyle = FontStyle.Bold;

            // Harita Seçim Butonu (Ahşap & Pirinç Dokulu Buton)
            var mapBtn = CreateUITexturedButton(leftCol.transform, "MapBtn", "◄   HARİTA: NEHİR VADİSİ   ►", btnDarkTex, medievalTitleFont, new Vector2(0.5f, 0.80f), new Vector2(390, 44), new Color(0.12f, 0.22f, 0.38f));
            var mapBtnTxt = mapBtn.GetComponentInChildren<Text>();
            if (mapBtnTxt != null) { mapBtnTxt.fontSize = 15; mapBtnTxt.fontStyle = FontStyle.Bold; mapBtnTxt.color = new Color(1f, 0.92f, 0.65f); }

            // Stratejik Harita Raporu Kutusu (Hakiki Eskitme Parşömen Plakası)
            var briefBox = CreateUIPanel(leftCol.transform, "BriefingBox", new Vector2(0.5f, 0.43f), new Vector2(0.5f, 0.43f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(390, 168), new Color(0.18f, 0.14f, 0.10f, 0.95f));
            if (parchTex != null)
            {
                var pImg = briefBox.GetComponent<Image>();
                pImg.sprite = Sprite.Create(parchTex, new Rect(0, 0, parchTex.width, parchTex.height), new Vector2(0.5f, 0.5f));
                pImg.color = Color.white;
            }
            var briefShadow = briefBox.AddComponent<Outline>();
            briefShadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
            briefShadow.effectDistance = new Vector2(1.5f, -1.5f);

            var briefTitle = CreateUIText(briefBox.transform, "BriefTitle", "📜  COĞRAFYA & STRATEJİK İSTİHBARAT", 12, new Color(0.20f, 0.08f, 0.02f), medievalTitleFont, new Vector2(0.5f, 0.88f), new Vector2(350, 22));
            briefTitle.fontStyle = FontStyle.Bold;

            var mapDesc = CreateUIText(briefBox.transform, "MapDesc", "• 3 Nehir Geçidi (2 Taş Köprü + 1 Sığlık)\n• Nehir fiziksel engeldir\n• Boğaz savunması ve köprü muharebesi.", 12, new Color(0.12f, 0.05f, 0.02f), medievalBodyFont, new Vector2(0.5f, 0.44f), new Vector2(350, 110));
            mapDesc.alignment = TextAnchor.UpperLeft;
            mapDesc.lineSpacing = 1.30f;
            mapDesc.fontStyle = FontStyle.Normal;

            // Zorluk Derecesi Butonu (Ahşap & Pirinç Dokulu Buton)
            var diffBtn = CreateUITexturedButton(leftCol.transform, "DiffBtn", "⚔️ ZORLUK: STANDART (Dengeli AI)", btnDarkTex, medievalTitleFont, new Vector2(0.5f, 0.10f), new Vector2(390, 42), new Color(0.14f, 0.18f, 0.26f));

            // SAĞ SÜTUN: SEFER KOMUTANLIĞI & OYUN MODU (380px)
            var rightCol = CreateUIPanel(mainCard.transform, "Right_CommandCol", new Vector2(0.745f, 0.435f), new Vector2(0.745f, 0.435f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(380, 360), new Color(0.04f, 0.05f, 0.07f, 0.75f));
            var rightBorder = rightCol.AddComponent<Outline>();
            rightBorder.effectColor = new Color(0.55f, 0.45f, 0.25f, 0.55f);
            rightBorder.effectDistance = new Vector2(1.2f, -1.2f);

            CreateUIText(rightCol.transform, "ColHeader2", "⚜️  SEFER KOMUTANLIĞI & OYUN MODU", 12, new Color(1f, 0.88f, 0.42f), medievalTitleFont, new Vector2(0.5f, 0.93f), new Vector2(350, 22)).fontStyle = FontStyle.Bold;

            // 1. TEK OYUNCULU SEFERİ BAŞLAT BUTONU (PRIMARY CTA - Zümrüt & Altın Yaldızlı Mühür Buton)
            var playBtn = CreateUITexturedButton(rightCol.transform, "PlayBtn", "⚔️  SEFERİ BAŞLAT  ⚔️", btnGreenTex, medievalTitleFont, new Vector2(0.5f, 0.76f), new Vector2(346, 62), new Color(0.10f, 0.50f, 0.22f));
            var playTxt = playBtn.GetComponentInChildren<Text>();
            if (playTxt != null) { playTxt.fontSize = 18; playTxt.fontStyle = FontStyle.Bold; playTxt.color = Color.white; }

            var playSub = CreateUIText(playBtn.transform, "PlaySub", "[ Tek Oyunculu Sefer • Savaş Alanına Gir ]", 10, new Color(0.85f, 1.0f, 0.88f), medievalBodyFont, new Vector2(0.5f, 0.18f), new Vector2(320, 16));
            playSub.fontStyle = FontStyle.Italic;

            // 2. ÇOK OYUNCULU SEÇENEĞİ BUTONU (Tıklandığında ayrı Çok Oyunculu Oda Lobisi Modalını açar)
            var modeBtn = CreateUITexturedButton(rightCol.transform, "MultiplayerOpenBtn", "🌐  ÇOK OYUNCULU (2 KİŞİLİK ODA)", btnDarkTex, medievalTitleFont, new Vector2(0.5f, 0.53f), new Vector2(346, 48), new Color(0.14f, 0.28f, 0.48f));
            var modeBtnTxt = modeBtn.GetComponentInChildren<Text>();
            if (modeBtnTxt != null) { modeBtnTxt.fontSize = 14; modeBtnTxt.fontStyle = FontStyle.Bold; modeBtnTxt.color = new Color(1f, 0.92f, 0.65f); }

            // 3. NASIL OYNANIR & KISAYOLLAR
            var guideBtn = CreateUITexturedButton(rightCol.transform, "GuideBtn", "📜  NASIL OYNANIR & KISAYOLLAR", btnDarkTex, medievalTitleFont, new Vector2(0.5f, 0.33f), new Vector2(346, 44), new Color(0.20f, 0.18f, 0.16f));
            var guideBtnTxt = guideBtn.GetComponentInChildren<Text>();
            if (guideBtnTxt != null) guideBtnTxt.fontSize = 13;

            // 4. SES & MÜZİK AYARLARI
            var audioBtn = CreateUITexturedButton(rightCol.transform, "AudioBtn", "⚙️  SES & MÜZİK AYARLARI", btnDarkTex, medievalTitleFont, new Vector2(0.5f, 0.14f), new Vector2(346, 44), new Color(0.16f, 0.22f, 0.32f));
            var audioBtnTxt = audioBtn.GetComponentInChildren<Text>();
            if (audioBtnTxt != null) audioBtnTxt.fontSize = 13;

            // --- C. ALT BİLGİ MÜHRÜ (LORE FOOTER) ---
            CreateUIText(mainCard.transform, "Footer", "⚜️ CSE-377 RTS  •  Kraliyet Strateji Divanı  •  Taktiksel AI Komutanlığı ⚜️", 11, new Color(0.85f, 0.78f, 0.60f, 0.90f), medievalBodyFont, new Vector2(0.5f, 0.035f), new Vector2(860, 20));

            // --- 7.5 ÇOK OYUNCULU PHOTON ODA LİSTESİ MODALI (Yalnızca "ÇOK OYUNCULU" seçeneğine girince açılır) ---
            var mpModal = CreateUIPanel(canvasGo.transform, "MultiplayerLobby_Modal", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.015f, 0.02f, 0.03f, 1.0f));

            var mpBox = CreateUIPanel(mpModal.transform, "Multiplayer_Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 560), new Color(0.08f, 0.06f, 0.05f, 0.98f));
            CreateUIPanel(mpBox.transform, "MpBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.85f, 0.70f, 0.28f, 0.85f));
            CreateUIPanel(mpBox.transform, "MpInner", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-4, -4), new Color(0.09f, 0.11f, 0.16f, 0.96f));
            CreateUIPanel(mpBox.transform, "MpGoldLine", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -68), new Vector2(-48, 2f), new Color(0.85f, 0.70f, 0.28f, 0.6f));

            var mpBadge = CreateUIText(mpBox.transform, "MpBadge", "⚜  PHOTON PUN 2 ÇOK OYUNCULU SEFER DİVANI  ⚜", 11, new Color(1f, 0.85f, 0.35f), medievalTitleFont, new Vector2(0.5f, 0.95f), new Vector2(680, 18));
            mpBadge.fontStyle = FontStyle.Bold;

            var mpTitle = CreateUIText(mpBox.transform, "MpTitle", "🌐  2 KİŞİLİK ODA LİSTESİ & ODA AÇMA EKRANI  🌐", 21, new Color(1.0f, 0.88f, 0.38f), medievalTitleFont, new Vector2(0.5f, 0.895f), new Vector2(680, 30));
            mpTitle.fontStyle = FontStyle.Bold;

            // 1. Satır: Kullanıcı Adı (Komutan Adı) & Oda Açma / Katılma
            var userLbl = CreateUIText(mpBox.transform, "UserLabel", "👤 KOMUTAN ADI (KULLANICI ADI):", 11, new Color(0.95f, 0.88f, 0.65f), medievalTitleFont, new Vector2(0.22f, 0.815f), new Vector2(240, 18));
            userLbl.alignment = TextAnchor.MiddleLeft;
            userLbl.fontStyle = FontStyle.Bold;

            var playerNameInput = CreateUIInputField(mpBox.transform, "PlayerNameInput", "Kullanıcı Adınız...", medievalBodyFont, new Vector2(0.22f, 0.75f), new Vector2(240, 36), new Color(0.04f, 0.05f, 0.08f, 0.98f));
            playerNameInput.text = RTSNetworkSyncManager.Instance != null ? RTSNetworkSyncManager.Instance.localPlayerName : "Komutan_101";
            var pNameBorder = playerNameInput.gameObject.AddComponent<Outline>();
            pNameBorder.effectColor = new Color(0.75f, 0.60f, 0.25f, 0.85f);

            var roomLbl = CreateUIText(mpBox.transform, "RoomLabel", "🏰 ODA ADI / KODU (Yeni Oda Aç veya Kodla Gir):", 11, new Color(0.95f, 0.88f, 0.65f), medievalTitleFont, new Vector2(0.66f, 0.815f), new Vector2(400, 18));
            roomLbl.alignment = TextAnchor.MiddleLeft;
            roomLbl.fontStyle = FontStyle.Bold;

            var roomInput = CreateUIInputField(mpBox.transform, "RoomCodeInput", "Oda Adı (Örn: ODA-101)...", medievalBodyFont, new Vector2(0.51f, 0.75f), new Vector2(170, 36), new Color(0.04f, 0.05f, 0.08f, 0.98f));
            roomInput.text = "ODA-101";
            var roomInputBorder = roomInput.gameObject.AddComponent<Outline>();
            roomInputBorder.effectColor = new Color(0.75f, 0.60f, 0.25f, 0.85f);

            var createRoomBtn = CreateUIButton(mpBox.transform, "CreateRoomBtn", "➕ YENİ ODA AÇ", medievalTitleFont, new Vector2(0.715f, 0.75f), new Vector2(124, 36), new Color(0.14f, 0.46f, 0.26f));
            var createRoomTxt = createRoomBtn.GetComponentInChildren<Text>();
            if (createRoomTxt != null) { createRoomTxt.fontSize = 11; createRoomTxt.fontStyle = FontStyle.Bold; }

            var joinRoomBtn = CreateUIButton(mpBox.transform, "JoinRoomBtn", "➡️ ODAYA KATIL", medievalTitleFont, new Vector2(0.875f, 0.75f), new Vector2(114, 36), new Color(0.16f, 0.34f, 0.62f));
            var joinRoomTxt = joinRoomBtn.GetComponentInChildren<Text>();
            if (joinRoomTxt != null) { joinRoomTxt.fontSize = 11; joinRoomTxt.fontStyle = FontStyle.Bold; }

            // 2. Bölüm: AÇIK ODALAR LİSTESİ (Aktif Lobi Odaları Tarayıcısı)
            var listHdr = CreateUIText(mpBox.transform, "RoomsListHeader", "📋 AÇIK ODALAR LİSTESİ (Katılmak İstediğiniz Odaya Tıklayın):", 12, new Color(1f, 0.88f, 0.42f), medievalTitleFont, new Vector2(0.42f, 0.64f), new Vector2(520, 20));
            listHdr.alignment = TextAnchor.MiddleLeft;
            listHdr.fontStyle = FontStyle.Bold;

            var refreshRoomsBtn = CreateUIButton(mpBox.transform, "RefreshRoomsBtn", "🔄 LİSTEYİ YENİLE", medievalTitleFont, new Vector2(0.865f, 0.64f), new Vector2(130, 28), new Color(0.22f, 0.26f, 0.34f));
            var refreshTxt = refreshRoomsBtn.GetComponentInChildren<Text>();
            if (refreshTxt != null) { refreshTxt.fontSize = 10; refreshTxt.fontStyle = FontStyle.Bold; }

            var roomsListBox = CreateUIPanel(mpBox.transform, "MpRoomsListBox", new Vector2(0.5f, 0.41f), new Vector2(0.5f, 0.41f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(680, 210), new Color(0.04f, 0.05f, 0.08f, 0.96f));
            var roomsListBorder = roomsListBox.AddComponent<Outline>();
            roomsListBorder.effectColor = new Color(0.65f, 0.52f, 0.22f, 0.65f);

            var noRoomsTxt = CreateUIText(roomsListBox.transform, "NoOpenRoomsText", "Şu anda açık bir oda bulunmuyor.\nYukarıdan kullanıcı adınızı girip '➕ YENİ ODA AÇ' butonuna basarak yeni bir oda penceresi açabilirsiniz!", 13, new Color(0.75f, 0.80f, 0.88f), medievalBodyFont, new Vector2(0.5f, 0.5f), new Vector2(640, 70));
            noRoomsTxt.fontStyle = FontStyle.Italic;

            Button[] openRoomBtns = new Button[3];
            Text[] openRoomTxts = new Text[3];
            float[] rowY = new float[] { 0.78f, 0.50f, 0.22f };
            for (int rIdx = 0; rIdx < 3; rIdx++)
            {
                var rBtn = CreateUIButton(roomsListBox.transform, $"OpenRoomRow_{rIdx}", "🏰 ODA-101 • Kurucu: Komutan • [1/2 AÇIK] • KATIL ►", medievalTitleFont, new Vector2(0.5f, rowY[rIdx]), new Vector2(656, 46), new Color(0.14f, 0.22f, 0.36f, 0.95f));
                var rTxt = rBtn.GetComponentInChildren<Text>();
                if (rTxt != null)
                {
                    rTxt.fontSize = 12;
                    rTxt.fontStyle = FontStyle.Bold;
                    rTxt.color = new Color(0.95f, 0.96f, 1f);
                }
                rBtn.gameObject.SetActive(false);
                openRoomBtns[rIdx] = rBtn;
                openRoomTxts[rIdx] = rTxt;
            }

            // Alt Bilgi & Ana Menüye Dön Butonu
            CreateUIText(mpBox.transform, "MpBrowserHint", "💡 '➕ YENİ ODA AÇ' veya listedeki bir odaya tıkladığınızda Oda Penceresi (Oyun Ayarları & Oyunu Başlat) açılır.", 11, new Color(0.85f, 0.82f, 0.70f), medievalBodyFont, new Vector2(0.5f, 0.18f), new Vector2(680, 22)).fontStyle = FontStyle.Italic;

            var mpBackBtn = CreateUITexturedButton(mpBox.transform, "MpBackBtn", "◄  ANA MENÜYE DÖN", btnDarkTex, medievalTitleFont, new Vector2(0.5f, 0.085f), new Vector2(280, 48), new Color(0.22f, 0.16f, 0.14f));

            mpModal.SetActive(false);

            // --- 7.6 AÇILAN ODA PENCERESİ (ACTIVE ROOM MODAL — Oda Açılınca veya Odaya Girince Açılan Yeni Pencere) ---
            // İçerisinde: 1. ve 2. Oyuncu Durumu, Oyun Ayarları (Harita Seçimi + Oyun/Ses Ayarları), Odayı Kapat ve Oyunu Başlat yer alır!
            var activeRoomModal = CreateUIPanel(canvasGo.transform, "ActiveRoom_Modal", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.012f, 0.016f, 0.025f, 1.0f));

            var arBox = CreateUIPanel(activeRoomModal.transform, "ActiveRoom_Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(780, 620), new Color(0.08f, 0.06f, 0.05f, 0.98f));
            CreateUIPanel(arBox.transform, "ArBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.88f, 0.72f, 0.30f, 0.90f));
            CreateUIPanel(arBox.transform, "ArInner", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-4, -4), new Color(0.08f, 0.10f, 0.15f, 0.98f));
            CreateUIPanel(arBox.transform, "ArGoldLine", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -70), new Vector2(-48, 2f), new Color(0.85f, 0.70f, 0.28f, 0.65f));

            var arBadge = CreateUIText(arBox.transform, "ArBadge", "⚜  PHOTON PUN 2 AÇIK ODA & SAVAŞ DİVANI PENCERESİ  ⚜", 11, new Color(1f, 0.85f, 0.35f), medievalTitleFont, new Vector2(0.5f, 0.955f), new Vector2(700, 18));
            arBadge.fontStyle = FontStyle.Bold;

            var arTitle = CreateUIText(arBox.transform, "ArTitle", "🏰  ODA: [ODA-101]  —  SAVAŞ DİVANI & OYUN AYARLARI", 21, new Color(1.0f, 0.88f, 0.38f), medievalTitleFont, new Vector2(0.5f, 0.905f), new Vector2(700, 30));
            arTitle.fontStyle = FontStyle.Bold;

            // 1. Bölüm: ODA OYUNCULARI VE AI KOMUTANLARI (1. Oyuncu Mavi vs 2. Oyuncu Kırmızı)
            var slotsHdr = CreateUIText(arBox.transform, "SlotsHeader", "👥 ODADAKİ KOMUTANLAR VE AI KOMUTAN EŞLEŞMESİ (2 KİŞİLİK ODA):", 12, new Color(1f, 0.88f, 0.42f), medievalTitleFont, new Vector2(0.5f, 0.835f), new Vector2(700, 20));
            slotsHdr.alignment = TextAnchor.MiddleLeft;
            slotsHdr.fontStyle = FontStyle.Bold;

            // 1. Oyuncu Kartı (Mavi Krallık + Mavi AI)
            var blueSlotBox = CreateUIPanel(arBox.transform, "BluePlayerSlot", new Vector2(0.5f, 0.745f), new Vector2(0.5f, 0.745f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 64), new Color(0.08f, 0.20f, 0.38f, 0.95f));
            var blueSlotBorder = blueSlotBox.AddComponent<Outline>();
            blueSlotBorder.effectColor = new Color(0.35f, 0.68f, 1.0f, 0.85f);

            var blueSlotTxt = CreateUIText(blueSlotBox.transform, "BlueSlotText", "🔵 1. OYUNCU: MAVİ KRALLIK + MAVİ AI KOMUTAN\n👑 Komutan: Bekleniyor...   •   ✅ HAZIR", 13, new Color(0.90f, 0.96f, 1.0f), medievalBodyFont, new Vector2(0.5f, 0.5f), new Vector2(670, 52));
            blueSlotTxt.alignment = TextAnchor.MiddleLeft;
            blueSlotTxt.fontStyle = FontStyle.Bold;

            // 2. Oyuncu Kartı (Kırmızı Krallık + Kırmızı AI)
            var redSlotBox = CreateUIPanel(arBox.transform, "RedPlayerSlot", new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 64), new Color(0.28f, 0.10f, 0.12f, 0.95f));
            var redSlotBorder = redSlotBox.AddComponent<Outline>();
            redSlotBorder.effectColor = new Color(0.95f, 0.38f, 0.35f, 0.85f);

            var redSlotTxt = CreateUIText(redSlotBox.transform, "RedSlotText", "🔴 2. OYUNCU: KIRMIZI KRALLIK + KIRMIZI AI KOMUTAN\n⏳ Rakip Oyuncu Bekleniyor... (1/2 — Başka Oyuncu Odaya Katıldığında Dolacak)", 13, new Color(0.95f, 0.82f, 0.55f), medievalBodyFont, new Vector2(0.5f, 0.5f), new Vector2(670, 52));
            redSlotTxt.alignment = TextAnchor.MiddleLeft;
            redSlotTxt.fontStyle = FontStyle.Bold;

            // 2. Bölüm: OYUN AYARLARI (Harita Seçimi, Rol Bilgisi & Ses/Oyun Ayarları)
            var settingsHdr = CreateUIText(arBox.transform, "RoomSettingsHeader", "⚙️ ODA İÇİ OYUN AYARLARI & HARİTA SEÇİMİ:", 12, new Color(1f, 0.88f, 0.42f), medievalTitleFont, new Vector2(0.5f, 0.515f), new Vector2(700, 20));
            settingsHdr.alignment = TextAnchor.MiddleLeft;
            settingsHdr.fontStyle = FontStyle.Bold;

            var mpMapBtn = CreateUITexturedButton(arBox.transform, "MpMapBtn", "◄   HARİTA: NEHİR VADİSİ   ►", btnDarkTex, medievalTitleFont, new Vector2(0.275f, 0.44f), new Vector2(340, 44), new Color(0.14f, 0.22f, 0.36f));
            var mpMapBtnTxt = mpMapBtn.GetComponentInChildren<Text>();
            if (mpMapBtnTxt != null) { mpMapBtnTxt.fontSize = 13; mpMapBtnTxt.fontStyle = FontStyle.Bold; mpMapBtnTxt.color = new Color(1f, 0.92f, 0.65f); }

            var roomSettingsBtn = CreateUITexturedButton(arBox.transform, "RoomSettingsBtn", "⚙️  OYUN & SES AYARLARI", btnDarkTex, medievalTitleFont, new Vector2(0.725f, 0.44f), new Vector2(340, 44), new Color(0.16f, 0.22f, 0.32f));
            var roomSettingsTxt = roomSettingsBtn.GetComponentInChildren<Text>();
            if (roomSettingsTxt != null) { roomSettingsTxt.fontSize = 13; roomSettingsTxt.fontStyle = FontStyle.Bold; roomSettingsTxt.color = new Color(0.95f, 0.92f, 0.82f); }

            var teamBtn = CreateUIButton(arBox.transform, "TeamSelectBtn", "🔵 1. OYUNCU: MAVİ KRALLIK + MAVİ AI", medievalTitleFont, new Vector2(0.5f, 0.34f), new Vector2(700, 34), new Color(0.12f, 0.34f, 0.68f));
            var teamBtnTxt = teamBtn.GetComponentInChildren<Text>();
            if (teamBtnTxt != null) { teamBtnTxt.fontSize = 11; teamBtnTxt.fontStyle = FontStyle.Bold; }

            // 3. Bölüm: Oda Bağlantı Durumu Kutusu
            var statusBox = CreateUIPanel(arBox.transform, "MpStatusBox", new Vector2(0.5f, 0.23f), new Vector2(0.5f, 0.23f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 46), new Color(0.04f, 0.06f, 0.09f, 0.95f));
            var statusBorder = statusBox.AddComponent<Outline>();
            statusBorder.effectColor = new Color(0.65f, 0.55f, 0.25f, 0.6f);

            var roomStatusTxt = CreateUIText(statusBox.transform, "RoomStatusText", "⏳ 1/2 OYUNCU — Rakip oyuncu odaya katıldığında 'OYUNU BAŞLAT' aktif olur.", 12, new Color(0.95f, 0.85f, 0.55f), medievalBodyFont, new Vector2(0.5f, 0.5f), new Vector2(680, 38));
            roomStatusTxt.fontStyle = FontStyle.Bold;

            // 4. Bölüm: Alt Aksiyon Butonları (ODAYI KAPAT & OYUNU BAŞLAT)
            var arCloseRoomBtn = CreateUITexturedButton(arBox.transform, "CloseRoomBtn", "❌  ODAYI KAPAT / AYRIL", btnDarkTex, medievalTitleFont, new Vector2(0.265f, 0.095f), new Vector2(270, 52), new Color(0.42f, 0.14f, 0.14f));
            var arCloseTxt = arCloseRoomBtn.GetComponentInChildren<Text>();
            if (arCloseTxt != null) { arCloseTxt.fontSize = 13; arCloseTxt.fontStyle = FontStyle.Bold; arCloseTxt.color = new Color(1f, 0.82f, 0.80f); }

            var mpStartBtn = CreateUITexturedButton(arBox.transform, "MpStartBtn", "⏳ RAKİP OYUNCU BEKLENİYOR (1/2)", btnGreenTex, medievalTitleFont, new Vector2(0.68f, 0.095f), new Vector2(410, 52), new Color(0.25f, 0.25f, 0.28f));
            var mpStartTxt = mpStartBtn.GetComponentInChildren<Text>();
            if (mpStartTxt != null) { mpStartTxt.fontSize = 14; mpStartTxt.fontStyle = FontStyle.Bold; mpStartTxt.color = new Color(1f, 0.85f, 0.45f); }
            mpStartBtn.interactable = false;

            activeRoomModal.SetActive(false);

            hud.startScreenModal = startModal;
            hud.startScreenPlayBtn = playBtn;
            hud.startScreenMapBtn = mapBtn;
            hud.startScreenMapBtnText = mapBtnTxt;
            hud.startScreenMapDescText = mapDesc;
            hud.startScreenDifficultyBtn = diffBtn;
            hud.startScreenDifficultyBtnText = diffBtn.GetComponentInChildren<Text>();
            hud.startScreenGuideBtn = guideBtn;
            hud.startScreenAudioBtn = audioBtn;
            hud.startScreenAudioBtnText = audioBtn.GetComponentInChildren<Text>();
            hud.startScreenModeBtn = modeBtn;
            hud.startScreenModeBtnText = modeBtnTxt;
            hud.multiplayerLobbyModal = mpModal;
            hud.mpPlayerNameInput = playerNameInput;
            hud.startScreenRoomInput = roomInput;
            hud.mpCreateRoomBtn = createRoomBtn;
            hud.startScreenJoinRoomBtn = joinRoomBtn;
            hud.mpRefreshRoomsBtn = refreshRoomsBtn;
            hud.mpOpenRoomRowBtns = openRoomBtns;
            hud.mpOpenRoomRowTexts = openRoomTxts;
            hud.mpNoOpenRoomsText = noRoomsTxt;
            hud.startScreenTeamBtn = teamBtn;
            hud.startScreenTeamBtnText = teamBtnTxt;
            hud.startScreenRoomStatusText = roomStatusTxt;
            hud.mpLobbyMapBtn = mpMapBtn;
            hud.mpLobbyMapBtnText = mpMapBtnTxt;
            hud.mpLobbyStartMatchBtn = mpStartBtn;
            hud.mpLobbyStartMatchBtnText = mpStartTxt;
            hud.mpLobbyBackBtn = mpBackBtn;
            hud.activeRoomModal = activeRoomModal;
            hud.activeRoomTitleText = arTitle;
            hud.activeRoomBlueSlotText = blueSlotTxt;
            hud.activeRoomRedSlotText = redSlotTxt;
            hud.activeRoomSettingsBtn = roomSettingsBtn;
            hud.activeRoomCloseBtn = arCloseRoomBtn;

            // --- 8. KONTROLLER & REHBER MODALI ---
            var guideModal = CreateUIPanel(canvasGo.transform, "ControlsGuide_Modal", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.02f, 0.03f, 0.05f, 0.92f));

            var guideBox = CreateUIPanel(guideModal.transform, "Guide_Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720, 540), new Color(0.1f, 0.13f, 0.18f, 0.98f));

            var gTitle = CreateUIText(guideBox.transform, "GTitle", "OYUN REHBERİ VE KISAYOLLAR", 22, new Color(1f, 0.85f, 0.25f), medievalTitleFont, new Vector2(0.5f, 0.92f), new Vector2(680, 35));
            gTitle.fontStyle = FontStyle.Bold;

            string guideContent =
                "TEMEL KONTROLLER:\n" +
                "• WASD / Yön Tuşları / Ekran Kenarı: Harita Kamerasını Kaydırma\n" +
                "• Fare Tekeri: Yakınlaşma / Uzaklaşma (RTS Zoom)\n" +
                "• Sol Tık: Birlik / Bina Seçimi | Sürükle: Çoklu Birlik Seçimi\n" +
                "• Sağ Tık: İntikal Emri, Düşmana Hücum veya Kaynak Toplama\n\n" +
                "HIZLI KISAYOLLAR:\n" +
                "• Q Tuşu: Şehir Merkezi seçiliyken hemen Köylü sıraya alır\n" +
                "• 1 .. 5: Kışla seçiliyken Asker eğitir (1: Mızrakçı, 2: Kılıçlı, 3: Okçu, 4: Trebüşe, 5: Süvari)\n" +
                "• F1: Taarruz Modu (Attack-Move) | F2: Tüm Askerleri Seç\n" +
                "• F3: Tüm Köylüleri Seç | F4: Boşta Kalan Köylüleri Bul ve Seç\n\n" +
                "NEHİR & KÖPRÜLER:\n" +
                "• Nehir fiziksel olarak geçilmezdir! Ordularınızı Batı Köprüsü (-28), Doğu Köprüsü (+28) veya Merkez Sığlıktan (0) geçirin.";

            var gTxt = CreateUIText(guideBox.transform, "GText", guideContent, 13, new Color(0.88f, 0.92f, 0.96f), medievalBodyFont, new Vector2(0.5f, 0.52f), new Vector2(660, 360));
            gTxt.alignment = TextAnchor.MiddleLeft;

            var gCloseBtn = CreateUITexturedButton(guideBox.transform, "CloseBtn", "ANLADIM / KAPAT", btnGreenTex, medievalTitleFont, new Vector2(0.5f, 0.08f), new Vector2(260, 44), new Color(0.2f, 0.65f, 0.35f));

            hud.controlsGuideModal = guideModal;
            hud.controlsGuideCloseBtn = gCloseBtn;
            guideModal.SetActive(false);

            // --- 9. OYUN İÇİ DURAKLATMA (PAUSE MENU / ESC MODALI - Gotik Kraliyet Savaş Divanı) ---
            var pauseModal = CreateUIPanel(canvasGo.transform, "PauseMenu_Modal", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.015f, 0.02f, 0.03f, 0.92f));

            Texture2D pauseFrameTex = LoadTextureSafely("Resources/UI/StartMenu/pause_frame.jpg");

            // Pause Kartı (520 x 680) - Gotik Kraliyet Çerçevesi
            var pauseBox = CreateUIPanel(pauseModal.transform, "Pause_Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 680), new Color(0.08f, 0.09f, 0.12f, 0.98f));
            if (pauseFrameTex != null)
            {
                var pImg = pauseBox.GetComponent<Image>();
                pImg.sprite = Sprite.Create(pauseFrameTex, new Rect(0, 0, pauseFrameTex.width, pauseFrameTex.height), new Vector2(0.5f, 0.5f));
                pImg.color = Color.white;
            }
            var pShadow = pauseBox.AddComponent<Outline>();
            pShadow.effectColor = new Color(0, 0, 0, 0.92f);
            pShadow.effectDistance = new Vector2(4f, -4f);

            // Üst Rozet & Başlık
            var pBadge = CreateUIText(pauseBox.transform, "PBadge", "⚜  KRALİYET SAVAŞ DİVANI  ⚜", 11, new Color(1f, 0.85f, 0.35f), medievalTitleFont, new Vector2(0.5f, 0.92f), new Vector2(400, 20));
            pBadge.fontStyle = FontStyle.Bold;

            var pTitle = CreateUIText(pauseBox.transform, "PTitle", "⚔️  OYUN DURAKLATILDI  ⚔️", 22, new Color(1.0f, 0.88f, 0.40f), medievalTitleFont, new Vector2(0.5f, 0.855f), new Vector2(440, 36));
            pTitle.fontStyle = FontStyle.Bold;

            var pSub = CreateUIText(pauseBox.transform, "PSub", "Savaş Alanı ve Birlikler Beklemede", 12, new Color(0.88f, 0.85f, 0.80f), medievalBodyFont, new Vector2(0.5f, 0.805f), new Vector2(400, 20));
            pSub.fontStyle = FontStyle.Italic;

            CreateUIPanel(pauseBox.transform, "PauseDiv", new Vector2(0.12f, 0.765f), new Vector2(0.88f, 0.765f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0, 1.5f), new Color(0.85f, 0.70f, 0.28f, 0.55f));

            // Butonlar (Dokulu Zümrüt & Ahşap/Demir Butonlar)
            // 1. Oyuna Devam Et (Zümrüt Mühür Buton)
            var pResumeBtn = CreateUITexturedButton(pauseBox.transform, "ResumeBtn", "⚔️  OYUNA DEVAM ET (ESC)", btnGreenTex, medievalTitleFont, new Vector2(0.5f, 0.67f), new Vector2(340, 52), new Color(0.10f, 0.50f, 0.22f));
            var resTxt = pResumeBtn.GetComponentInChildren<Text>();
            if (resTxt != null) { resTxt.fontSize = 15; resTxt.fontStyle = FontStyle.Bold; resTxt.color = Color.white; }

            // 2. Ses & Müzik Ayarları (Koyu Meşe/Demir Buton)
            var pSettingsBtn = CreateUITexturedButton(pauseBox.transform, "SettingsBtn", "⚙️  SES & MÜZİK AYARLARI", btnDarkTex, medievalTitleFont, new Vector2(0.5f, 0.535f), new Vector2(340, 46), new Color(0.16f, 0.22f, 0.32f));

            // 3. Maçı Yeniden Başlat (Koyu Meşe/Demir Buton)
            var pRestartBtn = CreateUITexturedButton(pauseBox.transform, "RestartBtn", "🔄  MAÇI YENİDEN BAŞLAT", btnDarkTex, medievalTitleFont, new Vector2(0.5f, 0.420f), new Vector2(340, 46), new Color(0.20f, 0.18f, 0.16f));

            // 4. Kısayollar & Rehber (Koyu Meşe/Demir Buton)
            var pGuideBtn = CreateUITexturedButton(pauseBox.transform, "GuideBtn", "📜  KISAYOLLAR & REHBER", btnDarkTex, medievalTitleFont, new Vector2(0.5f, 0.305f), new Vector2(340, 46), new Color(0.20f, 0.18f, 0.16f));

            // 5. Ana Menüye Dön (Koyu Meşe/Demir Buton)
            var pMainMenuBtn = CreateUITexturedButton(pauseBox.transform, "MainMenuBtn", "🏰  ANA MENÜYE DÖN", btnDarkTex, medievalTitleFont, new Vector2(0.5f, 0.190f), new Vector2(340, 46), new Color(0.22f, 0.14f, 0.14f));
            var mmTxt = pMainMenuBtn.GetComponentInChildren<Text>();
            if (mmTxt != null) { mmTxt.color = new Color(1.0f, 0.75f, 0.70f); }

            // Alt Mühür Bilgisi
            CreateUIText(pauseBox.transform, "PFooter", "⚜️ CSE-377 RTS  •  Kraliyet Komuta Karargahı ⚜️", 10, new Color(0.80f, 0.75f, 0.60f, 0.85f), medievalBodyFont, new Vector2(0.5f, 0.08f), new Vector2(380, 20));

            hud.pauseMenuModal = pauseModal;
            hud.pauseResumeBtn = pResumeBtn;
            hud.pauseSettingsBtn = pSettingsBtn;
            hud.pauseRestartBtn = pRestartBtn;
            hud.pauseGuideBtn = pGuideBtn;
            hud.pauseMainMenuBtn = pMainMenuBtn;
            pauseModal.SetActive(false);

            // --- 9.5 OYUN VE SES AYARLARI MODALI (AUDIO & SETTINGS MODAL) ---
            var settingsModal = CreateUIPanel(canvasGo.transform, "Settings_Modal", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.02f, 0.03f, 0.05f, 0.94f));

            var sBox = CreateUIPanel(settingsModal.transform, "Settings_Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(650, 580), new Color(0.08f, 0.06f, 0.05f, 0.98f));
            CreateUIPanel(sBox.transform, "SBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.85f, 0.70f, 0.28f, 0.85f));
            CreateUIPanel(sBox.transform, "SInner", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-4, -4), new Color(0.12f, 0.09f, 0.06f, 0.90f));
            CreateUIPanel(sBox.transform, "SGoldLine", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(-40, 2f), new Color(0.85f, 0.70f, 0.28f, 0.6f));

            var settTitle = CreateUIText(sBox.transform, "STitle", "OYUN SES VE MÜZİK AYARLARI", 22, new Color(1.0f, 0.88f, 0.35f), medievalTitleFont, new Vector2(0.5f, 0.93f), new Vector2(580, 34));
            settTitle.fontStyle = FontStyle.Bold;

            var settSub = CreateUIText(sBox.transform, "SSub", "Müzik parçaları, ses efektleri ve Türkçe birlik seslendirme düzeyleri.", 12, new Color(0.85f, 0.88f, 0.92f), medievalBodyFont, new Vector2(0.5f, 0.87f), new Vector2(580, 20));
            settSub.fontStyle = FontStyle.Italic;

            Button mDown, mUp, musDown, musUp, sfxDown, sfxUp, vocDown, vocUp;
            Text mTxt, musTxt, sfxTxt, vocTxt;
            RectTransform mFill, musFill, sfxFill, vocFill;

            CreateAudioVolumeRow(sBox.transform, "MasterRow", "🔊  Genel Ses (Master)", 0.77f, medievalTitleFont, out mDown, out mUp, out mTxt, out mFill);
            CreateAudioVolumeRow(sBox.transform, "MusicRow", "🎵  Müzik Sesi (Volume)", 0.65f, medievalTitleFont, out musDown, out musUp, out musTxt, out musFill);

            // 🎼 Çalan Müzik Parçası Seçici Paneli (Soundtrack Selector)
            var trackPanel = CreateUIPanel(sBox.transform, "TrackSelectorPanel", new Vector2(0.5f, 0.53f), new Vector2(0.5f, 0.53f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(580, 52), new Color(0.12f, 0.09f, 0.07f, 0.90f));
            CreateUIPanel(trackPanel.transform, "TrackBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.65f, 0.52f, 0.20f, 0.55f));

            var btnPrev = CreateUIButton(trackPanel.transform, "PrevTrackBtn", "◄ Önceki", medievalTitleFont, new Vector2(0, 0.5f), new Vector2(100, 36), new Color(0.25f, 0.20f, 0.16f));
            var prevRt = btnPrev.GetComponent<RectTransform>();
            prevRt.pivot = new Vector2(0, 0.5f);
            prevRt.anchoredPosition = new Vector2(8, 0);

            var trackTitle = CreateUIText(trackPanel.transform, "TrackTitle", "1. Han ve Köy Ezgisi", 13, new Color(1f, 0.88f, 0.35f), medievalTitleFont, new Vector2(0.5f, 0.68f), new Vector2(350, 20));
            trackTitle.fontStyle = FontStyle.Bold;

            var trackSub = CreateUIText(trackPanel.transform, "TrackSub", "Huzurlu Köy, İnşaat & Ekonomi Ezgisi (The Old Tower Inn)", 10, new Color(0.82f, 0.86f, 0.92f), medievalBodyFont, new Vector2(0.5f, 0.28f), new Vector2(350, 18));
            trackSub.fontStyle = FontStyle.Italic;

            var btnNext = CreateUIButton(trackPanel.transform, "NextTrackBtn", "Sonraki ►", medievalTitleFont, new Vector2(1, 0.5f), new Vector2(100, 36), new Color(0.25f, 0.20f, 0.16f));
            var nextRt = btnNext.GetComponent<RectTransform>();
            nextRt.pivot = new Vector2(1, 0.5f);
            nextRt.anchoredPosition = new Vector2(-8, 0);

            CreateAudioVolumeRow(sBox.transform, "SfxRow", "⚔️  Efekt Sesleri (SFX)", 0.41f, medievalTitleFont, out sfxDown, out sfxUp, out sfxTxt, out sfxFill);
            CreateAudioVolumeRow(sBox.transform, "VoiceRow", "🗣️  Birlik Replikleri (TR)", 0.29f, medievalTitleFont, out vocDown, out vocUp, out vocTxt, out vocFill);

            var muteBtn = CreateUIButton(sBox.transform, "MuteBtn", "🔊 SES DURUMU: AKTİF (AÇIK)", medievalTitleFont, new Vector2(0.5f, 0.18f), new Vector2(380, 36), new Color(0.20f, 0.22f, 0.26f));

            var resetBtn = CreateUIButton(sBox.transform, "ResetBtn", "🔄 Varsayılana Dön", medievalTitleFont, new Vector2(0.31f, 0.07f), new Vector2(210, 40), new Color(0.26f, 0.22f, 0.18f));
            var closeBtn = CreateUIButton(sBox.transform, "CloseBtn", "✔ TAMAM / KAYDET", medievalTitleFont, new Vector2(0.71f, 0.07f), new Vector2(230, 40), new Color(0.18f, 0.58f, 0.28f));

            hud.settingsModal = settingsModal;
            hud.settingsCloseBtn = closeBtn;
            hud.settingsResetBtn = resetBtn;
            hud.settingsMuteToggleBtn = muteBtn;
            hud.settingsMuteToggleText = muteBtn.GetComponentInChildren<Text>();

            hud.masterVolDownBtn = mDown;
            hud.masterVolUpBtn = mUp;
            hud.masterVolText = mTxt;
            hud.masterVolBar = mFill;

            hud.musicVolDownBtn = musDown;
            hud.musicVolUpBtn = musUp;
            hud.musicVolText = musTxt;
            hud.musicVolBar = musFill;

            hud.musicTrackPrevBtn = btnPrev;
            hud.musicTrackNextBtn = btnNext;
            hud.musicTrackTitleText = trackTitle;
            hud.musicTrackSubText = trackSub;

            hud.sfxVolDownBtn = sfxDown;
            hud.sfxVolUpBtn = sfxUp;
            hud.sfxVolText = sfxTxt;
            hud.sfxVolBar = sfxFill;

            hud.voiceVolDownBtn = vocDown;
            hud.voiceVolUpBtn = vocUp;
            hud.voiceVolText = vocTxt;
            hud.voiceVolBar = vocFill;

            settingsModal.SetActive(false);

            // --- 10. GLOBAL ALT METİN (TOOLTIP) SİSTEMİ (Üst Barın Hemen Altında Açılır) ---
            var tooltipGo = CreateUIPanel(canvasGo.transform, "UI_GlobalTooltip", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -56f), new Vector2(300, 64), new Color(0.06f, 0.045f, 0.03f, 0.98f));
            CreateUIPanel(tooltipGo.transform, "TooltipBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.78f, 0.62f, 0.25f, 0.90f));
            CreateUIPanel(tooltipGo.transform, "TooltipInnerBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-4, -4), new Color(0.12f, 0.09f, 0.06f, 0.90f));
            CreateUIPanel(tooltipGo.transform, "TooltipGoldBar", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -22), new Vector2(-16, 1.5f), new Color(0.78f, 0.62f, 0.25f, 0.50f));

            var tTitle = CreateUIText(tooltipGo.transform, "TooltipTitle", "BAŞLIK", 12, new Color(1f, 0.88f, 0.35f), medievalTitleFont, new Vector2(0.5f, 1f), new Vector2(280, 18));
            tTitle.rectTransform.pivot = new Vector2(0.5f, 1f);
            tTitle.rectTransform.anchoredPosition = new Vector2(0, -4);
            tTitle.alignment = TextAnchor.MiddleCenter;
            tTitle.fontStyle = FontStyle.Bold;

            var tBody = CreateUIText(tooltipGo.transform, "TooltipBody", "Açıklama...", 11, new Color(0.92f, 0.94f, 0.98f), medievalBodyFont, new Vector2(0.5f, 1f), new Vector2(280, 38));
            tBody.rectTransform.pivot = new Vector2(0.5f, 1f);
            tBody.rectTransform.anchoredPosition = new Vector2(0, -24);
            tBody.alignment = TextAnchor.MiddleCenter;

            var cg = tooltipGo.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            hud.globalTooltipBox = tooltipGo;
            hud.globalTooltipTitle = tTitle;
            hud.globalTooltipBody = tBody;
            hud.globalTooltipRt = tooltipGo.GetComponent<RectTransform>();
            tooltipGo.SetActive(false);

            // Başlangıçta ana menü açık başlar
            startModal.SetActive(true);
        }

        private static GameObject CreateUIPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = color;
            return go;
        }

        private static Text CreateUIText(Transform parent, string name, string text, int fontSize, Color color, Font font, Vector2 anchorNormalized, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorNormalized;
            rt.anchorMax = anchorNormalized;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;

            var t = go.GetComponent<Text>();
            t.text = text;
            t.font = font;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;

            // Yalnızca açık/parlak renkli metinler için siyah dış hat (outline) ekle;
            // Koyu parşömen metinlerinde harfleri lekeleyip okunmaz kılmaması için kontur ekleme!
            if ((color.r + color.g + color.b) >= 0.85f)
            {
                var outline = go.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                outline.effectDistance = new Vector2(1.2f, -1.2f);
            }
            return t;
        }

        private static RawImage CreateUIRawImage(Transform parent, string name, Texture2D texture, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var raw = go.GetComponent<RawImage>();
            raw.texture = texture;
            raw.color = color;
            raw.raycastTarget = false;
            return raw;
        }

        private static Font LoadFontSafely(string fontName)
        {
            try
            {
                var f = Resources.Load<Font>("Fonts/" + fontName);
                if (f != null) return f;
            }
            catch {}
            try
            {
                var f = Resources.Load<Font>(fontName);
                if (f != null) return f;
            }
            catch {}
            try
            {
                var osFont = Font.CreateDynamicFontFromOSFont(new string[] { fontName, "Palatino Linotype", "Georgia", "Times New Roman" }, 14);
                if (osFont != null) return osFont;
            }
            catch {}
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static Texture2D LoadTextureSafely(string relativePath)
        {
            try
            {
                if (relativePath.StartsWith("Resources/"))
                {
                    string resPath = relativePath.Substring("Resources/".Length);
                    if (resPath.EndsWith(".png")) resPath = resPath.Substring(0, resPath.Length - 4);
                    else if (resPath.EndsWith(".jpg")) resPath = resPath.Substring(0, resPath.Length - 4);
                    else if (resPath.EndsWith(".jpeg")) resPath = resPath.Substring(0, resPath.Length - 5);
                    var resTex = Resources.Load<Texture2D>(resPath);
                    if (resTex != null) return resTex;
                }
                string fullPath = System.IO.Path.Combine(Application.dataPath, relativePath);
                if (System.IO.File.Exists(fullPath))
                {
                    byte[] bytes = System.IO.File.ReadAllBytes(fullPath);
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(bytes)) return tex;
                }
            }
            catch {}
            return null;
        }

        private static Button CreateUITexturedButton(Transform parent, string name, string label, Texture2D tex, Font font, Vector2 anchorNormalized, Vector2 size, Color fallbackColor)
        {
            var btnGo = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(parent, false);
            var rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = anchorNormalized;
            rt.anchorMax = anchorNormalized;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;

            var img = btnGo.GetComponent<Image>();
            if (tex != null)
            {
                img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                img.color = Color.white;
            }
            else
            {
                img.color = fallbackColor;
            }

            var btn = btnGo.GetComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.15f, 0.95f, 1f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            txtGo.transform.SetParent(btnGo.transform, false);
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.sizeDelta = Vector2.zero;

            var txt = txtGo.GetComponent<Text>();
            txt.text = label;
            txt.font = font;
            txt.fontSize = 14;
            txt.fontStyle = FontStyle.Bold;
            txt.color = new Color(1f, 0.95f, 0.82f);
            txt.alignment = TextAnchor.MiddleCenter;
            txt.raycastTarget = false;

            var outline = txtGo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            return btn;
        }

        private static Button CreateUIButton(Transform parent, string name, string label, Font font, Vector2 anchorNormalized, Vector2 size, Color color)
        {
            var btnGo = CreateUIPanel(parent, name, anchorNormalized, anchorNormalized, new Vector2(0.5f, 0.5f), Vector2.zero, size, color);
            var btn = btnGo.AddComponent<Button>();

            var border = btnGo.AddComponent<Outline>();
            border.effectColor = new Color(0.85f, 0.70f, 0.28f, 0.60f);
            border.effectDistance = new Vector2(1.2f, -1.2f);

            var colors = btn.colors;
            colors.normalColor = color;
            colors.highlightedColor = new Color(
                Mathf.Min(1f, color.r * 1.30f + 0.12f),
                Mathf.Min(1f, color.g * 1.30f + 0.12f),
                Mathf.Min(1f, color.b * 1.30f + 0.12f),
                1f);
            colors.pressedColor = new Color(color.r * 0.70f, color.g * 0.70f, color.b * 0.70f, 1f);
            colors.selectedColor = color;
            colors.fadeDuration = 0.10f;
            btn.colors = colors;

            var txt = CreateUIText(btnGo.transform, "Label", label, 14, Color.white, font, new Vector2(0.5f, 0.5f), size);
            txt.raycastTarget = false;

            return btn;
        }

        private static InputField CreateUIInputField(Transform parent, string name, string placeholderText, Font font, Vector2 anchorNormalized, Vector2 size, Color bgColor)
        {
            var inputGo = CreateUIPanel(parent, name, anchorNormalized, anchorNormalized, new Vector2(0.5f, 0.5f), Vector2.zero, size, bgColor);
            var inputField = inputGo.AddComponent<InputField>();

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGo.transform.SetParent(inputGo.transform, false);
            var rtText = textGo.GetComponent<RectTransform>();
            rtText.anchorMin = Vector2.zero;
            rtText.anchorMax = Vector2.one;
            rtText.offsetMin = new Vector2(8, 2);
            rtText.offsetMax = new Vector2(-8, -2);
            var txt = textGo.GetComponent<Text>();
            txt.font = font;
            txt.fontSize = 12;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleLeft;

            var placeGo = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            placeGo.transform.SetParent(inputGo.transform, false);
            var rtPlace = placeGo.GetComponent<RectTransform>();
            rtPlace.anchorMin = Vector2.zero;
            rtPlace.anchorMax = Vector2.one;
            rtPlace.offsetMin = new Vector2(8, 2);
            rtPlace.offsetMax = new Vector2(-8, -2);
            var placeTxt = placeGo.GetComponent<Text>();
            placeTxt.font = font;
            placeTxt.fontSize = 12;
            placeTxt.fontStyle = FontStyle.Italic;
            placeTxt.color = new Color(0.7f, 0.7f, 0.7f, 0.6f);
            placeTxt.text = placeholderText;
            placeTxt.alignment = TextAnchor.MiddleLeft;

            inputField.textComponent = txt;
            inputField.placeholder = placeTxt;
            return inputField;
        }

        private static void CreateAudioVolumeRow(
            Transform parent, 
            string rowName, 
            string labelText, 
            float normalizedY, 
            Font font,
            out Button downBtn, 
            out Button upBtn, 
            out Text valText, 
            out RectTransform fillRt)
        {
            var rowPanel = CreateUIPanel(parent, rowName, new Vector2(0.5f, normalizedY), new Vector2(0.5f, normalizedY), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(580, 48), new Color(0.12f, 0.09f, 0.07f, 0.85f));
            CreateUIPanel(rowPanel.transform, "RowBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.55f, 0.42f, 0.22f, 0.45f));

            var lbl = CreateUIText(rowPanel.transform, "Label", labelText, 14, new Color(0.95f, 0.90f, 0.80f), font, new Vector2(0, 0.5f), new Vector2(210, 36));
            lbl.rectTransform.pivot = new Vector2(0, 0.5f);
            lbl.rectTransform.anchoredPosition = new Vector2(16, 0);
            lbl.alignment = TextAnchor.MiddleLeft;
            lbl.fontStyle = FontStyle.Bold;

            downBtn = CreateUIButton(rowPanel.transform, "DownBtn", "−", font, new Vector2(0, 0.5f), new Vector2(34, 32), new Color(0.26f, 0.20f, 0.16f));
            var dRt = downBtn.GetComponent<RectTransform>();
            dRt.pivot = new Vector2(0, 0.5f);
            dRt.anchoredPosition = new Vector2(236, 0);
            var dTxt = downBtn.GetComponentInChildren<Text>();
            if (dTxt != null) { dTxt.fontSize = 18; dTxt.fontStyle = FontStyle.Bold; }

            var trackGo = CreateUIPanel(rowPanel.transform, "Track", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(276, 0), new Vector2(184, 18), new Color(0.04f, 0.03f, 0.02f, 0.95f));
            CreateUIPanel(trackGo.transform, "TrackBorder", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.65f, 0.52f, 0.20f, 0.50f));

            var fillGo = CreateUIPanel(trackGo.transform, "Fill", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(150, 14), new Color(0.92f, 0.74f, 0.25f, 0.95f));
            fillRt = fillGo.GetComponent<RectTransform>();

            upBtn = CreateUIButton(rowPanel.transform, "UpBtn", "+", font, new Vector2(0, 0.5f), new Vector2(34, 32), new Color(0.26f, 0.20f, 0.16f));
            var uRt = upBtn.GetComponent<RectTransform>();
            uRt.pivot = new Vector2(0, 0.5f);
            uRt.anchoredPosition = new Vector2(466, 0);
            var uTxt = upBtn.GetComponentInChildren<Text>();
            if (uTxt != null) { uTxt.fontSize = 18; uTxt.fontStyle = FontStyle.Bold; }

            valText = CreateUIText(rowPanel.transform, "ValText", "% 85", 13, new Color(1f, 0.92f, 0.60f), font, new Vector2(0, 0.5f), new Vector2(65, 32));
            valText.rectTransform.pivot = new Vector2(0, 0.5f);
            valText.rectTransform.anchoredPosition = new Vector2(506, 0);
            valText.alignment = TextAnchor.MiddleCenter;
            valText.fontStyle = FontStyle.Bold;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(target, value);
            }
        }
    }
}
