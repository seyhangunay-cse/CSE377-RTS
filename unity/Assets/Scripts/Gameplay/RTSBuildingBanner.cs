using UnityEngine;

namespace CSE377.Gameplay
{
    /// <summary>
    /// Age of Empires tarzı binaların (Şehir Merkezi, Şanlı Kale, Kışla, Savunma Kulesi)
    /// çatılarına yerleştirilen ve rüzgarda gerçek zamanlı dalgalanan hanedanlık sancakları (Banners & Pennants).
    /// </summary>
    public class RTSBuildingBanner : MonoBehaviour
    {
        [Header("Sancak Ayarlari")]
        public UnitTeam team = UnitTeam.Player;
        public float poleHeight = 2.4f;
        public float flagWidth = 1.6f;
        public float flagHeight = 0.95f;
        public float waveSpeed = 4.5f;
        public float waveAmplitude = 0.18f;

        private GameObject poleGo;
        private GameObject flagGo;
        private Mesh flagMesh;
        private Vector3[] baseVertices;
        private Vector3[] dynamicVertices;
        private MeshFilter meshFilter;
        private RTSBuilding parentBuilding;

        public static void AttachBanner(RTSBuilding building, Vector3 localOffset, float height = 2.4f, float width = 1.6f)
        {
            if (building == null) return;
            var bannerGo = new GameObject("Building_Banner");
            bannerGo.transform.SetParent(building.transform, false);
            bannerGo.transform.localPosition = localOffset;

            var banner = bannerGo.AddComponent<RTSBuildingBanner>();
            banner.team = building.team;
            banner.poleHeight = height;
            banner.flagWidth = width;
            banner.parentBuilding = building;
            banner.Initialize();
        }

        public void Initialize()
        {
            CreatePole();
            CreateFlagMesh();
        }

        private void CreatePole()
        {
            var standardShader = Shader.Find("Standard") ?? Shader.Find("Diffuse");

            // Ahşap Gönder Direği
            poleGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            DestroyImmediate(poleGo.GetComponent<Collider>());
            poleGo.name = "Banner_Pole";
            poleGo.transform.SetParent(transform, false);
            poleGo.transform.localPosition = new Vector3(0, poleHeight * 0.5f, 0);
            poleGo.transform.localScale = new Vector3(0.08f, poleHeight * 0.5f, 0.08f);

            var poleMat = new Material(standardShader) { color = new Color(0.25f, 0.18f, 0.12f) };
            poleGo.GetComponent<Renderer>().material = poleMat;

            // Tepedeki Altın Topuz (Finial Ball)
            var finial = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(finial.GetComponent<Collider>());
            finial.name = "Finial_Ball";
            finial.transform.SetParent(transform, false);
            finial.transform.localPosition = new Vector3(0, poleHeight + 0.12f, 0);
            finial.transform.localScale = Vector3.one * 0.26f;

            var goldMat = new Material(standardShader) { color = new Color(0.95f, 0.82f, 0.28f) };
            finial.GetComponent<Renderer>().material = goldMat;
        }

        private int halfVertCount;

