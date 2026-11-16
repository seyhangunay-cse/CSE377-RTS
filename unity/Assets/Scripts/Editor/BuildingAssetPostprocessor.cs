#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CSE377.EditorTools
{
    /// <summary>
    /// Ensures generated architectural building .obj meshes and PBR textures
    /// in Assets/Resources/Models/Buildings & Textures/Buildings import with
    /// exact world-scale, repeat wrapping, and NormalMap flags automatically.
    /// </summary>
    public class BuildingAssetPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            string normPath = assetPath.Replace('\\', '/');
            if (!normPath.Contains("Resources/Textures/Buildings/")) return;

            var importer = assetImporter as TextureImporter;
            if (importer == null) return;

            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 4;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 1024;

            if (normPath.EndsWith("_normal.png"))
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
            }
            else
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
            }
        }

        private void OnPreprocessModel()
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            string normPath = assetPath.Replace('\\', '/');
            if (!normPath.Contains("Resources/Models/Buildings/")) return;

            var importer = assetImporter as ModelImporter;
            if (importer == null) return;

            importer.globalScale = 1.0f;
            importer.useFileScale = false;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            importer.materialSearch = ModelImporterMaterialSearch.Local;
        }
    }
}
#endif
