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

        public void AssignHarvestTask(RTSResourceSource source)
        {
            currentBuildingTask = null;
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
            currentResource = null;
            currentBuildingTask = building;
            if (building != null)
            {
                currentState = VillagerState.WalkingToBuild;
                Collider bCol = building.GetComponent<Collider>();
                Vector3 toMe = (transform.position - building.transform.position);
                toMe.y = 0;
                if (toMe.sqrMagnitude < 0.1f) toMe = Vector3.forward;
                toMe.Normalize();

                Vector3 buildSpot = (bCol != null) 
                    ? bCol.ClosestPoint(transform.position) + toMe * 0.7f 
                    : building.transform.position + toMe * 2.2f;

                unit.MoveTo(buildSpot, isManual: false, forcedCrossingX: float.NaN, ignoreBuilding: building);
            }
        }

        public void AssignRepairTask(RTSBuilding building)
        {
            currentResource = null;
            currentBuildingTask = building;
            if (building != null)
            {
                currentState = VillagerState.WalkingToRepair;
                Collider bCol = building.GetComponent<Collider>();
                Vector3 toMe = (transform.position - building.transform.position);
                toMe.y = 0;
                if (toMe.sqrMagnitude < 0.1f) toMe = Vector3.forward;
                toMe.Normalize();

                Vector3 repairSpot = (bCol != null) 
                    ? bCol.ClosestPoint(transform.position) + toMe * 0.7f 
                    : building.transform.position + toMe * 2.2f;

                unit.MoveTo(repairSpot, isManual: false, forcedCrossingX: float.NaN, ignoreBuilding: building);
            }
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
        }

        private void Update()
        {
            if (unit == null || unit.isDead || unit.currentHp <= 0) return;

            switch (currentState)
            {
                case VillagerState.WalkingToBuild:
                    if (currentBuildingTask == null)
                    {
                        currentState = VillagerState.Idle;
                        return;
                    }

                    Collider bCol = currentBuildingTask.GetComponent<Collider>();
                    float distToBuild = (bCol != null) 
                        ? Vector3.Distance(transform.position, bCol.ClosestPoint(transform.position)) 
                        : Vector3.Distance(transform.position, currentBuildingTask.transform.position);

                    // İnşaat menziline tam ulaşıldığında (en fazla 1.6m) doğrudan inşaat moduna geç
                    if (distToBuild <= buildRange || (!unit.isMoving && distToBuild <= 2.2f))
                    {
                        unit.StopMovementOrder();
                        currentState = VillagerState.Building;
                        nextBuildFxTime = Time.time + 0.1f;
                    }
                    break;

                case VillagerState.Building:
                    if (currentBuildingTask == null || currentBuildingTask.isConstructed)
                    {
                        currentState = VillagerState.Idle;
                        return;
                    }

                    // Binaya doğru dön
                    Vector3 lookDir = (currentBuildingTask.transform.position - transform.position).normalized;
                    lookDir.y = 0;
                    if (lookDir != Vector3.zero)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 10f);
                    }

                    // İnşaat ilerlemesini artır (Saniyede %25)
                    currentBuildingTask.AddConstructionProgress(25f * Time.deltaTime);
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
                    if (currentBuildingTask == null || currentBuildingTask.currentHp <= 0 || currentBuildingTask.currentHp >= currentBuildingTask.maxHp)
                    {
                        currentState = VillagerState.Idle;
                        return;
                    }

                    Collider repCol = currentBuildingTask.GetComponent<Collider>();
                    float distToRep = (repCol != null) 
                        ? Vector3.Distance(transform.position, repCol.ClosestPoint(transform.position)) 
                        : Vector3.Distance(transform.position, currentBuildingTask.transform.position);

                    if (distToRep <= buildRange || (!unit.isMoving && distToRep <= 2.2f))
                    {
                        unit.StopMovementOrder();
                        currentState = VillagerState.Repairing;
                        nextBuildFxTime = Time.time + 0.1f;

                        Vector3 repLook = currentBuildingTask.transform.position - transform.position;
                        repLook.y = 0f;
                        if (repLook.sqrMagnitude > 0.0001f)
                        {
                            transform.rotation = Quaternion.LookRotation(repLook.normalized);
                        }
                    }
                    break;

                case VillagerState.Repairing:
                    if (currentBuildingTask == null || currentBuildingTask.currentHp <= 0)
                    {
                        currentState = VillagerState.Idle;
                        return;
                    }

                    if (currentBuildingTask.currentHp >= currentBuildingTask.maxHp)
                    {
                        currentBuildingTask.currentHp = currentBuildingTask.maxHp;
                        currentBuildingTask.CheckDamageVisualState();
                        currentState = VillagerState.Idle;
                        RTSHUDController.Instance?.SetAiStatus($"[TAMİRAT] \"{currentBuildingTask.displayName} tamamen onarıldı ve güçlendirildi!\"", Color.green);
                        return;
                    }

                    // Saniyede 40 HP onar (Tamirat hızı)
                    if (unit != null && !unit.isManualMoveOrder && (unit.isMoving || unit.commandDestination != Vector3.zero))
                    {
                        unit.StopMovementOrder();
                    }

                    Vector3 repFace = currentBuildingTask.transform.position - transform.position;
                    repFace.y = 0f;
                    if (repFace.sqrMagnitude > 0.0001f)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(repFace.normalized), Time.deltaTime * 16f);
                    }

                    currentBuildingTask.currentHp += Mathf.RoundToInt(40f * Time.deltaTime);
                    currentBuildingTask.currentHp = Mathf.Min(currentBuildingTask.currentHp, currentBuildingTask.maxHp);
                    currentBuildingTask.CheckDamageVisualState();
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
                if (b != null && b.team == myTeam && b.buildingType == RTSBuildingType.TownCenter && b.currentHp > 0)
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
