using UnityEngine;

namespace CSE377.Gameplay
{
    public enum VillagerState
    {
        Idle,
        WalkingToResource,
        Harvesting,
        ReturningToTownCenter,
        WalkingToBuild,
        Building,
        WalkingToRepair,
        Repairing
    }

    /// <summary>
    /// Köylü biriminin kaynak toplama ve Şehir Merkezine taşıma döngüsünü (Harvesting Loop)
    /// ve yeni bina inşaatı (Building Construction) görevlerini yönetir.
    /// Age of Empires tarzı: Kaynak topla -> Dolunca Şehir Merkezine taşı -> Tekrar kaynağa dön!
    /// Bina inşaatı: Temele git -> Çekiçle inşa et (%0 -> %100).
    /// </summary>
    [RequireComponent(typeof(RTSUnit))]
    public class RTSVillager : MonoBehaviour
    {
        [Header("Taşıma Kapasitesi")]
        public RTSResourceType carryingType = RTSResourceType.Wood;
        public int carriedAmount = 0;
        public int maxCapacity = 10;

        [Header("Durum")]
        public VillagerState currentState = VillagerState.Idle;
        public RTSResourceSource currentResource;
        public RTSBuilding currentBuildingTask;
        public Transform townCenter;

        private RTSUnit unit;
        private float nextHarvestTime = 0f;
        private float nextBuildFxTime = 0f;
        private float harvestRange = 1.3f;
        private float depositRange = 2.4f;
        private float buildRange = 1.6f;

        private void Awake()
        {
            unit = GetComponent<RTSUnit>();
            unit.unitType = "villager";
        }

        private void Start()
        {
            FindTownCenter();
        }

        public Vector3 GetHarvestStandPosition(RTSResourceSource source)
        {
            if (source == null) return transform.position;

            Collider col = source.GetComponent<Collider>();
            if (col != null)
            {
                // Tarlanın veya kaynağın dış sınırındaki rahat çalışma noktası (tarlanın / çalının yanı)
                Vector3 closest = col.ClosestPoint(transform.position);
                Vector3 dir = (transform.position - source.transform.position);
                dir.y = 0;
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;

                // Tarlanın veya kaynağın 0.5 metre dibinde duracağı çalışma noktası
                return closest + dir.normalized * 0.78f;
            }

            return source.transform.position;
        }

        private float nextTargetRetryTime;
        private int targetRetryCount;

        private bool IsRemoteNetworkVillager =>
            unit != null &&
            RTSNetworkSyncManager.Instance != null &&
            RTSNetworkSyncManager.Instance.isMultiplayerMode &&
            unit.team != RTSGameManager.LocalPlayerTeam;

        public static RTSVillager FindBestBuilderFor(UnitTeam team, Vector3 buildPos)
        {
            RTSVillager best = null;
            int bestRank = int.MaxValue;
            float bestDistSq = float.MaxValue;

            foreach (var u in RTSUnit.AllUnits)
            {
                if (u == null || u.isDead || u.currentHp <= 0 || u.team != team) continue;
                var v = u.GetComponent<RTSVillager>();
                if (v == null || !v.enabled) continue;

                int rank = 3;
                if (v.currentState == VillagerState.Idle) rank = 0;
                else if (v.currentState == VillagerState.WalkingToResource ||
                         v.currentState == VillagerState.Harvesting ||
                         v.currentState == VillagerState.ReturningToTownCenter) rank = 1;
                else if (v.currentState == VillagerState.WalkingToRepair ||
                         v.currentState == VillagerState.Repairing) rank = 2;

                float dx = u.transform.position.x - buildPos.x;
                float dz = u.transform.position.z - buildPos.z;
                float distSq = dx * dx + dz * dz;

                if (rank < bestRank || (rank == bestRank && distSq < bestDistSq))
                {
                    bestRank = rank;
                    bestDistSq = distSq;
                    best = v;
                }
            }

            return best;
        }

