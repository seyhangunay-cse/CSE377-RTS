using System;
using System.Collections.Generic;
using System.Text;

namespace CSE377.AI
{
    /// <summary>
    /// Hilesiz (Fog-of-War uyumlu) Stratejik İstihbarat, Ekonomi/İşçi Dengesi ve Öneri Motoru.
    /// Yalnızca oyuncunun aktif görüşündeki ve sis hafızasında keşfettiği bilgilere göre:
    /// 1. Sisli bölge içi düşman hareketlerini ve binalarını analiz eder.
    /// 2. Kaynak stokları, işçi dağılımı ve nüfusa göre odaklanılması gereken kaynağı ve aksiyonları önerir.
    /// 3. Bütçe ve nüfus limitlerini aşmayacak uygulanabilir ActionPlan üretir.
    /// </summary>
    public static class RTSAiAdvisor
    {
        public struct EnemyIntelAnalysis
        {
            public bool hasLiveSighting;
            public bool hasFogMemory;
            public int liveEnemyUnits;
            public int rememberedEnemyUnits;
            public int discoveredEnemyBuildings;
            public string primaryThreatZone;
            public string dominantEnemyClass;   // "cavalry", "ranged", "infantry", "siege", "unknown"
            public string recommendedCounter;   // "spearman", "cavalry", "archer", "trebuchet", "scout"
            public string narrativeTr;
        }

        public struct EconomyAnalysis
        {
            public string focusResource;        // "wood", "food", "gold"
            public string focusResourceTr;      // "ODUN", "GIDA", "ALTIN"
            public string secondaryResource;
            public int recommendedReassignCount;
            public bool needsHouseSoon;
            public bool shouldTrainVillager;
            public bool needsGoldScout;
            public string narrativeTr;
        }

        public static string TranslateZoneTr(string zone)
        {
            if (string.IsNullOrEmpty(zone)) return "Kuzey Geçidi";
            switch (zone.ToLowerInvariant().Trim())
            {
                case "main_base":
                case "town_center":
                    return "Ana Üssümüz";
                case "north_gate":
                    return "Kuzey Geçidi";
                case "west_flank":
                    return "Batı Kanadı";
                case "east_flank":
                    return "Doğu Kanadı";
                case "enemy_base":
                case "enemy_camp":
                    return "Düşman Üssü";
                case "gold":
                    return "Altın Yatakları";
                default:
                    return zone;
            }
        }

        public static string TranslateBuildingTr(string bType)
        {
            if (string.IsNullOrEmpty(bType)) return "Yapı";
            switch (bType.ToLowerInvariant().Trim())
            {
                case "towncenter":
                case "town_center":
                    return "Şehir Merkezi";
                case "barracks": return "Kışla";
                case "archeryrange":
                case "archery_range":
                    return "Okçu Poligonu";
                case "blacksmith": return "Demirci";
                case "castle": return "Kale";
                case "defensetower":
                case "tower":
                    return "Savunma Kulesi";
                case "siegeworkshop":
                case "siege_workshop":
                    return "Kuşatma Atölyesi";
                case "farm": return "Tarla";
                case "house": return "Ev";
                case "market": return "Pazar";
                default: return bType;
            }
        }

        public static string TranslateUnitTr(string uType)
        {
            if (string.IsNullOrEmpty(uType)) return "Birlik";
            switch (uType.ToLowerInvariant().Trim())
            {
                case "spearman": return "Mızrakçı";
                case "archer": return "Okçu";
                case "crossbowman": return "Arbaletçi";
                case "cavalry": return "Ağır Süvari";
                case "scout": return "Keşif Atlısı";
                case "swordsman": return "Kılıçlı";
                case "trebuchet": return "Mancınık";
                case "villager": return "Köylü";
                default: return uType;
            }
        }

        public static int GetArmyCount(AiSafeSnapshot snap, string unitType)
        {
            if (snap == null || snap.army == null) return 0;
            int total = 0;
            for (int i = 0; i < snap.army.Count; i++)
            {
                var g = snap.army[i];
                if (g != null && string.Equals(g.type, unitType, StringComparison.OrdinalIgnoreCase))
                {
                    total += g.count;
                }
            }
            return total;
        }

        public static int GetMilitaryTotal(AiSafeSnapshot snap)
        {
            if (snap == null || snap.army == null) return 0;
            int total = 0;
            for (int i = 0; i < snap.army.Count; i++)
            {
                var g = snap.army[i];
                if (g != null && !string.Equals(g.type, "villager", StringComparison.OrdinalIgnoreCase))
                {
                    total += g.count;
                }
            }
            return total;
        }

        public static int GetBuildingCount(AiSafeSnapshot snap, string buildingType)
        {
            if (snap == null || snap.friendly_buildings == null) return 0;
            int total = 0;
            for (int i = 0; i < snap.friendly_buildings.Count; i++)
            {
                var b = snap.friendly_buildings[i];
                if (b != null && string.Equals(b.type, buildingType, StringComparison.OrdinalIgnoreCase))
                {
                    total += b.count;
                }
            }
            return total;
        }

