using System.Collections.Generic;
using UnityEngine;

namespace CSE377.Gameplay
{
    /// <summary>
    /// Savaş Sisi (Fog-of-War) altındaki statik yapıların (Sur, Kapı, Kule, Harabe)
    /// oyuncu tarafından keşfedilene kadar 3D dünyada gizlenmesini ve keşfedilince açılmasını sağlar.
    /// </summary>
    public class RTSFogDiscoverable : MonoBehaviour
    {
        public static readonly List<RTSFogDiscoverable> AllDiscoverables = new List<RTSFogDiscoverable>();

        private Renderer[] cachedRenderers;
        private Collider[] cachedColliders;
        private Light[] cachedLights;
        public bool isDiscovered { get; private set; } = false;
        public bool isDimmed { get; private set; } = false;
        private bool isInitialized = false;
        private readonly Dictionary<Renderer, Color> baseColors = new Dictionary<Renderer, Color>();

        private void OnEnable()
        {
            if (!AllDiscoverables.Contains(this))
            {
                AllDiscoverables.Add(this);
            }
        }

        private void OnDisable()
        {
            AllDiscoverables.Remove(this);
        }

        private void Awake()
        {
            InitializeComponents();
            // Başlangıçta 3D dünyada kesinlikle gizli başla
            ApplyVisibility(false);
        }

        private void Start()
        {
            // Awake sonrasında eklenen alt nesnelerin (Sancak, Meşale, İskele vb.) de gizlendiğinden emin ol
            ApplyVisibility(isDiscovered);
        }

        private void InitializeComponents()
        {
            isInitialized = true;
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
            cachedColliders = GetComponentsInChildren<Collider>(true);
            cachedLights = GetComponentsInChildren<Light>(true);
        }

        public void SetDiscovered(bool discovered)
        {
            if (!isInitialized) InitializeComponents();

            // Eğer zaten keşfedildiyse ve açık durumdaysa tekrar kapatma (RTS kuralı: keşfedilen surlar/binalar görünür kalır)
            if (isDiscovered && !discovered) return;

            if (isDiscovered != discovered)
            {
                ApplyVisibility(discovered);
            }
        }

        public void ApplyVisibility(bool visible)
        {
            isDiscovered = visible;

            // Sonradan eklenen sancak (RTSBuildingBanner), meşale (RTSAtmosphericLight) veya iskele parçalarını da kapsamak için her zaman güncel listeyi al
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
            cachedColliders = GetComponentsInChildren<Collider>(true);
            cachedLights = GetComponentsInChildren<Light>(true);

            if (cachedRenderers != null)
            {
                for (int i = 0; i < cachedRenderers.Length; i++)
                {
                    if (cachedRenderers[i] != null)
                    {
                        cachedRenderers[i].enabled = visible;
                    }
                }
            }

            if (cachedLights != null)
            {
                for (int i = 0; i < cachedLights.Length; i++)
                {
                    if (cachedLights[i] != null)
                    {
                        cachedLights[i].enabled = visible && !isDimmed;
                    }
                }
            }

            // Not: Fiziksel collider'lar kapatılmaz. Böylece birimler binaların içinden geçemez,
            // menzil hesaplamaları (ClosestPoint) bozulmaz ve tıklama etkileşimleri daima sağlıklı kalır.
        }

        /// <summary>
        /// Keşfedilmiş ancak şu an aktif görüş alanı dışında kalan (Explored) nesneleri
        /// gri/silik (Shroud) tonda gösterir; tekrar aktif görüşe (Visible) girdiğinde canlandırır.
        /// </summary>
        public void SetShroudDimmed(bool dimmed)
        {
            if (!isDiscovered) return;
            if (isDimmed == dimmed && baseColors.Count > 0) return;
            isDimmed = dimmed;

            if (cachedRenderers == null || cachedRenderers.Length == 0)
            {
                cachedRenderers = GetComponentsInChildren<Renderer>(true);
            }

            if (cachedRenderers != null)
            {
                for (int i = 0; i < cachedRenderers.Length; i++)
                {
                    var r = cachedRenderers[i];
                    if (r == null || r.sharedMaterial == null) continue;
                    if (!r.sharedMaterial.HasProperty("_Color")) continue;

                    if (!baseColors.TryGetValue(r, out Color origCol))
                    {
                        origCol = r.material.color;
                        baseColors[r] = origCol;
                    }

                    if (dimmed)
                    {
                        float lum = origCol.r * 0.3f + origCol.g * 0.59f + origCol.b * 0.11f;
                        Color greySilik = new Color(lum * 0.45f, lum * 0.47f, lum * 0.50f, origCol.a);
                        Color targetCol = Color.Lerp(origCol * 0.48f, greySilik, 0.55f);
                        var mats = r.materials;
                        for (int m = 0; m < mats.Length; m++)
                        {
                            if (mats[m] != null && mats[m].HasProperty("_Color")) mats[m].color = targetCol;
                        }
                    }
                    else
                    {
                        var mats = r.materials;
                        for (int m = 0; m < mats.Length; m++)
                        {
                            if (mats[m] != null && mats[m].HasProperty("_Color")) mats[m].color = origCol;
                        }
                    }
                }
            }

            if (cachedLights != null)
            {
                for (int i = 0; i < cachedLights.Length; i++)
                {
                    if (cachedLights[i] != null)
                    {
                        cachedLights[i].enabled = !dimmed;
                    }
                }
            }
        }
    }
}