        private void CreateFlagMesh()
        {
            var standardShader = Shader.Find("Standard") ?? Shader.Find("Diffuse");

            // Yatay Taşıyıcı Kiriş (Yardarm / Flama Kolu)
            var sparGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            DestroyImmediate(sparGo.GetComponent<Collider>());
            sparGo.name = "Banner_Yardarm";
            sparGo.transform.SetParent(transform, false);
            sparGo.transform.localPosition = new Vector3(flagWidth * 0.5f, poleHeight - 0.05f, 0);
            sparGo.transform.localRotation = Quaternion.Euler(0, 0, 90f);
            sparGo.transform.localScale = new Vector3(0.045f, flagWidth * 0.52f, 0.045f);
            var sparMat = new Material(standardShader) { color = new Color(0.28f, 0.20f, 0.14f) };
            sparGo.GetComponent<Renderer>().material = sparMat;

            // Taşıyıcı Kiriş Altın Uç Topuzu
            var sparFinial = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(sparFinial.GetComponent<Collider>());
            sparFinial.name = "Yardarm_Finial";
            sparFinial.transform.SetParent(sparGo.transform, false);
            sparFinial.transform.localPosition = new Vector3(0, 1.05f, 0);
            sparFinial.transform.localScale = Vector3.one * 1.6f;
            sparFinial.GetComponent<Renderer>().material = new Material(standardShader) { color = new Color(0.95f, 0.82f, 0.28f) };

            flagGo = new GameObject("Banner_Cloth", typeof(MeshFilter), typeof(MeshRenderer));
            flagGo.transform.SetParent(transform, false);
            flagGo.transform.localPosition = new Vector3(0.04f, poleHeight - flagHeight * 0.5f - 0.1f, 0);

            meshFilter = flagGo.GetComponent<MeshFilter>();
            var meshRenderer = flagGo.GetComponent<MeshRenderer>();

            // Dalgalanma için 8 sütun, 3 satır ızgara (Daha akıcı kumaş kıvrımları)
            int xSegments = 8;
            int ySegments = 3;
            halfVertCount = (xSegments + 1) * (ySegments + 1);
            int totalVerts = halfVertCount * 2; // Ön ve arka yüz için bağımsız verteksler (Normal sıfırlanmasını %100 önler)

            baseVertices = new Vector3[totalVerts];
            dynamicVertices = new Vector3[totalVerts];
            Vector2[] uvs = new Vector2[totalVerts];

            float dx = flagWidth / xSegments;
            float dy = flagHeight / ySegments;

            int index = 0;
            for (int y = 0; y <= ySegments; y++)
            {
                for (int x = 0; x <= xSegments; x++)
                {
                    // Swallowtail (Kırlangıç kuyruğu çentik): Flama uçlarında çift kuyruklu asil kraliyet kesimi
                    float xPos = x * dx;
                    float yPos = (y * dy) - (flagHeight * 0.5f);
                    float zPos = 0f;

                    if (x == xSegments && (y == 1 || y == 2))
                    {
                        xPos -= dx * 0.70f; // İki kuyruklu hanedanlık flama yarığı
                    }

                    Vector3 pos = new Vector3(xPos, yPos, zPos);
                    
                    // Ön yüz verteksi
                    baseVertices[index] = pos;
                    dynamicVertices[index] = pos;
                    uvs[index] = new Vector2((float)x / xSegments, (float)y / ySegments);

                    // Arka yüz verteksi (Aynı konuma kopyalanır ancak bağımsız normal hesaplanır)
                    int backIdx = index + halfVertCount;
                    baseVertices[backIdx] = pos;
                    dynamicVertices[backIdx] = pos;
                    uvs[backIdx] = new Vector2(1f - ((float)x / xSegments), (float)y / ySegments);

                    index++;
                }
            }

            int triCountPerSide = xSegments * ySegments * 6;
            int[] triangles = new int[triCountPerSide * 2];
            int tIndex = 0;

            // 1. ÖN YÜZ ÜÇGENLERİ (Normaller +Z dışa bakar)
            for (int y = 0; y < ySegments; y++)
            {
                for (int x = 0; x < xSegments; x++)
                {
                    int v0 = y * (xSegments + 1) + x;
                    int v1 = v0 + 1;
                    int v2 = (y + 1) * (xSegments + 1) + x;
                    int v3 = v2 + 1;

                    triangles[tIndex++] = v0;
                    triangles[tIndex++] = v1;
                    triangles[tIndex++] = v2;

                    triangles[tIndex++] = v1;
                    triangles[tIndex++] = v3;
                    triangles[tIndex++] = v2;
                }
            }

            // 2. ARKA YÜZ ÜÇGENLERİ (Normaller -Z dışa bakar, asla ön yüzle çakışmaz)
            for (int y = 0; y < ySegments; y++)
            {
                for (int x = 0; x < xSegments; x++)
                {
                    int bv0 = halfVertCount + (y * (xSegments + 1) + x);
                    int bv1 = bv0 + 1;
                    int bv2 = halfVertCount + ((y + 1) * (xSegments + 1) + x);
                    int bv3 = bv2 + 1;

                    triangles[tIndex++] = bv0;
                    triangles[tIndex++] = bv2;
                    triangles[tIndex++] = bv1;

                    triangles[tIndex++] = bv1;
                    triangles[tIndex++] = bv2;
                    triangles[tIndex++] = bv3;
                }
            }

            flagMesh = new Mesh
            {
                name = "FlagCloth_Procedural_DoubleSided",
                vertices = baseVertices,
                uv = uvs,
                triangles = triangles
            };
            flagMesh.RecalculateNormals();
            flagMesh.RecalculateBounds();
            meshFilter.mesh = flagMesh;

            var bannerShader = Shader.Find("Custom/MedievalDoubleSidedBanner")
                            ?? Shader.Find("Unlit/Color")
                            ?? Shader.Find("Sprites/Default")
                            ?? Shader.Find("Standard")
                            ?? Shader.Find("Diffuse");
            var bannerMat = new Material(bannerShader);

            Color clothColor = (team == UnitTeam.Player)
                ? new Color(0.12f, 0.48f, 0.98f) // Parlak Asil Kraliyet Mavisi
                : new Color(0.94f, 0.16f, 0.16f); // Parlak Düşman Kor Kırmızısı

            bannerMat.color = clothColor;
            if (bannerMat.HasProperty("_Glossiness")) bannerMat.SetFloat("_Glossiness", 0.15f);
            if (bannerMat.HasProperty("_Metallic")) bannerMat.SetFloat("_Metallic", 0.05f);

            if (bannerMat.HasProperty("_EmissionColor"))
            {
                bannerMat.EnableKeyword("_EMISSION");
                bannerMat.SetColor("_EmissionColor", clothColor * 0.45f);
            }

            meshRenderer.material = bannerMat;
        }

