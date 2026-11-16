using System.Collections.Generic;
using UnityEngine;

namespace CSE377.Gameplay
{
    public class RTSUnitRig : MonoBehaviour
    {
        public Transform leftLeg;
        public Transform rightLeg;
        public Transform leftArm;
        public Transform rightArm;
        public Transform head;
        public Transform torso;
        public Transform horseFL;
        public Transform horseFR;
        public Transform horseBL;
        public Transform horseBR;
        public Transform throwingArm;
        public bool isMounted;
        public bool isSiege;

        public void AnimateMove(float cycle, bool isAttacking)
        {
            if (isMounted)
            {
                float f1 = Mathf.Sin(cycle) * 30f;
                float f2 = Mathf.Sin(cycle + 0.55f) * 30f;
                float b1 = -Mathf.Sin(cycle + 0.20f) * 28f;
                float b2 = -Mathf.Sin(cycle + 0.75f) * 28f;
                if (horseFL != null) horseFL.localRotation = Quaternion.Euler(f1, 0f, 0f);
                if (horseFR != null) horseFR.localRotation = Quaternion.Euler(f2, 0f, 0f);
                if (horseBL != null) horseBL.localRotation = Quaternion.Euler(b1, 0f, 0f);
                if (horseBR != null) horseBR.localRotation = Quaternion.Euler(b2, 0f, 0f);
                if (!isAttacking && torso != null)
                {
                    torso.localRotation = Quaternion.Euler(Mathf.Sin(cycle) * 4f, 0f, 0f);
                }
            }
            else if (!isSiege)
            {
                float swing = Mathf.Sin(cycle * 0.95f) * 28f;
                if (leftLeg != null) leftLeg.localRotation = Quaternion.Euler(swing, 0f, 0f);
                if (rightLeg != null) rightLeg.localRotation = Quaternion.Euler(-swing, 0f, 0f);
                if (!isAttacking)
                {
                    if (leftArm != null) leftArm.localRotation = Quaternion.Euler(-swing * 0.42f, 0f, 0f);
                    if (rightArm != null) rightArm.localRotation = Quaternion.Euler(swing * 0.42f, 0f, 0f);
                    if (torso != null) torso.localRotation = Quaternion.Euler(3f, Mathf.Sin(cycle * 0.48f) * 4f, 0f);
                }
            }
        }

        public void ResetToRest(float dt)
        {
            Quaternion id = Quaternion.identity;
            if (leftLeg != null) leftLeg.localRotation = Quaternion.Lerp(leftLeg.localRotation, id, dt);
            if (rightLeg != null) rightLeg.localRotation = Quaternion.Lerp(rightLeg.localRotation, id, dt);
            if (leftArm != null) leftArm.localRotation = Quaternion.Lerp(leftArm.localRotation, id, dt);
            if (rightArm != null) rightArm.localRotation = Quaternion.Lerp(rightArm.localRotation, id, dt);
            if (torso != null) torso.localRotation = Quaternion.Lerp(torso.localRotation, id, dt);
            if (head != null) head.localRotation = Quaternion.Lerp(head.localRotation, id, dt);
            if (horseFL != null) horseFL.localRotation = Quaternion.Lerp(horseFL.localRotation, id, dt);
            if (horseFR != null) horseFR.localRotation = Quaternion.Lerp(horseFR.localRotation, id, dt);
            if (horseBL != null) horseBL.localRotation = Quaternion.Lerp(horseBL.localRotation, id, dt);
            if (horseBR != null) horseBR.localRotation = Quaternion.Lerp(horseBR.localRotation, id, dt);
            if (throwingArm != null) throwingArm.localRotation = Quaternion.Lerp(throwingArm.localRotation, id, dt);
        }
    }

    /// <summary>
    /// RTS birimleri için (Mızrakçı, Kılıç Ustası, Okçu, Arbaletçi, İzci, Süvari, Köylü, Mancınık)
    /// eklemli 3D modeller, PBR materyaller ve güvenli fallback görselleri üretir.
    /// </summary>
    public static class RTSUnitVisuals
    {
        private static Dictionary<string, Material> cachedMaterials = new Dictionary<string, Material>();

        private static string ResolveUnitMaterialKey(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return "steel";
            string s = rawName.ToLowerInvariant();
            if (s.Contains("horse_chestnut")) return "horse_chestnut";
            if (s.Contains("horse_dapple")) return "horse_dapple";
            if (s.Contains("tunic_team")) return "tunic_team";
            if (s.Contains("chainmail")) return "chainmail";
            if (s.Contains("leather")) return "leather";
            if (s.Contains("linen")) return "linen";
            if (s.Contains("skin")) return "skin";
            if (s.Contains("timber")) return "timber";
            if (s.Contains("granite")) return "granite";
            if (s.Contains("gold")) return "gold";
            if (s.Contains("iron")) return "iron";
            if (s.Contains("steel")) return "steel";
            return "steel";
        }

