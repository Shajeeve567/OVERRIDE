using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Override.Sensors.Tests
{
    public sealed class HearingSensorTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private Type _sensorType;
        private Type _noiseEventType;
        private Type _noiseKindType;
        private Type _noiseBusType;

        [SetUp]
        public void SetUp()
        {
            _sensorType = FindType("HearingSensor");
            _noiseEventType = FindType("Override.Core.NoiseEvent");
            _noiseKindType = FindType("Override.Core.NoiseKind");
            _noiseBusType = FindType("Override.Core.NoiseBus");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject value in _objects)
                if (value != null) UnityEngine.Object.DestroyImmediate(value);
            _objects.Clear();
        }

        [Test]
        public void InRangeNoise_UpdatesAllRememberedValues()
        {
            Component sensor = CreateSensor(Vector3.zero, 15f);
            object noise = CreateNoise(new Vector3(4f, 0f, 0f), 8f, "Gunshot", 12f);

            Assert.That(Process(sensor, noise), Is.True);
            Assert.That(Read<bool>(sensor, "HasHeardNoise"), Is.True);
            Assert.That(Read<Vector3>(sensor, "LastHeardPosition"), Is.EqualTo(new Vector3(4f, 0f, 0f)));
            Assert.That(Read<float>(sensor, "LastHeardTime"), Is.EqualTo(12f));
            Assert.That(Read<object>(sensor, "LastHeardKind").ToString(), Is.EqualTo("Gunshot"));
        }

        [Test]
        public void NoiseMustBeInsideBothEmitterRadiusAndListenerRange()
        {
            Component sensor = CreateSensor(Vector3.zero, 15f);

            Assert.That(Process(sensor, CreateNoise(new Vector3(8f, 0f, 0f), 5f, "Footstep", 1f)), Is.False);
            Assert.That(Process(sensor, CreateNoise(new Vector3(16f, 0f, 0f), 30f, "Gunshot", 2f)), Is.False);
            Assert.That(Read<bool>(sensor, "HasHeardNoise"), Is.False);
        }

        [Test]
        public void StaleAndInvalidEvents_DoNotOverwriteMemory()
        {
            Component sensor = CreateSensor(Vector3.zero, 15f);
            Assert.That(Process(sensor, CreateNoise(new Vector3(3f, 0f, 0f), 10f, "Door", 10f)), Is.True);

            Assert.That(Process(sensor, CreateNoise(new Vector3(2f, 0f, 0f), 10f, "Crate", 9f)), Is.False);
            Assert.That(Process(sensor, CreateNoise(Vector3.one, -1f, "Crate", 11f)), Is.False);
            Assert.That(Read<Vector3>(sensor, "LastHeardPosition"), Is.EqualTo(new Vector3(3f, 0f, 0f)));
            Assert.That(Read<float>(sensor, "LastHeardTime"), Is.EqualTo(10f));
        }

        [Test]
        public void NoiseKindMask_FiltersUnsupportedKinds()
        {
            Component sensor = CreateSensor(Vector3.zero, 15f);
            int gunshotValue = Convert.ToInt32(Enum.Parse(_noiseKindType, "Gunshot"));
            Set(sensor, "NoiseKindMask", 1 << gunshotValue);

            Assert.That(Process(sensor, CreateNoise(Vector3.right, 10f, "Footstep", 1f)), Is.False);
            Assert.That(Process(sensor, CreateNoise(Vector3.right, 10f, "Gunshot", 2f)), Is.True);
        }

        [Test]
        public void SubscriptionMethods_AreSafeAcrossDisableAndReenable()
        {
            Component sensor = CreateSensor(Vector3.zero, 15f);
            InvokeLifecycle(sensor, "OnEnable");
            Emit(CreateNoise(Vector3.right, 10f, "Door", 1f));
            Assert.That(Read<float>(sensor, "LastHeardTime"), Is.EqualTo(1f));

            InvokeLifecycle(sensor, "OnDisable");
            Emit(CreateNoise(Vector3.right * 2f, 10f, "Door", 2f));
            Assert.That(Read<float>(sensor, "LastHeardTime"), Is.EqualTo(1f));

            InvokeLifecycle(sensor, "OnEnable");
            Emit(CreateNoise(Vector3.right * 3f, 10f, "Door", 3f));
            Assert.That(Read<float>(sensor, "LastHeardTime"), Is.EqualTo(3f));
            Assert.That(Read<Vector3>(sensor, "LastHeardPosition"), Is.EqualTo(Vector3.right * 3f));
            InvokeLifecycle(sensor, "OnDisable");
        }

        [Test]
        public void Obstacle_DampensRangeWithoutErasingEarlierMemory()
        {
            Component sensor = CreateSensor(Vector3.zero, 15f);
            Set(sensor, "ObstacleLayers", (LayerMask)(1 << 0));
            Set(sensor, "ObstructedRangeMultiplier", 0.5f);

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "HearingTestWall";
            wall.transform.position = new Vector3(5f, 0f, 0f);
            wall.transform.localScale = new Vector3(0.5f, 4f, 4f);
            _objects.Add(wall);
            Physics.SyncTransforms();

            Assert.That(Process(sensor, CreateNoise(new Vector3(4f, 0f, 0f), 20f, "Gunshot", 1f)), Is.True);
            Assert.That(Process(sensor, CreateNoise(new Vector3(10f, 0f, 0f), 20f, "Gunshot", 2f)), Is.False);
            Assert.That(Read<Vector3>(sensor, "LastHeardPosition"), Is.EqualTo(new Vector3(4f, 0f, 0f)));
        }

        private Component CreateSensor(Vector3 position, float range)
        {
            var gameObject = new GameObject("HearingSensorTest");
            gameObject.transform.position = position;
            _objects.Add(gameObject);
            Component sensor = gameObject.AddComponent(_sensorType);
            Set(sensor, "HearingRange", range);
            return sensor;
        }

        private object CreateNoise(Vector3 position, float radius, string kind, float time)
        {
            object noiseKind = Enum.Parse(_noiseKindType, kind);
            return Activator.CreateInstance(_noiseEventType, position, radius, noiseKind, time);
        }

        private bool Process(Component sensor, object noise)
        {
            MethodInfo method = _sensorType.GetMethod("ProcessNoise", BindingFlags.Instance | BindingFlags.Public);
            return (bool)method.Invoke(sensor, new[] { noise });
        }

        private void Emit(object noise)
        {
            MethodInfo method = _noiseBusType.GetMethod(
                "Emit",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { _noiseEventType },
                null);
            method.Invoke(null, new[] { noise });
        }

        private static Type FindType(string fullName)
        {
            Type result = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(type => type != null);
            Assert.That(result, Is.Not.Null, "Required runtime type was not compiled: " + fullName);
            return result;
        }

        private static void Set(Component component, string property, object value)
        {
            PropertyInfo info = component.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(info, Is.Not.Null, "Missing property: " + property);
            info.SetValue(component, value);
        }

        private static void InvokeLifecycle(Component component, string methodName)
        {
            MethodInfo method = component.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Missing lifecycle method: " + methodName);
            method.Invoke(component, null);
        }

        private static T Read<T>(Component component, string property)
        {
            PropertyInfo info = component.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(info, Is.Not.Null, "Missing property: " + property);
            return (T)info.GetValue(component);
        }
    }
}
