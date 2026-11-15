using System.Collections.Generic;
using UnityEngine;

namespace CSE377.Gameplay
{
    public enum FogCellState
    {
        Unexplored, // Siyah keşif sisi (%85 karanlık)
        Explored,   // Gri savaş sisi (%45 sis - arazi görünür, düşman görünmez)
        Visible     // Tam aydınlık (Şu anda görüş alanında)
    }

    /// <summary>
    /// Gerçek RTS Savaş Sisi (Fog-of-War) ve Görüş Alanı Yöneticisi.
    /// 1. Oyuncu birimleri ve binalarının etrafında dairesel görüş alanı (Sight Radius) hesaplar.
    /// 2. Görüş alanı dışındaki düşman birimlerini 3D sahnede görünmez kılar.
    /// 3. Haritada Age of Empires tarzı dinamik sis ızgarası (Fog Grid) oluşturur.
    /// 4. AiSafeSnapshot için hilesiz düşman görünürlük filtresi sağlar (Anti-Cheat).
    /// </summary>
    public class RTSFogOfWarManager : MonoBehaviour
    {
        public static RTSFogOfWarManager Instance { get; private set; }

        [Header("Harita ve Sis Izgarası Ayarları")]
        public int gridResolution = 64; // 64x64 hücre (2.5m yüksek çözünürlük, pürüzsüz dairesel açılma)
        public float mapSize = 160f;    // 160x160 metre
        public float updateInterval = 0.10f; // Saniyede 10 kez akıcı yenileme

