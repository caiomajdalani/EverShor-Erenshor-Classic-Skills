using UnityEngine;

namespace ErenshorSkills
{
    /// <summary>
    /// Two-pole Chamberlin state-variable band-pass, shared by the Monk and
    /// Necromancer runtime synthesis.
    ///
    /// Both classes originally built their percussive and breath layers from
    /// bare sine partials. A knuckle landing on a body, or air moving through a
    /// crypt, is broadband noise shaped by resonance rather than a tone, which
    /// is why those layers read as electronic beeps under the rest of the mix.
    /// Filtering noise through a resonant band gives the same tonal centre with
    /// the texture intact.
    ///
    /// The clip builders drive their sample callback sequentially, so an
    /// instance can live in the closure and carry state across samples.
    /// </summary>
    internal sealed class ResonantBandPass
    {
        private const int SampleRate = 44100;

        private readonly float _f;
        private readonly float _q;
        private float _low;
        private float _band;

        internal ResonantBandPass(float cutoffHz, float resonance)
        {
            // This topology is stable only while f < 2 - q, so the two
            // coefficients must be clamped against each other: a cutoff that is
            // safe at high resonance diverges at low resonance. Every band
            // built in practice sits under about 2.1kHz at resonance 1.9 or
            // above, far inside the limit, but the guard means a future tuning
            // edit cannot write NaN samples straight into an AudioClip.
            _q = 1f / Mathf.Clamp(resonance, 0.7f, 20f);
            float ceiling = (2f - _q) * 0.85f;
            _f = Mathf.Min(ceiling, 2f * Mathf.Sin(Mathf.PI *
                Mathf.Clamp(cutoffHz, 20f, SampleRate / 6f) / SampleRate));
        }

        /// <summary>
        /// Advances the filter one sample. Must be called exactly once per
        /// output sample per instance; calling it twice in the same sample
        /// double-advances the state and corrupts the response.
        /// </summary>
        internal float Process(float input)
        {
            _low += _f * _band;
            float high = input - _low - _q * _band;
            _band += _f * high;
            return _band;
        }
    }
}
