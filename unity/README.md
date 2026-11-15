# CSE-377 Unity 6 RTS Client Kurulum Kılavuzu

Bu dizin, CSE-377 Agentic Multiplayer RTS projesinin Unity tarafındaki C# kaynak kodlarını ve arayüz yönergelerini içerir.

---

## 📂 Dosya Yapısı

Tüm scriptler `Assets/Scripts/AI/` dizini altında yer almaktadır:
- `ActionPlanData.cs`: `action_plan.schema.json` formatını C# nesnelerine dönüştüren ve serialize eden model.
- `AiSafeSnapshot.cs`: Fog-of-War filtrelenmiş güvenli oyun durumu snapshot modeli.
- `AiGatewayClient.cs`: Python AI Gateway sunucusuna (`http://localhost:5000/api/plan`) HTTP POST isteği gönderen köprü.
- `ApprovalPanelUI.cs`: Yapay zekanın sunduğu planı ekrana basan, [Onayla] ve [Reddet] butonlarını yöneten Human-in-the-Loop arayüz yöneticisi.
- `GameActionValidator.cs`: Onaylanan eylemlerin yerel kurallara ve bütçeye uygunluğunu test eden kural motoru.

---

## 🛠️ Unity Hub & Sahne Kurulum Adımları

1. **Unity Hub ile Projeyi Açın:**
   - Proje konumu olarak bu dizini (`c:\Users\Seyhan\CSE377-RTS\unity`) seçebilir veya Unity Hub'da oluşturduğunuz projenin `Assets/Scripts/` klasörüne bu dosyaları kopyalayabilirsiniz.

2. **Hiyerarşide (Hierarchy) Nesneleri Oluşturun:**
   - Boş bir GameObject oluşturun, adını `[AI_Manager]` yapın.
   - Bu nesneye aşağıdaki bileşenleri (Components) ekleyin:
     - `AiGatewayClient`
     - `GameActionValidator`
     - `ApprovalPanelUI`

3. **UI Canvas Kurulumu:**
   - Bir `Canvas` ve içine `ApprovalPanel` adında bir `Panel` (UI Image) ekleyin.
   - Panel içerisine şunları yerleştirin:
     - `SummaryText` (Text): Yapay zekanın plan özeti.
     - `ConfidenceText` (Text): Güven skoru (%85 vb.).
     - `ValidationStatusText` (Text): Durum ve kural hata mesajları.
     - `ActionsContainer` (Vertical Layout Group): Eylemlerin listeleneceği alan.
     - `ApproveButton` (Button): "Planı Onayla".
     - `RejectButton` (Button): "Planı Reddet".

4. **Inspector Bağlantılarını Yapın:**
   - `[AI_Manager]` üzerindeki `ApprovalPanelUI` bileşenine yukarıda oluşturduğunuz UI elemanlarını sürükleyip bırakın.
   - `AiGatewayClient` alanına `[AI_Manager]`'ı atayın.

5. **Test Etme:**
   - **Python AI Gateway'i Başlatın:**
     ```powershell
     & "ai\mcp-server\.venv\Scripts\python.exe" "ai\agent\gateway_server.py"
     ```
   - Unity'de Play tuşuna basın.
   - `AiGatewayClient.RequestPlan("Kuzey kapısına süvariler yaklaşıyor, ne yapmalıyım?")` fonksiyonunu tetiklediğinizde ekranda onay paneli açılacaktır!
