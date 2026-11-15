using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CSE377.Gameplay
{
    /// <summary>
    /// Age of Empires tarzı inşaat aşaması ahşap iskelesi (Construction Scaffolding).
    /// Binalar temel aşamasındayken etrafını saran ahşap kalaslar ve iskele platformları üretir.
    /// İnşaat %100'e ulaştığında iskele dağılır ve bina tamamlanmış haliyle ortaya çıkar.
    /// </summary>
    public class RTSConstructionScaffold : MonoBehaviour
    {
        private RTSBuilding building;
        private GameObject scaffoldRoot;
        private List<GameObject> scaffoldBeams = new List<GameObject>();
        private static Material woodScaffoldMat;

        private void Awake()
        {
            building = GetComponent<RTSBuilding>();
        }

        private void Start()
        {
            if (building != null && !building.isConstructed)
            {
                BuildScaffold();
            }
        }

        private static Material GetScaffoldMaterial()
        {
            if (woodScaffoldMat == null)
            {
                var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
                woodScaffoldMat = new Material(shader)
                {
                    color = new Color(0.55f, 0.38f, 0.22f), // Ahşap kalas rengi
                    name = "Mat_WoodScaffold"
                };
                woodScaffoldMat.SetFloat("_Glossiness", 0.15f);
            }
            return woodScaffoldMat;
        }

        public void BuildScaffold()
        {
            if (scaffoldRoot != null) Destroy(scaffoldRoot);

            scaffoldRoot = new GameObject("Construction_Scaffold_Root");
            scaffoldRoot.transform.SetParent(transform, false);
            scaffoldRoot.transform.localPosition = Vector3.zero;

            Vector3 size = GetScaffoldSize();
            float halfX = size.x * 0.5f;
            float halfZ = size.z * 0.5f;
            float height = size.y;

            Material mat = GetScaffoldMaterial();

            // 1. Dikey Köşe Direkleri (Vertical Corner Timber Posts)
            Vector3[] cornerPositions = new Vector3[]
            {
                new Vector3(-halfX, height * 0.5f, -halfZ),
                new Vector3(halfX, height * 0.5f, -halfZ),
                new Vector3(-halfX, height * 0.5f, halfZ),
                new Vector3(halfX, height * 0.5f, halfZ)
            };

            foreach (var pos in cornerPositions)
            {
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(post.GetComponent<Collider>());
                post.transform.SetParent(scaffoldRoot.transform, false);
                post.transform.localPosition = pos;
                post.transform.localScale = new Vector3(0.18f, height * 0.5f, 0.18f);
                post.GetComponent<Renderer>().material = mat;
                scaffoldBeams.Add(post);
            }

            // 2. Yatay Kuşak Kirişleri (Horizontal Tie Beams at 2 levels)
            float[] levels = new float[] { height * 0.35f, height * 0.75f };
            foreach (float lvl in levels)
            {
                CreateHorizontalBeam(new Vector3(0, lvl, -halfZ), new Vector3(size.x, 0.12f, 0.12f), mat);
                CreateHorizontalBeam(new Vector3(0, lvl, halfZ), new Vector3(size.x, 0.12f, 0.12f), mat);
                CreateHorizontalBeam(new Vector3(-halfX, lvl, 0), new Vector3(0.12f, 0.12f, size.z), mat);
                CreateHorizontalBeam(new Vector3(halfX, lvl, 0), new Vector3(0.12f, 0.12f, size.z), mat);
            }

            // 3. Çalışma İskele Platformu (Wooden Work Platform Planks)
            var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(plank.GetComponent<Collider>());
            plank.transform.SetParent(scaffoldRoot.transform, false);
            plank.transform.localPosition = new Vector3(0, height * 0.36f, -halfZ + 0.3f);
            plank.transform.localScale = new Vector3(size.x * 0.9f, 0.08f, 0.6f);
            plank.GetComponent<Renderer>().material = mat;
            scaffoldBeams.Add(plank);

            // 4. Ahşap İnşaat Merdiveni (Scaffold Ladder)
            CreateLadder(new Vector3(-halfX + 0.5f, height * 0.35f, -halfZ - 0.15f), height * 0.7f, mat);
        }

        private void CreateHorizontalBeam(Vector3 localPos, Vector3 scale, Material mat)
        {
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(beam.GetComponent<Collider>());
            beam.transform.SetParent(scaffoldRoot.transform, false);
            beam.transform.localPosition = localPos;
            beam.transform.localScale = scale;
            beam.GetComponent<Renderer>().material = mat;
            scaffoldBeams.Add(beam);
        }

        private void CreateLadder(Vector3 localPos, float height, Material mat)
        {
            var lRoot = new GameObject("Ladder");
            lRoot.transform.SetParent(scaffoldRoot.transform, false);
            lRoot.transform.localPosition = localPos;
            lRoot.transform.localRotation = Quaternion.Euler(0, 0, 8f);

            var rail1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(rail1.GetComponent<Collider>());
            rail1.transform.SetParent(lRoot.transform, false);
            rail1.transform.localPosition = new Vector3(-0.2f, 0, 0);
            rail1.transform.localScale = new Vector3(0.06f, height, 0.06f);
            rail1.GetComponent<Renderer>().material = mat;

            var rail2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(rail2.GetComponent<Collider>());
            rail2.transform.SetParent(lRoot.transform, false);
            rail2.transform.localPosition = new Vector3(0.2f, 0, 0);
            rail2.transform.localScale = new Vector3(0.06f, height, 0.06f);
            rail2.GetComponent<Renderer>().material = mat;

            int rungCount = Mathf.FloorToInt(height / 0.35f);
            for (int i = 0; i < rungCount; i++)
            {
                var rung = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(rung.GetComponent<Collider>());
                rung.transform.SetParent(lRoot.transform, false);
                rung.transform.localPosition = new Vector3(0, -height * 0.5f + (i + 1) * 0.35f, 0);
                rung.transform.localScale = new Vector3(0.38f, 0.04f, 0.04f);
                rung.GetComponent<Renderer>().material = mat;
            }
        }

        private Vector3 GetScaffoldSize()
        {
            if (building == null) return new Vector3(4f, 4f, 4f);
            switch (building.buildingType)
            {
                case RTSBuildingType.TownCenter: return new Vector3(7.5f, 6.5f, 7.5f);
                case RTSBuildingType.Castle: return new Vector3(9.0f, 9.0f, 9.0f);
                case RTSBuildingType.Barracks: return new Vector3(5.5f, 4.5f, 5.5f);
                case RTSBuildingType.ArcheryRange: return new Vector3(5.5f, 4.0f, 5.5f);
                case RTSBuildingType.SiegeWorkshop: return new Vector3(6.0f, 4.5f, 6.0f);
                case RTSBuildingType.Market: return new Vector3(5.5f, 4.0f, 5.5f);
                case RTSBuildingType.Blacksmith: return new Vector3(5.0f, 4.0f, 5.0f);
                case RTSBuildingType.DefenseTower: return new Vector3(3.2f, 7.5f, 3.2f);
                case RTSBuildingType.House: return new Vector3(4.2f, 3.5f, 4.2f);
                case RTSBuildingType.Farm: return new Vector3(5.0f, 1.5f, 5.0f);
                case RTSBuildingType.StoneWall: return new Vector3(3.2f, 3.5f, 1.5f);
                case RTSBuildingType.CityGate: return new Vector3(4.5f, 4.5f, 2.0f);
                default: return new Vector3(4.5f, 4.0f, 4.5f);
            }
        }

        /// <summary>
        /// İnşaat tamamlandığında iskeleleri şık bir şekilde yıkarak sahneden kaldırır.
        /// </summary>
        public void DismantleScaffold()
        {
            if (scaffoldRoot == null) return;

            // Her bir kalası hafifçe dışa fırlatarak yok et
            foreach (var beam in scaffoldBeams)
            {
                if (beam != null)
                {
                    beam.transform.SetParent(null);
                    StartCoroutine(DropAndFadeBeam(beam));
                }
            }
            scaffoldBeams.Clear();

            Destroy(scaffoldRoot);
            scaffoldRoot = null;
        }

        private IEnumerator DropAndFadeBeam(GameObject beam)
        {
            float elapsed = 0f;
            float duration = 0.65f;
            Vector3 vel = (beam.transform.position - transform.position).normalized * Random.Range(1.5f, 3.0f) + Vector3.up * 1.5f;

            while (elapsed < duration && beam != null)
            {
                elapsed += Time.deltaTime;
                vel += Vector3.down * (9.8f * Time.deltaTime);
                beam.transform.position += vel * Time.deltaTime;
                beam.transform.Rotate(new Vector3(45f, 60f, 30f) * Time.deltaTime, Space.World);

                float t = elapsed / duration;
                beam.transform.localScale = Vector3.Lerp(beam.transform.localScale, Vector3.zero, t * t);
                yield return null;
            }

            if (beam != null) Destroy(beam);
        }

        private void OnDestroy()
        {
            if (scaffoldRoot != null) Destroy(scaffoldRoot);
        }
    }
}
