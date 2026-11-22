using System.Collections.Generic;
using UnityEngine;

namespace CSE377.Gameplay
{
    public enum RTSProjectileType
    {
        Arrow,
        SiegeBoulder
    }

    /// <summary>
    /// Gerçekçi 3D Parabolik Mermi Motoru (Ballistic Projectile Engine).
    /// - Okçular, Savunma Kuleleri ve Şanlı Kale için parabolik ok fiziği ve saplanma efektleri.
    /// - Kuşatma Mancınığı (Trebuchet) için alevli/kayalık dev gülle, parabolik tepe kavisi ve Alan Etkili (AoE) patlama hasarı.
    /// </summary>
    public class RTSProjectile : MonoBehaviour
    {
        public RTSProjectileType projectileType = RTSProjectileType.Arrow;
        public RTSUnit targetUnit;
        public RTSBuilding targetBuilding;
        public Vector3 targetPosition;
        public int damage = 12;
        public float speed = 20f;
        public float arcHeight = 2.4f;
        public RTSUnit sourceUnit;
        public UnitTeam team = UnitTeam.Player;

        private Vector3 startPosition;
        private Vector3 previousPosition;
        private float travelProgress = 0f;
        private float totalDistance = 1f;
        private Vector3 tumbleAxis;
        private Renderer[] cachedRenderers;
        private bool isFogVisible = true;

        private void Awake()
        {
            cachedRenderers = GetComponentsInChildren<Renderer>(true);
        }

        private void UpdateFogVisibility()
        {
            if (team == RTSGameManager.LocalPlayerTeam || RTSFogOfWarManager.Instance == null)
            {
                SetRenderersVisible(true);
                return;
            }
            bool visibleNow = RTSFogOfWarManager.Instance.IsPositionInPlayerVision(transform.position);
            SetRenderersVisible(visibleNow);
        }

        private void SetRenderersVisible(bool vis)
        {
            if (isFogVisible == vis) return;
            isFogVisible = vis;
            if (cachedRenderers == null) cachedRenderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                if (cachedRenderers[i] != null) cachedRenderers[i].enabled = vis;
            }
        }

        public static void FireArrow(Vector3 fromPos, RTSUnit target, int dmg, RTSUnit source, UnitTeam firingTeam = UnitTeam.Player)
        {
            if (target == null) return;
            CreateArrowInstance(fromPos, target, null, target.transform.position + Vector3.up * 0.9f, dmg, source, firingTeam);
        }

        public static void FireArrowAtBuilding(Vector3 fromPos, RTSBuilding target, int dmg, RTSUnit source, UnitTeam firingTeam = UnitTeam.Player)
        {
            if (target == null) return;
            Collider col = target.GetComponent<Collider>();
            Vector3 hitPoint = (col != null) ? col.ClosestPoint(fromPos) : target.transform.position;
            hitPoint.y += 1.2f;
            CreateArrowInstance(fromPos, null, target, hitPoint, dmg, source, firingTeam);
        }

        public static void FireSiegeBoulder(Vector3 fromPos, RTSUnit target, int dmg, RTSUnit source, UnitTeam firingTeam = UnitTeam.Player)
        {
            if (target == null) return;
            CreateBoulderInstance(fromPos, target, null, target.transform.position, dmg, source, firingTeam);
        }

        public static void FireSiegeBoulderAtBuilding(Vector3 fromPos, RTSBuilding target, int dmg, RTSUnit source, UnitTeam firingTeam = UnitTeam.Player)
        {
            if (target == null) return;
            Collider col = target.GetComponent<Collider>();
            Vector3 hitPoint = (col != null) ? col.ClosestPoint(fromPos) : target.transform.position;
            hitPoint.y += 1.5f;
            CreateBoulderInstance(fromPos, null, target, hitPoint, dmg, source, firingTeam);
        }

