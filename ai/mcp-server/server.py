import os
import sys

# ai/rag klasörünü sistem yoluna ekle
current_dir = os.path.dirname(os.path.abspath(__file__))
rag_dir = os.path.abspath(os.path.join(current_dir, "../rag"))
if rag_dir not in sys.path:
    sys.path.insert(0, rag_dir)

from rag_engine import get_rag_engine  # type: ignore

try:
    from mcp.server import MCPServer  # type: ignore
except ImportError:
    try:
        from mcp.server.fastmcp import FastMCP as MCPServer  # type: ignore
    except ImportError:
        class MCPServer:  # type: ignore
            def __init__(self, name: str):
                self.name = name
            def tool(self):
                def decorator(fn):
                    return fn
                return decorator
            def run(self):
                pass

# MCP Sunucusunu Başlat (Kılavuzdaki v2 standardı)
mcp = MCPServer("RTS Strategic Commander")

# İki oyunculu (Mavi vs Kırmızı) maçlarda her oyuncunun kendi AI Komutanı için izole hafıza (TEAM_SAFE_STATES)
# DİKKAT: Rakibin gizli bilgileri, harita sisi altındaki yerler burada yer almaz!
DEFAULT_STATE_TEMPLATE = {
    "match_id": "demo-match-01",
    "team_id": "BLUE",
    "resources": {"wood": 450, "food": 200, "gold": 150},
    "workers": {"total": 3, "food": 1, "wood": 1, "gold": 1, "building": 0, "idle": 0},
    "population": {"current": 4, "max": 10},
    "friendly_buildings": [
        {"type": "towncenter", "count": 1}
    ],
    "fog_intel": {
        "explored_percent": 18,
        "visible_percent": 12,
        "discovered_wood_nodes": 3,
        "discovered_food_nodes": 2,
        "discovered_gold_nodes": 1,
        "seconds_until_next_raid": 120,
        "last_known_enemies": [],
        "discovered_enemy_buildings": []
    },
    "army": [
        {"type": "spearman", "count": 4},
        {"type": "archer", "count": 2}
    ],
    "visible_enemies": [
        {"type": "cavalry", "count": 3, "zone": "north_gate"}
    ],
    "unlocked_tech": ["Age_1"],
    "recent_events": [
        {"type": "UNDER_ATTACK", "zone": "north_gate", "seconds_ago": 12}
    ]
}

TEAM_SAFE_STATES = {
    "BLUE": dict(DEFAULT_STATE_TEMPLATE, team_id="BLUE"),
    "RED": dict(DEFAULT_STATE_TEMPLATE, team_id="RED")
}

ACTIVE_TEAM_ID = "BLUE"
CURRENT_SAFE_STATE = TEAM_SAFE_STATES["BLUE"]

def set_active_team_snapshot(team_id: str = "BLUE", snapshot: dict = None):
    """Belirli bir takımın (BLUE veya RED) güvenli Savaş Sisi snapshot'ını günceller ve aktif komutan bağlamını seçer."""
    global ACTIVE_TEAM_ID, CURRENT_SAFE_STATE
    norm_team = "RED" if str(team_id).upper().strip() == "RED" else "BLUE"
    ACTIVE_TEAM_ID = norm_team
    state = TEAM_SAFE_STATES[norm_team]
    state["team_id"] = norm_team
    if isinstance(snapshot, dict):
        for key in (
            "match_id", "resources", "workers", "population",
            "fog_intel", "army", "friendly_buildings",
            "visible_enemies", "unlocked_tech", "recent_events"
        ):
            if key in snapshot and snapshot[key] is not None:
                state[key] = snapshot[key]
    CURRENT_SAFE_STATE = state
    return state

@mcp.tool()
def get_player_resources(*args, **kwargs) -> dict:
    """Mevcut oyuncunun odun, yiyecek ve altın kaynaklarını döner."""
    return CURRENT_SAFE_STATE["resources"]

@mcp.tool()
def get_army_status(*args, **kwargs) -> list[dict]:
    """Mevcut oyuncunun sahip olduğu askeri birlikleri listeler."""
    return CURRENT_SAFE_STATE["army"]

@mcp.tool()
def get_visible_enemies(*args, **kwargs) -> list[dict]:
    """Yalnızca savaş sisi (Fog-of-War) dışında kalan, görüş alanındaki düşmanları döner."""
    return CURRENT_SAFE_STATE["visible_enemies"]

@mcp.tool()
def get_technology_status(*args, **kwargs) -> list[str]:
    """Araştırılmış ve kilidi açılmış teknolojileri döner."""
    return CURRENT_SAFE_STATE["unlocked_tech"]