        private static Material GetUnitPbrMaterial(string matKey, UnitTeam team)
        {
            string cacheKey = $"PBR_{team}_{matKey}";
            if (cachedMaterials.TryGetValue(cacheKey, out var cached) && cached != null)
            {
                return cached;
            }

            var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            var mat = new Material(shader)
            {
                name = $"Mat_Unit_{cacheKey}",
                color = Color.white
            };

            string albedoPath = null;
            string normalPath = null;
            float metallic = 0.0f;
            float smoothness = 0.22f;

            switch (matKey)
            {
                case "tunic_team":
                    albedoPath = (team == UnitTeam.Player)
                        ? "Textures/Units/tunic_blue_albedo"
                        : "Textures/Units/tunic_red_albedo";
                    normalPath = "Textures/Units/tunic_normal";
                    smoothness = 0.16f;
                    break;
                case "chainmail":
                    albedoPath = "Textures/Units/chainmail_albedo";
                    normalPath = "Textures/Units/chainmail_normal";
                    metallic = 0.68f;
                    smoothness = 0.44f;
                    break;
                case "steel":
                    albedoPath = "Textures/Units/steel_plate_albedo";
                    normalPath = "Textures/Units/steel_plate_normal";
                    metallic = 0.85f;
                    smoothness = 0.72f;
                    break;
                case "leather":
                    albedoPath = "Textures/Units/leather_armor_albedo";
                    normalPath = "Textures/Units/leather_armor_normal";
                    smoothness = 0.26f;
                    break;
                case "linen":
                    albedoPath = "Textures/Units/linen_peasant_albedo";
                    normalPath = "Textures/Units/linen_peasant_normal";
                    smoothness = 0.14f;
                    break;
                case "skin":
                    albedoPath = "Textures/Units/skin_tone_albedo";
                    smoothness = 0.18f;
                    break;
                case "horse_chestnut":
                    albedoPath = "Textures/Units/horse_chestnut_albedo";
                    normalPath = "Textures/Units/horse_coat_normal";
                    smoothness = 0.34f;
                    break;
                case "horse_dapple":
                    albedoPath = "Textures/Units/horse_dapple_albedo";
                    normalPath = "Textures/Units/horse_coat_normal";
                    smoothness = 0.30f;
                    break;
                case "timber":
                    albedoPath = "Textures/Buildings/timber_oak_albedo";
                    normalPath = "Textures/Buildings/timber_oak_normal";
                    smoothness = 0.20f;
                    break;
                case "iron":
                    albedoPath = "Textures/Buildings/wrought_iron_albedo";
                    normalPath = "Textures/Buildings/wrought_iron_normal";
                    metallic = 0.80f;
                    smoothness = 0.55f;
                    break;
                case "granite":
                    albedoPath = "Textures/Environment/granite_rock_albedo";
                    normalPath = "Textures/Environment/granite_rock_normal";
                    smoothness = 0.16f;
                    break;
                case "gold":
                    albedoPath = "Textures/Environment/gold_ore_albedo";
                    normalPath = "Textures/Environment/gold_ore_normal";
                    metallic = 0.88f;
                    smoothness = 0.82f;
                    break;
            }

            if (!string.IsNullOrEmpty(albedoPath))
            {
                var tex = Resources.Load<Texture2D>(albedoPath);
                if (tex != null) mat.mainTexture = tex;
            }

            if (shader != null && shader.name == "Standard")
            {
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Glossiness", smoothness);

                if (!string.IsNullOrEmpty(normalPath))
                {
                    var nTex = Resources.Load<Texture2D>(normalPath);
                    if (nTex != null)
                    {
                        mat.EnableKeyword("_NORMALMAP");
                        mat.SetTexture("_BumpMap", nTex);
                        mat.SetFloat("_BumpScale", 0.70f);
                    }
                }
            }

            cachedMaterials[cacheKey] = mat;
            return mat;
        }