        private RTSFogDiscoverable parentDiscoverable;

        private void Update()
        {
            if (baseVertices == null || flagMesh == null || halfVertCount <= 0) return;

            if (parentBuilding != null)
            {
                if (parentDiscoverable == null)
                {
                    parentDiscoverable = parentBuilding.GetComponent<RTSFogDiscoverable>();
                }
                // Bina sis altında henüz keşfedilmemişse dalgalanma hesaplama ve bayrağı açma
                if (parentDiscoverable != null && !parentDiscoverable.isDiscovered)
                {
                    return;
                }
            }

            // Eğer bina inşaat halinde ise sancak henüz açılmasın
            if (parentBuilding != null && !parentBuilding.isConstructed)
            {
                if (flagGo != null && flagGo.activeSelf) flagGo.SetActive(false);
                return;
            }

            if (flagGo != null && !flagGo.activeSelf) flagGo.SetActive(true);

            float time = Time.time * waveSpeed;

            // Rüzgar dalgalanmasını simüle et: Direğe bağlı olan sol uç (x=0) sabit kalır, sağa doğru dalga şiddetlenir
            for (int i = 0; i < halfVertCount; i++)
            {
                Vector3 basePos = baseVertices[i];
                float distanceRatio = basePos.x / flagWidth; // 0 (direk) -> 1 (flama ucu)
                float wave = Mathf.Sin(time + basePos.x * 2.8f) * (waveAmplitude * distanceRatio);
                float ripple = Mathf.Cos(time * 1.5f + basePos.y * 3.2f) * (0.05f * distanceRatio);

                Vector3 animatedPos = new Vector3(basePos.x, basePos.y, wave + ripple);

                // Hem ön hem de arka yüzün vertekslerini eşzamanlı güncelle
                dynamicVertices[i] = animatedPos;
                dynamicVertices[i + halfVertCount] = animatedPos;
            }

            flagMesh.vertices = dynamicVertices;
            flagMesh.RecalculateNormals();
            flagMesh.RecalculateBounds();
        }
    }
}
