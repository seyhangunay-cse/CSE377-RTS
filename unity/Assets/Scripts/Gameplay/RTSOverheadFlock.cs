using System.Collections.Generic;
using UnityEngine;

namespace CSE377.Gameplay
{
    /// <summary>
    /// Harita üzerinde gökyüzünde süzülen, kanat çırpan ve rüzgar termallerinde daireler çizen
    /// yabani kuş sürüleri (Overhead Soaring Birds / Eagles).
    /// Age of Empires tarzı uçsuz bucaksız doğal atmosfer ve canlılık sağlar.
    /// </summary>
    public class RTSOverheadFlock : MonoBehaviour
    {
        [Header("Sürü Ayarları")]
        public int birdCount = 5;
        public float flyAltitude = 25f;
        public float flightSpeed = 9.5f;
        public float orbitRadius = 45f;
        public Vector3 centerPoint = Vector3.zero;

        private class BirdInstance
        {
            public GameObject root;
            public Transform leftWing;
            public Transform rightWing;
            public float flightAngle;
            public float flapRate;
            public float flapPhase;
            public float heightOffset;
            public float radiusOffset;
            public float speedMultiplier;
        }

        private readonly List<BirdInstance> birds = new List<BirdInstance>();

        private void Start()
        {
            SpawnFlock();
        }

        private void SpawnFlock()
        {
            var birdMat = new Material(Shader.Find("Standard"));
            birdMat.color = new Color(0.24f, 0.22f, 0.20f); // Koyu kahverengi / şahin tüyleri
            birdMat.SetFloat("_Glossiness", 0.1f);

            for (int i = 0; i < birdCount; i++)
            {
                var bRoot = new GameObject($"Flock_Bird_{i}");
                bRoot.transform.SetParent(transform, false);

                // 1. Gövde (Fuselage)
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(bRoot.transform, false);
                body.transform.localScale = new Vector3(0.22f, 0.14f, 0.75f);
                var bRen = body.GetComponent<Renderer>();
                if (bRen != null) bRen.material = birdMat;

                // 2. Sol Kanat (Left Wing)
                var leftPivot = new GameObject("LeftWing_Pivot");
                leftPivot.transform.SetParent(bRoot.transform, false);
                leftPivot.transform.localPosition = new Vector3(-0.11f, 0.02f, 0.10f);

                var lWingMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyImmediate(lWingMesh.GetComponent<Collider>());
                lWingMesh.transform.SetParent(leftPivot.transform, false);
                lWingMesh.transform.localPosition = new Vector3(-0.48f, 0, 0);
                lWingMesh.transform.localScale = new Vector3(0.95f, 0.04f, 0.35f);
                var lRen = lWingMesh.GetComponent<Renderer>();
                if (lRen != null) lRen.material = birdMat;

                // 3. Sağ Kanat (Right Wing)
                var rightPivot = new GameObject("RightWing_Pivot");
                rightPivot.transform.SetParent(bRoot.transform, false);
                rightPivot.transform.localPosition = new Vector3(0.11f, 0.02f, 0.10f);

                var rWingMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyImmediate(rWingMesh.GetComponent<Collider>());
                rWingMesh.transform.SetParent(rightPivot.transform, false);
                rWingMesh.transform.localPosition = new Vector3(0.48f, 0, 0);
                rWingMesh.transform.localScale = new Vector3(0.95f, 0.04f, 0.35f);
                var rRen = rWingMesh.GetComponent<Renderer>();
                if (rRen != null) rRen.material = birdMat;

                var inst = new BirdInstance
                {
                    root = bRoot,
                    leftWing = leftPivot.transform,
                    rightWing = rightPivot.transform,
                    flightAngle = (i / (float)birdCount) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f),
                    flapRate = Random.Range(3.8f, 5.2f),
                    flapPhase = Random.Range(0f, Mathf.PI * 2f),
                    heightOffset = Random.Range(-2.5f, 3.0f),
                    radiusOffset = Random.Range(-12f, 14f),
                    speedMultiplier = Random.Range(0.85f, 1.15f)
                };

                birds.Add(inst);
            }
        }

        private void Update()
        {
            float t = Time.time;

            for (int i = 0; i < birds.Count; i++)
            {
                var b = birds[i];
                if (b.root == null) continue;

                // Harita etrafında geniş eliptik süzülüş
                b.flightAngle += (flightSpeed * b.speedMultiplier / (orbitRadius + b.radiusOffset)) * Time.deltaTime;

                float rX = orbitRadius + b.radiusOffset + Mathf.Sin(t * 0.4f + i) * 6f;
                float rZ = (orbitRadius * 0.75f) + b.radiusOffset + Mathf.Cos(t * 0.35f + i) * 8f;

                float posX = centerPoint.x + Mathf.Cos(b.flightAngle) * rX;
                float posZ = centerPoint.z + Mathf.Sin(b.flightAngle) * rZ;
                float posY = flyAltitude + b.heightOffset + Mathf.Sin(t * 0.6f + i) * 1.2f;

                Vector3 targetPos = new Vector3(posX, posY, posZ);
                Vector3 moveDelta = targetPos - b.root.transform.position;

                if (moveDelta.sqrMagnitude > 0.0001f)
                {
                    Vector3 lookDir = moveDelta.normalized;
                    // Dönüş yönünde gövde yana yatışı (Banking roll)
                    float bankAngle = Mathf.Clamp(-lookDir.x * 25f, -28f, 28f);
                    b.root.transform.rotation = Quaternion.LookRotation(lookDir) * Quaternion.Euler(0, 0, bankAngle);
                }

                b.root.transform.position = targetPos;

                // Kanat Çırpma & Süzülme (Gliding vs Flapping):
                // 3 saniye süzülme, 1.5 saniye zarif kanat çırpışı
                float cycle = (t * 0.6f + b.flapPhase) % 4.5f;
                bool isFlapping = cycle < 1.6f;

                float flapAngle = isFlapping 
                    ? Mathf.Sin(t * b.flapRate) * 26f 
                    : Mathf.Sin(t * 1.2f) * 4f; // Süzülürken hafif hava titremesi

                b.leftWing.localRotation = Quaternion.Euler(0, 0, flapAngle);
                b.rightWing.localRotation = Quaternion.Euler(0, 0, -flapAngle);

                // Siyah keşif sisi altındaki bölgelerde uçan kuşları gizle, keşfedilen bölgelerde göster
                bool isDiscovered = RTSFogOfWarManager.Instance == null ||
                                    RTSFogOfWarManager.Instance.GetCellStateAtWorldPos(targetPos) != FogCellState.Unexplored;
                if (b.root.activeSelf != isDiscovered)
                {
                    b.root.SetActive(isDiscovered);
                }
            }
        }

        private void OnDestroy()
        {
            foreach (var b in birds)
            {
                if (b.root != null) Destroy(b.root);
            }
            birds.Clear();
        }
    }
}