        public static void ApplyUnitMaterials(GameObject root, UnitTeam team)
        {
            if (root == null) return;
            foreach (var ren in root.GetComponentsInChildren<Renderer>(true))
            {
                if (ren == null) continue;
                var shared = ren.sharedMaterials;
                if (shared == null || shared.Length <= 1)
                {
                    string srcName = (shared != null && shared.Length == 1 && shared[0] != null)
                        ? $"{ren.gameObject.name}_{shared[0].name}"
                        : ren.gameObject.name;
                    ren.sharedMaterial = GetUnitPbrMaterial(ResolveUnitMaterialKey(srcName), team);
                }
                else
                {
                    var mats = new Material[shared.Length];
                    for (int i = 0; i < shared.Length; i++)
                    {
                        string slotName = (shared[i] != null) ? shared[i].name : ren.gameObject.name;
                        mats[i] = GetUnitPbrMaterial(ResolveUnitMaterialKey(slotName), team);
                    }
                    ren.sharedMaterials = mats;
                }
            }
        }

        private static Transform WrapJointPivot(Transform root, string jointName, Vector3 pivotLocalPos)
        {
            Transform child = null;
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (tr != root && string.Equals(tr.name, jointName, System.StringComparison.OrdinalIgnoreCase))
                {
                    child = tr;
                    break;
                }
            }
            if (child == null) return null;

            var pivotGo = new GameObject($"Pivot_{jointName}");
            pivotGo.transform.SetParent(root, false);
            pivotGo.transform.localPosition = pivotLocalPos;
            pivotGo.transform.localRotation = Quaternion.identity;

            child.SetParent(pivotGo.transform, false);
            child.localPosition = -pivotLocalPos;
            child.localRotation = Quaternion.identity;
            return pivotGo.transform;
        }

        private static bool TryBuildArticulatedUnit(Transform modelRoot, string lowerType, UnitTeam team)
        {
            string modelName;
            bool isMounted = false;
            bool isSiege = false;

            switch (lowerType)
            {
                case "villager": modelName = "Villager"; break;
                case "spearman": modelName = "Spearman"; break;
                case "swordsman": modelName = "Swordsman"; break;
                case "archer": modelName = "Archer"; break;
                case "crossbowman": modelName = "Crossbowman"; break;
                case "scout": modelName = "Scout"; isMounted = true; break;
                case "cavalry": modelName = "Cavalry"; isMounted = true; break;
                case "trebuchet": modelName = "Trebuchet"; isSiege = true; break;
                default: modelName = "Spearman"; break;
            }

            var prefab = Resources.Load<GameObject>($"Models/Units/{modelName}");
            if (prefab == null) return false;

            var go = Object.Instantiate(prefab, modelRoot);
            go.name = $"Articulated_{modelName}";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            foreach (var col in go.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(col);
            }

            ApplyUnitMaterials(go, team);

            var rig = go.AddComponent<RTSUnitRig>();
            rig.isMounted = isMounted;
            rig.isSiege = isSiege;

            if (isSiege)
            {
                rig.throwingArm = WrapJointPivot(go.transform, "J_ThrowingArm", new Vector3(0f, 2.05f, 0.15f));
            }
            else if (isMounted)
            {
                rig.horseFL = WrapJointPivot(go.transform, "J_HorseFL", new Vector3(-0.22f, 0.80f, 0.52f));
                rig.horseFR = WrapJointPivot(go.transform, "J_HorseFR", new Vector3( 0.22f, 0.80f, 0.52f));
                rig.horseBL = WrapJointPivot(go.transform, "J_HorseBL", new Vector3(-0.22f, 0.80f, -0.52f));
                rig.horseBR = WrapJointPivot(go.transform, "J_HorseBR", new Vector3( 0.22f, 0.80f, -0.52f));
                rig.torso = WrapJointPivot(go.transform, "J_Torso", new Vector3(0f, 1.28f, -0.05f));
                rig.head = WrapJointPivot(go.transform, "J_Head", new Vector3(0f, 1.80f, -0.05f));
                rig.leftArm = WrapJointPivot(go.transform, "J_LeftArm", new Vector3(-0.25f, 1.80f, -0.05f));
                rig.rightArm = WrapJointPivot(go.transform, "J_RightArm", new Vector3(0.25f, 1.80f, -0.05f));
            }
            else
            {
                rig.leftLeg = WrapJointPivot(go.transform, "J_LeftLeg", new Vector3(-0.115f, 0.72f, 0f));
                rig.rightLeg = WrapJointPivot(go.transform, "J_RightLeg", new Vector3(0.115f, 0.72f, 0f));
                rig.torso = WrapJointPivot(go.transform, "J_Torso", new Vector3(0f, 0.72f, 0f));
                rig.head = WrapJointPivot(go.transform, "J_Head", new Vector3(0f, 1.24f, 0f));
                rig.leftArm = WrapJointPivot(go.transform, "J_LeftArm", new Vector3(-0.24f, 1.18f, 0f));
                rig.rightArm = WrapJointPivot(go.transform, "J_RightArm", new Vector3(0.24f, 1.18f, 0f));
            }

            return true;
        }

