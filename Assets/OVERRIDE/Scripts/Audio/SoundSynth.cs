// SoundSynth.cs
// Every sound effect in the game, synthesized from simple maths when Play starts: no audio files,
// no downloads, no licences to check. Each clip is a few lines: noise, sine waves and envelopes.
//
//   noise burst + fast decay   gunshots, footsteps, impacts (the "crack" of a short broadband burst)
//   decaying inharmonic sines  metallic clanks (canister), because metal rings at unrelated frequencies
//   pure sines                 electronic beeps (terminal, UI), ARGUS feels synthetic on purpose
//   filtered noise + sweep     sliding doors and crate scrapes
//
// Clips are made once and cached. A fixed random seed makes them identical every run.

using UnityEngine;

namespace Override.Audio
{
    public static class SoundSynth
    {
        const int Rate = 44100;
        static System.Random random = new System.Random(2026);

        public static AudioClip Gunshot, Footstep, DoorSlide, DoorHinge, CrateScrape, CanisterClank,
                                HackBeep, TerminalDone, HitTick, KillTick, ReloadClick, DryClick,
                                Hurt, Spotted, SeekerHum, SeekerLunge;

        /// Builds every clip (about 5 ms in total). Safe to call more than once.
        public static void Build()
        {
            if (Gunshot != null) return;
            Gunshot = Make("Gunshot", 0.35f, t => Noise() * Env(t, 0.002f, 0.08f) * 0.9f
                                                 + Sine(t, 70f) * Env(t, 0.001f, 0.12f) * 0.8f);
            Footstep = Make("Footstep", 0.12f, t => LowNoise(0.15f) * Env(t, 0.002f, 0.035f) * 0.7f);
            DoorSlide = Make("DoorSlide", 0.7f, t => LowNoise(0.05f) * Bell(t, 0.7f) * 0.5f
                                                    + Sine(t, 55f + 40f * t) * Bell(t, 0.7f) * 0.25f);
            DoorHinge = Make("DoorHinge", 0.6f, t => Sine(t, 420f + 180f * Mathf.Sin(t * 9f)) * Bell(t, 0.6f) * 0.15f
                                                   + LowNoise(0.1f) * Bell(t, 0.6f) * 0.2f);
            CrateScrape = Make("CrateScrape", 0.5f, t => LowNoise(0.25f) * Bell(t, 0.5f) * 0.6f);
            CanisterClank = Make("CanisterClank", 0.6f, t => (Sine(t, 523f) + 0.7f * Sine(t, 1187f) + 0.5f * Sine(t, 1913f))
                                                             * Env(t, 0.001f, 0.15f) * 0.35f
                                                             + Noise() * Env(t, 0.001f, 0.01f) * 0.5f);
            HackBeep = Make("HackBeep", 0.14f, t => Sine(t, 880f) * Gate(t, 0.12f) * 0.35f);
            TerminalDone = Make("TerminalDone", 0.5f, t => Sine(t, t < 0.15f ? 660f : t < 0.3f ? 880f : 1320f)
                                                           * Gate(t, 0.48f) * 0.3f);
            HitTick = Make("HitTick", 0.05f, t => Sine(t, 2400f) * Env(t, 0.001f, 0.012f) * 0.4f);
            KillTick = Make("KillTick", 0.18f, t => (Sine(t, 1600f) * Env(t, 0.001f, 0.02f)
                                                    + Sine(t - 0.08f, 1200f) * Env(t - 0.08f, 0.001f, 0.03f)) * 0.45f);
            ReloadClick = Make("ReloadClick", 0.5f, t => Noise() * (Env(t, 0.001f, 0.008f) + Env(t - 0.35f, 0.001f, 0.01f)) * 0.6f);
            DryClick = Make("DryClick", 0.06f, t => Noise() * Env(t, 0.001f, 0.006f) * 0.5f);
            Hurt = Make("Hurt", 0.25f, t => (Sine(t, 90f) + LowNoise(0.2f) * 0.6f) * Env(t, 0.003f, 0.08f) * 0.7f);
            Spotted = Make("Spotted", 0.45f, t => Sine(t, t < 0.2f ? 740f : 554f) * Gate(t, 0.42f) * 0.3f);
            SeekerHum = Make("SeekerHum", 2f, t => (Sine(t, 70f) * 0.6f + Sine(t, 140.5f) * 0.25f)
                                                  * (0.8f + 0.2f * Mathf.Sin(t * Mathf.PI * 2f)) * 0.5f);   // 2 s loop
            SeekerLunge = Make("SeekerLunge", 0.35f, t => (LowNoise(0.4f) * 0.6f + Sine(t, 160f - 200f * t) * 0.5f)
                                                         * Bell(t, 0.35f) * 0.8f);
        }

        // ---------------------------------------------------------------- building blocks

        delegate float Wave(float t);

        static AudioClip Make(string name, float seconds, Wave wave)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            lowState = 0f;
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(wave((float)i / Rate), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Noise() => (float)(random.NextDouble() * 2.0 - 1.0);

        static float lowState;
        /// Noise through a one-pole low-pass filter: smaller k = darker, thuddier sound.
        static float LowNoise(float k)
        {
            lowState += k * (Noise() - lowState);
            return lowState * 2.5f;
        }

        static float Sine(float t, float hz) => t < 0f ? 0f : Mathf.Sin(2f * Mathf.PI * hz * t);

        /// Quick attack, exponential decay (time constant `decay` seconds).
        static float Env(float t, float attack, float decay)
        {
            if (t < 0f) return 0f;
            if (t < attack) return t / attack;
            return Mathf.Exp(-(t - attack) / decay);
        }

        /// Smooth rise and fall over the whole clip.
        static float Bell(float t, float length) => Mathf.Sin(Mathf.Clamp01(t / length) * Mathf.PI);

        /// On for `length` seconds with 5 ms fades, so beeps don't click.
        static float Gate(float t, float length) =>
            Mathf.Clamp01(t / 0.005f) * Mathf.Clamp01((length - t) / 0.005f);
    }
}