        public static EnemyIntelAnalysis AnalyzeEnemyIntel(AiSafeSnapshot snap)
        {
            var res = new EnemyIntelAnalysis
            {
                primaryThreatZone = "north_gate",
                dominantEnemyClass = "unknown",
                recommendedCounter = "spearman",
                narrativeTr = ""
            };

            if (snap == null)
            {
                res.narrativeTr = "Anlık saha verisi bekleniyor.";
                return res;
            }

            int cavScore = 0;
            int rangedScore = 0;
            int infScore = 0;
            int siegeScore = 0;
            var zoneDanger = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var seenParts = new List<string>();
            var memoryParts = new List<string>();

            if (snap.visible_enemies != null)
            {
                foreach (var e in snap.visible_enemies)
                {
                    if (e == null || e.count <= 0) continue;
                    res.hasLiveSighting = true;
                    res.liveEnemyUnits += e.count;
                    AccumulateEnemyThreat(e.type, e.count, e.zone, 3, ref cavScore, ref rangedScore, ref infScore, ref siegeScore, zoneDanger);
                    seenParts.Add($"{e.count} {TranslateUnitTr(e.type)} ({TranslateZoneTr(e.zone)})");
                }
            }

            var fog = snap.fog_intel;
            if (fog != null && fog.last_known_enemies != null)
            {
                foreach (var mem in fog.last_known_enemies)
                {
                    if (mem == null || mem.count <= 0 || mem.currently_visible) continue;
                    res.hasFogMemory = true;
                    res.rememberedEnemyUnits += mem.count;
                    int weight = (mem.seconds_ago <= 45) ? 2 : 1;
                    AccumulateEnemyThreat(mem.type, mem.count, mem.zone, weight, ref cavScore, ref rangedScore, ref infScore, ref siegeScore, zoneDanger);
                    memoryParts.Add($"{mem.count} {TranslateUnitTr(mem.type)} ({TranslateZoneTr(mem.zone)}, {mem.seconds_ago}sn önce)");
                }
            }

            var enemyBuildingHints = new List<string>();
            bool enemyHasArchery = false;
            bool enemyHasBarracks = false;
            bool enemyHasBlacksmith = false;
            bool enemyHasCastleOrTower = false;
            bool enemyHasSiege = false;

            if (fog != null && fog.discovered_enemy_buildings != null)
            {
                foreach (var eb in fog.discovered_enemy_buildings)
                {
                    if (eb == null || eb.count <= 0) continue;
                    res.discoveredEnemyBuildings += eb.count;
                    string bt = (eb.type ?? "").ToLowerInvariant();
                    enemyBuildingHints.Add($"{eb.count} {TranslateBuildingTr(bt)}");
                    if (bt.Contains("archery")) enemyHasArchery = true;
                    if (bt.Contains("barracks")) enemyHasBarracks = true;
                    if (bt.Contains("blacksmith")) enemyHasBlacksmith = true;
                    if (bt.Contains("tower") || bt.Contains("castle")) enemyHasCastleOrTower = true;
                    if (bt.Contains("siege")) enemyHasSiege = true;

                    if (!zoneDanger.ContainsKey(eb.zone ?? "enemy_base"))
                    {
                        zoneDanger[eb.zone ?? "enemy_base"] = 0;
                    }
                    zoneDanger[eb.zone ?? "enemy_base"] += eb.count;
                }
            }

            // En yoğun tehdit bölgesi
            int bestZoneScore = -1;
            foreach (var kv in zoneDanger)
            {
                if (kv.Value > bestZoneScore)
                {
                    bestZoneScore = kv.Value;
                    res.primaryThreatZone = kv.Key;
                }
            }

            // Rakip ağırlıklı sınıf ve karşıt birim belirleme
            if (siegeScore > 0 && siegeScore >= cavScore)
            {
                res.dominantEnemyClass = "siege";
                res.recommendedCounter = "cavalry";
            }
            else if (cavScore >= rangedScore && cavScore >= infScore && cavScore > 0)
            {
                res.dominantEnemyClass = "cavalry";
                res.recommendedCounter = "spearman";
            }
            else if (rangedScore >= cavScore && rangedScore >= infScore && rangedScore > 0)
            {
                res.dominantEnemyClass = "ranged";
                res.recommendedCounter = "cavalry";
            }
            else if (infScore > 0)
            {
                res.dominantEnemyClass = "infantry";
                res.recommendedCounter = "archer";
            }
            else if (enemyHasSiege || enemyHasArchery)
            {
                res.dominantEnemyClass = enemyHasSiege ? "siege" : "ranged";
                res.recommendedCounter = "cavalry";
            }
            else if (enemyHasBarracks || enemyHasBlacksmith)
            {
                res.dominantEnemyClass = "infantry";
                res.recommendedCounter = "archer";
            }
            else if (enemyHasCastleOrTower)
            {
                res.dominantEnemyClass = "fortified";
                res.recommendedCounter = "trebuchet";
            }

            int exploredPct = (fog != null) ? fog.explored_percent : 0;
            var sb = new StringBuilder();
            if (res.hasLiveSighting)
            {
                sb.Append($"Aktif görüşte rakip: {string.Join(", ", seenParts)}. ");
            }
            if (res.hasFogMemory)
            {
                sb.Append($"Gri sis izi: {string.Join(", ", memoryParts)}. ");
            }
            if (enemyBuildingHints.Count > 0)
            {
                sb.Append($"Sis dışı düşman yapıları: {string.Join(", ", enemyBuildingHints)}");
                if (enemyHasArchery) sb.Append(" (menzilli baskı sinyali → Süvari/Zırh etkili)");
                else if (enemyHasBlacksmith || enemyHasBarracks) sb.Append(" (piyade yığınağı → Okçu hattı etkili)");
                else if (enemyHasCastleOrTower) sb.Append(" (tahkimat → Mancınık gerekir)");
                sb.Append(". ");
            }

            if (!res.hasLiveSighting && !res.hasFogMemory && enemyBuildingHints.Count == 0)
            {
                sb.Append($"Haritanın %{exploredPct}'i açıldı; sis içinde henüz düşman birliği veya yapısı tespit edilmedi. ");
                if (fog != null && fog.seconds_until_next_raid >= 0)
                {
                    sb.Append($"İlk/sonraki akına ~{fog.seconds_until_next_raid}sn var; öncü keşif şart.");
                }
            }
            else
            {
                sb.Append($"Önerilen karşıt birlik: {TranslateUnitTr(res.recommendedCounter)} ({TranslateZoneTr(res.primaryThreatZone)}).");
            }

            res.narrativeTr = sb.ToString().Trim();
            return res;
        }

