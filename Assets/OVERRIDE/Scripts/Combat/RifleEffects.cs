// RifleEffects.cs
// Shot visuals (GDD "weapon feel"): muzzle flash light + particles, a tracer line, impact sparks and
// bullet holes. Everything is built in code and POOLED: created once at start and reused, so firing
// 8 shots per second allocates nothing and creates no new GameObjects (optimization evidence).

using UnityEngine;

namespace Override.Combat
{
    public class RifleEffects : MonoBehaviour
    {
        [SerializeField] HitscanRifle rifle;
        [Tooltip("Empty at the gun's barrel tip.")]
        [SerializeField] Transform muzzle;

        [Header("Muzzle flash")]
        [SerializeField] float flashTime = 0.05f;
        [SerializeField] Color flashColour = new Color(1f, 0.75f, 0.35f);

        [Header("Tracer")]
        [SerializeField] float tracerTime = 0.05f;
        [SerializeField] float tracerWidth = 0.015f;
        [SerializeField] Color tracerColour = new Color(1f, 0.9f, 0.6f, 0.8f);

        [Header("Impacts")]
        [SerializeField] int sparkPoolSize = 12;
        [SerializeField] int decalPoolSize = 40;
        [SerializeField] float decalSize = 0.08f;
        [Tooltip("Optional material for bullet holes; a dark unlit one is made if empty.")]
        [SerializeField] Material decalMaterial;

        Material effectMaterial;   // one shared unlit material that uses vertex/particle colours
        Light flashLight;
        ParticleSystem muzzleParticles;
        LineRenderer tracer;
        float flashOffTime, tracerOffTime;

        ParticleSystem[] sparks;
        int nextSpark;
        Transform[] decals;
        int nextDecal;

        void Awake()
        {
            effectMaterial = new Material(Shader.Find("Sprites/Default"));
            if (decalMaterial == null)
            {
                decalMaterial = new Material(effectMaterial) { color = new Color(0.04f, 0.04f, 0.04f, 0.9f) };
            }

            BuildMuzzleFlash();
            BuildTracer();
            BuildSparkPool();
            BuildDecalPool();
        }

        void OnEnable() { if (rifle != null) rifle.Fired += OnFired; }
        void OnDisable() { if (rifle != null) rifle.Fired -= OnFired; }

        void OnFired(ShotResult shot)
        {
            // Flash
            flashLight.enabled = true;
            flashOffTime = Time.time + flashTime;
            muzzleParticles.Play();

            // Tracer from the barrel to where the bullet landed
            tracer.SetPosition(0, muzzle.position);
            tracer.SetPosition(1, shot.Point);
            tracer.enabled = true;
            tracerOffTime = Time.time + tracerTime;

            if (!shot.Hit) return;
            PlaySpark(shot.Point, shot.Normal);
            if (shot.HitStatic) PlaceDecal(shot.Point, shot.Normal);
        }

        void Update()
        {
            if (flashLight.enabled && Time.time >= flashOffTime) flashLight.enabled = false;
            if (tracer.enabled && Time.time >= tracerOffTime) tracer.enabled = false;
        }

        // ---------------------------------------------------------------- building (once)

        void BuildMuzzleFlash()
        {
            flashLight = muzzle.gameObject.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.range = 4f;
            flashLight.intensity = 3f;
            flashLight.color = flashColour;
            flashLight.shadows = LightShadows.None;
            flashLight.enabled = false;

            muzzleParticles = CreateBurst("MuzzleFlash", muzzle, 6, 0.04f, new Vector2(1f, 3f),
                                          new Vector2(0.03f, 0.06f), 15f, flashColour, 0f);
            muzzleParticles.transform.localPosition = Vector3.zero;
            muzzleParticles.transform.localRotation = Quaternion.identity;
            var main = muzzleParticles.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;   // stays on the barrel
        }

        void BuildTracer()
        {
            var go = new GameObject("Tracer");
            go.transform.SetParent(transform, false);
            tracer = go.AddComponent<LineRenderer>();
            tracer.positionCount = 2;
            tracer.useWorldSpace = true;
            tracer.widthMultiplier = tracerWidth;
            tracer.sharedMaterial = effectMaterial;
            tracer.startColor = tracerColour;
            tracer.endColor = tracerColour;
            tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tracer.receiveShadows = false;
            tracer.enabled = false;
        }

        void BuildSparkPool()
        {
            sparks = new ParticleSystem[sparkPoolSize];
            for (int i = 0; i < sparkPoolSize; i++)
                sparks[i] = CreateBurst("Spark", null, 12, 0.25f, new Vector2(2f, 6f),
                                        new Vector2(0.02f, 0.05f), 35f, new Color(1f, 0.7f, 0.2f), 1f);
        }

        void BuildDecalPool()
        {
            decals = new Transform[decalPoolSize];
            var parent = new GameObject("BulletHoles").transform;
            for (int i = 0; i < decalPoolSize; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>());   // purely visual: bullets and players pass through
                quad.name = "BulletHole";
                quad.transform.SetParent(parent, false);
                quad.transform.localScale = Vector3.one * decalSize;
                var r = quad.GetComponent<MeshRenderer>();
                r.sharedMaterial = decalMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                quad.SetActive(false);
                decals[i] = quad.transform;
            }
        }

        /// A one-shot burst particle system. Lifetime and speed/size are (min, max) ranges.
        ParticleSystem CreateBurst(string name, Transform parent, short count, float lifetime,
                                   Vector2 speed, Vector2 size, float coneAngle, Color colour, float gravity)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // settings can only change while stopped

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.5f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = colour;
            main.gravityModifier = gravity;
            main.maxParticles = count * 2;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = 0.01f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = effectMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        // ---------------------------------------------------------------- using the pools

        void PlaySpark(Vector3 point, Vector3 normal)
        {
            var ps = sparks[nextSpark];
            nextSpark = (nextSpark + 1) % sparks.Length;
            ps.transform.SetPositionAndRotation(point, Quaternion.LookRotation(normal));
            ps.Clear();
            ps.Play();
        }

        void PlaceDecal(Vector3 point, Vector3 normal)
        {
            var d = decals[nextDecal];
            nextDecal = (nextDecal + 1) % decals.Length;   // the oldest hole is reused
            // A quad is visible from its -Z side, so point +Z into the wall. Lift it 3 mm to avoid flicker.
            d.SetPositionAndRotation(point + normal * 0.003f,
                                     Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
            d.gameObject.SetActive(true);
        }
    }
}
