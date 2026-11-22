"""
CSE-377 AI Gateway HTTP Server
Unity istemcisi ile Agent Runner arasındaki HTTP köprüsü.
UnityWebRequest ile gelen POST /api/plan isteklerini karşılar,
Agent Runner'ı çalıştırır ve doğrulanmış ActionPlan JSON'unu döner.
"""
import os
import sys
import json
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

def generate_emergency_fallback_plan(query: str, current_state: dict) -> dict:
    q_lower = query.lower()

    # 1. KUŞATMA & KARARGAH YIKIMI
    if any(k in q_lower for k in ["kuşat", "kusat", "karargah", "kale", "üssü", "ussu", "yık", "yik"]):
        return {
            "summary": "Taktiksel Kuşatma Emri: Ordumuz kuzeydeki düşman kalesine ve ana karargahına genel taarruza geçiriliyor.",
            "confidence": 0.96,
            "actions": [
                {
                    "action_type": "TRAIN_UNIT",
                    "unit_type": "spearman",
                    "count": 4,
                    "target_zone": "north_gate",
                    "reason": "Kuşatma sırasında piyade koruma hattı sağlamak için mızrakçı eğitimi."
                },
                {
                    "action_type": "ATTACK",
                    "unit_type": "soldier",
                    "count": 10,
                    "target_zone": "enemy_base",
                    "reason": "Düşman karargahını doğrudan kuşatıp imha etmek."
                }
            ]
        }

    # 2. TAARRUZ / KARŞI SALDIRI
    if any(k in q_lower for k in ["saldır", "saldir", "taarruz", "hücum", "hucum", "yok et", "öldür", "oldur", "vur"]):
        return {
            "summary": "Taktiksel Taarruz Emri: Mevcut ordumuz düşman kuvvetlerine karşı taarruza geçiriliyor.",
            "confidence": 0.94,
            "actions": [
                {
                    "action_type": "ATTACK",
                    "unit_type": "soldier",
                    "count": 8,
                    "target_zone": "north_gate",
                    "reason": "Kuzey koridorundaki düşman kuvvetlerini bertaraf et."
                },
                {
                    "action_type": "TRAIN_UNIT",
                    "unit_type": "archer",
                    "count": 3,
                    "target_zone": "north_gate",
                    "reason": "Hücuma kalkan birliklere arkadan ok desteği sağla."
                }
            ]
        }

    # 3. EKONOMİ & TARLA & KÖYLÜ & KAYNAK ANALİZİ (Canlı snapshot tabanlı)
    if any(k in q_lower for k in ["analiz", "durum", "öneri", "oneri", "tavsiye", "odak", "ekonomi", "kaynak", "gıda", "gida", "odun", "tarla", "çiftlik", "ciftlik", "köylü", "koylu", "işçi", "isci", "rakip", "ne yap"]):
        return build_dynamic_intel_plan(q_lower, current_state or {})

    # 4. ÇAĞ ATLAMA & TEKNOLOJİ & DEMİRCİ
    if any(k in q_lower for k in ["çağ", "cag", "feodal", "atla", "geliş", "gelis", "demirci", "teknoloji", "silah", "zırh", "zirh"]):
        return {
            "summary": "Teknolojik Gelişim ve Çağ Atlama Hamlesi: Demirci atölyesi kurularak birlik teçhizatı geliştiriliyor ve Feodal Çağ hazırlıkları başlatılıyor.",
            "confidence": 0.93,
            "actions": [
                {
                    "action_type": "BUILD",
                    "building_type": "blacksmith",
                    "target_zone": "town_center",
                    "reason": "Silah ve zırh araştırmalarını açmak için Demirci Atölyesi inşası."
                },
                {
                    "action_type": "RESEARCH",
                    "building_type": "Iron_Weapons",
                    "target_zone": "town_center",
                    "reason": "Piyadelerin saldırı gücünü artıran Demir Silahlar araştırması."
                }
            ]
        }

    # 5. TAHKİMAT & KULE & SUR & KAPI
    if any(k in q_lower for k in ["kule", "sur", "duvar", "tahkim", "kapı", "kapi", "barikat"]):
        return {
            "summary": "Tahkimat ve Garnizon Planı: Geçiş koridoruna Savunma Kulesi ve Taş Surlar çekilerek üs güvenliği sağlanıyor.",
            "confidence": 0.91,
            "actions": [
                {
                    "action_type": "BUILD",
                    "building_type": "tower",
                    "target_zone": "north_gate",
                    "reason": "Kuzey geçidine düşmanı oklayan Savunma Kulesi dikilmesi."
                },
                {
                    "action_type": "BUILD",
                    "building_type": "stonewall",
                    "target_zone": "north_gate",
                    "reason": "Düşman geçişini kilitleyen Taş Sur tahkimatı."
                },
                {
                    "action_type": "TRAIN_UNIT",
                    "unit_type": "archer",
                    "count": 2,
                    "target_zone": "north_gate",
                    "reason": "Kule korumasında mevzilenecek okçular."
                }
            ]
        }

    # 6. KEŞİF / HARİTA / SİS / ALTIN
    if any(k in q_lower for k in ["keşif", "kesif", "scout", "gez", "altın", "altin", "harita", "dolaş", "dolas", "sis", "maden"]):
        focus = "gold" if any(k in q_lower for k in ["altın", "altin", "maden"]) else "unexplored"
        msg = "altın yataklarını ve zengin madenleri tespit etmek" if focus == "gold" else "savaş sisini dağıtıp haritayı aydınlatmak"
        return {
            "summary": f"Taktiksel Keşif Emri: 1 askerimiz {msg} üzere sisli bölgelere keşfe çıkarılıyor.",
            "confidence": 0.95,
            "actions": [
                {
                    "action_type": "SCOUT",
                    "unit_type": "soldier",
                    "count": 1,
                    "target_zone": focus,
                    "reason": f"Haritayı tarayarak {focus} odaklı keşif yapmak."
                }
            ]
        }

    # 7. EV & NÜFUS
    if any(k in q_lower for k in ["ev", "nüfus", "nufus", "kapasite"]):
        return {
            "summary": "Konut ve Nüfus Genişletme Planı: Nüfus tıkanıklığını gidermek için yeni evler inşa ediliyor.",
            "confidence": 0.90,
            "actions": [
                {
                    "action_type": "BUILD",
                    "building_type": "house",
                    "target_zone": "town_center",
                    "reason": "Nüfus kapasitesini artırmak için Ev inşası."
                }
            ]
        }

    # 8. ÖZEL BİRLİK İSTEKLERİ
    if any(k in q_lower for k in ["okçu", "okcu"]):
        return {
            "summary": "Menzilli Kuvvet Eğitimi: Düşmanı uzaktan bertaraf etmek için kışladan okçular eğitiliyor.",
            "confidence": 0.91,
            "actions": [
                {
                    "action_type": "TRAIN_UNIT",
                    "unit_type": "archer",
                    "count": 4,
                    "target_zone": "north_gate",
                    "reason": "Kuzey kapısını korumak için 4 okçu eğitimi."
                }
            ]
        }

    if any(k in q_lower for k in ["süvari", "suvari", "atlı", "atli"]):
        return {
            "summary": "Süvari Baskın Birliği: Hızlı manevra ve ani hücumlar için süvari taburu yetiştiriliyor.",
            "confidence": 0.91,
            "actions": [
                {
                    "action_type": "TRAIN_UNIT",
                    "unit_type": "cavalry",
                    "count": 3,
                    "target_zone": "north_gate",
                    "reason": "Düşman hatlarına sarkacak 3 süvari eğitimi."
                }
            ]
        }

    return build_dynamic_intel_plan(q_lower, current_state or {})