        private float GetHorizontalDistanceToBuilding(RTSBuilding b)
        {
            if (b == null) return float.MaxValue;
            Collider bCol = b.GetComponent<Collider>();
            Vector3 targetPoint = (bCol != null) ? bCol.ClosestPoint(transform.position) : b.transform.position;
            return Vector2.Distance(
                new Vector2(transform.position.x, transform.position.z),
                new Vector2(targetPoint.x, targetPoint.z));
        }

        private Vector3 ComputeBuildingApproachSpot(RTSBuilding building, float standOffset = 0.7f)
        {
            Collider bCol = building.GetComponent<Collider>();
            Vector3 toMe = transform.position - building.transform.position;
            toMe.y = 0f;
            if (toMe.sqrMagnitude < 0.04f) toMe = Vector3.forward;
            toMe.Normalize();

            if (bCol == null)
            {
                return building.transform.position + toMe * 2.2f;
            }

            Vector3 closest = bCol.ClosestPoint(transform.position);
            Vector3 flatPos = new Vector3(transform.position.x, 0f, transform.position.z);
            Vector3 flatClosest = new Vector3(closest.x, 0f, closest.z);

            // Köylü bina footprint'inin içinde kaldıysa dış yüzeye yansıt
            if (Vector3.Distance(flatPos, flatClosest) < 0.05f)
            {
                Vector3 farOutside = building.transform.position + toMe * 12f;
                farOutside.y = bCol.bounds.center.y;
                closest = bCol.ClosestPoint(farOutside);
                flatClosest = new Vector3(closest.x, 0f, closest.z);
            }

            Vector3 dirFromEdge = flatPos - flatClosest;
            Vector3 approachDir = (dirFromEdge.sqrMagnitude > 0.04f) ? dirFromEdge.normalized : toMe;
            Vector3 spot = closest + approachDir * standOffset;
            spot.y = transform.position.y;
            return spot;
        }

        public void AssignHarvestTask(RTSResourceSource source)
        {
            currentBuildingTask = null;
            targetRetryCount = 0;
            nextTargetRetryTime = 0f;
            currentResource = source;
            if (source != null)
            {
                carryingType = source.resourceType;
                currentState = VillagerState.WalkingToResource;
                Vector3 harvestSpot = GetHarvestStandPosition(source);
                unit.MoveTo(harvestSpot, isManual: false, forcedCrossingX: float.NaN, ignoreBuilding: source.GetComponent<RTSBuilding>());
            }
        }

        public void AssignBuildTask(RTSBuilding building)
        {
            if (building == null || building.isDestroyed || building.currentHp <= 0 || building.isConstructed)
            {
                CancelTask();
                return;
            }

            currentResource = null;
            currentBuildingTask = building;
            targetRetryCount = 0;
            nextTargetRetryTime = Time.time + 0.8f;
            currentState = VillagerState.WalkingToBuild;
            Vector3 buildSpot = ComputeBuildingApproachSpot(building, 0.7f);
            unit.MoveTo(buildSpot, isManual: false, forcedCrossingX: float.NaN, ignoreBuilding: building);
        }

        public void AssignRepairTask(RTSBuilding building)
        {
            if (building == null || building.isDestroyed || building.currentHp <= 0 || !building.isConstructed || building.currentHp >= building.maxHp)
            {
                CancelTask();
                return;
            }

            currentResource = null;
            currentBuildingTask = building;
            targetRetryCount = 0;
            nextTargetRetryTime = Time.time + 0.8f;
            currentState = VillagerState.WalkingToRepair;
            Vector3 repairSpot = ComputeBuildingApproachSpot(building, 0.7f);
            unit.MoveTo(repairSpot, isManual: false, forcedCrossingX: float.NaN, ignoreBuilding: building);
        }

