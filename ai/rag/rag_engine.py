"""
CSE-377 RAG Bilgi Bankası ve Arama Motoru
Oyun kuralları, birim istatistikleri ve teknolojileri indeksler.
Hafif, saf Python TF-IDF ve Cosine Similarity tabanlı yerel arama yapar.
"""
import os
import json
import re
import math
from typing import List, Dict, Any, Optional

# Türkçe & İngilizce yaygın stop words (etkisiz kelimeler)
STOP_WORDS = {
    "ve", "veya", "ile", "için", "bir", "bu", "şu", "o", "da", "de", "ise", "ki",
    "gibi", "kadar", "olan", "olarak", "en", "daha", "her", "ne", "nasıl", "neden",
    "the", "a", "an", "is", "are", "and", "or", "in", "on", "at", "to", "for", "with"
}

def clean_and_tokenize(text: str) -> List[str]:
    """Metni küçük harfe çevirir, özel karakterleri temizler ve kelimelere ayırır."""
    if not text:
        return []
    # Türkçe karakter duyarlı küçük harfe çevirme
    text = (
        text.replace("İ", "i")
            .replace("I", "ı")
            .lower()
    )
    tokens = re.findall(r"[a-z0-9çğıöşü]+", text)
    return [t for t in tokens if len(t) > 1 and t not in STOP_WORDS]

