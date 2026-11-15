# CSE-377 Agentic Multiplayer RTS Project

> Unity 6, Photon (Fusion/PUN), Model Context Protocol (MCP), LLM Agent ve RAG destekli stratejik komutan mimarisi.

---

## 📌 Proje Genel Bakışı
Bu proje, klasik *Age of Empires* dinamiklerini modern yapay zekâ (Ajan tabanlı LLM, MCP, RAG) mimarisiyle birleştiren iki oyunculu bir Gerçek Zamanlı Strateji (RTS) oyunudur. 

Yapay zekâ (AI Strategic Commander), oyun belleğini doğrudan manipüle etmek yerine Model Context Protocol (MCP) üzerinden oyun durumunu güvenli araçlarla (tools) inceler, oyun kurallarını RAG üzerinden çeker ve oyuncuya yapılandırılmış eylem planları (`ActionPlan`) sunar. Planlar yalnızca insan onayı (Human-in-the-Loop) ve yerel kural doğrulamasının (`GameActionValidator`) ardından ağ katmanında çalıştırılır.

---

## 🏗️ Sistem Mimarisi ve Veri Akışı

```text
[ Oyuncu / UI ] 
      │ 
      ▼ (Strateji Talebi / Soru)
[ Unity 6 Client ] 
      │ 
      ▼ (Fog-of-War Filtreli Güvenli Snapshot - AiSafeSnapshot)
[ AI Gateway / Agent Runner (Python) ] ◄════► [ LLM (GPT-4o-mini / Claude 3.5) ]
      │                                                │
      │ (MCP Protokolü üzerinden dinamik araç çağrısı)│ (Gerektiğinde RAG sorgusu)
      ▼                                                ▼
[ Custom RTS MCP Server ]                     [ RAG Bilgi Bankası ]
  ├─ get_player_resources                      └─ units.json, rules.md
  ├─ get_army_status
  ├─ get_visible_enemies
  ├─ get_technology_status
  └─ search_game_knowledge
      │
      ▼ (JSON Schema Uyumlu ActionPlan)
[ Unity Approval Panel (Human-in-the-Loop) ]
      │
      ▼ (Onaylandıysa)
[ GameActionValidator.cs (Kaynak & Yetki Kontrolü) ]
      │
      ▼ (Geçerliyse)
[ Photon Network Authority (Birim Üretimi / Emir İletimi) ]