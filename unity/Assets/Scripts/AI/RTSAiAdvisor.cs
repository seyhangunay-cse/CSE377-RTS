using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CSE377.AI
{
    /// <summary>
    /// Hilesiz (Fog-of-War uyumlu) Akıllı Prompt Anlama, Stratejik İstihbarat, Ekonomi/İşçi Dengesi ve Öneri Motoru.
    /// Kullanıcının serbest Türkçe/İngilizce komut ve sorularını bağlamına göre analiz eder:
    /// 1. Karşıtlık (Counter) sorularını (örn: "Okçulara karşı ne basayım?") kendi üretim emriyle karıştırmadan yanıtlar.
    /// 2. Çağ/Teknoloji/Bina gereksinimleri ile eksik kaynak farklarını net hesaplar.
    /// 3. Akın hazırlığı, sis içi düşman izleri ve kaynak/işçi odak tavsiyelerini dinamik bütçeyle birleştirir.
    /// 4. Çoklu birim/bina/yön/adet komutlarını (örn: "Batı kanadına 3 okçu ve 2 mızrakçı gönder") ayrıştırır.
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
            public string dominantEnemyClass;   // "cavalry", "ranged", "infantry", "siege", "fortified", "unknown"
            public string recommendedCounter;   // "spearman", "cavalry", "archer", "trebuchet"
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

        public static string NormalizeTr(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            var sb = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                switch (c)
                {
                    case 'Ç': case 'ç': sb.Append('c'); break;
                    case 'Ğ': case 'ğ': sb.Append('g'); break;
                    case 'İ': case 'I': case 'ı': sb.Append('i'); break;
                    case 'Ö': case 'ö': sb.Append('o'); break;
                    case 'Ş': case 'ş': sb.Append('s'); break;
                    case 'Ü': case 'ü': sb.Append('u'); break;
                    default: sb.Append(char.ToLowerInvariant(c)); break;
                }
            }
            return sb.ToString().Trim();
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
                case "market": return "Pazar Yeri";
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
                case "swordsman": return "Kılıçlı Piyade";
                case "trebuchet": return "Mancınık";
                case "villager": return "Köylü";
                default: return uType;
            }
        }

        private static string TranslateResourceTr(string resKey)
        {
            switch ((resKey ?? "").ToLowerInvariant())
            {
                case "wood": return "ODUN";
                case "gold": return "ALTIN";
                default: return "GIDA";
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

        public static int GetArmyCount(AiSafeSnapshot snap, string unitType)
        {
            if (snap == null || snap.army == null) return 0;
            int total = 0;
            for (int i = 0; i < snap.army.Count; i++)
            {
                var g = snap.army[i];
                if (g != null && string.Equals(g.type, unitType, StringComparison.OrdinalIgnoreCase))
                    total += g.count;
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
                    total += g.count;
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
                    total += b.count;
            }
            return total;
        }

        public static int GetAgeLevel(AiSafeSnapshot snap)
        {
            if (HasTech(snap, "Age_3") || HasTech(snap, "Castle_Age")) return 3;
            if (HasTech(snap, "Age_2") || HasTech(snap, "Feudal_Age")) return 2;
            return 1;
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

            int cavScore = 0, rangedScore = 0, infScore = 0, siegeScore = 0;
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
            bool enemyHasArchery = false, enemyHasBarracks = false, enemyHasBlacksmith = false, enemyHasCastleOrTower = false, enemyHasSiege = false;

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

                    string z = string.IsNullOrEmpty(eb.zone) ? "enemy_base" : eb.zone;
                    if (!zoneDanger.ContainsKey(z)) zoneDanger[z] = 0;
                    zoneDanger[z] += eb.count;
                }
            }

            int bestZoneScore = -1;
            foreach (var kv in zoneDanger)
            {
                if (kv.Value > bestZoneScore)
                {
                    bestZoneScore = kv.Value;
                    res.primaryThreatZone = kv.Key;
                }
            }

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
                sb.Append($"Aktif görüşte rakip: {string.Join(", ", seenParts)}. ");
            if (res.hasFogMemory)
                sb.Append($"Gri sis izi: {string.Join(", ", memoryParts)}. ");
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
                    sb.Append($"Akına ~{fog.seconds_until_next_raid}sn var; öncü keşif önerilir.");
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

            float foodNeedScore = 0f, woodNeedScore = 0f, goldNeedScore = 0f;

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

            if (discoveredGold == 0 && goldNeedScore >= 30f)
                eco.needsGoldScout = true;

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

            int workersOnFocus = (eco.focusResource == "wood") ? w.wood : (eco.focusResource == "gold") ? w.gold : w.food;
            if (w.idle > 0)
                eco.recommendedReassignCount = w.idle;
            else if (workersOnFocus == 0 && w.total >= 2)
                eco.recommendedReassignCount = (w.total >= 4) ? 2 : 1;
            else if (workersOnFocus <= 1 && w.total >= 5)
                eco.recommendedReassignCount = 1;

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

            string q = NormalizeTr(userPrompt);
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

            string requestedZone = DetectTargetZone(q, enemy.primaryThreatZone);
            bool isQuestion = ContainsAny(q, "?", "nasil", "neden", "ne yap", "ne bas", "ne uret", "ne kur", "ne kadar", "kac ", "ne zaman", "hangisi", "hangi ", "miyim", "miyim", "mi ", "mi?", "mu ", "mu?", "var mi", "yeter mi", "eksik");
            bool asksCounterCue = ContainsAny(q, "karsi", "durdur", "yener", "iyi gider", "counter", "savun");
            bool asksAttackCmd = ContainsAny(q, "kusat", "saldir", "taarruz", "hucum", "yok et", "oldur", "yik", "vur") && !asksCounterCue;

            // 1. KARŞITLIK / COUNTER SORULARI ("okçulara karşı ne basayım?", "atlıları nasıl durdururum?", "mancınığa ne iyi gelir?")
            if (asksCounterCue && TryDetectCounteredEnemy(q, out string enemyUnitMentioned, out string bestCounterUnit, out string counterReasonTr))
            {
                return BuildCounterAnswerPlan(cmdPrefix, snapshot, enemyUnitMentioned, bestCounterUnit, counterReasonTr, requestedZone, eco, ref remWood, ref remFood, ref remGold, ref remPop);
            }

            // 2. AKIN / BASKIN HAZIRLIK & ZAMANLAMA SORULARI ("akına kaç saniye var", "hazır mıyız?", "saldırı ne zaman?")
            if (ContainsAny(q, "akin", "baskin", "dalga", "saldiri ne zaman", "hazir mi", "guvende mi"))
            {
                return BuildRaidReadinessPlan(cmdPrefix, snapshot, eco, enemy, requestedZone, ref remWood, ref remFood, ref remGold, ref remPop);
            }

            // 3. ÇAĞ ATLAMA & TEKNOLOJİ & EKSİK HESABI ("çağ atlamak için ne eksik?", "feodal", "demirci", "el arabası")
            if (ContainsAny(q, "cag", "feodal", "kale cagi", "demirci", "teknoloji", "arastir", "el arabasi", "demir silah", "pul zirh", "fletching", "ok ucu", "balistik"))
            {
                return BuildAgeAndTechPlan(cmdPrefix, q, snapshot, eco, ref remWood, ref remFood, ref remGold, ref remPop);
            }

            // 4. BİLGİ / KARŞILAŞTIRMA / MALİYET / STRATEJİ REHBERİ ("mızrakçı mı okçu mu", "maliyetleri ne", "nasıl kazanırım")
            if (ContainsAny(q, "maliyet", "kac altin", "kac odun", "kac gida", "ne ister", "fiyat", "daha iyi", "daha guclu", "mi daha", "hangisi", "farki", "en guclu", "nasil kazan", "tavsiye et", "ozetle", "ne ise yarar", " vs "))
            {
                return BuildKnowledgeComparisonPlan(cmdPrefix, q, snapshot, eco, enemy, ref remWood, ref remFood, ref remGold, ref remPop);
            }

            // 5. BELİRLİ KAYNAK DARBOĞAZI / NÜFUS / İŞÇİ SORUNU ("altınım yok", "neden üretemiyorum", "odun az", "işçileri nereye vereyim")
            if (ContainsAny(q, "altin yok", "altinim yok", "altin az", "altin yetm", "odun yok", "odunum yok", "odun az", "odun yetm",
                "gida yok", "gidam yok", "gida az", "gida yetm", "yiyecek yok", "uretemiyorum", "basamiyorum", "yapamiyorum",
                "nufus dol", "nufus yetm", "bosta koylu", "bosta isci", "isci dagit", "nereye ver", "odaklanmaliyim", "hangi kaynak"))
            {
                return BuildBottleneckDiagnosisPlan(cmdPrefix, q, snapshot, eco, enemy, ref remWood, ref remFood, ref remGold, ref remPop);
            }

            // 6. KUŞATMA & TAARRUZ EMİRLERİ
            if (asksAttackCmd)
            {
                var plan = new RTSActionPlan { confidence = 0.94f };
                bool toBase = ContainsAny(q, "karargah", "us", "kale", "merkez", "camp");
                string atkZone = toBase ? "enemy_base" : requestedZone;
                int mil = GetMilitaryTotal(snapshot);
                int reqCount = ExtractCountNearKeywords(q, new[] { "asker", "ordu", "birlik", "mizrakci", "okcu", "suvari", "mancinik", "kilicli" }, 10);

                if (mil == 0)
                {
                    plan.summary = TrimSummary($"{cmdPrefix}Dikkat: Şu anda sahada hiç muharip askerimiz yok, önce ordu kurmalıyız! {eco.narrativeTr}");
                    TryAddAffordableCounterUnit(plan, enemy.recommendedCounter, atkZone, ref remWood, ref remFood, ref remGold, ref remPop,
                        "Taarruz öncesi acil ilk muharip birlik eğitimi.");
                    if (plan.actions.Count == 0 && w.total > 0)
                    {
                        plan.actions.Add(new RTSAction { action_type = "ASSIGN_GATHER", unit_type = "villager", count = Math.Max(1, w.idle > 0 ? w.idle : 2), target_zone = "food", reason = "İlk askerleri basabilmek için gıda üretimine odaklan." });
                    }
                    return plan;
                }

                plan.summary = TrimSummary($"{cmdPrefix}Taarruz Emri ({TranslateZoneTr(atkZone)} • {mil} muharip hazır): {enemy.narrativeTr}");
                plan.actions.Add(new RTSAction
                {
                    action_type = "ATTACK",
                    unit_type = "soldier",
                    count = Math.Min(20, Math.Max(mil, reqCount)),
                    target_zone = atkZone,
                    reason = $"{TranslateZoneTr(atkZone)} bölgesindeki düşman hedeflerine taarruz et."
                });
                TryAddAffordableCounterUnit(plan, enemy.recommendedCounter, atkZone, ref remWood, ref remFood, ref remGold, ref remPop,
                    $"Taarruz hattına {TranslateEnemyClassTr(enemy.dominantEnemyClass)} tehdidine karşı destek eğitimi.");
                return plan;
            }

            // 7. KEŞİF / SİS AÇMA / ALTIN MADENİ BULMA EMİRLERİ
            if (ContainsAny(q, "kesif", "kesfet", "scout", "harita", "dolas", "sis", "maden") && !ContainsAny(q, "analiz", "durum", "rapor", "oner"))
            {
                string focus = ContainsAny(q, "altin", "maden") ? "gold" : "unexplored";
                string focusTr = (focus == "gold") ? "keşfedilmemiş altın yataklarını" : "sis altındaki karanlık bölgeleri";
                int reqCount = ExtractCountNearKeywords(q, new[] { "asker", "gozcu", "scout", "atli", "birlik" }, 1);
                var plan = new RTSActionPlan { confidence = 0.95f };
                plan.summary = TrimSummary($"{cmdPrefix}Sis Keşif Raporu: {enemy.narrativeTr} Öncü birlik {focusTr} açıyor.");

                if (GetMilitaryTotal(snapshot) > 0)
                {
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "SCOUT",
                        unit_type = "soldier",
                        count = Math.Min(GetMilitaryTotal(snapshot), reqCount),
                        target_zone = focus,
                        reason = $"Sisli bölgeyi açıp {focusTr} ve rakip yerleşimini tespit et."
                    });
                }
                else
                {
                    TryAddAffordableUnit(plan, "spearman", 1, requestedZone, ref remWood, ref remFood, ref remGold, ref remPop,
                        "Keşif çıkaracak askerimiz kalmadığı için 1 öncü birim üret.");
                }
                return plan;
            }

            // 8. AÇIK ÇOKLU VEYA TEKİL BİNA / BİRLİK / SEVK EMİRLERİ
            // Örn: "3 okçu ve 2 mızrakçı üret", "batı kanadına 4 okçu gönder", "okçu poligonu ve tarla yap", "mancınık bas"
            bool asksBuildVerb = ContainsAny(q, "yap", "insa", "kur", "dik", "ekle");
            bool asksMoveVerb = ContainsAny(q, "gonder", "yolla", "cek", "tasi", "yerlestir", "tut");
            bool asksTrainVerb = ContainsAny(q, "bas", "uret", "egit", "cikar", "yetistir", "hazirla");
            bool asksAnalysisOrAdvice = isQuestion || ContainsAny(q, "analiz", "durum", "oneri", "tavsiye", "odak", "rakip", "rapor");

            var mentionedBuildings = DetectMentionedBuildings(q);
            var mentionedUnits = DetectMentionedUnits(q);

            if (!asksAnalysisOrAdvice && (mentionedBuildings.Count > 0 || mentionedUnits.Count > 0 || asksBuildVerb || asksMoveVerb || asksTrainVerb))
            {
                return BuildCustomCommandPlan(cmdPrefix, q, snapshot, eco, enemy, requestedZone,
                    mentionedBuildings, mentionedUnits, asksBuildVerb, asksMoveVerb, asksTrainVerb,
                    ref remWood, ref remFood, ref remGold, ref remPop);
            }

            // 9. EKONOMİ / KAYNAK / İŞÇİ ODAK PLANI
            if (ContainsAny(q, "ekonomi", "kaynak", "tarla", "ciftlik", "koylu", "isci") && !ContainsAny(q, "rakip", "sis"))
            {
                return BuildEconomyFocusPlan(cmdPrefix, snapshot, eco, ref remWood, ref remFood, ref remGold, ref remPop);
            }

            // 10. GENEL KAPSAMLI ANALİZ + ÖNERİ (Varsayılan akıllı komutan cevabı)
            return BuildFullBattlefieldAnalysisPlan(cmdPrefix, snapshot, eco, enemy, requestedZone, ref remWood, ref remFood, ref remGold, ref remPop);
        }

        private static RTSActionPlan BuildCounterAnswerPlan(
            string cmdPrefix, AiSafeSnapshot snapshot,
            string enemyUnit, string counterUnit, string reasonTr, string zone, EconomyAnalysis eco,
            ref int remWood, ref int remFood, ref int remGold, ref int remPop)
        {
            var plan = new RTSActionPlan { confidence = 0.96f };
            string enemyTr = TranslateUnitTr(enemyUnit);
            string counterTr = TranslateUnitTr(counterUnit);
            int age = GetAgeLevel(snapshot);
            int ownedCounter = GetArmyCount(snapshot, counterUnit);

            var sb = new StringBuilder();
            sb.Append($"{cmdPrefix}[KARŞITLIK YANITI] {enemyTr} tehdidine karşı en etkili birlik {counterTr}'dir ({reasonTr}). ");

            // Çağ kısıtı kontrolü: Cavalry / Trebuchet Kale Çağı (Age 3), Archer / Swordsman Feodal (Age 2) gerektirir (ancak Ai/Test veya kale varsa esnet)
            bool needsHigherAge = (counterUnit == "cavalry" || counterUnit == "trebuchet") && age < 3;
            if (needsHigherAge)
            {
                sb.Append($"Not: {counterTr} Kale Çağı (3. Çağ) birliğidir (şu an {age}. Çağdasınız); acil ara çözüm olarak mevcut ordunun konumlanması ve ");
            }

            if (ownedCounter > 0)
            {
                sb.Append($"Elinizde {ownedCounter} {counterTr} hazır — {TranslateZoneTr(zone)} hattına sevk ediyoruz.");
                plan.actions.Add(new RTSAction
                {
                    action_type = "MOVE_GROUP",
                    unit_type = counterUnit,
                    count = Math.Min(10, ownedCounter),
                    target_zone = zone,
                    reason = $"Mevcut {counterTr} birliklerini {enemyTr} tehdidine karşı {TranslateZoneTr(zone)} bölgesine yerleştir."
                });
            }

            bool trainedPrimary = false;
            if (!needsHigherAge)
            {
                trainedPrimary = TryAddAffordableUnit(plan, counterUnit, 3, zone, ref remWood, ref remFood, ref remGold, ref remPop,
                    $"{enemyTr} saldırısını kırmak için bütçeye uygun {counterTr} eğitimi.");
            }
            else
            {
                // Yaş 1/2 iken okçu veya kılıçlı/mızrakçı alternatifi üret
                string alt = (counterUnit == "cavalry") ? "spearman" : "archer";
                if (TryAddAffordableUnit(plan, alt, 3, zone, ref remWood, ref remFood, ref remGold, ref remPop,
                    $"{counterTr} açılana kadar {enemyTr} karşısında ön hat tutacak {TranslateUnitTr(alt)} eğitimi."))
                {
                    sb.Append($"{TranslateUnitTr(alt)} takviyesi + Kale Çağı hedefi önerilir.");
                }
            }

            if (!trainedPrimary && ownedCounter == 0 && !needsHigherAge)
            {
                var c = GetCost(counterUnit);
                string missing = DescribeMissingResources(c.wood, c.food, c.gold, remWood, remFood, remGold);
                sb.Append($"Ancak bütçe/nüfus yetmiyor ({missing}). İşçileri {eco.focusResourceTr} kaynağına odaklıyoruz.");
                if (snapshot.workers != null && snapshot.workers.total > 0)
                {
                    string targetRes = (remGold < c.gold) ? "gold" : (remWood < c.wood) ? "wood" : "food";
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "ASSIGN_GATHER",
                        unit_type = "villager",
                        count = Math.Max(1, snapshot.workers.idle > 0 ? snapshot.workers.idle : 2),
                        target_zone = targetRes,
                        reason = $"{counterTr} basabilmek için eksik {TranslateResourceTr(targetRes)} kaynağını topla."
                    });
                }
            }

            plan.summary = TrimSummary(sb.ToString());
            return plan;
        }

        private static RTSActionPlan BuildRaidReadinessPlan(
            string cmdPrefix, AiSafeSnapshot snapshot, EconomyAnalysis eco, EnemyIntelAnalysis enemy, string zone,
            ref int remWood, ref int remFood, ref int remGold, ref int remPop)
        {
            var plan = new RTSActionPlan { confidence = 0.94f };
            int raidSec = snapshot.fog_intel != null ? snapshot.fog_intel.seconds_until_next_raid : -1;
            int mil = GetMilitaryTotal(snapshot);
            int towers = GetBuildingCount(snapshot, "defensetower");

            string raidStr = (raidSec >= 0) ? $"Akına ~{raidSec} saniye kaldı" : "Çok oyunculu PvP haritası aktif";
            string readiness = (mil >= 8 || (mil >= 5 && towers >= 1))
                ? "İYİ (Savunma çekirdeği mevcut)"
                : (mil >= 3 ? "ORTA (Ek asker veya Kule gerek)" : "KRİTİK (Çok az askerimiz var!)");

            plan.summary = TrimSummary($"{cmdPrefix}[AKIN HAZIRLIK ANALİZİ] {raidStr}. Ordumuz: {mil} muharip, {towers} Savunma Kulesi → Durum: {readiness}. {eco.narrativeTr}");

            if (towers == 0 && remWood >= 125 && remGold >= 50)
            {
                remWood -= 125; remGold -= 50;
                plan.actions.Add(new RTSAction
                {
                    action_type = "BUILD",
                    building_type = "tower",
                    target_zone = zone,
                    reason = $"Yaklaşan akını karşılamak için {TranslateZoneTr(zone)} hattına Savunma Kulesi dik."
                });
            }
            TryAddAffordableCounterUnit(plan, enemy.recommendedCounter, zone, ref remWood, ref remFood, ref remGold, ref remPop,
                "Akın gelmeden savunma birliklerini güçlendir.");

            if (mil > 0)
            {
                plan.actions.Add(new RTSAction
                {
                    action_type = "MOVE_GROUP",
                    unit_type = "soldier",
                    count = mil,
                    target_zone = zone,
                    reason = $"Mevcut {mil} askeri akın güzergahı olan {TranslateZoneTr(zone)} önüne mevzilendir."
                });
            }
            return plan;
        }

        private static RTSActionPlan BuildAgeAndTechPlan(
            string cmdPrefix, string q, AiSafeSnapshot snapshot, EconomyAnalysis eco,
            ref int remWood, ref int remFood, ref int remGold, ref int remPop)
        {
            var plan = new RTSActionPlan { confidence = 0.93f };
            int age = GetAgeLevel(snapshot);
            bool hasBlacksmith = GetBuildingCount(snapshot, "blacksmith") > 0;
            bool wantsCastle = ContainsAny(q, "kale cagi", "3. cag", "ucuncu cag", "balistik");

            var sb = new StringBuilder();
            sb.Append($"{cmdPrefix}[ÇAĞ & TEKNOLOJİ RAPORU] Şu an {age}. Çağdasınız. ");

            if (age == 1 && !wantsCastle)
            {
                // II. Feodal Çağ: 200 Odun, 500 Gıda
                if (remWood >= 200 && remFood >= 500)
                {
                    remWood -= 200; remFood -= 500;
                    sb.Append("II. Feodal Çağ (200 Odun, 500 Gıda) için kaynaklar TAMAM! Hemen çağ atlıyoruz.");
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "RESEARCH",
                        building_type = "Feudal_Age",
                        target_zone = "town_center",
                        reason = "II. Feodal Çağ'a yükselerek Okçu, Kılıçlı ve Savunma Kulesi kilidini aç."
                    });
                }
                else
                {
                    string miss = DescribeMissingResources(200, 500, 0, remWood, remFood, remGold);
                    sb.Append($"II. Feodal Çağ (200 Odun, 500 Gıda) için {miss} eksik. ");
                    string focusNeed = (500 - remFood > (200 - remWood) * 2) ? "food" : "wood";
                    sb.Append($"İşçileri {TranslateResourceTr(focusNeed)} kaynağına yönlendirin.");
                    if (snapshot.workers != null && snapshot.workers.total > 0)
                    {
                        plan.actions.Add(new RTSAction
                        {
                            action_type = "ASSIGN_GATHER",
                            unit_type = "villager",
                            count = Math.Max(1, snapshot.workers.idle > 0 ? snapshot.workers.idle : 2),
                            target_zone = focusNeed,
                            reason = $"Feodal Çağ eksik kaynağını ({miss}) toplamak için köylü odaklaması."
                        });
                    }
                    if (!hasBlacksmith && remWood >= 150)
                    {
                        remWood -= 150;
                        plan.actions.Add(new RTSAction
                        {
                            action_type = "BUILD",
                            building_type = "blacksmith",
                            target_zone = "town_center",
                            reason = "Feodal Çağ sonrası silah/zırh araştırmalarını açmak için Demirci kur."
                        });
                    }
                }
                plan.summary = TrimSummary(sb.ToString());
                return plan;
            }

            if (age == 2 && wantsCastle)
            {
                if (remFood >= 800 && remGold >= 200)
                {
                    remFood -= 800; remGold -= 200;
                    sb.Append("III. Kale Çağı (800 Gıda, 200 Altın) bütçesi hazır! Yükseliyoruz.");
                    plan.actions.Add(new RTSAction { action_type = "RESEARCH", building_type = "Castle_Age", target_zone = "town_center", reason = "Ağır Süvari ve Mancınık birimleri için III. Kale Çağı'na geç." });
                }
                else
                {
                    string miss = DescribeMissingResources(0, 800, 200, remWood, remFood, remGold);
                    sb.Append($"III. Kale Çağı (800 Gıda, 200 Altın) için {miss} eksik.");
                    string focusNeed = (800 - remFood >= (200 - remGold) * 2) ? "food" : "gold";
                    if (snapshot.workers != null && snapshot.workers.total > 0)
                        plan.actions.Add(new RTSAction { action_type = "ASSIGN_GATHER", unit_type = "villager", count = Math.Max(1, snapshot.workers.idle > 0 ? snapshot.workers.idle : 2), target_zone = focusNeed, reason = $"Kale Çağı için {miss} eksiğini kapat." });
                }
                plan.summary = TrimSummary(sb.ToString());
                return plan;
            }

            // Demirci ve mevcut teknolojiler
            if (!hasBlacksmith && remWood >= 150)
            {
                remWood -= 150;
                plan.actions.Add(new RTSAction { action_type = "BUILD", building_type = "blacksmith", target_zone = "town_center", reason = "Silah ve Zırh araştırmaları için Demirci Atölyesi inşası." });
            }
            if (!HasTech(snapshot, "Iron_Weapons") && remFood >= 100 && remGold >= 50)
            {
                remFood -= 100; remGold -= 50;
                plan.actions.Add(new RTSAction { action_type = "RESEARCH", building_type = "Iron_Weapons", target_zone = "town_center", reason = "Piyade ve süvari saldırısını +2 artır." });
            }
            if (!HasTech(snapshot, "Scale_Armor") && remFood >= 100 && remGold >= 80)
            {
                remFood -= 100; remGold -= 80;
                plan.actions.Add(new RTSAction { action_type = "RESEARCH", building_type = "Scale_Armor", target_zone = "town_center", reason = "Zırh +2 ve Can +20 artır." });
            }
            if (!HasTech(snapshot, "Wheelbarrow") && remWood >= 50 && remFood >= 175)
            {
                remWood -= 50; remFood -= 175;
                plan.actions.Add(new RTSAction { action_type = "RESEARCH", building_type = "Wheelbarrow", target_zone = "town_center", reason = "Köylü taşıma kapasitesi ve hızını artır." });
            }

            if (plan.actions.Count == 0 && snapshot.workers != null && snapshot.workers.total > 0)
            {
                sb.Append("Araştırma bütçesi şu an yetersiz; köylüleri gerekli kaynağa yönlendiriyoruz. ");
                plan.actions.Add(new RTSAction { action_type = "ASSIGN_GATHER", unit_type = "villager", count = Math.Max(1, snapshot.workers.idle > 0 ? snapshot.workers.idle : 2), target_zone = eco.focusResource, reason = $"Teknoloji yatırımı için {eco.focusResourceTr} topla." });
            }
            else
            {
                sb.Append(eco.narrativeTr);
            }
            plan.summary = TrimSummary(sb.ToString());
            return plan;
        }

        private static RTSActionPlan BuildKnowledgeComparisonPlan(
            string cmdPrefix, string q, AiSafeSnapshot snapshot, EconomyAnalysis eco, EnemyIntelAnalysis enemy,
            ref int remWood, ref int remFood, ref int remGold, ref int remPop)
        {
            var plan = new RTSActionPlan { confidence = 0.94f };
            string info;

            if (ContainsAny(q, "maliyet", "kac altin", "kac odun", "kac gida", "ne ister", "fiyat"))
            {
                info = "Birim Maliyetleri: Köylü(50G), Mızrakçı(60G,20A), Okçu(50O,30A), Kılıçlı(60G,30A), Arbaletçi(60O,40A), Süvari(100G,80A), Mancınık(200O,150A). " +
                       $"Mevcut durumunuzda önerilen odak: {eco.focusResourceTr}.";
            }
            else if (ContainsAny(q, "nasil kazan", "zafer", "oyunu bitir"))
            {
                info = "Zafer Şartı: Düşman Şehir Merkezini (Town Center) yıkmak gerekir! Önce 6+ köylüyle ekonomiyi kurun, Feodal/Kale Çağına geçip Mancınık + Mızrakçı/Okçu ordusuyla düşman üssünü kuşatın.";
            }
            else if (ContainsAny(q, "mizrak") && ContainsAny(q, "okcu"))
            {
                info = "Mızrakçı vs Okçu: Mızrakçı süvarileri ezer (+18 bonus) ve gıda/altın ister; Okçu ise mızrakçıları uzaktan eritir ve odun/altın ister. İkisini birlikte (önde Mızrakçı duvarı, arkada Okçu) kullanmak en dengelisidir.";
            }
            else if (ContainsAny(q, "demirci"))
            {
                info = "Demirci (150 Odun): Tüm ordunun saldırısını (Demir Silahlar), zırh/canını (Pul Zırh) ve okçu menzilini (Çelik Ok Uçları) kalıcı olarak yükseltir.";
            }
            else
            {
                info = $"Karşıtlık Üçgeni: Mızrakçı > Süvari > Okçu > Mızrakçı; Mancınık > Kule/Bina. Şu anki istihbarata göre önerilen birlik: {TranslateUnitTr(enemy.recommendedCounter)}.";
            }

            plan.summary = TrimSummary($"{cmdPrefix}{info} | {eco.narrativeTr}");
            TryAddAffordableCounterUnit(plan, enemy.recommendedCounter, enemy.primaryThreatZone, ref remWood, ref remFood, ref remGold, ref remPop,
                $"Mevcut saha verisine göre bütçe dahilinde {TranslateUnitTr(enemy.recommendedCounter)} takviyesi.");
            if (plan.actions.Count == 0 && snapshot.workers != null && snapshot.workers.total > 0)
            {
                plan.actions.Add(new RTSAction { action_type = "ASSIGN_GATHER", unit_type = "villager", count = 1, target_zone = eco.focusResource, reason = $"Ekonomi odağı: {eco.focusResourceTr}" });
            }
            return plan;
        }

        private static RTSActionPlan BuildBottleneckDiagnosisPlan(
            string cmdPrefix, string q, AiSafeSnapshot snapshot, EconomyAnalysis eco, EnemyIntelAnalysis enemy,
            ref int remWood, ref int remFood, ref int remGold, ref int remPop)
        {
            var plan = new RTSActionPlan { confidence = 0.95f };
            var w = snapshot.workers ?? new AiSafeSnapshot.WorkerAllocationState();
            var pop = snapshot.population ?? new AiSafeSnapshot.PopulationState();
            var fog = snapshot.fog_intel;

            string targetRes = eco.focusResource;
            if (ContainsAny(q, "altin")) targetRes = "gold";
            else if (ContainsAny(q, "odun")) targetRes = "wood";
            else if (ContainsAny(q, "gida", "yiyecek")) targetRes = "food";

            string targetTr = TranslateResourceTr(targetRes);
            var sb = new StringBuilder();

            if (ContainsAny(q, "uretemiyorum", "basamiyorum", "neden") && pop.current >= pop.max)
            {
                sb.Append($"{cmdPrefix}[NÜFUS TIKANIKLIĞI] Nüfusunuz tamamen dolu ({pop.current}/{pop.max})! Yeni birlik basabilmek için Ev (30 Odun) şart. ");
                if (remWood >= 30)
                {
                    remWood -= 30;
                    plan.actions.Add(new RTSAction { action_type = "BUILD", building_type = "house", target_zone = "town_center", reason = $"Nüfus kilidini ({pop.current}/{pop.max}) açmak için Ev inşa et." });
                }
                else
                {
                    targetRes = "wood";
                    targetTr = "ODUN";
                    sb.Append($"Ancak Odun ({remWood}) da yetersiz; 2 işçiyi Odun'a yönlendiriyoruz.");
                }
            }
            else
            {
                int currentOnRes = (targetRes == "wood") ? w.wood : (targetRes == "gold") ? w.gold : w.food;
                int nodesOpen = (fog == null) ? 1 : (targetRes == "wood" ? fog.discovered_wood_nodes : targetRes == "gold" ? fog.discovered_gold_nodes : fog.discovered_food_nodes);
                sb.Append($"{cmdPrefix}[KAYNAK & İŞÇİ TEŞHİSİ] {targetTr} stoğunuz: {(targetRes == "wood" ? remWood : targetRes == "gold" ? remGold : remFood)}. ");
                sb.Append($"Şu an {targetTr}'da {currentOnRes} köylü çalışıyor (Boşta: {w.idle}, Toplam: {w.total}). ");

                if (nodesOpen == 0 && targetRes == "gold")
                {
                    sb.Append("Sis dışında hiç Altın Madeni keşfedilmemiş! Önce gözcüyle altın madeni açılmalı. ");
                    if (GetMilitaryTotal(snapshot) > 0)
                    {
                        plan.actions.Add(new RTSAction { action_type = "SCOUT", unit_type = "soldier", count = 1, target_zone = "gold", reason = "Sis altındaki altın madenlerini tespit etmek için keşif çıkar." });
                    }
                }
                else
                {
                    sb.Append($"ÖNERİ: {(w.idle > 0 ? w.idle + " boşta işçi dahil " : "")}köylüleri {targetTr} toplamaya yönlendirin.");
                }
            }

            if (w.total > 0 && !plan.actions.Exists(a => a.action_type == "SCOUT" && a.target_zone == "gold"))
            {
                int moveCnt = Math.Max(1, w.idle > 0 ? w.idle : Math.Min(2, w.total));
                plan.actions.Add(new RTSAction
                {
                    action_type = "ASSIGN_GATHER",
                    unit_type = "villager",
                    count = moveCnt,
                    target_zone = targetRes,
                    reason = $"{targetTr} açığını kapatmak için {moveCnt} köylüyü bu kaynağa odakla."
                });
            }

            if (remFood >= 50 && remPop >= 1 && w.total < 7)
            {
                remFood -= 50; remPop -= 1;
                plan.actions.Add(new RTSAction { action_type = "TRAIN_UNIT", unit_type = "villager", count = 1, target_zone = "town_center", reason = "Ekonomik toplama gücünü büyütmek için 1 köylü daha eğit." });
            }

            plan.summary = TrimSummary(sb.ToString());
            return plan;
        }

        private static RTSActionPlan BuildCustomCommandPlan(
            string cmdPrefix, string q, AiSafeSnapshot snapshot, EconomyAnalysis eco, EnemyIntelAnalysis enemy, string zone,
            List<(string bType, int count)> buildings, List<(string uType, int count)> units,
            bool asksBuild, bool asksMove, bool asksTrain,
            ref int remWood, ref int remFood, ref int remGold, ref int remPop)
        {
            var plan = new RTSActionPlan { confidence = 0.93f };
            var notes = new List<string>();

            foreach (var (bType, cnt) in buildings)
            {
                var bc = GetBuildingCost(bType);
                int built = 0;
                for (int i = 0; i < cnt; i++)
                {
                    if (remWood >= bc.wood && remFood >= bc.food && remGold >= bc.gold)
                    {
                        remWood -= bc.wood; remFood -= bc.food; remGold -= bc.gold;
                        built++;
                        string bZone = (bType == "tower" || bType == "barracks") ? zone : "town_center";
                        plan.actions.Add(new RTSAction
                        {
                            action_type = "BUILD",
                            building_type = bType,
                            target_zone = bZone,
                            reason = $"Talebiniz üzerine {TranslateBuildingTr(bType)} inşası."
                        });
                    }
                    else
                    {
                        string miss = DescribeMissingResources(bc.wood, bc.food, bc.gold, remWood, remFood, remGold);
                        notes.Add($"{TranslateBuildingTr(bType)} için {miss} eksik");
                        break;
                    }
                }
                if (built > 0) notes.Add($"{built} {TranslateBuildingTr(bType)} planlandı");
            }

            foreach (var (uType, cnt) in units)
            {
                int owned = GetArmyCount(snapshot, uType);
                // Kullanıcı açıkça "gönder/çek" dediyse ve "bas/üret" demediyse önce sahadaki mevcut birimleri kaydır
                if (asksMove && !asksTrain && owned > 0)
                {
                    int toMove = Math.Min(owned, cnt);
                    plan.actions.Add(new RTSAction
                    {
                        action_type = "MOVE_GROUP",
                        unit_type = uType,
                        count = toMove,
                        target_zone = zone,
                        reason = $"Sahadaki {toMove} {TranslateUnitTr(uType)} birliğini {TranslateZoneTr(zone)} hedefine gönder."
                    });
                    notes.Add($"{toMove} mevcut {TranslateUnitTr(uType)} -> {TranslateZoneTr(zone)}");
                    int remainingToTrain = cnt - toMove;
                    if (remainingToTrain > 0)
                    {
                        TryAddAffordableUnit(plan, uType, remainingToTrain, zone, ref remWood, ref remFood, ref remGold, ref remPop,
                            $"Eksik kalan {TranslateUnitTr(uType)} sayısını eğitimle tamamla.");
                    }
                }
                else
                {
                    bool added = TryAddAffordableUnit(plan, uType, cnt, zone, ref remWood, ref remFood, ref remGold, ref remPop,
                        $"Talebiniz üzerine {TranslateZoneTr(zone)} için {TranslateUnitTr(uType)} eğitimi.");
                    if (added)
                    {
                        notes.Add($"{TranslateUnitTr(uType)} eğitimi planlandı");
                    }
                    else if (owned > 0 && asksMove)
                    {
                        plan.actions.Add(new RTSAction
                        {
                            action_type = "MOVE_GROUP",
                            unit_type = uType,
                            count = Math.Min(owned, cnt),
                            target_zone = zone,
                            reason = $"Yeni üretim bütçesi yok; eldeki {owned} {TranslateUnitTr(uType)} birliğini gönder."
                        });
                    }
                    else
                    {
                        var uc = GetCost(uType);
                        string reasonStr = (remPop <= 0)
                            ? "Nüfus dolu! Önce Ev (30 Odun) gerekli"
                            : DescribeMissingResources(uc.wood, uc.food, uc.gold, remWood, remFood, remGold) + " eksik";
                        notes.Add($"{TranslateUnitTr(uType)} basılamadı ({reasonStr})");
                    }
                }
            }

            if (plan.actions.Count == 0 && snapshot.workers != null && snapshot.workers.total > 0)
            {
                plan.actions.Add(new RTSAction
                {
                    action_type = "ASSIGN_GATHER",
                    unit_type = "villager",
                    count = Math.Max(1, snapshot.workers.idle > 0 ? snapshot.workers.idle : 2),
                    target_zone = eco.focusResource,
                    reason = $"İstenen emir için eksik kaynağı ({eco.focusResourceTr}) hızla topla."
                });
            }

            string noteStr = notes.Count > 0 ? string.Join(" • ", notes) : eco.narrativeTr;
            plan.summary = TrimSummary($"{cmdPrefix}{noteStr}. {eco.narrativeTr}");
            return plan;
        }

        private static RTSActionPlan BuildEconomyFocusPlan(
            string cmdPrefix, AiSafeSnapshot snapshot, EconomyAnalysis eco,
            ref int remWood, ref int remFood, ref int remGold, ref int remPop)
        {
            var plan = new RTSActionPlan { confidence = 0.93f };
            var w = snapshot.workers ?? new AiSafeSnapshot.WorkerAllocationState();
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
                remWood -= 50; remFood -= 175;
                plan.actions.Add(new RTSAction
                {
                    action_type = "RESEARCH",
                    building_type = "Wheelbarrow",
                    target_zone = "town_center",
                    reason = "Köylü taşıma kapasitesini ve hızını %25 artıran El Arabası teknolojisi."
                });
            }
            return plan;
        }

        private static RTSActionPlan BuildFullBattlefieldAnalysisPlan(
            string cmdPrefix, AiSafeSnapshot snapshot, EconomyAnalysis eco, EnemyIntelAnalysis enemy, string zone,
            ref int remWood, ref int remFood, ref int remGold, ref int remPop)
        {
            var plan = new RTSActionPlan { confidence = 0.95f };
            var w = snapshot.workers ?? new AiSafeSnapshot.WorkerAllocationState();
            var pop = snapshot.population ?? new AiSafeSnapshot.PopulationState();

            plan.summary = TrimSummary($"{cmdPrefix}{eco.narrativeTr} | SİS İSTİHBARATI: {enemy.narrativeTr}");

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

            if (eco.shouldTrainVillager && remFood >= 50 && remPop >= 1)
            {
                remFood -= 50; remPop -= 1;
                plan.actions.Add(new RTSAction
                {
                    action_type = "TRAIN_UNIT",
                    unit_type = "villager",
                    count = 1,
                    target_zone = "town_center",
                    reason = $"Toplam işçi sayısı az ({w.total}); ekonomiyi hızlandırmak için 1 yeni köylü eğit."
                });
            }

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

            TryAddAffordableCounterUnit(plan, enemy.recommendedCounter, zone, ref remWood, ref remFood, ref remGold, ref remPop,
                enemy.hasLiveSighting || enemy.hasFogMemory || enemy.discoveredEnemyBuildings > 0
                    ? $"Sis istihbaratına ({TranslateEnemyClassTr(enemy.dominantEnemyClass)}) karşı en etkili karşıt birim."
                    : "Erken akınlara karşı savunma çekirdeği oluştur.");

            if (plan.actions.Count == 0 && GetMilitaryTotal(snapshot) > 0)
            {
                plan.actions.Add(new RTSAction { action_type = "SCOUT", unit_type = "soldier", count = 1, target_zone = "unexplored", reason = "Mevcut kaynaklar sınırlı olduğundan sisli bölgelerde ücretsiz keşif icra et." });
            }
            else if (plan.actions.Count == 0 && w.total > 0)
            {
                plan.actions.Add(new RTSAction { action_type = "ASSIGN_GATHER", unit_type = "villager", count = 1, target_zone = eco.focusResource, reason = $"Bütçeyi toparlamak için köylüleri {eco.focusResourceTr} kaynağına odakla." });
            }

            return plan;
        }

        private static bool TryDetectCounteredEnemy(string q, out string enemyUnit, out string counterUnit, out string reasonTr)
        {
            if (ContainsAny(q, "suvari", "atli", "sovalye", "cavalry"))
            {
                enemyUnit = "cavalry"; counterUnit = "spearman";
                reasonTr = "Mızrakçılar atlılara karşı +18 devasa bonus hasar verir ve maliyeti çok ekonomiktir";
                return true;
            }
            if (ContainsAny(q, "okcu", "arbalet", "archer", "crossbow"))
            {
                enemyUnit = "archer"; counterUnit = "cavalry";
                reasonTr = "Süvariler yüksek hızlarıyla menzilli hattı kapatıp okçuları hızla ezer";
                return true;
            }
            if (ContainsAny(q, "mizrak", "kilic", "piyade", "spearman", "swordsman"))
            {
                enemyUnit = "spearman"; counterUnit = "archer";
                reasonTr = "Okçular yavaş hareket eden yakın dövüş piyadelerini uzaktan hasar almadan eritir";
                return true;
            }
            if (ContainsAny(q, "mancinik", "katapult", "trebuchet"))
            {
                enemyUnit = "trebuchet"; counterUnit = "cavalry";
                reasonTr = "Kuşatma silahları yakın mesafede savunmasızdır; hızlı süvariler onları anında yok eder";
                return true;
            }
            if (ContainsAny(q, "kule", "kale", "sur", "tower", "castle"))
            {
                enemyUnit = "tower"; counterUnit = "trebuchet";
                reasonTr = "Mancınık kule ve kale menzilinin dışından (+200 bina bonusuyla) yapıları yerle bir eder";
                return true;
            }
            enemyUnit = ""; counterUnit = "spearman"; reasonTr = "";
            return false;
        }

        private static string DetectTargetZone(string q, string fallbackZone)
        {
            if (ContainsAny(q, "bati", "sol kanat", "west")) return "west_flank";
            if (ContainsAny(q, "dogu", "sag kanat", "east")) return "east_flank";
            if (ContainsAny(q, "dusman us", "dusman kamp", "enemy", "karargah")) return "enemy_base";
            if (ContainsAny(q, "bizim us", "ana us", "sehir merkez", "town center", "ev yan")) return "town_center";
            if (ContainsAny(q, "kopru", "gecit", "kuzey", "north", "orta")) return "north_gate";
            return string.IsNullOrEmpty(fallbackZone) ? "north_gate" : fallbackZone;
        }

        private static List<(string uType, int count)> DetectMentionedUnits(string q)
        {
            string cleaned = (q ?? "").Replace("okcu poligon", "").Replace("mancinik atolye", "").Replace("sehir merkez", "");
            var found = new List<(string uType, int count)>();
            var patterns = new (string uType, string[] keys)[]
            {
                ("villager", new[] { "koylu", "isci", "villager" }),
                ("spearman", new[] { "mizrak", "spearman" }),
                ("crossbowman", new[] { "arbalet", "crossbow" }),
                ("archer", new[] { "okcu", "archer" }),
                ("swordsman", new[] { "kilicli", "kilic", "swordsman" }),
                ("cavalry", new[] { "suvari", "atli", "sovalye", "cavalry" }),
                ("trebuchet", new[] { "mancinik", "katapult", "trebuchet" }),
            };

            foreach (var p in patterns)
            {
                if (ContainsAny(cleaned, p.keys))
                {
                    int cnt = ExtractCountNearKeywords(cleaned, p.keys, (p.uType == "trebuchet" || p.uType == "villager") ? 2 : 3);
                    found.Add((p.uType, cnt));
                }
            }
            return found;
        }

        private static List<(string bType, int count)> DetectMentionedBuildings(string q)
        {
            var found = new List<(string bType, int count)>();
            var patterns = new (string bType, string[] keys)[]
            {
                ("house", new[] { "ev ", "evler", "barinak", "house" }),
                ("farm", new[] { "tarla", "ciftlik", "farm" }),
                ("barracks", new[] { "kisla", "barracks" }),
                ("archeryrange", new[] { "okcu poligon", "talimgah", "archery" }),
                ("blacksmith", new[] { "demirci", "blacksmith" }),
                ("tower", new[] { "kule", "tower" }),
                ("castle", new[] { "kale", "castle" }),
                ("siegeworkshop", new[] { "kusatma atolye", "mancinik atolye", "siege" }),
                ("market", new[] { "pazar", "market" }),
            };

            string padded = " " + q + " ";
            foreach (var p in patterns)
            {
                if (ContainsAny(padded, p.keys))
                {
                    int cnt = ExtractCountNearKeywords(q, p.keys, 1);
                    found.Add((p.bType, Math.Min(3, cnt)));
                }
            }
            return found;
        }

        private static int ExtractCountNearKeywords(string q, string[] keywords, int defaultCount)
        {
            for (int i = 0; i < keywords.Length; i++)
            {
                string kw = Regex.Escape(keywords[i].Trim());
                var m = Regex.Match(q, @"(\d{1,2})\s*(?:adet|tane|x)?\s*[a-z]*" + kw);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int n))
                    return Math.Max(1, Math.Min(20, n));
            }
            return defaultCount;
        }

        private static bool ContainsAny(string q, params string[] terms)
        {
            for (int i = 0; i < terms.Length; i++)
            {
                if (q.Contains(terms[i])) return true;
            }
            return false;
        }

        private static string DescribeMissingResources(int reqW, int reqF, int reqG, int hasW, int hasF, int hasG)
        {
            var parts = new List<string>();
            if (reqW > hasW) parts.Add($"{reqW - hasW} Odun");
            if (reqF > hasF) parts.Add($"{reqF - hasF} Gıda");
            if (reqG > hasG) parts.Add($"{reqG - hasG} Altın");
            return parts.Count > 0 ? string.Join(", ", parts) : "Nüfus";
        }

        private static void TryAddAffordableCounterUnit(
            RTSActionPlan plan, string unitType, string zone,
            ref int remWood, ref int remFood, ref int remGold, ref int remPop, string reason)
        {
            if (TryAddAffordableUnit(plan, unitType, 3, zone, ref remWood, ref remFood, ref remGold, ref remPop, reason))
                return;
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

        private static (int wood, int food, int gold) GetBuildingCost(string bType)
        {
            switch ((bType ?? "").ToLowerInvariant())
            {
                case "house": return (30, 0, 0);
                case "farm": return (60, 0, 0);
                case "barracks": return (150, 0, 0);
                case "blacksmith": return (150, 0, 0);
                case "tower": return (125, 0, 50);
                case "archeryrange": return (175, 0, 0);
                case "siegeworkshop": return (200, 0, 100);
                case "castle": return (350, 0, 250);
                case "market": return (150, 0, 0);
                default: return (150, 0, 0);
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