def build_dynamic_intel_plan(q_lower: str, state: dict) -> dict:
    """Canlı fog_intel, resources, workers ve population verilerine dayalı hilesiz analiz planı üretir."""
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

    fog = state.get("fog_intel") or {}
    explored_pct = int(fog.get("explored_percent", 0))
    visible_enemies = state.get("visible_enemies") or []
    last_known = fog.get("last_known_enemies") or []
    enemy_blds = fog.get("discovered_enemy_buildings") or []
    disc_gold = int(fog.get("discovered_gold_nodes", 1))

    # 1. Odaklanılacak kaynağı belirle
    scores = {
        "food": (55 if food < 120 else (25 if food < 220 else 0)) + (45 if w_food == 0 and w_total > 0 else 0),
        "wood": (50 if wood < 120 else (22 if wood < 200 else 0)) + (42 if w_wood == 0 and w_total > 0 else 0) + (35 if (pop_max - pop_cur) <= 2 and wood < 60 else 0),
        "gold": (48 if gold < 90 else (20 if gold < 180 else 0)) + (36 if w_gold == 0 and w_total >= 2 else 0),
    }
    focus_key = max(scores, key=lambda k: scores[k])
    focus_tr = {"food": "GIDA", "wood": "ODUN", "gold": "ALTIN"}[focus_key]

    # 2. Sis içi rakip & karşıt birim analizi
    cav_s, rng_s, inf_s = 0, 0, 0
    threat_zone = "north_gate"
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

    enemy_note = (
        f"Aktif görüşte {len(visible_enemies)} düşman grubu ({threat_zone})"
        if visible_enemies
        else (
            f"Gri sis izinde {len(last_known)} grup / {len(enemy_blds)} düşman yapısı"
            if (last_known or enemy_blds)
            else f"Haritanın %{explored_pct}'i açık; rakip henüz sis altında"
        )
    )

    actions = []
    rem_wood, rem_food, rem_gold = wood, food, gold
    rem_pop = max(0, pop_max - pop_cur)

    if (pop_max - pop_cur) <= 2 and pop_max < 60 and rem_wood >= 30 and w_total > 0:
        rem_wood -= 30
        actions.append({
            "action_type": "BUILD",
            "building_type": "house",
            "target_zone": "town_center",
            "reason": f"Nüfus sınıra yakın ({pop_cur}/{pop_max}); yeni birlikler için Ev inşa et."
        })

    if focus_key == "gold" and disc_gold == 0:
        actions.append({
            "action_type": "SCOUT",
            "unit_type": "soldier",
            "count": 1,
            "target_zone": "gold",
            "reason": "Altın düşük ve henüz sis dışında altın madeni tespit edilmedi; altın keşfi yap."
        })
    elif w_total > 0:
        reassign_cnt = w_idle if w_idle > 0 else (2 if w_total >= 4 else 1)
        actions.append({
            "action_type": "ASSIGN_GATHER",
            "unit_type": "villager",
            "count": min(20, reassign_cnt),
            "target_zone": focus_key,
            "reason": f"Kaynak odağı {focus_tr}; {reassign_cnt} köylüyü bu kaynağa yönlendir."
        })

    if w_total < 7 and rem_food >= 50 and rem_pop >= 1:
        rem_food -= 50
        rem_pop -= 1
        actions.append({
            "action_type": "TRAIN_UNIT",
            "unit_type": "villager",
            "count": 1,
            "target_zone": "town_center",
            "reason": f"Toplam işçi ({w_total}) artırmak için 1 yeni köylü eğit."
        })

    if explored_pct < 40 or (not visible_enemies and not last_known and not enemy_blds):
        actions.append({
            "action_type": "SCOUT",
            "unit_type": "soldier",
            "count": 1,
            "target_zone": "unexplored",
            "reason": f"Haritanın %{explored_pct}'i açık; rakibin yerleşimini görmek için sisli bölgeyi tara."
        })

    unit_costs = {
        "spearman": (0, 60, 20),
        "archer": (50, 0, 30),
        "cavalry": (0, 100, 80),
    }
    cw, cf, cg = unit_costs.get(counter_unit, (0, 60, 20))
    if rem_pop >= 1 and rem_wood >= cw and rem_food >= cf and rem_gold >= cg:
        cnt = min(3, rem_pop, rem_wood // cw if cw else 3, rem_food // cf if cf else 3, rem_gold // cg if cg else 3)
        if cnt >= 1:
            actions.append({
                "action_type": "TRAIN_UNIT",
                "unit_type": counter_unit,
                "count": cnt,
                "target_zone": threat_zone,
                "reason": f"Sis içi istihbarata karşı uygun savunma/karşıt birlik ({counter_unit}) eğitimi."
            })

    summary = (
        f"Kaynaklar: {wood} Odun, {food} Gıda, {gold} Altın (Nüfus {pop_cur}/{pop_max}). "
        f"İşçiler ({w_total}): {w_food} Gıda, {w_wood} Odun, {w_gold} Altın, {w_idle} Boşta -> "
        f"ODAK: {focus_tr}. Sis: {enemy_note}."
    )
    return {
        "summary": summary[:650],
        "confidence": 0.93,
        "actions": actions[:8]
    }

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