        private static void CreateArrowInstance(Vector3 fromPos, RTSUnit tUnit, RTSBuilding tBuild, Vector3 targetPos, int dmg, RTSUnit src, UnitTeam firingTeam)
        {
            var arrowGo = new GameObject("Projectile_Arrow");
            arrowGo.transform.position = fromPos + Vector3.up * 0.8f;

            // 1. Ok Şaftı (İnce Ahşap Gövde)
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(arrowGo.transform, false);
            shaft.transform.localScale = new Vector3(0.045f, 0.40f, 0.045f);
            shaft.transform.localRotation = Quaternion.Euler(90f, 0, 0); // İleri doğrultuda hizala
            shaft.GetComponent<Renderer>().material.color = new Color(0.48f, 0.32f, 0.16f); // Ahşap gövde

            // 2. Çelik Temren (Demir Ok Ucu)
            var tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(tip.GetComponent<Collider>());
            tip.transform.SetParent(arrowGo.transform, false);
            tip.transform.localPosition = new Vector3(0, 0, 0.42f);
            tip.transform.localScale = new Vector3(0.08f, 0.08f, 0.14f);
            tip.GetComponent<Renderer>().material.color = new Color(0.72f, 0.74f, 0.78f); // Çelik gri

            // 3. Tüy / Yelek (Fletching - Beyaz tüyler)
            var fletch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(fletch.GetComponent<Collider>());
            fletch.transform.SetParent(arrowGo.transform, false);
            fletch.transform.localPosition = new Vector3(0, 0, -0.38f);
            fletch.transform.localScale = new Vector3(0.12f, 0.015f, 0.16f);
            fletch.GetComponent<Renderer>().material.color = (firingTeam == UnitTeam.Player) ? new Color(0.9f, 0.95f, 1f) : new Color(1f, 0.4f, 0.4f);

            var proj = arrowGo.AddComponent<RTSProjectile>();
            proj.projectileType = RTSProjectileType.Arrow;
            proj.targetUnit = tUnit;
            proj.targetBuilding = tBuild;
            proj.targetPosition = targetPos;
            proj.damage = dmg;
            proj.sourceUnit = src;
            proj.team = firingTeam;
            proj.speed = 22f; // Hızlı ok uçuşu
            proj.startPosition = arrowGo.transform.position;
            proj.previousPosition = proj.startPosition;
            proj.totalDistance = Vector3.Distance(proj.startPosition, proj.targetPosition);
            if (proj.totalDistance < 0.1f) proj.totalDistance = 0.1f;
            proj.arcHeight = Mathf.Clamp(proj.totalDistance * 0.18f, 1.2f, 3.8f);
        }

        private static void CreateBoulderInstance(Vector3 fromPos, RTSUnit tUnit, RTSBuilding tBuild, Vector3 targetPos, int dmg, RTSUnit src, UnitTeam firingTeam)
        {
            var boulderGo = new GameObject("Projectile_SiegeBoulder");
            boulderGo.transform.position = fromPos + Vector3.up * 1.5f;

            // 1. Ana Masif Kaya Gövdesi (Pürüzlü granit kaya)
            var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(rock.GetComponent<Collider>());
            rock.transform.SetParent(boulderGo.transform, false);
            rock.transform.localScale = new Vector3(0.75f, 0.65f, 0.85f);
            var ren = rock.GetComponent<Renderer>();
            ren.material.color = new Color(0.28f, 0.26f, 0.24f); // Koyu yanık taş

            // 2. Alevli Köz Parçası (Siege Fire Trail)
            var fireCore = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(fireCore.GetComponent<Collider>());
            fireCore.transform.SetParent(boulderGo.transform, false);
            fireCore.transform.localScale = new Vector3(0.55f, 0.55f, 0.55f);
            var fMat = new Material(Shader.Find("Standard"));
            fMat.color = new Color(1f, 0.55f, 0.1f, 0.85f);
            fMat.EnableKeyword("_EMISSION");
            fMat.SetColor("_EmissionColor", new Color(1f, 0.4f, 0f) * 1.8f);
            fireCore.GetComponent<Renderer>().material = fMat;

            var proj = boulderGo.AddComponent<RTSProjectile>();
            proj.projectileType = RTSProjectileType.SiegeBoulder;
            proj.targetUnit = tUnit;
            proj.targetBuilding = tBuild;
            proj.targetPosition = targetPos;
            proj.damage = dmg;
            proj.sourceUnit = src;
            proj.team = firingTeam;
            proj.speed = 17f; // Ağır gülle süzülüşü
            proj.startPosition = boulderGo.transform.position;
            proj.previousPosition = proj.startPosition;
            proj.totalDistance = Vector3.Distance(proj.startPosition, proj.targetPosition);
            if (proj.totalDistance < 0.1f) proj.totalDistance = 0.1f;
            proj.arcHeight = Mathf.Clamp(proj.totalDistance * 0.32f, 4.5f, 9.0f); // İhtişamlı yüksek mancınık kavisi
            proj.tumbleAxis = Random.onUnitSphere;
        }

        private void Update()
        {
            // Hedef hareket ediyorsa takip et
            if (targetUnit != null && targetUnit.currentHp > 0)
            {
                targetPosition = targetUnit.transform.position + Vector3.up * 0.8f;
            }
            else if (targetBuilding != null && targetBuilding.currentHp > 0)
            {
                Collider col = targetBuilding.GetComponent<Collider>();
                if (col != null)
                {
                    targetPosition = col.ClosestPoint(transform.position) + Vector3.up * 1.2f;
                }
            }

            travelProgress += (speed / totalDistance) * Time.deltaTime;

            if (travelProgress >= 1f)
            {
                HitTarget();
                return;
            }

            // Parabolik balistik kavis formülü: Y = Lerp + 4 * H * t * (1 - t)
            Vector3 currentPos = Vector3.Lerp(startPosition, targetPosition, travelProgress);
            float parabolicArc = 4f * arcHeight * travelProgress * (1f - travelProgress);
            currentPos.y += parabolicArc;

            // Merminin uçuş teğetine bakması (Ballistic Tangent Orientation)
            Vector3 velocity = currentPos - previousPosition;
            if (velocity.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(velocity);
            }

            if (projectileType == RTSProjectileType.SiegeBoulder)
            {
                // Güllenin havada takla atması
                transform.Rotate(tumbleAxis * (Time.deltaTime * 360f), Space.Self);
            }

            previousPosition = transform.position;
            transform.position = currentPos;
            UpdateFogVisibility();
        }

