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

        [Test]
        public void WeakPointMeshCollider_MatchesRenderedSensorCoreBounds()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                Transform source = instance.GetComponentsInChildren<Transform>(true)
                    .Single(value => value.name == "SensorCore_WeakPoint");
                Transform proxy = instance.GetComponentsInChildren<Transform>(true)
                    .Single(value => value.name == "SensorCore_WeakPoint_Collider");
                MeshCollider collider = proxy.GetComponent<MeshCollider>();
                Assert.That(collider, Is.Not.Null);
                MeshFilter filter = source.GetComponent<MeshFilter>();
                Renderer renderer = source.GetComponent<Renderer>();
                Physics.SyncTransforms();

                Assert.That(filter, Is.Not.Null);
                Assert.That(renderer, Is.Not.Null);
                Assert.That(collider.sharedMesh, Is.SameAs(filter.sharedMesh));
                Assert.That(proxy.GetComponents<MonoBehaviour>()
                    .Any(component => component.GetType().Name == "SeekerWeakPoint"), Is.True);
                MonoBehaviour references = instance.GetComponents<MonoBehaviour>()
                    .Single(component => component.GetType().Name == "SeekerVisualReferences");
                SerializedObject data = new SerializedObject(references);
                Assert.That(data.FindProperty("_sensorCore").objectReferenceValue, Is.Not.Null);
                Assert.That(data.FindProperty("_sensorHousing").objectReferenceValue, Is.Not.Null);
                Assert.That(data.FindProperty("_weakPoint").objectReferenceValue, Is.SameAs(proxy));
                Assert.That(data.FindProperty("_leftThruster").objectReferenceValue, Is.Not.Null);
                Assert.That(data.FindProperty("_rightThruster").objectReferenceValue, Is.Not.Null);
                Assert.That(collider.bounds.size.x, Is.EqualTo(renderer.bounds.size.x).Within(0.005f));
                Assert.That(collider.bounds.size.y, Is.EqualTo(renderer.bounds.size.y).Within(0.005f));
                Assert.That(collider.bounds.size.z, Is.EqualTo(renderer.bounds.size.z).Within(0.005f));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

    }
}
