using UnityEngine;

namespace CSE377.Gameplay
{
    /// <summary>
    /// Nehir suyunun batıdan doğuya doğal akışını, su yüzeyi parıltısını
    /// ve kıyı şeritlerindeki hafif dalgalanmayı simüle eden prosedürel su akıntı motoru.
    /// </summary>
    public class RTSRiverWaterFlow : MonoBehaviour
    {
        [Header("Akıntı Ayarları")]
        public float flowSpeedX = 0.045f;
        public float waveFrequency = 1.2f;
        public float waveAmplitude = 0.003f; // Z-fighting (yeşil zemin çakışması) olmaması için mikro genlik

        private Material waterMat;
        private Vector3 initialPosition;

        private void Start()
        {
            var ren = GetComponent<Renderer>();
            if (ren != null)
            {
                waterMat = ren.material;
            }
            initialPosition = transform.position;
        }

        private void Update()
        {
            if (waterMat != null)
            {
                // 1. Akıntı doku kaydırması (UV akışı)
                float offsetX = (Time.time * flowSpeedX) % 1.0f;
                float offsetZ = Mathf.Sin(Time.time * 0.8f) * 0.02f;
                waterMat.mainTextureOffset = new Vector2(offsetX, offsetZ);

                // 2. Güneş ışığında su yüzeyi parıltısı (Specular / Metallic nefes alışı)
                float shimmer = 0.85f + Mathf.Sin(Time.time * 2.0f) * 0.08f;
                waterMat.SetFloat("_Glossiness", Mathf.Clamp01(shimmer));
            }

            // 3. Su yüzeyini Y ekseninde sabit tut (alttaki yeşil çimen zeminiyle Z-fighting / yeşil çizgi oluşmaması için)
            transform.position = initialPosition;
        }
    }
}
