using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Override.WardenAI;
using UnityEditor;
using UnityEngine;

namespace Override.WardenGameplay.Tests
{
    public sealed class WardenGameplayAdapterTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private Type _coverType;
        private Type _controllerType;
        private Type _visionType;
        private Type _hearingType;
        private Type _weaponType;
        private Type _healthType;
        private Type _weakPointType;

        [SetUp]
        public void SetUp()
        {
            _coverType = FindType("WardenCoverPoint");
            _controllerType = FindType("WardenGameplayController");
            _visionType = FindType("VisionSensor");
            _hearingType = FindType("HearingSensor");
            _weaponType = FindType("WardenWeapon");
            _healthType = FindType("Override.Combat.Health");
            _weakPointType = FindType("WardenWeakPoint");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject value in _objects)
                if (value != null) UnityEngine.Object.DestroyImmediate(value);
            _objects.Clear();
        }

        [Test]
        public void CoverPoint_AllowsOnlyOneOccupant()
        {
            Component cover = CreateComponent("Cover", _coverType);
            GameObject first = CreateObject("First Warden");
            GameObject second = CreateObject("Second Warden");

            Assert.That(Invoke<bool>(cover, "TryOccupy", first), Is.True);
            Assert.That(Invoke<bool>(cover, "TryOccupy", second), Is.False);
            Assert.That(Read<bool>(cover, "IsOccupied"), Is.True);

            Invoke(cover, "Release", first);
            Assert.That(Invoke<bool>(cover, "TryOccupy", second), Is.True);
        }

        [Test]
        public void CoverPoint_BuildsExpectedTacticalOption()
        {
            Component cover = CreateComponent("Cover", _coverType);
            cover.transform.position = new Vector3(3f, 0f, 4f);
            Invoke(cover, "Configure", 0.8f, true, true);

            WardenTacticalOption option = Invoke<WardenTacticalOption>(
                cover,
                "BuildOption",
                CreateObject("Claimant"),
                Vector3.zero);

            Assert.That(option.PathAvailable, Is.True);
            Assert.That(option.CoverAvailable, Is.True);
            Assert.That(option.CoverOccupied, Is.False);
            Assert.That(option.CoverQuality, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(option.ResultingDistanceToPlayer, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void HiddenPlayer_UsesConfirmedLastSeenPositionNotCurrentTransform()
        {
            GameObject warden = CreateObject("Warden");
            GameObject player = CreateObject("Player");
            player.transform.position = new Vector3(0f, 0f, 5f);

            Component vision = warden.AddComponent(_visionType);
            Component hearing = warden.AddComponent(_hearingType);
            Component weapon = warden.AddComponent(_weaponType);
            Component health = warden.AddComponent(_healthType);
            Component controller = warden.AddComponent(_controllerType);

            Set(vision, "Target", player.transform);
            Set(vision, "Range", 20f);
            Set(vision, "FieldOfView", 120f);
            Set(vision, "ObstacleLayers", (LayerMask)0);
            Physics.SyncTransforms();
            Assert.That(Invoke<bool>(vision, "CheckVisibilityNow"), Is.True);

            MethodInfo configure = _controllerType.GetMethod("Configure", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(configure, Is.Not.Null);
            configure.Invoke(controller, new object[]
            {
                vision, hearing, weapon, health, player.transform, null,
                null, null, null, null
            });

            WardenDecisionState visibleState = Invoke<WardenDecisionState>(controller, "BuildDecisionStateNow");
            Assert.That(visibleState.PlayerVisible, Is.True);
            Assert.That(visibleState.HasKnownPlayerPosition, Is.True);

            player.transform.position = new Vector3(100f, 0f, 100f);
            Physics.SyncTransforms();
            Assert.That(Invoke<bool>(vision, "CheckVisibilityNow"), Is.False);

            WardenDecisionState hiddenState = Invoke<WardenDecisionState>(controller, "BuildDecisionStateNow");
            Assert.That(hiddenState.PlayerVisible, Is.False);
            Assert.That(hiddenState.HasKnownPlayerPosition, Is.True);
            Assert.That(hiddenState.DistanceToPlayer, Is.EqualTo(5f).Within(0.05f));
        }

        [Test]
        public void WardenShot_HittingPlayerCollider_DecreasesHealth()
        {
            GameObject warden = CreateObject("Warden");
            Component weapon = warden.AddComponent(_weaponType);
            Invoke(weapon, "Configure", warden.transform, (LayerMask)(~0));
            Invoke(weapon, "Awake");

            GameObject player = CreateObject("Player");
            player.transform.position = new Vector3(0f, 0f, 5f);
            player.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);
            Component health = player.AddComponent(_healthType);
            Invoke(health, "Configure", 100f, false);

            Physics.SyncTransforms();
            Invoke(weapon, "FireShot", player.transform.position);

            Assert.That(Read<float>(health, "Current"), Is.EqualTo(92f).Within(0.001f));
        }

        [Test]
        public void WardenWeapon_WhenOwnColliderInPath_IgnoresSelfAndDamagesTarget()
        {
            GameObject warden = CreateObject("Warden");
            BoxCollider wardenCollider = warden.AddComponent<BoxCollider>();
            wardenCollider.size = new Vector3(2f, 2f, 2f);
            Component wardenHealth = warden.AddComponent(_healthType);
            Invoke(wardenHealth, "Configure", 200f, false);

            Component weapon = warden.AddComponent(_weaponType);
            Invoke(weapon, "Configure", warden.transform, (LayerMask)(~0));
            Invoke(weapon, "Awake");

            GameObject player = CreateObject("Player");
            player.transform.position = new Vector3(0f, 0f, 5f);
            player.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);
            Component playerHealth = player.AddComponent(_healthType);
            Invoke(playerHealth, "Configure", 100f, false);

            Physics.SyncTransforms();
            Invoke(weapon, "FireShot", player.transform.position);

            Assert.That(Read<float>(wardenHealth, "Current"), Is.EqualTo(200f));
            Assert.That(Read<float>(playerHealth, "Current"), Is.EqualTo(92f).Within(0.001f));
        }

        [Test]
        public void WardenWeapon_WardensUnderSharedParent_CanDamageEachOther()
        {
            GameObject group = CreateObject("Enemy Group");
            GameObject shooter = CreateObject("Shooter Warden");
            shooter.transform.SetParent(group.transform);
            shooter.AddComponent(_controllerType);

            Component weapon = shooter.AddComponent(_weaponType);
            Invoke(weapon, "Configure", shooter.transform, (LayerMask)(~0));
            Invoke(weapon, "Awake");

            GameObject target = CreateObject("Target Warden");
            target.transform.SetParent(group.transform);
            target.transform.position = new Vector3(0f, 0f, 5f);
            target.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);
            Component targetHealth = target.AddComponent(_healthType);
            Invoke(targetHealth, "Configure", 100f, false);

            Physics.SyncTransforms();
            Invoke(weapon, "FireShot", target.transform.position);

            Assert.That(Read<float>(targetHealth, "Current"), Is.EqualTo(92f).Within(0.001f));
        }

        [Test]
        public void HoldAction_WhenAmmoEmpty_TriggersReload()
        {
            GameObject warden = CreateObject("Warden");
            GameObject player = CreateObject("Player");
            player.transform.position = new Vector3(0f, 0f, 25f);

            Component vision = warden.AddComponent(_visionType);
            Component hearing = warden.AddComponent(_hearingType);
            Component weapon = warden.AddComponent(_weaponType);
            Component health = warden.AddComponent(_healthType);
            Component controller = warden.AddComponent(_controllerType);

            Set(vision, "Target", player.transform);
            Set(vision, "Range", 20f);
            Set(vision, "FieldOfView", 120f);
            Invoke(weapon, "Awake");
            Invoke(health, "Configure", 100f, false);
            Invoke(controller, "Awake");

            FieldInfo ammoField = _weaponType.GetField("_ammo", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(ammoField, Is.Not.Null);
            ammoField.SetValue(weapon, 0);
            Assert.That(Read<int>(weapon, "Ammo"), Is.EqualTo(0));
            Assert.That(Read<bool>(weapon, "IsReloading"), Is.False);

            MethodInfo configure = _controllerType.GetMethod("Configure", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(configure, Is.Not.Null);
            configure.Invoke(controller, new object[]
            {
                vision, hearing, weapon, health, player.transform, null,
                null, null, null, null
            });

            Type snapshotType = FindType("Override.Core.WorldSnapshot");
            object snapshot = Activator.CreateInstance(snapshotType);
            MethodInfo tick = _controllerType.GetMethod("Tick", BindingFlags.Instance | BindingFlags.Public);
            tick.Invoke(controller, new[] { snapshot });

            Assert.That(Read<bool>(weapon, "IsReloading"), Is.True);
        }

        [Test]
        public void NonReloadAction_WhenVisibleAmmoCannotCompleteBurst_TriggersReloadFallback()
        {
            GameObject warden = CreateObject("Warden");
            GameObject player = CreateObject("Player");
            player.transform.position = new Vector3(0f, 0f, 5f);

            Component vision = warden.AddComponent(_visionType);
            Component weapon = warden.AddComponent(_weaponType);
            Component health = warden.AddComponent(_healthType);
            Component controller = warden.AddComponent(_controllerType);
            Set(vision, "Target", player.transform);
            Set(vision, "Range", 20f);
            Set(vision, "ObstacleLayers", (LayerMask)0);
            Invoke(weapon, "Awake");
            Invoke(health, "Configure", 100f, false);
            Invoke(controller, "Awake");

            FieldInfo ammoField = _weaponType.GetField("_ammo", BindingFlags.Instance | BindingFlags.NonPublic);
            ammoField.SetValue(weapon, 2);
            Assert.That(Invoke<bool>(vision, "CheckVisibilityNow"), Is.True);

            _controllerType.GetMethod("Configure", BindingFlags.Instance | BindingFlags.Public).Invoke(
                controller,
                new object[] { vision, null, weapon, health, player.transform, null, null, null, null, null });
            Invoke(controller, "ApplyDecision", WardenAction.Flank);

            Assert.That(Read<bool>(weapon, "IsReloading"), Is.True);
        }

        [Test]
        public void WardenDeath_CancelsWeaponActionsImmediately()
        {
            GameObject warden = CreateObject("Warden");
            Component weapon = warden.AddComponent(_weaponType);
            Component health = warden.AddComponent(_healthType);
            Component controller = warden.AddComponent(_controllerType);
            Invoke(weapon, "Awake");
            Invoke(health, "Configure", 10f, false);
            Invoke(controller, "Awake");

            FieldInfo ammoField = _weaponType.GetField("_ammo", BindingFlags.Instance | BindingFlags.NonPublic);
            ammoField.SetValue(weapon, 1);

            _controllerType.GetMethod("Configure", BindingFlags.Instance | BindingFlags.Public).Invoke(
                controller,
                new object[] { null, null, weapon, health, null, null, null, null, null, null });

            Assert.That(Invoke<bool>(weapon, "StartReload"), Is.True);
            Invoke(health, "TakeDamage", 10f, Vector3.zero, Vector3.up);

            Assert.That(Read<bool>(health, "IsDead"), Is.True);
            Assert.That(Read<bool>(weapon, "IsReloading"), Is.False);
        }

        [Test]
        public void HearingNoise_WhileHiddenAndStationary_UpdatesFacingAndLastKnownPosition()
        {
            GameObject warden = CreateObject("Warden");
            warden.transform.position = Vector3.zero;
            warden.transform.rotation = Quaternion.identity;

            GameObject player = CreateObject("Player");
            player.transform.position = new Vector3(10f, 0f, 0f);

            Component vision = warden.AddComponent(_visionType);
            Component hearing = warden.AddComponent(_hearingType);
            Component weapon = warden.AddComponent(_weaponType);
            Component health = warden.AddComponent(_healthType);
            Component controller = warden.AddComponent(_controllerType);

            Set(vision, "Target", player.transform);
            Set(vision, "Range", 20f);
            Set(vision, "FieldOfView", 60f);
            Set(hearing, "HearingRange", 20f);
            Invoke(hearing, "Awake");
            Invoke(health, "Configure", 100f, false);
            Invoke(controller, "Awake");

            MethodInfo configure = _controllerType.GetMethod("Configure", BindingFlags.Instance | BindingFlags.Public);
            configure.Invoke(controller, new object[]
            {
                vision, hearing, weapon, health, player.transform, null,
                null, null, null, null
            });

            Type noiseEventType = FindType("Override.Core.NoiseEvent");
            Type noiseKindType = FindType("Override.Core.NoiseKind");
            object footstepKind = Enum.Parse(noiseKindType, "Footstep");
            object noise = Activator.CreateInstance(noiseEventType, new Vector3(10f, 0f, 0f), 12f, footstepKind, 10f);

            MethodInfo processNoise = _hearingType.GetMethod("ProcessNoise", BindingFlags.Instance | BindingFlags.Public);
            bool heard = (bool)processNoise.Invoke(hearing, new[] { noise });
            Assert.That(heard, Is.True);

            Type snapshotType = FindType("Override.Core.WorldSnapshot");
            object snapshot = Activator.CreateInstance(snapshotType);
            MethodInfo tick = _controllerType.GetMethod("Tick", BindingFlags.Instance | BindingFlags.Public);
            tick.Invoke(controller, new[] { snapshot });

            Assert.That(Read<bool>(controller, "HasKnownPosition"), Is.True);
            Assert.That(Read<Vector3>(controller, "LastKnownPosition"), Is.EqualTo(new Vector3(10f, 0f, 0f)));

            MethodInfo updateFacing = _controllerType.GetMethod("UpdateFacing", BindingFlags.Instance | BindingFlags.Public);
            updateFacing.Invoke(controller, new object[] { 1f });

            float angleAfter = Vector3.Angle(warden.transform.forward, Vector3.right);
            Assert.That(angleAfter, Is.LessThan(90f));
            Assert.That(warden.transform.forward.x, Is.GreaterThan(0.1f));
        }

        [Test]
        public void CachedPlayerHealth_WhenPlayerPrefabHasHealth_ResolvesPlayerHealth()
        {
            GameObject warden = CreateObject("Warden");
            GameObject player = CreateObject("Player");
            Component health = player.AddComponent(_healthType);
            Invoke(health, "Configure", 100f, false);

            Component controller = warden.AddComponent(_controllerType);
            MethodInfo configure = _controllerType.GetMethod("Configure", BindingFlags.Instance | BindingFlags.Public);
            configure.Invoke(controller, new object[]
            {
                null, null, null, null, player.transform, null,
                null, null, null, null
            });

            FieldInfo playerHealthField = _controllerType.GetField("_playerHealth", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(playerHealthField, Is.Not.Null);
            object resolvedHealth = playerHealthField.GetValue(controller);
            Assert.That(resolvedHealth, Is.Not.Null);
            Assert.That(resolvedHealth, Is.SameAs(health));
        }

        [Test]
        public void UpdateFacing_WhilePathIsActive_StillFacesKnownPlayerPosition()
        {
            GameObject warden = CreateObject("Warden");
            warden.transform.position = Vector3.zero;
            warden.transform.rotation = Quaternion.identity;

            Component controller = warden.AddComponent(_controllerType);
            Invoke(controller, "Awake");

            // Set known position to the right (10, 0, 0)
            FieldInfo knownPosField = _controllerType.GetField("_lastKnownPosition", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo hasKnownPosField = _controllerType.GetField("_hasKnownPosition", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo commandField = _controllerType.GetField("_command", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(knownPosField, Is.Not.Null);
            Assert.That(hasKnownPosField, Is.Not.Null);
            Assert.That(commandField, Is.Not.Null);

            knownPosField.SetValue(controller, new Vector3(10f, 0f, 0f));
            hasKnownPosField.SetValue(controller, true);

            // Simulate active movement path towards (0, 0, 10)
            object command = commandField.GetValue(controller);
            FieldInfo pathField = command.GetType().GetField("Path");
            Assert.That(pathField, Is.Not.Null);
            pathField.SetValue(command, new[] { new Vector3(0f, 0f, 10f) });

            // Call UpdateFacing with active path
            MethodInfo updateFacing = _controllerType.GetMethod("UpdateFacing", BindingFlags.Instance | BindingFlags.Public);
            updateFacing.Invoke(controller, new object[] { 1f });

            float angleAfter = Vector3.Angle(warden.transform.forward, Vector3.right);
            Assert.That(angleAfter, Is.LessThan(90f));
            Assert.That(warden.transform.forward.x, Is.GreaterThan(0.1f));
        }

        [Test]
        public void Configure_DoesNotOverrideSceneBalanceAndWiresVisionTarget()
        {
            GameObject warden = CreateObject("Warden");
            Component wardenHealth = warden.AddComponent(_healthType);
            Invoke(wardenHealth, "Configure", 100f, false);
            Component vision = warden.AddComponent(_visionType);
            Component hearing = warden.AddComponent(_hearingType);
            Set(hearing, "HearingRange", 12f);

            GameObject player = CreateObject("Player");
            Component playerHealth = player.AddComponent(_healthType);
            Invoke(playerHealth, "Configure", 100f, false);

            Component advance = CreateComponent("Advance", _coverType);
            Component flank = CreateComponent("Flank", _coverType);
            Component retreat = CreateComponent("Retreat", _coverType);

            Set(advance, "Quality", 0.5f);
            Set(flank, "Quality", 0.5f);
            Set(retreat, "Quality", 0.95f);

            Component controller = warden.AddComponent(_controllerType);
            MethodInfo configure = _controllerType.GetMethod("Configure", BindingFlags.Instance | BindingFlags.Public);
            configure.Invoke(controller, new object[]
            {
                vision, hearing, null, wardenHealth, player.transform, null,
                null, advance, flank, retreat
            });

            Assert.That(Read<float>(wardenHealth, "Max"), Is.EqualTo(100f));
            Assert.That(Read<float>(playerHealth, "Max"), Is.EqualTo(100f));
            Assert.That(Read<float>(hearing, "HearingRange"), Is.EqualTo(12f));
            Assert.That(Read<float>(advance, "Quality"), Is.EqualTo(0.5f));
            Assert.That(Read<float>(flank, "Quality"), Is.EqualTo(0.5f));
            Assert.That(Read<float>(retreat, "Quality"), Is.EqualTo(0.95f));
            Assert.That(Read<Transform>(vision, "Target"), Is.SameAs(player.transform));
        }

        [TestCase("Assets/_Project/Prefabs/WardenGameplay.prefab", 77f)]
        [TestCase("Assets/_Project/Prefabs/Visuals/WardenVisual.prefab", 39f)]
        public void WardenPrefab_WeakPointCollidersHaveSafeWorldDimensions(
            string prefabPath,
            float expectedCombinedScale)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            _objects.Add(instance);
            Physics.SyncTransforms();

            Component[] weakPoints = instance.GetComponentsInChildren(_weakPointType, true);
            Assert.That(weakPoints.Length, Is.EqualTo(2));

            Component eye = weakPoints.Single(value => Read<object>(value, "Type").ToString() == "SensorEye");
            Component rear = weakPoints.Single(value => Read<object>(value, "Type").ToString() == "RearVent");
            SphereCollider eyeCollider = eye.GetComponent<SphereCollider>();
            BoxCollider rearCollider = rear.GetComponent<BoxCollider>();

            Assert.That(eyeCollider, Is.Not.Null);
            Assert.That(rearCollider, Is.Not.Null);

            float eyeWorldRadius = eyeCollider.radius * LargestAbsoluteComponent(eye.transform.lossyScale);
            Vector3 rearWorldSize = Vector3.Scale(rearCollider.size, Absolute(rear.transform.lossyScale));
            Assert.That(eyeWorldRadius, Is.EqualTo(0.00208f * expectedCombinedScale).Within(0.002f));
            Assert.That(rearWorldSize.x, Is.EqualTo(0.00545f * expectedCombinedScale).Within(0.005f));
            Assert.That(rearWorldSize.y, Is.EqualTo(0.00468f * expectedCombinedScale).Within(0.005f));
            Assert.That(rearWorldSize.z, Is.EqualTo(0.00260f * expectedCombinedScale).Within(0.005f));

            Assert.That(eyeCollider.bounds.size.x, Is.LessThan(0.4f));
            Assert.That(rearCollider.bounds.size.magnitude, Is.LessThan(0.7f));
        }

        private Component CreateComponent(string name, Type type)
        {
            return CreateObject(name).AddComponent(type);
        }

        private GameObject CreateObject(string name)
        {
            var value = new GameObject(name);
            _objects.Add(value);
            return value;
        }

        private static Type FindType(string fullName)
        {
            Type result = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(type => type != null);
            Assert.That(result, Is.Not.Null, "Required type was not compiled: " + fullName);
            return result;
        }

        private static void Set(Component component, string propertyName, object value)
        {
            PropertyInfo property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(property, Is.Not.Null, "Missing property: " + propertyName);
            property.SetValue(component, value);
        }

        private static T Read<T>(Component component, string propertyName)
        {
            PropertyInfo property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(property, Is.Not.Null, "Missing property: " + propertyName);
            return (T)property.GetValue(component);
        }

        private static void Invoke(Component component, string methodName, params object[] arguments)
        {
            MethodInfo method = FindMethod(component.GetType(), methodName, arguments.Length);
            method.Invoke(component, arguments);
        }

        private static T Invoke<T>(Component component, string methodName, params object[] arguments)
        {
            MethodInfo method = FindMethod(component.GetType(), methodName, arguments.Length);
            return (T)method.Invoke(component, arguments);
        }

        private static MethodInfo FindMethod(Type type, string methodName, int argumentCount)
        {
            MethodInfo method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SingleOrDefault(candidate =>
                    candidate.Name == methodName && candidate.GetParameters().Length == argumentCount);
            Assert.That(method, Is.Not.Null, "Missing method: " + methodName);
            return method;
        }

        private static Vector3 Absolute(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private static float LargestAbsoluteComponent(Vector3 value)
        {
            value = Absolute(value);
            return Mathf.Max(value.x, Mathf.Max(value.y, value.z));
        }
    }
}
