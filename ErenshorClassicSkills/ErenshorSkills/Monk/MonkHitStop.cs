using System.Collections.Generic;
using UnityEngine;

namespace ErenshorSkills.Monk
{
    /// <summary>
    /// Micro time-freeze on heavy contact. Camera shake already told the player
    /// something landed, but shake alone reads as noise; the weight of a strike
    /// comes from the brief hold where attacker and target both stop moving at
    /// the contact frame.
    ///
    /// This deliberately does not touch <c>Time.timeScale</c>. Erenshor runs sim
    /// players, pets, and timed native systems in the same world, and slowing
    /// global time for a player's punch would drag all of them. Only the two
    /// animators involved in the hit are held, so the effect is local to the
    /// exchange and cannot desynchronize anything else.
    /// </summary>
    internal static class MonkHitStop
    {
        internal static int AppliedCount { get; private set; }
        internal static string LastDescription { get; private set; } = "none";

        /// <summary>
        /// Hold in seconds per technique. Only strikes with real mass get one;
        /// fast jabs and pressure-point taps stay crisp because freezing a light
        /// strike makes it feel sluggish rather than heavy.
        /// </summary>
        private static float HoldSeconds(string key, float tier)
        {
            switch (key)
            {
                case "dragon_punch": return 0.075f;
                case "skybreaker_rush": return 0.072f;
                case "whirlwind_kick": return 0.065f;
                case "vaulting_tempest": return 0.062f;
                case "flying_kick": return 0.058f;
                case "fist_of_falling_rain": return 0.055f;
                case "sweeping_crescent": return 0.052f;
                case "gale_splitting_palm": return 0.050f;
                case "shoulder_check": return 0.048f;
                case "quivering_palm": return 0.046f;
                case "crescent_heel": return 0.044f;
                case "round_kick": return 0.042f;
                case "pummel": return 0.038f;
                case "hundred_fists": return 0.034f;
                default: return 0f;
            }
        }

        internal static void Apply(MonkTechniqueDefinition definition,
            Transform caster, Transform target)
        {
            if (definition == null || caster == null ||
                !definition.IsOffensive) return;
            float tier = Mathf.Clamp01((definition.Level - 1f) / 34f);
            float hold = HoldSeconds(definition.Key, tier);
            if (hold <= 0f) return;
            // Higher-tier versions of the same technique hit harder.
            hold *= Mathf.Lerp(0.86f, 1.16f, tier);

            // Only the target is held. The caster is mid-technique and
            // MonkAnimationDriver owns its Animator.speed for the duration of
            // the authored motion; a hold that outlives the technique would
            // restore the driver's temporary speed after the driver had already
            // restored the original, leaving the character permanently fast.
            // The receiving end is also where the weight of a hit is read.
            if (!Freeze(target, hold)) return;
            AppliedCount++;
            LastDescription = $"{definition.Key} held {hold * 1000f:0}ms " +
                "on the target";
        }

        private static bool Freeze(Transform actor, float seconds)
        {
            if (actor == null || seconds <= 0f) return false;
            Animator animator = actor.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isActiveAndEnabled) return false;
            // Never contend with an authored Monk motion on the same rig.
            if (animator.GetComponent<MonkAnimationDriver>() != null)
                return false;
            MonkHitStopHold hold =
                animator.GetComponent<MonkHitStopHold>();
            if (hold == null)
                hold = animator.gameObject.AddComponent<MonkHitStopHold>();
            hold.Begin(animator, seconds);
            return true;
        }
    }

    /// <summary>
    /// Holds one animator at near-zero speed for a fixed real-time window and
    /// always restores the speed it captured, including if the object is
    /// disabled or destroyed mid-hold.
    /// </summary>
    internal sealed class MonkHitStopHold : MonoBehaviour
    {
        private Animator _animator;
        private float _restoreSpeed = 1f;
        private float _remaining;
        private bool _holding;

        internal void Begin(Animator animator, float seconds)
        {
            if (animator == null) return;
            // A second hit landing inside an existing hold must not capture the
            // already-frozen speed as the value to restore.
            if (!_holding)
            {
                _animator = animator;
                _restoreSpeed = animator.speed;
                _holding = true;
            }
            _remaining = Mathf.Max(_remaining, seconds);
            _animator.speed = _restoreSpeed * 0.06f;
        }

        private void Update()
        {
            if (!_holding) return;
            _remaining -= Time.unscaledDeltaTime;
            if (_remaining > 0f) return;
            Release();
        }

        private void Release()
        {
            if (!_holding) return;
            _holding = false;
            _remaining = 0f;
            if (_animator != null) _animator.speed = _restoreSpeed;
        }

        private void OnDisable() => Release();

        private void OnDestroy() => Release();
    }
}