        private static void AccumulateEnemyThreat(
            string rawType, int count, string zone, int weight,
            ref int cav, ref int ranged, ref int inf, ref int siege,
            Dictionary<string, int> zoneDanger)
        {
            string t = (rawType ?? "").ToLowerInvariant();
            if (t == "cavalry" || t == "scout") cav += count * weight;
            else if (t == "archer" || t == "crossbowman") ranged += count * weight;
            else if (t == "trebuchet") siege += count * weight;
            else inf += count * weight;

            string z = string.IsNullOrEmpty(zone) ? "north_gate" : zone;
            if (!zoneDanger.ContainsKey(z)) zoneDanger[z] = 0;
            zoneDanger[z] += count * weight;
        }

        public static EconomyAnalysis AnalyzeEconomy(AiSafeSnapshot snap)
        {
            var eco = new EconomyAnalysis
            {
                focusResource = "food",
                focusResourceTr = "GIDA",
                secondaryResource = "wood"
            };

            if (snap == null)
            {
                eco.narrativeTr = "Ekonomi verisi bekleniyor.";
                return eco;
            }

            var res = snap.resources ?? new AiSafeSnapshot.ResourceState();
            var w = snap.workers ?? new AiSafeSnapshot.WorkerAllocationState();
            var pop = snap.population ?? new AiSafeSnapshot.PopulationState();
            var fog = snap.fog_intel;

            eco.needsHouseSoon = (pop.max - pop.current) <= 2 && pop.max < 60;
            eco.shouldTrainVillager = (w.total < 7) && (pop.current < pop.max) && (res.food >= 50);

            int discoveredGold = (fog != null) ? fog.discovered_gold_nodes : 1;
            int discoveredWood = (fog != null) ? fog.discovered_wood_nodes : 1;

            // Eksik ve darboğaz puanı hesapla (yüksek skor = acil odaklanılması gereken kaynak)
            float foodNeedScore = 0f;
            float woodNeedScore = 0f;
            float goldNeedScore = 0f;

            if (res.food < 120) foodNeedScore += 55f;
            else if (res.food < 220) foodNeedScore += 25f;
            if (w.food == 0 && w.total > 0) foodNeedScore += 45f;
            else if (w.food <= 1 && w.total >= 3) foodNeedScore += 20f;

            if (res.wood < 120) woodNeedScore += 50f;
            else if (res.wood < 200) woodNeedScore += 22f;
            if (eco.needsHouseSoon && res.wood < 60) woodNeedScore += 35f;
            if (w.wood == 0 && w.total > 0) woodNeedScore += 42f;
            else if (w.wood <= 1 && w.total >= 3) woodNeedScore += 18f;

            if (res.gold < 90) goldNeedScore += 48f;
            else if (res.gold < 180) goldNeedScore += 20f;
            if (w.gold == 0 && w.total >= 2) goldNeedScore += 36f;
            else if (w.gold <= 1 && w.total >= 4) goldNeedScore += 15f;

            // Altın en az keşfedildiyse köylü yönlendirmek yerine altın keşfi uyarısı
            if (discoveredGold == 0 && goldNeedScore >= 30f)
            {
                eco.needsGoldScout = true;
            }

            if (foodNeedScore >= woodNeedScore && foodNeedScore >= goldNeedScore)
            {
                eco.focusResource = "food";
                eco.focusResourceTr = "GIDA";
                eco.secondaryResource = (woodNeedScore >= goldNeedScore) ? "wood" : "gold";
            }
            else if (woodNeedScore >= foodNeedScore && woodNeedScore >= goldNeedScore)
            {
                eco.focusResource = "wood";
                eco.focusResourceTr = "ODUN";
                eco.secondaryResource = (foodNeedScore >= goldNeedScore) ? "food" : "gold";
            }
            else
            {
                eco.focusResource = "gold";
                eco.focusResourceTr = "ALTIN";
                eco.secondaryResource = (foodNeedScore >= woodNeedScore) ? "food" : "wood";
            }

            // İşçi kaydırma sayısı: boşta işçi varsa öncelik onlara; yoksa dengesiz yığılmadan çek
            int workersOnFocus = (eco.focusResource == "wood") ? w.wood : (eco.focusResource == "gold") ? w.gold : w.food;
            if (w.idle > 0)
            {
                eco.recommendedReassignCount = w.idle;
            }
            else if (workersOnFocus == 0 && w.total >= 2)
            {
                eco.recommendedReassignCount = (w.total >= 4) ? 2 : 1;
            }
            else if (workersOnFocus <= 1 && w.total >= 5)
            {
                eco.recommendedReassignCount = 1;
            }

            string reasonFocus = (eco.focusResource == "food")
                ? (w.food == 0 ? "Gıda'da hiç işçi yok" : "Köylü/piyade ve Çağ için Gıda kritik")
                : (eco.focusResource == "wood")
                    ? (eco.needsHouseSoon ? "Ev/Kışla ve okçu üretimi için Odun şart" : (w.wood == 0 ? "Odun'da işçi yok" : "İnşaat ve menzilli hat için Odun gerekli"))
                    : (eco.needsGoldScout ? "Altın stoğu düşük ama sis altında altın madeni keşfedilmeli" : (w.gold == 0 ? "Altın'da işçi yok" : "Gelişmiş birlik ve teknoloji için Altın gerekli"));

            string idleNote = w.idle > 0 ? $" ({w.idle} işçi BOŞTA!)" : "";
            string popNote = eco.needsHouseSoon ? $" • Nüfus sınıra yakın ({pop.current}/{pop.max})!" : $" • Nüfus: {pop.current}/{pop.max}";
            string discNote = (discoveredWood == 0 && eco.focusResource == "wood") ? " • Orman keşfi gerekli" : "";

            eco.narrativeTr =
                $"Kaynaklar: {res.wood} Odun, {res.food} Gıda, {res.gold} Altın{popNote}. " +
                $"İşçiler ({w.total}): {w.food} Gıda / {w.wood} Odun / {w.gold} Altın / {w.building} İnşaat / {w.idle} Boşta{idleNote}. " +
                $"ODAKLANILACAK KAYNAK: {eco.focusResourceTr} ({reasonFocus}{discNote}).";
            return eco;
        }

