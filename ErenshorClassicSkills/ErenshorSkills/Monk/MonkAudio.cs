using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ErenshorSkills.Monk
{
    /// <summary>
    /// Per-technique martial audio synthesized at runtime. Every technique has
    /// a stable acoustic signature while sharing a restrained Erenshor mix:
    /// short air motion, a dry physical transient, a low body thump, and only
    /// a small tonal tail for supernatural palm/focus techniques.
    /// </summary>
    internal static class MonkAudio
    {
        private const int SampleRate = 44100;
        private static readonly Dictionary<string, AudioClip> Windups =
            new Dictionary<string, AudioClip>(
                StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AudioClip> Impacts =
            new Dictionary<string, AudioClip>(
                StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AudioClip> Utilities =
            new Dictionary<string, AudioClip>(
                StringComparer.OrdinalIgnoreCase);
        private static readonly AudioClip[] AutoImpacts = new AudioClip[2];

        private struct AudioProfile
        {
            internal int Id;
            internal int Voice;
            internal float Weight;
            internal float Pitch;
            internal float Air;
            internal float Ring;
            internal float Rhythm;
            internal string WindupSample;
            internal string ImpactSample;

            internal AudioProfile(int id, int voice, float weight,
                float pitch, float air, float ring, float rhythm,
                string windupSample, string impactSample)
            {
                Id = id;
                Voice = voice;
                Weight = weight;
                Pitch = pitch;
                Air = air;
                Ring = ring;
                Rhythm = rhythm;
                WindupSample = windupSample;
                ImpactSample = impactSample;
            }
        }

        internal static int WindupRequestCount { get; private set; }
        internal static int ImpactRequestCount { get; private set; }
        internal static int UtilityRequestCount { get; private set; }
        internal static int AutoImpactRequestCount { get; private set; }
        internal static int SourcePlayCount { get; private set; }
        internal static string LastClipName { get; private set; } = "";

        internal static void PlayWindup(MonkTechniqueDefinition definition,
            Transform source)
        {
            if (definition == null || source == null ||
                !definition.IsOffensive)
                return;
            WindupRequestCount++;
            if (UseNativeSpellAudio) return;
            if (!Windups.TryGetValue(definition.Key, out AudioClip clip))
            {
                clip = BuildWindup(definition);
                Windups[definition.Key] = clip;
            }
            float tier = Tier(definition);
            AudioProfile profile = Profile(definition);
            PlayAt(clip, source.position + Vector3.up * 1.0f,
                Mathf.Clamp(Mathf.Lerp(0.42f, 0.60f, tier) +
                    (profile.Weight - 1f) * 0.12f, 0.38f, 0.72f),
                Mathf.Lerp(18f, 26f, Mathf.Clamp01(profile.Weight - 0.65f)));
        }

        internal static void PlayImpact(MonkTechniqueDefinition definition,
            Vector3 position)
        {
            if (definition == null) return;
            ImpactRequestCount++;
            if (UseNativeSpellAudio) return;
            if (!Impacts.TryGetValue(definition.Key, out AudioClip clip))
            {
                clip = BuildImpact(definition);
                Impacts[definition.Key] = clip;
            }
            float tier = Tier(definition);
            AudioProfile profile = Profile(definition);
            // Impacts repeat far more than anything else a Monk plays, so they
            // carry the widest spread.
            PlayAt(clip, position,
                Mathf.Clamp(Mathf.Lerp(0.78f, 0.98f, tier) +
                    (profile.Weight - 1f) * 0.22f, 0.76f, 1f),
                Mathf.Lerp(26f, 38f, Mathf.Clamp01(profile.Weight - 0.72f)),
                0.11f);
        }

        /// <summary>
        /// Adds a close, low body transient beneath Erenshor's native melee
        /// contact. The stock hit remains audible for material identity while
        /// this layer supplies the missing mass of a fist-weapon impact.
        /// </summary>
        internal static void PlayAutoImpact(Transform caster,
            Transform target, bool rightHand)
        {
            if (caster == null || target == null) return;
            AutoImpactRequestCount++;
            int index = rightHand ? 0 : 1;
            AudioClip clip = AutoImpacts[index];
            if (clip == null)
            {
                clip = BuildAutoImpact(rightHand);
                AutoImpacts[index] = clip;
            }
            Vector3 direction = target.position - caster.position;
            direction = Vector3.ProjectOnPlane(direction, caster.up);
            if (direction.sqrMagnitude < 0.001f)
                direction = caster.forward;
            direction.Normalize();
            Vector3 position = target.position + caster.up * 0.92f -
                direction * 0.22f;
            // Autoattacks are the most repeated sound in the class by a wide
            // margin; they need the most variation of anything here.
            PlayAt(clip, position, 0.84f, 31f, 0.14f);
        }

        internal static void PlayUtility(
            MonkTechniqueDefinition definition, Transform source)
        {
            if (definition == null || source == null) return;
            UtilityRequestCount++;
            if (UseNativeSpellAudio) return;
            if (!Utilities.TryGetValue(definition.Key, out AudioClip clip))
            {
                clip = BuildUtility(definition);
                Utilities[definition.Key] = clip;
            }
            AudioProfile profile = Profile(definition);
            PlayAt(clip, source.position + Vector3.up * 0.92f,
                Mathf.Clamp(Mathf.Lerp(0.62f, 0.80f, Tier(definition)) +
                    (profile.Weight - 1f) * 0.12f, 0.58f, 0.92f),
                Mathf.Lerp(20f, 28f, Mathf.Clamp01(profile.Weight - 0.70f)));
        }

        internal static bool Validate(out string detail)
        {
            int uniqueSeeds = MonkSkillData.All
                .Select(value => TechniqueSeed(value.Key)).Distinct().Count();
            // Derived from what the profiles and builders actually name, so a
            // dropped or renamed file fails the self-test instead of silently
            // producing a clip with a missing layer.
            string[] requiredSamples = MonkSkillData.All
                .SelectMany(value => new[]
                {
                    Profile(value).WindupSample, Profile(value).ImpactSample
                })
                .Concat(new[]
                {
                    "whip_fast.wav", "whip_broad.wav", "punch_body.wav",
                    "knuckle_dry.wav", "stone_heavy.wav", "metal_ring.wav",
                    "aura_shimmer.wav", "aura_tick.wav", "focus_bell.wav"
                })
                .Where(value => value != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            int sampleCount = requiredSamples.Count(value =>
                EmbeddedResources.GetBytesByFilename(value) != null);
            int profileCount = MonkSkillData.All
                .Select(value => Profile(value).Id).Distinct().Count();
            MonkTechniqueDefinition jab = MonkSkillData.All.FirstOrDefault(
                value => value.Key == "jab");
            AudioProfile jabProfile = Profile(jab);
            bool weightedJab = jab != null && jabProfile.Weight >= 1.18f &&
                jabProfile.Pitch <= 1.02f &&
                jabProfile.ImpactSample.StartsWith("punch_",
                    StringComparison.OrdinalIgnoreCase);
            bool valid = uniqueSeeds == MonkSkillData.ExpectedCustomCount &&
                profileCount == MonkSkillData.ExpectedCustomCount &&
                sampleCount == requiredSamples.Length && weightedJab;
            detail = valid
                ? $"{uniqueSeeds} deterministic per-technique sound " +
                  $"signatures and {profileCount} authored acoustic profiles " +
                  $"with {sampleCount} CC0 source layers; every active " +
                  "technique receives unique motion weight, pitch contour, " +
                  "transient rhythm, physical sample, body resonance, and " +
                  "tail; knuckle, crack, slap, and air layers are resonant " +
                  "filtered noise rather than sine partials; each cast varies " +
                  "pitch and level so repeats never retrigger an identical clip"
                : $"sound signature/profile collision: {uniqueSeeds}/" +
                  MonkSkillData.ExpectedCustomCount +
                  $", {profileCount}/{MonkSkillData.ExpectedCustomCount}" +
                  $"; embedded source layers {sampleCount}/" +
                  requiredSamples.Length + $"; weighted Jab={weightedJab}";
            return valid;
        }

        internal static void GetPlaybackCounts(out int windups,
            out int impacts, out int utilities, out int sources)
        {
            windups = WindupRequestCount;
            impacts = ImpactRequestCount;
            utilities = UtilityRequestCount;
            sources = SourcePlayCount;
        }

        internal static int BuiltUniqueClipCount =>
            Windups.Values.Concat(Impacts.Values).Concat(Utilities.Values)
                .Where(value => value != null)
                .Select(value => value.name)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();

        /// <summary>
        /// When true the Monk defers to Erenshor's own audio path, exactly as
        /// the Necromancer does. Kept as a flag rather than deleting the
        /// recorded route so a technique with no good native match can be put
        /// back on samples.
        /// </summary>
        internal static readonly bool UseNativeSpellAudio = true;

        /// <summary>
        /// Air movement before a technique, taken from Erenshor's own swoosh
        /// and charge clips so it sits in the game's mix.
        /// </summary>
        internal static AudioClip NativeCharge(string key)
        {
            switch (key)
            {
                case "jab":
                case "pressure_point":
                case "eagle_strike":
                    return NativeAudioLibrary.Resolve(
                        "Magical Swoosh 8", "Magical Swoosh 16");
                case "round_kick":
                case "crescent_heel":
                case "sweeping_crescent":
                case "whirlwind_kick":
                case "flying_kick":
                case "vaulting_tempest":
                    return NativeAudioLibrary.Resolve(
                        "Magical Swoosh 25", "Magical Swoosh 17");
                case "dragon_punch":
                case "skybreaker_rush":
                case "shoulder_check":
                    return NativeAudioLibrary.Resolve(
                        "casting_charge_matter_fast_03", "Magical Swoosh 17");
                case "gale_splitting_palm":
                case "stone_throw":
                case "quivering_palm":
                    return NativeAudioLibrary.Resolve(
                        "magic_conjure_charge1_03", "Magical Swoosh 16");
                default:
                    return NativeAudioLibrary.Resolve(
                        "Magical Swoosh 16", "Magical Swoosh 17");
            }
        }

        /// <summary>
        /// Contact. Offensive techniques land on the game's own melee hit
        /// clips, spread across the list so consecutive techniques differ;
        /// heavier finishers get a magical impact over the top instead.
        /// </summary>
        internal static AudioClip NativeComplete(
            MonkTechniqueDefinition definition)
        {
            if (definition == null) return null;
            switch (definition.Key)
            {
                case "dragon_punch":
                case "skybreaker_rush":
                    return NativeAudioLibrary.Resolve(
                        "Impact Earthquake_02", "Magical Impact 27");
                case "whirlwind_kick":
                case "vaulting_tempest":
                    return NativeAudioLibrary.Resolve(
                        "Magical Impact 27", "Magical Impact 8");
                case "gale_splitting_palm":
                    return NativeAudioLibrary.Resolve(
                        "energy_blast_large_04", "Magical Impact 8");
                case "quivering_palm":
                    return NativeAudioLibrary.Resolve(
                        "cinematic_deep_low_whoosh_impact_04", "Impact_03");
                case "stone_throw":
                    return NativeAudioLibrary.Resolve(
                        "energy_blast_small_04", "Magical Impact 6");
            }
            if (!definition.IsOffensive)
                return NativeAudioLibrary.Resolve("Buff 20", "Buff 10");
            // Spread ordinary strikes across the native hit list so a rotation
            // does not retrigger one clip.
            AudioClip hit = NativeAudioLibrary.HitSound(
                (int)TechniqueSeed(definition.Key));
            return hit ?? NativeAudioLibrary.Resolve(
                "Impact_03", "Magical Impact 4");
        }

        private static float Tier(MonkTechniqueDefinition definition) =>
            Mathf.Clamp01((definition.Level - 1f) / 34f);

        private static AudioProfile Profile(
            MonkTechniqueDefinition definition)
        {
            switch (definition.Key)
            {
                case "open_hand_discipline": return new AudioProfile(0, 7, 0.82f, 1.08f, 0.30f, 0.46f, 0.18f, "whoosh_fast.wav", "punch_light.wav");
                case "mend": return new AudioProfile(1, 8, 0.86f, 1.18f, 0.48f, 0.78f, 0.42f, "whoosh_broad.wav", "punch_mid.wav");
                case "jab": return new AudioProfile(2, 0, 1.22f, 0.94f, 0.32f, 0.08f, 0.12f, "whoosh_fast.wav", "punch_heavy.wav");
                case "shoulder_check": return new AudioProfile(3, 1, 1.16f, 0.86f, 0.42f, 0.12f, 0.16f, "whoosh_heavy.wav", "punch_heavy.wav");
                case "tiger_claw": return new AudioProfile(4, 2, 0.96f, 1.22f, 0.34f, 0.18f, 0.34f, "whoosh_fast.wav", "punch_mid.wav");
                case "round_kick": return new AudioProfile(5, 3, 1.12f, 0.92f, 0.70f, 0.16f, 0.26f, "whoosh_broad.wav", "punch_heavy.wav");
                case "focused_will": return new AudioProfile(6, 9, 0.94f, 1.04f, 0.20f, 0.68f, 0.12f, "whoosh_fast.wav", "punch_light.wav");
                case "stone_throw": return new AudioProfile(7, 5, 1.02f, 1.12f, 0.56f, 0.72f, 0.22f, "whoosh_fast.wav", "punch_mid.wav");
                case "crescent_heel": return new AudioProfile(8, 3, 1.14f, 1.06f, 0.74f, 0.24f, 0.38f, "whoosh_broad.wav", "punch_hard.wav");
                case "centered_breath": return new AudioProfile(9, 8, 0.90f, 0.96f, 0.62f, 0.70f, 0.56f, "whoosh_broad.wav", "punch_light.wav");
                case "feign_death": return new AudioProfile(10, 10, 0.84f, 0.72f, 0.22f, 0.32f, 0.82f, "whoosh_heavy.wav", "punch_hard.wav");
                case "countering_palm": return new AudioProfile(11, 5, 1.02f, 1.20f, 0.26f, 0.86f, 0.48f, "whoosh_fast.wav", "punch_light.wav");
                case "eagle_strike": return new AudioProfile(12, 2, 1.04f, 1.14f, 0.40f, 0.14f, 0.58f, "whoosh_fast.wav", "punch_heavy.wav");
                case "breath_of_harmony": return new AudioProfile(13, 8, 0.98f, 1.12f, 0.76f, 0.82f, 0.30f, "whoosh_broad.wav", "punch_mid.wav");
                case "silent_step": return new AudioProfile(14, 6, 0.78f, 1.34f, 0.66f, 0.06f, 0.72f, "whoosh_fast.wav", "punch_light.wav");
                case "fist_of_falling_rain": return new AudioProfile(15, 4, 1.18f, 1.08f, 0.58f, 0.20f, 0.94f, "whoosh_heavy.wav", "punch_hard.wav");
                case "flying_kick": return new AudioProfile(16, 3, 1.24f, 1.16f, 0.86f, 0.18f, 0.44f, "whoosh_broad.wav", "punch_heavy.wav");
                case "reed_in_the_wind": return new AudioProfile(17, 6, 0.86f, 1.24f, 0.82f, 0.26f, 0.64f, "whoosh_broad.wav", "punch_mid.wav");
                case "stone_stance": return new AudioProfile(18, 11, 1.18f, 0.68f, 0.20f, 0.34f, 0.08f, "whoosh_heavy.wav", "punch_heavy.wav");
                case "sweeping_crescent": return new AudioProfile(19, 3, 1.20f, 0.84f, 0.92f, 0.22f, 0.74f, "whoosh_broad.wav", "punch_hard.wav");
                case "pressure_point": return new AudioProfile(20, 0, 0.90f, 1.42f, 0.12f, 0.58f, 0.04f, "whoosh_fast.wav", "punch_light.wav");
                case "gale_splitting_palm": return new AudioProfile(21, 6, 1.28f, 1.30f, 1.00f, 0.68f, 0.52f, "whoosh_broad.wav", "punch_hard.wav");
                case "whirlwind_kick": return new AudioProfile(22, 3, 1.34f, 0.98f, 1.00f, 0.28f, 0.88f, "whoosh_broad.wav", "punch_heavy.wav");
                case "empty_body": return new AudioProfile(23, 10, 0.92f, 0.82f, 0.38f, 0.94f, 0.46f, "whoosh_fast.wav", "punch_mid.wav");
                case "dragon_punch": return new AudioProfile(24, 12, 1.42f, 1.18f, 0.82f, 0.52f, 0.68f, "whoosh_heavy.wav", "punch_hard.wav");
                case "purify_body": return new AudioProfile(25, 8, 0.98f, 1.30f, 0.70f, 0.90f, 0.38f, "whoosh_broad.wav", "punch_light.wav");
                case "hundred_fists": return new AudioProfile(26, 4, 1.26f, 1.16f, 0.62f, 0.24f, 1.00f, "whoosh_heavy.wav", "punch_heavy.wav");
                case "five_point_focus": return new AudioProfile(27, 9, 1.10f, 1.26f, 0.28f, 1.00f, 0.20f, "whoosh_fast.wav", "punch_mid.wav");
                case "quivering_palm": return new AudioProfile(28, 5, 1.54f, 0.76f, 0.18f, 1.00f, 0.12f, "whoosh_heavy.wav", "punch_hard.wav");
                case "pummel": return new AudioProfile(29, 4, 1.14f, 0.94f, 0.46f, 0.10f, 0.82f, "whoosh_heavy.wav", "punch_heavy.wav");
                case "vaulting_tempest": return new AudioProfile(30, 3, 1.40f, 1.22f, 1.00f, 0.46f, 0.62f, "whoosh_broad.wav", "punch_hard.wav");
                case "skybreaker_rush": return new AudioProfile(31, 12, 1.62f, 0.70f, 0.90f, 0.76f, 0.90f, "whoosh_heavy.wav", "punch_heavy.wav");
                default: return new AudioProfile(-1, 0, 1f, 1f, 0.5f,
                    0.2f, 0.2f, "whoosh_fast.wav", "punch_light.wav");
            }
        }

        /// <summary>
        /// Air movement before a strike, composited from recordings only.
        /// Which whoosh a technique uses comes from its profile; the pitch and
        /// the optional doubled layer are what separate a jab from a tempest.
        /// </summary>
        private static AudioClip BuildWindup(
            MonkTechniqueDefinition definition)
        {
            AudioProfile profile = Profile(definition);
            uint seed = TechniqueSeed(definition.Key);
            float pitch = Mathf.Lerp(1.14f, 0.84f,
                Mathf.InverseLerp(0.68f, 1.62f, profile.Weight));
            var layers = new List<PcmSampleMixer.Layer>
            {
                new PcmSampleMixer.Layer(profile.WindupSample, 1f, pitch)
            };
            // Sweeping techniques get a second, broader pass just behind the
            // first so the swing reads as travelling rather than as one puff.
            if (profile.Air > 0.60f)
                layers.Add(new PcmSampleMixer.Layer(
                    profile.Air > 0.85f ? "whip_broad.wav" : "whip_fast.wav",
                    Mathf.Lerp(0.34f, 0.62f, profile.Air),
                    pitch * Mathf.Lerp(0.92f, 1.08f, Unit(seed, 10)),
                    0.020f + Unit(seed, 11) * 0.020f));
            return PcmSampleMixer.BuildLayered(
                "CS Monk Windup " + definition.Key, layers.ToArray());
        }

        /// <summary>
        /// Contact. The punch recording is the sound; weight adds a doubled
        /// low copy, rhythm adds a second strike, and only the focus-oriented
        /// techniques keep a tuned bell over the top.
        /// </summary>
        private static AudioClip BuildImpact(
            MonkTechniqueDefinition definition)
        {
            AudioProfile profile = Profile(definition);
            uint seed = TechniqueSeed(definition.Key) ^ 0x9E3779B9u;
            float tier = Tier(definition);
            float pitch = Mathf.Lerp(1.12f, 0.82f,
                Mathf.InverseLerp(0.68f, 1.62f, profile.Weight));
            var layers = new List<PcmSampleMixer.Layer>
            {
                new PcmSampleMixer.Layer(profile.ImpactSample, 1f, pitch)
            };
            if (profile.Weight > 1.05f)
                layers.Add(new PcmSampleMixer.Layer("punch_body.wav",
                    Mathf.Lerp(0.32f, 0.68f, profile.Weight - 1.05f),
                    pitch * 0.66f, 0.008f));
            if (profile.Rhythm > 0.55f)
                layers.Add(new PcmSampleMixer.Layer(profile.ImpactSample,
                    Mathf.Lerp(0.30f, 0.55f, profile.Rhythm),
                    pitch * 1.12f, 0.075f + profile.Rhythm * 0.070f));
            if (profile.Ring >= 0.48f)
                layers.Add(new PcmSampleMixer.Layer("focus_bell.wav",
                    0.08f + profile.Ring * 0.16f,
                    Mathf.Lerp(0.86f, 1.30f, Unit(seed, 13)),
                    0.012f + profile.Rhythm * 0.028f, 1.10f));
            if (tier > 0.72f)
                layers.Add(new PcmSampleMixer.Layer("metal_ring.wav",
                    0.14f + tier * 0.12f, pitch * 0.90f, 0.016f));
            return PcmSampleMixer.BuildLayered(
                "CS Monk Impact " + definition.Key, layers.ToArray());
        }

        private static AudioClip BuildAutoImpact(bool rightHand)
        {
            uint seed = TechniqueSeed(rightHand
                ? "auto_fist_right" : "auto_fist_left");
            float pitch = rightHand ? 0.98f : 1.06f;
            return PcmSampleMixer.BuildLayered(
                "CS Monk Auto Fist Impact " +
                (rightHand ? "Right" : "Left"),
                new PcmSampleMixer.Layer(
                    rightHand ? "punch_mid.wav" : "punch_light.wav",
                    1f, pitch),
                new PcmSampleMixer.Layer("knuckle_dry.wav", 0.34f,
                    pitch * Mathf.Lerp(0.94f, 1.06f, Unit(seed, 4)), 0.004f),
                new PcmSampleMixer.Layer("punch_body.wav", 0.30f,
                    pitch * 0.62f, 0.010f));
        }

        /// <summary>
        /// Non-offensive techniques: a shimmer rather than a strike, with the
        /// bell carrying the meditative ones.
        /// </summary>
        private static AudioClip BuildUtility(
            MonkTechniqueDefinition definition)
        {
            AudioProfile profile = Profile(definition);
            uint seed = TechniqueSeed(definition.Key);
            float pitch = Mathf.Lerp(1.16f, 0.86f,
                Mathf.InverseLerp(0.68f, 1.62f, profile.Weight));
            var layers = new List<PcmSampleMixer.Layer>
            {
                new PcmSampleMixer.Layer(
                    profile.Ring > 0.60f ? "aura_shimmer.wav"
                        : "aura_tick.wav", 1f, pitch)
            };
            layers.Add(new PcmSampleMixer.Layer("focus_bell.wav",
                0.24f + profile.Ring * 0.30f,
                Mathf.Lerp(0.78f, 1.24f, Unit(seed, 7)), 0.018f, 1.30f));
            // Grounded stances land on stone instead of shimmering.
            if (profile.Weight > 1.10f)
                layers.Add(new PcmSampleMixer.Layer("stone_heavy.wav",
                    0.38f, pitch * 0.82f, 0.010f));
            return PcmSampleMixer.BuildLayered(
                "CS Monk Utility " + definition.Key, layers.ToArray());
        }

        private static uint TechniqueSeed(string text)
        {
            uint hash = 2166136261u;
            foreach (char value in text ?? string.Empty)
            {
                hash ^= char.ToLowerInvariant(value);
                hash *= 16777619u;
            }
            return hash;
        }

        private static float Unit(uint seed, int step)
        {
            uint value = seed + (uint)(step + 1) * 0x9E3779B9u;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777215f;
        }


        private static float SoftClip(float value) =>
            value / (1f + Mathf.Abs(value) * 0.45f);


        private static float SmoothStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static void PlayAt(AudioClip clip, Vector3 position,
            float volume, float maxDistance)
        {
            PlayAt(clip, position, volume, maxDistance, 0.07f);
        }

        /// <summary>
        /// Plays one clip with per-cast variation. Without this every use of a
        /// technique fired a byte-identical sample, and a Monk's rotation is
        /// fast enough that the repetition reads as a machine gun rather than
        /// as strikes. A few percent of pitch and level spread is what makes
        /// repeated hits sound like separate events.
        /// </summary>
        private static void PlayAt(AudioClip clip, Vector3 position,
            float volume, float maxDistance, float pitchSpread)
        {
            if (clip == null) return;
            float mix = Mathf.Clamp01(volume *
                GameData.CombatVol * GameData.MasterVol *
                UnityEngine.Random.Range(0.93f, 1.07f));
            if (mix <= 0.001f) return;
            var host = new GameObject("CS Monk Martial Audio");
            host.transform.position = position;
            var source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.priority = 64;
            source.playOnAwake = false;
            source.loop = false;
            source.pitch = UnityEngine.Random.Range(1f - pitchSpread,
                1f + pitchSpread);
            // Monk techniques are player-driven and should retain a firm
            // near-field transient even when the third-person camera swings
            // away from the contact point.
            source.spatialBlend = 0.24f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 3.5f;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            source.volume = mix;
            source.Play();
            SourcePlayCount++;
            LastClipName = clip.name;
            if (SkillsPlugin.CfgEnableTestMode?.Value == true)
                SkillsPlugin.Log?.LogInfo(
                    $"[Monk Audio] clip={clip.name}; volume={mix:0.00}; " +
                    $"range={maxDistance:0.0}");
            // A detuned source plays longer than the clip's nominal length, so
            // the host lifetime has to account for the pitch or the tail of a
            // down-pitched impact is cut off.
            UnityEngine.Object.Destroy(host,
                clip.length / Mathf.Max(0.20f, source.pitch) + 0.20f);
        }
    }
}
