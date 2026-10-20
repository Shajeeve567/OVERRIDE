using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Override.SeekerAssets.Tests
{
    public sealed class SeekerAssetTests
    {
        private const string ModelPath =
            "Assets/_Project/Art/Models/Seeker/Seeker_Hunter.fbx";
        private const string MaterialPath =
            "Assets/_Project/Art/Materials/Seeker_Atlas.mat";
        private const string PrefabPath =
            "Assets/_Project/Prefabs/Visuals/SeekerVisual.prefab";

        [Test]
        public void ModelImporter_IsStaticAndDoesNotImportCamerasOrLights()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;

            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
        }

        [Test]
        public void Material_UsesRequiredUrpMapsAndKeywords()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
            Assert.That(material.GetTexture("_BaseMap"), Is.Not.Null);
            Assert.That(material.GetTexture("_MetallicGlossMap"), Is.Not.Null);
            Assert.That(material.GetTexture("_BumpMap"), Is.Not.Null);
            Assert.That(material.GetTexture("_EmissionMap"), Is.Not.Null);
            Assert.That(material.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), Is.True);
            Assert.That(material.IsKeywordEnabled("_NORMALMAP"), Is.True);
            Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.True);
        }

        [Test]
        public void Prefab_HasExpectedScaleBoundsAndSharedAtlas()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers.Length, Is.EqualTo(13));

                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers.Skip(1))
                    bounds.Encapsulate(renderer.bounds);

                Assert.That(bounds.size.x, Is.EqualTo(1.95f).Within(0.02f));
                Assert.That(bounds.size.y, Is.EqualTo(1.1966f).Within(0.02f));
                Assert.That(bounds.size.z, Is.EqualTo(1.176f).Within(0.02f));

                Material atlas = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                Assert.That(renderers.SelectMany(r => r.sharedMaterials),
                    Is.All.SameAs(atlas));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

    }
}