        private static Material GetMat(string key, Color color, float metallic = 0f, float smoothness = 0.3f)
        {
            if (cachedMaterials.TryGetValue(key, out var mat) && mat != null)
            {
                return mat;
            }

            var shader = Shader.Find("Standard") ?? Shader.Find("Mobile/Diffuse") ?? Shader.Find("Diffuse");
            var m = new Material(shader)
            {
                name = $"Mat_{key}",
                color = color
            };

            if (shader.name == "Standard")
            {
                m.SetFloat("_Metallic", metallic);
                m.SetFloat("_Glossiness", smoothness);
            }

            cachedMaterials[key] = m;
            return m;
        }

        public static Material GetTeamMat(UnitTeam team)
        {
            return (team == UnitTeam.Player)
                ? GetMat("Team_Player", new Color(0.12f, 0.45f, 0.95f), 0.1f, 0.4f)  // Asil Kraliyet Mavisi
                : GetMat("Team_Enemy", new Color(0.92f, 0.15f, 0.15f), 0.1f, 0.4f);   // Savaşçı Kızıl Kırmızı
        }

        public static Material GetSteelMat()
        {
            return GetMat("Steel", new Color(0.78f, 0.80f, 0.84f), 0.75f, 0.7f);
        }

        public static Material GetWoodMat()
        {
            return GetMat("Wood", new Color(0.48f, 0.32f, 0.18f), 0f, 0.2f);
        }

        public static Material GetLeatherMat()
        {
            return GetMat("Leather", new Color(0.36f, 0.24f, 0.14f), 0f, 0.25f);
        }

        public static Material GetLinenMat()
        {
            return GetMat("Linen", new Color(0.82f, 0.78f, 0.68f), 0f, 0.15f);
        }

        public static Material GetGoldMat()
        {
            return GetMat("Gold", new Color(0.95f, 0.75f, 0.15f), 0.85f, 0.8f);
        }

        public static Material GetHorseMat()
        {
            return GetMat("Horse", new Color(0.32f, 0.18f, 0.10f), 0f, 0.3f);
        }

        private static GameObject CreatePart(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (rot.HasValue) go.transform.localRotation = rot.Value;

            var ren = go.GetComponent<Renderer>();
            if (ren != null && mat != null) ren.material = mat;

            return go;
        }

        /// <summary>
        /// Birimin türüne ve takımına göre eksiksiz 3D modelini oluşturur.
        /// </summary>
        public static GameObject BuildModel(Transform parent, string unitType, UnitTeam team)
        {
            var modelRoot = new GameObject("VisualModel");
            modelRoot.transform.SetParent(parent, false);
            modelRoot.transform.localPosition = Vector3.zero;
            modelRoot.transform.localRotation = Quaternion.identity;

            string t = unitType.ToLower();

            if (TryBuildArticulatedUnit(modelRoot.transform, t, team))
            {
                return modelRoot;
            }

            if (t == "trebuchet")
            {
                BuildTrebuchet(modelRoot.transform, team);
            }
            else if (t == "cavalry" || t == "scout")
            {
                BuildCavalry(modelRoot.transform, team);
            }
            else if (t == "archer" || t == "crossbowman")
            {
                BuildArcher(modelRoot.transform, team);
            }
            else if (t == "villager")
            {
                BuildVillager(modelRoot.transform, team);
            }
            else // spearman, swordsman vb.
            {
                BuildSpearman(modelRoot.transform, team);
            }

            return modelRoot;
        }

        #region 1. Mızrakçı / Muhafız (Spearman)