@mcp.tool()
def get_fog_intelligence(*args, **kwargs) -> dict:
    """
    Hilesiz Savaş Sisi (Fog-of-War) istihbaratını döner: keşfedilmiş harita yüzdesi,
    sis dışında tespit edilmiş kaynak yatakları, düşman binaları ve son görülen düşman birlik izleri.
    """
    fog = CURRENT_SAFE_STATE.get("fog_intel") or {}
    visible = CURRENT_SAFE_STATE.get("visible_enemies") or []
    return {
        "explored_percent": fog.get("explored_percent", 0),
        "visible_percent": fog.get("visible_percent", 0),
        "discovered_resources": {
            "wood_nodes": fog.get("discovered_wood_nodes", 0),
            "food_nodes": fog.get("discovered_food_nodes", 0),
            "gold_nodes": fog.get("discovered_gold_nodes", 0),
        },
        "seconds_until_next_raid": fog.get("seconds_until_next_raid", -1),
        "active_visible_enemies": visible,
        "last_known_enemies_in_fog": fog.get("last_known_enemies", []),
        "discovered_enemy_buildings": fog.get("discovered_enemy_buildings", []),
    }

@mcp.tool()
def get_worker_and_economy_report(*args, **kwargs) -> dict:
    """
    Kaynak stoklarını, köylü işçi görev dağılımını (gıda/odun/altın/inşaat/boşta),
    nüfus kapasitesini, dost binaları ve odaklanılması gereken öncelikli kaynağı analiz eder.
    """
    res = CURRENT_SAFE_STATE.get("resources") or {}
    w = CURRENT_SAFE_STATE.get("workers") or {}
    pop = CURRENT_SAFE_STATE.get("population") or {}
    fog = CURRENT_SAFE_STATE.get("fog_intel") or {}

    wood = int(res.get("wood", 0))
    food = int(res.get("food", 0))
    gold = int(res.get("gold", 0))
    w_food = int(w.get("food", 0))
    w_wood = int(w.get("wood", 0))
    w_gold = int(w.get("gold", 0))
    w_idle = int(w.get("idle", 0))
    w_total = int(w.get("total", 0))

    scores = {
        "food": (55 if food < 120 else (25 if food < 220 else 0)) + (45 if w_food == 0 and w_total > 0 else 0),
        "wood": (50 if wood < 120 else (22 if wood < 200 else 0)) + (42 if w_wood == 0 and w_total > 0 else 0),
        "gold": (48 if gold < 90 else (20 if gold < 180 else 0)) + (36 if w_gold == 0 and w_total >= 2 else 0),
    }
    focus_resource = max(scores, key=lambda k: scores[k])
    return {
        "resources": {"wood": wood, "food": food, "gold": gold},
        "workers": w,
        "population": pop,
        "friendly_buildings": CURRENT_SAFE_STATE.get("friendly_buildings", []),
        "recommended_focus_resource": focus_resource,
        "idle_workers": w_idle,
        "discovered_gold_nodes": fog.get("discovered_gold_nodes", 0),
    }

@mcp.tool()
def estimate_plan_cost(unit_type: str = "spearman", count: int = 1, *args, **kwargs) -> dict:
    """
    Belirli sayıda birim üretmenin maliyetini ve oyuncunun bunu karşılayıp karşılayamayacağını hesaplar.
    """
    unit_costs = {
        "spearman": {"food": 60, "gold": 20, "wood": 0},
        "archer": {"wood": 50, "gold": 30, "food": 0},
        "cavalry": {"food": 100, "gold": 80, "wood": 0},
        "swordsman": {"food": 60, "gold": 30, "wood": 0},
        "crossbowman": {"wood": 60, "gold": 40, "food": 0},
        "trebuchet": {"wood": 200, "gold": 150, "food": 0}
    }
    
    try:
        count = int(count)
    except (ValueError, TypeError):
        count = 1

    unit_key = str(unit_type).lower().strip()
    if unit_key not in unit_costs:
        return {"error": f"Bilinmeyen birim tipi: {unit_type}"}
        
    req = unit_costs[unit_key]
    total_cost = {res: req[res] * count for res in req}
    res = CURRENT_SAFE_STATE["resources"]
    
    affordable = (
        res.get("food", 0) >= total_cost.get("food", 0) and
        res.get("gold", 0) >= total_cost.get("gold", 0) and
        res.get("wood", 0) >= total_cost.get("wood", 0)
    )
    
    return {
        "unit": unit_type,
        "count": count,
        "total_cost": total_cost,
        "available_resources": res,
        "affordable": affordable
    }

@mcp.tool()
def search_game_knowledge(query: str = "counter", top_k: int = 3, *args, **kwargs) -> list[dict]:
    """
    Oyun kuralları, birim istatistikleri, karşıtlıklar (counters) ve teknolojiler hakkında bilgi bankasında RAG araması yapar.
    """
    try:
        top_k = int(top_k)
    except (ValueError, TypeError):
        top_k = 3
    engine = get_rag_engine()
    results = engine.search(str(query), top_k=top_k)
    return results

if __name__ == "__main__":
    mcp.run()