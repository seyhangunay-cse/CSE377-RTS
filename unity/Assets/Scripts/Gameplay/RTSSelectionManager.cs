using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using CSE377.AI;

namespace CSE377.Gameplay
{
    /// <summary>
    /// Klasik RTS fare kontrolleri:
    /// 1. Sol tık: Tekli asker veya bina seçimi.
    /// 2. Sol tık sürükleme: Yeşil seçim kutusu (Box / Marquee Selection).
    /// 3. Sağ tık: Haritada hareket, saldırı, kaynak toplama veya inşaat emri.
    /// 4. Bina İnşaat Modu (Placement Ghost Preview & Placement).
    /// </summary>
    public class RTSSelectionManager : MonoBehaviour
    {
        public static RTSSelectionManager Instance { get; private set; }

        [Header("Seçili Birlikler ve Binalar")]
        public List<RTSUnit> selectedUnits = new List<RTSUnit>();
        public RTSBuilding selectedBuilding;
        public RTSUnit inspectedEnemyUnit;
        public RTSBuilding inspectedEnemyBuilding;

        [Header("Bina İnşaat Modu")]
        public bool isPlacingBuilding = false;
        private RTSBuildingType placingType;
        private GameObject placementGhost;
        private bool placementGhostIsPrimitive = false;
        private float placingRotationY = 0f;

        [Header("Seçim Kutusu Görseli")]
        public Color boxFillColor = new Color(0.1f, 0.9f, 0.2f, 0.15f);
        public Color boxBorderColor = new Color(0.2f, 1f, 0.3f, 0.85f);
        public float borderThickness = 2f;

        private Vector2 mouseDragStart;
        private bool isDraggingBox = false;
        private Texture2D whiteTexture;

        private float lastClickTime = -999f;
        private string lastClickedUnitType = "";

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            whiteTexture = new Texture2D(1, 1);
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply();
        }

        private void Update()
        {
            if (Time.timeScale == 0f) return;

            if (isPlacingBuilding)
            {
                HandleBuildingPlacement();
                return;
            }

            HandleLeftClickSelection();
            HandleRightClickOrders();
            HandleKeyboardAttackOrders();
            HandleSelectionHotkeys();
        }

        #region Bina İnşaat Yerleştirme (Placement Mode)

