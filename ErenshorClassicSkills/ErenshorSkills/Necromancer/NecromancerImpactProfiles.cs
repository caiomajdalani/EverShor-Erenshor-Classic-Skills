using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ErenshorSkills.Necromancer
{
    internal enum NecromancerImpactMotion
    {
        Rise,
        Implode,
        Burst,
        Spiral,
        Crawl,
        Veil,
        Mend,
        Scream,
        Orbit,
        Cloud,
        Transfer,
        Shell,
        Collapse,
        Helix,
        Rune,
        Shatter,
        Pyre,
        Seal,
        Cyclone
    }

    /// <summary>
    /// A spell-specific visual recipe layered over the matching native
    /// Erenshor effect. No two Necromancer definitions share a visual
    /// fingerprint: motion, timing, silhouette, skulls, rings, and palette
    /// are deliberately authored to communicate what the spell did.
    /// </summary>
    internal sealed class NecromancerImpactProfile
    {
        internal int Id;
        internal string Motif;
        internal NecromancerImpactMotion Motion;
        internal Color Secondary;
        internal int Burst;
        internal int AccentBurst;
        internal int SkullBurst;
        internal int RingCount;
        internal float Radius;
        internal float Height;
        internal float Speed;
        internal float Size;
        internal float Duration;
        internal float Spin;
        internal float Gravity;
        internal float Noise;
        internal bool Trails;

        internal string VisualFingerprint(Color primary)
        {
            return string.Join("|",
                Motion.ToString(),
                ColorKey(primary),
                ColorKey(Secondary),
                Burst.ToString(CultureInfo.InvariantCulture),
                AccentBurst.ToString(CultureInfo.InvariantCulture),
                SkullBurst.ToString(CultureInfo.InvariantCulture),
                RingCount.ToString(CultureInfo.InvariantCulture),
                FloatKey(Radius),
                FloatKey(Height),
                FloatKey(Speed),
                FloatKey(Size),
                FloatKey(Duration),
                FloatKey(Spin),
                FloatKey(Gravity),
                FloatKey(Noise),
                Trails ? "1" : "0");
        }

        private static string ColorKey(Color value) =>
            $"{FloatKey(value.r)},{FloatKey(value.g)}," +
            $"{FloatKey(value.b)},{FloatKey(value.a)}";

        private static string FloatKey(float value) =>
            value.ToString("0.000", CultureInfo.InvariantCulture);
    }

    internal static class NecromancerImpactProfiles
    {
        private static readonly Dictionary<string, NecromancerImpactProfile>
            Profiles = BuildProfiles();

        internal static NecromancerImpactProfile Get(string key)
        {
            Profiles.TryGetValue(key ?? "", out NecromancerImpactProfile value);
            return value;
        }

        internal static bool Validate(
            IReadOnlyList<NecromancerSpellDefinition> definitions,
            out string detail)
        {
            var errors = new List<string>();
            var ids = new HashSet<int>();
            var motifs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visuals = new HashSet<string>(StringComparer.Ordinal);
            foreach (NecromancerSpellDefinition definition in definitions)
            {
                NecromancerImpactProfile profile = Get(definition.Key);
                if (profile == null)
                {
                    errors.Add($"{definition.Key}: missing impact profile");
                    continue;
                }
                if (!ids.Add(profile.Id))
                    errors.Add($"{definition.Key}: duplicate signature id {profile.Id}");
                if (string.IsNullOrWhiteSpace(profile.Motif) ||
                    !motifs.Add(profile.Motif))
                    errors.Add($"{definition.Key}: duplicate/empty motif '{profile.Motif}'");
                if (!visuals.Add(profile.VisualFingerprint(
                        definition.VfxColor)))
                    errors.Add($"{definition.Key}: duplicate visual fingerprint");
            }

            foreach (string key in Profiles.Keys)
                if (!ContainsDefinition(definitions, key))
                    errors.Add($"{key}: orphan impact profile");

            detail = errors.Count == 0
                ? $"{definitions.Count}/{definitions.Count} spell-specific " +
                  "impact profiles have unique ids, motifs, and visual fingerprints"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        private static bool ContainsDefinition(
            IReadOnlyList<NecromancerSpellDefinition> definitions, string key)
        {
            foreach (NecromancerSpellDefinition definition in definitions)
                if (string.Equals(definition.Key, key,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static Dictionary<string, NecromancerImpactProfile>
            BuildProfiles()
        {
            var profiles = new Dictionary<string, NecromancerImpactProfile>(
                StringComparer.OrdinalIgnoreCase);

            Add(profiles, "bone_thrall_1", P(1, "Cavorting Grave Rise",
                NecromancerImpactMotion.Rise, C(0.90f, 0.92f, 0.76f),
                17, 6, 2, 1, 0.72f, 0.04f, 1.05f, 0.105f, 0.95f,
                1.2f, -0.08f, 0.08f, false));
            Add(profiles, "siphon_spark", P(2, "Spark Soul Implosion",
                NecromancerImpactMotion.Implode, C(0.92f, 0.18f, 0.48f),
                13, 7, 1, 2, 0.68f, 0.48f, 0.92f, 0.075f, 0.82f,
                2.8f, 0f, 0.12f, true));
            Add(profiles, "rotting_fever", P(3, "Crawling Fever Pustules",
                NecromancerImpactMotion.Crawl, C(0.48f, 0.31f, 0.06f),
                19, 8, 0, 1, 0.84f, 0.10f, 0.34f, 0.130f, 1.35f,
                0.6f, 0.18f, 0.48f, false));
            Add(profiles, "bone_armor_1", P(4, "Bone Armor Assembly",
                NecromancerImpactMotion.Shell, C(0.94f, 0.96f, 0.84f),
                22, 9, 0, 3, 0.92f, 0.72f, 0.48f, 0.115f, 1.20f,
                1.6f, -0.12f, 0.05f, false));
            Add(profiles, "clinging_shadow", P(5, "Clinging Shadow Coil",
                NecromancerImpactMotion.Spiral, C(0.58f, 0.08f, 0.70f),
                15, 7, 2, 2, 0.74f, 0.18f, 0.55f, 0.090f, 1.28f,
                -2.4f, 0.08f, 0.35f, true));
            Add(profiles, "mend_bones", P(6, "Mending Bone Convergence",
                NecromancerImpactMotion.Mend, C(0.92f, 1.00f, 0.78f),
                18, 10, 0, 2, 0.77f, 0.62f, 0.78f, 0.100f, 1.08f,
                1.1f, -0.16f, 0.04f, false));
            Add(profiles, "noxious_bolt", P(7, "Noxious Acid Shatter",
                NecromancerImpactMotion.Shatter, C(0.86f, 0.96f, 0.08f),
                21, 11, 0, 1, 0.63f, 0.82f, 1.52f, 0.072f, 0.76f,
                0.9f, 0.42f, 0.22f, true));
            Add(profiles, "fear", P(8, "Terror Skull Shock",
                NecromancerImpactMotion.Scream, C(0.94f, 0.34f, 1.00f),
                12, 8, 5, 3, 0.88f, 1.02f, 1.18f, 0.098f, 0.88f,
                2.2f, 0f, 0.16f, true));
            Add(profiles, "gather_shadows", P(9, "Descending Shadow Veil",
                NecromancerImpactMotion.Veil, C(0.28f, 0.02f, 0.38f),
                24, 6, 4, 1, 1.02f, 1.42f, 0.46f, 0.145f, 1.46f,
                -1.4f, 0.38f, 0.52f, false));

            Add(profiles, "bone_thrall_2", P(10, "Sentry Grave Muster",
                NecromancerImpactMotion.Rise, C(0.76f, 0.84f, 0.70f),
                25, 10, 3, 2, 0.91f, 0.03f, 1.26f, 0.116f, 1.06f,
                1.5f, -0.10f, 0.10f, false));
            Add(profiles, "venomous_blood", P(11, "Venom Blood Splatter",
                NecromancerImpactMotion.Burst, C(0.84f, 0.03f, 0.10f),
                27, 9, 0, 1, 0.73f, 0.94f, 1.36f, 0.088f, 0.92f,
                0.7f, 0.64f, 0.28f, true));
            Add(profiles, "dark_covenant_1", P(12, "Covenant Heart Spiral",
                NecromancerImpactMotion.Helix, C(0.18f, 0.48f, 0.98f),
                16, 12, 2, 2, 0.68f, 0.86f, 0.72f, 0.080f, 1.24f,
                3.1f, -0.08f, 0.14f, true));
            Add(profiles, "heart_flutter", P(13, "Stopped Heart Pulse",
                NecromancerImpactMotion.Rune, C(0.96f, 0.06f, 0.18f),
                14, 9, 1, 4, 0.79f, 0.98f, 1.04f, 0.094f, 0.86f,
                0.4f, 0f, 0.18f, false));
            Add(profiles, "siphon_soul", P(14, "Soul Vortex Extraction",
                NecromancerImpactMotion.Implode, C(0.86f, 0.22f, 0.94f),
                26, 13, 3, 2, 0.96f, 1.04f, 1.28f, 0.108f, 1.18f,
                3.5f, -0.06f, 0.24f, true));
            Add(profiles, "bone_armor_2", P(15, "Ossified Carapace Lock",
                NecromancerImpactMotion.Shell, C(0.88f, 0.94f, 0.82f),
                31, 12, 0, 4, 1.08f, 0.78f, 0.62f, 0.128f, 1.38f,
                2.0f, -0.14f, 0.07f, false));
            Add(profiles, "reclaim_servant", P(16, "Servant Bone Collapse",
                NecromancerImpactMotion.Collapse, C(0.84f, 0.90f, 0.72f),
                28, 10, 4, 1, 1.14f, 0.88f, 1.42f, 0.104f, 1.02f,
                -2.1f, 0.58f, 0.20f, true));
            Add(profiles, "deathly_guise", P(17, "Deathly Ash Collapse",
                NecromancerImpactMotion.Veil, C(0.08f, 0.07f, 0.10f),
                30, 7, 3, 2, 0.97f, 1.52f, 0.38f, 0.162f, 1.62f,
                -0.8f, 0.72f, 0.61f, false));
            Add(profiles, "leeching_curse", P(18, "Leeching Twin Helix",
                NecromancerImpactMotion.Helix, C(0.86f, 0.04f, 0.20f),
                23, 14, 3, 3, 0.88f, 0.96f, 0.92f, 0.096f, 1.34f,
                3.8f, -0.04f, 0.27f, true));
            Add(profiles, "intensify_death", P(19, "Thrall Empowerment Runes",
                NecromancerImpactMotion.Orbit, C(0.90f, 0.96f, 0.74f),
                20, 15, 4, 4, 1.02f, 0.84f, 0.58f, 0.114f, 1.48f,
                2.7f, -0.10f, 0.11f, false));

            Add(profiles, "bone_thrall_3", P(20, "Soldier Grave Column",
                NecromancerImpactMotion.Rise, C(0.72f, 0.86f, 0.66f),
                34, 13, 4, 3, 1.12f, 0.02f, 1.52f, 0.132f, 1.22f,
                1.8f, -0.14f, 0.13f, true));
            Add(profiles, "infectious_cloud", P(21, "Expanding Plague Cloud",
                NecromancerImpactMotion.Cloud, C(0.72f, 0.92f, 0.06f),
                38, 16, 2, 3, 1.58f, 0.42f, 0.52f, 0.182f, 1.76f,
                1.3f, -0.02f, 0.72f, false));
            Add(profiles, "dark_covenant_2", P(22, "Blood Communion Rune",
                NecromancerImpactMotion.Rune, C(0.14f, 0.58f, 1.00f),
                24, 17, 3, 5, 0.92f, 0.72f, 0.84f, 0.102f, 1.42f,
                2.5f, -0.05f, 0.17f, true));
            Add(profiles, "screaming_terror", P(23, "Screaming Skull Wave",
                NecromancerImpactMotion.Scream, C(1.00f, 0.22f, 0.88f),
                18, 12, 9, 5, 1.22f, 1.18f, 1.62f, 0.126f, 1.12f,
                3.2f, 0.02f, 0.32f, true));
            Add(profiles, "scent_of_decay", P(24, "Decay Resistance Fracture",
                NecromancerImpactMotion.Shatter, C(0.46f, 0.08f, 0.64f),
                29, 18, 2, 2, 0.86f, 0.88f, 1.46f, 0.084f, 0.98f,
                -1.7f, 0.34f, 0.44f, true));
            Add(profiles, "bond_of_death", P(25, "Interlocked Death Bond",
                NecromancerImpactMotion.Orbit, C(0.82f, 0.90f, 0.70f),
                27, 19, 5, 5, 1.18f, 0.82f, 0.66f, 0.122f, 1.64f,
                3.4f, -0.13f, 0.15f, false));
            Add(profiles, "bone_thrall_4", P(26, "Vanguard Crypt Eruption",
                NecromancerImpactMotion.Rise, C(0.82f, 0.22f, 0.26f),
                42, 17, 6, 4, 1.31f, 0.01f, 1.82f, 0.146f, 1.36f,
                2.1f, -0.18f, 0.18f, true));
            Add(profiles, "soul_transfusion", P(27, "Azure Soul Transfer",
                NecromancerImpactMotion.Transfer, C(0.70f, 0.92f, 1.00f),
                25, 20, 3, 3, 1.02f, 1.06f, 1.34f, 0.092f, 1.56f,
                2.9f, -0.09f, 0.21f, true));
            Add(profiles, "bone_armor_3", P(28, "Sepulchral Plate Assembly",
                NecromancerImpactMotion.Shell, C(0.96f, 0.98f, 0.86f),
                45, 21, 1, 6, 1.32f, 0.88f, 0.78f, 0.154f, 1.72f,
                2.4f, -0.18f, 0.09f, false));
            Add(profiles, "devouring_darkness", P(29, "Devouring Shadow Maw",
                NecromancerImpactMotion.Cyclone, C(0.46f, 0.02f, 0.70f),
                36, 16, 7, 4, 1.26f, 0.66f, 1.06f, 0.138f, 1.82f,
                -4.2f, 0.26f, 0.66f, true));
            Add(profiles, "drain_spirit", P(30, "Spirit Tornado Extraction",
                NecromancerImpactMotion.Implode, C(0.98f, 0.12f, 0.36f),
                40, 22, 6, 3, 1.24f, 1.16f, 1.58f, 0.142f, 1.54f,
                4.4f, -0.08f, 0.39f, true));
            Add(profiles, "funeral_pyre", P(31, "Gravefire Funeral Pillar",
                NecromancerImpactMotion.Pyre, C(0.58f, 0.05f, 0.82f),
                48, 24, 4, 4, 1.18f, 0.10f, 2.18f, 0.156f, 1.94f,
                2.6f, -0.32f, 0.46f, true));
            Add(profiles, "bone_thrall_5", P(32, "Champion Ossuary Coronation",
                NecromancerImpactMotion.Rise, C(0.92f, 0.08f, 0.18f),
                56, 26, 10, 6, 1.52f, 0.00f, 2.32f, 0.174f, 1.68f,
                2.9f, -0.22f, 0.22f, true));
            Add(profiles, "dread_shield", P(33, "Vengeful Skull Aegis",
                NecromancerImpactMotion.Orbit, C(0.90f, 0.38f, 1.00f),
                32, 20, 12, 6, 1.36f, 0.96f, 0.72f, 0.148f, 1.88f,
                4.0f, -0.12f, 0.19f, true));
            Add(profiles, "harmshield", P(34, "Sealed Harmshield Sphere",
                NecromancerImpactMotion.Seal, C(0.96f, 0.98f, 1.00f),
                44, 28, 8, 7, 1.44f, 0.94f, 0.32f, 0.166f, 1.46f,
                3.6f, -0.16f, 0.08f, false));
            Add(profiles, "plague_of_souls", P(35, "Plague Soul Cyclone",
                NecromancerImpactMotion.Cyclone, C(0.72f, 0.10f, 0.94f),
                62, 30, 14, 7, 1.72f, 0.52f, 1.36f, 0.188f, 2.12f,
                5.0f, 0.12f, 0.82f, true));

            Add(profiles, "leeching_curse_dot", P(36,
                "Lingering Leech Pulse", NecromancerImpactMotion.Helix,
                C(0.76f, 0.03f, 0.18f), 11, 5, 2, 1, 0.61f, 0.74f,
                0.44f, 0.066f, 1.06f, 2.15f, -0.03f, 0.25f, true));
            Add(profiles, "infectious_cloud_dot", P(37,
                "Lingering Plague Haze", NecromancerImpactMotion.Cloud,
                C(0.62f, 0.76f, 0.04f), 17, 7, 1, 2, 1.06f, 0.24f,
                0.28f, 0.124f, 1.48f, 0.85f, 0.04f, 0.64f, false));

            return profiles;
        }

        private static void Add(
            IDictionary<string, NecromancerImpactProfile> profiles,
            string key, NecromancerImpactProfile profile) =>
            profiles.Add(key, profile);

        private static NecromancerImpactProfile P(int id, string motif,
            NecromancerImpactMotion motion, Color secondary, int burst,
            int accentBurst, int skullBurst, int ringCount, float radius,
            float height, float speed, float size, float duration, float spin,
            float gravity, float noise, bool trails)
        {
            return new NecromancerImpactProfile
            {
                Id = id,
                Motif = motif,
                Motion = motion,
                Secondary = secondary,
                Burst = burst,
                AccentBurst = accentBurst,
                SkullBurst = skullBurst,
                RingCount = ringCount,
                Radius = radius,
                Height = height,
                Speed = speed,
                Size = size,
                Duration = duration,
                Spin = spin,
                Gravity = gravity,
                Noise = noise,
                Trails = trails
            };
        }

        private static Color C(float r, float g, float b) =>
            new Color(r, g, b, 1f);
    }
}