        private static void BuildSpearman(Transform root, UnitTeam team)
        {
            var teamMat = GetTeamMat(team);
            var steelMat = GetSteelMat();
            var leatherMat = GetLeatherMat();
            var woodMat = GetWoodMat();

            // 1. Gövde & Zırh (Zincir zırh + Takım tuniği)
            CreatePart(PrimitiveType.Capsule, root, new Vector3(0, 0.85f, 0), new Vector3(0.55f, 0.65f, 0.45f), teamMat);
            // Zırh göğüslüğü
            CreatePart(PrimitiveType.Cube, root, new Vector3(0, 0.95f, 0), new Vector3(0.58f, 0.45f, 0.48f), steelMat);

            // 2. Kafa & Çelik Miğfer (Kettle Hat)
            CreatePart(PrimitiveType.Sphere, root, new Vector3(0, 1.45f, 0), new Vector3(0.35f, 0.38f, 0.35f), steelMat);
            // Miğfer siperliği (Geniş çelik kenar)
            CreatePart(PrimitiveType.Cylinder, root, new Vector3(0, 1.42f, 0), new Vector3(0.48f, 0.04f, 0.48f), steelMat);
            // Miğfer sorgucu (Takım tüyleri)
            CreatePart(PrimitiveType.Cylinder, root, new Vector3(0, 1.68f, -0.05f), new Vector3(0.08f, 0.22f, 0.08f), teamMat, Quaternion.Euler(20f, 0, 0));

            // 3. Bacaklar & Çizmeler
            CreatePart(PrimitiveType.Cylinder, root, new Vector3(-0.16f, 0.35f, 0), new Vector3(0.18f, 0.4f, 0.18f), leatherMat);
            CreatePart(PrimitiveType.Cylinder, root, new Vector3( 0.16f, 0.35f, 0), new Vector3(0.18f, 0.4f, 0.18f), leatherMat);

            // 4. Sol Kol: Ortaçağ Kalkanı (Heater Shield)
            var shieldPivot = new GameObject("Shield_Pivot");
            shieldPivot.transform.SetParent(root, false);
            shieldPivot.transform.localPosition = new Vector3(-0.38f, 0.95f, 0.1f);
            shieldPivot.transform.localRotation = Quaternion.Euler(0, 25f, 0);

            // Kalkan gövdesi (Takım rengi)
            CreatePart(PrimitiveType.Cube, shieldPivot.transform, Vector3.zero, new Vector3(0.08f, 0.72f, 0.48f), teamMat);
            // Kalkan çelik göbeği (Umbo)
            CreatePart(PrimitiveType.Sphere, shieldPivot.transform, new Vector3(-0.05f, 0, 0), new Vector3(0.18f, 0.18f, 0.18f), steelMat);

            // 5. Sağ Kol: 2.4 Metre Yüksekliğinde Heybetli Mızrak (Pike / Spear)
            var spearPivot = new GameObject("Spear_Pivot");
            spearPivot.transform.SetParent(root, false);
            spearPivot.transform.localPosition = new Vector3(0.36f, 0.85f, 0.15f);
            spearPivot.transform.localRotation = Quaternion.Euler(15f, 0, 0); // Hafif öne meyilli asil duruş

            // Mızrak ahşap şaftı (2.4m)
            CreatePart(PrimitiveType.Cylinder, spearPivot.transform, new Vector3(0, 0.4f, 0), new Vector3(0.05f, 1.2f, 0.05f), woodMat);
            // Mızrak çelik ucu (Pike blade)
            CreatePart(PrimitiveType.Cylinder, spearPivot.transform, new Vector3(0, 1.7f, 0), new Vector3(0.08f, 0.35f, 0.03f), steelMat);
        }

        #endregion

        #region 2. Okçu / Nişancı (Archer)

        private static void BuildArcher(Transform root, UnitTeam team)
        {
            var teamMat = GetTeamMat(team);
            var leatherMat = GetLeatherMat();
            var woodMat = GetWoodMat();
            var linenMat = GetLinenMat();

            // 1. Çevik Gövde (Deri zırh + Takım kuşağı)
            CreatePart(PrimitiveType.Capsule, root, new Vector3(0, 0.82f, 0), new Vector3(0.48f, 0.62f, 0.42f), leatherMat);
            CreatePart(PrimitiveType.Cube, root, new Vector3(0, 0.80f, 0), new Vector3(0.50f, 0.20f, 0.45f), teamMat);

            // 2. Kafa & Okçu Başlığı (Hood / Coif)
            CreatePart(PrimitiveType.Sphere, root, new Vector3(0, 1.40f, 0), new Vector3(0.34f, 0.36f, 0.34f), linenMat);
            // Deri siperlik/kukuleta
            CreatePart(PrimitiveType.Sphere, root, new Vector3(0, 1.52f, -0.06f), new Vector3(0.30f, 0.22f, 0.32f), leatherMat);

            // 3. Bacaklar & Hafif Avcı Çizmeleri
            CreatePart(PrimitiveType.Cylinder, root, new Vector3(-0.14f, 0.35f, 0), new Vector3(0.16f, 0.4f, 0.16f), leatherMat);
            CreatePart(PrimitiveType.Cylinder, root, new Vector3( 0.14f, 0.35f, 0), new Vector3(0.16f, 0.4f, 0.16f), leatherMat);

            // 4. Sırtta Sadak (Quiver with Arrows)
            var quiver = CreatePart(PrimitiveType.Cylinder, root, new Vector3(0.15f, 1.0f, -0.25f), new Vector3(0.16f, 0.45f, 0.16f), leatherMat, Quaternion.Euler(25f, 0, 15f));
            // Ok tüyleri (Fletching)
            CreatePart(PrimitiveType.Cube, quiver.transform, new Vector3(0, 0.5f, 0), new Vector3(0.12f, 0.2f, 0.12f), GetMat("Fletch", Color.white));

            // 5. Sol Kol: Klasik Ortaçağ Porsuk Ağacı Yayı (Longbow)
            var bowPivot = new GameObject("Bow_Pivot");
            bowPivot.transform.SetParent(root, false);
            bowPivot.transform.localPosition = new Vector3(-0.35f, 0.90f, 0.25f);
            bowPivot.transform.localRotation = Quaternion.Euler(0, -20f, 10f);

            // Yay gövdesi (Yay şeklinde 3 parçalı kavis)
            CreatePart(PrimitiveType.Cylinder, bowPivot.transform, new Vector3(0, 0, 0), new Vector3(0.04f, 0.5f, 0.04f), woodMat);
            CreatePart(PrimitiveType.Cylinder, bowPivot.transform, new Vector3(0,  0.45f, -0.1f), new Vector3(0.035f, 0.25f, 0.035f), woodMat, Quaternion.Euler(-25f, 0, 0));
            CreatePart(PrimitiveType.Cylinder, bowPivot.transform, new Vector3(0, -0.45f, -0.1f), new Vector3(0.035f, 0.25f, 0.035f), woodMat, Quaternion.Euler( 25f, 0, 0));
        }