        public void StartBuildingPlacement(RTSBuildingType type)
        {
            CancelBuildingPlacement();

            isPlacingBuilding = true;
            placingType = type;
            placingRotationY = 0f;
            placementGhostIsPrimitive = false;

            var archPrefab = Resources.Load<GameObject>($"Models/Buildings/{type}");
            if (archPrefab != null)
            {
                placementGhost = Instantiate(archPrefab);
            }
            else
            {
                placementGhostIsPrimitive = true;
                placementGhost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                switch (type)
                {
                    case RTSBuildingType.TownCenter: placementGhost.transform.localScale = new Vector3(7.4f, 6.8f, 7.4f); break;
                    case RTSBuildingType.Castle: placementGhost.transform.localScale = new Vector3(8.8f, 7.6f, 8.8f); break;
                    case RTSBuildingType.Barracks: placementGhost.transform.localScale = new Vector3(7.2f, 3.9f, 5.0f); break;
                    case RTSBuildingType.ArcheryRange: placementGhost.transform.localScale = new Vector3(7.2f, 3.6f, 5.0f); break;
                    case RTSBuildingType.SiegeWorkshop: placementGhost.transform.localScale = new Vector3(7.4f, 4.3f, 5.4f); break;
                    case RTSBuildingType.Market: placementGhost.transform.localScale = new Vector3(6.6f, 3.5f, 6.2f); break;
                    case RTSBuildingType.Blacksmith: placementGhost.transform.localScale = new Vector3(5.4f, 4.2f, 5.2f); break;
                    case RTSBuildingType.DefenseTower: placementGhost.transform.localScale = new Vector3(3.2f, 6.0f, 3.2f); break;
                    case RTSBuildingType.House: placementGhost.transform.localScale = new Vector3(4.1f, 3.7f, 4.1f); break;
                    case RTSBuildingType.Farm: placementGhost.transform.localScale = new Vector3(5.4f, 1.2f, 5.4f); break;
                    default: placementGhost.transform.localScale = new Vector3(5.0f, 4.0f, 5.0f); break;
                }
            }

            placementGhost.name = "Placement_Ghost";
            foreach (var col in placementGhost.GetComponentsInChildren<Collider>())
            {
                Destroy(col);
            }

            // Yarı saydam hayalet malzeme (bina siluetini net gösterir)
            var ghostShader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            var ghostMat = new Material(ghostShader);
            ghostMat.color = new Color(0.25f, 0.95f, 0.42f, 0.48f);
            if (ghostShader != null && ghostShader.name == "Standard")
            {
                ghostMat.SetFloat("_Mode", 3);
                ghostMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                ghostMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                ghostMat.SetInt("_ZWrite", 0);
                ghostMat.DisableKeyword("_ALPHATEST_ON");
                ghostMat.EnableKeyword("_ALPHABLEND_ON");
                ghostMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                ghostMat.renderQueue = 3000;
            }
            foreach (var ren in placementGhost.GetComponentsInChildren<Renderer>(true))
            {
                if (ren == null) continue;
                var shared = ren.sharedMaterials;
                if (shared == null || shared.Length <= 1)
                {
                    ren.sharedMaterial = ghostMat;
                }
                else
                {
                    var mats = new Material[shared.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = ghostMat;
                    ren.sharedMaterials = mats;
                }
            }

            GetBuildingPlacementInfo(type, out string bName, out string bCost, out string bDesc);
            if (RTSHUDController.Instance != null)
            {
                RTSHUDController.Instance.ShowPlacementBanner(
                    $"{bName.ToUpper()}  (Maliyet: {bCost})",
                    $"{bDesc}   •   [Sol Tık]: İnşa Et   •   [Sağ Tık / ESC]: İptal"
                );
                RTSHUDController.Instance.SetAiStatus($"[İNŞAAT EMRİ] {bName} ({bCost})  •  {bDesc}  |  Yerleştir: [Sol Tık]  •  İptal: [Sağ Tık / ESC]", new Color(1f, 0.85f, 0.3f));
            }
        }

        private void GetBuildingPlacementInfo(RTSBuildingType type, out string displayName, out string costText, out string desc)
        {
            switch (type)
            {
                case RTSBuildingType.TownCenter:
                    displayName = "Şehir Merkezi";
                    costText = "350 Odun, 100 Altın";
                    desc = "Köylü Eğitimi & Kaynak Depolama Merkezi";
                    break;
                case RTSBuildingType.Barracks:
                    displayName = "Kışla";
                    costText = "150 Odun";
                    desc = "Mızrakçı, Kılıçlı Piyade & Okçu Eğitimi";
                    break;
                case RTSBuildingType.Blacksmith:
                    displayName = "Demirci Atölyesi";
                    costText = "150 Odun";
                    desc = "Silah, Zırh, Ok Temreni & El Arabası Geliştirmeleri";
                    break;
                case RTSBuildingType.House:
                    displayName = "Ahşap Ev";
                    costText = "30 Odun";
                    desc = "+5 Nüfus Kapasitesi Sağlar";
                    break;
                case RTSBuildingType.Farm:
                    displayName = "Buğday Tarlası";
                    costText = "60 Odun";
                    desc = "Sürekli ve Güvenli Yiyecek Hasadı";
                    break;
                case RTSBuildingType.StoneWall:
                    displayName = "Taş Savunma Duvarı";
                    costText = "20 Odun, 10 Altın";
                    desc = "Düşman Geçişini Engelleyen Masif Taş Tahkimat";
                    break;
                case RTSBuildingType.CityGate:
                    displayName = "Şehir Kapısı";
                    costText = "50 Odun, 20 Altın";
                    desc = "Dost Orduların Geçişi İçin Kontrollü Ağır Kapı";
                    break;
                case RTSBuildingType.DefenseTower:
                    displayName = "Savunma Kulesi";
                    costText = "125 Odun, 50 Altın";
                    desc = "Ok Yaylımı ile Savunma (18m Menzil)";
                    break;
                case RTSBuildingType.Castle:
                    displayName = "Şanlı İmparatorluk Kalesi";
                    costText = "350 Odun, 250 Altın";
                    desc = "Ağır Şövalye & Kuşatma Mancınığı Eğitimi, 3'lü Ok Yaylımı";
                    break;
                case RTSBuildingType.ArcheryRange:
                    displayName = "Okçu Talimgahı";
                    costText = "175 Odun";
                    desc = "Menzilli Okçu ve Arbaletçi Birlikleri Eğitimi";
                    break;
                case RTSBuildingType.SiegeWorkshop:
                    displayName = "Kuşatma Atölyesi";
                    costText = "200 Odun, 100 Altın";
                    desc = "Kuşatma Mancınığı & Trebüşe Ağır Kuşatma Üretimi";
                    break;
                case RTSBuildingType.Market:
                    displayName = "Pazar Yeri";
                    costText = "150 Odun";
                    desc = "Odun, Yiyecek ve Altın Ticareti & Kaynak Takası";
                    break;
                default:
                    displayName = type.ToString();
                    costText = "";
                    desc = "";
                    break;
            }
        }

        private static bool IsFortificationType(RTSBuildingType type)
        {
            return type == RTSBuildingType.StoneWall || type == RTSBuildingType.CityGate || type == RTSBuildingType.DefenseTower;
        }

        private static float GetFortificationHalfWidth(RTSBuildingType type)
        {
            if (type == RTSBuildingType.StoneWall) return 2.0f;
            if (type == RTSBuildingType.CityGate) return 3.0f;
            if (type == RTSBuildingType.DefenseTower) return 1.0f;
            return 2.0f;
        }

        private static Vector2 GetFortificationHalfExtents(RTSBuildingType type, float rotY)
        {
            if (type == RTSBuildingType.DefenseTower) return new Vector2(1.0f, 1.0f);
            float halfW = GetFortificationHalfWidth(type);
            float halfT = 0.7f;
            bool rotated90 = Mathf.Abs(Mathf.Sin(rotY * Mathf.Deg2Rad)) > 0.707f;
            return rotated90 ? new Vector2(halfT, halfW) : new Vector2(halfW, halfT);
        }

        private Vector3 SnapFortificationPosition(Vector3 rawPos, RTSBuildingType newType, ref float targetRotY)
        {
            if (!IsFortificationType(newType)) return rawPos;

            float newHalfW = GetFortificationHalfWidth(newType);
            Vector3 bestSnapped = rawPos;
            float bestSnapDist = 3.2f; // Mıknatıs hizalama yarıçapı
            float bestRotY = targetRotY;

            foreach (var b in RTSBuilding.AllBuildings)
            {
                if (b == null || b.currentHp <= 0 || !IsFortificationType(b.buildingType)) continue;

                Vector2 existExt = GetFortificationHalfExtents(b.buildingType, b.transform.eulerAngles.y);
                float joinOffsetX = existExt.x + newHalfW;
                float joinOffsetZ = existExt.y + newHalfW;

                Vector3 leftCandidate = new Vector3(b.transform.position.x - joinOffsetX, rawPos.y, b.transform.position.z);
                Vector3 rightCandidate = new Vector3(b.transform.position.x + joinOffsetX, rawPos.y, b.transform.position.z);
                Vector3 backCandidate = new Vector3(b.transform.position.x, rawPos.y, b.transform.position.z - joinOffsetZ);
                Vector3 forwardCandidate = new Vector3(b.transform.position.x, rawPos.y, b.transform.position.z + joinOffsetZ);

                float dLeft = Vector2.Distance(new Vector2(rawPos.x, rawPos.z), new Vector2(leftCandidate.x, leftCandidate.z));
                if (dLeft < bestSnapDist)
                {
                    bestSnapDist = dLeft;
                    bestSnapped = leftCandidate;
                    if (newType != RTSBuildingType.DefenseTower) bestRotY = 0f;
                }

                float dRight = Vector2.Distance(new Vector2(rawPos.x, rawPos.z), new Vector2(rightCandidate.x, rightCandidate.z));
                if (dRight < bestSnapDist)
                {
                    bestSnapDist = dRight;
                    bestSnapped = rightCandidate;
                    if (newType != RTSBuildingType.DefenseTower) bestRotY = 0f;
                }

                float dBack = Vector2.Distance(new Vector2(rawPos.x, rawPos.z), new Vector2(backCandidate.x, backCandidate.z));
                if (dBack < bestSnapDist)
                {
                    bestSnapDist = dBack;
                    bestSnapped = backCandidate;
                    if (newType != RTSBuildingType.DefenseTower) bestRotY = 90f;
                }

                float dForward = Vector2.Distance(new Vector2(rawPos.x, rawPos.z), new Vector2(forwardCandidate.x, forwardCandidate.z));
                if (dForward < bestSnapDist)
                {
                    bestSnapDist = dForward;
                    bestSnapped = forwardCandidate;
                    if (newType != RTSBuildingType.DefenseTower) bestRotY = 90f;
                }
            }

            targetRotY = bestRotY;
            bestSnapped.x = Mathf.Clamp(bestSnapped.x, -64f, 64f);
            bestSnapped.z = Mathf.Clamp(bestSnapped.z, -64f, 64f);
            return bestSnapped;
        }

        private void HandleBuildingPlacement()
        {
            if (IsFortificationType(placingType) && Input.GetKeyDown(KeyCode.R))
            {
                placingRotationY = (placingRotationY < 45f) ? 90f : 0f;
            }

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f))
            {
                Vector3 clampedPlacePos = new Vector3(
                    Mathf.Clamp(hit.point.x, -64f, 64f),
                    hit.point.y,
                    Mathf.Clamp(hit.point.z, -64f, 64f)
                );

                // Taş Duvar, Şehir Kapısı ve Savunma Kulesi yan yana getirildiğinde 4 yönde uç uca tam kenetlenmesi için otomatik hizala (Snap)
                clampedPlacePos = SnapFortificationPosition(clampedPlacePos, placingType, ref placingRotationY);

                if (placementGhost != null)
                {
                    float yOff = placementGhostIsPrimitive ? (placementGhost.transform.localScale.y * 0.5f) : 0.04f;
                    placementGhost.transform.position = clampedPlacePos + Vector3.up * yOff;
                    placementGhost.transform.rotation = Quaternion.Euler(0f, placingRotationY, 0f);
                }

                bool isBlockedByObject = false;
                Vector2 place2D = new Vector2(clampedPlacePos.x, clampedPlacePos.z);
                bool placingFortification = IsFortificationType(placingType);

                foreach (var b in RTSBuilding.AllBuildings)
                {
                    if (b == null) continue;
                    if (placingFortification && IsFortificationType(b.buildingType))
                    {
                        // Tahkimat parçaları (Duvar + Kapı + Kule) uç uca birleşebilmeli; sadece üst üste binmeyi engelle
                        Vector2 existExt = GetFortificationHalfExtents(b.buildingType, b.transform.eulerAngles.y);
                        Vector2 newExt = GetFortificationHalfExtents(placingType, placingRotationY);
                        float dx = Mathf.Abs(place2D.x - b.transform.position.x);
                        float dz = Mathf.Abs(place2D.y - b.transform.position.z);
                        if (dx < (existExt.x + newExt.x) - 0.25f && dz < (existExt.y + newExt.y) - 0.25f)
                        {
                            isBlockedByObject = true;
                            break;
                        }
                    }
                    else
                    {
                        float minDist = (Mathf.Max(b.GetBuildingFootprint().x, b.GetBuildingFootprint().z) * 0.5f) + 2.8f;
                        if (Vector2.Distance(place2D, new Vector2(b.transform.position.x, b.transform.position.z)) < minDist)
                        {
                            isBlockedByObject = true;
                            break;
                        }
                    }
                }

                if (!isBlockedByObject)
                {
                    foreach (var r in RTSResourceSource.AllSources)
                    {
                        if (r == null || r.GetComponent<RTSBuilding>() != null) continue;
                        if (Vector2.Distance(place2D, new Vector2(r.transform.position.x, r.transform.position.z)) < 3.5f)
                        {
                            isBlockedByObject = true;
                            break;
                        }
                    }
                }

                if (placementGhost != null)
                {
                    Color ghostCol = isBlockedByObject
                        ? new Color(1.0f, 0.22f, 0.22f, 0.58f)
                        : new Color(0.25f, 0.95f, 0.42f, 0.48f);
                    var firstRen = placementGhost.GetComponentInChildren<Renderer>(true);
                    if (firstRen != null && firstRen.sharedMaterial != null)
                    {
                        firstRen.sharedMaterial.color = ghostCol;
                    }
                }

                // Sol Tık: Binayı İnşa Et
                if (Input.GetMouseButtonDown(0))
                {
                    if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    {
                        return;
                    }

                    if (Mathf.Abs(hit.point.x) > 66f || Mathf.Abs(hit.point.z) > 66f)
                    {
                        RTSHUDController.Instance?.SetAiStatus("⚠️ [HARİTA SINIRI] Harita sonundaki siyah sınır bölgesine bina inşa edilemez!", new Color(1f, 0.45f, 0.25f));
                        return;
                    }

                    if (isBlockedByObject)
                    {
                        RTSHUDController.Instance?.SetAiStatus("⚠️ [ENGEL VAR] Kaynakların, ağaçların veya mevcut binaların üstüne bina inşa edilemez!", new Color(1f, 0.45f, 0.25f));
                        return;
                    }

                    ConfirmBuildingPlacement(clampedPlacePos);
                }
            }

