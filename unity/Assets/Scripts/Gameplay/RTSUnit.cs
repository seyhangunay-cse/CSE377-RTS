using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CSE377.Gameplay
{
    public enum UnitTeam
    {
        Player,
        Enemy
    }

    /// <summary>
    /// Sahnedeki aktif RTS birliklerini (askerleri) temsil eder.
    /// Canlı muharebe, menzil kontrolü, taş-kağıt-makas karşıtlık hasarı, ok atışı ve can barlarını yönetir.
    /// </summary>
    public class RTSUnit : MonoBehaviour
    {
        public static readonly List<RTSUnit> AllUnits = new List<RTSUnit>();
        public const float PlayableMapLimit = 68f; // Harita sonu siyah sınır kuşağına girilmesini engeller (-68m .. +68m)

        [Header("Birim Özellikleri")]
        public string unitType = "spearman"; // "spearman", "archer", "cavalry"
        public UnitTeam team = UnitTeam.Player;
        public float moveSpeed = 3.5f;
        public int maxHp = 100;
        public int currentHp = 100;
        public int attack = 10;
        public int armor = 0;
        public float attackRange = 1.8f;
        public float detectionRadius = 10f;
        public float sightRadius = 11f;
        public float attackCooldown = 1.2f;

        [Header("Hedef ve İntikal")]
        public Vector3 targetDestination;
        public Vector3 commandDestination = Vector3.zero;
        public bool isMoving = false;
        public bool isManualMoveOrder = false;
        public RTSUnit currentAttackTarget;
        public RTSBuilding currentAttackBuilding;

        [Header("Seçim Durumu")]
        public bool isSelected = false;

        [Header("Savaş Sisi ve Keşif")]
        public bool isFogVisible = false;
        public bool isAutoScouting = false;
        public string scoutFocus = "";
        private float nextScoutCheckTime = 0f;
        private static float lastUnderAttackAlarmTime = -99f;

        private Renderer meshRenderer;
        private GameObject selectionIndicator;
        private float nextAttackTime = 0f;
        private float stuckCheckTimer = 0f;
        private float stuckAccumulatedTime = 0f;
        private Vector3 lastStuckPos;

        [Header("3D Animasyon ve Görsel")]
        public Transform visualChild;
        private Animator unitAnimator;
        private PlayableGraph playableGraph;
        private AnimationClip currentPlayingClip;
        private float walkCycleTime = 0f;
        private bool isAttackingAnimPlaying = false;

        private static AnimationClip knightIdleClip;
        private static AnimationClip knightWalkClip;
        private static AnimationClip knightAttackClip;
        private static AnimationClip knightDeathClip;
        private static bool clipsLoaded = false;

        private static void LoadClipsIfNeeded()
        {
            if (clipsLoaded) return;
            clipsLoaded = true;

            try
            {
                var allClips = Resources.LoadAll<AnimationClip>("Models/KnightCharacter");
                if (allClips != null && allClips.Length > 0)
                {
                    foreach (var c in allClips)
                    {
                        if (c == null) continue;
                        string n = c.name.ToLower();
                        if (n.Contains("idle") && knightIdleClip == null) knightIdleClip = c;
                        else if ((n.Contains("walk") || n.Contains("run")) && knightWalkClip == null) knightWalkClip = c;
                        else if (n.Contains("attack") && knightAttackClip == null) knightAttackClip = c;
                        else if (n.Contains("death") && knightDeathClip == null) knightDeathClip = c;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[RTSUnit] Animasyon klipleri yüklenirken uyarı: {ex.Message}");
            }
        }

        public void PlayClip(AnimationClip clip)
        {
            if (unitAnimator == null || clip == null || currentPlayingClip == clip) return;
            currentPlayingClip = clip;

            try
            {
                if (playableGraph.IsValid())
                {
                    playableGraph.Destroy();
                }
                AnimationPlayableUtilities.PlayClip(unitAnimator, clip, out playableGraph);
            }
            catch
            {
                // PlayableGraph desteği olmayan nesneler için sessizce devam et
            }
        }

        private void OnDestroy()
        {
            if (playableGraph.IsValid())
            {
                playableGraph.Destroy();
            }
        }

        private void OnEnable()
        {
            AllUnits.Add(this);
            RTSGameManager.Instance?.UpdateHUD();
        }

        private void OnDisable()
        {
            AllUnits.Remove(this);
            RTSGameManager.Instance?.UpdateHUD();
        }

        private void Awake()
        {
            currentHp = maxHp;
            targetDestination = transform.position;
            commandDestination = Vector3.zero;
            lastStuckPos = transform.position;
            meshRenderer = GetComponentInChildren<Renderer>();

            // Alt görsel modeli belirle
            if (visualChild == null)
            {
                var v = transform.Find("VisualModel");
                if (v != null)
                {
                    visualChild = v;
                }
                else
                {
                    for (int i = 0; i < transform.childCount; i++)
                    {
                        var child = transform.GetChild(i);
                        if (child.name != "SelectionIndicator")
                        {
                            visualChild = child;
                            break;
                        }
                    }
                }
            }

            unitAnimator = GetComponentInChildren<Animator>();
            LoadClipsIfNeeded();

            if (GetComponent<Collider>() == null)
            {
                var col = gameObject.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0, 1f, 0);
                col.radius = 0.5f;
                col.height = 2f;
            }

            var rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;
            rb.useGravity = false;

            SetupVisualAppearance();
            EnsurePhysics();
            CreateSelectionIndicator();
        }

        public void EnsurePhysics()
        {
            if (GetComponent<Collider>() == null)
            {
                var col = gameObject.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0, 1f, 0);
                col.radius = 0.45f;
                col.height = 2f;
            }

            var rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        private void Start()
        {
            EnsurePhysics();
            SetupVisualAppearance();
            ApplyTeamVisuals();
            RTSTechManager.Instance?.ApplyResearchedTechsToNewUnit(this);
            UnstuckFromBuildings();

            if (team != RTSGameManager.LocalPlayerTeam)
            {
                bool vis = (RTSFogOfWarManager.Instance != null) && RTSFogOfWarManager.Instance.IsUnitVisible(this);
                SetFogOfWarVisibility(vis);
            }
            else
            {
                SetFogOfWarVisibility(true);
            }
        }

        private void UnstuckFromBuildings()
        {
            Collider myCol = GetComponent<Collider>();
            if (myCol == null) return;

            foreach (var b in RTSBuilding.AllBuildings)
            {
                if (b != null)
                {
                    if (b.buildingType == RTSBuildingType.CityGate && b.team == this.team) continue;
                    Collider bCol = b.GetComponent<Collider>();
                    if (bCol != null && bCol.bounds.Intersects(myCol.bounds))
                    {
                        // Binanın önündeki (+Z) açık alana güvenle kaydır
                        float forwardOffset = bCol.bounds.extents.z + 1.5f;
                        Vector3 safeExit = new Vector3(transform.position.x, transform.position.y, b.transform.position.z + forwardOffset);
                        transform.position = safeExit;
                        targetDestination = safeExit;
                        break;
                    }
                }
            }
        }

        public void SetSelected(bool selected)
        {
            isSelected = selected;
            if (selectionIndicator != null)
            {
                selectionIndicator.SetActive(selected);
            }
            UpdateHealthBar();
        }

        /// <summary>
        /// Belirtilen koordinatın nehir üzerindeki geçilebilir bir köprüde (Batı/Doğu) veya sığ geçitte olup olmadığını belirler.
        /// </summary>
        public static bool IsCrossingPoint(Vector3 pos)
        {
            bool onWestBridge = Mathf.Abs(pos.x - (-28f)) <= 4.2f && Mathf.Abs(pos.z) <= 9.2f;
            bool onEastBridge = Mathf.Abs(pos.x - 28f) <= 4.2f && Mathf.Abs(pos.z) <= 9.2f;
            bool onShallows = Mathf.Abs(pos.x - 0f) <= 3.8f && Mathf.Abs(pos.z) <= 7.2f;
            return onWestBridge || onEastBridge || onShallows;
        }

        /// <summary>
        /// Belirtilen koordinatın geçit vermez derin nehir suyu alanında olup olmadığını kontrol eder.
        /// </summary>
        public static bool IsDeepWater(Vector3 pos)
        {
            if (!RTSAutoBootstrap.HasRiver) return false;
            if (Mathf.Abs(pos.z) >= 5.8f) return false;
            return !IsCrossingPoint(pos);
        }

        private readonly System.Collections.Generic.Queue<Vector3> moveWaypoints = new System.Collections.Generic.Queue<Vector3>();
        private RTSBuilding currentIgnoreBuilding;

        public void MoveTo(Vector3 destination, bool isManual = true, float forcedCrossingX = float.NaN, RTSBuilding ignoreBuilding = null)
        {
            if (currentHp <= 0 || isDead) return;

            EnsurePhysics();
            currentIgnoreBuilding = ignoreBuilding;

            float clampedX = Mathf.Clamp(destination.x, -PlayableMapLimit, PlayableMapLimit);
            float clampedZ = Mathf.Clamp(destination.z, -PlayableMapLimit, PlayableMapLimit);
            Vector3 targetPos = new Vector3(clampedX, transform.position.y, clampedZ);
            stuckCheckTimer = 0f;
            stuckAccumulatedTime = 0f;
            lastStuckPos = transform.position;

            currentAttackTarget = null;
            currentAttackBuilding = null;
            isManualMoveOrder = isManual;
            if (isManual) isAutoScouting = false;

            moveWaypoints.Clear();

            // 1. Nehirli Haritada Derin Su Kontrolü: Hedef doğrudan derin suyun içindeyse suya girilmesini engelle ve kıyıya sabitle
            if (RTSAutoBootstrap.HasRiver && IsDeepWater(targetPos))
            {
                float shoreZ = (transform.position.z < 0) ? -7.2f : 7.2f;
                targetPos = new Vector3(targetPos.x, targetPos.y, shoreZ);
            }

            // 2. Nehir Geçiş ve Köprü Rota Yönlendirmesi (Bridge & River Routing)
            float currZ = transform.position.z;
            float destZ = targetPos.z;

            bool currentIsCrossing = RTSAutoBootstrap.HasRiver && IsCrossingPoint(transform.position);
            bool destIsCrossing = RTSAutoBootstrap.HasRiver && IsCrossingPoint(targetPos);

            bool currentIsSouth = currZ < 0;
            bool currentIsNorth = currZ > 0;
            bool destIsSouth = destZ < 0;
            bool destIsNorth = destZ > 0;

            // Durum 1: Nehrin karşı kıyısına geçiş (Güney <-> Kuzey) - Yalnızca nehirli haritalarda
            if (RTSAutoBootstrap.HasRiver && !currentIsCrossing && !destIsCrossing &&
                ((currentIsSouth && destIsNorth) || (currentIsNorth && destIsSouth)))
            {
                float crossingX = 0f;
                if (!float.IsNaN(forcedCrossingX))
                {
                    crossingX = forcedCrossingX;
                }
                else
                {
                    float currX = transform.position.x;
                    float destX = targetPos.x;

                    float costWest = Mathf.Abs(currX - (-28f)) + Mathf.Abs(destX - (-28f));
                    float costCenter = Mathf.Abs(currX - 0f) + Mathf.Abs(destX - 0f) + 8f; // Taş köprüler öncelikli
                    float costEast = Mathf.Abs(currX - 28f) + Mathf.Abs(destX - 28f);

                    if (costWest <= costCenter && costWest <= costEast) crossingX = -28f;
                    else if (costEast <= costCenter && costEast <= costWest) crossingX = 28f;
                    else crossingX = 0f;
                }

                float rampOffset = (crossingX == 0f) ? 7.5f : 9.5f;
                float enterZ = currentIsSouth ? -rampOffset : rampOffset;
                float exitZ = currentIsSouth ? rampOffset : -rampOffset;

                Vector3 bridgeEnter = new Vector3(crossingX, transform.position.y, enterZ);
                Vector3 bridgeExit = new Vector3(crossingX, transform.position.y, exitZ);

                // Birimden köprü girişine kadar bina engellerini dolanarak git
                var leg1 = CalculateBuildingAvoidancePath(transform.position, bridgeEnter, ignoreBuilding);
                foreach (var p in leg1) moveWaypoints.Enqueue(p);

                // Köprü çıkış noktası
                moveWaypoints.Enqueue(bridgeExit);

                // Köprü çıkışından nihai hedefe kadar bina engellerini dolanarak git
                var leg2 = CalculateBuildingAvoidancePath(bridgeExit, targetPos, ignoreBuilding);
                foreach (var p in leg2) moveWaypoints.Enqueue(p);
            }
            // Durum 2: Karadan doğrudan köprünün / sığ geçidin üzerine tıklanması (Köprünün üstüne sağ tık emri)
            else if (RTSAutoBootstrap.HasRiver && destIsCrossing && !currentIsCrossing)
            {
                float bridgeX = (Mathf.Abs(targetPos.x - (-28f)) <= 4.2f) ? -28f : 
                                ((Mathf.Abs(targetPos.x - 28f) <= 4.2f) ? 28f : 0f);
                float rampOffset = (bridgeX == 0f) ? 7.5f : 9.5f;
                Vector3 rampPoint = new Vector3(bridgeX, transform.position.y, currentIsSouth ? -rampOffset : rampOffset);

                var leg1 = CalculateBuildingAvoidancePath(transform.position, rampPoint, ignoreBuilding);
                foreach (var p in leg1) moveWaypoints.Enqueue(p);

                moveWaypoints.Enqueue(targetPos);
            }
            // Durum 3: Köprü üzerinden karaya çıkış emri
            else if (RTSAutoBootstrap.HasRiver && currentIsCrossing && !destIsCrossing)
            {
                float currentBridgeX = (Mathf.Abs(transform.position.x - (-28f)) <= 4.2f) ? -28f : 
                                       ((Mathf.Abs(transform.position.x - 28f) <= 4.2f) ? 28f : 0f);
                float rampOffset = (currentBridgeX == 0f) ? 7.5f : 9.5f;
                Vector3 rampExit = new Vector3(currentBridgeX, transform.position.y, destIsSouth ? -rampOffset : rampOffset);

                moveWaypoints.Enqueue(rampExit);

                var leg2 = CalculateBuildingAvoidancePath(rampExit, targetPos, ignoreBuilding);
                foreach (var p in leg2) moveWaypoints.Enqueue(p);
            }
            // Durum 4: Aynı karada intikal (Örn: Üs içi hareket, inşaata gidiş, madene gidiş vb.)
            else
            {
                var fullPath = CalculateBuildingAvoidancePath(transform.position, targetPos, ignoreBuilding);
                foreach (var p in fullPath) moveWaypoints.Enqueue(p);
            }

            if (moveWaypoints.Count > 0)
            {
                targetDestination = moveWaypoints.Dequeue();
                commandDestination = targetPos;
                isMoving = true;
            }
            else
            {
                isMoving = false;
            }

            Debug.Log($"[RTSUnit] {unitType} intikale başladı -> Hedef: {targetPos} (Waypoint Sayısı: {moveWaypoints.Count + 1})");
        }

        /// <summary>
        /// Hedefe doğru intikal ederken aradaki tüm binaların (Şehir Merkezi, Kışla, Ev, Kale vb.)
        /// ve nehrin etrafından dolanacak en kısa A* yol noktalarını (Waypoints) hesaplar.
        /// </summary>
        private System.Collections.Generic.List<Vector3> CalculateBuildingAvoidancePath(Vector3 start, Vector3 target, RTSBuilding ignoreBuilding = null)
        {
            return RTSPathfinder.FindPath(start, target, this.team, ignoreBuilding);
        }

        private static float GetPathLength(Vector3 start, System.Collections.Generic.List<Vector3> corners, Vector3 end)
        {
            float len = 0;
            Vector3 prev = start;
            for (int i = 0; i < corners.Count; i++)
            {
                len += Vector3.Distance(prev, corners[i]);
                prev = corners[i];
            }
            len += Vector3.Distance(prev, end);
            return len;
        }

        private static bool LineIntersectsAABB(float x1, float z1, float x2, float z2, float minX, float maxX, float minZ, float maxZ, float centerX = 0f, float centerZ = 0f)
        {
            float dx = x2 - x1;
            float dz = z2 - z1;

            // Eğer başlangıç noktası binanın emniyet alanı içindeyse:
            // Birim binadan uzaklaşıyorsa bu binayı engel sayma!
            if (x1 >= minX && x1 <= maxX && z1 >= minZ && z1 <= maxZ)
            {
                float toCenterX = centerX - x1;
                float toCenterZ = centerZ - z1;
                float dot = dx * toCenterX + dz * toCenterZ;
                if (dot <= 0.05f)
                {
                    // Hedef bina merkezinden dışarı doğru, engelleme yok
                    return false;
                }
            }

            // Liang-Barsky / Slab segment-AABB kesişim testi
            float tmin = 0f;
            float tmax = 1f;

            if (Mathf.Abs(dx) < 1e-5f)
            {
                if (x1 < minX || x1 > maxX) return false;
            }
            else
            {
                float t1 = (minX - x1) / dx;
                float t2 = (maxX - x1) / dx;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                tmin = Mathf.Max(tmin, t1);
                tmax = Mathf.Min(tmax, t2);
                if (tmin > tmax) return false;
            }

            if (Mathf.Abs(dz) < 1e-5f)
            {
                if (z1 < minZ || z1 > maxZ) return false;
            }
            else
            {
                float t1 = (minZ - z1) / dz;
                float t2 = (maxZ - z1) / dz;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                tmin = Mathf.Max(tmin, t1);
                tmax = Mathf.Min(tmax, t2);
                if (tmin > tmax) return false;
            }

            return tmin <= tmax && tmax >= 0f && tmin <= 1f;
        }

        public void AttackTarget(RTSUnit target)
        {
            if (isDead || currentHp <= 0) return;

            var vil = GetComponent<RTSVillager>();
            if (vil != null) vil.CancelTask();

            currentAttackBuilding = null;
            isManualMoveOrder = false;
            isAutoScouting = false;

            if (target != null && target.currentHp > 0)
            {
                currentAttackTarget = target;
                Vector3 enemyPos = target.transform.position;
                float distanceToEnemy = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), new Vector3(enemyPos.x, 0, enemyPos.z));

                if (distanceToEnemy <= attackRange)
                {
                    moveWaypoints.Clear();
                    targetDestination = transform.position;
                    isMoving = false;
                }
                else
                {
                    MoveTo(enemyPos, isManual: false);
                    currentAttackTarget = target;
                }
            }
            else
            {
                currentAttackTarget = null;
            }

            stuckCheckTimer = 0f;
            stuckAccumulatedTime = 0f;
            lastStuckPos = transform.position;
        }

        public void AttackBuilding(RTSBuilding building)
        {
            if (isDead || currentHp <= 0 || building == null || building.team == this.team || building.currentHp <= 0)
            {
                return;
            }

            var vil = GetComponent<RTSVillager>();
            if (vil != null) vil.CancelTask();

            currentAttackTarget = null;
            isManualMoveOrder = false;
            isAutoScouting = false;

            Collider bCol = building.GetComponent<Collider>();
            Vector3 bPos = (bCol != null) ? bCol.ClosestPoint(transform.position) : building.transform.position;
            float distToBuilding = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), new Vector3(bPos.x, 0, bPos.z));
            float effectiveRange = (unitType == "cavalry" || unitType == "spearman") ? attackRange + 1.8f : attackRange + 1.2f;

            if (distToBuilding <= effectiveRange)
            {
                moveWaypoints.Clear();
                targetDestination = transform.position;
                isMoving = false;
                currentAttackBuilding = building;
            }
            else
            {
                MoveTo(bPos, isManual: false, forcedCrossingX: float.NaN, ignoreBuilding: building);
                currentAttackBuilding = building;
            }

            stuckCheckTimer = 0f;
            stuckAccumulatedTime = 0f;
            lastStuckPos = transform.position;
        }

        public void StartAutoScout(string focus = "")
        {
            isAutoScouting = true;
            scoutFocus = focus;
            currentAttackTarget = null;
            currentAttackBuilding = null;
            isManualMoveOrder = false;
            PickNextScoutDestination();
        }

        public void StopAutoScout()
        {
            isAutoScouting = false;
            scoutFocus = "";
        }

        public void StopMovementFromNetwork()
        {
            moveWaypoints.Clear();
            isMoving = false;
            commandDestination = Vector3.zero;
            targetDestination = transform.position;
        }

        private void PickNextScoutDestination()
        {
            if (RTSFogOfWarManager.Instance != null)
            {
                Vector3 target = RTSFogOfWarManager.Instance.GetNearestUnexploredPoint(transform.position, scoutFocus);
                if (target != Vector3.zero)
                {
                    MoveTo(target, isManual: false);
                    return;
                }
            }

            // Harita tamamen açıldıysa keşif modunu bitir
            isAutoScouting = false;
            if (team == UnitTeam.Player)
            {
                RTSHUDController.Instance?.SetAiStatus("GÖZCÜ RAPORU: Tüm harita başarıyla keşfedildi!", Color.green);
            }
        }

        private void Update()
        {
            if (isDead) return;

            // 0. Köprü, Sığ Geçit ve Zemin Yükseklik Takibi (Birimlerin ve seçim halkasının suya/rampaya gömülmemesi için)
            float curAbsZ = Mathf.Abs(transform.position.z);
            bool curOnWest = Mathf.Abs(transform.position.x - (-28f)) <= 3.8f;
            bool curOnEast = Mathf.Abs(transform.position.x - 28f) <= 3.8f;
            bool curOnShallows = Mathf.Abs(transform.position.x) <= 4.0f && curAbsZ <= 7.2f;
            float desiredY = 0f;
            if ((curOnWest || curOnEast) && curAbsZ <= 9.6f)
            {
                desiredY = (curAbsZ <= 6.5f) ? 1.08f : Mathf.Lerp(0.08f, 1.08f, Mathf.Clamp01((9.6f - curAbsZ) / 3.1f));
            }
            else if (curOnShallows)
            {
                desiredY = (curAbsZ <= 5.5f) ? 0.16f : Mathf.Lerp(0f, 0.16f, Mathf.Clamp01((7.2f - curAbsZ) / 1.7f));
            }
            if (Mathf.Abs(transform.position.y - desiredY) > 0.005f)
            {
                float smoothY = Mathf.MoveTowards(transform.position.y, desiredY, Time.deltaTime * 12.0f);
                transform.position = new Vector3(transform.position.x, smoothY, transform.position.z);
            }

            // 1. Hedef Durumu ve Otomatik Düşman Tespiti (Aggro)
            // Manuel intikal emrinde olan birimler oyuncunun emrine sadık kalır, düşmana kilitlenip emri bozmaz!
            if (!isManualMoveOrder)
            {
                if (currentAttackTarget != null && currentAttackTarget.currentHp <= 0)
                {
                    currentAttackTarget = null;
                }

                if (currentAttackBuilding != null && (currentAttackBuilding.currentHp <= 0 || currentAttackBuilding.team == this.team))
                {
                    currentAttackBuilding = null;
                }

                // Eğer birim boşta ve bir hedefe saldırmıyorsa, menzil içindeki en yakın düşmanı veya düşman savunma binasını tespit et
                if (currentAttackTarget == null && currentAttackBuilding == null && !isAutoScouting)
                {
                    bool isCombatUnit = unitType.ToLower() != "villager";
                    if (isCombatUnit)
                    {
                        currentAttackTarget = FindClosestEnemy();
                        if (currentAttackTarget == null)
                        {
                            currentAttackBuilding = FindClosestEnemyBuilding();
                        }
                    }
                }
            }

            // 2. Düşman Askerine Yönelme ve Saldırı Döngüsü
            if (currentAttackTarget != null && currentAttackTarget.currentHp > 0)
            {
                Vector3 enemyPos = currentAttackTarget.transform.position;
                float distanceToEnemy = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), new Vector3(enemyPos.x, 0, enemyPos.z));

                if (distanceToEnemy <= attackRange)
                {
                    // Saldırı menzilinde: Dur, düşmana dön ve vur
                    moveWaypoints.Clear();
                    isMoving = false;
                    Vector3 lookDir = (enemyPos - transform.position).normalized;
                    lookDir.y = 0;
                    if (lookDir != Vector3.zero)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 14f);
                    }

                    if (Time.time >= nextAttackTime)
                    {
                        PerformAttack(currentAttackTarget);
                        nextAttackTime = Time.time + attackCooldown;
                    }
                }
                else
                {
                    // Menzil dışında: Eğer ara geçiş noktaları (köprü / bina engeli dolanma) varsa onları takip et
                    if (moveWaypoints.Count > 0)
                    {
                        isMoving = true;
                    }
                    else
                    {
                        // Nehrin karşı yakasındaysa ve geçiş noktası kalmadıysa köprü rotası oluştur
                        bool crossRiverNeeded = RTSAutoBootstrap.HasRiver && 
                            !IsCrossingPoint(transform.position) && !IsCrossingPoint(enemyPos) &&
                            ((transform.position.z < 0 && enemyPos.z > 0) || (transform.position.z > 0 && enemyPos.z < 0));

                        if (crossRiverNeeded)
                        {
                            var targetUnit = currentAttackTarget;
                            MoveTo(enemyPos, isManual: false);
                            currentAttackTarget = targetUnit;
                        }
                        else
                        {
                            targetDestination = enemyPos;
                            isMoving = true;
                        }
                    }
                }
            }
            // 2.2 Düşman Binasına Yönelme ve Kuşatma Saldırısı (Siege)
            else if (currentAttackBuilding != null && currentAttackBuilding.currentHp > 0)
            {
                Collider bCol = currentAttackBuilding.GetComponent<Collider>();
                Vector3 bPoint = (bCol != null) ? bCol.ClosestPoint(transform.position) : currentAttackBuilding.transform.position;
                float distToBuilding = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), new Vector3(bPoint.x, 0, bPoint.z));

                float effectiveRange = (unitType == "cavalry" || unitType == "spearman") ? attackRange + 1.8f : attackRange + 1.2f;

                if (distToBuilding <= effectiveRange)
                {
                    moveWaypoints.Clear();
                    isMoving = false;
                    Vector3 lookDir = (currentAttackBuilding.transform.position - transform.position).normalized;
                    lookDir.y = 0;
                    if (lookDir != Vector3.zero)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 14f);
                    }

                    if (Time.time >= nextAttackTime)
                    {
                        PerformAttackOnBuilding(currentAttackBuilding);
                        nextAttackTime = Time.time + attackCooldown;
                    }
                }
                else
                {
                    if (moveWaypoints.Count > 0)
                    {
                        isMoving = true;
                    }
                    else
                    {
                        bool crossRiverNeeded = RTSAutoBootstrap.HasRiver && 
                            !IsCrossingPoint(transform.position) && !IsCrossingPoint(bPoint) &&
                            ((transform.position.z < 0 && bPoint.z > 0) || (transform.position.z > 0 && bPoint.z < 0));

                        if (crossRiverNeeded)
                        {
                            var targetBld = currentAttackBuilding;
                            MoveTo(bPoint, isManual: false);
                            currentAttackBuilding = targetBld;
                        }
                        else
                        {
                            targetDestination = bPoint;
                            isMoving = true;
                        }
                    }
                }
            }
            else
            {
                // Eğer bir hedefe saldırılmıyorsa:
                // Sırada takip edilecek bir waypoint varsa rotayı kesinlikle koru!
                // Yalnızca birim tamamen durduğunda ve bekleyen bir intikal emri varsa:
                if (!isMoving && moveWaypoints.Count == 0 && commandDestination != Vector3.zero)
                {
                    float distToCmd = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), new Vector3(commandDestination.x, 0, commandDestination.z));
                    if (distToCmd > 0.6f)
                    {
                        MoveTo(commandDestination, isManual: false);
                    }
                    else
                    {
                        commandDestination = Vector3.zero;
                    }
                }
            }

            // 2.9 Binalarla Fiziksel İç İçe Geçmeyi Önleme (Instant Depenetration)
            Collider myCol = GetComponent<Collider>();
            if (myCol != null)
            {
                foreach (var b in RTSBuilding.AllBuildings)
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;
                    if (b.buildingType == RTSBuildingType.CityGate && b.team == this.team)
                    {
                        Vector3 localUnitPos = b.transform.InverseTransformPoint(transform.position);
                        if (Mathf.Abs(localUnitPos.x) < 1.45f) continue;
                    }
                    Collider bCol = b.GetComponent<Collider>();
                    if (bCol != null && bCol.bounds.Intersects(myCol.bounds))
                    {
                        if (Physics.ComputePenetration(myCol, transform.position, transform.rotation,
                                                       bCol, b.transform.position, b.transform.rotation,
                                                       out Vector3 depenDir, out float depenDist))
                        {
                            depenDir.y = 0;
                            if (depenDir.sqrMagnitude > 0.001f)
                            {
                                depenDir.Normalize();
                                transform.position += depenDir * (depenDist + 0.10f);
                            }
                        }
                    }
                }
            }

            // 3. Fiziksel Hareket, Yumuşak Sürü Ayrışması (Soft Flocking) ve Akıllı Sıkışma Önleme
            if (isMoving)
            {
                Vector3 currentPos = transform.position;
                Vector3 targetFlat = new Vector3(targetDestination.x, currentPos.y, targetDestination.z);
                float distance = Vector3.Distance(currentPos, targetFlat);

                // Ara geçiş noktalarında (köprü girişi/çıkışı, bina köşesi) 0.65m akıcı geçiş.
                // Yalnızca son hedefte: Saldırı hedefi varsa menzil eşiği, yoksa 0.35m durma eşiği.
                float stopThreshold = (moveWaypoints.Count > 0) 
                    ? 0.65f 
                    : ((currentAttackTarget != null || currentAttackBuilding != null) ? attackRange * 0.9f : 0.35f);

                if (distance > stopThreshold)
                {
                    Vector3 moveDir = (targetFlat - currentPos).normalized;

                    // Yumuşak ayrışma: Sadece yanlara yumuşak kaçış sağla, hareketi geriye doğru asla engelleme!
                    Vector3 separation = GetSeparationVector();
                    Vector3 finalDir = moveDir;

                    if (separation != Vector3.zero)
                    {
                        // Hareketi engelleyen ters (geriye) kuvveti filtrele
                        float dot = Vector3.Dot(separation, moveDir);
                        if (dot < 0)
                        {
                            separation -= moveDir * dot;
                        }
                        finalDir = (moveDir + separation * 0.35f).normalized;
                    }

                    // Fiziksel Engel Kontrolü (Duvarlar, Ağaç Gövdeleri, Nehir Engelleyicileri, Binalar)
                    float stepDist = moveSpeed * Time.deltaTime;
                    Vector3 proposedDir = finalDir;

                    if (proposedDir != Vector3.zero)
                    {
                        Vector3 castOrigin = transform.position + Vector3.up * 0.6f;
                        float castRadius = 0.32f;

                        if (Physics.SphereCast(castOrigin, castRadius, proposedDir, out RaycastHit hit, stepDist + 0.35f))
                        {
                            if (hit.collider != null && !hit.collider.isTrigger)
                            {
                                var otherUnit = hit.collider.GetComponentInParent<RTSUnit>();
                                string colName = hit.collider.name;
                                bool isBridgeOrGround = colName.Contains("Bridge") || colName.Contains("Ground") || colName.Contains("Ramp") || colName.Contains("Terrain") || colName.Contains("Deck") || colName.Contains("Shallow");

                                if (otherUnit == null && !isBridgeOrGround)
                                {
                                    // Dost Şehir Kapısı Kontrolü: Dost kapılardan serbestçe geçilebilir!
                                    var b = hit.collider.GetComponentInParent<RTSBuilding>();
                                    bool isFriendlyGate = (b != null && b.buildingType == RTSBuildingType.CityGate && b.team == this.team);

                                    if (!isFriendlyGate)
                                    {
                                        // 1. Hedef İnşaat / Tamirat / Kuşatma Binası Kontrolü
                                        if (b != null && currentAttackBuilding == b)
                                        {
                                            isMoving = false;
                                            moveWaypoints.Clear();
                                            commandDestination = Vector3.zero;
                                            proposedDir = Vector3.zero;
                                            return;
                                        }

                                        var villager = GetComponent<RTSVillager>();
                                        if (villager != null && b != null && 
                                            (villager.currentBuildingTask == b || (villager.currentState == VillagerState.WalkingToBuild && villager.currentBuildingTask == b)))
                                        {
                                            // Köylü inşa edeceği binanın sınırına ulaştı! Doğrudan inşaata başlasın
                                            isMoving = false;
                                            commandDestination = Vector3.zero;
                                            villager.currentState = VillagerState.Building;
                                            proposedDir = Vector3.zero;
                                            return;
                                        }

                                        // 2. Akıllı Engel Teğeti ve Kayma (Fluid Obstacle Steering)
                                        Vector3 normal = hit.normal;
                                        normal.y = 0;
                                        if (normal.sqrMagnitude < 0.01f) normal = -proposedDir;
                                        normal.Normalize();

                                        // Engelin normaline dik teğetleri hesapla
                                        Vector3 tRight = Vector3.Cross(Vector3.up, normal).normalized;
                                        Vector3 tLeft = -tRight;

                                        // Hedefe en çok yaklaştıran teğeti seç
                                        Vector3 toTarget = (targetFlat - currentPos).normalized;
                                        Vector3 tangent = (Vector3.Dot(tRight, toTarget) >= Vector3.Dot(tLeft, toTarget)) ? tRight : tLeft;

                                        // Duvar yüzeyinden hafifçe dışarı iterek (0.30f) sürtünmeyi ve kafa atmayı önle
                                        proposedDir = (tangent * 0.90f + normal * 0.30f).normalized;
                                    }
                                }
                            }
                        }
                    }

                    transform.position += proposedDir * stepDist;

                    // Harita sonu sınır kelepçesi: Birimlerin harita dışındaki siyah boşluğa çıkmasını kesin olarak engeller
                    Vector3 clampedPos = transform.position;
                    clampedPos.x = Mathf.Clamp(clampedPos.x, -PlayableMapLimit, PlayableMapLimit);
                    clampedPos.z = Mathf.Clamp(clampedPos.z, -PlayableMapLimit, PlayableMapLimit);
                    transform.position = clampedPos;

                    if (proposedDir != Vector3.zero)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(proposedDir), Time.deltaTime * 14f);
                    }

                    // Dinamik Yürüme Animasyonu (Sınıfa Özel Zengin Prosedürel Ritim ve Asil Duruş)
                    if (visualChild != null && !isAttackingAnimPlaying)
                    {
                        string uType = (unitType ?? "").ToLower();
                        if (uType == "cavalry" || uType == "scout")
                        {
                            // Süvari Dörtnal Ritim Hareketi (Gallop cadence & pitch/roll)
                            walkCycleTime += Time.deltaTime * (moveSpeed * 2.5f);
                            float gallopBounce = Mathf.Abs(Mathf.Sin(walkCycleTime)) * 0.12f;
                            float gallopPitch = Mathf.Sin(walkCycleTime) * 3.5f;
                            float gallopRoll = Mathf.Cos(walkCycleTime * 0.5f) * 2.0f;
                            visualChild.localPosition = new Vector3(0, gallopBounce, 0);
                            visualChild.localRotation = Quaternion.Euler(gallopPitch, 0, gallopRoll);
                        }
                        else if (uType == "spearman")
                        {
                            // Mızrakçı Askeri Adım: Dimdik kalkan duruşu, hafif ileri adım eğimi ve ritmik kalça salınımı
                            walkCycleTime += Time.deltaTime * (moveSpeed * 2.8f);
                            float stepBounce = Mathf.Abs(Mathf.Sin(walkCycleTime)) * 0.07f;
                            float hipSway = Mathf.Sin(walkCycleTime * 0.5f) * 2.2f;
                            visualChild.localPosition = new Vector3(0, stepBounce, 0);
                            visualChild.localRotation = Quaternion.Euler(2.0f, 0, hipSway);
                        }
                        else if (uType == "swordsman")
                        {
                            // Kılıç Ustası Ağır Piyade Yürüyüşü: Göğüs salınımı ve omuz hareketi
                            walkCycleTime += Time.deltaTime * (moveSpeed * 2.8f);
                            float stepBounce = Mathf.Abs(Mathf.Sin(walkCycleTime)) * 0.065f;
                            float torsoRoll = Mathf.Cos(walkCycleTime * 0.5f) * 2.5f;
                            visualChild.localPosition = new Vector3(0, stepBounce, 0);
                            visualChild.localRotation = Quaternion.Euler(1.5f, 0, torsoRoll);
                        }
                        else if (uType == "archer" || uType == "crossbowman")
                        {
                            // Okçu / Arbaletçi: Çevik, temkinli ve hafif adımlama
                            walkCycleTime += Time.deltaTime * (moveSpeed * 3.0f);
                            float stepBounce = Mathf.Abs(Mathf.Sin(walkCycleTime)) * 0.05f;
                            float lightSway = Mathf.Sin(walkCycleTime * 0.5f) * 1.8f;
                            visualChild.localPosition = new Vector3(0, stepBounce, 0);
                            visualChild.localRotation = Quaternion.Euler(1.0f, 0, lightSway);
                        }
                        else if (uType == "trebuchet")
                        {
                            // Mancınık Ağır Ahşap Şasi Salınımı (Lumbering siege frame heave)
                            walkCycleTime += Time.deltaTime * (moveSpeed * 1.8f);
                            float heavyHeave = Mathf.Sin(walkCycleTime) * 0.035f;
                            visualChild.localPosition = new Vector3(0, Mathf.Abs(heavyHeave), 0);
                            visualChild.localRotation = Quaternion.Euler(heavyHeave * 15f, 0, 0);
                        }
                        else
                        {
                            // Köylü / Genel: Dengeli adım ve alet salınımı
                            walkCycleTime += Time.deltaTime * (moveSpeed * 2.8f);
                            float stepBounce = Mathf.Abs(Mathf.Sin(walkCycleTime)) * 0.065f;
                            float sway = Mathf.Sin(walkCycleTime * 0.5f) * 2.0f;
                            visualChild.localPosition = new Vector3(0, stepBounce, 0);
                            visualChild.localRotation = Quaternion.Euler(0, 0, sway);
                        }
                    }

                    // Sıkışma kontrolü ve akıllı engelden sıyrılma (Stuck Recovery)
                    stuckCheckTimer += Time.deltaTime;
                    if (stuckCheckTimer >= 1.0f)
                    {
                        float moved = Vector3.Distance(currentPos, lastStuckPos);
                        if (moved < 0.18f)
                        {
                            stuckAccumulatedTime += stuckCheckTimer;

                            // Eğer bir bina yüzeyine takılı kaldıysa, binanın etrafından dolanacak A* rotasını otomatik yeniden hesapla!
                            if (stuckAccumulatedTime >= 1.2f)
                            {
                                var savedTargetUnit = currentAttackTarget;
                                var savedTargetBld = currentAttackBuilding;
                                Vector3 finalDest = (commandDestination != Vector3.zero) ? commandDestination : targetDestination;
                                MoveTo(finalDest, isManual: isManualMoveOrder, forcedCrossingX: float.NaN, ignoreBuilding: currentIgnoreBuilding ?? savedTargetBld);
                                currentAttackTarget = savedTargetUnit;
                                currentAttackBuilding = savedTargetBld;
                                stuckAccumulatedTime = 0f;
                            }
                            else
                            {
                                Vector3 toTarget = (targetDestination - currentPos);
                                toTarget.y = 0;
                                if (toTarget.sqrMagnitude > 0.1f)
                                {
                                    Vector3 perp = Vector3.Cross(Vector3.up, toTarget.normalized);
                                    float sign = (Random.value > 0.5f) ? 1f : -1f;
                                    transform.position += perp * (sign * 0.45f);
                                }
                            }

                            if (stuckAccumulatedTime >= 3.6f)
                            {
                                var v = GetComponent<RTSVillager>();
                                if (v != null && v.currentState == VillagerState.WalkingToBuild && v.currentBuildingTask != null)
                                {
                                    Collider bCol = v.currentBuildingTask.GetComponent<Collider>();
                                    float distToBld = (bCol != null) 
                                        ? Vector3.Distance(transform.position, bCol.ClosestPoint(transform.position)) 
                                        : Vector3.Distance(transform.position, v.currentBuildingTask.transform.position);

                                    if (distToBld <= 2.2f)
                                    {
                                        isMoving = false;
                                        commandDestination = Vector3.zero;
                                        v.currentState = VillagerState.Building;
                                        stuckAccumulatedTime = 0f;
                                        return;
                                    }
                                }

                                isMoving = false;
                                isManualMoveOrder = false;
                                stuckAccumulatedTime = 0f;
                            }
                        }
                        else
                        {
                            stuckAccumulatedTime = 0f;
                        }
                        lastStuckPos = currentPos;
                        stuckCheckTimer = 0f;
                    }
                }
                else
                {
                    // Sırada bir sonraki geçiş noktası (Waypoint - örneğin köprü çıkışı veya nihai hedef) var mı?
                    if (moveWaypoints.Count > 0)
                    {
                        targetDestination = moveWaypoints.Dequeue();
                        isMoving = true;
                        stuckCheckTimer = 0f;
                        stuckAccumulatedTime = 0f;
                        lastStuckPos = transform.position;
                    }
                    else
                    {
                        isMoving = false;
                        isManualMoveOrder = false;
                        stuckCheckTimer = 0f;
                        stuckAccumulatedTime = 0f;
                        commandDestination = Vector3.zero;

                        // Durma / Muharebe veya Rölanti (Idle) Animasyonu
                        if (currentAttackTarget != null || currentAttackBuilding != null)
                        {
                            PlayClip(knightAttackClip ?? knightIdleClip);
                        }
                        else
                        {
                            PlayClip(knightIdleClip);
                            if (visualChild != null && !isAttackingAnimPlaying)
                            {
                                visualChild.localPosition = Vector3.Lerp(visualChild.localPosition, Vector3.zero, Time.deltaTime * 6f);
                                visualChild.localRotation = Quaternion.Lerp(visualChild.localRotation, Quaternion.identity, Time.deltaTime * 6f);
                            }
                        }

                        // Keşif modundaysa yeni sisli noktayı seç
                        if (isAutoScouting && Time.time >= nextScoutCheckTime)
                        {
                            nextScoutCheckTime = Time.time + 0.6f;
                            PickNextScoutDestination();
                        }
                    }
                }
            }
        }

        private Vector3 GetSeparationVector()
        {
            Vector3 push = Vector3.zero;
            float separationRadius = 1.1f;

            foreach (var other in AllUnits)
            {
                if (other != null && other != this && other.currentHp > 0)
                {
                    Vector3 diff = transform.position - other.transform.position;
                    diff.y = 0;
                    float d = diff.sqrMagnitude;
                    if (d > 0.0001f && d < separationRadius * separationRadius)
                    {
                        float dist = Mathf.Sqrt(d);
                        push += (diff / dist) * ((separationRadius - dist) / separationRadius);
                    }
                }
            }

            return push.normalized;
        }

        private RTSUnit FindClosestEnemy()
        {
            RTSUnit closest = null;
            float minDistance = detectionRadius;
            UnitTeam enemyTeam = (team == UnitTeam.Player) ? UnitTeam.Enemy : UnitTeam.Player;

            foreach (var u in AllUnits)
            {
                if (u != null && u != this && u.team == enemyTeam && u.currentHp > 0)
                {
                    float dist = Vector3.Distance(transform.position, u.transform.position);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closest = u;
                    }
                }
            }

            return closest;
        }

        private RTSBuilding FindClosestEnemyBuilding()
        {
            RTSBuilding closest = null;
            float minDistance = detectionRadius + 2.5f;
            UnitTeam enemyTeam = (team == UnitTeam.Player) ? UnitTeam.Enemy : UnitTeam.Player;

            foreach (var b in RTSBuilding.AllBuildings)
            {
                if (b != null && b.team == enemyTeam && b.currentHp > 0)
                {
                    Collider bCol = b.GetComponent<Collider>();
                    Vector3 bPoint = (bCol != null) ? bCol.ClosestPoint(transform.position) : b.transform.position;
                    float dist = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), new Vector3(bPoint.x, 0, bPoint.z));
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closest = b;
                    }
                }
            }

            return closest;
        }

        private void PerformAttack(RTSUnit enemy)
        {
            PlayClip(knightAttackClip);
            RTSAudioManager.Instance?.NotifyCombat();

            string t = (unitType ?? "").ToLower();
            if (t == "archer" || t == "crossbowman")
            {
                // Menzilli Saldırı: Parabolik ok fırlat ve yay geri tepme animasyonu
                StartCoroutine(RangedRecoilCoroutine());
                RTSProjectile.FireArrow(transform.position, enemy, attack, this, this.team);
                RTSAudioManager.Instance?.PlaySound(RTSSoundType.ArrowShoot);
            }
            else if (t == "trebuchet")
            {
                // Kuşatma Silahı: Parabolik alevli dev taş gülle ve şasi fırlatma sarsıntısı
                StartCoroutine(TrebuchetLaunchCoroutine());
                RTSProjectile.FireSiegeBoulder(transform.position, enemy, attack, this, this.team);
                RTSAudioManager.Instance?.PlaySound(RTSSoundType.SiegeLaunch);
            }
            else
            {
                int damage = CalculateCounterDamage(enemy);
                ApplyCombatDamage(enemy, damage);

                // Yakın dövüş sınıfa özel hamle atağı, vuruş efekti ve kılıç çarpışma sesi
                StartCoroutine(AttackLungeCoroutine());
                SpawnSlashEffect(enemy.transform.position + Vector3.up * 1f);
                RTSAudioManager.Instance?.PlaySound(RTSSoundType.SwordClash);
            }
        }

        private void PerformAttackOnBuilding(RTSBuilding building)
        {
            if (building == null || building.currentHp <= 0 || building.team == this.team)
            {
                currentAttackBuilding = null;
                return;
            }

            RTSAudioManager.Instance?.NotifyCombat();
            PlayClip(knightAttackClip);

            Collider bCol = building.GetComponent<Collider>();
            Vector3 hitPoint = (bCol != null) ? bCol.ClosestPoint(transform.position) : building.transform.position;
            hitPoint.y += 1.2f;

            string t = (unitType ?? "").ToLower();
            if (t == "archer" || t == "crossbowman")
            {
                int siegeDmg = Mathf.Max(2, attack / 2);
                StartCoroutine(RangedRecoilCoroutine());
                RTSProjectile.FireArrowAtBuilding(transform.position, building, siegeDmg, this, this.team);
                RTSAudioManager.Instance?.PlaySound(RTSSoundType.ArrowShoot);
            }
            else if (t == "trebuchet")
            {
                int siegeDmg = attack * 4;
                StartCoroutine(TrebuchetLaunchCoroutine());
                RTSProjectile.FireSiegeBoulderAtBuilding(transform.position, building, siegeDmg, this, this.team);
                RTSAudioManager.Instance?.PlaySound(RTSSoundType.SiegeLaunch);
            }
            else
            {
                int siegeDmg = attack;
                if (t == "cavalry")
                {
                    siegeDmg = Mathf.RoundToInt(attack * 1.25f);
                }
                StartCoroutine(AttackLungeCoroutine());
                RTSAudioManager.Instance?.PlaySound(RTSSoundType.SwordClash);
                building.TakeDamage(siegeDmg, this.team);
                SpawnSlashEffect(hitPoint);
            }
        }

        /// <summary>
        /// RAG Bilgi Bankasındaki taş-kağıt-makas karşıtlık kurallarına göre bonus hasar hesaplar.
        /// </summary>
        public int CalculateCounterDamage(RTSUnit target)
        {
            int finalDmg = attack;
            string myType = unitType.ToLower();
            string targetType = target.unitType.ToLower();

            // Kural 1: Mızrakçı > Süvari (Bonus +18 hasar)
            if (myType == "spearman" && targetType == "cavalry")
            {
                finalDmg += 18;
                Debug.Log($"[KRİTİK KARŞITLIK] Mızrakçı süvariye karşı bonus hasar vurdu! Hasar: {finalDmg}");
            }
            // Kural 2: Süvari > Okçu (Bonus +12 hasar)
            else if (myType == "cavalry" && targetType == "archer")
            {
                finalDmg += 12;
            }
            // Kural 3: Okçu > Mızrakçı (Bonus +8 hasar)
            else if (myType == "archer" && targetType == "spearman")
            {
                finalDmg += 8;
            }

            return finalDmg;
        }

        public void ApplyCombatDamage(RTSUnit target, int damage)
        {
            if (target != null)
            {
                target.TakeDamage(damage, this);
            }
        }

        public void TakeDamage(int damage, RTSUnit attacker = null)
        {
            if (isDead || currentHp <= 0) return;

            int effectiveDamage = Mathf.Max(1, damage - armor);
            currentHp = Mathf.Max(0, currentHp - effectiveDamage);
            UpdateHealthBar();

            // Yüzen Savaş Hasar Metni (Floating Combat Text)
            bool isCrit = (damage >= 25);
            string txt = isCrit ? $"KRİTİK -{effectiveDamage}!" : $"-{effectiveDamage}";
            Color txtColor = isCrit ? new Color(1.0f, 0.55f, 0.15f) : (team == UnitTeam.Enemy ? new Color(1f, 0.95f, 0.85f) : new Color(1f, 0.35f, 0.35f));
            RTSVFXManager.Instance?.SpawnCombatText(transform.position, txt, txtColor);

            // Meşru Müdafaa ve Misilleme (Retaliation)
            if (attacker != null && attacker.currentHp > 0 && currentHp > 0)
            {
                // Oyuncunun manuel intikal emri YOKSA ve birim boşta ise saldırana karşılık ver
                if (!isManualMoveOrder && (currentAttackTarget == null || currentAttackTarget.currentHp <= 0))
                {
                    AttackTarget(attacker);
                }

                // Yakındaki boşta duran müttefikleri (14 metre) yardıma çağır
                AlertNearbyAllies(attacker);
            }

            // Hasar yeme görsel tepkisi (Hafif kırmızı parlama)
            if (meshRenderer != null)
            {
                StartCoroutine(DamageFlashCoroutine());
            }

            // Oyuncu birimi hasar aldığında meşru alarm ve mini-harita uyarısı
            if (team == RTSGameManager.LocalPlayerTeam)
            {
                RTSMinimapController.Instance?.AddPing(transform.position, new Color32(255, 60, 60, 255), 4.5f);

                if (Time.time >= lastUnderAttackAlarmTime + 6f)
                {
                    lastUnderAttackAlarmTime = Time.time;
                    RTSHUDController.Instance?.SetAiStatus("TEHLİKE! Birlikleriniz saldırı altında! (Boşluk Tuşu: Olay Yerine Git)", Color.red);
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.AlarmHorn);
                }
            }

            // Çok oyunculu modda birim hasarını ve ölümünü karşı oyuncuya anında senkronize et
            RTSNetworkSyncManager.Instance?.BroadcastUnitHpSync(this, currentHp <= 0);

            if (currentHp <= 0)
            {
                Die(fromNetwork: false);
            }
        }

        public void ApplyNetworkHp(int syncedHp)
        {
            if (isDead) return;
            if (syncedHp < currentHp)
            {
                int diff = currentHp - Mathf.Max(0, syncedHp);
                currentHp = Mathf.Max(0, syncedHp);
                UpdateHealthBar();
                if (diff > 0)
                {
                    RTSVFXManager.Instance?.SpawnCombatText(transform.position, $"-{diff}", new Color(1f, 0.35f, 0.35f));
                }
            }
            if (currentHp <= 0 || syncedHp <= 0)
            {
                currentHp = 0;
                Die(fromNetwork: true);
            }
        }

        private void AlertNearbyAllies(RTSUnit attacker)
        {
            if (attacker == null || attacker.currentHp <= 0) return;

            foreach (var ally in AllUnits)
            {
                if (ally != null && ally != this && ally.team == this.team && ally.currentHp > 0)
                {
                    if (ally.unitType.ToLower() == "villager") continue; // Köylüleri savaşa çağırma

                    // Sadece boşta duran müttefikler yardıma koşsun, oyuncunun manuel intikal emrindeki askerler bozulmasın!
                    if (!ally.isManualMoveOrder && ally.currentAttackTarget == null && !ally.isAutoScouting)
                    {
                        float dist = Vector3.Distance(ally.transform.position, transform.position);
                        if (dist <= 14f)
                        {
                            ally.AttackTarget(attacker);
                        }
                    }
                }
            }
        }

        private System.Collections.IEnumerator DamageFlashCoroutine()
        {
            Color orig = (team == UnitTeam.Player) ? new Color(0.2f, 0.6f, 1f) : new Color(0.9f, 0.2f, 0.2f);
            if (meshRenderer != null) meshRenderer.material.color = Color.white;
            yield return new WaitForSeconds(0.08f);
            if (meshRenderer != null) meshRenderer.material.color = orig;
        }

        public bool isDead { get; private set; } = false;

        private void Die(bool fromNetwork = false)
        {
            if (isDead) return;
            isDead = true;

            if (!fromNetwork)
            {
                RTSNetworkSyncManager.Instance?.BroadcastUnitHpSync(this, forceZero: true);
            }

            // 1. Tüm hareketi ve komutları anında durdur
            isMoving = false;
            moveWaypoints.Clear();
            commandDestination = Vector3.zero;
            targetDestination = transform.position;
            currentAttackTarget = null;
            currentAttackBuilding = null;
            isAutoScouting = false;
            isManualMoveOrder = false;
            isAttackingAnimPlaying = false;

            // Eğer köylü ise iş/inşaat görevini sonlandır ve bileşeni kapat
            var villager = GetComponent<RTSVillager>();
            if (villager != null)
            {
                villager.CancelTask();
                villager.enabled = false;
            }

            // Seçim yöneticisinden anında düşür
            RTSSelectionManager.Instance?.DeselectUnit(this);

            Debug.Log($"[{team} {unitType}] Birimi muharebede elendi.");

            // 2. Birimi anında aktif listeden çıkar ve seçim/can göstergelerini kapat
            AllUnits.Remove(this);
            if (selectionIndicator != null) selectionIndicator.SetActive(false);
            if (overheadHealthBarRoot != null) overheadHealthBarRoot.SetActive(false);

            // 3. Fiziksel collider'ı hemen kapat (artık tıklanamaz ve diğer birimlerin yolunu tıkamaz)
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;

            // Animator varsa yürüyüş/koşma döngüsünü durdurmak için kapat
            if (unitAnimator != null) unitAnimator.enabled = false;

            // 4. Oyun yöneticisine bildir (Nüfus ve kayıp istatistiği anında güncellensin)
            var gameMgr = RTSGameManager.Instance;
            if (gameMgr != null)
            {
                gameMgr.OnUnitDied(this);
            }

            // 5. Kılıç çarpışması / askeri zırh düşüş sesi
            RTSAudioManager.Instance?.PlaySound(RTSSoundType.DefeatSound, 0.18f);

            // 6. Mevcut yürüyen diğer tüm coroutine'leri durdur (lunge, flash vb.) ve yere yığılmayı başlat
            StopAllCoroutines();
            StartCoroutine(CasualtyCollapseCoroutine());
        }

        private IEnumerator CasualtyCollapseCoroutine()
        {
            float elapsed = 0f;
            float fallDuration = 0.45f;

            Transform fallTarget = (visualChild != null) ? visualChild : transform;
            Quaternion startRot = fallTarget.localRotation;
            Vector3 startPos = fallTarget.localPosition;

            // Karakter yere yığılma açısı (yaklaşık 82 derece yere yapışma)
            float pitchDir = (Random.value > 0.5f) ? -82f : 82f;
            Quaternion targetFallRot = Quaternion.Euler(pitchDir, Random.Range(-25f, 25f), Random.Range(-15f, 15f));
            Vector3 targetFallPos = new Vector3(startPos.x, 0.08f, startPos.z + (pitchDir > 0 ? 0.35f : -0.35f));

            while (elapsed < fallDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fallDuration);
                float easeT = t * t; // Doğal yerçekimi ivmesi

                if (fallTarget != null)
                {
                    fallTarget.localRotation = Quaternion.Slerp(startRot, targetFallRot, easeT);
                    fallTarget.localPosition = Vector3.Lerp(startPos, targetFallPos, easeT);
                }
                yield return null;
            }

            // Yere tam temas halinde sabitle
            if (fallTarget != null)
            {
                fallTarget.localRotation = targetFallRot;
                fallTarget.localPosition = targetFallPos;
            }

            // Savaş alanında 6 saniye boyunca düşmüş bir savaşçı/şehit kalıntısı olarak kalır (AoE hissi)
            yield return new WaitForSeconds(6.0f);

            // Yavaşça toprağa karışarak kaybolur
            float fadeElapsed = 0f;
            float fadeDuration = 1.8f;
            Vector3 corpsePos = (fallTarget != null) ? fallTarget.localPosition : transform.position;

            while (fadeElapsed < fadeDuration)
            {
                fadeElapsed += Time.deltaTime;
                float progress = fadeElapsed / fadeDuration;

                if (fallTarget != null)
                {
                    fallTarget.localPosition = corpsePos + Vector3.down * (progress * 0.42f);
                    fallTarget.localScale = Vector3.one * (1f - progress * 0.55f);
                }
                yield return null;
            }

            Destroy(gameObject);
        }

        public void SetupVisualAppearance()
        {
            transform.localScale = Vector3.one;

            if (unitType == "cavalry")
            {
                attackRange = 2.4f;
                attack = 18;
                armor = 3;
                maxHp = 170;
                moveSpeed = 4.8f;
                detectionRadius = 18f;
                sightRadius = 18f;
            }
            else if (unitType == "scout")
            {
                attackRange = 2.2f;
                attack = 12;
                armor = 1;
                maxHp = 120;
                moveSpeed = 5.2f;
                detectionRadius = 22f;
                sightRadius = 24f; // En geniş keşif görüşü
            }
            else if (unitType == "crossbowman")
            {
                attackRange = 9.5f;
                attack = 18; // Ağır delici hasar
                armor = 2;
                maxHp = 80;
                moveSpeed = 3.0f;
                detectionRadius = 18f;
                sightRadius = 15f;
            }
            else if (unitType == "archer")
            {
                attackRange = 8.5f;
                attack = 12;
                armor = 0;
                maxHp = 70;
                moveSpeed = 3.3f;
                detectionRadius = 18f;
                sightRadius = 14f;
            }
            else if (unitType == "swordsman")
            {
                attackRange = 2.2f;
                attack = 15;
                armor = 4; // Ağır zırh
                maxHp = 130;
                moveSpeed = 3.4f;
                detectionRadius = 16f;
                sightRadius = 12f;
            }
            else if (unitType == "spearman")
            {
                attackRange = 2.4f;
                attack = 11;
                armor = 2;
                maxHp = 100;
                moveSpeed = 3.5f;
                detectionRadius = 16f;
                sightRadius = 12f;
            }
            else if (unitType == "trebuchet")
            {
                attackRange = 18f;
                attack = 55;
                armor = 2;
                maxHp = 150;
                moveSpeed = 2.0f;
                detectionRadius = 20f;
                sightRadius = 16f;
            }
            else if (unitType == "villager")
            {
                attackRange = 1.5f;
                attack = 4;
                armor = 0;
                maxHp = 60;
                moveSpeed = 3.2f;
                detectionRadius = 8f;
                sightRadius = 10f;
            }
            else
            {
                attackRange = 2.2f;
                attack = 12;
                armor = 2;
                maxHp = 100;
                moveSpeed = 3.5f;
                detectionRadius = 16f;
                sightRadius = 12f;
            }

            currentHp = maxHp;
            ApplyTeamVisuals();
        }

        /// <summary>
        /// Sadece takım bayrak, zırh kumaşı ve kalkanlarını takım rengine boyar; çelik, deri, ahşap ve at renklerini korur.
        /// </summary>
        public void ApplyTeamVisuals()
        {
            Color teamColor = (team == UnitTeam.Player) 
                ? new Color(0.12f, 0.45f, 0.95f) 
                : new Color(0.92f, 0.15f, 0.15f);

            var allRenderers = GetComponentsInChildren<Renderer>(true);
            foreach (var ren in allRenderers)
            {
                if (ren == null) continue;
                string goName = ren.gameObject.name.ToLower();
                if (goName == "selectionindicator") continue;

                string matName = (ren.material != null) ? ren.material.name.ToLower() : "";
                if (matName.Contains("team") || goName.Contains("team") || goName.Contains("banner") || goName.Contains("shield") || goName.Contains("sash"))
                {
                    ren.material.color = teamColor;
                }
            }
        }

        private Renderer[] cachedRenderers;

        /// <summary>
        /// Savaş sisi (Fog-of-War) kapsamında rakip biriminin 3D görünürlüğünü açar veya gizler.
        /// Dost birimler her zaman görünürdür.
        /// </summary>
        public void SetFogOfWarVisibility(bool isVisible)
        {
            if (team == RTSGameManager.LocalPlayerTeam)
            {
                isVisible = true;
            }
            isFogVisible = isVisible;

            cachedRenderers = GetComponentsInChildren<Renderer>(true);

            foreach (var r in cachedRenderers)
            {
                if (r != null && (selectionIndicator == null || !r.transform.IsChildOf(selectionIndicator.transform)))
                {
                    r.enabled = isVisible;
                }
            }

            if (!isVisible && overheadHealthBarRoot != null && overheadHealthBarRoot.activeSelf)
            {
                overheadHealthBarRoot.SetActive(false);
            }
        }

        private void CreateSelectionIndicator()
        {
            selectionIndicator = new GameObject("SelectionIndicator");
            selectionIndicator.transform.SetParent(transform, false);
            selectionIndicator.transform.localPosition = new Vector3(0, 0.06f, 0);

            var standardShader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            var ringMat = new Material(standardShader);
            ringMat.color = (team == UnitTeam.Player) 
                ? new Color(0.18f, 0.95f, 0.42f, 0.95f) 
                : new Color(0.95f, 0.22f, 0.22f, 0.95f);

            float r = (unitType == "cavalry" || unitType == "scout") ? 1.05f : (unitType == "trebuchet" ? 1.45f : 0.78f);

            // Dış Çember
            var outer = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            DestroyImmediate(outer.GetComponent<Collider>());
            outer.transform.SetParent(selectionIndicator.transform, false);
            outer.transform.localScale = new Vector3(r * 2f, 0.015f, r * 2f);
            outer.GetComponent<Renderer>().material = ringMat;

            // 4 Köşe Taktiksel Çentik (Age of Empires Brackets)
            for (int i = 0; i < 4; i++)
            {
                var bracket = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyImmediate(bracket.GetComponent<Collider>());
                bracket.transform.SetParent(selectionIndicator.transform, false);
                float angle = i * 90f + 45f;
                Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                bracket.transform.localPosition = dir * (r * 1.05f) + Vector3.up * 0.01f;
                bracket.transform.localRotation = Quaternion.Euler(0, angle, 0);
                bracket.transform.localScale = new Vector3(0.12f, 0.025f, 0.35f);
                bracket.GetComponent<Renderer>().material = ringMat;
            }

            selectionIndicator.SetActive(false);
        }

        private GameObject overheadHealthBarRoot;
        private Transform healthBarFill;
        private Renderer healthBarFillRen;
        private Material healthBarMat;

        private void CreateOverheadHealthBar()
        {
            if (overheadHealthBarRoot != null) return;

            overheadHealthBarRoot = new GameObject("Overhead_HealthBar");
            overheadHealthBarRoot.transform.SetParent(transform, false);
            overheadHealthBarRoot.transform.localPosition = new Vector3(0, (unitType == "cavalry" || unitType == "scout") ? 2.6f : 2.15f, 0);

            // 1. Arka Plan Siyah Bar
            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(bg.GetComponent<Collider>());
            bg.transform.SetParent(overheadHealthBarRoot.transform, false);
            bg.transform.localPosition = Vector3.zero;
            bg.transform.localScale = new Vector3(0.90f, 0.09f, 0.03f);
            var bgRen = bg.GetComponent<Renderer>();
            if (bgRen != null)
            {
                var bgMat = new Material(Shader.Find("Standard"));
                bgMat.color = new Color(0.12f, 0.12f, 0.14f);
                bgMat.SetFloat("_Glossiness", 0f);
                bgRen.material = bgMat;
            }

            // 2. Can Dolgu Barı (Pivotu sol kenara hizalı)
            var fillPivot = new GameObject("Fill_Pivot");
            fillPivot.transform.SetParent(overheadHealthBarRoot.transform, false);
            fillPivot.transform.localPosition = new Vector3(-0.43f, 0, -0.018f);

            var fillMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(fillMesh.GetComponent<Collider>());
            fillMesh.transform.SetParent(fillPivot.transform, false);
            fillMesh.transform.localPosition = new Vector3(0.43f, 0, 0);
            fillMesh.transform.localScale = new Vector3(0.86f, 0.07f, 0.02f);

            healthBarFill = fillPivot.transform;
            healthBarFillRen = fillMesh.GetComponent<Renderer>();
            if (healthBarFillRen != null)
            {
                healthBarMat = new Material(Shader.Find("Standard"));
                healthBarMat.color = new Color(0.18f, 0.85f, 0.35f);
                healthBarMat.SetFloat("_Glossiness", 0f);
                healthBarFillRen.material = healthBarMat;
            }

            overheadHealthBarRoot.SetActive(false);
        }

        public void UpdateHealthBar()
        {
            if (overheadHealthBarRoot == null)
            {
                CreateOverheadHealthBar();
            }

            if (isDead)
            {
                if (overheadHealthBarRoot != null) overheadHealthBarRoot.SetActive(false);
                return;
            }

            bool shouldShow = (team == RTSGameManager.LocalPlayerTeam || isFogVisible) && (isSelected || (currentHp < maxHp && currentHp > 0));
            overheadHealthBarRoot.SetActive(shouldShow);

            if (shouldShow && healthBarFill != null)
            {
                float pct = Mathf.Clamp01((float)currentHp / Mathf.Max(1, maxHp));
                healthBarFill.localScale = new Vector3(pct, 1f, 1f);

                if (healthBarMat != null)
                {
                    Color barColor = (pct > 0.60f) 
                        ? new Color(0.18f, 0.85f, 0.35f) // Canlı Yeşil
                        : (pct > 0.30f)
                            ? new Color(0.98f, 0.72f, 0.12f) // Altın Sarısı
                            : new Color(0.95f, 0.22f, 0.22f); // Tehlike Kırmızısı
                    healthBarMat.color = barColor;
                }
            }
        }

        private void LateUpdate()
        {
            if (isDead) return;

            if (overheadHealthBarRoot != null && overheadHealthBarRoot.activeSelf)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    overheadHealthBarRoot.transform.rotation = cam.transform.rotation;
                }
            }
        }

        private IEnumerator AttackLungeCoroutine()
        {
            if (visualChild == null || isDead) yield break;
            isAttackingAnimPlaying = true;
            float elapsed = 0f;
            string t = (unitType ?? "").ToLower();

            if (t == "spearman")
            {
                // Mızrakçı: Hızlı ve seri delici saplama hamlesi (Rapid Forward Thrust)
                float thrustDuration = 0.24f;
                while (elapsed < thrustDuration && !isDead)
                {
                    elapsed += Time.deltaTime;
                    float p = elapsed / thrustDuration;
                    float fwd = (p < 0.4f) ? Mathf.Lerp(0f, 0.55f, p / 0.4f) : Mathf.Lerp(0.55f, 0f, (p - 0.4f) / 0.6f);
                    float pitch = Mathf.Sin(p * Mathf.PI) * 8f;
                    if (visualChild != null)
                    {
                        visualChild.localPosition = new Vector3(0, 0, fwd);
                        visualChild.localRotation = Quaternion.Euler(pitch, 0, 0);
                    }
                    yield return null;
                }
            }
            else if (t == "cavalry")
            {
                // Süvari: Şahlanma, toynak indirme ve kargı hücumu (Cavalry Rear & Lance Thrust)
                float cavDuration = 0.32f;
                while (elapsed < cavDuration && !isDead)
                {
                    elapsed += Time.deltaTime;
                    float p = elapsed / cavDuration;
                    float pitch = (p < 0.35f) ? Mathf.Lerp(0f, -12f, p / 0.35f) : Mathf.Lerp(-12f, 16f, (p - 0.35f) / 0.65f);
                    float fwd = (p < 0.35f) ? 0f : Mathf.Sin(((p - 0.35f) / 0.65f) * Mathf.PI) * 0.62f;
                    float hop = Mathf.Sin(p * Mathf.PI) * 0.12f;
                    if (visualChild != null)
                    {
                        visualChild.localPosition = new Vector3(0, hop, fwd);
                        visualChild.localRotation = Quaternion.Euler(pitch, 0, 0);
                    }
                    yield return null;
                }
            }
            else if (t == "swordsman")
            {
                // Kılıç Ustası: Geniş kavisli çapraz kılıç savurma (Sweeping Diagonal Cleave)
                float slashDuration = 0.26f;
                while (elapsed < slashDuration && !isDead)
                {
                    elapsed += Time.deltaTime;
                    float p = elapsed / slashDuration;
                    float fwd = Mathf.Sin(p * Mathf.PI) * 0.42f;
                    float yaw = Mathf.Sin(p * Mathf.PI) * 28f;
                    float roll = Mathf.Sin(p * Mathf.PI) * -14f;
                    float pitch = Mathf.Sin(p * Mathf.PI) * 12f;
                    if (visualChild != null)
                    {
                        visualChild.localPosition = new Vector3(0, 0, fwd);
                        visualChild.localRotation = Quaternion.Euler(pitch, yaw, roll);
                    }
                    yield return null;
                }
            }
            else
            {
                // Köylü / Genel: Tepeden balta/çekiç indirme (Overhead Strike)
                float chopDuration = 0.25f;
                while (elapsed < chopDuration && !isDead)
                {
                    elapsed += Time.deltaTime;
                    float p = elapsed / chopDuration;
                    float fwd = Mathf.Sin(p * Mathf.PI) * 0.32f;
                    float pitch = (p < 0.3f) ? Mathf.Lerp(0f, -14f, p / 0.3f) : Mathf.Lerp(-14f, 22f, (p - 0.3f) / 0.7f);
                    if (visualChild != null)
                    {
                        visualChild.localPosition = new Vector3(0, 0, fwd);
                        visualChild.localRotation = Quaternion.Euler(pitch, 0, 0);
                    }
                    yield return null;
                }
            }

            if (visualChild != null && !isDead)
            {
                visualChild.localPosition = Vector3.zero;
                visualChild.localRotation = Quaternion.identity;
            }
            isAttackingAnimPlaying = false;
        }

        private IEnumerator RangedRecoilCoroutine()
        {
            if (visualChild == null || isDead) yield break;
            isAttackingAnimPlaying = true;
            float elapsed = 0f;
            float duration = 0.28f;

            while (elapsed < duration && !isDead)
            {
                elapsed += Time.deltaTime;
                float p = elapsed / duration;
                // İlk %35 geriye yay çekme, sonra ileriye ok fırlatma yaylanması
                float fwd = (p < 0.35f) ? Mathf.Lerp(0f, -0.08f, p / 0.35f) : Mathf.Lerp(-0.08f, 0.14f, Mathf.Sin(((p - 0.35f) / 0.65f) * Mathf.PI));
                float pitch = (p < 0.35f) ? Mathf.Lerp(0f, -6f, p / 0.35f) : Mathf.Lerp(-6f, 10f, Mathf.Sin(((p - 0.35f) / 0.65f) * Mathf.PI));
                if (visualChild != null)
                {
                    visualChild.localPosition = new Vector3(0, 0, fwd);
                    visualChild.localRotation = Quaternion.Euler(pitch, 0, 0);
                }
                yield return null;
            }

            if (visualChild != null && !isDead)
            {
                visualChild.localPosition = Vector3.zero;
                visualChild.localRotation = Quaternion.identity;
            }
            isAttackingAnimPlaying = false;
        }

        private IEnumerator TrebuchetLaunchCoroutine()
        {
            if (visualChild == null || isDead) yield break;
            isAttackingAnimPlaying = true;
            float elapsed = 0f;
            float duration = 0.55f;

            while (elapsed < duration && !isDead)
            {
                elapsed += Time.deltaTime;
                float p = elapsed / duration;
                // Ağır karşı ağırlık düşüşü ve şasi öne yatışı
                float pitch = (p < 0.25f) 
                    ? Mathf.Lerp(0f, 18f, p / 0.25f) 
                    : Mathf.Lerp(18f, -6f, (p - 0.25f) / 0.75f) * Mathf.Exp(-(p - 0.25f) * 3f);
                float dipY = Mathf.Sin(p * Mathf.PI) * -0.15f;

                if (visualChild != null)
                {
                    visualChild.localPosition = new Vector3(0, dipY, 0);
                    visualChild.localRotation = Quaternion.Euler(pitch, 0, 0);
                }
                yield return null;
            }

            if (visualChild != null && !isDead)
            {
                visualChild.localPosition = Vector3.zero;
                visualChild.localRotation = Quaternion.identity;
            }
            isAttackingAnimPlaying = false;
        }

        private void SpawnSlashEffect(Vector3 pos)
        {
            RTSVFXManager.Instance?.SpawnMeleeSparkVFX(pos, Vector3.up);
        }

        /// <summary>
        /// Sınıfa özel 3D model ve ekipmanları (RTSUnitVisuals) kullanarak sahada yeni bir RTS birimi üretir.
        /// </summary>
        public static RTSUnit SpawnUnit(string type, Vector3 pos, UnitTeam team)
        {
            string t = type.ToLower();
            GameObject unitGo = new GameObject($"{team}_{t}");
            unitGo.transform.position = pos;

            // 1. Zengin, dik duran, sınıfa özel 3D model ve ekipmanları inşa et
            var visual = RTSUnitVisuals.BuildModel(unitGo.transform, t, team);

            // Alt parçalardaki istenmeyen iç collider'ları derhal temizle - Sadece kök collider kalacak
            foreach (var col in unitGo.GetComponentsInChildren<Collider>(true))
            {
                DestroyImmediate(col);
            }

            var u = unitGo.AddComponent<RTSUnit>();
            u.unitType = t;
            u.team = team;
            u.visualChild = (visual != null) ? visual.transform : null;
            u.EnsurePhysics();
            u.SetupVisualAppearance();

            if (t == "villager")
            {
                if (unitGo.GetComponent<RTSVillager>() == null)
                {
                    unitGo.AddComponent<RTSVillager>();
                }
            }

            if (team != RTSGameManager.LocalPlayerTeam)
            {
                bool vis = (RTSFogOfWarManager.Instance != null) && RTSFogOfWarManager.Instance.IsUnitVisible(u);
                u.SetFogOfWarVisibility(vis);
            }
            else
            {
                u.SetFogOfWarVisibility(true);
            }

            return u;
        }
    }
}