        /// <summary>
        /// Oyuncu köylüye zemine sağ tıklayarak doğrudan intikal emri verdiğinde mevcut toplama veya inşaat görevini iptal eder.
        /// Köylüyü boşa (Idle) çıkarır, böylece kendi kendine kaynağa geri dönmez veya toplama repliği söylemez.
        /// </summary>
        public void CancelTask()
        {
            currentState = VillagerState.Idle;
            currentResource = null;
            currentBuildingTask = null;
            targetRetryCount = 0;
            nextTargetRetryTime = 0f;
            unit?.ClearIgnoreBuilding();
        }

        private void Update()
        {
            if (unit == null || unit.isDead || unit.currentHp <= 0) return;

            switch (currentState)
            {
                case VillagerState.WalkingToBuild:
                    if (currentBuildingTask == null || currentBuildingTask.isDestroyed || currentBuildingTask.currentHp <= 0 || currentBuildingTask.isConstructed)
                    {
                        currentBuildingTask = null;
                        if (unit != null && !unit.isManualMoveOrder) unit.StopMovementOrder();
                        currentState = VillagerState.Idle;
                        return;
                    }

                    float distToBuild = GetHorizontalDistanceToBuilding(currentBuildingTask);

                    // İnşaat menziline ulaşıldığında veya kalabalıkken durup yeterli yakınlıkta kaldığında inşaata başla
                    if (distToBuild <= buildRange || (!unit.isMoving && distToBuild <= 3.0f))
                    {
                        unit.StopMovementOrder();
                        currentState = VillagerState.Building;
                        targetRetryCount = 0;
                        nextBuildFxTime = Time.time + 0.1f;
                    }
                    else if (!unit.isMoving && Time.time >= nextTargetRetryTime)
                    {
                        if (targetRetryCount < 3)
                        {
                            targetRetryCount++;
                            nextTargetRetryTime = Time.time + 0.9f;
                            Vector3 retrySpot = ComputeBuildingApproachSpot(currentBuildingTask, 0.7f);
                            unit.MoveTo(retrySpot, isManual: false, forcedCrossingX: float.NaN, ignoreBuilding: currentBuildingTask);
                        }
                        else
                        {
                            CancelTask();
                        }
                    }
                    break;

                case VillagerState.Building:
                    if (currentBuildingTask == null || currentBuildingTask.isDestroyed || currentBuildingTask.currentHp <= 0 || currentBuildingTask.isConstructed)
                    {
                        currentBuildingTask = null;
                        if (unit != null && !unit.isManualMoveOrder) unit.StopMovementOrder();
                        currentState = VillagerState.Idle;
                        return;
                    }

                    if (unit != null && !unit.isManualMoveOrder && (unit.isMoving || unit.commandDestination != Vector3.zero))
                    {
                        unit.StopMovementOrder();
                    }

                    // İtme/çarpışma sonucu binadan uzaklaştıysa tekrar yaklaş
                    if (GetHorizontalDistanceToBuilding(currentBuildingTask) > 3.2f)
                    {
                        AssignBuildTask(currentBuildingTask);
                        return;
                    }

                    // Binaya doğru dön
                    Vector3 lookDir = currentBuildingTask.transform.position - transform.position;
                    lookDir.y = 0f;
                    if (lookDir.sqrMagnitude > 0.0001f)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir.normalized), Time.deltaTime * 10f);
                    }

                    // İnşaat ilerlemesini artır (Saniyede %25; ağdaki karşı oyuncuda sahibin paketi otoritedir)
                    if (!IsRemoteNetworkVillager)
                    {
                        currentBuildingTask.AddConstructionProgress(25f * Time.deltaTime);
                    }
                    unit?.AnimateWorkSwing(Time.time * 9.5f);

                    // Çekiç vurma efekti ve sesi
                    if (Time.time >= nextBuildFxTime)
                    {
                        SpawnBuildEffect(currentBuildingTask.transform.position);
                        RTSAudioManager.Instance?.PlaySound(RTSSoundType.HammerBuild, 0.12f);
                        nextBuildFxTime = Time.time + 0.45f;
                    }
                    break;

