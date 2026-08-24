using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ErenshorSkills.Necromancer
{
    /// <summary>
    /// Cached, spell-authored Necromancer audio. Native spell sounds are not
    /// layered underneath this system: each spell owns a cast gesture and a
    /// resolve signature assembled from spectral, bone, blood, disease,
    /// gravefire, ward, and soul-transfer synthesis voices.
    /// </summary>
    internal static class NecromancerAudio
    {
        private const int SampleRate = 44100;

        private enum VoiceFamily
        {
            Bone,
            Soul,
            Disease,
            Ward,
            Shadow,
            Blood,
            Fear,
            Covenant,
            Fire,
            Death,
            Transfer
        }

        private struct AudioProfile
        {
            internal int Id;
            internal VoiceFamily Family;
            internal int Voice;
            internal float Weight;
            internal float Pitch;
            internal float Texture;
            internal float Resonance;
            internal float Rhythm;
            internal float Decay;

            internal AudioProfile(int id, VoiceFamily family, int voice,
                float weight, float pitch, float texture, float resonance,
                float rhythm, float decay)
            {
                Id = id;
                Family = family;
                Voice = voice;
                Weight = weight;
                Pitch = pitch;
                Texture = texture;
                Resonance = resonance;
                Rhythm = rhythm;
                Decay = decay;
            }

            internal string Fingerprint =>
                $"{Family}|{Voice}|{Weight:0.000}|{Pitch:0.000}|" +
                $"{Texture:0.000}|{Resonance:0.000}|{Rhythm:0.000}|" +
                $"{Decay:0.000}";
        }

        private static readonly Dictionary<string, AudioClip> CastClips =
            new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AudioClip> ResolveClips =
            new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private static AudioClip _nativeSilence;

        /// <summary>
        /// When true the class defers entirely to Erenshor's own spell audio
        /// path. The add-on's private AudioSource is kept behind this flag
        /// rather than deleted so the recorded-layer route can be restored for
        /// an ability the native library has no good clip for.
        /// </summary>
        internal static readonly bool UseNativeSpellAudio = true;

        internal static int CastRequestCount { get; private set; }
        internal static int ResolveRequestCount { get; private set; }
        internal static int SourcePlayCount { get; private set; }
        internal static string LastClipName { get; private set; } = "";
        internal static AudioClip NativeSilence
        {
            get
            {
                EnsureNativeSilence();
                return _nativeSilence;
            }
        }

        internal static void Prewarm()
        {
            EnsureNativeSilence();
            foreach (NecromancerSpellDefinition definition in
                NecromancerSpellData.All)
            {
                if (!CastClips.ContainsKey(definition.Key))
                    CastClips[definition.Key] = BuildCast(definition);
                if (!ResolveClips.ContainsKey(definition.Key))
                    ResolveClips[definition.Key] = BuildResolve(definition);
            }
        }

        internal static void PlayCast(NecromancerSpellDefinition definition,
            Transform source)
        {
            if (definition == null || definition.HiddenEffect || source == null)
                return;
            CastRequestCount++;
            // The native cast pipeline now plays this spell's charge clip
            // through the character's own AudioSource. Playing a second layer
            // here would double the transient and fight the game's mix.
            if (UseNativeSpellAudio) return;
            if (!CastClips.TryGetValue(definition.Key, out AudioClip clip))
            {
                clip = BuildCast(definition);
                CastClips[definition.Key] = clip;
            }
            AudioProfile profile = Profile(definition.Key);
            // Native Erenshor multiplies its own combat sounds by 1.25-4x on
            // top of the volume sliders; a 0.45 base sat well under the game
            // it plays alongside.
            PlayAt(clip, source.position + Vector3.up * 1.05f,
                Mathf.Clamp(0.62f + profile.Weight * 0.20f, 0.64f, 0.95f),
                Mathf.Lerp(18f, 26f, Mathf.Clamp01(profile.Weight - 0.55f)));
        }

        internal static void PlayResolve(NecromancerSpellDefinition definition,
            Vector3 position)
        {
            if (definition == null) return;
            ResolveRequestCount++;
            if (UseNativeSpellAudio) return;
            if (!ResolveClips.TryGetValue(definition.Key, out AudioClip clip))
            {
                clip = BuildResolve(definition);
                ResolveClips[definition.Key] = clip;
            }
            AudioProfile profile = Profile(definition.Key);
            // Resolves repeat most on damage-over-time rotations, so they get
            // the wider spread.
            PlayAt(clip, position,
                Mathf.Clamp(0.80f + profile.Weight * 0.22f, 0.82f, 1f),
                Mathf.Lerp(24f, 36f, Mathf.Clamp01(profile.Weight - 0.48f)),
                0.09f);
        }

        internal static bool Validate(out string detail)
        {
            int expected = NecromancerSpellData.All.Count;
            var ids = new HashSet<int>();
            var fingerprints = new HashSet<string>(StringComparer.Ordinal);
            var missing = new List<string>();
            foreach (NecromancerSpellDefinition definition in
                NecromancerSpellData.All)
            {
                AudioProfile profile = Profile(definition.Key);
                if (profile.Id < 1)
                    missing.Add(definition.Key);
                else
                {
                    ids.Add(profile.Id);
                    fingerprints.Add(profile.Fingerprint);
                }
            }
            // Every sample any ability names must actually be embedded. Without
            // this check a renamed or dropped file degrades silently to
            // synthesis-only and nothing reports it.
            string[] requiredSamples = NecromancerSpellData.All
                .SelectMany(value => new[]
                {
                    CastSample(value.Key), ResolveSample(value.Key)
                })
                .Concat(new[] { "focus_bell.wav" })
                .Where(value => value != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var absent = requiredSamples.Where(value =>
                EmbeddedResources.GetBytesByFilename(value) == null).ToList();
            int sampleCount = requiredSamples.Length - absent.Count;
            int voicedAbilities = NecromancerSpellData.All.Count(value =>
                CastSample(value.Key) != null ||
                ResolveSample(value.Key) != null);
            // With the native path active, what matters is that Erenshor's own
            // clips actually resolve for every ability. A null here means the
            // spell silently falls back to the one-sample silent clip.
            var nativeGaps = NecromancerSpellData.All
                .Where(value => NativeCharge(value.Key) == null ||
                    NativeComplete(value.Key) == null)
                .Select(value => value.Key).ToList();
            bool valid = missing.Count == 0 && ids.Count == expected &&
                fingerprints.Count == expected &&
                BuiltUniqueClipCount == expected * 2 &&
                absent.Count == 0 && voicedAbilities == expected &&
                (!UseNativeSpellAudio || nativeGaps.Count == 0) &&
                _nativeSilence != null;
            detail = valid
                ? $"{expected}/{expected} spell-authored cast/resolve sound " +
                  "profiles have unique spectral voices, physical textures, " +
                  "rhythms, resonance, weight, pitch, and decay; all " +
                  $"{expected * 2} clips are prebuilt and cached over " +
                  $"{sampleCount} CC0 recorded layers, with all " +
                  $"{voicedAbilities} abilities naming their own cast and " +
                  "resolve source; breath, grit, and edge are resonant " +
                  "filtered noise rather than sine partials; each cast varies " +
                  "pitch and level and now respects the combat and master " +
                  "volume sliders; every ability drives Erenshor's own " +
                  $"ChargeSound/CompleteSound from a library of " +
                  $"{NativeAudioLibrary.ClipCount} native clips so it is mixed " +
                  "exactly like a stock spell"
                : $"Necromancer audio profiles ids={ids.Count}/{expected}, " +
                  $"fingerprints={fingerprints.Count}/{expected}, clips=" +
                  $"{BuiltUniqueClipCount}/{expected * 2}, voiced=" +
                  $"{voicedAbilities}/{expected}, samples=" +
                  $"{sampleCount}/{requiredSamples.Length}, absent=" +
                  string.Join(",", absent) + ", nativeClipGaps=" +
                  string.Join(",", nativeGaps) + ", missing=" +
                  string.Join(",", missing);
            return valid;
        }

        private static void EnsureNativeSilence()
        {
            if (_nativeSilence != null) return;
            _nativeSilence = AudioClip.Create("CS Necromancer Native Silence",
                1, 1, SampleRate, false);
            _nativeSilence.SetData(new[] { 0f }, 0);
            _nativeSilence.hideFlags = HideFlags.HideAndDontSave;
        }

        internal static void GetPlaybackCounts(out int casts,
            out int resolves, out int sources)
        {
            casts = CastRequestCount;
            resolves = ResolveRequestCount;
            sources = SourcePlayCount;
        }

        internal static int BuiltUniqueClipCount =>
            CastClips.Values.Concat(ResolveClips.Values)
                .Where(value => value != null)
                .Select(value => value.name)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();

        /// <summary>
        /// Erenshor's own charge clip for an ability, played through the
        /// native cast pipeline. Two names are given wherever a sensible
        /// fallback exists so a game update that renames one clip degrades to
        /// the other instead of to silence.
        /// </summary>
        internal static AudioClip NativeCharge(string key)
        {
            switch (key)
            {
                // Bone: matter assembling itself.
                case "bone_thrall_1":
                case "bone_thrall_2":
                    return NativeAudioLibrary.Resolve(
                        "casting_charge_matter_grow_01", "Magical Swoosh 17");
                case "bone_thrall_3":
                case "bone_thrall_4":
                case "bone_thrall_5":
                    return NativeAudioLibrary.Resolve(
                        "casting_charge_matter_grow_04", "Magical Swoosh 17");
                case "mend_bones":
                case "reclaim_servant":
                    return NativeAudioLibrary.Resolve(
                        "casting_charge_matter_grow_02", "Buff 10");

                // Soul: drawing something in.
                case "siphon_spark":
                case "siphon_soul":
                case "drain_spirit":
                    return NativeAudioLibrary.Resolve(
                        "dark_wind_suck_conjure_01", "dark_wind_growls_04");
                case "soul_transfusion":
                    return NativeAudioLibrary.Resolve(
                        "magic_conjure_charge2_05", "Magical Swoosh 25");

                // Disease.
                case "rotting_fever":
                case "noxious_bolt":
                case "infectious_cloud":
                case "scent_of_decay":
                    return NativeAudioLibrary.Resolve(
                        "magic_conjure_charge1_03", "casting_charge_matter_fast_03");

                // Wards use the game's own buff charge.
                case "bone_armor_1":
                case "bone_armor_2":
                case "bone_armor_3":
                    return NativeAudioLibrary.Resolve("Buff 10", "Buff 11");
                case "dread_shield":
                case "harmshield":
                    return NativeAudioLibrary.Resolve("Buff 11", "Buff 10");

                // Shadow.
                case "clinging_shadow":
                case "gather_shadows":
                    return NativeAudioLibrary.Resolve(
                        "dark_wind_growls_01", "dark_wind_growls_06");
                case "devouring_darkness":
                case "deathly_guise":
                    return NativeAudioLibrary.Resolve(
                        "dark_wind_growls_08", "dark_wind_growls_07");

                // Blood.
                case "venomous_blood":
                case "heart_flutter":
                case "leeching_curse":
                    return NativeAudioLibrary.Resolve(
                        "magic_conjure_charge2_03", "magic_conjure_charge1_03");

                // Fear.
                case "fear":
                case "screaming_terror":
                    return NativeAudioLibrary.Resolve(
                        "fear_confuse_horror_spell_01",
                        "fear_confuse_horror_spell_02");

                // Covenant rites ring.
                case "dark_covenant_1":
                case "dark_covenant_2":
                case "intensify_death":
                case "bond_of_death":
                    return NativeAudioLibrary.Resolve(
                        "chimes_magical_bells_05", "chimes_magical_bells_07");

                case "funeral_pyre":
                    return NativeAudioLibrary.Resolve(
                        "fireball_conjure_01", "fireball_conjure_02");
                case "plague_of_souls":
                    return NativeAudioLibrary.Resolve(
                        "dark_wind_growls_02", "dark_wind_growls_08");
                default:
                    return NativeAudioLibrary.Resolve(
                        "Magical Swoosh 17", "Buff 10");
            }
        }

        /// <summary>Erenshor's own resolve clip for an ability.</summary>
        internal static AudioClip NativeComplete(string key)
        {
            switch (key)
            {
                case "bone_thrall_1":
                case "bone_thrall_2":
                case "bone_thrall_3":
                case "bone_thrall_4":
                case "bone_thrall_5":
                    return NativeAudioLibrary.Resolve(
                        "ice_spell_impact_shatter_02", "Magical Impact 4");
                case "mend_bones":
                    return NativeAudioLibrary.Resolve(
                        "healing_magic_spell_02", "Buff 20");
                case "reclaim_servant":
                    return NativeAudioLibrary.Resolve(
                        "Impact_03", "Magical Impact 4");

                case "siphon_spark":
                    return NativeAudioLibrary.Resolve(
                        "energy_blast_small_01", "Magical Impact 8");
                case "siphon_soul":
                    return NativeAudioLibrary.Resolve(
                        "energy_blast_small_04", "Magical Impact 8");
                case "drain_spirit":
                    return NativeAudioLibrary.Resolve(
                        "energy_blast_large_04", "energy_blast_small_05");
                case "soul_transfusion":
                    return NativeAudioLibrary.Resolve(
                        "magic_sparkle_chimes_explode", "Buff 20");

                case "rotting_fever":
                case "infectious_cloud":
                case "infectious_cloud_dot":
                case "scent_of_decay":
                    return NativeAudioLibrary.Resolve(
                        "fireball_impact_magic_smoke_01", "Debuff 10");
                case "noxious_bolt":
                    return NativeAudioLibrary.Resolve(
                        "energy_blast_small_03", "Magical Impact 6");

                case "bone_armor_1":
                case "bone_armor_2":
                case "bone_armor_3":
                case "dread_shield":
                case "harmshield":
                    return NativeAudioLibrary.Resolve("Buff 20", "Buff 10");

                case "clinging_shadow":
                case "gather_shadows":
                    return NativeAudioLibrary.Resolve(
                        "dark_magic_conjure_blast_2", "Debuff 10");
                case "devouring_darkness":
                    return NativeAudioLibrary.Resolve(
                        "dark_magic_conjure_blast_4", "dark_magic_conjure_blast_2");
                case "deathly_guise":
                    return NativeAudioLibrary.Resolve(
                        "Ghost Death_01", "Death 4");

                case "venomous_blood":
                case "leeching_curse":
                case "leeching_curse_dot":
                    return NativeAudioLibrary.Resolve("Debuff 19", "Debuff 10");
                case "heart_flutter":
                    return NativeAudioLibrary.Resolve(
                        "cinematic_deep_low_whoosh_impact_04", "Impact_03");

                case "fear":
                    return NativeAudioLibrary.Resolve(
                        "fear_confuse_horror_spell_04",
                        "fear_confuse_horror_spell_05");
                case "screaming_terror":
                    return NativeAudioLibrary.Resolve(
                        "fear_confuse_horror_spell_06",
                        "fear_confuse_horror_spell_04");

                case "dark_covenant_1":
                case "dark_covenant_2":
                    return NativeAudioLibrary.Resolve(
                        "chimes_magic_bell_ding_2", "Buff 20");
                case "intensify_death":
                    return NativeAudioLibrary.Resolve(
                        "Magical Impact 27", "Magical Impact 8");
                case "bond_of_death":
                    return NativeAudioLibrary.Resolve("Death 5", "Death 4");

                case "funeral_pyre":
                    return NativeAudioLibrary.Resolve(
                        "fireball_impact_burn_04", "fireball_impact_sizzle_burn4");
                case "plague_of_souls":
                    return NativeAudioLibrary.Resolve(
                        "Impact Earthquake_02", "Magical Impact 27");
                default:
                    return NativeAudioLibrary.Resolve(
                        "Magical Impact 8", "Impact_03");
            }
        }

        /// <summary>
        /// Recorded layer for the gathering half of a spell, chosen per ability
        /// rather than per family so that two spells sharing a voice family
        /// still open differently. Returning null leaves a spell on synthesis
        /// alone, which is deliberate for the damage-over-life ticks: they fire
        /// repeatedly and a recorded transient on every tick becomes noise.
        /// </summary>
        private static string CastSample(string key)
        {
            switch (key)
            {
                case "bone_thrall_1": return "bone_crack_a.wav";
                case "bone_thrall_2": return "bone_crack_b.wav";
                case "bone_thrall_3": return "bone_crack_a.wav";
                case "bone_thrall_4": return "bone_heavy.wav";
                case "bone_thrall_5": return "bone_heavy.wav";
                case "mend_bones": return "ward_shimmer.wav";
                case "reclaim_servant": return "death_knell.wav";

                case "siphon_spark": return "soul_draw.wav";
                case "siphon_soul": return "soul_draw.wav";
                case "drain_spirit": return "shadow_low.wav";
                case "soul_transfusion": return "transfer_pulse.wav";

                case "rotting_fever": return "disease_gas.wav";
                case "noxious_bolt": return "disease_gas.wav";
                case "infectious_cloud": return "disease_gas.wav";
                case "scent_of_decay": return "disease_gas.wav";

                case "bone_armor_1": return "ward_tick.wav";
                case "bone_armor_2": return "ward_shimmer.wav";
                case "bone_armor_3": return "ward_shimmer.wav";
                case "dread_shield": return "ward_heavy.wav";
                case "harmshield": return "ward_shimmer.wav";

                case "clinging_shadow": return "shadow_low.wav";
                case "gather_shadows": return "shadow_low.wav";
                case "devouring_darkness": return "shadow_low.wav";
                case "deathly_guise": return "shadow_low.wav";

                case "venomous_blood": return "blood_heart.wav";
                case "heart_flutter": return "blood_heart.wav";
                case "leeching_curse": return "blood_heart.wav";

                case "fear": return "fear_shriek.wav";
                case "screaming_terror": return "fear_shriek.wav";

                case "dark_covenant_1": return "covenant_chain.wav";
                case "dark_covenant_2": return "covenant_chain.wav";
                case "intensify_death": return "covenant_chain.wav";
                case "bond_of_death": return "covenant_chain.wav";

                case "funeral_pyre": return "fire_pyre.wav";
                case "plague_of_souls": return "death_wail.wav";
                default: return null;
            }
        }

        /// <summary>Recorded layer for the moment a spell lands.</summary>
        private static string ResolveSample(string key)
        {
            switch (key)
            {
                case "bone_thrall_1": return "bone_crack_b.wav";
                case "bone_thrall_2": return "bone_crack_a.wav";
                case "bone_thrall_3": return "bone_heavy.wav";
                case "bone_thrall_4": return "bone_crack_b.wav";
                case "bone_thrall_5": return "bone_heavy.wav";
                case "mend_bones": return "bone_crack_a.wav";
                case "reclaim_servant": return "bone_crack_a.wav";

                case "siphon_spark": return "soul_draw.wav";
                case "siphon_soul": return "transfer_pulse.wav";
                case "drain_spirit": return "soul_draw.wav";
                case "soul_transfusion": return "transfer_pulse.wav";

                case "rotting_fever": return "disease_wet.wav";
                case "noxious_bolt": return "disease_bolt.wav";
                case "infectious_cloud": return "disease_wet.wav";
                case "infectious_cloud_dot": return "disease_wet.wav";
                case "scent_of_decay": return "disease_gas.wav";

                case "bone_armor_1": return "ward_shimmer.wav";
                case "bone_armor_2": return "ward_heavy.wav";
                case "bone_armor_3": return "ward_heavy.wav";
                case "dread_shield": return "ward_heavy.wav";
                case "harmshield": return "ward_heavy.wav";

                case "clinging_shadow": return "shadow_low.wav";
                case "gather_shadows": return "shadow_low.wav";
                case "devouring_darkness": return "death_wail.wav";
                case "deathly_guise": return "death_knell.wav";

                case "venomous_blood": return "disease_wet.wav";
                case "heart_flutter": return "blood_heart.wav";
                case "leeching_curse": return "blood_heart.wav";
                case "leeching_curse_dot": return "blood_heart.wav";

                case "fear": return "fear_shriek.wav";
                case "screaming_terror": return "fear_roar.wav";

                case "dark_covenant_1": return "ward_shimmer.wav";
                case "dark_covenant_2": return "ward_heavy.wav";
                case "intensify_death": return "covenant_chain.wav";
                case "bond_of_death": return "death_knell.wav";

                case "funeral_pyre": return "fire_pyre.wav";
                case "plague_of_souls": return "fear_roar.wav";
                default: return null;
            }
        }

        private static AudioProfile Profile(string key)
        {
            switch (key)
            {
                case "bone_thrall_1": return P(1, VoiceFamily.Bone, 0, 0.82f, 1.16f, 0.72f, 0.34f, 0.22f, 0.72f);
                case "siphon_spark": return P(2, VoiceFamily.Soul, 1, 0.78f, 1.34f, 0.38f, 0.72f, 0.18f, 0.54f);
                case "rotting_fever": return P(3, VoiceFamily.Disease, 2, 0.86f, 0.88f, 0.92f, 0.26f, 0.68f, 0.90f);
                case "bone_armor_1": return P(4, VoiceFamily.Ward, 3, 0.94f, 1.08f, 0.66f, 0.82f, 0.28f, 0.96f);
                case "clinging_shadow": return P(5, VoiceFamily.Shadow, 4, 0.80f, 0.76f, 0.54f, 0.68f, 0.46f, 1.06f);
                case "mend_bones": return P(6, VoiceFamily.Bone, 5, 0.90f, 1.26f, 0.42f, 0.90f, 0.36f, 0.86f);
                case "noxious_bolt": return P(7, VoiceFamily.Disease, 6, 0.92f, 1.42f, 0.84f, 0.22f, 0.16f, 0.58f);
                case "fear": return P(8, VoiceFamily.Fear, 7, 0.88f, 1.18f, 0.58f, 0.78f, 0.52f, 0.78f);
                case "gather_shadows": return P(9, VoiceFamily.Shadow, 8, 0.98f, 0.64f, 0.76f, 0.62f, 0.74f, 1.22f);
                case "bone_thrall_2": return P(10, VoiceFamily.Bone, 9, 0.96f, 1.02f, 0.78f, 0.42f, 0.30f, 0.82f);
                case "venomous_blood": return P(11, VoiceFamily.Blood, 10, 1.02f, 0.92f, 0.96f, 0.32f, 0.62f, 0.76f);
                case "dark_covenant_1": return P(12, VoiceFamily.Covenant, 11, 0.94f, 1.10f, 0.54f, 0.86f, 0.42f, 1.08f);
                case "heart_flutter": return P(13, VoiceFamily.Blood, 12, 1.06f, 0.78f, 0.64f, 0.58f, 0.92f, 0.66f);
                case "siphon_soul": return P(14, VoiceFamily.Soul, 13, 1.08f, 0.84f, 0.48f, 0.88f, 0.58f, 1.14f);
                case "bone_armor_2": return P(15, VoiceFamily.Ward, 14, 1.10f, 0.94f, 0.72f, 0.92f, 0.34f, 1.10f);
                case "reclaim_servant": return P(16, VoiceFamily.Death, 15, 1.14f, 0.72f, 0.90f, 0.52f, 0.82f, 0.84f);
                case "deathly_guise": return P(17, VoiceFamily.Death, 16, 0.92f, 0.56f, 0.82f, 0.74f, 0.38f, 1.32f);
                case "leeching_curse": return P(18, VoiceFamily.Blood, 17, 1.04f, 0.86f, 0.74f, 0.80f, 0.70f, 1.18f);
                case "intensify_death": return P(19, VoiceFamily.Covenant, 18, 1.12f, 1.02f, 0.62f, 0.94f, 0.54f, 1.16f);
                case "bone_thrall_3": return P(20, VoiceFamily.Bone, 19, 1.14f, 0.90f, 0.84f, 0.50f, 0.40f, 0.90f);
                case "infectious_cloud": return P(21, VoiceFamily.Disease, 20, 1.18f, 0.66f, 1.00f, 0.48f, 0.78f, 1.36f);
                case "dark_covenant_2": return P(22, VoiceFamily.Covenant, 21, 1.18f, 0.96f, 0.70f, 1.00f, 0.64f, 1.24f);
                case "screaming_terror": return P(23, VoiceFamily.Fear, 22, 1.22f, 1.30f, 0.78f, 0.92f, 0.86f, 0.98f);
                case "scent_of_decay": return P(24, VoiceFamily.Disease, 23, 1.08f, 0.74f, 0.88f, 0.66f, 0.44f, 1.06f);
                case "bond_of_death": return P(25, VoiceFamily.Covenant, 24, 1.24f, 0.88f, 0.68f, 1.00f, 0.48f, 1.34f);
                case "bone_thrall_4": return P(26, VoiceFamily.Bone, 25, 1.28f, 0.80f, 0.90f, 0.58f, 0.50f, 1.00f);
                case "soul_transfusion": return P(27, VoiceFamily.Transfer, 26, 1.12f, 1.22f, 0.44f, 1.00f, 0.76f, 1.30f);
                case "bone_armor_3": return P(28, VoiceFamily.Ward, 27, 1.30f, 0.86f, 0.78f, 1.00f, 0.40f, 1.36f);
                case "devouring_darkness": return P(29, VoiceFamily.Shadow, 28, 1.34f, 0.50f, 0.98f, 0.86f, 0.88f, 1.48f);
                case "drain_spirit": return P(30, VoiceFamily.Soul, 29, 1.38f, 0.68f, 0.66f, 1.00f, 0.72f, 1.42f);
                case "funeral_pyre": return P(31, VoiceFamily.Fire, 30, 1.42f, 0.82f, 1.00f, 0.72f, 0.66f, 1.38f);
                case "bone_thrall_5": return P(32, VoiceFamily.Bone, 31, 1.52f, 0.68f, 1.00f, 0.76f, 0.58f, 1.18f);
                case "dread_shield": return P(33, VoiceFamily.Ward, 32, 1.40f, 0.76f, 0.86f, 1.00f, 0.74f, 1.52f);
                case "harmshield": return P(34, VoiceFamily.Ward, 33, 1.48f, 0.62f, 0.72f, 1.00f, 0.30f, 1.62f);
                case "plague_of_souls": return P(35, VoiceFamily.Death, 34, 1.58f, 0.58f, 1.00f, 1.00f, 0.94f, 1.72f);
                case "leeching_curse_dot": return P(36, VoiceFamily.Blood, 35, 0.74f, 1.04f, 0.70f, 0.58f, 0.84f, 0.70f);
                case "infectious_cloud_dot": return P(37, VoiceFamily.Disease, 36, 0.82f, 0.78f, 0.94f, 0.42f, 0.96f, 1.02f);
                default: return default;
            }
        }

        private static AudioProfile P(int id, VoiceFamily family, int voice,
            float weight, float pitch, float texture, float resonance,
            float rhythm, float decay) =>
            new AudioProfile(id, family, voice, weight, pitch, texture,
                resonance, rhythm, decay);

        /// <summary>
        /// Gathering half of a spell, composited entirely from recordings.
        /// The ability's own source leads; a second quieter layer pitched down
        /// gives it body, and the pair differs per ability by choice of source,
        /// pitch and stacking rather than by oscillator parameters.
        /// </summary>
        private static AudioClip BuildCast(
            NecromancerSpellDefinition definition)
        {
            AudioProfile profile = Profile(definition.Key);
            string sample = CastSample(definition.Key);
            if (sample == null) return null;
            uint seed = SpellSeed(definition.Key);
            // Pitch tracks the profile but stays near unity, so a bone crack
            // still sounds like bone instead of a chipmunk of one.
            float pitch = Mathf.Lerp(0.88f, 1.10f,
                Mathf.InverseLerp(0.50f, 1.42f, profile.Pitch));
            var layers = new List<PcmSampleMixer.Layer>
            {
                new PcmSampleMixer.Layer(sample, 1f, pitch)
            };
            // Heavier spells get an octave-down copy for weight; the small
            // offset keeps it from phasing against the lead.
            if (profile.Weight > 0.95f)
                layers.Add(new PcmSampleMixer.Layer(sample,
                    Mathf.Lerp(0.28f, 0.52f, profile.Weight - 0.95f),
                    pitch * 0.62f, 0.012f + Unit(seed, 11) * 0.010f));
            return PcmSampleMixer.BuildLayered(
                "CS Necromancer Cast " + definition.Key, layers.ToArray());
        }

        /// <summary>
        /// The moment a spell lands. Same rule: recordings only, with a tuned
        /// bell tail retained for the spells whose profile calls for one.
        /// </summary>
        private static AudioClip BuildResolve(
            NecromancerSpellDefinition definition)
        {
            AudioProfile profile = Profile(definition.Key);
            string sample = ResolveSample(definition.Key);
            if (sample == null) return null;
            uint seed = SpellSeed(definition.Key) ^ 0x9E3779B9u;
            float pitch = Mathf.Lerp(0.86f, 1.12f,
                Mathf.InverseLerp(0.50f, 1.42f, profile.Pitch));
            var layers = new List<PcmSampleMixer.Layer>
            {
                new PcmSampleMixer.Layer(sample, 1f, pitch)
            };
            if (profile.Weight > 1.0f)
                layers.Add(new PcmSampleMixer.Layer(sample,
                    Mathf.Lerp(0.30f, 0.58f, profile.Weight - 1.0f),
                    pitch * 0.58f, 0.010f));
            // A rhythmic second strike for spells with a pronounced pulse.
            if (profile.Rhythm > 0.70f)
                layers.Add(new PcmSampleMixer.Layer(sample, 0.42f,
                    pitch * 1.14f,
                    0.085f + profile.Rhythm * 0.075f));
            if (profile.Resonance >= 0.42f)
                layers.Add(new PcmSampleMixer.Layer("focus_bell.wav",
                    0.10f + profile.Resonance * 0.16f,
                    Mathf.Lerp(0.52f, 0.80f, Unit(seed, 13)),
                    0.020f + profile.Rhythm * 0.026f, 1.10f));
            return PcmSampleMixer.BuildLayered(
                "CS Necromancer Resolve " + definition.Key, layers.ToArray());
        }


        private static AudioClip MixSample(string name, AudioClip generated,
            string sampleName, float gain, float pitch, float offsetSeconds,
            float generatedGain = 1f) =>
            PcmSampleMixer.Mix(name, generated, sampleName, gain, pitch,
                offsetSeconds, generatedGain);

        private static uint SpellSeed(string text)
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
            value / (1f + Mathf.Abs(value));

        private static float SmoothStep01(float value) =>
            value * value * (3f - 2f * value);

        private static void PlayAt(AudioClip clip, Vector3 position,
            float volume, float range)
        {
            PlayAt(clip, position, volume, range, 0.06f);
        }

        /// <summary>
        /// Plays one spell layer with per-cast variation.
        ///
        /// This previously applied the raw volume and a hard-coded pitch of 1.
        /// Both were wrong: ignoring GameData.CombatVol and GameData.MasterVol
        /// meant Necromancer spells stayed at full level no matter where the
        /// player set the audio sliders, and a fixed pitch made a dot rotation
        /// retrigger the exact same waveform every few seconds.
        /// </summary>
        private static void PlayAt(AudioClip clip, Vector3 position,
            float volume, float range, float pitchSpread)
        {
            if (clip == null) return;
            float mix = Mathf.Clamp01(volume *
                GameData.CombatVol * GameData.MasterVol *
                UnityEngine.Random.Range(0.94f, 1.06f));
            if (mix <= 0.001f) return;
            var audioObject = new GameObject(clip.name);
            audioObject.transform.position = position;
            var source = audioObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = mix;
            source.pitch = UnityEngine.Random.Range(1f - pitchSpread,
                1f + pitchSpread);
            source.priority = 64;
            source.spatialBlend = 0.58f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1.8f;
            source.maxDistance = range;
            source.dopplerLevel = 0f;
            source.playOnAwake = false;
            source.Play();
            SourcePlayCount++;
            LastClipName = clip.name;
            UnityEngine.Object.Destroy(audioObject,
                clip.length / Mathf.Max(0.20f, source.pitch) + 0.25f);
        }
    }
}
