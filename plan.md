# 🌐 CSE377-RTS: PHOTON 2 KİŞİLİK MULTIPLAYER & ÇİFT AI KOMUTAN MİMARİ PLANI (PLAN.MD)

> **Odak:** Photon (PUN 2 / Realtime) üzerinden 2 gerçek oyuncunun (Mavi Krallık vs. Kırmızı Krallık) karşılıklı oynayabilmesi ve **her iki oyuncunun da kendi bağımsız Yapay Zekâ Komutanına** danışıp emir onaylayabilmesi.

---

## 🎯 1. TEMEL VİZYON VE MİMARİ HEDEFLER

1. **Simetrik 2 Kişilik Oyun (`LocalPlayerTeam`):**
   - **1. Oyuncu (Host / MasterClient):** `UnitTeam.Player` (Mavi Krallık - Mavi Bayraklar & Mavi Bina Kubbeleri/Çatıları). Güney / Güney-Batı üssünde başlar.
   - **2. Oyuncu (Client / Guest):** `UnitTeam.Enemy` (Kırmızı Krallık - Kırmızı Bayraklar & Kırmızı Bina Kubbeleri/Çatıları). Kuzey / Kuzey-Doğu üssünde başlar.
   - Her oyuncu yalnızca kendi takımının birimlerini ve binalarını seçebilir, kendi üssünden üretim yapabilir ve yalnızca kendi birimlerinin açtığı **Savaş Sisini (Fog of War)** görebilir.

2. **Her İki Oyuncu İçin Bağımsız AI Komutanı (Dual AI Commanders):**
   - Her iki oyuncunun ekranında kendi **AI Strateji Danışmanı & Emir Onay Paneli (`ApprovalPanelUI`)** çalışır.
   - Her oyuncunun AI Komutanına gönderilen `AiSafeSnapshot` verisi **Savaş Sisi (Fog-of-War) Korumalıdır**: AI Komutan yalnızca o oyuncunun aktif görüş alanında (`FogCellState.Visible`) olan rakip birimleri görebilir; karanlık (`Unexplored`) veya görüş dışı (`Explored`) bölgelerdeki düşman hareketlerini sızdırmaz.
   - Python AI Gateway (`gateway_server.py`) ve MCP Sunucusu (`server.py`) `TEAM_SAFE_STATES = {"BLUE": ..., "RED": ...}` ile iki oyuncunun stratejik hafızasını birbirinden tamamen izole tutar.

3. **Photon Ağ Senkronizasyonu (`RTSNetworkSyncManager`):**
   - Birim üretimi (`RPC_SpawnUnit`), bina inşası (`RPC_SpawnBuilding`), hareket (`RPC_IssueMoveOrder`), saldırı (`RPC_IssueAttackOrder`), köylü görevleri (`RPC_IssueHarvestOrBuildOrder`) ve onaylanan AI Komutan planları (`RPC_ExecuteAIPlan`) Photon RPC katmanı üzerinden her iki oyuncunun ekranında deterministik olarak senkronize edilir.

---

## 📅 2. UYGULAMA AŞAMALARI

| Faz | Modül / Dosyalar | Yapılacak İşlemler |
| :--- | :--- | :--- |
| **Faz 1** | **Simetrik Takım Perspektifi (`LocalPlayerTeam`)**<br>`RTSGameManager.cs`, `RTSSelectionManager.cs`, `RTSFogOfWarManager.cs`, `RTSFogDiscoverable.cs`, `RTSCameraController.cs` | `LocalPlayerTeam` tanımı; Kırmızı oyuncunun kendi birim/binalarını seçebilmesi, kamerasının Kırmızı Üste başlaması ve Savaş Sisinin her oyuncunun kendi takımına göre hesaplanması. |
| **Faz 2** | **Photon PUN 2 Lobi & RPC Senkronizasyonu**<br>`RTSNetworkSyncManager.cs`, `RTSHUDController.cs`, `RTSUnit.cs`, `RTSBuilding.cs` | Başlangıç ekranına `Tek Oyunculu (PvE)` / `2 Kişilik Online (Photon PvP)` lobi seçimi, Oda ID girişi, birim/bina `networkId` eşlemesi ve hareket/saldırı/üretim RPC senkronizasyonu. |
| **Faz 3** | **Çift AI Komutan & MCP Sunucu İzolasyonu**<br>`AiSafeSnapshot.cs`, `AiGatewayClient.cs`, `gateway_server.py`, `agent_runner.py`, `server.py` | Snapshot içerisine `team_id` (`"BLUE"` / `"RED"`) eklenmesi, Python sunucusunda Mavi ve Kırmızı takım hafızalarının ayrılması ve onaylanan AI planlarının (`RPC_ExecuteAIPlan`) her iki takım için simetrik icrası. |
| **Faz 4** | **PvP Oyun Döngüsü & Test**<br>`RTSGameManager.cs`, `RTSHUDController.cs` | 2 kişilik PvP modunda otomatik yapay bot dalgalarının (`enableEnemyRaids`) kapatılması, Karargah yıkımına dayalı PvP Zafer/Yenilgi ekranı ve çift istemci senkronizasyon testi. |