        public static string BuildCompactHudIntelLine(AiSafeSnapshot snap)
        {
            if (snap == null) return "İstihbarat hazırlanıyor...";
            var enemy = AnalyzeEnemyIntel(snap);
            var eco = AnalyzeEconomy(snap);
            var w = snap.workers ?? new AiSafeSnapshot.WorkerAllocationState();
            int exp = snap.fog_intel != null ? snap.fog_intel.explored_percent : 0;

            string enemyBrief = enemy.hasLiveSighting
                ? $"Görüşte {enemy.liveEnemyUnits} rakip ({TranslateUnitTr(enemy.recommendedCounter)} önerilir)"
                : (enemy.hasFogMemory || enemy.discoveredEnemyBuildings > 0)
                    ? $"Sis izi: {enemy.rememberedEnemyUnits} iz / {enemy.discoveredEnemyBuildings} yapı"
                    : "Rakip sis altında (Keşif gerek)";

            return $"Sis Açık: %{exp} | {enemyBrief}\nİşçiler ({w.total}): {w.food}🌾 {w.wood}🪵 {w.gold}🪙 {w.idle}💤 → ODAK: {eco.focusResourceTr}";
        }

        public static RTSActionPlan BuildIntelligentPlan(string userPrompt, AiSafeSnapshot snapshot)
        {
            if (snapshot == null) snapshot = AiSafeSnapshot.CreateMockSnapshot();

            string q = (userPrompt ?? "").ToLowerInvariant().Trim();
            bool isRedTeam = snapshot.team_id == "RED";
            string cmdPrefix = isRedTeam ? "[KIRMIZI AI KOMUTAN] " : "[MAVİ AI KOMUTAN] ";

            var eco = AnalyzeEconomy(snapshot);
            var enemy = AnalyzeEnemyIntel(snapshot);
            var res = snapshot.resources ?? new AiSafeSnapshot.ResourceState();
            var w = snapshot.workers ?? new AiSafeSnapshot.WorkerAllocationState();
            var pop = snapshot.population ?? new AiSafeSnapshot.PopulationState();

            int remWood = res.wood;
            int remFood = res.food;
            int remGold = res.gold;
            int remPop = Math.Max(0, pop.max - pop.current);

            bool asksFullAnalysis = string.IsNullOrEmpty(q)
                || q.Contains("analiz") || q.Contains("durum") || q.Contains("öneri") || q.Contains("oneri")
                || q.Contains("tavsiye") || q.Contains("odak") || q.Contains("ne yap") || q.Contains("rakip")
                || q.Contains("işçi") || q.Contains("isci") || q.Contains("dağıt") || q.Contains("dagit");

            // 1. KAPSAMLI SİS + RAKİP + KAYNAK + İŞÇİ ANALİZİ VE ODAK ÖNERİSİ
            if (asksFullAnalysis && !q.Contains("kuşat") && !q.Contains("kusat") && !q.Contains("saldır") && !q.Contains("saldir"))
            {
                var plan = new RTSActionPlan();
                plan.confidence = 0.95f;
                plan.summary = TrimSummary($"{cmdPrefix}{eco.narrativeTr} | SİS İSTİHBARATI: {enemy.narrativeTr}");

                // Adım 1: Nüfus dolmaya yakınsa Ev (30 Odun)
                if (eco.needsHouseSoon && remWood >= 30 && w.total > 0)
                {
                    remWood -= 30;
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "BUILD",
                        building_type = "house",
                        target_zone = "town_center",
                        reason = $"Nüfus limitine yaklaşıldı ({pop.current}/{pop.max}); yeni birlikler için +5 kapasiteli Ev kur."
                    });
                }