        #endregion

        #region 3. Süvari (Cavalry / Atlı Şövalye)

        private static void BuildCavalry(Transform root, UnitTeam team)
        {
            var teamMat = GetTeamMat(team);
            var horseMat = GetHorseMat();
            var leatherMat = GetLeatherMat();
            var steelMat = GetSteelMat();

            // --- A. 3D ORTAÇAĞ SAVAŞ ATI (WARHORSE) ---
            var horseRoot = new GameObject("Warhorse");
            horseRoot.transform.SetParent(root, false);
            horseRoot.transform.localPosition = Vector3.zero;

            // 1. At Gövdesi
            CreatePart(PrimitiveType.Capsule, horseRoot.transform, new Vector3(0, 0.95f, 0), new Vector3(0.75f, 1.25f, 0.85f), horseMat, Quaternion.Euler(90f, 0, 0));
            // Takım Rengi At Zırhı / Örtüsü (Caparison Barding)
            CreatePart(PrimitiveType.Cube, horseRoot.transform, new Vector3(0, 0.92f, 0), new Vector3(0.82f, 0.55f, 1.35f), teamMat);

            // 2. At Boynu ve Başı
            CreatePart(PrimitiveType.Capsule, horseRoot.transform, new Vector3(0, 1.45f, 0.75f), new Vector3(0.42f, 0.65f, 0.45f), horseMat, Quaternion.Euler(40f, 0, 0));
            CreatePart(PrimitiveType.Cube, horseRoot.transform, new Vector3(0, 1.72f, 1.05f), new Vector3(0.35f, 0.38f, 0.65f), horseMat, Quaternion.Euler(15f, 0, 0));

            // 3. Kulaklar & Kuyruk
            CreatePart(PrimitiveType.Cube, horseRoot.transform, new Vector3(-0.12f, 2.0f, 0.95f), new Vector3(0.06f, 0.16f, 0.06f), horseMat, Quaternion.Euler(15f, -10f, 0));
            CreatePart(PrimitiveType.Cube, horseRoot.transform, new Vector3( 0.12f, 2.0f, 0.95f), new Vector3(0.06f, 0.16f, 0.06f), horseMat, Quaternion.Euler(15f,  10f, 0));
            CreatePart(PrimitiveType.Cylinder, horseRoot.transform, new Vector3(0, 0.9f, -0.95f), new Vector3(0.12f, 0.6f, 0.12f), GetMat("Mane", Color.black), Quaternion.Euler(-30f, 0, 0));

            // 4. 4 At Bacağı (Güçlü toynaklar)
            Vector3[] legPositions = new Vector3[]
            {
                new Vector3(-0.32f, 0.45f,  0.55f),
                new Vector3( 0.32f, 0.45f,  0.55f),
                new Vector3(-0.32f, 0.45f, -0.55f),
                new Vector3( 0.32f, 0.45f, -0.55f)
            };
            foreach (var legPos in legPositions)
            {
                CreatePart(PrimitiveType.Cylinder, horseRoot.transform, legPos, new Vector3(0.16f, 0.5f, 0.16f), horseMat);
                CreatePart(PrimitiveType.Cylinder, horseRoot.transform, legPos - new Vector3(0, 0.42f, 0), new Vector3(0.18f, 0.08f, 0.18f), GetMat("Hoof", Color.black));
            }

            // --- B. ATIN ÜSTÜNDEKİ ŞÖVALYE SÜVARİ BİNİCİ ---
            var riderRoot = new GameObject("Knight_Rider");
            riderRoot.transform.SetParent(root, false);
            riderRoot.transform.localPosition = new Vector3(0, 1.25f, -0.1f); // Eyerin üstünde oturma

            // Binici Gövde & Çelik Göğüslük
            CreatePart(PrimitiveType.Capsule, riderRoot.transform, new Vector3(0, 0.55f, 0), new Vector3(0.52f, 0.55f, 0.45f), teamMat);
            CreatePart(PrimitiveType.Cube, riderRoot.transform, new Vector3(0, 0.62f, 0), new Vector3(0.55f, 0.38f, 0.48f), steelMat);

            // Binici Kafa & Parlak Çelik Şövalye Miğferi
            CreatePart(PrimitiveType.Sphere, riderRoot.transform, new Vector3(0, 1.05f, 0), new Vector3(0.36f, 0.38f, 0.36f), steelMat);
            CreatePart(PrimitiveType.Cube, riderRoot.transform, new Vector3(0, 1.28f, -0.05f), new Vector3(0.1f, 0.3f, 0.15f), teamMat); // Miğfer tüylüğü

            // Süvari Kılıcı (Cavalry Saber / Broadsword)
            var swordPivot = new GameObject("Saber_Pivot");
            swordPivot.transform.SetParent(riderRoot.transform, false);
            swordPivot.transform.localPosition = new Vector3(0.38f, 0.55f, 0.25f);
            swordPivot.transform.localRotation = Quaternion.Euler(45f, 0, 0); // İleri hücum kılıç tutuşu

            CreatePart(PrimitiveType.Cube, swordPivot.transform, new Vector3(0, 0.45f, 0), new Vector3(0.08f, 0.9f, 0.03f), steelMat);
            CreatePart(PrimitiveType.Cube, swordPivot.transform, Vector3.zero, new Vector3(0.24f, 0.04f, 0.06f), GetGoldMat());
        }