        private void HitTarget()
        {
            if (projectileType == RTSProjectileType.Arrow)
            {
                HandleArrowImpact();
            }
            else
            {
                HandleSiegeBoulderImpact();
            }

            Destroy(gameObject);
        }

        private void HandleArrowImpact()
        {
            RTSAudioManager.Instance?.PlaySound(RTSSoundType.ArrowHit);

            if (targetUnit != null && targetUnit.currentHp > 0)
            {
                if (sourceUnit != null)
                {
                    sourceUnit.ApplyCombatDamage(targetUnit, damage);
                }
                else
                {
                    targetUnit.TakeDamage(damage);
                }
            }
            else if (targetBuilding != null && targetBuilding.currentHp > 0)
            {
                targetBuilding.TakeDamage(damage, this.team);
            }

            // Kıvılcım / vuruş efekti
            RTSVFXManager.Instance?.SpawnArrowImpactVFX(transform.position);
        }

        private void HandleSiegeBoulderImpact()
        {
            RTSAudioManager.Instance?.PlaySound(RTSSoundType.SiegeImpact);

            // Kamera Sarsıntısı (Screen Shake)
            if (Camera.main != null)
            {
                var camCtrl = Camera.main.GetComponent<RTSCameraController>();
                camCtrl?.TriggerShake(0.45f, 0.35f);
            }

            Vector3 impactCenter = transform.position;

            // 1. Ana Hedefe Tam Hasar
            if (targetBuilding != null && targetBuilding.currentHp > 0)
            {
                targetBuilding.TakeDamage(damage, this.team);
            }
            else if (targetUnit != null && targetUnit.currentHp > 0)
            {
                if (sourceUnit != null) sourceUnit.ApplyCombatDamage(targetUnit, damage);
                else targetUnit.TakeDamage(damage);
            }

            // 2. Alan Etkili Patlama Hasarı (AoE Splash Damage - 3.8 metre)
            float splashRadius = 3.8f;
            int splashDamage = Mathf.RoundToInt(damage * 0.5f);

            foreach (var u in new System.Collections.Generic.List<RTSUnit>(RTSUnit.AllUnits))
            {
                if (u != null && u != targetUnit && u.team != this.team && u.currentHp > 0)
                {
                    float dist = Vector3.Distance(impactCenter, u.transform.position);
                    if (dist <= splashRadius)
                    {
                        u.TakeDamage(splashDamage);

                        // Patlama itiş kuvveti (Knockback impulse)
                        Vector3 pushDir = (u.transform.position - impactCenter).normalized;
                        pushDir.y = 0;
                        u.transform.position += pushDir * Mathf.Lerp(1.2f, 0.3f, dist / splashRadius);
                    }
                }
            }

            // Yakındaki düşman binalarına da şarapnel hasarı
            foreach (var b in new System.Collections.Generic.List<RTSBuilding>(RTSBuilding.AllBuildings))
            {
                if (b != null && b != targetBuilding && b.team != this.team && b.currentHp > 0)
                {
                    Collider bCol = b.GetComponent<Collider>();
                    Vector3 closest = (bCol != null) ? bCol.ClosestPoint(impactCenter) : b.transform.position;
                    if (Vector3.Distance(impactCenter, closest) <= splashRadius)
                    {
                        b.TakeDamage(splashDamage, this.team);
                    }
                }
            }

            // 3. Görkemli Kuşatma Krateri & Toz Bulutu Efekti
            SpawnExplosionDebris(impactCenter);
        }

        private void SpawnExplosionDebris(Vector3 center)
        {
            // Şok dalgası halkası
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "SiegeCraterRing";
            ring.transform.position = center + Vector3.up * 0.05f;
            ring.transform.localScale = new Vector3(3.6f, 0.04f, 3.6f);
            var ren = ring.GetComponent<Renderer>();
            ren.material.color = new Color(0.2f, 0.16f, 0.12f, 0.8f);
            Destroy(ring, 1.2f);

            // Havaya fırlayan alevli kaya parçacıkları
            for (int i = 0; i < 6; i++)
            {
                var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(shard.GetComponent<Collider>());
                shard.transform.position = center + Vector3.up * 0.5f;
                shard.transform.localScale = Vector3.one * Random.Range(0.2f, 0.38f);
                shard.GetComponent<Renderer>().material.color = new Color(0.85f, 0.45f, 0.1f);

                var rb = shard.AddComponent<Rigidbody>();
                Vector3 blastDir = (Random.onUnitSphere + Vector3.up * 1.5f).normalized;
                rb.linearVelocity = blastDir * Random.Range(4f, 8.5f);
                Destroy(shard, 0.65f);
            }
        }
    }
}