class RAGEngine:
    def __init__(self, knowledge_dir: Optional[str] = None):
        if knowledge_dir is None:
            base_dir = os.path.dirname(os.path.abspath(__file__))
            knowledge_dir = os.path.join(base_dir, "knowledge")
        self.knowledge_dir = knowledge_dir
        self.documents: List[Dict[str, Any]] = []
        self.doc_vectors: List[Dict[str, float]] = []
        self.idf: Dict[str, float] = {}
        self._build_index()

    def _build_index(self):
        """Knowledge dizinindeki units.json, technologies.json ve rules.md dosyalarını yükleyip indeksler."""
        self.documents = []
        
        # 1. units.json yükle
        units_path = os.path.join(self.knowledge_dir, "units.json")
        if os.path.exists(units_path):
            with open(units_path, "r", encoding="utf-8") as f:
                units = json.load(f)
                for u in units:
                    doc_text = (
                        f"Birim {u.get('name', '')} {u.get('display_name', '')} Kategori: {u.get('category', '')}. "
                        f"Maliyet: Odun {u.get('cost', {}).get('wood', 0)}, Yiyecek {u.get('cost', {}).get('food', 0)}, "
                        f"Altın {u.get('cost', {}).get('gold', 0)}. "
                        f"İstatistikler: HP {u.get('stats', {}).get('hp', 0)}, Saldırı {u.get('stats', {}).get('attack', 0)}, "
                        f"Zırh {u.get('stats', {}).get('defense', 0)}, Hız {u.get('stats', {}).get('speed', 0)}. "
                        f"Güçlü olduğu birimler (Counters): {', '.join(u.get('counters', []))}. "
                        f"Zayıf olduğu birimler (Weak against): {', '.join(u.get('weak_against', []))}. "
                        f"Gereken bina: {u.get('required_building', '')}. "
                        f"Açıklama: {u.get('description', '')}"
                    )
                    self.documents.append({
                        "id": f"unit_{u.get('id', '')}",
                        "title": f"Birim: {u.get('display_name', '')} ({u.get('name', '')})",
                        "category": "units",
                        "content": doc_text,
                        "raw": u
                    })

        # 2. technologies.json yükle
        techs_path = os.path.join(self.knowledge_dir, "technologies.json")
        if os.path.exists(techs_path):
            with open(techs_path, "r", encoding="utf-8") as f:
                techs = json.load(f)
                for t in techs:
                    doc_text = (
                        f"Teknoloji {t.get('name', '')} ID: {t.get('id', '')}. "
                        f"Gereken Çağ: {t.get('age_required', '')}. "
                        f"Maliyet: Odun {t.get('cost', {}).get('wood', 0)}, Yiyecek {t.get('cost', {}).get('food', 0)}, "
                        f"Altın {t.get('cost', {}).get('gold', 0)}. "
                        f"Araştırma Binası: {t.get('research_building', '')}. "
                        f"Etkiler: {json.dumps(t.get('effects', {}), ensure_ascii=False)}. "
                        f"Açıklama: {t.get('description', '')}"
                    )
                    self.documents.append({
                        "id": f"tech_{t.get('id', '')}",
                        "title": f"Teknoloji: {t.get('name', '')}",
                        "category": "technologies",
                        "content": doc_text,
                        "raw": t
                    })

        # 3. rules.md yükle ve kural başlıklarına göre parçala
        rules_path = os.path.join(self.knowledge_dir, "rules.md")
        if os.path.exists(rules_path):
            with open(rules_path, "r", encoding="utf-8") as f:
                md_content = f.read()
                
            # '### Kural' başlıklarına göre ayır
            sections = re.split(r"(?=###\s+Kural\s+\d+:)", md_content)
            for idx, sec in enumerate(sections):
                sec = sec.strip()
                if not sec or not sec.startswith("### Kural"):
                    continue
                # Başlığı ve içeriği ayıkla
                lines = sec.splitlines()
                title_line = lines[0].replace("###", "").strip()
                body = "\n".join(lines[1:]).strip()
                
                self.documents.append({
                    "id": f"rule_{idx + 1}",
                    "title": title_line,
                    "category": "rules",
                    "content": f"{title_line}\n{body}",
                    "raw": {"rule_text": sec}
                })

        # 4. TF-IDF ve Vektör Tablosu Oluştur
        total_docs = len(self.documents)
        if total_docs == 0:
            return

        doc_token_counts: List[Dict[str, int]] = []
        doc_frequencies: Dict[str, int] = {}

        for doc in self.documents:
            tokens = clean_and_tokenize(doc["content"])
            counts: Dict[str, int] = {}
            for t in tokens:
                counts[t] = counts.get(t, 0) + 1
            doc_token_counts.append(counts)
            
            for t in counts:
                doc_frequencies[t] = doc_frequencies.get(t, 0) + 1

        # IDF hesapla
        self.idf = {
            t: math.log((1 + total_docs) / (1 + df)) + 1.0
            for t, df in doc_frequencies.items()
        }

        # Belge TF-IDF vektörleri ve normları
        self.doc_vectors = []
        for counts in doc_token_counts:
            total_tokens = sum(counts.values()) or 1
            vec: Dict[str, float] = {}
            for t, cnt in counts.items():
                tf = cnt / total_tokens
                vec[t] = tf * self.idf.get(t, 1.0)
            self.doc_vectors.append(vec)

    def search(self, query: str, top_k: int = 3) -> List[Dict[str, Any]]:
        """
        Sorgu metniyle eşleşen en alakalı kuralları, birimleri ve teknolojileri döner.
        Cosine similarity ve anahtar kelime eşleşme skoru kullanır.
        """
        if not self.documents:
            return []

        query_tokens = clean_and_tokenize(query)
        if not query_tokens:
            return []

        # Sorgu TF-IDF vektörü
        q_counts: Dict[str, int] = {}
        for t in query_tokens:
            q_counts[t] = q_counts.get(t, 0) + 1

        total_q = sum(q_counts.values())
        q_vec: Dict[str, float] = {}
        for t, cnt in q_counts.items():
            tf = cnt / total_q
            q_vec[t] = tf * self.idf.get(t, 1.0)

        q_norm = math.sqrt(sum(w * w for w in q_vec.values()))
        if q_norm == 0:
            return []

        scored_results = []
        for idx, doc_vec in enumerate(self.doc_vectors):
            doc = self.documents[idx]
            # Vektör iç çarpımı (dot product)
            dot = sum(doc_vec.get(t, 0.0) * q_weight for t, q_weight in q_vec.items())
            doc_norm = math.sqrt(sum(w * w for w in doc_vec.values()))
            
            cosine_sim = (dot / (q_norm * doc_norm)) if (doc_norm > 0) else 0.0
            
            # Başlıkta doğrudan geçme bonusu
            title_lower = doc["title"].lower()
            bonus = 0.0
            for qt in query_tokens:
                if qt in title_lower:
                    bonus += 0.15

            total_score = cosine_sim + bonus
            if total_score > 0.01:
                scored_results.append({
                    "id": doc["id"],
                    "title": doc["title"],
                    "category": doc["category"],
                    "content": doc["content"],
                    "score": round(total_score, 4)
                })

        scored_results.sort(key=lambda x: x["score"], reverse=True)
        return scored_results[:top_k]

# Global singleton motor örneği
_rag_engine_instance: Optional[RAGEngine] = None

def get_rag_engine() -> RAGEngine:
    global _rag_engine_instance
    if _rag_engine_instance is None:
        _rag_engine_instance = RAGEngine()
    return _rag_engine_instance

if __name__ == "__main__":
    engine = get_rag_engine()
    test_queries = [
        "süvari saldırısına karşı ne üretmeliyim?",
        "okçu ve kule menzili geliştirme",
        "kapı savunması kuralları",
        "mancınık kuşatma silahı maliyeti"
    ]
    print(f"Toplam indekslenen doküman sayısı: {len(engine.documents)}")
    print("=" * 60)
    for q in test_queries:
        print(f"\n[SORGU]: {q}")
        hits = engine.search(q, top_k=2)
        for h in hits:
            print(f" -> [{h['category'].upper()}] {h['title']} (Skor: {h['score']})")
            print(f"    Özet: {h['content'][:120]}...")