            // Sağ Tık veya ESC: İptal
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                CancelBuildingPlacement();
            }
        }

        private void ConfirmBuildingPlacement(Vector3 placePosition)
        {
            string bKey = (placingType == RTSBuildingType.DefenseTower) ? "tower" :
                          (placingType == RTSBuildingType.Blacksmith) ? "blacksmith" :
                          (placingType == RTSBuildingType.House) ? "house" :
                          (placingType == RTSBuildingType.Farm) ? "farm" :
                          (placingType == RTSBuildingType.StoneWall) ? "stonewall" :
                          (placingType == RTSBuildingType.CityGate) ? "citygate" :
                          (placingType == RTSBuildingType.Castle) ? "castle" :
                          (placingType == RTSBuildingType.ArcheryRange) ? "archeryrange" :
                          (placingType == RTSBuildingType.SiegeWorkshop) ? "siegeworkshop" :
                          (placingType == RTSBuildingType.Market) ? "market" :
                          (placingType == RTSBuildingType.TownCenter) ? "towncenter" : "barracks";

            var gameMgr = RTSGameManager.Instance;
            if (gameMgr == null) return;

            if (GameActionValidator.BuildingCosts.TryGetValue(bKey, out var cost))
            {
                if (gameMgr.wood < cost.wood || gameMgr.food < cost.food || gameMgr.gold < cost.gold)
                {
                    GetBuildingPlacementInfo(placingType, out string bName, out _, out _);
                    string msg = $"Yetersiz Kaynak! {bName} inşası için {cost.wood} Odun, {cost.gold} Altın gerekiyor.";
                    if (RTSHUDController.Instance != null) RTSHUDController.Instance.SetAiStatus(msg, new Color(1f, 0.4f, 0.4f));
                    Debug.LogWarning($"[RTSSelectionManager] {msg}");
                    return;
                }

                // Kaynakları düş
                gameMgr.wood -= cost.wood;
                gameMgr.food -= cost.food;
                gameMgr.gold -= cost.gold;
                gameMgr.UpdateHUD();
            }

            // Temeli at (Yerel oyuncunun takımına göre Mavi veya Kırmızı)
            UnitTeam myTeam = RTSGameManager.LocalPlayerTeam;
            var building = RTSBuilding.SpawnBuilding(placingType, placePosition, myTeam, startBuilt: false, rotationY: placingRotationY);
            RTSNetworkSyncManager.Instance?.BroadcastBuildingSpawn(placingType, placePosition, myTeam, false, placingRotationY);

            // Seçili köylüleri inşaata gönder
            bool assignedVillager = false;
            foreach (var u in selectedUnits)
            {
                if (u != null && u.team == myTeam)
                {
                    var villager = u.GetComponent<RTSVillager>();
                    if (villager != null)
                    {
                        villager.AssignBuildTask(building);
                        assignedVillager = true;
                    }
                }
            }

            // Eğer seçili köylü yoksa sahadan kendi takımına ait bir köylü bulup görevlendir
            if (!assignedVillager)
            {
                foreach (var u in RTSUnit.AllUnits)
                {
                    if (u != null && u.team == myTeam && u.currentHp > 0)
                    {
                        var v = u.GetComponent<RTSVillager>();
                        if (v != null)
                        {
                            v.AssignBuildTask(building);
                            break;
                        }
                    }
                }
            }

            SpawnClickFeedback(placePosition, Color.yellow);
            RTSAudioManager.Instance?.PlayVoice("villager_build", force: true);
            RTSAudioManager.Instance?.PlaySound(RTSSoundType.HammerBuild);
            CancelBuildingPlacement();

            if (RTSHUDController.Instance != null)
            {
                GetBuildingPlacementInfo(placingType, out string bldName, out _, out _);
                RTSHUDController.Instance.SetAiStatus($"{bldName} temeli atıldı! Köylü inşaata başladı.", Color.green);
            }
        }

        public void CancelBuildingPlacement()
        {
            isPlacingBuilding = false;
            if (placementGhost != null)
            {
                Destroy(placementGhost);
                placementGhost = null;
            }
            RTSHUDController.Instance?.HidePlacementBanner();
        }

        #endregion

        #region Seçim Mekaniği

        private void HandleLeftClickSelection()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                mouseDragStart = Input.mousePosition;
                isDraggingBox = false;
            }

            if (Input.GetMouseButton(0))
            {
                if (Vector2.Distance(mouseDragStart, Input.mousePosition) > 12f)
                {
                    isDraggingBox = true;
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                bool isShiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                if (isDraggingBox)
                {
                    ExecuteBoxSelection(isShiftHeld);
                }
                else
                {
                    ExecuteSingleClickSelection(isShiftHeld);
                }

                isDraggingBox = false;
            }
        }

        private void ExecuteSingleClickSelection(bool isShiftHeld)
        {
            UnitTeam myTeam = RTSGameManager.LocalPlayerTeam;
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f))
            {
                // 1. Asker seçimi veya Düşmana Sol Tık ile Taarruz
                var unit = hit.collider.GetComponentInParent<RTSUnit>();
                if (unit != null)
                {
                    if (unit.team == myTeam)
                    {
                        bool isDoubleClick = (Time.time - lastClickTime < 0.38f && lastClickedUnitType == unit.unitType);
                        lastClickTime = Time.time;
                        lastClickedUnitType = unit.unitType;

                        if (isDoubleClick)
                        {
                            SelectAllOfSameTypeOnScreen(unit.unitType);
                            return;
                        }

                        if (!isShiftHeld) DeselectAll();
                        inspectedEnemyUnit = null;
                        inspectedEnemyBuilding = null;
                        SelectUnit(unit);
                    }
                    else
                    {
                        // Rakip askeri tıklandı!
                        if (selectedUnits.Count > 0)
                        {
                            // Eğer oyuncunun seçili birlikleri varsa: Doğrudan taarruz emri ver ve düşman kartını göster
                            inspectedEnemyUnit = unit;
                            inspectedEnemyBuilding = null;
                            SpawnClickFeedback(unit.transform.position, Color.red);
                            RTSAudioManager.Instance?.PlaySound(RTSSoundType.AttackOrder);
                            RTSAudioManager.Instance?.NotifyCombat();

                            var leadUnit = selectedUnits.Find(u => u != null && (u.unitType == "cavalry" || u.unitType == "scout")) 
                                        ?? selectedUnits.Find(u => u != null && u.unitType != "villager") 
                                        ?? selectedUnits.Find(u => u != null);
                            if (leadUnit != null) RTSAudioManager.Instance?.PlayUnitVoice(leadUnit.unitType, "attack", force: true);

                            RTSHUDController.Instance?.SetAiStatus($"[SALDIRI] \"{unit.unitType.ToUpper()} hedefine taarruz!\"", new Color(1f, 0.35f, 0.35f));
                            RTSNetworkSyncManager.Instance?.BroadcastAttackOrder(selectedUnits, unit.transform.position, false);
                            foreach (var u in selectedUnits)
                            {
                                if (u != null && u.team == myTeam) u.AttackTarget(unit);
                            }
                        }
                        else
                        {
                            // Hiç birlik seçili değilse: Düşman birimini inceleme kartına al
                            DeselectAll();
                            inspectedEnemyUnit = unit;
                            inspectedEnemyBuilding = null;
                            RTSAudioManager.Instance?.PlaySound(RTSSoundType.UnitSelect);
                        }
                    }
                    return;
                }

                // 2. Bina seçimi veya Düşman Binasına Sol Tık ile Kuşatma
                var building = hit.collider.GetComponentInParent<RTSBuilding>();
                if (building != null)
                {
                    if (building.team == myTeam)
                    {
                        DeselectAll();
                        inspectedEnemyUnit = null;
                        inspectedEnemyBuilding = null;
                        SelectBuilding(building);
                    }
                    else
                    {
                        // Rakip binası tıklandı!
                        if (selectedUnits.Count > 0)
                        {
                            // Eğer oyuncunun seçili birlikleri varsa: Doğrudan kuşatma emri ver ve bina kartını göster
                            inspectedEnemyBuilding = building;
                            inspectedEnemyUnit = null;
                            SpawnClickFeedback(building.transform.position, Color.red);
                            RTSAudioManager.Instance?.PlaySound(RTSSoundType.WarHorn);
                            RTSAudioManager.Instance?.PlaySound(RTSSoundType.AttackOrder);
                            RTSAudioManager.Instance?.NotifyCombat();

                            var leadSiege = selectedUnits.Find(u => u != null && u.unitType == "trebuchet") 
                                         ?? selectedUnits.Find(u => u != null && u.unitType != "villager") 
                                         ?? selectedUnits.Find(u => u != null);
                            if (leadSiege != null) RTSAudioManager.Instance?.PlayUnitVoice(leadSiege.unitType, "attack", force: true);

                            RTSHUDController.Instance?.SetAiStatus($"[KUŞATMA] \"{building.displayName.ToUpper()} yerle bir edilecek! Hücum!\"", new Color(1f, 0.45f, 0.15f));
                            RTSNetworkSyncManager.Instance?.BroadcastAttackOrder(selectedUnits, building.transform.position, true);
                            foreach (var u in selectedUnits)
                            {
                                if (u != null && u.team == myTeam) u.AttackBuilding(building);
                            }
                        }
                        else
                        {
                            // Hiç birlik seçili değilse: Düşman binasını inceleme kartına al
                            DeselectAll();
                            inspectedEnemyBuilding = building;
                            inspectedEnemyUnit = null;
                            RTSAudioManager.Instance?.PlaySound(RTSSoundType.UnitSelect);
                        }
                    }
                    return;
                }
            }

            // Boşluğa veya zemine tıklandıysa tüm seçimleri temizle
            if (!isShiftHeld)
            {
                DeselectAll();
            }
        }

        private void ExecuteBoxSelection(bool isShiftHeld)
        {
            UnitTeam myTeam = RTSGameManager.LocalPlayerTeam;
            Rect screenRect = GetScreenRect(mouseDragStart, Input.mousePosition);
            Camera cam = Camera.main;

            if (!isShiftHeld)
            {
                DeselectAll();
            }

            foreach (var unit in RTSUnit.AllUnits)
            {
                if (unit != null && unit.team == myTeam)
                {
                    Vector3 screenPos = cam.WorldToScreenPoint(unit.transform.position);
                    if (screenPos.z > 0 && screenRect.Contains(new Vector2(screenPos.x, Screen.height - screenPos.y)))
                    {
                        SelectUnit(unit);
                    }
                }
            }
        }

        public void SelectUnit(RTSUnit unit)
        {
            HideBuildingSelectionDecal();
            selectedBuilding = null;
            inspectedEnemyUnit = null;
            inspectedEnemyBuilding = null;

            if (!selectedUnits.Contains(unit))
            {
                selectedUnits.Add(unit);
                unit.SetSelected(true);

                string t = (unit.unitType ?? "").ToLower();
                if (t == "cavalry" || t == "scout") RTSAudioManager.Instance?.PlaySound(RTSSoundType.UnitSelectCavalry);
                else if (t == "archer" || t == "crossbowman") RTSAudioManager.Instance?.PlaySound(RTSSoundType.UnitSelectArcher);
                else if (t == "villager") RTSAudioManager.Instance?.PlaySound(RTSSoundType.UnitSelectVillager);
                else if (t == "trebuchet") RTSAudioManager.Instance?.PlaySound(RTSSoundType.UnitSelectTrebuchet);
                else RTSAudioManager.Instance?.PlaySound(RTSSoundType.UnitSelectSpearman);

                // Gerçek Türkçe Asker Seslendirmesini Oynat
                RTSAudioManager.Instance?.PlayUnitVoice(unit.unitType, "select");

                if (RTSHUDController.Instance != null)
                {
                    RTSHUDController.Instance.SetAiStatus(GetTurkishVoiceLineForSelection(unit.unitType), Color.white);
                }
            }

            // Seçilen birimler arasında köylü varsa inşaat menüsünü aç
            bool hasVillager = false;
            foreach (var u in selectedUnits)
            {
                if (u != null && u.GetComponent<RTSVillager>() != null)
                {
                    hasVillager = true;
                    break;
                }
            }

            if (RTSHUDController.Instance != null)
            {
                if (hasVillager) RTSHUDController.Instance.ShowVillagerBuildMenu();
                else RTSHUDController.Instance.HideActionPanel();
            }
        }

        public void SelectAllOfSameTypeOnScreen(string unitType)
        {
            DeselectAll();
            Camera cam = Camera.main;
            if (cam == null) return;

            int count = 0;
            RTSUnit firstSelected = null;
            UnitTeam myTeam = RTSGameManager.LocalPlayerTeam;
            foreach (var u in RTSUnit.AllUnits)
            {
                if (u != null && u.team == myTeam && u.unitType == unitType && u.currentHp > 0)
                {
                    Vector3 vp = cam.WorldToViewportPoint(u.transform.position);
                    if (vp.z > 0 && vp.x >= 0 && vp.x <= 1 && vp.y >= 0 && vp.y <= 1)
                    {
                        if (firstSelected == null) firstSelected = u;
                        selectedUnits.Add(u);
                        u.SetSelected(true);
                        count++;
                    }
                }
            }

            if (firstSelected != null)
            {
                string t = (firstSelected.unitType ?? "").ToLower();
                if (t == "cavalry" || t == "scout") RTSAudioManager.Instance?.PlaySound(RTSSoundType.UnitSelectCavalry);
                else if (t == "archer" || t == "crossbowman") RTSAudioManager.Instance?.PlaySound(RTSSoundType.UnitSelectArcher);
                else if (t == "villager") RTSAudioManager.Instance?.PlaySound(RTSSoundType.UnitSelectVillager);
                else if (t == "trebuchet") RTSAudioManager.Instance?.PlaySound(RTSSoundType.UnitSelectTrebuchet);
                else RTSAudioManager.Instance?.PlaySound(RTSSoundType.UnitSelectSpearman);

                RTSAudioManager.Instance?.PlayUnitVoice(firstSelected.unitType, "select");
            }

            RTSHUDController.Instance?.SetAiStatus($"[ÇİFT TIK] Ekrandaki tüm {unitType.ToUpper()} birlikleri ({count} adet) seçildi!", Color.cyan);
        }

        private string GetTurkishVoiceLineForSelection(string unitType)
        {
            switch (unitType.ToLower())
            {
                case "spearman":
                    return "[MIZRAKÇI] \"Emredin kumandanım! Kalkan duvarımız aşılmazdır!\"";
                case "swordsman":
                    return "[KILIÇ USTASI] \"Kılıcım ve canım imparatorluğuma feda olsun! Ön saftayım!\"";
                case "archer":
                    return "[OKÇU] \"Hedef gösterin efendim, yayım gerildi!\"";
                case "crossbowman":
                    return "[ARBALETÇİ] \"Arbalet hazır efendim! En ağır zırhları bile delip geçeriz!\"";
                case "cavalry":
                    return "[SÜVARİ] \"Atlarımız hazır! Hızlı şok taarruzuna hazırız!\"";
                case "scout":
                    return "[KEŞİF ATLI] \"Gözcülük görevindeyim! Düşman hareketlerini raporluyorum!\"";
                case "villager":
                    return "[KÖYLÜ] \"Buyrun efendim, yeni bir inşaat veya hasat emri mi var?\"";
                case "trebuchet":
                    return "[KUŞATMA] \"Mancınıklar kuruldu, kale surlarını yıkmaya hazırız!\"";
                default:
                    return "[SAVAŞÇI] \"Emirlerinizi bekliyoruz efendim!\"";
            }
        }

        public void SelectBuilding(RTSBuilding building)
        {
            DeselectAllUnits();
            inspectedEnemyUnit = null;
            inspectedEnemyBuilding = null;
            selectedBuilding = building;

            if (RTSHUDController.Instance != null)
            {
                if (building.buildingType == RTSBuildingType.Barracks)
                {
                    RTSHUDController.Instance.ShowBarracksMenu(building);
                }
                else if (building.buildingType == RTSBuildingType.Blacksmith)
                {
                    RTSHUDController.Instance.ShowBlacksmithMenu(building);
                }
                else if (building.buildingType == RTSBuildingType.TownCenter)
                {
                    RTSHUDController.Instance.ShowTownCenterMenu(building);
                }
                else if (building.buildingType == RTSBuildingType.Castle)
                {
                    RTSHUDController.Instance.ShowCastleMenu(building);
                }
                else if (building.buildingType == RTSBuildingType.ArcheryRange)
                {
                    RTSHUDController.Instance.ShowArcheryRangeMenu(building);
                }
                else if (building.buildingType == RTSBuildingType.SiegeWorkshop)
                {
                    RTSHUDController.Instance.ShowSiegeWorkshopMenu(building);
                }
                else if (building.buildingType == RTSBuildingType.Market)
                {
                    RTSHUDController.Instance.ShowMarketMenu(building);
                }
                else
                {
                    RTSHUDController.Instance.HideActionPanel();
                }
            }

            SpawnClickFeedback(building.transform.position, Color.cyan);
            RTSAudioManager.Instance?.PlaySound(RTSSoundType.BuildingSelect, 0.04f);
            ShowBuildingSelectionDecal(building);
        }

        public void DeselectAll()
        {
            HideBuildingSelectionDecal();
            DeselectAllUnits();
            selectedBuilding = null;
            inspectedEnemyUnit = null;
            inspectedEnemyBuilding = null;

            if (RTSHUDController.Instance != null)
            {
                RTSHUDController.Instance.HideActionPanel();
            }
        }

        private void DeselectAllUnits()
        {
            foreach (var u in selectedUnits)
            {
                if (u != null) u.SetSelected(false);
            }
            selectedUnits.Clear();
        }

        public void DeselectUnit(RTSUnit unit)
        {
            if (unit == null) return;
            unit.SetSelected(false);
            selectedUnits.Remove(unit);
            if (selectedUnits.Count == 0 && inspectedEnemyUnit == null && inspectedEnemyBuilding == null && selectedBuilding == null)
            {
                RTSHUDController.Instance?.HideActionPanel();
            }
        }

        private GameObject buildingSelectionDecal;

        private void ShowBuildingSelectionDecal(RTSBuilding building)
        {
            HideBuildingSelectionDecal();
            if (building == null) return;

            buildingSelectionDecal = new GameObject("BuildingSelectionDecal");
            Vector3 footprint = building.GetBuildingFootprint();
            buildingSelectionDecal.transform.position = new Vector3(building.transform.position.x, 0.035f, building.transform.position.z);

            var standardShader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            var decalMat = new Material(standardShader)
            {
                color = (building.team == UnitTeam.Player) 
                    ? new Color(0.2f, 0.88f, 1f, 0.95f) 
                    : new Color(0.95f, 0.22f, 0.22f, 0.95f)
            };

            float sx = footprint.x * 0.5f + 0.6f;
            float sz = footprint.z * 0.5f + 0.6f;
            float th = 0.14f;

            // 4 Kenar çizgisi
            CreateDecalEdge(buildingSelectionDecal.transform, new Vector3(0, 0, sz), new Vector3(sx * 2f, 0.015f, th), decalMat);
            CreateDecalEdge(buildingSelectionDecal.transform, new Vector3(0, 0, -sz), new Vector3(sx * 2f, 0.015f, th), decalMat);
            CreateDecalEdge(buildingSelectionDecal.transform, new Vector3(-sx, 0, 0), new Vector3(th, 0.015f, sz * 2f), decalMat);
            CreateDecalEdge(buildingSelectionDecal.transform, new Vector3(sx, 0, 0), new Vector3(th, 0.015f, sz * 2f), decalMat);
        }

        private void CreateDecalEdge(Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(edge.GetComponent<Collider>());
            edge.transform.SetParent(parent, false);
            edge.transform.localPosition = localPos;
            edge.transform.localScale = localScale;
            edge.GetComponent<Renderer>().material = mat;
        }

        private void HideBuildingSelectionDecal()
        {
            if (buildingSelectionDecal != null)
            {
                Destroy(buildingSelectionDecal);
                buildingSelectionDecal = null;
            }
        }

        #endregion

        #region Sağ Tık ve Klavye Taarruz Emirleri

        private void HandleRightClickOrders()
        {
            if (Input.GetMouseButtonDown(1) && selectedUnits.Count > 0)
            {
                Camera cam = Camera.main;
                if (cam == null) return;

                Ray ray = cam.ScreenPointToRay(Input.mousePosition);

                // Sonsuz matematiksel zemin düzlemi (Y = 0) - Herhangi bir collider olmasa bile zemin noktasını garanti eder
                Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
                Vector3 groundPos = Vector3.zero;
                bool hitGroundPlane = groundPlane.Raycast(ray, out float enterDist);
                if (hitGroundPlane)
                {
                    groundPos = ray.GetPoint(enterDist);
                }

                bool hasPhysicalHit = Physics.Raycast(ray, out RaycastHit hit, 300f);
                Vector3 clickWorldPos = hasPhysicalHit ? hit.point : groundPos;

                // Doğrudan zemine, köprüye veya nehir sınırına mı tıklandı?
                bool isGroundHit = !hasPhysicalHit || (hit.collider != null && (
                    hit.collider.name.Contains("Terrain") || 
                    hit.collider.name.Contains("Ground") || 
                    hit.collider.name.Contains("Plane") ||
                    hit.collider.name.Contains("Bridge") ||
                    hit.collider.name.Contains("Ramp") ||
                    hit.collider.name.Contains("Deck") ||
                    hit.collider.name.Contains("Road") ||
                    hit.collider.name.Contains("Shallow") ||
                    hit.collider.name.Contains("River_Water_Blocker")));

                // 1. Hedef toplanabilir kaynak mı? (Ağaç / Altın Madeni / Böğürtlen / Dost Tarla)
                // Rakip tarlaya tıklandığında hasat değil saldırı emri verilir.
                UnitTeam myTeam = RTSGameManager.LocalPlayerTeam;
                var resourceSource = hasPhysicalHit ? hit.collider.GetComponentInParent<RTSResourceSource>() : null;
                if (resourceSource != null)
                {
                    var resBld = resourceSource.GetComponent<RTSBuilding>();
                    if (resBld != null && resBld.team != myTeam)
                    {
                        resourceSource = null;
                    }
                }
                if (resourceSource == null && !isGroundHit && selectedUnits.Exists(u => u != null && u.GetComponent<RTSVillager>() != null))
                {
                    float closestResDist = 0.8f;
                    foreach (var res in RTSResourceSource.AllSources)
                    {
                        if (res != null && res.remainingAmount > 0)
                        {
                            var resBld = res.GetComponent<RTSBuilding>();
                            if (resBld != null && resBld.team != myTeam) continue;

                            Collider rCol = res.GetComponent<Collider>();
                            float d = (rCol != null) 
                                ? Vector3.Distance(new Vector3(clickWorldPos.x, 0, clickWorldPos.z), rCol.ClosestPoint(new Vector3(clickWorldPos.x, 0, clickWorldPos.z)))
                                : Vector3.Distance(new Vector3(clickWorldPos.x, 0, clickWorldPos.z), new Vector3(res.transform.position.x, 0, res.transform.position.z));

                            if (d < closestResDist)
                            {
                                closestResDist = d;
                                resourceSource = res;
                            }
                        }
                    }
                }

                if (resourceSource != null)
                {
                    bool assignedVillager = false;
                    foreach (var u in selectedUnits)
                    {
                        if (u != null)
                        {
                            var villager = u.GetComponent<RTSVillager>();
                            if (villager != null)
                            {
                                villager.AssignHarvestTask(resourceSource);
                                assignedVillager = true;
                            }
                        }
                    }

                    if (assignedVillager)
                    {
                        RTSNetworkSyncManager.Instance?.BroadcastVillagerOrder(selectedUnits, resourceSource.transform.position, "HARVEST");
                        SpawnClickFeedback(resourceSource.transform.position, Color.yellow);
                        RTSAudioManager.Instance?.PlayVoice("villager_work", force: true);
                        if (resourceSource.resourceType == RTSResourceType.Gold)
                        {
                            RTSAudioManager.Instance?.PlaySound(RTSSoundType.GoldMine);
                        }
                        else
                        {
                            RTSAudioManager.Instance?.PlaySound(RTSSoundType.HarvestChop);
                        }

                        string resIcon = (resourceSource.resourceType == RTSResourceType.Food) ? "[Gıda]" :
                                         (resourceSource.resourceType == RTSResourceType.Gold) ? "[Altın]" : "[Odun]";
                        string resName = (resourceSource.resourceType == RTSResourceType.Food) ? "yiyecek" :
                                         (resourceSource.resourceType == RTSResourceType.Gold) ? "altın" : "odun";

                        RTSHUDController.Instance?.SetAiStatus($"{resIcon} [KÖYLÜ] \"İşimin başındayım, {resName} topluyorum!\"", Color.green);
                        return;
                    }
                }

                // 2. Hedef inşa halindeki bir bina mı? (Foundation)
                var buildingTarget = hasPhysicalHit ? hit.collider.GetComponentInParent<RTSBuilding>() : null;
                if ((buildingTarget == null || buildingTarget.isConstructed || buildingTarget.team != myTeam) &&
                    selectedUnits.Exists(u => u != null && u.GetComponent<RTSVillager>() != null))
                {
                    float searchRadius = 5.0f;
                    RTSBuilding bestBld = null;
                    float bestDist = searchRadius;

                    foreach (var b in RTSBuilding.AllBuildings)
                    {
                        if (b != null && !b.isConstructed && b.team == myTeam)
                        {
                            Collider bc = b.GetComponent<Collider>();
                            float d = (bc != null) 
                                ? Vector3.Distance(clickWorldPos, bc.ClosestPoint(clickWorldPos))
                                : Vector3.Distance(new Vector3(clickWorldPos.x, 0, clickWorldPos.z), new Vector3(b.transform.position.x, 0, b.transform.position.z));

                            if (d < bestDist)
                            {
                                bestDist = d;
                                bestBld = b;
                            }
                        }
                    }

                    if (bestBld != null)
                    {
                        buildingTarget = bestBld;
                    }
                }

                if (buildingTarget != null && !buildingTarget.isConstructed && buildingTarget.team == myTeam)
                {
                    bool assignedVillager = false;
                    foreach (var u in selectedUnits)
                    {
                        if (u != null)
                        {
                            var villager = u.GetComponent<RTSVillager>();
                            if (villager != null)
                            {
                                villager.AssignBuildTask(buildingTarget);
                                assignedVillager = true;
                            }
                        }
                    }

                    if (assignedVillager)
                    {
                        RTSNetworkSyncManager.Instance?.BroadcastVillagerOrder(selectedUnits, buildingTarget.transform.position, "BUILD");
                        SpawnClickFeedback(buildingTarget.transform.position, Color.yellow);
                        RTSAudioManager.Instance?.PlayVoice("villager_build", force: true);
                        RTSAudioManager.Instance?.PlaySound(RTSSoundType.HammerBuild);
                        RTSHUDController.Instance?.SetAiStatus("[İNŞAAT] \"Hemen başlıyorum, yapıyı tamamlayacağız!\"", Color.green);
                        return;
                    }
                }

                // 2b. Hedef hasar görmüş dost bir bina mı? (Tamirat / Repair)
                if (buildingTarget != null && buildingTarget.isConstructed && buildingTarget.team == myTeam && buildingTarget.currentHp < buildingTarget.maxHp)
                {
                    bool assignedRepairVillager = false;
                    foreach (var u in selectedUnits)
                    {
                        if (u != null)
                        {
                            var villager = u.GetComponent<RTSVillager>();
                            if (villager != null)
                            {
                                villager.AssignRepairTask(buildingTarget);
                                assignedRepairVillager = true;
                            }
                        }
                    }

                    if (assignedRepairVillager)
                    {
                        RTSNetworkSyncManager.Instance?.BroadcastVillagerOrder(selectedUnits, buildingTarget.transform.position, "REPAIR");
                        SpawnClickFeedback(buildingTarget.transform.position, new Color(0.2f, 0.8f, 1f));
                        RTSAudioManager.Instance?.PlayVoice("villager_build", force: true);
                        RTSAudioManager.Instance?.PlaySound(RTSSoundType.HammerBuild);
                        RTSHUDController.Instance?.SetAiStatus($"[TAMİRAT] \"Hasar görmüş {buildingTarget.displayName} yapısını onarıyorum!\"", new Color(0.2f, 0.9f, 1f));
                        return;
                    }
                }

                // 3 & 4. Hedef rakip birimi veya binası mı? (Doğrudan Collider Tıklaması)
                RTSBuilding enemyBuilding = hasPhysicalHit ? hit.collider.GetComponentInParent<RTSBuilding>() : null;
                RTSUnit enemyUnit = hasPhysicalHit ? hit.collider.GetComponentInParent<RTSUnit>() : null;

                // Tıklama Toleransı (Click Magnetism): Eğer doğrudan ince collider vurulamadıysa ancak
                // tıklanan noktanın 4.5m yakınında rakip birimi veya 6.5m yakınında rakip binası varsa kilitlen
                if ((enemyBuilding == null || enemyBuilding.team == myTeam) && 
                    (enemyUnit == null || enemyUnit.team == myTeam))
                {
                    float closestUnitDist = 4.5f;
                    foreach (var u in RTSUnit.AllUnits)
                    {
                        if (u != null && u.team != myTeam && u.currentHp > 0)
                        {
                            float d = Vector3.Distance(new Vector3(clickWorldPos.x, 0, clickWorldPos.z), new Vector3(u.transform.position.x, 0, u.transform.position.z));
                            if (d < closestUnitDist)
                            {
                                closestUnitDist = d;
                                enemyUnit = u;
                            }
                        }
                    }

                    if (enemyUnit == null)
                    {
                        float closestBldDist = 6.5f;
                        foreach (var b in RTSBuilding.AllBuildings)
                        {
                            if (b != null && b.team != myTeam && b.currentHp > 0)
                            {
                                Collider bCol = b.GetComponent<Collider>();
                                float d = (bCol != null) 
                                    ? Vector3.Distance(new Vector3(clickWorldPos.x, 0, clickWorldPos.z), bCol.ClosestPoint(new Vector3(clickWorldPos.x, 0, clickWorldPos.z)))
                                    : Vector3.Distance(new Vector3(clickWorldPos.x, 0, clickWorldPos.z), new Vector3(b.transform.position.x, 0, b.transform.position.z));

                                if (d < closestBldDist)
                                {
                                    closestBldDist = d;
                                    enemyBuilding = b;
                                }
                            }
                        }
                    }
                }

                // Rakip binası kuşatması
                if (enemyBuilding != null && enemyBuilding.team != myTeam && enemyBuilding.currentHp > 0)
                {
                    inspectedEnemyBuilding = enemyBuilding;
                    inspectedEnemyUnit = null;
                    SpawnClickFeedback(enemyBuilding.transform.position, Color.red);
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.WarHorn);
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.AttackOrder);
                    RTSAudioManager.Instance?.NotifyCombat();

                    var leadSiege = selectedUnits.Find(u => u != null && u.unitType == "trebuchet") 
                                 ?? selectedUnits.Find(u => u != null && u.unitType != "villager") 
                                 ?? selectedUnits.Find(u => u != null);
                    if (leadSiege != null) RTSAudioManager.Instance?.PlayUnitVoice(leadSiege.unitType, "attack", force: true);

                    RTSHUDController.Instance?.SetAiStatus($"[KUŞATMA] \"{enemyBuilding.displayName.ToUpper()} yerle bir edilecek! Hücum!\"", new Color(1f, 0.45f, 0.15f));
                    RTSNetworkSyncManager.Instance?.BroadcastAttackOrder(selectedUnits, enemyBuilding.transform.position, true);
                    foreach (var u in selectedUnits)
                    {
                        if (u != null && u.team == myTeam) u.AttackBuilding(enemyBuilding);
                    }
                    return;
                }

                // Rakip askeri taarruzu
                if (enemyUnit != null && enemyUnit.team != myTeam && enemyUnit.currentHp > 0)
                {
                    inspectedEnemyUnit = enemyUnit;
                    inspectedEnemyBuilding = null;
                    SpawnClickFeedback(enemyUnit.transform.position, Color.red);
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.WarHorn);
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.AttackOrder);
                    RTSAudioManager.Instance?.NotifyCombat();

                    var leadUnit = selectedUnits.Find(u => u != null && (u.unitType == "cavalry" || u.unitType == "scout")) 
                                ?? selectedUnits.Find(u => u != null && u.unitType != "villager") 
                                ?? selectedUnits.Find(u => u != null);
                    if (leadUnit != null) RTSAudioManager.Instance?.PlayUnitVoice(leadUnit.unitType, "attack", force: true);

                    RTSHUDController.Instance?.SetAiStatus($"[SALDIRI] \"Kılıçlar çekilsin! Zafer bizim olacak!\"", new Color(1f, 0.35f, 0.35f));
                    RTSNetworkSyncManager.Instance?.BroadcastAttackOrder(selectedUnits, enemyUnit.transform.position, false);
                    foreach (var u in selectedUnits)
                    {
                        if (u != null && u.team == myTeam) u.AttackTarget(enemyUnit);
                    }
                    return;
                }

                // 5. HAREKET EMRİ (Formasyonlu Dağılım)
                Vector3 destinationPoint = groundPos;
                if (hasPhysicalHit && hit.collider.GetComponentInParent<RTSUnit>() == null)
                {
                    destinationPoint = hit.point;
                }
                else if (!hitGroundPlane && hasPhysicalHit)
                {
                    destinationPoint = hit.point;
                }

                // Harita Sonu (Siyah Sınır) Koruması: Harita sınırı dışına tıklanırsa emniyetli sınıra sabitle
                if (Mathf.Abs(destinationPoint.x) > RTSUnit.PlayableMapLimit || Mathf.Abs(destinationPoint.z) > RTSUnit.PlayableMapLimit)
                {
                    destinationPoint.x = Mathf.Clamp(destinationPoint.x, -RTSUnit.PlayableMapLimit, RTSUnit.PlayableMapLimit);
                    destinationPoint.z = Mathf.Clamp(destinationPoint.z, -RTSUnit.PlayableMapLimit, RTSUnit.PlayableMapLimit);
                    RTSHUDController.Instance?.SetAiStatus("⚠️ [HARİTA SONU] Siyah sınır bölgesine geçilemez! Birlikler harita sınırında durduruluyor.", new Color(1.0f, 0.75f, 0.25f));
                }

                // Nehirli Harita Derin Su Koruması (İşaretlenen yer nehirse suya girilmesini önle)
                bool isRiverClick = RTSAutoBootstrap.HasRiver && (
                    (hasPhysicalHit && hit.collider != null && hit.collider.name.Contains("River_Water_Blocker")) ||
                    RTSUnit.IsDeepWater(destinationPoint)
                );

                if (isRiverClick)
                {
                    // Seçili birliklerin ağırlık merkezini bul
                    Vector3 groupCenter = Vector3.zero;
                    int validCount = 0;
                    foreach (var u in selectedUnits)
                    {
                        if (u != null) { groupCenter += u.transform.position; validCount++; }
                    }
                    if (validCount > 0) groupCenter /= validCount;

                    bool unitsAreSouth = groupCenter.z < 0;

                    // Eğer köprü veya sığ geçit girişine çok yakın tıklandıysa geçiş noktasına yönlendir,
                    // aksi takdirde birimlerin bulunduğu taraftaki emniyetli nehir kıyısına (sahile) sabitle.
                    if (Mathf.Abs(destinationPoint.x - (-28f)) <= 7.0f)
                    {
                        destinationPoint = new Vector3(-28f, 0, unitsAreSouth ? -9.5f : 9.5f);
                    }
                    else if (Mathf.Abs(destinationPoint.x - 28f) <= 7.0f)
                    {
                        destinationPoint = new Vector3(28f, 0, unitsAreSouth ? -9.5f : 9.5f);
                    }
                    else if (Mathf.Abs(destinationPoint.x - 0f) <= 5.5f)
                    {
                        destinationPoint = new Vector3(0f, 0, unitsAreSouth ? -7.5f : 7.5f);
                    }
                    else
                    {
                        destinationPoint = new Vector3(destinationPoint.x, 0, unitsAreSouth ? -7.2f : 7.2f);
                    }

                    // Nehir uyarısı görseli ve bildirimi
                    SpawnClickFeedback(destinationPoint, new Color(1.0f, 0.75f, 0.20f));

                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.Click);
                    if (RTSHUDController.Instance != null)
                    {
                        RTSHUDController.Instance.SetAiStatus("⚠️ [AŞILMAZ NEHİR] Derin nehir suyu geçilemez! Birlikler nehir kıyısında duruyor. Geçiş için taş köprüleri (-28m / +28m) veya sığ geçidi (0m) kullanın.", new Color(1.0f, 0.80f, 0.25f));
                    }
                }
                else
                {
                    SpawnClickFeedback(destinationPoint, Color.green);

                    bool onlyVillagers = selectedUnits.TrueForAll(u => u != null && u.unitType == "villager");
                    bool hasCavalry = selectedUnits.Exists(u => u != null && (u.unitType == "cavalry" || u.unitType == "scout"));
                    if (hasCavalry)
                    {
                        RTSAudioManager.Instance?.PlaySound(RTSSoundType.MoveOrderCavalry);
                    }
                    else
                    {
                        RTSAudioManager.Instance?.PlaySound(RTSSoundType.MoveOrder);
                    }

                    var leadMover = selectedUnits.Find(u => u != null && (onlyVillagers ? u.unitType == "villager" : u.unitType != "villager"))
                                 ?? selectedUnits.Find(u => u != null);
                    if (leadMover != null)
                    {
                        RTSAudioManager.Instance?.PlayUnitVoice(leadMover.unitType, "move");
                    }

                    if (RTSHUDController.Instance != null)
                    {
                        if (onlyVillagers)
                        {
                            RTSHUDController.Instance.SetAiStatus("[KÖYLÜ İNTİKALİ] \"Yola çıktım efendim, oraya gidiyorum.\"", Color.cyan);
                        }
                        else
                        {
                            RTSHUDController.Instance.SetAiStatus("[İNTİKAL] \"Derhal efendim! Askerler intikale geçiyor.\"", Color.cyan);
                        }
                    }
                }

                // Eğer seçili birimler arasında köylü varsa ve zemine hareket emri verildiyse önceki toplama/inşaat görevini iptal et
                foreach (var u in selectedUnits)
                {
                    if (u != null)
                    {
                        var v = u.GetComponent<RTSVillager>();
                        if (v != null) v.CancelTask();
                    }
                }

                DistributeMoveOrder(destinationPoint);
            }
        }

        /// <summary>
        /// F1 Tuşu ile doğrudan fare imlecinin olduğu bölgeye genel taarruz emri (Attack-Move) verir. (A tuşu kamera WASD ile çakışmaz)
        /// </summary>
        private void HandleKeyboardAttackOrders()
        {
            if (Input.GetKeyDown(KeyCode.F1) && selectedUnits.Count > 0)
            {
                var combatUnits = selectedUnits.FindAll(u => u != null && u.unitType != "villager" && u.currentHp > 0);
                if (combatUnits.Count == 0) return;

                Camera cam = Camera.main;
                if (cam == null) return;

                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
                Vector3 targetPos = Vector3.zero;
                if (groundPlane.Raycast(ray, out float enterDist))
                {
                    targetPos = ray.GetPoint(enterDist);
                }
                else if (Physics.Raycast(ray, out RaycastHit hit, 300f))
                {
                    targetPos = hit.point;
                }

                if (targetPos != Vector3.zero)
                {
                    if (RTSAutoBootstrap.HasRiver && RTSUnit.IsDeepWater(targetPos))
                    {
                        Vector3 groupCenter = Vector3.zero;
                        int validCount = 0;
                        foreach (var u in combatUnits) { if (u != null) { groupCenter += u.transform.position; validCount++; } }
                        if (validCount > 0) groupCenter /= validCount;
                        targetPos = new Vector3(targetPos.x, 0, (groupCenter.z < 0) ? -7.2f : 7.2f);
                    }

                    SpawnClickFeedback(targetPos, new Color(1f, 0.2f, 0.2f));
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.WarHorn);
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.AttackOrder);
                    RTSAudioManager.Instance?.NotifyCombat();

                    var lead = combatUnits.Find(u => u.unitType == "cavalry" || u.unitType == "scout") ?? combatUnits[0];
                    RTSAudioManager.Instance?.PlayUnitVoice(lead.unitType, "attack", force: true);

                    DistributeMoveOrder(targetPos);
                    RTSHUDController.Instance?.SetAiStatus($"[F1 TAARRUZ YÜRÜYÜŞÜ] {combatUnits.Count} muharip birliğimiz taarruza geçti!", Color.red);
                }
            }
        }

        /// <summary>
        /// Tüm askerleri, tüm köylüleri veya boştaki işçileri seçen klavye kısayolları.
        /// </summary>
        private void HandleSelectionHotkeys()
        {
            // F2 veya Tab: Tüm Askerleri Seç (Select All Military)
            if (Input.GetKeyDown(KeyCode.F2) || Input.GetKeyDown(KeyCode.Tab))
            {
                SelectAllMilitaryUnits();
            }

            // F3 veya V: Tüm Köylüleri Seç (Select All Villagers)
            if (Input.GetKeyDown(KeyCode.F3) || Input.GetKeyDown(KeyCode.V))
            {
                SelectAllVillagers();
            }

            // F4 veya Period (.): Boştaki Köylüyü Seç (Select Idle Villager)
            if (Input.GetKeyDown(KeyCode.F4) || Input.GetKeyDown(KeyCode.Period))
            {
                SelectNextIdleVillager();
            }

            // Seçili bina klavye kısayolları (Şehir Merkezi seçiliyken Q ile Köylü; Kışla seçiliyken 1..5 ile asker basma)
            if (selectedBuilding != null && selectedBuilding.team == RTSGameManager.LocalPlayerTeam)
            {
                Vector3 fwd = (Vector3.zero - selectedBuilding.transform.position);
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                else fwd.Normalize();

                if (selectedBuilding.buildingType == RTSBuildingType.TownCenter)
                {
                    if (Input.GetKeyDown(KeyCode.Q))
                    {
                        Vector3 spawnPos = selectedBuilding.transform.position + fwd * 5.0f;
                        RTSGameManager.Instance?.TrainSingleUnit("villager", spawnPos);
                    }
                }
                else if (selectedBuilding.buildingType == RTSBuildingType.Barracks)
                {
                    Vector3 spawnPos = selectedBuilding.transform.position + fwd * 5.5f;
                    if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Q)) RTSGameManager.Instance?.TrainSingleUnit("spearman", spawnPos);
                    else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.W)) RTSGameManager.Instance?.TrainSingleUnit("swordsman", spawnPos);
                    else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.E)) RTSGameManager.Instance?.TrainSingleUnit("archer", spawnPos);
                    else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.R)) RTSGameManager.Instance?.TrainSingleUnit("trebuchet", spawnPos);
                    else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.T)) RTSGameManager.Instance?.TrainSingleUnit("cavalry", spawnPos);
                }
            }
        }

        public void SelectAllMilitaryUnits()
        {
            DeselectAll();
            UnitTeam myTeam = RTSGameManager.LocalPlayerTeam;
            var combatUnits = RTSUnit.AllUnits.FindAll(u => u != null && u.team == myTeam && u.unitType.ToLower() != "villager" && u.currentHp > 0);
            if (combatUnits.Count > 0)
            {
                foreach (var u in combatUnits)
                {
                    SelectUnit(u);
                }

                RTSAudioManager.Instance?.PlaySound(RTSSoundType.WarHorn);
                RTSAudioManager.Instance?.PlaySound(RTSSoundType.UnitSelect);
                var lead = combatUnits.Find(u => u.unitType == "cavalry" || u.unitType == "scout") ?? combatUnits[0];
                RTSAudioManager.Instance?.PlayUnitVoice(lead.unitType, "select", force: true);

                RTSHUDController.Instance?.SetAiStatus($"[TÜM ASKERLER SEÇİLDİ] {combatUnits.Count} savaşçı hazır! Taarruz emri bekleniyor.", Color.cyan);
            }
            else
            {
                RTSHUDController.Instance?.SetAiStatus("Sahada görevli savaşçı bulunamadı. Kışladan yeni asker eğitin!", Color.yellow);
            }
        }

        public void SelectAllVillagers()
        {
            DeselectAll();
            UnitTeam myTeam = RTSGameManager.LocalPlayerTeam;
            var villagers = RTSUnit.AllUnits.FindAll(u => u != null && u.team == myTeam && u.unitType.ToLower() == "villager" && u.currentHp > 0);
            if (villagers.Count > 0)
            {
                foreach (var u in villagers)
                {
                    SelectUnit(u);
                }

                RTSAudioManager.Instance?.PlaySound(RTSSoundType.UnitSelect);
                RTSAudioManager.Instance?.PlayVoice("villager_select", force: true);

                RTSHUDController.Instance?.SetAiStatus($"[TÜM KÖYLÜLER SEÇİLDİ] {villagers.Count} işçi seçildi!", Color.green);
            }
            else
            {
                RTSHUDController.Instance?.SetAiStatus("Sahada köylü bulunamadı. Şehir Merkezinden yeni köylü üretin!", Color.yellow);
            }
        }

        public void SelectNextIdleVillager()
        {
            UnitTeam myTeam = RTSGameManager.LocalPlayerTeam;
            var villagers = RTSUnit.AllUnits.FindAll(u => u != null && u.team == myTeam && u.unitType.ToLower() == "villager" && u.currentHp > 0);
            foreach (var u in villagers)
            {
                var v = u.GetComponent<RTSVillager>();
                if (v != null && v.currentState == VillagerState.Idle)
                {
                    DeselectAll();
                    SelectUnit(u);
                    Camera.main?.GetComponent<RTSCameraController>()?.FocusOnWorldPosition(u.transform.position);
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.UnitSelect);
                    RTSAudioManager.Instance?.PlayVoice("villager_select", force: true);
                    RTSHUDController.Instance?.SetAiStatus("[BOŞTAKİ İŞÇİ SEÇİLDİ] İşçiye hemen kaynak veya inşaat görevi verin.", Color.yellow);
                    return;
                }
            }

            RTSHUDController.Instance?.SetAiStatus("[İŞÇİ RAPORU] Tüm köylüleriniz aktif çalışıyor! Boşta kalan işçi yok.", Color.green);
        }

        public void DistributeMoveOrder(Vector3 centerDestination)
        {
            selectedUnits.RemoveAll(u => u == null || u.currentHp <= 0);
            int count = selectedUnits.Count;
            if (count == 0) return;

            Debug.Log($"[RTSSelectionManager] {count} birime taktiksel ordu formasyonu intikal emri verildi -> {centerDestination}");

            var netMoves = new List<(RTSUnit unit, Vector3 dest)>();

            if (count == 1)
            {
                netMoves.Add((selectedUnits[0], centerDestination));
                RTSNetworkSyncManager.Instance?.BroadcastMoveOrders(netMoves);
                selectedUnits[0].MoveTo(centerDestination, isManual: true);
                return;
            }

            // 1. Ordu Merkezini ve İntikal Doğrultusunu Hesapla (Direction-Aligned Formation)
            Vector3 groupCenter = Vector3.zero;
            foreach (var u in selectedUnits)
            {
                groupCenter += u.transform.position;
            }
            groupCenter /= count;

            Vector3 marchDir = centerDestination - groupCenter;
            marchDir.y = 0;
            if (marchDir.sqrMagnitude < 0.2f) marchDir = Vector3.forward;
            marchDir.Normalize();

            Vector3 rightDir = new Vector3(marchDir.z, 0, -marchDir.x);

            // 2. Birlikleri Askeri Rol Hiyerarşisine Göre Ayrıştır:
            // Ön Hat: Mızrakçılar & Kılıçlılar (Tanklar / Yakın Dövüş Siperi)
            // Arka Hat: Okçular & Arbaletçiler (Menzilli Ateş Gücü)
            // Kanatlar: Süvariler & Keşif Atlıları (Hızlı Şok ve Kuşatma Birlikleri)
            // Merkez Arka: Trebüşeler & Köylüler (Kuşatma Silahları & Sivil Destek)
            var frontline = new List<RTSUnit>();
            var backline = new List<RTSUnit>();
            var flanks = new List<RTSUnit>();
            var support = new List<RTSUnit>();

            foreach (var u in selectedUnits)
            {
                string t = u.unitType.ToLower();
                if (t == "spearman" || t == "swordsman") frontline.Add(u);
                else if (t == "archer" || t == "crossbowman") backline.Add(u);
                else if (t == "cavalry" || t == "scout") flanks.Add(u);
                else support.Add(u);
            }

            float spacingX = 2.4f;
            float spacingZ = 2.5f;
            int currentRow = 0;

            // 1. Kademe: Ön Hat (Piyadeler)
            if (frontline.Count > 0)
            {
                AssignRankFormation(frontline, centerDestination, marchDir, rightDir, currentRow, spacingX, spacingZ, netMoves);
                currentRow += Mathf.CeilToInt((float)frontline.Count / 6);
            }

            // 2. Kademe: Arka Hat (Okçular - Piyadelerin hemen arkasında korumalı ateş hattı)
            if (backline.Count > 0)
            {
                AssignRankFormation(backline, centerDestination, marchDir, rightDir, currentRow, spacingX, spacingZ, netMoves);
                currentRow += Mathf.CeilToInt((float)backline.Count / 6);
            }

            // 3. Kademe: Kuşatma & Destek (Mancınıklar ve Köylüler - En geride güvenli merkez)
            if (support.Count > 0)
            {
                AssignRankFormation(support, centerDestination, marchDir, rightDir, currentRow, spacingX + 0.8f, spacingZ + 0.8f, netMoves);
            }

            // 4. Kademe: Yan Kanatlar (Süvariler - Ordunun sol ve sağ kanatlarında hücum düzeni)
            if (flanks.Count > 0)
            {
                for (int i = 0; i < flanks.Count; i++)
                {
                    bool isRightFlank = (i % 2 == 1);
                    int flankIndex = i / 2;
                    float sideSign = isRightFlank ? 1f : -1f;
                    float lateralOffset = (3.8f + flankIndex * 2.0f) * sideSign;
                    float depthOffset = (flankIndex * 1.8f);

                    Vector3 flankDest = centerDestination + rightDir * lateralOffset - marchDir * depthOffset;
                    netMoves.Add((flanks[i], flankDest));
                    flanks[i].MoveTo(flankDest, isManual: true);
                }
            }

            RTSNetworkSyncManager.Instance?.BroadcastMoveOrders(netMoves);
        }

        private void AssignRankFormation(List<RTSUnit> units, Vector3 center, Vector3 forward, Vector3 right, int rankIndex, float spX, float spZ, List<(RTSUnit unit, Vector3 dest)> netMoves = null)
        {
            int maxCols = 6;
            for (int i = 0; i < units.Count; i++)
            {
                int col = i % maxCols;
                int subRow = i / maxCols;
                int totalInThisSubRow = Mathf.Min(maxCols, units.Count - subRow * maxCols);

                float xOff = (col - (totalInThisSubRow - 1) * 0.5f) * spX;
                float zOff = (rankIndex + subRow) * spZ;

                Vector3 dest = center + right * xOff - forward * zOff;
                netMoves?.Add((units[i], dest));
                units[i].MoveTo(dest, isManual: true);
            }
        }

        public void SpawnClickFeedback(Vector3 position, Color color)
        {
            RTSWaypointType type = RTSWaypointType.Move;
            if (color.r > 0.8f && color.g < 0.5f) type = RTSWaypointType.Attack;
            else if (color.r > 0.8f && color.g > 0.8f) type = RTSWaypointType.Gather;
            else if (color.b > 0.7f && color.g > 0.7f) type = RTSWaypointType.Construct;

            RTSVFXManager.Instance?.SpawnWaypointVFX(position, type);
        }

        public void SpawnClickFeedback(Vector3 position, RTSWaypointType type)
        {
            RTSVFXManager.Instance?.SpawnWaypointVFX(position, type);
        }

        #endregion

        #region Kutu Çizimi (GUI)

        private void OnGUI()
        {
            if (isDraggingBox)
            {
                Rect rect = GetScreenRect(mouseDragStart, Input.mousePosition);

                // İç Dolgu
                GUI.color = boxFillColor;
                GUI.DrawTexture(rect, whiteTexture);

                // Kenarlık Çizgileri
                GUI.color = boxBorderColor;
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, borderThickness), whiteTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.yMax - borderThickness, rect.width, borderThickness), whiteTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.y, borderThickness, rect.height), whiteTexture);
                GUI.DrawTexture(new Rect(rect.xMax - borderThickness, rect.y, borderThickness, rect.height), whiteTexture);

                GUI.color = Color.white;
            }
        }

        private Rect GetScreenRect(Vector2 screenPos1, Vector2 screenPos2)
        {
            screenPos1.y = Screen.height - screenPos1.y;
            screenPos2.y = Screen.height - screenPos2.y;

            Vector2 topLeft = Vector2.Min(screenPos1, screenPos2);
            Vector2 bottomRight = Vector2.Max(screenPos1, screenPos2);

            return Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
        }

        #endregion
    }
}