                // Adım 2: İşçileri odaklanılacak kaynağa yönlendir (veya altın madeni yoksa önce keşfet)
                if (eco.needsGoldScout && eco.focusResource == "gold" && GetMilitaryTotal(snapshot) > 0)
                {
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "SCOUT",
                        unit_type = "soldier",
                        count = 1,
                        target_zone = "gold",
                        reason = "Altın stoğu düşük ve henüz keşfedilmemiş altın madenleri var; 1 gözcüyü altın keşfine çıkar."
                    });
                }
                else if (eco.recommendedReassignCount > 0 && w.total > 0)
                {
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "ASSIGN_GATHER",
                        unit_type = "villager",
                        count = eco.recommendedReassignCount,
                        target_zone = eco.focusResource,
                        reason = $"{eco.focusResourceTr} üretimine odaklanmak için {eco.recommendedReassignCount} köylüyü bu kaynağa yönlendir."
                    });
                }

                // Adım 3: Ekonomi küçükse ve bütçe uygunsa köylü ekle
                if (eco.shouldTrainVillager && remFood >= 50 && remPop >= 1)
                {
                    remFood -= 50;
                    remPop -= 1;
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "TRAIN_UNIT",
                        unit_type = "villager",
                        count = 1,
                        target_zone = "town_center",
                        reason = $"Toplam işçi sayısı az ({w.total}); ekonomiyi hızlandırmak için 1 yeni köylü eğit."
                    });
                }

                // Adım 4: Sisli bölge açıksa/kapalıysa gözcü veya karşıt birlik üret
                bool mapMostlyFoggy = snapshot.fog_intel == null || snapshot.fog_intel.explored_percent < 40 || (!enemy.hasLiveSighting && !enemy.hasFogMemory && enemy.discoveredEnemyBuildings == 0);
                if (mapMostlyFoggy && GetMilitaryTotal(snapshot) > 0)
                {
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "SCOUT",
                        unit_type = "soldier",
                        count = 1,
                        target_zone = "unexplored",
                        reason = $"Haritanın %{(snapshot.fog_intel != null ? snapshot.fog_intel.explored_percent : 0)}'i açık; rakip yerleşimini ve birikimini sis arkasında tespit et."
                    });
                }

                TryAddAffordableCounterUnit(plan, enemy.recommendedCounter, enemy.primaryThreatZone, ref remWood, ref remFood, ref remGold, ref remPop,
                    enemy.hasLiveSighting || enemy.hasFogMemory || enemy.discoveredEnemyBuildings > 0
                        ? $"Sis istihbaratına ({TranslateEnemyClassTr(enemy.dominantEnemyClass)}) karşı en etkili karşıt birim."
                        : "Erken akınlara karşı savunma çekirdeği oluştur.");

                // En az 1 somut aksiyon garantisi
                if (plan.actions.Count == 0 && GetMilitaryTotal(snapshot) > 0)
                {
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "SCOUT",
                        unit_type = "soldier",
                        count = 1,
                        target_zone = "unexplored",
                        reason = "Mevcut kaynaklar sınırlı olduğundan sisli bölgelerde ücretsiz keşif icra et."
                    });
                }
                else if (plan.actions.Count == 0 && w.total > 0)
                {
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "ASSIGN_GATHER",
                        unit_type = "villager",
                        count = 1,
                        target_zone = eco.focusResource,
                        reason = $"Bütçeyi toparlamak için köylüleri {eco.focusResourceTr} kaynağına odakla."
                    });
                }

                return plan;
            }

            // 2. KUŞATMA & KARARGAH YIKIMI
            if (q.Contains("kuşat") || q.Contains("kusat") || q.Contains("karargah") || q.Contains("üs") || q.Contains("üssü") || q.Contains("yık") || q.Contains("kale"))
            {
                var plan = new RTSActionPlan();
                plan.confidence = 0.94f;
                string zoneNote = enemy.discoveredEnemyBuildings > 0
                    ? $"Tespit edilen {enemy.discoveredEnemyBuildings} düşman yapısı üzerine taarruz başlatılıyor."
                    : "Kuzey düşman karargahı hattına keşifli kuşatma başlatılıyor.";
                plan.summary = TrimSummary($"{cmdPrefix}Kuşatma Doktrini: {zoneNote} ({eco.focusResourceTr} kaynağına odaklanmanız önerilir).");

                TryAddAffordableUnit(plan, "spearman", 3, "north_gate", ref remWood, ref remFood, ref remGold, ref remPop,
                    "Kuşatma hattında öncü piyade desteği.");

                plan.actions.Add(new RTSAction
                {
                    action_type = "ATTACK",
                    unit_type = "soldier",
                    count = 10,
                    target_zone = "enemy_base",
                    reason = "Düşman ana kalesini ve karargahını doğrudan kuşatıp imha et."
                });
                return plan;
            }

            // 3. TAARRUZ / KARŞI SALDIRI
            if (q.Contains("saldır") || q.Contains("saldir") || q.Contains("taarruz") || q.Contains("hücum") || q.Contains("hucum") || q.Contains("karşı") || q.Contains("karsi") || q.Contains("yok et") || q.Contains("vur"))
            {
                var plan = new RTSActionPlan();
                plan.confidence = 0.92f;
                string atkZone = enemy.hasLiveSighting || enemy.hasFogMemory ? enemy.primaryThreatZone : "north_gate";
                plan.summary = TrimSummary($"{cmdPrefix}Karşı Taarruz ({TranslateZoneTr(atkZone)}): {enemy.narrativeTr}");

                plan.actions.Add(new RTSAction
                {
                    action_type = "ATTACK",
                    unit_type = "soldier",
                    count = 8,
                    target_zone = atkZone,
                    reason = $"{TranslateZoneTr(atkZone)} bölgesindeki düşman hedeflerine taarruz et."
                });

                TryAddAffordableCounterUnit(plan, enemy.recommendedCounter, atkZone, ref remWood, ref remFood, ref remGold, ref remPop,
                    $"Taarruzda rakip sınıfına ({TranslateEnemyClassTr(enemy.dominantEnemyClass)}) karşı destek takviyesi.");

                return plan;
            }

            // 4. EKONOMİ / TARLA / KAYNAK / KÖYLÜ
            if (q.Contains("ekonomi") || q.Contains("kaynak") || q.Contains("gıda") || q.Contains("gida") || q.Contains("odun") || q.Contains("tarla") || q.Contains("çiftlik") || q.Contains("ciftlik") || q.Contains("köylü") || q.Contains("koylu"))
            {
                var plan = new RTSActionPlan();
                plan.confidence = 0.92f;
                plan.summary = TrimSummary($"{cmdPrefix}Ekonomi & İşçi Planı: {eco.narrativeTr}");

                if (w.total > 0)
                {
                    int moveWorkers = Math.Max(1, eco.recommendedReassignCount);
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "ASSIGN_GATHER",
                        unit_type = "villager",
                        count = moveWorkers,
                        target_zone = eco.focusResource,
                        reason = $"Ekonomideki öncelikli açık: {eco.focusResourceTr}. {moveWorkers} köylüyü buraya odakla."
                    });
                }

                TryAddAffordableUnit(plan, "villager", 2, "town_center", ref remWood, ref remFood, ref remGold, ref remPop,
                    "Kaynak toplama hızını artırmak için bütçenin yettiği kadar yeni köylü eğit.");

                if (remWood >= 60 && w.total > 0)
                {
                    remWood -= 60;
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "BUILD",
                        building_type = "farm",
                        target_zone = "town_center",
                        reason = "Kesintisiz gıda üretimi için Şehir Merkezi yanına Tarla inşa et."
                    });
                }

                if (remWood >= 50 && remFood >= 175 && !HasTech(snapshot, "Wheelbarrow"))
                {
                    remWood -= 50;
                    remFood -= 175;
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "RESEARCH",
                        building_type = "Wheelbarrow",
                        target_zone = "town_center",
                        reason = "Köylü taşıma kapasitesini ve toplama hızını %25 artıran El Arabası teknolojisi."
                    });
                }

                return plan;
            }

            // 5. ÇAĞ ATLAMA & TEKNOLOJİ
            if (q.Contains("çağ") || q.Contains("cag") || q.Contains("feodal") || q.Contains("atla") || q.Contains("geliş") || q.Contains("gelis") || q.Contains("demirci") || q.Contains("teknoloji") || q.Contains("silah") || q.Contains("zırh") || q.Contains("zirh"))
            {
                var plan = new RTSActionPlan();
                plan.confidence = 0.90f;
                plan.summary = TrimSummary($"{cmdPrefix}Teknoloji & Çağ Planı: {eco.narrativeTr}");

                if (GetBuildingCount(snapshot, "blacksmith") == 0 && remWood >= 150)
                {
                    remWood -= 150;
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "BUILD",
                        building_type = "blacksmith",
                        target_zone = "town_center",
                        reason = "Silah ve zırh araştırmalarını açmak için Demirci Atölyesi inşası."
                    });
                }

                if (remFood >= 100 && remGold >= 50 && !HasTech(snapshot, "Iron_Weapons"))
                {
                    remFood -= 100;
                    remGold -= 50;
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "RESEARCH",
                        building_type = "Iron_Weapons",
                        target_zone = "town_center",
                        reason = "Yakın dövüş birliklerinin saldırı gücünü artıran Demir Silahlar geliştirmesi."
                    });
                }

                if (plan.actions.Count == 0 && w.total > 0)
                {
                    string neededRes = (remFood < 200) ? "food" : (remWood < 150) ? "wood" : "gold";
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "ASSIGN_GATHER",
                        unit_type = "villager",
                        count = Math.Max(1, w.idle > 0 ? w.idle : 2),
                        target_zone = neededRes,
                        reason = $"Demirci/Çağ atlama maliyetini toplamak için köylüleri {(neededRes == "food" ? "Gıda" : neededRes == "wood" ? "Odun" : "Altın")} kaynağına odakla."
                    });
                }

                return plan;
            }

            // 6. TAHKİMAT & KULE & SUR
            if (q.Contains("kule") || q.Contains("sur") || q.Contains("duvar") || q.Contains("tahkim") || q.Contains("kapı") || q.Contains("kapi"))
            {
                var plan = new RTSActionPlan();
                plan.confidence = 0.91f;
                string zone = enemy.hasLiveSighting || enemy.hasFogMemory ? enemy.primaryThreatZone : "north_gate";
                plan.summary = TrimSummary($"{cmdPrefix}Savunma Tahkimatı ({TranslateZoneTr(zone)}): {eco.narrativeTr}");

                if (remWood >= 125 && remGold >= 50)
                {
                    remWood -= 125;
                    remGold -= 50;
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "BUILD",
                        building_type = "tower",
                        target_zone = zone,
                        reason = $"{TranslateZoneTr(zone)} hattına otomatik ok atan Savunma Kulesi inşası."
                    });
                }
                else if (w.total > 0)
                {
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "ASSIGN_GATHER",
                        unit_type = "villager",
                        count = 2,
                        target_zone = (remWood < 125) ? "wood" : "gold",
                        reason = "Savunma Kulesi (125 Odun, 50 Altın) için eksik kaynağı hızla topla."
                    });
                }

                TryAddAffordableUnit(plan, "archer", 2, zone, ref remWood, ref remFood, ref remGold, ref remPop,
                    "Kule çevresinde menzilli savunma desteği.");
                return plan;
            }

            // 7. KEŞİF / HARİTA / SİS / ALTIN
            if (q.Contains("keşif") || q.Contains("kesif") || q.Contains("scout") || q.Contains("harita") || q.Contains("sis") || q.Contains("altın") || q.Contains("altin") || q.Contains("maden"))
            {
                string focus = (q.Contains("altın") || q.Contains("altin") || q.Contains("maden") || eco.needsGoldScout) ? "gold" : "unexplored";
                string focusTr = focus == "gold" ? "sis altındaki altın madenlerini" : "haritadaki sisli karanlık bölgeleri";

                var plan = new RTSActionPlan();
                plan.confidence = 0.95f;
                plan.summary = TrimSummary($"{cmdPrefix}Sis Keşif Raporu: {enemy.narrativeTr} Öncü birlik {focusTr} açıyor.");

                plan.actions.Add(new RTSAction
                {
                    action_type = "SCOUT",
                    unit_type = "soldier",
                    count = 1,
                    target_zone = focus,
                    reason = $"Sisli bölgeyi açıp {focusTr} ve rakip hazırlığını tespit et."
                });
                return plan;
            }

            // 8. EV & NÜFUS
            if (q.Contains("ev") || q.Contains("nüfus") || q.Contains("nufus") || q.Contains("kapasite") || q.Contains("barınak"))
            {
                var plan = new RTSActionPlan();
                plan.confidence = 0.90f;
                plan.summary = TrimSummary($"{cmdPrefix}Nüfus Planı ({pop.current}/{pop.max}): {eco.narrativeTr}");

                if (remWood >= 30)
                {
                    remWood -= 30;
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "BUILD",
                        building_type = "house",
                        target_zone = "town_center",
                        reason = "Nüfus kapasitesini +5 artırmak için Ev inşa et."
                    });
                }
                else if (w.total > 0)
                {
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "ASSIGN_GATHER",
                        unit_type = "villager",
                        count = 2,
                        target_zone = "wood",
                        reason = "Ev (30 Odun) inşa etmek için köylüleri Odun toplamaya odakla."
                    });
                }
                return plan;
            }

            // 9. ÖZEL BİRLİK İSTEKLERİ (OKÇU / SÜVARİ)
            if (q.Contains("okçu") || q.Contains("okcu"))
            {
                var plan = new RTSActionPlan();
                plan.confidence = 0.90f;
                plan.summary = TrimSummary($"{cmdPrefix}Okçu Sevkiyatı ({TranslateZoneTr(enemy.primaryThreatZone)}): {eco.narrativeTr}");
                TryAddAffordableUnit(plan, "archer", 4, enemy.primaryThreatZone, ref remWood, ref remFood, ref remGold, ref remPop,
                    "Menzilli ateş gücü sağlamak için bütçe dahilinde okçu eğit.");
                if (plan.actions.Count == 0 && w.total > 0)
                {
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "ASSIGN_GATHER",
                        unit_type = "villager",
                        count = 2,
                        target_zone = (remWood < 50) ? "wood" : "gold",
                        reason = "Okçu (50 Odun, 30 Altın) üretebilmek için eksik kaynağa odaklan."
                    });
                }
                return plan;
            }

            if (q.Contains("süvari") || q.Contains("suvari") || q.Contains("atlı") || q.Contains("atli"))
            {
                var plan = new RTSActionPlan();
                plan.confidence = 0.90f;
                plan.summary = TrimSummary($"{cmdPrefix}Süvari Sevkiyatı ({TranslateZoneTr(enemy.primaryThreatZone)}): {eco.narrativeTr}");
                TryAddAffordableUnit(plan, "cavalry", 3, enemy.primaryThreatZone, ref remWood, ref remFood, ref remGold, ref remPop,
                    "Hızlı baskın ve okçuları ezmek için bütçe dahilinde süvari eğit.");
                if (plan.actions.Count == 0 && w.total > 0)
                {
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "ASSIGN_GATHER",
                        unit_type = "villager",
                        count = 2,
                        target_zone = (remFood < 100) ? "food" : "gold",
                        reason = "Süvari (100 Gıda, 80 Altın) için eksik kaynağa köylü odakla."
                    });
                }
                return plan;
            }

            // 10. VARSAYILAN DİNAMİK TAKTİK + EKONOMİ PLANI (Kuzey Kapısını Savun vb.)
            var defPlan = new RTSActionPlan();
            defPlan.confidence = 0.91f;
            defPlan.summary = TrimSummary($"{cmdPrefix}{enemy.narrativeTr} | {eco.narrativeTr}");

            TryAddAffordableCounterUnit(defPlan, enemy.recommendedCounter, enemy.primaryThreatZone, ref remWood, ref remFood, ref remGold, ref remPop,
                $"Sis içi düşman analizine ({TranslateEnemyClassTr(enemy.dominantEnemyClass)}) göre karşıt birlik eğitimi.");

            if (GetArmyCount(snapshot, enemy.recommendedCounter) > 0)
            {
                defPlan.actions.Add(new RTSAction
                {
                    action_type = "MOVE_GROUP",
                    unit_type = enemy.recommendedCounter,
                    count = Math.Min(6, GetArmyCount(snapshot, enemy.recommendedCounter)),
                    target_zone = enemy.primaryThreatZone,
                    reason = $"Mevcut {TranslateUnitTr(enemy.recommendedCounter)} birliklerini {TranslateZoneTr(enemy.primaryThreatZone)} mevzisine çek."
                });
            }

            if (eco.recommendedReassignCount > 0 && w.total > 0)
            {
                defPlan.actions.Add(new RTSAction
                {
                    action_type = "ASSIGN_GATHER",
                    unit_type = "villager",
                    count = eco.recommendedReassignCount,
                    target_zone = eco.focusResource,
                    reason = $"{eco.focusResourceTr} üretimini güçlendirmek için işçi yönlendirmesi."
                });
            }

            if (defPlan.actions.Count == 0 && GetMilitaryTotal(snapshot) > 0)
            {
                defPlan.actions.Add(new RTSAction
                {
                    action_type = "SCOUT",
                    unit_type = "soldier",
                    count = 1,
                    target_zone = "unexplored",
                    reason = "Kaynaklar yetersiz olduğundan harita sisini açarak düşman durumunu öğren."
                });
            }
            return defPlan;
        }

        private static void TryAddAffordableCounterUnit(
            RTSActionPlan plan, string unitType, string zone,
            ref int remWood, ref int remFood, ref int remGold, ref int remPop, string reason)
        {
            if (TryAddAffordableUnit(plan, unitType, 3, zone, ref remWood, ref remFood, ref remGold, ref remPop, reason))
                return;

            // Pahalı birim (ör. cavalry/trebuchet) karşılanamıyorsa spearmen/archer dene
            if (unitType != "spearman" && TryAddAffordableUnit(plan, "spearman", 2, zone, ref remWood, ref remFood, ref remGold, ref remPop, reason + " (bütçe dostu alternatif)"))
                return;

            TryAddAffordableUnit(plan, "archer", 2, zone, ref remWood, ref remFood, ref remGold, ref remPop, reason + " (bütçe dostu alternatif)");
        }

        private static bool TryAddAffordableUnit(
            RTSActionPlan plan, string unitType, int desiredCount, string zone,
            ref int remWood, ref int remFood, ref int remGold, ref int remPop, string reason)
        {
            if (remPop <= 0) return false;
            var cost = GetCost(unitType);
            int maxByWood = (cost.wood > 0) ? remWood / cost.wood : desiredCount;
            int maxByFood = (cost.food > 0) ? remFood / cost.food : desiredCount;
            int maxByGold = (cost.gold > 0) ? remGold / cost.gold : desiredCount;

            int count = Math.Min(desiredCount, Math.Min(remPop, Math.Min(maxByWood, Math.Min(maxByFood, maxByGold))));
            if (count <= 0) return false;

            remWood -= cost.wood * count;
            remFood -= cost.food * count;
            remGold -= cost.gold * count;
            remPop -= count;

            plan.actions.Add(new RTSAction
            {
                action_type = "TRAIN_UNIT",
                unit_type = unitType,
                count = count,
                target_zone = zone,
                reason = reason
            });
            return true;
        }

        private static (int wood, int food, int gold) GetCost(string unitType)
        {
            switch ((unitType ?? "").ToLowerInvariant())
            {
                case "villager": return (0, 50, 0);
                case "spearman": return (0, 60, 20);
                case "archer": return (50, 0, 30);
                case "cavalry": return (0, 100, 80);
                case "swordsman": return (0, 60, 30);
                case "crossbowman": return (60, 0, 40);
                case "trebuchet": return (200, 0, 150);
                default: return (0, 60, 20);
            }
        }

        private static string TranslateEnemyClassTr(string cls)
        {
            switch ((cls ?? "").ToLowerInvariant())
            {
                case "cavalry": return "Süvari/Atlı";
                case "ranged": return "Menzilli (Okçu)";
                case "infantry": return "Piyade";
                case "siege": return "Kuşatma/Mancınık";
                case "fortified": return "Kule/Tahkimat";
                default: return "Bilinmeyen";
            }
        }

        private static bool HasTech(AiSafeSnapshot snap, string techId)
        {
            if (snap == null || snap.unlocked_tech == null) return false;
            for (int i = 0; i < snap.unlocked_tech.Count; i++)
            {
                if (string.Equals(snap.unlocked_tech[i], techId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string TrimSummary(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return (text.Length > 640) ? text.Substring(0, 637) + "..." : text;
        }
    }
}