        [Header("Görsel Sis Renkleri")]
        public Color unexploredColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);     // %100 Tamamen siyah keşif sisi (Keşfedilmemiş)
        public Color exploredColor = new Color(0.05f, 0.06f, 0.08f, 0.56f);   // Keşfedilip uzaklaşılan alan: Gri silik savaş sisi (Düşman görünmez)
        public Color visibleColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);        // Tam aydınlık ve şeffaf (Aktif görüş alanı)

        private FogCellState[,] cellStates;
        private Texture2D fogTexture;
        private Color32[] fogPixels;
        private Renderer fogPlaneRenderer;
        private GameObject fogGridRoot;
        private float nextUpdateTime = 0f;
        private float nextSpottedAlarmTime = 0f;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void Start()
        {
            BuildFogGrid();
            UpdateFogGridAndEnemies();
        }

        private void BuildFogGrid()
        {
            fogGridRoot = new GameObject("FogOfWar_Grid");
            fogGridRoot.transform.position = Vector3.zero;

            cellStates = new FogCellState[gridResolution, gridResolution];

            // 1. Tek ve akıcı Atmosferik Savaş Sisi Düzlemi (Bilinear GPU enterpolasyonu ile pürüzsüz sis)
            var fogPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            fogPlane.name = "FogOfWar_AtmosphericPlane";
            fogPlane.transform.SetParent(fogGridRoot.transform);
            fogPlane.transform.position = new Vector3(0, 0.28f, 0);
            fogPlane.transform.localScale = new Vector3(mapSize / 10f, 1f, mapSize / 10f); // 160x160 metre
            Destroy(fogPlane.GetComponent<Collider>());

            fogTexture = new Texture2D(gridResolution, gridResolution, TextureFormat.RGBA32, false);
            fogTexture.filterMode = FilterMode.Bilinear; // Yumuşak, yuvarlak ve organik dairesel görüş sağlar
            fogTexture.wrapMode = TextureWrapMode.Clamp;
            fogPixels = new Color32[gridResolution * gridResolution];

            Color32 unexpColor32 = unexploredColor;
            for (int i = 0; i < fogPixels.Length; i++)
            {
                fogPixels[i] = unexpColor32;
            }
            fogTexture.SetPixels32(fogPixels);
            fogTexture.Apply(false);

            var ren = fogPlane.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.mainTexture = fogTexture;
            mat.renderQueue = 3100;
            ren.material = mat;
            ren.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ren.receiveShadows = false;
            fogPlaneRenderer = ren;

            for (int x = 0; x < gridResolution; x++)
            {
                for (int z = 0; z < gridResolution; z++)
                {
                    cellStates[x, z] = FogCellState.Unexplored;
                }
            }

            Debug.Log($"[RTSFogOfWarManager] {gridResolution}x{gridResolution} Yumuşak Atmosferik Sis Kuruldu!");
        }

        /// <summary>
        /// Harita değiştiğinde veya yeni bir maç başladığında tüm savaş sisini baştan sıfırlar.
        /// Eski haritadaki üs ve birliklerin keşif kalıntılarını tamamen temizler.
        /// </summary>
        public void ResetFog()
        {
            if (cellStates == null || fogPixels == null) return;

            Color32 unexpColor32 = unexploredColor;
            for (int x = 0; x < gridResolution; x++)
            {
                for (int z = 0; z < gridResolution; z++)
                {
                    cellStates[x, z] = FogCellState.Unexplored;
                }
            }

            for (int i = 0; i < fogPixels.Length; i++)
            {
                fogPixels[i] = unexpColor32;
            }

            if (fogTexture != null)
            {
                fogTexture.SetPixels32(fogPixels);
                fogTexture.Apply(false);
            }

            // Yeni haritadaki güncel birliklerin görüş alanını sıfırdan hesapla
            UpdateFogGridAndEnemies();
        }

        private void Update()
        {
            if (Time.time < nextUpdateTime) return;
            nextUpdateTime = Time.time + updateInterval;

            UpdateFogGridAndEnemies();
        }

        private void UpdateFogGridAndEnemies()
        {
            // 1. Bilinear Sis Dokusunu ve Hücre Durumlarını Güncelle
            if (cellStates != null && fogPixels != null)
            {
                float cellSize = mapSize / gridResolution;
                float startOffset = -mapSize * 0.5f + cellSize * 0.5f;

                Color32 unexpColor32 = unexploredColor;
                Color32 expColor32 = exploredColor;
                Color32 visColor32 = visibleColor;

                for (int z = 0; z < gridResolution; z++)
                {
                    for (int x = 0; x < gridResolution; x++)
                    {
                        Vector3 cellCenter = new Vector3(startOffset + x * cellSize, 0, startOffset + z * cellSize);
                        // Harita sonu en dış çerçeve (x == 0, x == 63, z == 0, z == 63 -> ±78.75m) her zaman simsiyah kalır
                        bool isMapEdgeAbyss = (x == 0 || x == gridResolution - 1 || z == 0 || z == gridResolution - 1);
                        bool isCurrentlyVisible = !isMapEdgeAbyss && IsPositionInPlayerVision(cellCenter);
                        // Unity varsayılan Plane UV eksenleri (U: Doğu'dan Batı'ya, V: Kuzey'den Güney'e)
                        // ters eşleştiği için hem X (sütun) hem Z (satır) piksel indeksleri ters çevrilir:
                        int texX = (gridResolution - 1) - x;
                        int texZ = (gridResolution - 1) - z;
                        int pIdx = texZ * gridResolution + texX;

                        if (isMapEdgeAbyss)
                        {
                            cellStates[x, z] = FogCellState.Unexplored;
                            fogPixels[pIdx] = unexpColor32;
                        }
                        else if (isCurrentlyVisible)
                        {
                            cellStates[x, z] = FogCellState.Visible;
                            fogPixels[pIdx] = visColor32;
                        }
                        else
                        {
                            if (cellStates[x, z] == FogCellState.Visible || cellStates[x, z] == FogCellState.Explored)
                            {
                                cellStates[x, z] = FogCellState.Explored;
                                fogPixels[pIdx] = expColor32;
                            }
                            else
                            {
                                cellStates[x, z] = FogCellState.Unexplored;
                                fogPixels[pIdx] = unexpColor32;
                            }
                        }
                    }
                }

                if (fogTexture != null)
                {
                    fogTexture.SetPixels32(fogPixels);
                    fogTexture.Apply(false);
                }
            }

            // 2. Birimlerin (Dost ve Rakip) görünürlüğünü güncelle
            UnitTeam localTeam = RTSGameManager.LocalPlayerTeam;
            foreach (var unit in RTSUnit.AllUnits)
            {
                if (unit == null) continue;

                if (unit.team == localTeam)
                {
                    if (!unit.isFogVisible)
                    {
                        unit.SetFogOfWarVisibility(true);
                    }
                }
                else
                {
                    bool isVisible = IsUnitVisible(unit);
                    if (!unit.isFogVisible && isVisible)
                    {
                        // Rakip birimi sis arkasından ilk defa oyuncunun görüş alanına girdi!
                        if (Time.time >= nextSpottedAlarmTime)
                        {
                            nextSpottedAlarmTime = Time.time + 12f;
                            RTSHUDController.Instance?.SetAiStatus("⚠️ GÖZCÜ RAPORU: Düşman birliği tespit edildi!", Color.yellow);
                            RTSAudioManager.Instance?.PlaySound(RTSSoundType.AlarmHorn);
                        }
                    }
                    unit.SetFogOfWarVisibility(isVisible);
                }
            }

            // 3. Doğal Kaynaklar (Altın Madenleri ve Ağaçlar)
            // Keşfedilmemiş alandaki kaynaklar gizlenir; keşfedilip uzaklaşıldığında (Explored) gri/silik görünür, aktif görüşte (Visible) parlak görünür
            foreach (var res in RTSResourceSource.AllSources)
            {
                if (res != null)
                {
                    var state = GetCellStateAtWorldPos(res.transform.position);
                    res.SetFogVisibility(state != FogCellState.Unexplored);
                    if (res.isDiscovered)
                    {
                        res.SetShroudDimmed(state != FogCellState.Visible);
                    }
                }
            }

            // 4. Statik Yapılar ve Düşman Binaları
            // Sis altındayken gizlenir, keşfedildiğinde açılır; aktif görüş dışındayken (Explored) gri/silik görünür
            foreach (var disc in RTSFogDiscoverable.AllDiscoverables)
            {
                if (disc != null)
                {
                    var state = GetCellStateAtWorldPos(disc.transform.position);
                    disc.SetDiscovered(state != FogCellState.Unexplored);
                    if (disc.isDiscovered)
                    {
                        disc.SetShroudDimmed(state != FogCellState.Visible);
                    }
                }
            }
        }

        /// <summary>
        /// Verilen dünya koordinatının yerel oyuncunun herhangi bir dost birimi veya binası tarafından görülüp görülmediğini hesaplar.
        /// </summary>
        public bool IsPositionInPlayerVision(Vector3 worldPos)
        {
            UnitTeam localTeam = RTSGameManager.LocalPlayerTeam;

            // 1. Dost Birlikler (Mızrakçı, Okçu, Köylü vb.)
            foreach (var u in RTSUnit.AllUnits)
            {
                if (u != null && u.team == localTeam && u.currentHp > 0)
                {
                    float dist = Vector3.Distance(u.transform.position, worldPos);
                    if (dist <= u.sightRadius)
                    {
                        return true;
                    }
                }
            }

            // 2. Dost Binalar (Kışla, Kule, Şehir Merkezi vb.)
            foreach (var b in RTSBuilding.AllBuildings)
            {
                if (b != null && b.team == localTeam && b.isConstructed && b.currentHp > 0)
                {
                    float dist = Vector3.Distance(b.transform.position, worldPos);
                    if (dist <= b.sightRadius)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Belirli bir rakip biriminin yerel oyuncu tarafından görülüp görülmediği (Hile koruması filtresi).
        /// </summary>
        public bool IsUnitVisible(RTSUnit unit)
        {
            if (unit == null) return false;
            if (unit.team == RTSGameManager.LocalPlayerTeam) return true;

            return IsPositionInPlayerVision(unit.transform.position);
        }

        /// <summary>
        /// Belirli bir rakip binasının yerel oyuncu tarafından keşfedilip keşfedilmediği.
        /// </summary>
        public bool IsBuildingVisible(RTSBuilding building)
        {
            if (building == null) return false;
            if (building.team == RTSGameManager.LocalPlayerTeam) return true;

            return GetCellStateAtWorldPos(building.transform.position) != FogCellState.Unexplored;
        }

        public FogCellState GetCellState(int x, int z)
        {
            if (cellStates == null || x < 0 || x >= gridResolution || z < 0 || z >= gridResolution)
                return FogCellState.Unexplored;
            return cellStates[x, z];
        }

        public FogCellState GetCellStateAtWorldPos(Vector3 worldPos)
        {
            if (cellStates == null) return FogCellState.Unexplored;
            float cellSize = mapSize / gridResolution;
            int x = Mathf.Clamp(Mathf.FloorToInt((worldPos.x + mapSize * 0.5f) / cellSize), 0, gridResolution - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt((worldPos.z + mapSize * 0.5f) / cellSize), 0, gridResolution - 1);
            return cellStates[x, z];
        }

        /// <summary>
        /// Verilen pozisyona en yakın sisli (Unexplored) dünya koordinatını hesaplar.
        /// Eğer focus 'gold' ise, öncelikle sis altındaki altın madenlerine yönlendirir.
        /// </summary>
        public Vector3 GetNearestUnexploredPoint(Vector3 fromPos, string focus = "")
        {
            // 1. Odak altın ise ve henüz keşfedilmemiş altın madeni varsa doğrudan oraya git
            if (focus == "gold" || focus == "gold_mine")
            {
                foreach (var res in RTSResourceSource.AllSources)
                {
                    if (res != null && res.resourceType == RTSResourceType.Gold)
                    {
                        if (GetCellStateAtWorldPos(res.transform.position) == FogCellState.Unexplored)
                        {
                            return res.transform.position;
                        }
                    }
                }
            }

            if (cellStates == null) return Vector3.zero;

            float cellSize = mapSize / gridResolution;
            float halfMap = mapSize * 0.5f;

            int startX = Mathf.Clamp(Mathf.FloorToInt((fromPos.x + halfMap) / cellSize), 0, gridResolution - 1);
            int startZ = Mathf.Clamp(Mathf.FloorToInt((fromPos.z + halfMap) / cellSize), 0, gridResolution - 1);

            Vector3 closestUnexplored = Vector3.zero;
            float minSqrDist = float.MaxValue;

            // Halka halka dışarı doğru tara
            for (int r = 1; r < gridResolution; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;

                        int x = startX + dx;
                        int z = startZ + dz;

                        // Harita sonu siyah sınır kuşağına (x <= 2 veya x >= gridResolution - 3) otomatik keşif gönderilmez
                        if (x >= 3 && x < gridResolution - 3 && z >= 3 && z < gridResolution - 3)
                        {
                            if (cellStates[x, z] == FogCellState.Unexplored)
                            {
                                float worldX = (x + 0.5f) * cellSize - halfMap;
                                float worldZ = (z + 0.5f) * cellSize - halfMap;
                                Vector3 candidate = new Vector3(worldX, 0.1f, worldZ);
                                float sqrDist = (candidate - fromPos).sqrMagnitude;
                                if (sqrDist < minSqrDist)
                                {
                                    minSqrDist = sqrDist;
                                    closestUnexplored = candidate;
                                }
                            }
                        }
                    }
                }

                if (closestUnexplored != Vector3.zero)
                {
                    return closestUnexplored;
                }
            }

            return closestUnexplored;
        }
    }
}
