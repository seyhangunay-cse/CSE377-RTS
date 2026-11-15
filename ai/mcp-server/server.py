import os
import sys

# ai/rag klasörünü sistem yoluna ekle
current_dir = os.path.dirname(os.path.abspath(__file__))
rag_dir = os.path.abspath(os.path.join(current_dir, "../rag"))
if rag_dir not in sys.path:
    sys.path.insert(0, rag_dir)

from rag_engine import get_rag_engine  # type: ignore

from mcp.server import MCPServer

# MCP Sunucusunu Başlat (Kılavuzdaki v2 standardı)
mcp = MCPServer("RTS Strategic Commander")

# Test aşamasında Unity'den gelecek güvenli snapshot verisini simüle eden mock store
# DİKKAT: Rakibin gizli bilgileri, harita sisi altındaki yerler burada yer almaz!
CURRENT_SAFE_STATE = {
    "match_id": "demo-match-01",
    "resources": {"wood": 450, "food": 200, "gold": 150},
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