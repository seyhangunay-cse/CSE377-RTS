# RTS Oyun Kuralları ve Stratejik Doktrin Bilgi Bankası

Bu doküman, AI Strategic Commander ve insan oyuncu için geçerli temel kuralları, muharebe dinamiklerini ve sistem kısıtlarını tanımlar.

---

### Kural 1: Muharebe Karşıtlık Sistemi (Taş-Kağıt-Makas Üçgeni)
- **Mızrakçı > Süvari:** Mızrakçılar (Spearman), atlı birliklere ve süvarilere karşı devasa bonus hasar (+18) verir. Düşman atlı saldırısı tespit edildiğinde ana savunma hattı kesinlikle mızrakçılardan kurulmalıdır.
- **Süvari > Okçu:** Süvariler (Cavalry / Knight), yüksek hızları ve can havuzları sayesinde menzilli okçuları hızla yakalar ve etkisiz hale getirir.
- **Okçu > Mızrakçı:** Okçular (Archer), mızrakçıların düşük zırhı ve yavaş hareketinden faydalanarak onları uzaktan vurup eritir.
- **Kılıçlı Piyade:** Dengeli zırh ve saldırıya sahiptir; mızrakçıları yener ancak menzilli okçu yaylım ateşine dikkat etmelidir.
- **Kuşatma Araçları (Mancınık/Trebuchet):** Binalara ve kapılara karşı yıkıcıdır, ancak süvarilere karşı tamamen savunmasızdır; etrafında daima koruma birliği bulunmalıdır.

---

### Kural 2: Savaş Sisi (Fog-of-War) ve Görüş Alanı Disiplini
- Harita sisi altındaki düşman birimlerine ve binalara doğrudan hedefli saldırı emri verilemez.
- Yapay zekâ strateji komutanı yalnızca `get_visible_enemies` aracı ile taranan güvenli snapshot verisindeki düşmanları hedef alabilir.
- Görünmeyen bir bölgeye hareket edilmek isteniyorsa `MOVE_GROUP` eylemi keşif amacıyla dar geçitlere veya güvenli görüş sınırlarına doğru yönlendirilmelidir.

---

### Kural 3: Savunma Hattı ve Kapı Güvenliği (Choke Point Savunması)
- Kuzey Kapısı (`north_gate`), Doğu Kapısı veya üs girişleri kritik savunma boğazlarıdır (choke point).
- Bir kapıya yönelik düşman saldırısı (`UNDER_ATTACK`) algılandığında:
  1. Kapı önünde derhal düşman sınıfına uygun karşıt birim (örneğin süvariye karşı mızrakçı) konuşlandırılmalıdır.
  2. Birimler `MOVE_GROUP` emriyle ilgili geçit alanına kaydırılmalıdır.
  3. Kuleler veya okçular mızrakçıların arkasında güvenli ateş pozisyonuna alınmalıdır.

---

### Kural 4: Kaynak Yönetimi ve Emir Karşılanabilirlik İlkesi
- Oyuncunun üç temel kaynağı vardır: Odun (wood), Yiyecek (food), Altın (gold).
- Yapay zekâ komutanı, herhangi bir birim eğitimi (`TRAIN_UNIT`) veya teknoloji araştırması (`RESEARCH`) önermeden önce mutlaka `estimate_plan_cost` ve `get_player_resources` araçlarıyla kaynak yeterliliğini denetlemelidir.
- Oyuncunun bütçesini aşan (`affordable: false`) bir eylem planı sunulmamalı, gerekirse birim sayısı düşürülmeli veya elde olan mevcut birliklerin pozisyonu değiştirilmelidir.

---

### Kural 5: Teknoloji Ağacı ve Çağ Yükseltme Koşulları
- Üst seviye birimlerin ve teknolojilerin kilidi çağ seviyesine bağlıdır (`Age_1`, `Age_2`, `Age_3`).
- Demir Silahlar, Çelik Ok Uçları ve Pul Zırh için Demirci (`blacksmith`) binası ve `Age_2` gereklidir.
- Bir teknoloji zaten araştırılmışsa (`get_technology_status` listesinde mevcutsa), mükerrer araştırma emri verilmemelidir.

---

### Kural 6: İnsan Onayı (Human-in-the-Loop) ve Ağ Otoritesi Doğrulaması
- Yapay zekanın ürettiği tüm eylem planları (`ActionPlan`) Unity istemcisindeki onay panelinde (Approval Panel) insan oyuncuya sunulur.
- Oyuncu onay vermediği sürece hiçbir birim üretilmez ve hiçbir emir Photon ağına gönderilmez.
- Onaylanan planlar yerel `GameActionValidator` kural denetleyicisinden geçtikten sonra ağ katmanına iletilir.

---

### Kural 7: Nüfus Sınırı ve Ev İnşası (Population Cap)
- Başlangıçta Şehir Merkezi 10 birimlik nüfus sağlar.
- Yeni asker veya köylü üretilebilmesi için mevcut nüfusun azami nüfustan az olması şarttır.
- Nüfus sınırına yaklaşıldığında veya ulaşıldığında köylüler ile Ev (`house`) inşa edilmelidir (Maliyet: 30 Odun, her ev +5 nüfus kapasitesi sağlar, azami 50).
- Nüfus dolu olduğunda (`population == max_population`) birim üretilemez; öncelikle `BUILD` eylemi ile `house` inşa edilmelidir.

---

### Kural 8: Ekonomi Dengesi, İşçi Odağı (ASSIGN_GATHER) ve Sis İçi Rakip İstihbaratı
- `get_worker_and_economy_report` aracı mevcut Odun/Gıda/Altın stoklarını, köylü dağılımını (`food`, `wood`, `gold`, `idle`) ve odaklanılması gereken öncelikli kaynağı döner.
- Boşta (`idle > 0`) köylü varsa veya kritik bir kaynakta işçi yoksa (`w_food == 0`, `w_wood == 0` veya `w_gold == 0`), `ASSIGN_GATHER` eylemi ile (`unit_type: "villager"`, `target_zone: "food" | "wood" | "gold"`) köylüler o kaynağa yönlendirilmelidir.
- `get_fog_intelligence` aracı savaş sisi dışında keşfedilmiş düşman binalarını (`discovered_enemy_buildings`) ve son görülen düşman izlerini (`last_known_enemies_in_fog`) verir; düşman Okçu Poligonu kurduysa Süvari, Kışla/Demirci ağırlıklıysa Okçu, Kule/Kale diktiyse Mancınık önerilir; keşif %35 altındaysa `SCOUT` çıkarılmalıdır.
