# 🏰 IMPERIUM: MEDIEVAL AGENTS - KAPSAMLI GELİŞTİRME VE MASTER PLANI (PLAN.MD)

> **Tarih:** 26 Eylül 2026  
> **Odak:** Görsel Geliştirme (Visual Polish), Çeşitlilik & Süreklilik (Replayability), 4-5 Farklı Harita Sistemi ve Başlangıçta Harita Seçimi.

---

## 🎯 1. STRATEJİK YÖN DEĞİŞİMİ VE KULLANICI VİZYONU

Detaylı UI istatistikleri ve mikro ayarlar yerine, oyunu **gerçekten bir üst seviyeye taşıyacak, görsel olarak etkileyici ve her oyunda farklı bir deneyim sunacak** temel unsurlara odaklanıyoruz:

1. **4-5 Farklı Harita Tasarımı:**
   * Nehirsiz açık meydanlar, yoğun orman boğazları, köşelerde çapraz üsler ve nehir geçitleri.
2. **Başlangıç Ekranında Harita Seçimi (Map Selector):**
   * Oyuncu maça başlamadan önce istediği harita türünü seçebilecek veya `🎲 Rastgele Harita` ile her maçta sürpriz bir sahaya adım atacak.
3. **Görsel Devrim (Visual Polish & Atmosphere):**
   * Tekdüze yeşil zemin yerine harita türüne özel zemin renkleri, üs meydanları (cobblestone aprons), yol patikaları, zenginleştirilmiş flora, kayalıklar ve dinamik atmosferik ışıklandırma/sis.
4. **Haritaya Uyumlu Düşman Akınları & Taktiksel Oynanış (Replayability):**
   * Düşman akınları haritanın coğrafyasına göre adapte olacak (örneğin bozkırda geniş kanat süvari baskını, kara ormanda dar geçitlerden koçbaşı/mancınık baskını, çapraz haritada uzun vadi hücumu).

---

## 🗺️ 2. HARİTA HAVUZU VE COĞRAFİ ÖZELLİKLER (5 FARKLI HARİTA)

```
===================================================================================================
HARİTA 1: 🌊 NEHİR VADİSİ (RIVER VALLEY)
---------------------------------------------------------------------------------------------------
Düzen:       Kuzey-Güney ayrımı. Ortada Z=0 ekseninde 11m genişliğinde derin su nehri.
Geçitler:    Batı Taş Köprüsü (X=-28), Doğu Taş Köprüsü (X=+28), Merkez Taş Sığlığı (X=0).
Üsler:       Oyuncu Güneyde (0, -52), Düşman Kuzeyde (0, +55).
Görsel:      Zengin vadi yeşili zemin, berrak mavi su akıntısı, sazlıklar, köprü meşaleleri.
Oynanış:     Köprü tahkimatı, boğaz tutma, nehir kıyısı savunması.

===================================================================================================
HARİTA 2: 🏜️ AÇIK BOZKIR (OPEN STEPPE / ARABIA)
---------------------------------------------------------------------------------------------------
Düzen:       ORTADA NEHİR YOK! 160x160m tamamen açık, ferah ova ve hafif tepecikler.
Geçitler:    Sınırsız serbest hareket. Nehir engelleyicisi ve köprü bulunmaz.
Üsler:       Oyuncu Güneyde (0, -50), Düşman Kuzeyde (0, +50).
Görsel:      Sıcak altın-sarı çayır tonu, açık güneşli gökyüzü, dağınık ağaç koruları, tepe kayalıkları.
Oynanış:     Erken keşif, hızlı süvari akınları, her yönden gelebilecek kuşatmalar, geniş meydan muharebeleri.

===================================================================================================
HARİTA 3: 🌲 KARA ORMAN (BLACK FOREST)
---------------------------------------------------------------------------------------------------
Düzen:       Haritanın %70'i geçilmez devasa sık ormanlarla kaplıdır.
Geçitler:    Üsler arasında yalnızca 2 dar boğaz (Choke Points: Batı Geçidi X=-22, Doğu Geçidi X=+22).
Üsler:       Oyuncu Güney orman açıklığında (0, -50), Düşman Kuzey kalesinde (0, +50).
Görsel:      Koyu yosunlu zemin, gizemli orman sisi (Atmospheric Fog), devasa çam ve meşe ağaçları.
Oynanış:     Dar boğazlara sur çekme (Walling), kulelerle geçit savunması, kuşatma silahlarıyla yarma harekâtı.

===================================================================================================
HARİTA 4: ⛰️ ÇAPRAZ YAYLA (DIAGONAL HIGHLANDS / CROSS CORNERS)
---------------------------------------------------------------------------------------------------
Düzen:       ÜSLER ÇAPRAZ KÖŞELERDE!
Geçitler:    Merkezde zengin bir antik vadi/harabe veya yüksek yayla bulunur; intikal hatları uzundur.
Üsler:       Oyuncu Güney-Batı köşesinde (-48, -48), Düşman Kuzey-Doğu kalesinde (+48, +48).
Görsel:      Yüksek dağ çayırı rengi, merkezde antik taş harabeler ve dev dikilitaşlar, berrak dağ atmosferi.
Oynanış:     Merkezdeki zengin altın ve maden yataklarının kontrolü için kıyasıya mücadele, uzun lojistik hatlar.

===================================================================================================
HARİTA 5: 🎲 RASTGELE HARİTA (RANDOM MAP GENERATOR)
---------------------------------------------------------------------------------------------------
Düzen:       Yukarıdaki 4 harita türünden birini rastgele seçer ve ağaç/maden konumlarını tohumla çeşitlendirir.
Oynanış:     Her yeni oyunda oyuncuyu sürpriz bir taktiksel durum ve harita düzeni karşılar.
===================================================================================================
```