        #endregion

        #region 4. Köylü / İşçi (Villager)

        private static void BuildVillager(Transform root, UnitTeam team)
        {
            var teamMat = GetTeamMat(team);
            var linenMat = GetLinenMat();
            var leatherMat = GetLeatherMat();
            var woodMat = GetWoodMat();
            var steelMat = GetSteelMat();

            // 1. Köylü Gövdesi (Keten gömlek + Deri önlük + Takım kuşağı)
            CreatePart(PrimitiveType.Capsule, root, new Vector3(0, 0.78f, 0), new Vector3(0.50f, 0.60f, 0.45f), linenMat);
            CreatePart(PrimitiveType.Cube, root, new Vector3(0, 0.75f, 0.10f), new Vector3(0.42f, 0.45f, 0.30f), leatherMat); // İş önlüğü
            CreatePart(PrimitiveType.Cube, root, new Vector3(0, 0.65f, 0), new Vector3(0.52f, 0.12f, 0.47f), teamMat); // Takım kuşağı

            // 2. Kafa & Çiftçi Kasketi
            CreatePart(PrimitiveType.Sphere, root, new Vector3(0, 1.35f, 0), new Vector3(0.34f, 0.35f, 0.34f), GetMat("Skin", new Color(0.92f, 0.76f, 0.64f)));
            CreatePart(PrimitiveType.Cylinder, root, new Vector3(0, 1.48f, 0), new Vector3(0.44f, 0.08f, 0.44f), linenMat);

            // 3. Bacaklar & Dayanıklı Çamur Çizmeleri
            CreatePart(PrimitiveType.Cylinder, root, new Vector3(-0.15f, 0.32f, 0), new Vector3(0.17f, 0.38f, 0.17f), leatherMat);
            CreatePart(PrimitiveType.Cylinder, root, new Vector3( 0.15f, 0.32f, 0), new Vector3(0.17f, 0.38f, 0.17f), leatherMat);

            // 4. Sırtta Ahşap Kaynak Sepeti (Resource Backpack Basket)
            var basket = CreatePart(PrimitiveType.Cube, root, new Vector3(0, 0.85f, -0.28f), new Vector3(0.42f, 0.45f, 0.28f), woodMat);
            // Sepet içindeki altın ve odun parçaları
            CreatePart(PrimitiveType.Sphere, basket.transform, new Vector3(-0.1f, 0.25f, 0), new Vector3(0.2f, 0.2f, 0.2f), GetGoldMat());
            CreatePart(PrimitiveType.Cylinder, basket.transform, new Vector3(0.1f, 0.3f, 0), new Vector3(0.12f, 0.35f, 0.12f), woodMat, Quaternion.Euler(15f, 0, 10f));

            // 5. Elinde Oduncu Baltası / İnşaat Çekici
            var toolPivot = new GameObject("Tool_Pivot");
            toolPivot.transform.SetParent(root, false);
            toolPivot.transform.localPosition = new Vector3(0.32f, 0.75f, 0.18f);
            toolPivot.transform.localRotation = Quaternion.Euler(20f, 0, 0);

            // Balta ahşap sapı
            CreatePart(PrimitiveType.Cylinder, toolPivot.transform, new Vector3(0, 0.15f, 0), new Vector3(0.04f, 0.65f, 0.04f), woodMat);
            // Balta demir başı
            CreatePart(PrimitiveType.Cube, toolPivot.transform, new Vector3(0, 0.45f, 0.08f), new Vector3(0.05f, 0.18f, 0.22f), steelMat);
        }

