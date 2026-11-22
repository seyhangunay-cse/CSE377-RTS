"""
CSE-377 AI Gateway HTTP Server
Unity istemcisi ile Agent Runner arasındaki HTTP köprüsü.
UnityWebRequest ile gelen POST /api/plan isteklerini karşılar,
Agent Runner'ı çalıştırır ve doğrulanmış ActionPlan JSON'unu döner.
"""
import os
import sys
import json
import re
from http.server import HTTPServer, BaseHTTPRequestHandler
import socketserver

# Encoding
if hasattr(sys.stdout, "reconfigure"):
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass

# Mevcut dizin ve MCP sunucu dizinini ekle
current_dir = os.path.dirname(os.path.abspath(__file__))
mcp_dir = os.path.abspath(os.path.join(current_dir, "../mcp-server"))
if mcp_dir not in sys.path:
    sys.path.insert(0, mcp_dir)

from agent_runner import run_agent  # type: ignore
import server  # type: ignore

PORT = 5000


class GatewayRequestHandler(BaseHTTPRequestHandler):
    def _set_headers(self, status_code=200):
        self.send_response(status_code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "POST, GET, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()

    def do_OPTIONS(self):
        self._set_headers(200)

    def do_GET(self):
        if self.path == "/health":
            self._set_headers(200)
            self.wfile.write(json.dumps({"status": "healthy", "service": "RTS-AI-Gateway"}).encode("utf-8"))
        else:
            self._set_headers(404)
            self.wfile.write(json.dumps({"error": "Bilinmeyen GET rotası"}).encode("utf-8"))

    def do_POST(self):
        if self.path == "/api/plan":
            content_length = int(self.headers.get("Content-Length", 0))
            raw_bytes = self.rfile.read(content_length)
            try:
                post_data = raw_bytes.decode("utf-8")
            except UnicodeDecodeError:
                try:
                    post_data = raw_bytes.decode("cp1254")
                except UnicodeDecodeError:
                    post_data = raw_bytes.decode("utf-8", errors="replace")

            query = "Durumu kontrol et ve savunma planı yap."
            try:
                body = json.loads(post_data) if post_data else {}
                if isinstance(body, dict):
                    query = body.get("query") or body.get("user_prompt") or body.get("prompt") or query
                snapshot = body.get("snapshot") if isinstance(body, dict) else None

                # Unity'den gelen canlı snapshot varsa ilgili takımın (BLUE / RED) MCP belleğini güncelle
                team_id = "BLUE"
                if isinstance(body, dict) and body.get("team_id"):
                    team_id = str(body.get("team_id")).upper()
                elif snapshot and isinstance(snapshot, dict) and snapshot.get("team_id"):
                    team_id = str(snapshot.get("team_id")).upper()

                if snapshot and isinstance(snapshot, dict):
                    server.set_active_team_snapshot(team_id, snapshot)
                    print(f"[GATEWAY] [{team_id} KOMUTANI] Unity snapshot verisi ile MCP safe store güncellendi.")

                print(f"[GATEWAY] [{team_id} KOMUTANI] Yeni plan talebi alındı: '{query}'")
                plan = run_agent(query)

                self._set_headers(200)
                self.wfile.write(json.dumps(plan, ensure_ascii=False).encode("utf-8"))

            except Exception as e:
                print(f"[GATEWAY UYARI - AKILLI YEDEK PLAN DEVREYE GİRDİ]: {e}")
                fallback_plan = generate_emergency_fallback_plan(query, server.CURRENT_SAFE_STATE)
                self._set_headers(200)
                self.wfile.write(json.dumps(fallback_plan, ensure_ascii=False).encode("utf-8"))
        else:
            self._set_headers(404)
            self.wfile.write(json.dumps({"error": "Bilinmeyen POST rotası"}).encode("utf-8"))


def normalize_tr(text: str) -> str:
    if not text:
        return ""
    tr_map = str.maketrans("çğıöşüÇĞİIÖŞÜ", "cgiosucgiiosu")
    return text.translate(tr_map).lower().strip()


def generate_emergency_fallback_plan(query: str, current_state: dict) -> dict:
    return build_dynamic_intel_plan(query, current_state or {})


def build_dynamic_intel_plan(query: str, state: dict) -> dict:
    """
    Canlı fog_intel, resources, workers ve population verilerine dayalı
    soru-cevap, karşıtlık analizi, çağ/bütçe hesaplayıcı ve işçi odak motoru.
    """
    q = normalize_tr(query)

    res = state.get("resources") or {}
    wood = int(res.get("wood", 450))
    food = int(res.get("food", 200))
    gold = int(res.get("gold", 150))

    w = state.get("workers") or {}
    w_total = int(w.get("total", 3))
    w_food = int(w.get("food", 0))
    w_wood = int(w.get("wood", 0))
    w_gold = int(w.get("gold", 0))
    w_idle = int(w.get("idle", 0))

    pop = state.get("population") or {}
    pop_cur = int(pop.get("current", 4))
    pop_max = int(pop.get("max", 10))
    rem_pop = max(0, pop_max - pop_cur)

    fog = state.get("fog_intel") or {}
    explored_pct = int(fog.get("explored_percent", 0))
    visible_enemies = state.get("visible_enemies") or []
    last_known = fog.get("last_known_enemies") or []
    enemy_blds = fog.get("discovered_enemy_buildings") or []
    disc_gold = int(fog.get("discovered_gold_nodes", 1))
    raid_sec = fog.get("seconds_until_next_raid", -1)

    army_list = state.get("army") or []
    techs = {str(t).lower() for t in (state.get("unlocked_tech") or [])}
    age = 3 if ("age_3" in techs or "castle_age" in techs) else (2 if ("age_2" in techs or "feudal_age" in techs) else 1)

    def get_army_cnt(ut: str) -> int:
        return sum(int(g.get("count", 0)) for g in army_list if isinstance(g, dict) and str(g.get("type", "")).lower() == ut)

    mil_total = sum(int(g.get("count", 0)) for g in army_list if isinstance(g, dict) and str(g.get("type", "")).lower() != "villager")

    unit_costs = {
        "villager": (0, 50, 0), "spearman": (0, 60, 20), "archer": (50, 0, 30),
        "cavalry": (0, 100, 80), "swordsman": (0, 60, 30), "crossbowman": (60, 0, 40),
        "trebuchet": (200, 0, 150),
    }
    bld_costs = {
        "house": (30, 0, 0), "farm": (60, 0, 0), "barracks": (150, 0, 0),
        "blacksmith": (150, 0, 0), "tower": (125, 0, 50), "archeryrange": (175, 0, 0),
        "siegeworkshop": (200, 0, 100), "castle": (350, 0, 250), "market": (150, 0, 0),
    }

    def missing_str(rw: int, rf: int, rg: int, hw: int, hf: int, hg: int) -> str:
        parts = []
        if rw > hw:
            parts.append(f"{rw - hw} Odun")
        if rf > hf:
            parts.append(f"{rf - hf} Gıda")
        if rg > hg:
            parts.append(f"{rg - hg} Altın")
        return ", ".join(parts) if parts else "Nüfus"

    zone = "north_gate"
    if any(k in q for k in ["bati", "sol kanat", "west"]):
        zone = "west_flank"
    elif any(k in q for k in ["dogu", "sag kanat", "east"]):
        zone = "east_flank"
    elif any(k in q for k in ["dusman us", "karargah", "enemy"]):
        zone = "enemy_base"
    elif any(k in q for k in ["ana us", "bizim us", "sehir merkez"]):
        zone = "town_center"

    scores = {
        "food": (55 if food < 120 else (25 if food < 220 else 0)) + (45 if w_food == 0 and w_total > 0 else 0),
        "wood": (50 if wood < 120 else (22 if wood < 200 else 0)) + (42 if w_wood == 0 and w_total > 0 else 0) + (35 if rem_pop <= 2 and wood < 60 else 0),
        "gold": (48 if gold < 90 else (20 if gold < 180 else 0)) + (36 if w_gold == 0 and w_total >= 2 else 0),
    }
    focus_key = max(scores, key=lambda k: scores[k])
    focus_tr = {"food": "GIDA", "wood": "ODUN", "gold": "ALTIN"}[focus_key]

    cav_s, rng_s, inf_s = 0, 0, 0
    threat_zone = zone
    for grp in list(visible_enemies) + list(last_known):
        if not isinstance(grp, dict):
            continue
        ut = str(grp.get("type", "")).lower()
        cnt = int(grp.get("count", 0))
        if cnt <= 0:
            continue
        threat_zone = grp.get("zone") or threat_zone
        if ut in ("cavalry", "scout"):
            cav_s += cnt
        elif ut in ("archer", "crossbowman"):
            rng_s += cnt
        else:
            inf_s += cnt

    counter_unit = "spearman"
    if cav_s >= rng_s and cav_s >= inf_s and cav_s > 0:
        counter_unit = "spearman"
    elif rng_s >= cav_s and rng_s >= inf_s and rng_s > 0:
        counter_unit = "cavalry"
    elif inf_s > 0:
        counter_unit = "archer"
    else:
        for eb in enemy_blds:
            bt = str((eb or {}).get("type", "")).lower()
            if "archery" in bt:
                counter_unit = "cavalry"
                break
            if "barracks" in bt or "blacksmith" in bt:
                counter_unit = "archer"
                break

    rem_w, rem_f, rem_g, rp = wood, food, gold, rem_pop
    actions = []

    def try_train(ut: str, desired: int, tz: str, reason: str) -> bool:
        nonlocal rem_w, rem_f, rem_g, rp
        if rp <= 0:
            return False
        cw, cf, cg = unit_costs.get(ut, (0, 60, 20))
        cnt = min(desired, rp, rem_w // cw if cw else desired, rem_f // cf if cf else desired, rem_g // cg if cg else desired)
        if cnt <= 0:
            return False
        rem_w -= cw * cnt
        rem_f -= cf * cnt
        rem_g -= cg * cnt
        rp -= cnt
        actions.append({"action_type": "TRAIN_UNIT", "unit_type": ut, "count": cnt, "target_zone": tz, "reason": reason})
        return True

    asks_counter = any(k in q for k in ["karsi", "durdur", "yener", "iyi gider", "counter", "savun"])
    if asks_counter and any(k in q for k in ["suvari", "atli", "okcu", "arbalet", "mizrak", "kilic", "mancinik", "kule", "kale"]):
        if any(k in q for k in ["suvari", "atli"]):
            enemy_n, best_u, why = "Süvari", "spearman", "Mızrakçılar atlılara +18 bonus hasar verir"
        elif any(k in q for k in ["okcu", "arbalet"]):
            enemy_n, best_u, why = "Okçu", "cavalry", "Süvariler okçuları hızla yakalayıp ezer"
        elif "mancinik" in q:
            enemy_n, best_u, why = "Mancınık", "cavalry", "Hızlı süvariler savunmasız mancınıkları yok eder"
        elif any(k in q for k in ["kule", "kale"]):
            enemy_n, best_u, why = "Kule/Kale", "trebuchet", "Mancınık menzil dışından binalara +200 bonus vurur"
        else:
            enemy_n, best_u, why = "Piyade", "archer", "Okçular piyadeleri uzaktan eritir"

        owned = get_army_cnt(best_u)
        if owned > 0:
            actions.append({"action_type": "MOVE_GROUP", "unit_type": best_u, "count": min(10, owned), "target_zone": zone, "reason": f"Eldeki {owned} {best_u} birliğini {zone} hattına çek."})
        train_target = best_u if not (best_u in ("cavalry", "trebuchet") and age < 3) else ("spearman" if best_u == "cavalry" else "archer")
        if not try_train(train_target, 3, zone, f"{enemy_n} tehdidine karşı {train_target} eğitimi.") and w_total > 0:
            actions.append({"action_type": "ASSIGN_GATHER", "unit_type": "villager", "count": max(1, w_idle or 2), "target_zone": focus_key, "reason": f"Karşıt birlik bütçesi için {focus_tr} topla."})
        return {"summary": f"[KARŞITLIK YANITI] {enemy_n} karşısında en etkili birim {best_u} ({why}). Odağınız: {focus_tr}.", "confidence": 0.95, "actions": actions[:8]}

    if any(k in q for k in ["cag", "feodal", "kale cagi", "demirci", "teknoloji", "el arabasi", "demir silah", "pul zirh"]):
        if age == 1:
            if rem_w >= 200 and rem_f >= 500:
                actions.append({"action_type": "RESEARCH", "building_type": "Feudal_Age", "target_zone": "town_center", "reason": "II. Feodal Çağ (200 Odun, 500 Gıda) başlatılıyor."})
                return {"summary": "II. Feodal Çağ için kaynaklar yeterli! Araştırma başlatılıyor.", "confidence": 0.94, "actions": actions}
            miss = missing_str(200, 500, 0, rem_w, rem_f, rem_g)
            need_k = "food" if (500 - rem_f) > (200 - rem_w) * 2 else "wood"
            if w_total > 0:
                actions.append({"action_type": "ASSIGN_GATHER", "unit_type": "villager", "count": max(1, w_idle or 2), "target_zone": need_k, "reason": f"Feodal Çağ için {miss} eksik; işçileri odakla."})
            return {"summary": f"Şu an 1. Çağdasınız. II. Feodal Çağ (200 Odun, 500 Gıda) için {miss} eksik; işçiler yönlendiriliyor.", "confidence": 0.94, "actions": actions}

    if any(k in q for k in ["akin", "baskin", "hazir mi", "saldiri ne zaman"]):
        r_str = f"Akına ~{raid_sec}sn kaldı" if raid_sec >= 0 else "Çok oyunculu PvP"
        try_train(counter_unit, 3, zone, "Akın gelmeden savunma güçlendirmesi.")
        if mil_total > 0:
            actions.append({"action_type": "MOVE_GROUP", "unit_type": "soldier", "count": mil_total, "target_zone": zone, "reason": f"Mevcut {mil_total} askeri {zone} savunmasına çek."})
        return {"summary": f"[AKIN ANALİZİ] {r_str}. Orduda {mil_total} asker var. Odak: {focus_tr}.", "confidence": 0.93, "actions": actions[:8]}

    if any(k in q for k in ["kusat", "saldir", "taarruz", "hucum", "yok et", "oldur"]) and not asks_counter:
        atk_z = "enemy_base" if any(k in q for k in ["karargah", "us", "kale", "kamp"]) else zone
        if mil_total == 0:
            try_train(counter_unit, 3, atk_z, "Taarruz öncesi ilk muharip birlik eğitimi.")
            return {"summary": f"Sahada henüz asker yok! Önce {counter_unit} eğitip {focus_tr} toplamalıyız.", "confidence": 0.93, "actions": actions}
        actions.append({"action_type": "ATTACK", "unit_type": "soldier", "count": min(20, max(mil_total, 8)), "target_zone": atk_z, "reason": f"{atk_z} hedefine taarruz başlat."})
        try_train(counter_unit, 2, atk_z, "Taarruz hattına taze destek.")
        return {"summary": f"Taarruz Emri ({atk_z} • {mil_total} asker): Sis istihbaratına göre saldırı başlatılıyor.", "confidence": 0.94, "actions": actions}

    if any(k in q for k in ["kesif", "kesfet", "scout", "harita", "dolas"]) and "analiz" not in q:
        focus_s = "gold" if "altin" in q or "maden" in q else "unexplored"
        actions.append({"action_type": "SCOUT", "unit_type": "soldier", "count": 1, "target_zone": focus_s, "reason": f"{focus_s} odaklı sis taraması."})
        return {"summary": f"Keşif Emri: Haritanın %{explored_pct}'i açık; gözcü {focus_s} yönüne çıkarılıyor.", "confidence": 0.95, "actions": actions}

    if any(k in q for k in ["bas", "uret", "egit", "yap", "insa", "kur", "gonder", "yolla", "cek"]) and not any(k in q for k in ["analiz", "durum", "oneri", "nasil", "neden", "?"]):
        unit_map = [("villager", ["koylu", "isci"]), ("spearman", ["mizrak"]), ("crossbowman", ["arbalet"]), ("archer", ["okcu"]), ("swordsman", ["kilic"]), ("cavalry", ["suvari", "atli", "sovalye"]), ("trebuchet", ["mancinik"])]
        bld_map = [("house", ["ev ", "evler"]), ("farm", ["tarla", "ciftlik"]), ("barracks", ["kisla"]), ("archeryrange", ["okcu poligon", "talimgah"]), ("blacksmith", ["demirci"]), ("tower", ["kule"]), ("castle", ["kale"]), ("siegeworkshop", ["kusatma atolye", "mancinik atolye"]), ("market", ["pazar"])]
        padded = " " + q + " "
        for b_key, kws in bld_map:
            if any(k in padded for k in kws):
                bw, bf, bg = bld_costs[b_key]
                if rem_w >= bw and rem_f >= bf and rem_g >= bg:
                    rem_w -= bw
                    rem_f -= bf
                    rem_g -= bg
                    actions.append({"action_type": "BUILD", "building_type": b_key, "target_zone": zone if b_key in ("tower", "barracks") else "town_center", "reason": f"Talebiniz üzere {b_key} inşası."})
        q_units = q.replace("okcu poligon", "").replace("mancinik atolye", "")
        for u_key, kws in unit_map:
            if any(k in q_units for k in kws):
                m = re.search(r"(\d{1,2})\s*(?:adet|tane|x)?\s*[a-z]*(?:" + "|".join(kws) + ")", q_units)
                cnt = min(20, int(m.group(1))) if m else 3
                owned = get_army_cnt(u_key)
                if any(k in q for k in ["gonder", "yolla", "cek"]) and owned > 0:
                    actions.append({"action_type": "MOVE_GROUP", "unit_type": u_key, "count": min(owned, cnt), "target_zone": zone, "reason": f"Eldeki {u_key} birliklerini {zone} bölgesine gönder."})
                else:
                    try_train(u_key, cnt, zone, f"Talebiniz üzere {u_key} eğitimi.")
        if actions:
            return {"summary": f"Özel Emir Uygulandı (Bütçe kalanı: {rem_w} Odun, {rem_f} Gıda, {rem_g} Altın).", "confidence": 0.93, "actions": actions[:8]}

    if rem_pop <= 2 and pop_max < 60 and rem_w >= 30 and w_total > 0:
        rem_w -= 30
        actions.append({"action_type": "BUILD", "building_type": "house", "target_zone": "town_center", "reason": f"Nüfus sınıra yakın ({pop_cur}/{pop_max}); Ev inşa et."})

    if focus_key == "gold" and disc_gold == 0:
        actions.append({"action_type": "SCOUT", "unit_type": "soldier", "count": 1, "target_zone": "gold", "reason": "Altın düşük ve sis dışında altın madeni tespit edilmedi; altın keşfi yap."})
    elif w_total > 0:
        reassign_cnt = w_idle if w_idle > 0 else (2 if w_total >= 4 else 1)
        actions.append({"action_type": "ASSIGN_GATHER", "unit_type": "villager", "count": min(20, reassign_cnt), "target_zone": focus_key, "reason": f"Kaynak odağı {focus_tr}; {reassign_cnt} köylüyü bu kaynağa yönlendir."})

    if w_total < 7:
        try_train("villager", 1, "town_center", f"Toplam işçi ({w_total}) artırmak için 1 köylü eğit.")

    if explored_pct < 40 or (not visible_enemies and not last_known and not enemy_blds):
        actions.append({"action_type": "SCOUT", "unit_type": "soldier", "count": 1, "target_zone": "unexplored", "reason": f"Haritanın %{explored_pct}'i açık; sisli bölgeyi tara."})

    try_train(counter_unit, 3, threat_zone, f"Sis içi istihbarata karşı savunma/karşıt birlik ({counter_unit}) eğitimi.")

    enemy_note = (
        f"Aktif görüşte {len(visible_enemies)} düşman grubu ({threat_zone})"
        if visible_enemies
        else (f"Gri sis izinde {len(last_known)} grup / {len(enemy_blds)} düşman yapısı" if (last_known or enemy_blds) else f"Haritanın %{explored_pct}'i açık; rakip henüz sis altında")
    )
    summary = (
        f"Kaynaklar: {wood} Odun, {food} Gıda, {gold} Altın (Nüfus {pop_cur}/{pop_max}). "
        f"İşçiler ({w_total}): {w_food} Gıda, {w_wood} Odun, {w_gold} Altın, {w_idle} Boşta -> "
        f"ODAK: {focus_tr}. Sis: {enemy_note}."
    )
    return {"summary": summary[:650], "confidence": 0.94, "actions": actions[:8]}


class ThreadedHTTPServer(socketserver.ThreadingMixIn, HTTPServer):
    daemon_threads = True


def run_server(port=PORT):
    server_address = ("", port)
    httpd = ThreadedHTTPServer(server_address, GatewayRequestHandler)
    print("=" * 65)
    print(f"[AI GATEWAY] Sunucu http://localhost:{port} adresinde hazır!")
    print(f"[AI GATEWAY] Uç Noktalar:")
    print(f"  - GET  http://localhost:{port}/health")
    print(f"  - POST http://localhost:{port}/api/plan")
    print("=" * 65)
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\n[AI GATEWAY] Sunucu durduruldu.")
        httpd.server_close()


if __name__ == "__main__":
    run_server()