                case VillagerState.WalkingToRepair:
                    if (currentBuildingTask == null || currentBuildingTask.isDestroyed || currentBuildingTask.currentHp <= 0 || !currentBuildingTask.isConstructed || currentBuildingTask.currentHp >= currentBuildingTask.maxHp)
                    {
                        currentBuildingTask = null;
                        if (unit != null && !unit.isManualMoveOrder) unit.StopMovementOrder();
                        currentState = VillagerState.Idle;
                        return;
                    }

                    float distToRep = GetHorizontalDistanceToBuilding(currentBuildingTask);

                    if (distToRep <= buildRange || (!unit.isMoving && distToRep <= 3.0f))
                    {
                        unit.StopMovementOrder();
                        currentState = VillagerState.Repairing;
                        targetRetryCount = 0;
                        nextBuildFxTime = Time.time + 0.1f;

                        Vector3 repLook = currentBuildingTask.transform.position - transform.position;
                        repLook.y = 0f;
                        if (repLook.sqrMagnitude > 0.0001f)
                        {
                            transform.rotation = Quaternion.LookRotation(repLook.normalized);
                        }
                    }
                    else if (!unit.isMoving && Time.time >= nextTargetRetryTime)
                    {
                        if (targetRetryCount < 3)
                        {
                            targetRetryCount++;
                            nextTargetRetryTime = Time.time + 0.9f;
                            Vector3 retryRepSpot = ComputeBuildingApproachSpot(currentBuildingTask, 0.7f);
                            unit.MoveTo(retryRepSpot, isManual: false, forcedCrossingX: float.NaN, ignoreBuilding: currentBuildingTask);
                        }
                        else
                        {
                            CancelTask();
                        }
                    }
                    break;

                case VillagerState.Repairing:
                    if (currentBuildingTask == null || currentBuildingTask.isDestroyed || currentBuildingTask.currentHp <= 0)
                    {
                        currentBuildingTask = null;
                        if (unit != null && !unit.isManualMoveOrder) unit.StopMovementOrder();
                        currentState = VillagerState.Idle;
                        return;
                    }

                    if (currentBuildingTask.currentHp >= currentBuildingTask.maxHp)
                    {
                        currentBuildingTask.currentHp = currentBuildingTask.maxHp;
                        currentBuildingTask.CheckDamageVisualState();
                        currentBuildingTask = null;
                        currentState = VillagerState.Idle;
                        RTSHUDController.Instance?.SetAiStatus($"[TAMİRAT] Tamirat tamamlandı ve bina güçlendirildi!", Color.green);
                        return;
                    }

                    // Saniyede 40 HP onar (Tamirat hızı)
                    if (unit != null && !unit.isManualMoveOrder && (unit.isMoving || unit.commandDestination != Vector3.zero))
                    {
                        unit.StopMovementOrder();
                    }

                    if (GetHorizontalDistanceToBuilding(currentBuildingTask) > 3.2f)
                    {
                        AssignRepairTask(currentBuildingTask);
                        return;
                    }

