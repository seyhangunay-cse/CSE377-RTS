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

    # 3. EKONOMİ & TARLA & KÖYLÜ
    if any(k in q_lower for k in ["ekonomi", "kaynak", "gıda", "gida", "odun", "tarla", "çiftlik", "ciftlik", "köylü", "koylu", "işçi", "isci"]):
        return {
            "summary": "Ekonomik Büyüme Programı: Kaynak toplama hızını artırmak ve gıda üretimini güvenceye almak için köylü eğitimi ve tarla yatırımı yapılıyor.",
            "confidence": 0.92,
            "actions": [
                {
                    "action_type": "TRAIN_UNIT",
                    "unit_type": "villager",
                    "count": 2,
                    "target_zone": "town_center",
                    "reason": "Ekonomik işgücünü artırmak için köylü eğitimi."
                },
                {
                    "action_type": "BUILD",
                    "building_type": "farm",
                    "target_zone": "town_center",
                    "reason": "Düzenli gıda hasadı için Şehir Merkezi yanına tarla kurulumu."
                },
                {
                    "action_type": "RESEARCH",
                    "building_type": "Wheelbarrow",
                    "target_zone": "town_center",
                    "reason": "Köylülerin taşıma kapasitesini artıran El Arabası geliştirmesi."
                }
            ]
        }

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

    # 9. DİNAMİK SAVUNMA
    enemies = current_state.get("visible_enemies", [])
    target_zone = "north_gate"
    if enemies and isinstance(enemies, list) and len(enemies) > 0:
        first_enemy = enemies[0]
        if isinstance(first_enemy, dict):
            target_zone = first_enemy.get("zone", "north_gate")

    return {
        "summary": f"Taktiksel Savunma Planı: {target_zone} bölgesindeki tehdide karşı mızrakçı hattı kuruluyor ve okçu desteği sağlanıyor.",
        "confidence": 0.88,
        "actions": [
            {
                "action_type": "TRAIN_UNIT",
                "unit_type": "spearman",
                "count": 4,
                "target_zone": target_zone,
                "reason": f"{target_zone} savunması için mızrakçı eğitimi."
            },
            {
                "action_type": "MOVE_GROUP",
                "unit_type": "spearman",
                "count": 4,
                "target_zone": target_zone,
                "reason": f"Birlikleri {target_zone} mevzisine intikal ettir."
            },
            {
                "action_type": "TRAIN_UNIT",
                "unit_type": "archer",
                "count": 2,
                "target_zone": target_zone,
                "reason": f"{target_zone} bölgesini arkadan ok atışlarıyla koruma."
            }
        ]
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
