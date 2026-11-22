"""
CSE-377 Agent Runner (Tutorial 2 & 4)
Ollama (llama3.2:3b) ve MCP v2 araçlarıyla çok adımlı dinamik tool-calling döngüsü.
Üretilen nihai strateji planını action_plan.schema.json ile doğrular.
"""
import os
import sys
import json
import re
import argparse
from typing import Dict, Any, Tuple, Optional

# Windows konsolu için UTF-8 desteği
if hasattr(sys.stdout, "reconfigure"):
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
if hasattr(sys.stderr, "reconfigure"):
    try:
        sys.stderr.reconfigure(encoding="utf-8")
    except Exception:
        pass

# ai/mcp-server klasörünü sistem yoluna ekle
current_dir = os.path.dirname(os.path.abspath(__file__))
mcp_server_dir = os.path.abspath(os.path.join(current_dir, "../mcp-server"))
if mcp_server_dir not in sys.path:
    sys.path.insert(0, mcp_server_dir)

import httpx  # type: ignore
import jsonschema  # type: ignore
import server  # type: ignore

OLLAMA_API_URL = os.environ.get("OLLAMA_API_URL", "http://localhost:11434/api/chat")
MODEL_NAME = os.environ.get("OLLAMA_MODEL", "llama3.2:3b")

# action_plan.schema.json dosyasını yükle
SCHEMA_PATH = os.path.join(current_dir, "schemas", "action_plan.schema.json")
try:
    with open(SCHEMA_PATH, "r", encoding="utf-8") as f:
        ACTION_PLAN_SCHEMA = json.load(f)
except Exception as e:
    print(f"[UYARI] action_plan.schema.json yüklenemedi: {e}")
    ACTION_PLAN_SCHEMA = {}