---

## 🎨 3. GÖRSEL İYİLEŞTİRMELER (VISUAL & ATMOSPHERIC POLISH)

### 3.1. Zemin ve Doku Zenginliği
* **Tekdüze Yeşilin Kaldırılması:** Düz yeşil plane yerine harita türüne özel ana zemin materyali (Bozkır için sıcak altın çimen, Kara Orman için koyu orman yosunu, Nehir için canlı zümrüt yeşili).
* **Üs Taş Zeminleri (Cobblestone Base Aprons):** Şehir Merkezi ve ana kışlaların altında gerçekçi taş/kaldırım zemin döşemesi.
* **Toprak Yol Hatları & Patikalar:** Üslerden madenlere ve geçitlere uzanan organik toprak yollar.
* **Maden Yatağı Zenginleştirmesi:** Altın ve taş madenlerinin etrafında taş parçacıkları, döküntüler ve madenci izleri.

### 3.2. Işıklandırma ve Atmosfer
* **Haritaya Özel Gökyüzü ve Sis:**
  * *Nehir Vadisi:* Sabah güneşinin yumuşak nehir parıltısı ve hafif su buharı.
  * *Açık Bozkır:* Yüksek parlaklıkta sıcak altın güneş ışığı, uzun gölgeler, berrak ufuk çizgisi.
  * *Kara Orman:* Mistik yeşilimsi orman sisi (`RenderSettings.fog`), gölgeli ağaç altları.
  * *Çapraz Yayla:* Ferah dağ mavisi atmosfer ve yüksek kontrastlı ışık.

### 3.3. Çevre Unsurları ve Antik Kalıntılar
* Merkez yaylada antik sütunlar, yıkık taş duvarlar (ruins) ve kamp ateşleri.
* Çiçek alanları, çalı kümeleri ve doğal kaya grupları.

---

## 🎮 4. BAŞLANGIÇ EKRANINDA HARİTA SEÇİCİ VE SÜREKLİLİK (GAME LOOP)

### 4.1. Start Screen Harita Seçim Arayüzü
* Başlangıç ekranına (`StartScreen_Modal`) özel bir **Harita Seçim Bölümü** eklenir:
  * Buton: `🗺️ HARİTA: NEHİR VADİSİ` (tıklandığında sırayla değişir).
  * Harita Kartı: Seçilen haritanın ikonu, stratejik açıklaması ve arazi özellikleri ("Nehirsiz açık meydan • Hızlı süvari muharebesi").
  * "Seferi Başlat" dendiğinde seçilen harita dinamik olarak inşa edilir ve kamera doğrudan oyuncunun üssüne odaklanır.

### 4.2. Haritaya Uyumlu Düşman Akın Motoru (Adaptive Raid AI)
* **Nehir Vadisi:** Düşman 1. dalgada Batı Köprüsünden, 2. dalgada Merkez Sığlıktan, 3. dalgada Doğu Köprüsünden taarruz eder.
* **Açık Bozkır:** Düşman akınları doğrudan açık araziden, sol kanattan süvarilerle, merkezden mızrakçılarla hücum eder.
* **Kara Orman:** Düşman yalnızca Batı ve Doğu boğazlarından saldırır; savunma duvarı yapılmışsa mancınıklarla surları döver.
* **Çapraz Yayla:** Düşman Kuzey-Doğu kalesinden hareket edip merkez vadiden geçerek Güney-Batı'daki oyuncu üssünü hedefler.

---

## 📅 5. UYGULAMA PLANI VE SIRADAKİ ADIMLAR

| Aşama | Yapılacak İşlem | Açıklama |
| :--- | :--- | :--- |
| **Aşama 1** | **Harita Tipleri (RTSMapType) & Seçim Modülü** | `RTSMapType` enum'ı, başlangıç menüsüne interaktif harita seçim butonu ve açıklama paneli eklenmesi. |
| **Aşama 2** | **4 Farklı Harita Üretecinin Kodlanması** | `RTSAutoBootstrap.cs` içine Nehir Vadisi, Açık Bozkır, Kara Orman ve Çapraz Yayla harita üreticilerinin entegrasyonu. |
| **Aşama 3** | **Görsel Geliştirmeler (Zeminler, Işık, Sis, Detaylar)** | Üs taş meydanları, haritaya özel zemin renkleri, atmosferik sis/ışıklandırma ve çevre kalıntılarının eklenmesi. |
| **Aşama 4** | **Birim Yönlendirme & Akınların Haritaya Bağlanması** | `RTSUnit.cs` nehir kontrolünün nehirsiz haritalara uyarlanması, `RTSGameManager.cs` akınlarının seçili haritanın üs ve geçitlerine göre otomatik konumlanması. |
| **Aşama 5** | **Test ve Doğrulama** | Haritalar arası geçişlerin, derleme kontrolünün (`verify_build.py`) ve oyun içi akışın test edilmesi. |

---

## 🚀 ŞİMDİ NE YAPIYORUZ?

Kullanıcının talimatı doğrultusunda derhal **Aşama 1 ve Aşama 2**'ye başlıyoruz:
1. `RTSMapType` enum'ını ve Başlangıç Ekranındaki interaktif **Harita Seçim Butonunu** oluşturmak.
2. `RTSAutoBootstrap.cs` içerisine 4 haritayı (Nehir Vadisi, Açık Bozkır, Kara Orman, Çapraz Yayla) ve `Rastgele` seçeneğini inşa edecek görsel arazi ve üs kurucularını entegre etmek.