        #endregion

        #region 5. Kuşatma Silahı (Trebuchet / Mancınık)

        private static void BuildTrebuchet(Transform root, UnitTeam team)
        {
            var woodMat = GetWoodMat();
            var steelMat = GetSteelMat();
            var teamMat = GetTeamMat(team);

            // Kenney FBX prefab varsa onu kullan
            var trebuchetPrefab = Resources.Load<GameObject>("Models/siege-trebuchet") 
                               ?? Resources.Load<GameObject>("Models/Trebuchet");

            if (trebuchetPrefab != null)
            {
                var go = Object.Instantiate(trebuchetPrefab, root);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one * 1.5f;

                foreach (var col in go.GetComponentsInChildren<Collider>(true))
                {
                    Object.DestroyImmediate(col);
                }

                // Kenney siege-trebuchet modelinin uzun ekseni X'teyse ileri yön (+Z) ile hizalamak için 90 derece döndür
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                if (renderers != null && renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                    if (b.size.x > b.size.z * 1.15f)
                    {
                        go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    }
                    // Zemine tam oturması için alt sınırı Y=0'a hizala
                    b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                    float minLocalY = b.min.y - root.position.y;
                    if (Mathf.Abs(minLocalY) > 0.02f)
                    {
                        go.transform.localPosition = new Vector3(0f, -minLocalY, 0f);
                    }
                }
                return;
            }

            // Prosedürel Ağır Ortaçağ Mancınığı (Ahşap Tekerlekli Araba & Fırlatma Kolu)
            // Taban Şasi
            CreatePart(PrimitiveType.Cube, root, new Vector3(0, 0.5f, 0), new Vector3(1.8f, 0.25f, 2.8f), woodMat);

            // 4 Ahşap Tekerlek
            Vector3[] wheelPositions = new Vector3[]
            {
                new Vector3(-0.95f, 0.4f,  1.1f),
                new Vector3( 0.95f, 0.4f,  1.1f),
                new Vector3(-0.95f, 0.4f, -1.1f),
                new Vector3( 0.95f, 0.4f, -1.1f)
            };
            foreach (var wp in wheelPositions)
            {
                CreatePart(PrimitiveType.Cylinder, root, wp, new Vector3(0.8f, 0.12f, 0.8f), woodMat, Quaternion.Euler(0, 0, 90f));
            }

            // A-Tipi Dikey Taşıyıcı Kirişler
            CreatePart(PrimitiveType.Cube, root, new Vector3(-0.7f, 1.8f, 0), new Vector3(0.18f, 2.4f, 0.22f), woodMat, Quaternion.Euler(0, 0, -10f));
            CreatePart(PrimitiveType.Cube, root, new Vector3( 0.7f, 1.8f, 0), new Vector3(0.18f, 2.4f, 0.22f), woodMat, Quaternion.Euler(0, 0,  10f));

            // Fırlatma Kolu (Throwing Beam)
            CreatePart(PrimitiveType.Cube, root, new Vector3(0, 2.6f, 0.2f), new Vector3(0.16f, 0.2f, 3.8f), woodMat, Quaternion.Euler(-25f, 0, 0));

            // Karşı Ağırlık Sandığı (Counterweight Box)
            CreatePart(PrimitiveType.Cube, root, new Vector3(0, 2.1f, -1.4f), new Vector3(0.9f, 0.9f, 0.9f), steelMat);

            // Takım Sancağı
            CreatePart(PrimitiveType.Cube, root, new Vector3(0, 3.6f, -1.4f), new Vector3(0.1f, 0.5f, 0.8f), teamMat);
        }

        #endregion
    }
}