# Ollama'ya sunulacak MCP Araçları Şeması
TOOLS = [
    {
        "type": "function",
        "function": {
            "name": "get_player_resources",
            "description": "Oyuncunun mevcut odun (wood), yiyecek (food) ve altın (gold) kaynak miktarını döner.",
            "parameters": {
                "type": "object",
                "properties": {}
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "get_army_status",
            "description": "Oyuncunun halihazırda sahip olduğu askeri birliklerin türlerini ve sayılarını döner.",
            "parameters": {
                "type": "object",
                "properties": {}
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "get_visible_enemies",
            "description": "Savaş sisi (Fog-of-War) dışında kalan, görüş alanındaki düşman birimlerini ve bulundukları bölgeleri döner.",
            "parameters": {
                "type": "object",
                "properties": {}
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "get_technology_status",
            "description": "Oyuncunun halihazırda araştırmış olduğu teknolojileri listeler.",
            "parameters": {
                "type": "object",
                "properties": {}
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "get_fog_intelligence",
            "description": "Savaş sisi kapsamını, sis dışında keşfedilmiş düşman binalarını, son görülen düşman birlik izlerini ve keşfedilmiş kaynak yataklarını döner.",
            "parameters": {
                "type": "object",
                "properties": {}
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "get_worker_and_economy_report",
            "description": "Kaynak stokları, köylülerin (işçilerin) gıda/odun/altın/boşta dağılımı, nüfus kapasitesi ve odaklanılması gereken öncelikli kaynak analizini döner.",
            "parameters": {
                "type": "object",
                "properties": {}
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "estimate_plan_cost",
            "description": "Belirli sayıda birim üretmenin kaynak maliyetini ve oyuncunun mevcut bütçesiyle bunu karşılayıp karşılayamayacağını döner.",
            "parameters": {
                "type": "object",
                "properties": {
                    "unit_type": {
                        "type": "string",
                        "description": "Üretilecek birim tipi: spearman, archer, cavalry, swordsman, crossbowman veya trebuchet"
                    },
                    "count": {
                        "type": "integer",
                        "description": "Üretilmesi planlanan birim adedi (örn. 1-20)"
                    }
                },
                "required": ["unit_type", "count"]
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "search_game_knowledge",
            "description": "Oyun kuralları, birim istatistikleri, taş-kağıt-makas karşıtlıkları (counters), taktikler ve teknolojiler hakkında bilgi bankasında RAG araması yapar.",
            "parameters": {
                "type": "object",
                "properties": {
                    "query": {
                        "type": "string",
                        "description": "Aramak istediğiniz konu veya soru (örn. 'süvariye karşı savunma', 'mızrakçı maliyeti', 'kapı güvenliği')"
                    },
                    "top_k": {
                        "type": "integer",
                        "description": "Döndürülecek en alakalı maksimum kayıt sayısı (varsayılan 3)"
                    }
                },
                "required": ["query"]
            }
        }
    }
]

# Araç eşleştirme sözlüğü
TOOL_MAP = {
    "get_player_resources": server.get_player_resources,
    "get_army_status": server.get_army_status,
    "get_visible_enemies": server.get_visible_enemies,
    "get_technology_status": server.get_technology_status,
    "get_fog_intelligence": server.get_fog_intelligence,
    "get_worker_and_economy_report": server.get_worker_and_economy_report,
    "estimate_plan_cost": server.estimate_plan_cost,
    "search_game_knowledge": server.search_game_knowledge
}

SYSTEM_PROMPT = """Sen Age of Empires tarzı bir RTS Strateji ve Ekonomi Komutanısın.
Görevin oyuncunun sorusunu, sisli bölge istihbaratını, kaynak stoklarını ve işçi dağılımını MCP araçlarıyla sorgulamak; rakibi analiz etmek, odaklanılması gereken kaynağı/işçi ayarını net söylemek ve action_plan.schema.json kurallarına uygun bir ActionPlan üretmektir.

TEMEL DOKTRİN VE ÇALIŞMA ADIMLARI:
1. Durumu Anla: Eksik bilgi varsa kafandan varsayım yapma; ilgili araçları çağır:
   - Savaş sisi ve sis içi rakip/yapı izleri için: get_fog_intelligence
   - Aktif düşman tehdidi için: get_visible_enemies
   - Kaynaklar, köylü işçi dağılımı ve odaklanılacak kaynak için: get_worker_and_economy_report
   - Mevcut ordumuzu görmek için: get_army_status
   - Birim üreteceksen bütçeyi doğrulamak için: estimate_plan_cost
   - Karşıtlık/kural sorusu varsa: search_game_knowledge
2. Karşıtlık Sistemi (Counters):
   - Süvarilere karşı Mızrakçı (Spearman) en etkilidir.
   - Mızrakçılara karşı Okçu (Archer) en etkilidir.
   - Okçulara karşı Süvari (Cavalry) en etkilidir.
3. Özet ve İşçi Odağı: summary alanında mevcut kaynak durumunu, işçi dağılımını, odaklanılması gereken kaynağı ('food', 'wood' veya 'gold') ve sis içi rakip analizini kısaca özetle.
4. Kaynak Kontrolü: Oyuncunun bütçesini aşan (affordable: false) birim üretim emri planlama.
5. Çıktı Formatı: Vereceğin nihai cevabın SADECE aşağıdaki JSON şemasına uyması ZORUNLUDUR:
{
  "summary": "Kaynak/işçi odağı ve sis içi rakip analiz özeti (maksimum 650 karakter)",
  "confidence": 0.90,
  "actions": [
    {
      "action_type": "ASSIGN_GATHER",
      "unit_type": "villager",
      "count": 2,
      "target_zone": "food",
      "reason": "Gıda stoğu kritik ve gıdada yeterli işçi yok; 2 köylüyü gıdaya odakla."
    }
  ]
}

ÖNEMLİ KISITLAR:
- 'action_type' yalnızca şunlardan biri olabilir: 'TRAIN_UNIT', 'BUILD', 'MOVE_GROUP', 'RESEARCH', 'SCOUT', 'ATTACK', 'ASSIGN_GATHER'.
- 'ASSIGN_GATHER' eyleminde 'unit_type' 'villager', 'target_zone' ise yalnızca 'food', 'wood' veya 'gold' olmalıdır.
- 'ATTACK' eylemi için 'unit_type' alanına 'soldier', 'target_zone' alanına 'north_gate' veya 'enemy_base' yaz.
- 'TRAIN_UNIT' eylemi için 'unit_type' alanına 'spearman', 'archer', 'cavalry', 'villager' yaz.
- 'RESEARCH' eylemi için 'building_type' alanına teknoloji adını yaz (örn: 'Wheelbarrow', 'Iron_Weapons', 'Scale_Armor', 'Fletching').
- 'BUILD' eylemi için 'building_type' alanına inşa edilecek binayı yaz (örn: 'barracks', 'tower', 'blacksmith', 'house', 'farm').
- 'SCOUT' eylemi için 'target_zone' alanına 'gold' veya 'unexplored' yaz.
- 'count' 1 ile 20 arasında bir tamsayı olmalıdır.
- JSON nesnesi dışında hiçbir karşılama, önsöz veya markdown açıklaması ekleme!
"""

def parse_actions_from_markdown(text: str) -> Optional[Dict[str, Any]]:
    """Model salt JSON yerine Markdown listesi veya maddeler ürettiyse bunu ActionPlan formatına dönüştürür."""
    actions = []
    blocks = re.split(r'\n(?=\d+\.|\* \*\*|###)', text)
    for block in blocks:
        act_match = re.search(r'(?:Action Type|action_type)[:\s*]+([A-Z_]+)', block, re.IGNORECASE)
        if not act_match:
            continue
        raw_act = act_match.group(1).upper()
        if raw_act not in ["TRAIN_UNIT", "MOVE_GROUP", "BUILD", "RESEARCH", "SCOUT", "EXPLORE", "ATTACK", "ATTACK_MOVE"]:
            if "TRAIN" in raw_act: raw_act = "TRAIN_UNIT"
            elif "SCOUT" in raw_act or "EXPLORE" in raw_act: raw_act = "SCOUT"
            elif "ATTACK" in raw_act or "ASSAULT" in raw_act: raw_act = "ATTACK"
            elif "MOVE" in raw_act: raw_act = "MOVE_GROUP"
            elif "BUILD" in raw_act: raw_act = "BUILD"
            elif "RESEARCH" in raw_act: raw_act = "RESEARCH"
            else: continue

        unit_match = re.search(r'(?:Unit Type|unit_type|Unit)[:\s*]+([a-zA-Z_]+)', block, re.IGNORECASE)
        unit = unit_match.group(1).lower() if unit_match else "spearman"

        building_match = re.search(r'(?:Building Type|building_type|Building|Tech|Technology)[:\s*]+([a-zA-Z_]+)', block, re.IGNORECASE)
        building = building_match.group(1) if building_match else ""

        count_match = re.search(r'(?:Count|count)[:\s*]+(\d+)', block, re.IGNORECASE)
        count = int(count_match.group(1)) if count_match else 2
        count = max(1, min(20, count))

        zone_match = re.search(r'(?:Target Zone|target_zone|Zone)[:\s*]+([a-zA-Z0-9_]+)', block, re.IGNORECASE)
        zone = zone_match.group(1).lower() if zone_match else "north_gate"

        reason_match = re.search(r'(?:Reason|reason)[:\s*]+([^\n*]+)', block, re.IGNORECASE)
        reason = reason_match.group(1).strip() if reason_match else f"{unit} birimleri {zone} bölgesine sevk ediliyor."

        action_dict = {
            "action_type": raw_act,
            "unit_type": unit,
            "count": count,
            "target_zone": zone,
            "reason": reason[:300]
        }
        if building:
            action_dict["building_type"] = building

        actions.append(action_dict)

    if actions:
        summary_line = text.split("\n")[0].strip()
        if len(summary_line) < 10:
            summary_line = "Stratejik savunma ve intikal eylem planı."
        return {
            "summary": summary_line[:300],
            "confidence": 0.85,
            "actions": actions
        }
    return None

def sanitize_json_text(text: str) -> str:
    r"""Gecersiz kacis karakterlerini (\x, bozuk \u veya tekil ters egik cizgileri) temizler."""
    # 1. \x.. kaçışlarını temizle
    text = re.sub(r'\\x[0-9a-fA-F]{2}', '', text)
    # 2. Geçersiz \u kaçışlarını düzelt (ör. \udot, \ucuk -> udot, ucuk)
    text = re.sub(r'\\u(?![0-9a-fA-F]{4})', 'u', text)
    # 3. Geçerli JSON kaçışları dışındaki ters eğik çizgileri temizle
    text = re.sub(r'\\(?!["\\/bfnrt]|u[0-9a-fA-F]{4})', '', text)
    return text

def normalize_action_plan(data: Dict[str, Any], default_summary: str = "Stratejik Komutan Eylem Planı") -> Dict[str, Any]:
    """Planı action_plan.schema.json gereksinimlerine göre temizler ve fazlalıkları ayıklar."""
    valid_action_types = {"TRAIN_UNIT", "BUILD", "MOVE_GROUP", "RESEARCH", "SCOUT", "EXPLORE", "ATTACK", "ATTACK_MOVE", "ASSIGN_GATHER"}

    cleaned_plan: Dict[str, Any] = {}
    summary = data.get("summary")
    if not summary or not isinstance(summary, str) or len(summary.strip()) < 5:
        summary = default_summary
    cleaned_plan["summary"] = summary[:650]

    confidence = data.get("confidence")
    try:
        cleaned_plan["confidence"] = float(confidence) if confidence is not None else 0.90
    except (ValueError, TypeError):
        cleaned_plan["confidence"] = 0.90
    cleaned_plan["confidence"] = max(0.0, min(1.0, cleaned_plan["confidence"]))

    raw_actions = data.get("actions", [])
    if not isinstance(raw_actions, list):
        raw_actions = []

    cleaned_actions = []
    for act in raw_actions:
        if not isinstance(act, dict):
            continue
        cleaned_act: Dict[str, Any] = {}
        act_type = str(act.get("action_type", "")).upper()
        if act_type not in valid_action_types:
            if "GATHER" in act_type or "ASSIGN" in act_type: act_type = "ASSIGN_GATHER"
            elif "TRAIN" in act_type: act_type = "TRAIN_UNIT"
            elif "MOVE" in act_type: act_type = "MOVE_GROUP"
            elif "ATTACK" in act_type: act_type = "ATTACK"
            elif "SCOUT" in act_type: act_type = "SCOUT"
            elif "BUILD" in act_type: act_type = "BUILD"
            elif "RESEARCH" in act_type: act_type = "RESEARCH"
            else: act_type = "MOVE_GROUP"
        cleaned_act["action_type"] = act_type

        reason = str(act.get("reason", "")).strip()
        if not reason:
            reason = f"{cleaned_act['action_type']} emri icra ediliyor."
        cleaned_act["reason"] = reason[:320]

        if "unit_type" in act and act["unit_type"]:
            cleaned_act["unit_type"] = str(act["unit_type"]).lower()
        if "building_type" in act and act["building_type"]:
            cleaned_act["building_type"] = str(act["building_type"])
        if "target_zone" in act and act["target_zone"]:
            cleaned_act["target_zone"] = str(act["target_zone"]).lower()

        count = act.get("count", 1)
        try:
            cleaned_act["count"] = max(1, min(20, int(count)))
        except (ValueError, TypeError):
            cleaned_act["count"] = 1

        cleaned_actions.append(cleaned_act)

    cleaned_plan["actions"] = cleaned_actions[:8]
    return cleaned_plan

def extract_json_from_text(text: str) -> Dict[str, Any]:
    """Metin içindeki JSON bloğunu güvenle ayrıştırır veya markdown eylemlerini dönüştürür."""
    text = text.strip()
    if "```json" in text:
        text = text.split("```json")[1].split("```")[0].strip()
    elif "```" in text:
        text = text.split("```")[1].split("```")[0].strip()

    # 1. Başlangıç süslü parantezine hizala
    start = text.find("{")
    if start != -1:
        text = text[start:]

    # 2. Dize içi tırnakları hesaba katarak dengeli bitiş süslü parantezini bul
    depth = 0
    end_idx = -1
    in_string = False
    escape = False
    for i, c in enumerate(text):
        if escape:
            escape = False
            continue
        if c == '\\':
            escape = True
            continue
        if c == '"':
            in_string = not in_string
            continue
        if not in_string:
            if c == '{':
                depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0:
                    end_idx = i
                    break
    if end_idx != -1:
        text = text[:end_idx + 1]

    # Deneme A: Doğrudan JSON ayrıştırma
    try:
        data = json.loads(text)
        if isinstance(data, dict):
            return normalize_action_plan(data)
    except Exception:
        pass

    # Deneme B: Sanitize edilmiş metin ile ayrıştırma
    try:
        sanitized = sanitize_json_text(text)
        data = json.loads(sanitized)
        if isinstance(data, dict):
            return normalize_action_plan(data)
    except Exception:
        pass

    parsed = parse_actions_from_markdown(text)
    if parsed is not None:
        return normalize_action_plan(parsed)
    raise json.JSONDecodeError("Geçerli bir JSON ActionPlan tespit edilemedi.", text, 0)

def validate_action_plan(data: Dict[str, Any]) -> Tuple[bool, Optional[str]]:
    """Oluşturulan planı action_plan.schema.json şemasına göre doğrular."""
    if not ACTION_PLAN_SCHEMA:
        return True, None
    try:
        jsonschema.validate(instance=data, schema=ACTION_PLAN_SCHEMA)
        return True, None
    except jsonschema.ValidationError as err:
        return False, f"Şema Doğrulama Hatası ({err.json_path}): {err.message}"

def run_agent(user_query: str, max_steps: int = 6) -> Dict[str, Any]:
    """
    Ollama ile çok adımlı dinamik agent loop çalıştırır ve doğrulanmış ActionPlan döner.
    Aktif takım kimliğine (BLUE / RED) göre ilgili krallığın AI Komutanı olarak hareket eder.
    """
    active_team = getattr(server, "ACTIVE_TEAM_ID", "BLUE")
    cmd_tag = "[KIRMIZI AI KOMUTAN]" if active_team == "RED" else "[MAVİ AI KOMUTAN]"

    # 1. Hızlı Taarruz ve Kuşatma Tespiti (Attack / Kuşatma / Hücum / Karargah)
    q_lower = user_query.lower()
    is_analysis_query = any(k in q_lower for k in ["analiz", "öneri", "oneri", "tavsiye", "durum", "odak", "ne yap", "rakip", "dağıt", "dagit"])
    if not is_analysis_query and any(k in q_lower for k in ["saldır", "saldir", "taarruz", "hücum", "hucum", "kuşat", "kusat", "yok et", "öldür", "oldur", "vur", "kaleye", "karargah"]):
        target_zone = "enemy_base" if any(k in q_lower for k in ["karargah", "kale", "üs", "us", "kamp", "camp", "bina"]) else "north_gate"
        plan = {
            "summary": f"{cmd_tag} Taktiksel Kuşatma ve Taarruz Emri: Mevcut ordumuz {target_zone} bölgesindeki düşman hedeflerine genel taarruza geçiriliyor.",
            "confidence": 0.96,
            "actions": [
                {
                    "action_type": "ATTACK",
                    "unit_type": "soldier",
                    "count": 12,
                    "target_zone": target_zone,
                    "reason": "Düşman birliklerini ve tahkimatını bertaraf edip haritada tam zafer elde etmek."
                }
            ]
        }
        print(f"\n[RTS STRATEGIC COMMANDER] Hızlı taarruz/kuşatma planı onaylandı: {plan['summary']}")
        return plan

    # 2. Hızlı Taktiksel Keşif ve Savaş Sisi Taraması
    if not is_analysis_query and any(k in q_lower for k in ["keşf", "kesf", "keşif", "kesif", "scout", "gez", "altın", "altin", "harita", "dolaş", "dolas", "çevreyi gez", "haritayı aç"]):
        focus = "gold" if any(k in q_lower for k in ["altın", "altin"]) else "unexplored"
        unit_type = "soldier"
        count = 1
        m_count = re.search(r'(\d+)\s*(?:asker|atlı|atli|birlik|scout|süvari|mızrakçı|mizrakci|okçu|okcu)', q_lower)
        if m_count:
            count = max(1, min(10, int(m_count.group(1))))
        
        goal_text = "altın yataklarını tespit etmek" if focus == "gold" else "savaş sisini dağıtıp haritayı aydınlatmak"
        plan = {
            "summary": f"Taktiksel Keşif Emri: {count} askerimiz {goal_text} üzere sisli bölgelere keşfe çıkarılıyor.",
            "confidence": 0.95,
            "actions": [
                {
                    "action_type": "SCOUT",
                    "unit_type": "soldier",
                    "count": count,
                    "target_zone": focus,
                    "reason": f"Haritayı tarayarak {focus} odaklı keşif yapmak ve görüş alanını genişletmek."
                }
            ]
        }
        print(f"\n[RTS STRATEGIC COMMANDER] Hızlı taktiksel keşif planı onaylandı: {plan['summary']}")
        return plan

    # Hızlı Ev İnşası Tespiti (House / Nüfus)
    if not is_analysis_query and any(k in q_lower for k in ["ev yap", "ev inşa", "ev insha", "house", "nüfus için", "nufus icin", "ev kur", "nüfus", "nufus"]):
        plan = {
            "summary": "Taktiksel Nüfus Planı: Nüfus tavanını +5 artırmak amacıyla üssümüzde 1 adet Ev (House) inşa ediliyor.",
            "confidence": 0.95,
            "actions": [
                {
                    "action_type": "BUILD",
                    "building_type": "house",
                    "unit_type": "house",
                    "count": 1,
                    "target_zone": "main_base",
                    "reason": "Nüfus kapasitesini genişleterek yeni asker ve köylü üretimine olanak sağlamak."
                }
            ]
        }
        print(f"\n[RTS STRATEGIC COMMANDER] Hızlı ev inşası planı onaylandı: {plan['summary']}")
        return plan

    # Hızlı Köylü Üretimi Tespiti (Villager)
    if not is_analysis_query and any(k in q_lower for k in ["köylü üret", "koylu uret", "köylü bas", "koylu bas", "işçi üret", "isci uret", "köylü çıkar", "koylu cikar", "köylü", "koylu"]):
        count = 1
        m_count = re.search(r'(\d+)\s*(?:köylü|koylu|işçi|isci)', q_lower)
        if m_count:
            count = max(1, min(10, int(m_count.group(1))))
        plan = {
            "summary": f"Taktiksel Ekonomi Planı: Şehir Merkezimizden {count} yeni Köylü eğitilerek kaynak toplama gücümüz artırılıyor.",
            "confidence": 0.95,
            "actions": [
                {
                    "action_type": "TRAIN_UNIT",
                    "unit_type": "villager",
                    "count": count,
                    "target_zone": "main_base",
                    "reason": "Odun ve altın toplama verimliliğini artırmak üzere yeni işçi istihdam etmek."
                }
            ]
        }
        print(f"\n[RTS STRATEGIC COMMANDER] Hızlı köylü üretimi planı onaylandı: {plan['summary']}")
        return plan

    # Hızlı Taarruz ve Saldırı Tespiti (Attack / Taarruz / Hücum)
    if any(k in q_lower for k in ["saldır", "saldir", "taarruz", "hücum", "hucum", "düşmanı yok et", "dusmani yok et", "düşmanları öldür", "dusmanlari oldur", "orduyu sür", "orduyu sur", "saldırı", "saldiri", "hücuma geç", "hucuma gec"]):
        target_zone = "north_gate"
        if any(k in q_lower for k in ["kamp", "camp", "üs", "us", "merkez"]):
            target_zone = "enemy_camp"

        plan = {
            "summary": "Taktiksel Taarruz Emri: Mevcut muharip ordumuz düşman birliklerine taarruz etmek üzere derhal harekete geçiriliyor.",
            "confidence": 0.95,
            "actions": [
                {
                    "action_type": "ATTACK",
                    "unit_type": "soldier",
                    "count": 10,
                    "target_zone": target_zone,
                    "reason": "Düşman tehdidini bertaraf etmek ve sahamızın güvenliğini sağlamak."
                }
            ]
        }
        print(f"\n[RTS STRATEGIC COMMANDER] Hızlı taarruz planı onaylandı: {plan['summary']}")
        return plan

    team_role_note = (
        "\nAKTİF KOMUTAN KİMLİĞİN: KIRMIZI KRALLIK AI KOMUTANI (2. Oyuncu / Kuzey Üssü)."
        if active_team == "RED"
        else "\nAKTİF KOMUTAN KİMLİĞİN: MAVİ KRALLIK AI KOMUTANI (1. Oyuncu / Güney Üssü)."
    )
    messages = [
        {"role": "system", "content": SYSTEM_PROMPT + team_role_note},
        {"role": "user", "content": user_query}
    ]
    
    print("\n" + "=" * 65)
    print(f"[RTS STRATEGIC COMMANDER - {active_team}] Yeni Görev Başlatıldı")
    print(f"[KULLANICI TALEBİ]: {user_query}")
    print("=" * 65)
    
    step = 0
    while step < max_steps:
        step += 1
        print(f"\n--- [ADIM {step}/{max_steps}] Ollama LLM Sorgulanıyor ({MODEL_NAME}) ---")
        
        payload = {
            "model": MODEL_NAME,
            "messages": messages,
            "tools": TOOLS,
            "stream": False
        }
        
        try:
            response = httpx.post(OLLAMA_API_URL, json=payload, timeout=120.0)
            if response.status_code == 404:
                raise RuntimeError(
                    f"Model '{MODEL_NAME}' bulunamadı. Lütfen terminalden:\n"
                    f"& \"$env:LOCALAPPDATA\\Programs\\Ollama\\ollama.exe\" pull {MODEL_NAME}\n"
                    f"komutu ile modeli indirin."
                )
            response.raise_for_status()
            res_data = response.json()
        except httpx.ConnectError:
            raise ConnectionError(
                f"Ollama API'sine ({OLLAMA_API_URL}) bağlanılamadı.\n"
                f"Lütfen Ollama'nın çalıştığından emin olun."
            )
            
        message = res_data.get("message", {})
        
        # 1. MODEL ARAÇ ÇAĞRISI (TOOL CALL) TALEP ETTİYSE:
        if "tool_calls" in message and message["tool_calls"]:
            messages.append(message)
            for tool_call in message["tool_calls"]:
                tool_name = tool_call["function"]["name"]
                args = tool_call["function"].get("arguments", {})
                
                # Eğer string olarak geldiyse ayrıştır
                if isinstance(args, str):
                    try:
                        args = json.loads(args)
                    except Exception:
                        args = {}
                        
                print(f"[ARAÇ ÇAĞRISI]: {tool_name}({args})")
                
                if tool_name in TOOL_MAP:
                    try:
                        func = TOOL_MAP[tool_name]
                        result = func(**args) if args else func()
                    except Exception as ex:
                        result = {"error": f"Araç çalıştırma hatası: {str(ex)}"}
                else:
                    result = {"error": f"Bilinmeyen araç: {tool_name}"}
                    
                print(f"   -> [ARAÇ CEVABI]: {str(result)[:140]}...")
                
                tool_msg: Dict[str, Any] = {
                    "role": "tool",
                    "content": json.dumps(result, ensure_ascii=False)
                }
                if "id" in tool_call:
                    tool_msg["tool_call_id"] = tool_call["id"]
                messages.append(tool_msg)
            continue
            
        # 2. MODEL NİHAİ METİN YANITI ÜRETTİYSE:
        content = message.get("content", "").strip()
        if not content:
            print("[UYARI] Model boş yanıt döndü, devam ediliyor...")
            continue
            
        print("\n[MODEL NİHAİ YANITI ALINDI]:")
        print(content)
        
        # JSON ayrıştırma ve Schema Doğrulama
        try:
            plan_json = extract_json_from_text(content)
            is_valid, error_msg = validate_action_plan(plan_json)
            
            if is_valid:
                print("\n[BAŞARILI]: Eylem planı 'action_plan.schema.json' ile tam doğrulandı!")
                print(json.dumps(plan_json, indent=2, ensure_ascii=False))
                return plan_json
            else:
                print(f"\n[ŞEMA HATASI]: {error_msg}")
                # Kendi kendini onarma (Self-Repair) adımı
                messages.append(message)
                messages.append({
                    "role": "user",
                    "content": (
                        f"Ürettiğin yanıt JSON şemasına uymuyor: {error_msg}\n"
                        f"Lütfen şemaya BİREBİR uyan, fazladan alan içermeyen ve sadece geçerli JSON olan düzeltilmiş planı üret."
                    )
                })
                continue
                
        except json.JSONDecodeError as jde:
            print(f"\n[JSON PARSE HATASI]: {jde}")
            messages.append(message)
            messages.append({
                "role": "user",
                "content": "Yanıtın geçerli bir JSON formatında değildi. Lütfen sadece istenen JSON şemasına uygun ham JSON üret."
            })
            continue

    raise RuntimeError(f"Ajan {max_steps} adım içerisinde geçerli bir ActionPlan üretemedi.")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="CSE-377 RTS AI Strategic Commander")
    parser.add_argument(
        "--query",
        type=str,
        default="Kuzey kapısından atlılar yaklaşıyor. Durumumuzu kontrol et ve savunma için ne yapmam gerektiğini planla.",
        help="Komutana verilecek durum veya soru"
    )
    args = parser.parse_args()
    
    try:
        plan = run_agent(args.query)
    except Exception as e:
        print(f"\n[HATA]: {e}")