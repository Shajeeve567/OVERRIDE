// GameAudio.cs
// Plays every sound effect (GDD Section 10): gunshots, footsteps, doors, crates, canister impacts and
// hack beeps, plus feedback sounds for hits, kills, reloads, damage, being spotted and terminals.
//
// WHY NOISEBUS: world sounds come from the same NoiseBus events the AI listens to. So what the player
// hears is exactly what the Seeker and Warden hear: a footstep you hear is a footstep that gave you away.
//
// World sounds are 3D, played from a fixed POOL of AudioSources (no objects created during play).
// Feedback sounds (hit ticks, the spotted sting) are 2D, from one source on this object.
// MUSIC: an ambient pad always plays; a tension layer fades in with the Director's alarm level,
// while a camera sees you, and fully during the final wave.
// Put it on one object in the gameplay scene.

using Override.Combat;
using Override.Core;
using Override.Director;
using Override.Hunter;
using UnityEngine;

namespace Override.Audio
{
    public class GameAudio : MonoBehaviour
    {
        [Range(0f, 1f)] [SerializeField] float masterVolume = 0.8f;
        [SerializeField] int poolSize = 24;
        [SerializeField] float spottedCooldown = 4f;   // the sting once per sighting, not twice a second
        [Range(0f, 1f)] [SerializeField] float musicVolume = 0.5f;
        [SerializeField] float tensionFade = 0.5f;      // how fast the tension layer follows (per second)

        AudioSource[] pool;
        int next;
        AudioSource ui;
        HitscanRifle rifle;
        Transform player;
        float nextSpotted;
        AudioSource tension;
        DirectorController director;
        float lastSpotted = -10f;

        void Awake()
        {
            SoundSynth.Build();

            pool = new AudioSource[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                var go = new GameObject("Sfx " + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 1f;                        // fully 3D
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.minDistance = 2f;
                src.maxDistance = 60f;                        // the level is 2.5x scale
                pool[i] = src;
            }

            ui = gameObject.AddComponent<AudioSource>();
            ui.playOnAwake = false;
            ui.spatialBlend = 0f;                             // 2D: feedback, not a place in the world

            MusicSource(SoundSynth.AmbientPad, musicVolume);
            tension = MusicSource(SoundSynth.TensionPulse, 0f);
        }

        AudioSource MusicSource(AudioClip clip, float volume)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = volume * masterVolume;
            src.Play();
            return src;
        }

        void Update()
        {
            // Tension target: the alarm (0-100), a recent camera sighting, or the final wave.
            float target = director != null ? director.Alarm / 100f : 0f;
            if (Time.time - lastSpotted < 3f) target = Mathf.Max(target, 0.7f);
            if (director != null && director.FinalWave) target = 1f;
            float now = tension.volume / Mathf.Max(0.0001f, musicVolume * masterVolume);
            now = Mathf.MoveTowards(now, target, tensionFade * Time.deltaTime);
            tension.volume = now * musicVolume * masterVolume;
        }

        void OnEnable()
        {
            NoiseBus.Emitted += OnNoise;
            GameEvents.PlayerDamaged += OnPlayerDamaged;
            GameEvents.TerminalOverridden += OnTerminalOverridden;
            CameraNetwork.Sighted += OnSpotted;
        }

        void OnDisable()
        {
            NoiseBus.Emitted -= OnNoise;
            GameEvents.PlayerDamaged -= OnPlayerDamaged;
            GameEvents.TerminalOverridden -= OnTerminalOverridden;
            CameraNetwork.Sighted -= OnSpotted;
            if (rifle != null)
            {
                rifle.Fired -= OnFired;
                rifle.ReloadStarted -= OnReload;
                rifle.DryFired -= OnDryFire;
            }
        }

        void Start()
        {
            director = FindFirstObjectByType<DirectorController>();
            var p = GameObject.FindWithTag("Player");
            if (p != null)
            {
                player = p.transform;
                rifle = p.GetComponent<HitscanRifle>();
                if (rifle != null)
                {
                    rifle.Fired += OnFired;
                    rifle.ReloadStarted += OnReload;
                    rifle.DryFired += OnDryFire;
                }
            }

            // Every Seeker gets a looping hover hum: you can hear it coming, as it hears you.
            foreach (var seeker in FindObjectsByType<HunterBody>(FindObjectsSortMode.None))
            {
                var hum = seeker.gameObject.AddComponent<AudioSource>();
                hum.clip = SoundSynth.SeekerHum;
                hum.loop = true;
                hum.spatialBlend = 1f;
                hum.minDistance = 1.5f;
                hum.maxDistance = 20f;
                hum.volume = 0.5f * masterVolume;
                hum.Play();   // stops by itself when the Seeker is disabled on death
                var s = seeker;
                s.Lunged += () => PlayAt(SoundSynth.SeekerLunge, s.transform.position, 1f, Random.Range(0.9f, 1.1f));
            }
        }

        // ---------------------------------------------------------------- world sounds (from NoiseBus)

        void OnNoise(NoiseEvent e)
        {
            switch (e.Kind)
            {
                case NoiseKind.Footstep: PlayAt(SoundSynth.Footstep, e.Position, 0.5f, Random.Range(0.85f, 1.15f)); break;
                case NoiseKind.Gunshot: PlayAt(SoundSynth.Gunshot, e.Position, 1f, Random.Range(0.95f, 1.05f)); break;
                case NoiseKind.Door: PlayAt(SoundSynth.DoorSlide, e.Position, 0.8f, 1f); break;
                case NoiseKind.Crate: PlayAt(SoundSynth.CrateScrape, e.Position, 0.7f, Random.Range(0.9f, 1.1f)); break;
                case NoiseKind.Canister: PlayAt(SoundSynth.CanisterClank, e.Position, 0.9f, Random.Range(0.9f, 1.15f)); break;
                case NoiseKind.TerminalBeep: PlayAt(SoundSynth.HackBeep, e.Position, 0.8f, 1f); break;
            }
        }

        void PlayAt(AudioClip clip, Vector3 position, float volume, float pitch)
        {
            var src = pool[next];
            next = (next + 1) % pool.Length;   // the oldest sound is cut if all are busy
            src.transform.position = position;
            src.clip = clip;
            src.volume = volume * masterVolume;
            src.pitch = pitch;
            src.Play();
        }

        // ---------------------------------------------------------------- feedback sounds

        void OnFired(ShotResult shot)
        {
            if (shot.Kill) ui.PlayOneShot(SoundSynth.KillTick, masterVolume);
            else if (shot.HitTarget) ui.PlayOneShot(SoundSynth.HitTick, (shot.WeakPoint ? 0.9f : 0.6f) * masterVolume);
        }

        void OnReload()
        {
            if (player != null) PlayAt(SoundSynth.ReloadClick, player.position + Vector3.up, 0.7f, 1f);
        }

        void OnDryFire() => ui.PlayOneShot(SoundSynth.DryClick, 0.7f * masterVolume);

        void OnPlayerDamaged(float amount) => ui.PlayOneShot(SoundSynth.Hurt, Mathf.Clamp01(0.4f + amount / 50f) * masterVolume);

        void OnTerminalOverridden(ZoneId zone) => ui.PlayOneShot(SoundSynth.TerminalDone, masterVolume);

        void OnSpotted(Vector3 at)
        {
            lastSpotted = Time.time;
            if (Time.time < nextSpotted) return;
            nextSpotted = Time.time + spottedCooldown;
            ui.PlayOneShot(SoundSynth.Spotted, 0.8f * masterVolume);
        }
    }
}