                    Vector3 repFace = currentBuildingTask.transform.position - transform.position;
                    repFace.y = 0f;
                    if (repFace.sqrMagnitude > 0.0001f)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(repFace.normalized), Time.deltaTime * 16f);
                    }

                    if (!IsRemoteNetworkVillager)
                    {
                        currentBuildingTask.currentHp += Mathf.RoundToInt(40f * Time.deltaTime);
                        currentBuildingTask.currentHp = Mathf.Min(currentBuildingTask.currentHp, currentBuildingTask.maxHp);
                        currentBuildingTask.CheckDamageVisualState();
                    }
                    unit?.AnimateWorkSwing(Time.time * 9.5f);

                    // Çekiç vurma efekti ve sesi
                    if (Time.time >= nextBuildFxTime)
                    {
                        SpawnBuildEffect(currentBuildingTask.transform.position);
                        RTSAudioManager.Instance?.PlaySound(RTSSoundType.HammerBuild, 0.12f);
                        RTSVFXManager.Instance?.SpawnCombatText(currentBuildingTask.transform.position + Vector3.up * 2.2f, "+18 Onarım", new Color(0.25f, 0.90f, 1f));
                        nextBuildFxTime = Time.time + 0.45f;
                    }
                    break;

                case VillagerState.WalkingToResource:
                    if (currentResource == null || currentResource.remainingAmount <= 0)
                    {
                        var alt = FindNearbyAlternativeResource(carryingType, 30f);
                        if (alt != null)
                        {
                            AssignHarvestTask(alt);
                        }
                        else
                        {
                            currentState = VillagerState.Idle;
                        }
                        return;
                    }

                    Collider rCol = currentResource.GetComponent<Collider>();
                    float distToResource = (rCol != null) 
                        ? Vector3.Distance(transform.position, rCol.ClosestPoint(transform.position)) 
                        : Vector3.Distance(transform.position, currentResource.transform.position);

                    // Tarlanın / kaynağın yanına (menzile: 1.3m) tam ulaşıldığında hasada başla
                    if (distToResource <= harvestRange || (!unit.isMoving && distToResource <= 1.8f))
                    {
                        unit.StopMovementOrder();
                        currentState = VillagerState.Harvesting;
                        nextHarvestTime = Time.time + 0.15f;

                        // Kaynağa doğrudan dön
                        Vector3 faceDir = currentResource.transform.position - transform.position;
                        faceDir.y = 0f;
                        if (faceDir.sqrMagnitude > 0.0001f)
                        {
                            transform.rotation = Quaternion.LookRotation(faceDir.normalized);
                        }
                    }
                    break;

                case VillagerState.Harvesting:
                    if (currentResource == null || currentResource.remainingAmount <= 0)
                    {
                        // Kaynak tükendi, elde kaynak varsa merkeze götür, yoksa yakındaki yeni kaynağa geç
                        if (carriedAmount > 0)
                        {
                            ReturnToDeposit();
                        }
                        else
                        {
                            var alt = FindNearbyAlternativeResource(carryingType, 30f);
                            if (alt != null)
                            {
                                AssignHarvestTask(alt);
                            }
                            else
                            {
                                currentState = VillagerState.Idle;
                            }
                        }
                        return;
                    }

                    if (unit != null && !unit.isManualMoveOrder && (unit.isMoving || unit.commandDestination != Vector3.zero))
                    {
                        unit.StopMovementOrder();
                    }

                    // Tarlaya / kaynağa dönük kal
                    Vector3 lookFarm = currentResource.transform.position - transform.position;
                    lookFarm.y = 0f;
                    if (lookFarm.sqrMagnitude > 0.0001f)
                    {
                        lookFarm.Normalize();
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookFarm), Time.deltaTime * 16f);
                    }

                    // Hasat aralığı
                    unit?.AnimateWorkSwing(Time.time * 8.5f);
                    if (Time.time >= nextHarvestTime)
                    {
                        int gathered = currentResource.ExtractResource(currentResource.gatherPerTick);
                        carriedAmount += gathered;
                        nextHarvestTime = Time.time + currentResource.harvestInterval;

                        // Çekiç / Balta / Orak vuruş efekti ve sesi
                        SpawnHarvestEffect(transform.position + Vector3.up * 0.5f + lookFarm * 0.8f);
                        if (currentResource.resourceType == RTSResourceType.Gold)
                        {
                            RTSAudioManager.Instance?.PlaySound(RTSSoundType.GoldMine, 0.1f);
                        }
                        else
                        {
                            RTSAudioManager.Instance?.PlaySound(RTSSoundType.HarvestChop, 0.1f);
                        }

                        if (carriedAmount >= maxCapacity)
                        {
                            ReturnToDeposit();
                        }
                    }
                    break;

                case VillagerState.ReturningToTownCenter:
                    if (townCenter == null) FindTownCenter();
                    Vector3 depositPos = (townCenter != null) ? townCenter.position : Vector3.zero;

                    Collider tcCol = (townCenter != null) ? townCenter.GetComponent<Collider>() : null;
                    float distToTownCenter = (tcCol != null) 
                        ? Vector3.Distance(transform.position, tcCol.ClosestPoint(transform.position)) 
                        : Vector3.Distance(transform.position, depositPos);

                    if (distToTownCenter <= depositRange || (!unit.isMoving && distToTownCenter <= 3.2f))
                    {
                        unit.StopMovementOrder();
                        DepositResources();
                    }
                    else if (!unit.isMoving)
                    {
                        ReturnToDeposit();
                    }
                    break;

                case VillagerState.Idle:
                    break;
            }
        }

        private void ReturnToDeposit()
        {
            currentState = VillagerState.ReturningToTownCenter;
            if (townCenter == null) FindTownCenter();
            if (townCenter != null)
            {
                Collider tcCol = townCenter.GetComponent<Collider>();
                Vector3 depositSpot = townCenter.position;
                if (tcCol != null)
                {
                    Vector3 closest = tcCol.ClosestPoint(transform.position);
                    Vector3 dir = (transform.position - closest);
                    dir.y = 0;
                    if (dir.sqrMagnitude < 0.01f) dir = (transform.position - townCenter.position);
                    dir.y = 0;
                    if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
                    depositSpot = closest + dir.normalized * 1.6f;
                    depositSpot.y = 0;
                }
                var tcBuilding = townCenter.GetComponent<RTSBuilding>();
                unit.MoveTo(depositSpot, isManual: false, forcedCrossingX: float.NaN, ignoreBuilding: tcBuilding);
            }
        }

        private void DepositResources()
        {
            if (carriedAmount > 0)
            {
                var gameMgr = RTSGameManager.Instance;
                if (gameMgr != null && (unit == null || unit.team == RTSGameManager.LocalPlayerTeam))
                {
                    gameMgr.DepositResources(carryingType, carriedAmount);
                }

                if (unit == null || unit.team == RTSGameManager.LocalPlayerTeam)
                {
                    // AoE tarzı kaynak teslim bildirimi ve sesi
                    string icon = (carryingType == RTSResourceType.Wood) ? "[Odun]" : (carryingType == RTSResourceType.Gold) ? "[Altın]" : "[Gıda]";
                    Color resColor = (carryingType == RTSResourceType.Gold) ? new Color(1f, 0.86f, 0.2f) : (carryingType == RTSResourceType.Wood) ? new Color(0.85f, 0.65f, 0.35f) : new Color(0.35f, 0.90f, 0.4f);
                    Vector3 popPos = (townCenter != null) ? (townCenter.position + Vector3.up * 3.5f) : (transform.position + Vector3.up * 2f);
                    RTSVFXManager.Instance?.SpawnCombatText(popPos, $"+{carriedAmount} {icon}", resColor);

                    if (carryingType == RTSResourceType.Gold)
                        RTSAudioManager.Instance?.PlaySound(RTSSoundType.GoldMine, 0.18f);
                    else
                        RTSAudioManager.Instance?.PlaySound(RTSSoundType.Click, 0.18f);
                }

                Debug.Log($"[KÖYLÜ] {carriedAmount} adet {carryingType} Şehir Merkezine teslim edildi!");
                carriedAmount = 0;
            }

            // Eğer kaynak hala varsa geri dönüp tarlanın yanına git ve toplamaya devam et (Otomatik Döngü)
            if (currentResource != null && currentResource.remainingAmount > 0)
            {
                currentState = VillagerState.WalkingToResource;
                Vector3 returnSpot = GetHarvestStandPosition(currentResource);
                unit.MoveTo(returnSpot, isManual: false, forcedCrossingX: float.NaN, ignoreBuilding: currentResource.GetComponent<RTSBuilding>());
            }
            else
            {
                // Akıllı İşçi Otomasyonu (Smart Auto-Retask): En yakın yeni kaynağa geç!
                var altResource = FindNearbyAlternativeResource(carryingType, 35f);
                if (altResource != null)
                {
                    AssignHarvestTask(altResource);
                    RTSHUDController.Instance?.SetAiStatus($"[AKILLI İŞÇİ] Tükenen kaynaktan en yakın yeni {altResource.resourceType} kaynağına geçildi.", Color.cyan);
                }
                else
                {
                    currentState = VillagerState.Idle;
                }
            }
        }

        /// <summary>
        /// Akıllı İşçi Otomasyonu (Smart Auto-Retask):
        /// Mevcut kaynak tükendiğinde köylüyü durdurmak yerine yakındaki en uygun kaynağa otomatik yönlendirir.
        /// </summary>
        public RTSResourceSource FindNearbyAlternativeResource(RTSResourceType preferredType, float maxRadius = 35f)
        {
            RTSResourceSource bestSameType = null;
            float bestSameDist = float.MaxValue;
            RTSResourceSource bestAnyType = null;
            float bestAnyDist = float.MaxValue;

            UnitTeam myTeam = (unit != null) ? unit.team : RTSGameManager.LocalPlayerTeam;
            for (int i = 0; i < RTSResourceSource.AllSources.Count; i++)
            {
                var src = RTSResourceSource.AllSources[i];
                if (src == null || src == currentResource || src.remainingAmount <= 0) continue;

                // Yerel oyuncunun köylüsü tükenen kaynaktan sadece keşfedilmiş (sis dışı) alternatiflere otomatik geçer
                if (myTeam == RTSGameManager.LocalPlayerTeam && RTSFogOfWarManager.Instance != null)
                {
                    if (RTSFogOfWarManager.Instance.GetCellStateAtWorldPos(src.transform.position) == FogCellState.Unexplored)
                        continue;
                }

                float d = Vector3.Distance(transform.position, src.transform.position);
                if (d > maxRadius) continue;

                if (src.resourceType == preferredType)
                {
                    if (d < bestSameDist)
                    {
                        bestSameDist = d;
                        bestSameType = src;
                    }
                }
                else
                {
                    if (d < bestAnyDist)
                    {
                        bestAnyDist = d;
                        bestAnyType = src;
                    }
                }
            }

            return (bestSameType != null) ? bestSameType : bestAnyType;
        }

        private void FindTownCenter()
        {
            UnitTeam myTeam = (unit != null) ? unit.team : RTSGameManager.LocalPlayerTeam;
            RTSBuilding bestTc = null;
            float bestDist = float.MaxValue;

            foreach (var b in RTSBuilding.AllBuildings)
            {
                if (b != null && !b.isDestroyed && b.isConstructed && b.team == myTeam && b.buildingType == RTSBuildingType.TownCenter && b.currentHp > 0)
                {
                    float d = Vector3.Distance(transform.position, b.transform.position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestTc = b;
                    }
                }
            }

            if (bestTc != null)
            {
                townCenter = bestTc.transform;
                return;
            }

            var tc = GameObject.Find("Building_TownCenter");
            if (tc != null)
            {
                townCenter = tc.transform;
                return;
            }

            var marker = GameObject.Find("Zone_MainBase");
            if (marker != null)
            {
                townCenter = marker.transform;
            }
            else
            {
                var barracks = GameObject.Find("Building_Barracks");
                if (barracks != null) townCenter = barracks.transform;
            }
        }

        private void SpawnHarvestEffect(Vector3 sourcePos)
        {
            Vector3 hitPos = Vector3.Lerp(transform.position, sourcePos, 0.6f) + Vector3.up * 1.0f;
            if (carryingType == RTSResourceType.Gold)
            {
                RTSVFXManager.Instance?.SpawnMeleeSparkVFX(hitPos, Vector3.up);
            }
            else
            {
                RTSVFXManager.Instance?.SpawnHammerDustVFX(hitPos);
            }
        }

        private void SpawnBuildEffect(Vector3 buildingPos)
        {
            Vector3 hitPos = Vector3.Lerp(transform.position, buildingPos, 0.5f) + Vector3.up * 1.1f;
            RTSVFXManager.Instance?.SpawnHammerDustVFX(hitPos);
        }
    }
}
