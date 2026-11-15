using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace CSE377.Gameplay
{
    /// <summary>
    /// Klasik Age of Empires tarzı dinamik Mini-Harita (Radar).
    /// 1. 128x128 dinamik piksel dokusu ile savaş alanındaki dost/düşman birimleri ve binaları çizer.
    /// 2. Savaş Sisi (Fog-of-War) ile tam entegredir; keşfedilmemiş alanlar karanlık, görüş alanı aydınlık görünür.
    /// 3. Ana kameranın baktığı görüş alanını (Camera Viewport Box) beyaz çerçeveyle çizer.
    /// 4. Mini-haritaya sol tıklayarak veya sürükleyerek kamerayı haritanın istenilen yerine ışınlar.
    /// </summary>
    public class RTSMinimapController : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public static RTSMinimapController Instance { get; private set; }

        [Header("Harita Boyutları")]
        public float mapSize = 160f; // 160x160 metre
        public int textureSize = 128;
        public float updateInterval = 0.05f; // Saniyede ~20 kez yenileme (Akıcı ve hafif)

        [Header("Görsel Renkler")]
        public Color32 fogUnexploredColor = new Color32(0, 0, 0, 255);      // Sisli bilinmeyen alan (%100 Tamamen siyah)
        public Color32 fogExploredColor = new Color32(34, 52, 30, 255);     // Keşfedilip uzaklaşılan gri/silik zemin (Düşman görünmez)
        public Color32 fogVisibleColor = new Color32(68, 126, 56, 255);     // Canlı görüş altındaki yeşil çimen
        public Color32 playerUnitColor = new Color32(50, 180, 255, 255);   // Mavi / Camgöbeği dost birimler
        public Color32 enemyUnitColor = new Color32(245, 45, 45, 255);     // Kırmızı düşman birimleri
        public Color32 townCenterColor = new Color32(165, 105, 55, 255);   // Şehir Merkezi (Kahverengi)
        public Color32 barracksColor = new Color32(40, 110, 210, 255);     // Kışla (Mavi)
        public Color32 towerColor = new Color32(60, 170, 180, 255);        // Savunma Kulesi (Turkuaz)
        public Color32 goldNodeColor = new Color32(255, 215, 30, 255);     // Altın Madeni (Altın sarısı)
        public Color32 wallColor = new Color32(160, 165, 175, 255);        // Sur ve Kapı (Taş grisi)
        public Color32 riverColor = new Color32(28, 92, 160, 255);        // Nehir Su Mavisi
        public Color32 bridgeColor = new Color32(145, 120, 85, 255);       // Ahşap/Taş Köprü Rengi
        public Color32 cameraBoxColor = new Color32(255, 255, 255, 255);   // Kamera Görüş Kutusu (Beyaz)

        [Header("Bağlantılar")]
        public RawImage minimapImage;
        public RTSCameraController cameraController;

        private Texture2D minimapTexture;
        private Color32[] pixelBuffer;
        private RectTransform rectTransform;
        private float nextUpdateTime = 0f;

        public Texture2D MinimapTexture => minimapTexture;

        public class MinimapPing
        {
            public Vector2 worldPos;
            public Color32 color;
            public float expireTime;
            public float startTime;
        }

        private readonly List<MinimapPing> activePings = new List<MinimapPing>();
        public static Vector3 LastAlertWorldPosition = Vector3.zero;
        public static float LastAlertTime = -999f;

        public void AddPing(Vector3 worldPos, Color32 color, float duration = 4.5f)
        {
            activePings.Add(new MinimapPing
            {
                worldPos = new Vector2(worldPos.x, worldPos.z),
                color = color,
                expireTime = Time.time + duration,
                startTime = Time.time
            });
            LastAlertWorldPosition = worldPos;
            LastAlertTime = Time.time;
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            rectTransform = GetComponent<RectTransform>();
            if (cameraController == null)
            {
                cameraController = FindFirstObjectByType<RTSCameraController>();
            }

            if (minimapImage == null)
            {
                minimapImage = GetComponent<RawImage>();
            }

            InitMinimapTexture();
        }

        private void Start()
        {
            if (minimapImage == null)
            {
                minimapImage = GetComponent<RawImage>();
            }

            if (minimapImage != null)
            {
                minimapImage.texture = minimapTexture;
                minimapImage.color = Color.white;
            }

            // İlk kareyi hemen çiz
            DrawMinimap();
        }

        public void InitMinimapTexture()
        {
            if (minimapTexture == null)
            {
                minimapTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
                minimapTexture.filterMode = FilterMode.Point;
                minimapTexture.wrapMode = TextureWrapMode.Clamp;
                minimapTexture.name = "RTS_Minimap_DynamicTex";

                int totalPixels = textureSize * textureSize;
                pixelBuffer = new Color32[totalPixels];

                for (int i = 0; i < totalPixels; i++)
                {
                    pixelBuffer[i] = fogVisibleColor;
                }

                minimapTexture.SetPixels32(pixelBuffer);
                minimapTexture.Apply(false);
            }

            if (minimapImage != null)
            {
                minimapImage.texture = minimapTexture;
                minimapImage.color = Color.white;
            }
        }

        private void Update()
        {
            if (Time.time < nextUpdateTime) return;
            nextUpdateTime = Time.time + updateInterval;

            DrawMinimap();
        }

        public void DrawMinimap()
        {
            if (minimapTexture == null)
            {
                InitMinimapTexture();
            }

            if (minimapImage != null && minimapImage.texture != minimapTexture)
            {
                minimapImage.texture = minimapTexture;
                minimapImage.color = Color.white;
            }

            // 1. Zemin ve Savaş Sisi Katmanını Çiz
            DrawFogAndTerrainBackground();

            // 2. Doğal Nehir ve Stratejik Köprüler (Yalnızca nehirli haritalarda çizilir)
            if (RTSAutoBootstrap.HasRiver)
            {
                DrawRiverAndBridgesOnMinimap();
            }

            var fow = RTSFogOfWarManager.Instance;

            // 4. Doğal Kaynaklar (Altın Madenleri)
            // YALNIZCA keşfedilmiş alanlardaki kaynaklar mini-haritada çizilir!
            foreach (var res in RTSResourceSource.AllSources)
            {
                if (res != null && res.resourceType == RTSResourceType.Gold)
                {
                    Vector3 pos = res.transform.position;
                    if (fow == null || fow.GetCellStateAtWorldPos(pos) != FogCellState.Unexplored)
                    {
                        DrawWorldBlock(new Vector2(pos.x, pos.z), goldNodeColor, 3);
                    }
                }
            }

            // 5. Binalar (Şehir Merkezi, Kale, Kışla, Kule, Demirci, Ev, Tarla)
            // Dost binalar her zaman, rakip binaları yalnızca keşfedildiyse çizilir
            UnitTeam myTeam = RTSGameManager.LocalPlayerTeam;
            foreach (var b in RTSBuilding.AllBuildings)
            {
                if (b != null && b.currentHp > 0)
                {
                    Vector3 bPos = b.transform.position;
                    if (b.team == myTeam || fow == null || fow.GetCellStateAtWorldPos(bPos) != FogCellState.Unexplored)
                    {
                        Color32 bCol = buildingColorByType(b);
                        int bSize = (b.buildingType == RTSBuildingType.TownCenter || b.buildingType == RTSBuildingType.Castle || b.buildingType == RTSBuildingType.SiegeWorkshop) ? 5 :
                                    (b.buildingType == RTSBuildingType.House || b.buildingType == RTSBuildingType.Farm) ? 3 : 4;
                        DrawWorldBlock(new Vector2(bPos.x, bPos.z), bCol, bSize);
                    }
                }
            }

            // 6. Birlikler (Mızrakçı, Okçu, Köylü, Süvariler)
            foreach (var u in RTSUnit.AllUnits)
            {
                if (u != null && u.currentHp > 0)
                {
                    Color32 unitDotColor = (u.team == UnitTeam.Player) ? playerUnitColor : enemyUnitColor;
                    if (u.team == myTeam)
                    {
                        // Dost birimler her zaman görünür
                        DrawWorldDot(new Vector2(u.transform.position.x, u.transform.position.z), unitDotColor, 1);
                    }
                    else
                    {
                        // Rakip birimler yalnızca görüş alanındaysa çizilir (Hile Koruması)
                        bool visible = (RTSFogOfWarManager.Instance != null)
                            ? RTSFogOfWarManager.Instance.IsUnitVisible(u)
                            : true;

                        if (visible)
                        {
                            DrawWorldDot(new Vector2(u.transform.position.x, u.transform.position.z), unitDotColor, 1);
                        }
                    }
                }
            }

            // 6.5. Aktif Saldırı & Alarm Dalgaları (Minimap Alert Ripple Rings)
            for (int i = activePings.Count - 1; i >= 0; i--)
            {
                var ping = activePings[i];
                if (Time.time > ping.expireTime)
                {
                    activePings.RemoveAt(i);
                    continue;
                }

                float life = Time.time - ping.startTime;
                int pingRadius = Mathf.RoundToInt((life % 1.0f) * 8f) + 2;
                DrawWorldRing(ping.worldPos, ping.color, pingRadius);
                DrawWorldDot(ping.worldPos, new Color32(255, 255, 255, 255), 1);
            }

            // 7. Kamera Görüş Çerçevesi (Beyaz Dikdörtgen)
            DrawCameraFrustum();

            // 8. Mini-harita Dış Çerçevesi (1 Piksellik zarif sınır)
            DrawMinimapBorder();

            // 9. Dokuyu GPU'ya aktar
            minimapTexture.SetPixels32(pixelBuffer);
            minimapTexture.Apply(false);
        }

        private Color32 buildingColorByType(RTSBuilding b)
        {
            if (b.team == UnitTeam.Enemy) return enemyUnitColor;

            switch (b.buildingType)
            {
                case RTSBuildingType.TownCenter: return townCenterColor;
                case RTSBuildingType.Castle: return new Color32(230, 185, 45, 255); // İhtişamlı Kale Altını
                case RTSBuildingType.DefenseTower: return towerColor;
                case RTSBuildingType.Barracks: return barracksColor;
                case RTSBuildingType.Blacksmith: return new Color32(185, 120, 50, 255);
                case RTSBuildingType.House: return new Color32(190, 150, 90, 255); // Ahşap Ev rengi
                case RTSBuildingType.Farm: return new Color32(140, 190, 55, 255); // Buğday Tarlası Yeşili
                case RTSBuildingType.StoneWall: return wallColor;
                case RTSBuildingType.CityGate: return wallColor;
                case RTSBuildingType.ArcheryRange: return new Color32(75, 160, 85, 255); // Orman Okçu Yeşili
                case RTSBuildingType.SiegeWorkshop: return new Color32(195, 95, 45, 255); // Bronz / Tuğla
                case RTSBuildingType.Market: return new Color32(235, 185, 40, 255); // Pazar Sarısı
                default: return barracksColor;
            }
        }

        private void DrawRiverAndBridgesOnMinimap()
        {
            var fow = RTSFogOfWarManager.Instance;

            // Nehir Z = 0 ekseninde, genişliği ~11m (Z: -5.5 .. +5.5), uzunluğu X: -80 .. +80
            for (int py = 0; py < textureSize; py++)
            {
                float worldZ = ((float)py / textureSize - 0.5f) * mapSize;
                if (worldZ < -5.5f || worldZ > 5.5f) continue;

                for (int px = 0; px < textureSize; px++)
                {
                    float worldX = ((float)px / textureSize - 0.5f) * mapSize;

                    // Yalnızca keşfedilmiş hücrelerde nehir görünür
                    if (fow != null && fow.GetCellStateAtWorldPos(new Vector3(worldX, 0, worldZ)) == FogCellState.Unexplored)
                    {
                        continue;
                    }

                    int idx = py * textureSize + px;

                    // Köprü ve Sığlık Kontrolü:
                    // Batı Köprüsü: X = -28 (+- 3.2m)
                    // Doğu Köprüsü: X = +28 (+- 3.2m)
                    // Merkez Sığlık: X = 0 (+- 2.8m)
                    bool isWestBridge = Mathf.Abs(worldX - (-28f)) <= 3.2f;
                    bool isEastBridge = Mathf.Abs(worldX - 28f) <= 3.2f;
                    bool isShallows = Mathf.Abs(worldX - 0f) <= 2.8f;

                    if (isWestBridge || isEastBridge || isShallows)
                    {
                        pixelBuffer[idx] = bridgeColor;
                    }
                    else
                    {
                        pixelBuffer[idx] = riverColor;
                    }
                }
            }
        }

        private void DrawFogAndTerrainBackground()
        {
            var fow = RTSFogOfWarManager.Instance;

            for (int py = 0; py < textureSize; py++)
            {
                float worldZ = ((float)py / textureSize - 0.5f) * mapSize;

                for (int px = 0; px < textureSize; px++)
                {
                    float worldX = ((float)px / textureSize - 0.5f) * mapSize;
                    int idx = py * textureSize + px;

                    if (fow != null)
                    {
                        var state = fow.GetCellStateAtWorldPos(new Vector3(worldX, 0, worldZ));
                        switch (state)
                        {
                            case FogCellState.Unexplored:
                                pixelBuffer[idx] = fogUnexploredColor;
                                break;
                            case FogCellState.Explored:
                                pixelBuffer[idx] = fogExploredColor;
                                break;
                            case FogCellState.Visible:
                            default:
                                pixelBuffer[idx] = fogVisibleColor;
                                break;
                        }
                    }
                    else
                    {
                        pixelBuffer[idx] = fogVisibleColor;
                    }
                }
            }
        }

        private void DrawWorldDot(Vector2 worldPos, Color32 color, int radius)
        {
            Vector2Int pixel = WorldToPixel(worldPos);

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int px = pixel.x + dx;
                    int py = pixel.y + dy;

                    if (px >= 0 && px < textureSize && py >= 0 && py < textureSize)
                    {
                        pixelBuffer[py * textureSize + px] = color;
                    }
                }
            }
        }

        private void DrawWorldRing(Vector2 worldPos, Color32 color, int radius)
        {
            Vector2Int center = WorldToPixel(worldPos);
            int r2 = radius * radius;
            int innerR2 = Mathf.Max(0, radius - 1) * Mathf.Max(0, radius - 1);

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int dist2 = dx * dx + dy * dy;
                    if (dist2 <= r2 && dist2 >= innerR2)
                    {
                        SetPixelSafe(center.x + dx, center.y + dy, color);
                    }
                }
            }
        }

        private void DrawWorldBlock(Vector2 worldPos, Color32 color, int halfSize)
        {
            Vector2Int pixel = WorldToPixel(worldPos);

            for (int dx = -halfSize; dx <= halfSize; dx++)
            {
                for (int dy = -halfSize; dy <= halfSize; dy++)
                {
                    int px = pixel.x + dx;
                    int py = pixel.y + dy;

                    if (px >= 0 && px < textureSize && py >= 0 && py < textureSize)
                    {
                        // Kenarlık efekti
                        if (dx == -halfSize || dx == halfSize || dy == -halfSize || dy == halfSize)
                        {
                            pixelBuffer[py * textureSize + px] = new Color32(230, 240, 255, 255);
                        }
                        else
                        {
                            pixelBuffer[py * textureSize + px] = color;
                        }
                    }
                }
            }
        }

        private void DrawWorldLine(Vector2 worldStart, Vector2 worldEnd, Color32 color, int thickness = 1)
        {
            Vector2Int p0 = WorldToPixel(worldStart);
            Vector2Int p1 = WorldToPixel(worldEnd);

            int dx = Mathf.Abs(p1.x - p0.x);
            int dy = Mathf.Abs(p1.y - p0.y);
            int sx = p0.x < p1.x ? 1 : -1;
            int sy = p0.y < p1.y ? 1 : -1;
            int err = dx - dy;

            int x = p0.x;
            int y = p0.y;

            while (true)
            {
                for (int tx = -thickness / 2; tx <= thickness / 2; tx++)
                {
                    for (int ty = -thickness / 2; ty <= thickness / 2; ty++)
                    {
                        int px = x + tx;
                        int py = y + ty;
                        if (px >= 0 && px < textureSize && py >= 0 && py < textureSize)
                        {
                            pixelBuffer[py * textureSize + px] = color;
                        }
                    }
                }

                if (x == p1.x && y == p1.y) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }
            }
        }

        private void DrawCameraFrustum()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 camPos = cam.transform.position;
            float pitchRad = Mathf.Max(20f, cam.transform.eulerAngles.x) * Mathf.Deg2Rad;
            float groundZ = camPos.z + (camPos.y / Mathf.Tan(pitchRad));
            Vector2 camCenter = new Vector2(camPos.x, groundZ);

            Vector2Int centerPixel = WorldToPixel(camCenter);
            int halfW = Mathf.Clamp(Mathf.RoundToInt(camPos.y * 0.45f), 5, 22);
            int halfH = Mathf.Clamp(Mathf.RoundToInt(camPos.y * 0.32f), 4, 18);

            int minX = Mathf.Clamp(centerPixel.x - halfW, 0, textureSize - 1);
            int maxX = Mathf.Clamp(centerPixel.x + halfW, 0, textureSize - 1);
            int minY = Mathf.Clamp(centerPixel.y - halfH, 0, textureSize - 1);
            int maxY = Mathf.Clamp(centerPixel.y + halfH, 0, textureSize - 1);

            Color32 bColor = cameraBoxColor;

            // 2 piksel kalınlığında beyaz çerçeve
            for (int x = minX; x <= maxX; x++)
            {
                SetPixelSafe(x, minY, bColor);
                SetPixelSafe(x, minY + 1, bColor);
                SetPixelSafe(x, maxY, bColor);
                SetPixelSafe(x, maxY - 1, bColor);
            }
            for (int y = minY; y <= maxY; y++)
            {
                SetPixelSafe(minX, y, bColor);
                SetPixelSafe(minX + 1, y, bColor);
                SetPixelSafe(maxX, y, bColor);
                SetPixelSafe(maxX - 1, y, bColor);
            }
        }

        private void DrawMinimapBorder()
        {
            Color32 borderColor = new Color32(40, 50, 65, 255);
            for (int i = 0; i < textureSize; i++)
            {
                pixelBuffer[i] = borderColor; // Alt
                pixelBuffer[(textureSize - 1) * textureSize + i] = borderColor; // Üst
                pixelBuffer[i * textureSize] = borderColor; // Sol
                pixelBuffer[i * textureSize + (textureSize - 1)] = borderColor; // Sağ
            }
        }

        private void SetPixelSafe(int x, int y, Color32 color)
        {
            if (x >= 0 && x < textureSize && y >= 0 && y < textureSize)
            {
                pixelBuffer[y * textureSize + x] = color;
            }
        }

        private Vector2Int WorldToPixel(Vector2 worldPos)
        {
            float u = Mathf.Clamp01((worldPos.x + mapSize * 0.5f) / mapSize);
            float v = Mathf.Clamp01((worldPos.y + mapSize * 0.5f) / mapSize);

            int px = Mathf.Clamp(Mathf.FloorToInt(u * textureSize), 0, textureSize - 1);
            int py = Mathf.Clamp(Mathf.FloorToInt(v * textureSize), 0, textureSize - 1);
            return new Vector2Int(px, py);
        }

        #region Fare Etkileşimi (Sol Tık Kamera Odaklama, Sağ Tık Hareket/Saldırı)

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                IssueMinimapOrder(eventData);
            }
            else
            {
                PanCameraToMinimapPoint(eventData);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                PanCameraToMinimapPoint(eventData);
            }
        }

        private void IssueMinimapOrder(PointerEventData eventData)
        {
            if (rectTransform == null) rectTransform = GetComponent<RectTransform>();

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                Rect rect = rectTransform.rect;
                float u = Mathf.Clamp01((localPoint.x - rect.xMin) / rect.width);
                float v = Mathf.Clamp01((localPoint.y - rect.yMin) / rect.height);

                float worldX = (u - 0.5f) * mapSize;
                float worldZ = (v - 0.5f) * mapSize;
                Vector3 targetWorldPos = new Vector3(worldX, 0, worldZ);

                var selMgr = RTSSelectionManager.Instance;
                if (selMgr != null && selMgr.selectedUnits.Count > 0)
                {
                    var fow = RTSFogOfWarManager.Instance;
                    UnitTeam myTeam = RTSGameManager.LocalPlayerTeam;
                    bool hasVillager = selMgr.selectedUnits.Exists(u => u != null && u.GetComponent<RTSVillager>() != null);

                    // 1. Köylü Görevlendirmesi: Hasarlı Dost Bina Onarımı veya Kaynak Toplama
                    if (hasVillager)
                    {
                        // 1a. Hasarlı dost bina tamiratı
                        RTSBuilding damagedBuilding = null;
                        float closestDamagedDist = 7.5f;
                        foreach (var b in RTSBuilding.AllBuildings)
                        {
                            if (b != null && b.team == myTeam && b.isConstructed && b.currentHp < b.maxHp)
                            {
                                float d = Vector3.Distance(b.transform.position, targetWorldPos);
                                if (d < closestDamagedDist)
                                {
                                    damagedBuilding = b;
                                    closestDamagedDist = d;
                                }
                            }
                        }

                        if (damagedBuilding != null)
                        {
                            RTSNetworkSyncManager.Instance?.BroadcastVillagerOrder(selMgr.selectedUnits, damagedBuilding.transform.position, "REPAIR");
                            foreach (var unitItem in selMgr.selectedUnits)
                            {
                                if (unitItem != null)
                                {
                                    var vilComp = unitItem.GetComponent<RTSVillager>();
                                    if (vilComp != null) vilComp.AssignRepairTask(damagedBuilding);
                                }
                            }
                            selMgr.SpawnClickFeedback(damagedBuilding.transform.position, new Color(0.2f, 0.8f, 1f));
                            RTSAudioManager.Instance?.PlayVoice("villager_build", force: true);
                            RTSAudioManager.Instance?.PlaySound(RTSSoundType.HammerBuild);
                            RTSHUDController.Instance?.SetAiStatus($"[TAMİRAT] \"{damagedBuilding.displayName} onarımı için intikal ediyorum!\"", new Color(0.2f, 0.9f, 1f));
                            return;
                        }

                        // 1b. Kaynak toplama
                        RTSResourceSource targetResource = null;
                        float closestResDist = 6.0f;
                        foreach (var res in RTSResourceSource.AllSources)
                        {
                            if (res != null && res.remainingAmount > 0)
                            {
                                if (fow == null || fow.GetCellStateAtWorldPos(res.transform.position) != FogCellState.Unexplored)
                                {
                                    float d = Vector3.Distance(res.transform.position, targetWorldPos);
                                    if (d < closestResDist)
                                    {
                                        targetResource = res;
                                        closestResDist = d;
                                    }
                                }
                            }
                        }

                        if (targetResource != null)
                        {
                            RTSNetworkSyncManager.Instance?.BroadcastVillagerOrder(selMgr.selectedUnits, targetResource.transform.position, "HARVEST");
                            foreach (var unitItem in selMgr.selectedUnits)
                            {
                                if (unitItem != null)
                                {
                                    var vilComp = unitItem.GetComponent<RTSVillager>();
                                    if (vilComp != null) vilComp.AssignHarvestTask(targetResource);
                                }
                            }
                            selMgr.SpawnClickFeedback(targetResource.transform.position, Color.yellow);
                            RTSAudioManager.Instance?.PlayVoice("villager_farm", force: true);
                            return;
                        }
                    }

                    // 2. Rakip Birimi Taarruzu
                    RTSUnit targetEnemy = null;
                    float minUnitDist = 6f;

                    foreach (var enemyUnit in RTSUnit.AllUnits)
                    {
                        if (enemyUnit != null && enemyUnit.team != myTeam && enemyUnit.currentHp > 0)
                        {
                            bool isVis = (fow == null) || fow.IsUnitVisible(enemyUnit);
                            if (isVis)
                            {
                                float d = Vector3.Distance(enemyUnit.transform.position, targetWorldPos);
                                if (d < minUnitDist)
                                {
                                    targetEnemy = enemyUnit;
                                    minUnitDist = d;
                                }
                            }
                        }
                    }

                    if (targetEnemy != null)
                    {
                        RTSNetworkSyncManager.Instance?.BroadcastAttackOrder(selMgr.selectedUnits, targetEnemy.transform.position, false);
                        foreach (var myUnit in selMgr.selectedUnits)
                        {
                            if (myUnit != null) myUnit.AttackTarget(targetEnemy);
                        }
                        selMgr.SpawnClickFeedback(targetEnemy.transform.position, Color.red);
                        RTSAudioManager.Instance?.PlaySound(RTSSoundType.WarHorn);
                        RTSAudioManager.Instance?.PlaySound(RTSSoundType.AttackOrder);
                        RTSAudioManager.Instance?.NotifyCombat();

                        var lead = selMgr.selectedUnits.Find(u => u != null && u.unitType != "villager") ?? selMgr.selectedUnits.Find(u => u != null);
                        if (lead != null) RTSAudioManager.Instance?.PlayUnitVoice(lead.unitType, "attack", force: true);
                        return;
                    }

                    // 3. Rakip Binası Kuşatması
                    RTSBuilding targetEnemyBuilding = null;
                    float minBldDist = 8f;

                    foreach (var b in RTSBuilding.AllBuildings)
                    {
                        if (b != null && b.team != myTeam && b.currentHp > 0)
                        {
                            bool isVis = (fow == null) || fow.IsBuildingVisible(b);
                            if (isVis)
                            {
                                float d = Vector3.Distance(b.transform.position, targetWorldPos);
                                if (d < minBldDist)
                                {
                                    targetEnemyBuilding = b;
                                    minBldDist = d;
                                }
                            }
                        }
                    }

                    if (targetEnemyBuilding != null)
                    {
                        RTSNetworkSyncManager.Instance?.BroadcastAttackOrder(selMgr.selectedUnits, targetEnemyBuilding.transform.position, true);
                        foreach (var myUnit in selMgr.selectedUnits)
                        {
                            if (myUnit != null && myUnit.team == myTeam) myUnit.AttackBuilding(targetEnemyBuilding);
                        }
                        selMgr.SpawnClickFeedback(targetEnemyBuilding.transform.position, Color.red);
                        RTSAudioManager.Instance?.PlaySound(RTSSoundType.WarHorn);
                        RTSAudioManager.Instance?.PlaySound(RTSSoundType.AttackOrder);
                        RTSAudioManager.Instance?.NotifyCombat();

                        var lead = selMgr.selectedUnits.Find(u => u != null && u.unitType == "trebuchet") 
                                ?? selMgr.selectedUnits.Find(u => u != null && u.unitType != "villager") 
                                ?? selMgr.selectedUnits.Find(u => u != null);
                        if (lead != null) RTSAudioManager.Instance?.PlayUnitVoice(lead.unitType, "attack", force: true);
                        return;
                    }

                    // 4. Hedefe Formasyonlu İntikal Emri
                    selMgr.DistributeMoveOrder(targetWorldPos);
                    selMgr.SpawnClickFeedback(targetWorldPos, Color.green);
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.MoveOrder);
                }
            }
        }

        private void PanCameraToMinimapPoint(PointerEventData eventData)
        {
            if (rectTransform == null) rectTransform = GetComponent<RectTransform>();

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                Rect rect = rectTransform.rect;

                float u = Mathf.Clamp01((localPoint.x - rect.xMin) / rect.width);
                float v = Mathf.Clamp01((localPoint.y - rect.yMin) / rect.height);

                float worldX = (u - 0.5f) * mapSize;
                float worldZ = (v - 0.5f) * mapSize;

                if (cameraController != null)
                {
                    cameraController.FocusOnWorldPosition(new Vector3(worldX, 0, worldZ));
                }

                // Tıklama sonrası kameranın yeni konumunu mini-haritada anında güncelle
                DrawMinimap();
            }
        }

        #endregion
    }
}
