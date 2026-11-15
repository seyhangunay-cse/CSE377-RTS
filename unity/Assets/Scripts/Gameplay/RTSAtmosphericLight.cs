using System.Collections.Generic;
using UnityEngine;

namespace CSE377.Gameplay
{
    /// <summary>
    /// Orta çağ binalarına ve karargahlara sıcak gece/alacakaranlık meşaleleri,
    /// demir fenerler ve hafif şömine dumanı ekleyerek haritanın atmosferik derinliğini artıran sistem.
    /// Age of Empires tarzı yaşayan, nefes alan bir şehir dokusu oluşturur.
    /// </summary>
    public class RTSAtmosphericLight : MonoBehaviour
    {
        [Header("Işık Ayarları")]
        public Light lightSource;
        public float baseIntensity = 1.3f;
        public float flickerRange = 0.25f;
        public float flickerSpeed = 5.5f;

        private float randomOffset;

        private void Awake()
        {
            randomOffset = Random.Range(0f, 100f);
            if (lightSource == null)
            {
                lightSource = GetComponentInChildren<Light>();
            }
        }

        private void Update()
        {
            if (lightSource == null || !lightSource.enabled) return;

            // Organik ateş pırıltısı (Perlin noise)
            float noise = Mathf.PerlinNoise((Time.time + randomOffset) * flickerSpeed, randomOffset);
            lightSource.intensity = baseIntensity + (noise - 0.5f) * flickerRange;
        }

        /// <summary>
        /// Belirtilen binaya veya konuma orta çağ tarzı demir aplikli sıcak fener/meşale ekler.
        /// </summary>
        public static GameObject CreateTorch(Transform parent, Vector3 localPos, Color lightColor, float range = 7.5f, float intensity = 1.4f)
        {
            var torchGo = new GameObject("Atmospheric_Torch");
            torchGo.transform.SetParent(parent, false);
            torchGo.transform.localPosition = localPos;

            // 1. Demir Aplik Braketi (Dark Iron Bracket)
            var bracket = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(bracket.GetComponent<Collider>());
            bracket.transform.SetParent(torchGo.transform, false);
            bracket.transform.localPosition = new Vector3(0, -0.15f, 0);
            bracket.transform.localScale = new Vector3(0.12f, 0.35f, 0.12f);
            var bRen = bracket.GetComponent<Renderer>();
            if (bRen != null)
            {
                var ironMat = new Material(Shader.Find("Standard"));
                ironMat.color = new Color(0.18f, 0.18f, 0.20f);
                ironMat.SetFloat("_Metallic", 0.85f);
                ironMat.SetFloat("_Glossiness", 0.4f);
                bRen.material = ironMat;
            }

            // 2. Alev / Kor Parıltısı (Glowing Flame Core)
            var flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(flame.GetComponent<Collider>());
            flame.transform.SetParent(torchGo.transform, false);
            flame.transform.localPosition = new Vector3(0, 0.12f, 0);
            flame.transform.localScale = new Vector3(0.22f, 0.32f, 0.22f);
            var fRen = flame.GetComponent<Renderer>();
            if (fRen != null)
            {
                var flameMat = new Material(Shader.Find("Standard"));
                flameMat.color = lightColor;
                flameMat.EnableKeyword("_EMISSION");
                flameMat.SetColor("_EmissionColor", lightColor * 2.2f);
                fRen.material = flameMat;
            }

            // 3. Sıcak Gerçek Zamanlı Nokta Işık (Point Light)
            var lGo = new GameObject("Torch_PointLight");
            lGo.transform.SetParent(torchGo.transform, false);
            lGo.transform.localPosition = new Vector3(0, 0.15f, 0);
            var pLight = lGo.AddComponent<Light>();
            pLight.type = LightType.Point;
            pLight.color = lightColor;
            pLight.range = range;
            pLight.intensity = intensity;
            pLight.shadows = LightShadows.None; // Performans dostu

            var flicker = torchGo.AddComponent<RTSAtmosphericLight>();
            flicker.lightSource = pLight;
            flicker.baseIntensity = intensity;

            return torchGo;
        }

        /// <summary>
        /// Sağlıklı Şehir Merkezi ve Kalelerin çatısında hafif, huzurlu şömine dumanı üretir.
        /// </summary>
        public static GameObject AttachHearthChimneySmoke(Transform parent, Vector3 localPos)
        {
            // Opaque beyaz küre duman simülasyonu görsel hataları engellemek için kaldırıldı
            return null;
        }
    }

    /// <summary>
    /// Şömine duman simülasyonu (görsel stabilite için pasifize edilmiştir).
    /// </summary>
    public class RTSChimneySmokeSim : MonoBehaviour
    {
    }
}
