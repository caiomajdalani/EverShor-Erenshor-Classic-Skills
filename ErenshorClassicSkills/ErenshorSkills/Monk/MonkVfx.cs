using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ErenshorSkills.Monk
{
    /// <summary>
    /// Bespoke martial VFX. Every Monk technique has a different silhouette and
    /// motion language; palettes reinforce identity but are never the sole
    /// differentiator. A compact authored impact sheet sharpens contact frames
    /// while the geometry remains runtime-authored and self-cleaning.
    /// </summary>
    internal static class MonkVfx
    {
        private static readonly string[] PatternNames =
        {
            "Twin open-hand seals",
            "Inward mending breath",
            "Straight-punch pressure cone",
            "Shoulder shock wedge",
            "Triple tiger-claw rake",
            "Circular heel ribbon",
            "Shattering focus chains",
            "Resonant traveling palm projectile",
            "Crescent slow spiral",
            "Centered inward helix",
            "Collapsing feign ripple",
            "Counter-palm ward disc",
            "Descending eagle talons",
            "Restorative double helix",
            "Fading silent footprints",
            "Falling-rain fist barrage",
            "Flying-kick wind tunnel",
            "Swaying reed ribbons",
            "Rising stone guard plates",
            "Low sweeping ground crescent",
            "Converging pressure-point star",
            "Twin whirlwind kick helix",
            "Hollow-body phase cage",
            "Rising-dragon uppercut vortex",
            "Purifying concentric wash",
            "Hundred-fists impact fan",
            "Five-point focus constellation",
            "Quivering-palm hand seal",
            "Pummeling flying-fist barrage",
            "Triple gale-shear wave",
            "Vaulting crescent shock arc",
            "Skybreaker rush detonation"
        };

        private static Material _lineMaterial;
        private static Material _softParticleMaterial;
        private static Material _streakParticleMaterial;
        private static Material _impactParticleMaterial;
        private static Texture2D _impactParticleSheet;
        private static bool _impactParticleMaterialInitialized;
        private static readonly Dictionary<string, Material> TextureMaterials =
            new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> OffensiveSignatureKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "jab", "shoulder_check", "tiger_claw", "round_kick",
                "pummel", "stone_throw", "crescent_heel", "eagle_strike",
                "fist_of_falling_rain", "flying_kick",
                "sweeping_crescent", "pressure_point",
                "gale_splitting_palm", "whirlwind_kick", "dragon_punch",
                "vaulting_tempest", "skybreaker_rush", "quivering_palm"
            };
        private static readonly HashSet<string> UtilitySignatureKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "open_hand_discipline", "mend", "focused_will",
                "centered_breath", "feign_death", "countering_palm",
                "breath_of_harmony", "silent_step", "reed_in_the_wind",
                "stone_stance", "empty_body", "purify_body",
                "hundred_fists", "five_point_focus"
            };
        private static readonly Color ImpactIvory =
            new Color(1f, 0.92f, 0.72f, 1f);
        private static readonly Color ImpactDust =
            new Color(0.58f, 0.47f, 0.32f, 0.82f);
        internal static int AmplifiedImpactCount { get; private set; }

        // Erenshor's Main Camera Profile runs PostProcessing v2 Bloom at
        // intensity 10, threshold 1.0, soft knee 0.252, effectively unclamped.
        // Anything at or below 1.0 barely crosses that knee, which is why flat
        // white VFX read as matte cutouts. Driving the core of each effect well
        // past 1.0 lets the game's own bloom supply the glow, and keeping the
        // body just under it preserves the technique's hue in the halo.
        private const float CoreGain = 4.6f;
        private const float BodyGain = 1.7f;
        private const float TrailGain = 2.3f;
        internal static float TrailGainValue => TrailGain;
        internal static float CoreGainValue => CoreGain;
        internal static bool SoftParticlesActive { get; private set; }
        internal static string MaterialPathDescription { get; private set; } =
            "uninitialized";

        /// <summary>Pushes a colour above the bloom threshold.</summary>
        internal static Color Hdr(Color color, float gain) =>
            new Color(color.r * gain, color.g * gain, color.b * gain,
                color.a);

        /// <summary>
        /// White-hot centre that still carries a trace of the technique hue, so
        /// the bloom halo is tinted instead of neutral.
        /// </summary>
        internal static Color CoreOf(Color accent) =>
            Hdr(Color.Lerp(accent, Color.white, 0.72f), CoreGain);

        /// <summary>
        /// Deep saturated edge. Erenshor's palette is bright and high-key, so a
        /// pale effect at scene luminance disappears; a dark saturated rim under
        /// a hot core is what makes the shape read on any background.
        /// </summary>
        internal static Color RimOf(Color accent)
        {
            Color.RGBToHSV(accent, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Clamp01(s * 1.45f + 0.12f),
                Mathf.Clamp01(v * 0.34f));
        }

        /// <summary>
        /// Mend renders as Erenshor's own healing burst rather than authored
        /// line art. The native resolve prefab carries the game's exact heal
        /// aesthetic; the authored pattern remains only as a fallback when the
        /// native effect cannot be resolved in the current scene.
        /// </summary>
        internal static void PlayMendHeal(MonkTechniqueDefinition definition,
            Transform caster)
        {
            if (caster == null) return;
            if (SkillsPlugin.CfgEnableTestMode?.Value == true)
                SkillsPlugin.Log?.LogInfo("[Monk VFX] mend native-heal");
            try
            {
                Spell native = FindNativeHealSpell();
                List<GameObject> effects = GameData.EffectDB?.SpellEffects;
                if (native != null && effects != null &&
                    native.SpellResolveFXIndex >= 0 &&
                    native.SpellResolveFXIndex < effects.Count &&
                    effects[native.SpellResolveFXIndex] != null)
                {
                    GameObject burst = UnityEngine.Object.Instantiate(
                        effects[native.SpellResolveFXIndex],
                        caster.position, Quaternion.identity);
                    // The native Beneficial resolve path gives this prefab a
                    // ten-second timer; without one, effects authored to loop
                    // would sit at the cast spot forever.
                    burst.AddComponent<DestroyObjectTimer>().TimeToDestroy =
                        600f;
                    // The native heal prefab is visual-only on this direct
                    // resolve path, so Mend must explicitly use the same
                    // per-technique utility audio contract as every other
                    // successful Monk ability.
                    if (definition != null)
                        MonkAudio.PlayUtility(definition, caster);
                    return;
                }
            }
            catch (Exception exception)
            {
                SkillsPlugin.Log?.LogWarning(
                    "Mend native heal VFX failed: " + exception.Message);
            }
            if (definition != null) Play(definition, caster);
        }

        private static Spell FindNativeHealSpell()
        {
            Spell[] database = GameData.SpellDatabase?.SpellDatabase;
            if (database == null) return null;
            foreach (string hint in new[] { "Minor Healing", "Healing Bloom" })
            {
                Spell match = database.FirstOrDefault(spell => spell != null &&
                    string.Equals(spell.SpellName, hint,
                        StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            return null;
        }

        internal static void Play(MonkTechniqueDefinition definition,
            Transform caster, Transform target = null)
        {
            if (definition == null || caster == null) return;
            // First line on purpose: any invocation must log, even one that
            // fails later, so absence of this line in a session log means the
            // call site never ran.
            if (SkillsPlugin.CfgEnableTestMode?.Value == true)
                SkillsPlugin.Log?.LogInfo(
                    $"[Monk VFX] {definition.Key} pattern={definition.VfxPattern}");
            Vector3 forward = caster.forward;
            if (target != null)
            {
                Vector3 toward = target.position - caster.position;
                toward -= caster.up * Vector3.Dot(toward, caster.up);
                if (toward.sqrMagnitude > 0.001f)
                    forward = toward.normalized;
            }
            float impactHeight = ImpactHeight(definition.Key);
            // Put the contact pattern on the enemy-facing surface. Centering
            // it inside the target let the mesh depth-test away the strongest
            // ring, chevron, and flash layers from the gameplay camera.
            Vector3 impact = definition.IsOffensive && target != null
                ? target.position + caster.up * impactHeight -
                    forward * 0.27f
                : caster.position + caster.up * 0.96f;
            if (definition.Key == "skybreaker_rush")
            {
                // Skybreaker is a point-blank punch after authored root
                // motion, so its blast must remain attached to that contact
                // instead of an enemy pivot that can sit below uneven ground.
                // The forward offset puts the flash just beyond the fist and
                // keeps the sliding shelf visible on stairs and slopes.
                impact = caster.position + caster.up * 0.94f +
                    forward * 0.76f;
            }
            var root = new GameObject("CS Monk VFX - " + definition.Key);
            Quaternion rotation = forward.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(forward.normalized, caster.up)
                : caster.rotation;
            root.transform.SetPositionAndRotation(impact, rotation);

            BuildPattern(definition.VfxPattern, root, definition.VfxColor,
                caster, target);
            if (definition.IsOffensive)
            {
                AddImpactAmplifier(root, definition);
                MonkAudio.PlayImpact(definition, impact);
                Transform cameraAnchor =
                    GameData.PlayerControl?.transform;
                if (cameraAnchor != null &&
                    Vector3.Distance(cameraAnchor.position, impact) < 18f)
                {
                    GetCameraImpact(definition, out float shake,
                        out float duration);
                    GameData.CamControl?.ShakeScreen(shake, duration);
                }
                MonkHitStop.Apply(definition, caster, target);
            }
            else
            {
                AddUtilityPolish(root, definition);
                MonkAudio.PlayUtility(definition, caster);
            }
            UnityEngine.Object.Destroy(root, 2.8f);
        }

        private static void GetCameraImpact(
            MonkTechniqueDefinition definition, out float shake,
            out float duration)
        {
            float tier = Mathf.Clamp01((definition.Level - 1f) / 34f);
            shake = Mathf.Lerp(0.48f, 0.84f, tier);
            duration = Mathf.Lerp(0.085f, 0.145f, tier);
            switch (definition.Key)
            {
                case "jab": shake = 0.64f; duration = 0.075f; break;
                case "shoulder_check": shake = 0.82f; duration = 0.13f; break;
                case "tiger_claw": shake = 0.70f; duration = 0.09f; break;
                case "round_kick": shake = 0.78f; duration = 0.12f; break;
                case "pummel": shake = 0.73f; duration = 0.16f; break;
                case "stone_throw": shake = 0.68f; duration = 0.10f; break;
                case "crescent_heel": shake = 0.80f; duration = 0.11f; break;
                case "eagle_strike": shake = 0.76f; duration = 0.10f; break;
                case "fist_of_falling_rain": shake = 0.86f; duration = 0.18f; break;
                case "flying_kick": shake = 0.94f; duration = 0.14f; break;
                case "sweeping_crescent": shake = 0.88f; duration = 0.13f; break;
                case "pressure_point": shake = 0.58f; duration = 0.065f; break;
                case "gale_splitting_palm": shake = 0.90f; duration = 0.12f; break;
                case "whirlwind_kick": shake = 1.02f; duration = 0.18f; break;
                case "dragon_punch": shake = 1.18f; duration = 0.20f; break;
                case "vaulting_tempest": shake = 1.08f; duration = 0.17f; break;
                case "skybreaker_rush": shake = 1.52f; duration = 0.25f; break;
                case "quivering_palm": shake = 1.34f; duration = 0.22f; break;
            }
        }

        internal static MonkAttackVfxDriver BeginAttack(
            MonkTechniqueDefinition definition, Animator animator,
            Transform caster, Transform target, float duration,
            float impactTime)
        {
            if (definition == null || animator == null || caster == null ||
                !definition.IsOffensive)
                return null;
            var root = new GameObject(
                "CS Monk Motion VFX - " + definition.Key);
            MonkAttackVfxDriver driver =
                root.AddComponent<MonkAttackVfxDriver>();
            driver.Configure(definition, animator, caster, target, duration,
                impactTime);
            return driver;
        }

        private static void BuildPattern(int pattern, GameObject root,
            Color color, Transform caster, Transform target)
        {
            switch (pattern)
            {
                case 0: BuildOpenHand(root, color); break;
                case 1: BuildMend(root, color); break;
                case 2: BuildJab(root, color); break;
                case 3: BuildShoulderCheck(root, color); break;
                case 4: BuildTigerClaw(root, color); break;
                case 5: BuildRoundKick(root, color); break;
                case 6: BuildFocusedWill(root, color); break;
                case 7: BuildResonantPalm(root, color); break;
                case 8: BuildCrescentHeel(root, color); break;
                case 9: BuildCenteredBreath(root, color); break;
                case 10: BuildFeignDeath(root, color); break;
                case 11: BuildCounteringPalm(root, color); break;
                case 12: BuildEagleStrike(root, color); break;
                case 13: BuildBreathOfHarmony(root, color); break;
                case 14: BuildSilentStep(root, color); break;
                case 15: BuildFallingRain(root, color); break;
                case 16: BuildFlyingKick(root, color, caster, target); break;
                case 17: BuildReedInWind(root, color); break;
                case 18: BuildStoneStance(root, color); break;
                case 19: BuildSweepingCrescent(root, color); break;
                case 20: BuildPressurePoint(root, color); break;
                case 21: BuildWhirlwindKick(root, color); break;
                case 22: BuildEmptyBody(root, color); break;
                case 23: BuildDragonPunch(root, color); break;
                case 24: BuildPurifyBody(root, color); break;
                case 25: BuildHundredFists(root, color); break;
                case 26: BuildFivePointFocus(root, color); break;
                case 27: BuildQuiveringPalm(root, color); break;
                case 28: BuildPummel(root, color); break;
                case 29: BuildGaleSplittingPalm(root, color); break;
                case 30: BuildVaultingTempest(root, color); break;
                case 31: BuildSkybreakerRush(root, color); break;
                default: BuildJab(root, color); break;
            }
        }

        private static void BuildOpenHand(GameObject root, Color color)
        {
            AddRing(root, "Left hand seal", color, 0.24f, 18, false,
                new Vector3(-0.30f, 0.05f, 0f), 0f, 0.75f,
                Vector3.zero, 0.20f, 0.85f);
            AddRing(root, "Right hand seal", color, 0.24f, 18, false,
                new Vector3(0.30f, 0.05f, 0f), 0.08f, 0.75f,
                Vector3.zero, 0.20f, 0.85f);
            AddStroke(root, "Open palm bridge", color, new[]
            {
                new Vector3(-0.30f, 0.05f, 0f),
                new Vector3(0f, 0.20f, 0f),
                new Vector3(0.30f, 0.05f, 0f)
            }, 0.045f, 0.10f, 0.70f, Vector3.zero,
                Vector3.one, Vector3.one * 1.15f);
            AddBurst(root, "Discipline motes", color,
                ParticleSystemShapeType.Circle, 18, 0.025f, 0.07f,
                0.28f, 0.62f, 0.35f, Vector3.zero, false, false);
        }

        private static void BuildMend(GameObject root, Color color)
        {
            // Sized to read from the gameplay camera: the old version was
            // chest-height line flashes under a second long, thin enough to be
            // depth-tested away by the player's own body. Wider rings, a
            // longer spiral, and bright additive particle layers make the heal
            // unmistakable.
            AddSpiral(root, "Mending breath", color, 0.95f, 1.75f, 1.6f,
                40, 0.065f, 1.60f, true, 0f);
            for (int index = 0; index < 3; index++)
                AddRing(root, "Mend inward ring " + index, color,
                    1.25f - index * 0.22f, 36, true,
                    Vector3.down * (0.72f - index * 0.30f),
                    index * 0.16f, 1.35f, Vector3.zero, -0.55f, 1.05f);
            AddBurst(root, "Mend rising breath", color,
                ParticleSystemShapeType.Circle, 46, 0.055f, 0.15f,
                0.35f, 1.30f, 1.25f, Vector3.down * 0.65f, true, false,
                0f, true);
            AddBurst(root, "Mend soothing motes", Color.Lerp(color,
                    Color.white, 0.45f),
                ParticleSystemShapeType.Sphere, 30, 0.045f, 0.11f,
                0.15f, 0.70f, 1.45f, Vector3.zero, true, false,
                0.25f, true);
            AddBurst(root, "Mend closing sparkle", Color.Lerp(color,
                    Color.white, 0.65f),
                ParticleSystemShapeType.Circle, 22, 0.035f, 0.09f,
                0.55f, 1.60f, 0.85f, Vector3.down * 0.30f, true, true,
                0.75f, true);
        }

        private static void BuildJab(GameObject root, Color color)
        {
            Color accent = MartialTone(color);
            // A thin streak plus a light impact read as a tap. Jab is the
            // Monk's bread-and-butter strike, so it gets a committed drive
            // line, a hard contact flash, a shockwave that leaves the point of
            // impact, and knocked-loose debris.
            AddStroke(root, "Jab motion streak", ImpactIvory, new[]
            {
                new Vector3(0f, 0f, -0.86f),
                new Vector3(0f, 0f, -0.28f),
                new Vector3(0f, 0f, 0.10f)
            }, 0.082f, 0f, 0.26f, Vector3.zero,
                new Vector3(0.42f, 0.42f, 0.92f), Vector3.one * 1.15f);

            AddMartialImpact(root, accent, 1.28f, true, 0f);

            // Hard white contact flash on the frame of impact.
            AddRadialStar(root, "Jab contact flash", ImpactIvory, 10,
                0.05f, 0.62f, false, 0.05f, 0f, 0.16f);

            // Two shockwave rings leaving the contact point give the hit
            // somewhere to travel instead of stopping dead.
            AddRing(root, "Jab shockwave", ImpactIvory, 0.30f, 30, false,
                Vector3.zero, 0.02f, 0.34f, Vector3.zero, 1.85f, 0.055f);
            AddRing(root, "Jab shockwave echo", accent, 0.22f, 26, false,
                Vector3.zero, 0.07f, 0.40f, Vector3.zero, 2.40f, 0.038f);

            // Debris driven back along the punch line.
            AddBurst(root, "Jab impact debris", accent,
                ParticleSystemShapeType.Cone, 26, 0.028f, 0.085f,
                3.2f, 7.4f, 0.30f, Vector3.zero, false, true, 0.01f, true);
            AddBurst(root, "Jab impact sparks", ImpactIvory,
                ParticleSystemShapeType.Sphere, 16, 0.020f, 0.052f,
                2.1f, 5.0f, 0.24f, Vector3.zero, false, true, 0f, true);
        }

        private static void BuildShoulderCheck(GameObject root, Color color)
        {
            Color accent = MartialTone(color);
            for (int index = 0; index < 2; index++)
            {
                float width = 0.30f + index * 0.18f;
                AddStroke(root, "Shoulder force wedge " + index, accent,
                    new[]
                    {
                        new Vector3(-width, -0.18f + index * 0.12f, -0.22f),
                        new Vector3(0f, 0.02f + index * 0.08f, 0.05f),
                        new Vector3(width, -0.18f + index * 0.12f, -0.22f)
                    }, 0.040f, index * 0.025f, 0.32f,
                    Vector3.zero, Vector3.one * 0.72f,
                    Vector3.one * (1.05f + index * 0.08f));
            }
            AddMartialImpact(root, accent, 1.12f, true, 0.01f);
        }

        private static void BuildTigerClaw(GameObject root, Color color)
        {
            Color accent = MartialTone(color);
            for (int index = 0; index < 3; index++)
            {
                float offset = (index - 1) * 0.16f;
                AddArc(root, "Tiger claw rake " + index, accent, 0.50f,
                    -58f, 52f, 10, false,
                    new Vector3(offset, 0f, 0f), index * 0.035f, 0.58f,
                    new Vector3(0f, 0f, -18f), 0.12f, 0.032f);
            }
            AddMartialImpact(root, accent, 0.86f, false, 0.06f);
        }

        private static void BuildRoundKick(GameObject root, Color color)
        {
            Color accent = MartialTone(color);
            AddArc(root, "Round-kick heel wake", accent, 0.74f,
                -132f, 112f, 24, true, Vector3.down * 0.42f,
                0f, 0.42f, new Vector3(0f, 170f, 0f), 0.18f, 0.050f);
            AddMartialImpact(root, accent, 1.02f, true, 0.03f);
        }

        private static void BuildFocusedWill(GameObject root, Color color)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                for (int link = 0; link < 3; link++)
                    AddRing(root, "Broken focus chain", color, 0.12f,
                        12, false, new Vector3(side * (0.18f + link * 0.24f),
                            (link - 1) * 0.08f, 0f), link * 0.025f,
                        0.58f, new Vector3(0f, 0f, side * 180f),
                        0.35f, 0.045f);
            }
            AddRadialStar(root, "Will-break shards", color, 9, 0.18f,
                0.92f, false, 0.055f, 0f, 0.70f);
            AddBurst(root, "Focused-will flash", Color.white,
                ParticleSystemShapeType.Sphere, 20, 0.02f, 0.07f,
                1.1f, 3.1f, 0.36f, Vector3.zero, false, true);
        }

        private static void BuildResonantPalm(GameObject root, Color color)
        {
            // The moving orb is driven from the actual cupped hands by
            // MonkAttackVfxDriver. This is its target-side compression bloom.
            Color accent = Color.Lerp(color, Color.white, 0.08f);
            accent.a = 1f;
            AddRing(root, "Resonant projectile impact core", ImpactIvory,
                0.16f, 22, false, Vector3.zero, 0f, 0.34f,
                new Vector3(0f, 0f, 120f), 2.05f, 0.052f);
            AddRing(root, "Resonant projectile impact wave", accent,
                0.30f, 30, false, Vector3.zero, 0.018f, 0.48f,
                new Vector3(0f, 0f, -82f), 1.72f, 0.044f);
            AddRadialStar(root, "Resonant projectile impact spokes",
                ImpactIvory, 12, 0.16f, 0.90f, false,
                0.032f, 0.025f, 0.36f);
            AddBurst(root, "Resonant projectile impact motes", accent,
                ParticleSystemShapeType.Cone, 42, 0.030f, 0.090f,
                1.5f, 4.4f, 0.52f, Vector3.zero, false, true,
                0f, true);
            AddMartialImpact(root, accent, 1.14f, false, 0.015f);
        }

        private static void BuildCrescentHeel(GameObject root, Color color)
        {
            Color accent = MartialTone(color);
            AddArc(root, "Crescent heel wake", accent, 0.76f,
                -112f, 96f, 22, false, Vector3.down * 0.16f,
                0f, 0.38f, new Vector3(0f, 0f, 82f), 0.15f, 0.046f);
            AddArc(root, "Crescent heel edge", ImpactIvory, 0.60f,
                -92f, 78f, 17, false, Vector3.down * 0.16f,
                0.025f, 0.28f, new Vector3(0f, 0f, 62f), 0.10f, 0.022f);
            AddMartialImpact(root, accent, 0.94f, true, 0.03f);
        }

        private static void BuildCenteredBreath(GameObject root, Color color)
        {
            AddSpiral(root, "Centered left breath", color, 0.82f, 1.20f,
                2.2f, 38, 0.04f, 1.05f, true, 0f);
            AddSpiral(root, "Centered right breath", Color.white, 0.62f,
                1.05f, -2.2f, 34, 0.025f, 0.95f, true, 0.08f);
            AddRing(root, "Centered breath seal", color, 1.05f, 36, true,
                Vector3.down * 0.72f, 0f, 1.10f,
                new Vector3(0f, 70f, 0f), -0.58f, 0.045f);
            AddBurst(root, "Centered breath motes", color,
                ParticleSystemShapeType.Circle, 32, 0.025f, 0.075f,
                0.18f, 0.72f, 0.86f, Vector3.down * 0.62f, true, false);
        }

        private static void BuildFeignDeath(GameObject root, Color color)
        {
            for (int index = 0; index < 3; index++)
                AddRing(root, "Feign collapse ripple " + index, color,
                    1.15f - index * 0.22f, 30, true,
                    Vector3.down * 0.72f, index * 0.06f, 0.72f,
                    Vector3.zero, -0.72f, 0.04f);
            AddStroke(root, "Falling breath", color, new[]
            {
                new Vector3(0f, 0.55f, 0f),
                new Vector3(0f, 0.05f, 0f),
                new Vector3(0f, -0.62f, 0f)
            }, 0.055f, 0f, 0.62f, Vector3.zero,
                Vector3.one, new Vector3(1.5f, 0.12f, 1.5f));
            AddBurst(root, "Feign dust", color,
                ParticleSystemShapeType.Circle, 38, 0.03f, 0.10f,
                0.3f, 1.5f, 0.70f, Vector3.down * 0.72f, false, false);
        }

        private static void BuildCounteringPalm(GameObject root, Color color)
        {
            AddPolygon(root, "Counter ward octagon", color, 8, 0.74f,
                false, Vector3.zero, 0f, 1.0f,
                new Vector3(0f, 0f, 90f), 0.16f, 0.055f);
            AddRing(root, "Counter inner palm", Color.white, 0.28f, 20,
                false, Vector3.zero, 0.05f, 0.82f,
                new Vector3(0f, 0f, -150f), 0.20f, 0.04f);
            AddRadialStar(root, "Counter spokes", color, 8, 0.30f,
                0.66f, false, 0.035f, 0.04f, 0.90f);
            AddBurst(root, "Counter reversed sparks", color,
                ParticleSystemShapeType.Cone, 22, 0.025f, 0.07f,
                -2.5f, -0.8f, 0.50f, Vector3.zero, true, true);
        }

        private static void BuildEagleStrike(GameObject root, Color color)
        {
            Color accent = MartialTone(color);
            for (int index = 0; index < 3; index++)
            {
                float x = (index - 1) * 0.15f;
                AddStroke(root, "Eagle talon " + index, accent, new[]
                {
                    new Vector3(x - 0.10f, 0.54f, -0.08f),
                    new Vector3(x, 0.10f, 0f),
                    new Vector3(x + 0.06f, -0.28f, 0.04f)
                }, 0.035f, index * 0.028f, 0.34f,
                    new Vector3(0f, 0f, -10f + index * 10f),
                    new Vector3(0.78f, 0.92f, 0.78f), Vector3.one);
            }
            AddMartialImpact(root, accent, 0.88f, false, 0.055f);
        }

        private static void BuildBreathOfHarmony(GameObject root, Color color)
        {
            AddSpiral(root, "Harmony helix A", color, 0.72f, 1.55f,
                3.0f, 46, 0.045f, 1.30f, true, 0f);
            AddSpiral(root, "Harmony helix B", Color.Lerp(color,
                Color.white, 0.55f), 0.72f, 1.55f, -3.0f, 46,
                0.035f, 1.30f, true, 0.06f);
            for (int index = 0; index < 3; index++)
                AddRing(root, "Harmony rising ring " + index, color,
                    0.52f + index * 0.16f, 28, true,
                    Vector3.down * (0.65f - index * 0.42f),
                    index * 0.10f, 1.12f,
                    new Vector3(0f, 55f + index * 25f, 0f),
                    0.24f, 0.035f);
            AddBurst(root, "Harmony leaves", color,
                ParticleSystemShapeType.Circle, 42, 0.025f, 0.085f,
                0.35f, 1.1f, 1.05f, Vector3.down * 0.62f, true, false);
        }

        private static void BuildSilentStep(GameObject root, Color color)
        {
            AddFootprint(root, "Silent left footprint", color,
                new Vector3(-0.22f, -0.72f, -0.40f), -18f, 0f);
            AddFootprint(root, "Silent right footprint", color,
                new Vector3(0.22f, -0.72f, 0.22f), 18f, 0.16f);
            AddFootprint(root, "Silent fading step", color,
                new Vector3(-0.18f, -0.72f, 0.78f), -12f, 0.32f);
            AddBurst(root, "Silent ankle wisps", color,
                ParticleSystemShapeType.Circle, 24, 0.025f, 0.06f,
                0.20f, 0.75f, 0.90f, Vector3.down * 0.66f, true, true);
        }

        private static void BuildFallingRain(GameObject root, Color color)
        {
            Color accent = MartialTone(color);
            for (int index = 0; index < 3; index++)
            {
                float x = (index - 1) * 0.18f;
                float delay = index * 0.055f;
                AddStroke(root, "Falling fist streak " + index,
                    index == 1 ? ImpactIvory : accent, new[]
                    {
                        new Vector3(x - 0.08f, 0.68f, -0.12f),
                        new Vector3(x, 0.18f, -0.02f),
                        new Vector3(x + 0.05f, -0.24f, 0.04f)
                    }, 0.038f, delay, 0.30f, Vector3.zero,
                    new Vector3(0.70f, 1.05f, 0.70f), Vector3.one);
                AddRing(root, "Rain contact " + index, accent,
                    0.12f, 14, false, new Vector3(x, -0.24f, 0f),
                    delay + 0.04f, 0.25f, Vector3.zero, 1.25f, 0.022f);
            }
            AddMartialImpact(root, accent, 1.00f, false, 0.11f);
        }

        private static void BuildPummel(GameObject root, Color color)
        {
            Color accent = MartialTone(color);
            for (int index = 0; index < 12; index++)
            {
                float side = index % 2 == 0 ? -1f : 1f;
                float lane = (index % 6) - 2.5f;
                float x = side * (0.18f + (index % 3) * 0.055f);
                float y = lane * 0.13f;
                float delay = index * 0.038f;
                AddStroke(root, "Pummel flying fist " + index,
                    index % 4 == 0 ? ImpactIvory : accent, new[]
                    {
                        new Vector3(x + side * 0.82f, y, -0.72f),
                        new Vector3(x + side * 0.24f, y * 0.96f, -0.24f),
                        new Vector3(x, y, 0.08f)
                    }, 0.036f + (index % 3) * 0.008f,
                    delay, 0.23f, Vector3.zero,
                    new Vector3(0.48f, 0.72f, 0.66f),
                    Vector3.one * 1.06f);
                AddRing(root, "Pummel contact " + index,
                    index % 3 == 0 ? ImpactIvory : accent,
                    0.095f + (index % 3) * 0.018f, 12, false,
                    new Vector3(x * 0.28f, y, 0f),
                    delay + 0.055f, 0.21f, Vector3.zero,
                    1.45f, 0.020f);
            }
            AddBurst(root, "Pummel knuckle afterimages", accent,
                ParticleSystemShapeType.Cone, 72, 0.018f, 0.058f,
                1.8f, 4.8f, 0.55f, Vector3.zero, true, true, 0.06f);
            AddMartialImpact(root, accent, 1.22f, false, 0.18f);
        }

        private static void BuildFlyingKick(GameObject root, Color color,
            Transform caster, Transform target)
        {
            Color accent = MartialTone(color);
            Vector3 from = caster.position + Vector3.up * 0.75f;
            Vector3 to = target != null
                ? target.position + Vector3.up * 1.0f
                : from + caster.forward * 3f;
            Vector3 side = caster.right * 0.08f;
            AddWorldStroke(root, "Flying-kick foot wake", accent,
                new[] { from - side, Vector3.Lerp(from, to, 0.58f), to },
                0.058f, 0f, 0.34f);
            AddWorldStroke(root, "Flying-kick edge", ImpactIvory,
                new[] { from + side, Vector3.Lerp(from, to, 0.62f), to },
                0.024f, 0.02f, 0.25f);
            AddArc(root, "Flying-kick contact crescent", accent, 0.66f,
                -92f, 88f, 20, false, Vector3.zero, 0.02f, 0.34f,
                new Vector3(0f, 0f, 90f), 0.18f, 0.044f);
            AddMartialImpact(root, accent, 1.22f, true, 0.025f);
        }

        private static void BuildReedInWind(GameObject root, Color color)
        {
            for (int index = 0; index < 3; index++)
            {
                float x = (index - 1) * 0.38f;
                AddStroke(root, "Swaying reed " + index, color, new[]
                {
                    new Vector3(x, -0.72f, 0f),
                    new Vector3(x + 0.12f, -0.28f, 0f),
                    new Vector3(x - 0.08f, 0.18f, 0f),
                    new Vector3(x + 0.18f, 0.70f, 0f)
                }, 0.045f, index * 0.06f, 1.05f,
                    new Vector3(0f, 0f, index % 2 == 0 ? 42f : -42f),
                    new Vector3(1.0f, 0.65f, 1f),
                    new Vector3(1.0f, 1.08f, 1f));
                AddArc(root, "Reed leaf " + index, color, 0.18f,
                    -65f, 65f, 8, false,
                    new Vector3(x + 0.12f, 0.18f + index * 0.10f, 0f),
                    0.10f + index * 0.05f, 0.85f,
                    new Vector3(0f, 0f, -70f), 0.15f, 0.025f);
            }
            AddBurst(root, "Reed pollen", color,
                ParticleSystemShapeType.Circle, 20, 0.018f, 0.05f,
                0.15f, 0.65f, 0.95f, Vector3.zero, true, false);
        }

        private static void BuildStoneStance(GameObject root, Color color)
        {
            for (int index = 0; index < 4; index++)
            {
                float angle = index * 90f + 45f;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 position = new Vector3(Mathf.Cos(radians) * 0.62f,
                    -0.20f + (index % 2) * 0.30f,
                    Mathf.Sin(radians) * 0.62f);
                AddPlate(root, "Stone guard plate " + index, color,
                    position, angle, index * 0.055f);
            }
            AddRing(root, "Stone foundation", color, 0.88f, 8, true,
                Vector3.down * 0.72f, 0f, 1.10f,
                new Vector3(0f, 28f, 0f), 0.15f, 0.08f);
            AddBurst(root, "Stone rising chips", color,
                ParticleSystemShapeType.Circle, 42, 0.045f, 0.13f,
                0.35f, 1.7f, 0.90f, Vector3.down * 0.65f, true, true);
        }

        private static void BuildSweepingCrescent(GameObject root, Color color)
        {
            Color accent = MartialTone(color);
            AddArc(root, "Sweeping ground crescent", accent, 1.04f,
                -150f, 148f, 34, true, Vector3.down * 0.68f,
                0f, 0.42f, new Vector3(0f, 205f, 0f), 0.16f, 0.054f);
            AddArc(root, "Sweeping heel edge", ImpactIvory, 0.82f,
                -138f, 132f, 28, true, Vector3.down * 0.64f,
                0.025f, 0.30f, new Vector3(0f, 175f, 0f), 0.12f, 0.022f);
            AddMartialImpact(root, accent, 1.04f, true, 0.08f);
        }

        private static void BuildPressurePoint(GameObject root, Color color)
        {
            Color accent = MartialTone(color);
            AddRadialStar(root, "Pressure convergence", accent, 6,
                0.48f, 0.055f, false, 0.020f, 0f, 0.28f);
            AddPolygon(root, "Pressure-point diamond", ImpactIvory, 4,
                0.14f, false, Vector3.zero, 0.045f, 0.30f,
                new Vector3(0f, 0f, 125f), 0.35f, 0.026f);
            AddMartialImpact(root, accent, 0.66f, false, 0.06f);
        }

        private static void BuildWhirlwindKick(GameObject root, Color color)
        {
            Color accent = MartialTone(color);
            AddArc(root, "Whirlwind outer heel orbit", accent, 0.92f,
                -170f, 190f, 34, true, Vector3.down * 0.06f,
                0f, 0.46f, new Vector3(0f, 310f, 0f), 0.12f, 0.054f);
            AddArc(root, "Whirlwind ivory cutting edge", ImpactIvory, 0.76f,
                -150f, 210f, 32, true, Vector3.down * 0.02f,
                0.018f, 0.36f, new Vector3(0f, 360f, 0f), 0.08f, 0.026f);
            AddSpiral(root, "Whirlwind sustained cyclone", accent, 0.82f,
                0.64f, 2.25f, 38, 0.038f, 0.50f, true, 0.015f);
            AddArc(root, "Whirlwind planted-foot wake", ImpactDust, 0.96f,
                -175f, 175f, 32, true, Vector3.down * 0.66f,
                0.025f, 0.42f, new Vector3(0f, 285f, 0f), 0.18f, 0.030f);
            AddBurst(root, "Whirlwind ground grit", ImpactDust,
                ParticleSystemShapeType.Circle, 26, 0.030f, 0.072f,
                0.45f, 1.55f, 0.46f, Vector3.down * 0.66f,
                false, true, 0.02f, true);
            AddMartialImpact(root, accent, 1.12f, true, 0.075f);
        }

        private static void BuildEmptyBody(GameObject root, Color color)
        {
            AddRing(root, "Empty-body latitude", color, 0.78f, 36, true,
                Vector3.zero, 0f, 1.10f,
                new Vector3(0f, 80f, 0f), 0.28f, 0.035f);
            GameObject longitudeA = AddRing(root, "Empty-body longitude A",
                color, 0.78f, 36, false, Vector3.zero, 0.04f, 1.05f,
                new Vector3(0f, 0f, 80f), 0.28f, 0.035f);
            longitudeA.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            GameObject longitudeB = AddRing(root, "Empty-body longitude B",
                Color.white, 0.78f, 36, false, Vector3.zero, 0.08f,
                1.00f, new Vector3(0f, 0f, -80f), 0.28f, 0.025f);
            longitudeB.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            AddRing(root, "Empty core", new Color(color.r, color.g,
                color.b, 0.65f), 0.20f, 22, false, Vector3.zero,
                0.10f, 0.90f, Vector3.zero, 3.0f, 0.03f);
            AddBurst(root, "Empty-body phase motes", color,
                ParticleSystemShapeType.Sphere, 44, 0.018f, 0.060f,
                0.45f, 1.6f, 0.90f, Vector3.zero, false, true);
        }

        private static void BuildDragonPunch(GameObject root, Color color)
        {
            Color accent = MartialTone(color);
            AddSpiral(root, "Rising dragon outer vortex", accent,
                0.52f, 1.72f, 3.20f, 52, 0.052f, 0.58f, true, 0f);
            AddSpiral(root, "Rising dragon inner vortex", ImpactIvory,
                0.30f, 1.48f, -2.65f, 44, 0.030f, 0.50f, true, 0.025f);
            AddStroke(root, "Rising dragon uppercut core", ImpactIvory,
                new[]
            {
                new Vector3(0f, -0.76f, -0.38f),
                new Vector3(0f, -0.20f, -0.12f),
                new Vector3(0f, 0.38f, 0.08f),
                new Vector3(0f, 0.88f, 0.20f)
            }, 0.078f, 0.018f, 0.40f, Vector3.zero,
                new Vector3(0.38f, 0.44f, 0.38f),
                new Vector3(1.16f, 1.32f, 1.16f));
            AddRing(root, "Rising dragon launch ring", accent,
                0.34f, 26, true, Vector3.down * 0.64f,
                0f, 0.42f, new Vector3(0f, 220f, 0f),
                2.25f, 0.052f);
            AddRing(root, "Rising dragon crown", ImpactIvory,
                0.18f, 22, true, Vector3.up * 0.74f,
                0.07f, 0.34f, new Vector3(0f, -260f, 0f),
                2.80f, 0.038f);
            AddBurst(root, "Rising dragon sparks", accent,
                ParticleSystemShapeType.Cone, 56, 0.022f, 0.080f,
                2.0f, 5.8f, 0.52f, Vector3.down * 0.42f,
                true, true, 0f, true);
            AddBurst(root, "Rising dragon crown burst", ImpactIvory,
                ParticleSystemShapeType.Sphere, 34, 0.045f, 0.11f,
                0.85f, 2.4f, 0.34f, Vector3.up * 0.62f,
                false, true, 0.06f, true, true);
            AddMartialImpact(root, accent, 1.40f, false, 0.055f);
        }

        private static void BuildPurifyBody(GameObject root, Color color)
        {
            for (int index = 0; index < 4; index++)
                AddRing(root, "Purifying wash " + index,
                    index % 2 == 0 ? color : Color.white,
                    0.32f + index * 0.22f, 34, true,
                    Vector3.down * (0.68f - index * 0.36f),
                    index * 0.075f, 1.05f,
                    new Vector3(0f, 70f + index * 20f, 0f),
                    0.28f, 0.035f);
            AddRadialStar(root, "Purify expelled thorns", color, 10,
                0.22f, 1.08f, false, 0.035f, 0.10f, 0.70f);
            AddBurst(root, "Purifying light", Color.white,
                ParticleSystemShapeType.Circle, 48, 0.018f, 0.060f,
                0.30f, 1.35f, 0.96f, Vector3.down * 0.52f, true, false);
        }

        private static void BuildHundredFists(GameObject root, Color color)
        {
            for (int index = 0; index < 12; index++)
            {
                float side = index % 2 == 0 ? -1f : 1f;
                float y = -0.45f + (index % 6) * 0.18f;
                float delay = index * 0.025f;
                AddStroke(root, "Hundred-fists streak " + index,
                    index % 3 == 0 ? Color.white : color, new[]
                    {
                        new Vector3(side * 0.92f, y, -0.35f),
                        new Vector3(side * 0.28f, y + 0.04f, -0.08f),
                        new Vector3(side * 0.04f, y, 0.10f)
                    }, 0.045f + (index % 3) * 0.012f,
                    delay, 0.42f, Vector3.zero,
                    new Vector3(0.55f, 1f, 0.55f), Vector3.one);
                if (index % 3 == 0)
                    AddRing(root, "Hundred-fists impact " + index, color,
                        0.12f + index * 0.012f, 14, false,
                        new Vector3(0f, y, 0f), delay + 0.05f,
                        0.36f, Vector3.zero, 1.1f, 0.025f);
            }
            AddBurst(root, "Hundred-fists knuckle storm", color,
                ParticleSystemShapeType.Cone, 64, 0.018f, 0.055f,
                1.4f, 4.0f, 0.62f, Vector3.zero, true, true, 0.10f);
        }

        private static void BuildFivePointFocus(GameObject root, Color color)
        {
            var nodes = new Vector3[5];
            for (int index = 0; index < nodes.Length; index++)
            {
                float angle = (90f + index * 144f) * Mathf.Deg2Rad;
                nodes[index] = new Vector3(Mathf.Cos(angle) * 0.78f,
                    Mathf.Sin(angle) * 0.78f, 0f);
            }
            var star = new Vector3[6];
            for (int index = 0; index < 5; index++) star[index] = nodes[index];
            star[5] = nodes[0];
            AddStroke(root, "Five-point constellation", color, star,
                0.055f, 0f, 1.15f, new Vector3(0f, 0f, 34f),
                Vector3.one * 1.18f, Vector3.one * 0.78f);
            for (int index = 0; index < nodes.Length; index++)
            {
                AddRing(root, "Focus node " + index,
                    index == 0 ? Color.white : color, 0.11f, 14, false,
                    nodes[index], index * 0.055f, 1.05f,
                    new Vector3(0f, 0f, index % 2 == 0 ? 180f : -180f),
                    0.42f, 0.025f);
                AddStroke(root, "Focus ray " + index, color,
                    new[] { nodes[index], Vector3.zero }, 0.022f,
                    0.16f + index * 0.035f, 0.82f, Vector3.zero,
                    Vector3.one * 1.2f, Vector3.one * 0.2f);
            }
            AddBurst(root, "Focus alignment", color,
                ParticleSystemShapeType.Circle, 35, 0.018f, 0.055f,
                0.25f, 0.95f, 0.82f, Vector3.zero, false, false, 0.18f);
        }

        private static void BuildQuiveringPalm(GameObject root, Color color)
        {
            Color accent = MartialTone(color);
            AddRing(root, "Quivering compression", accent, 0.32f, 22,
                false, Vector3.zero, 0f, 0.30f,
                new Vector3(0f, 0f, 35f), -0.48f, 0.040f);
            AddRing(root, "Quivering release", ImpactIvory, 0.16f, 20,
                false, Vector3.zero, 0.075f, 0.34f,
                Vector3.zero, 2.15f, 0.038f);
            AddRadialStar(root, "Quivering fracture", accent, 10,
                0.11f, 0.72f, false, 0.025f, 0.09f, 0.36f);
            AddMartialImpact(root, accent, 1.35f, true, 0.09f);
        }

        private static void BuildGaleSplittingPalm(GameObject root,
            Color color)
        {
            Color accent = Color.Lerp(color, Color.white, 0.08f);
            Color limeEdge = Color.Lerp(color,
                new Color(1f, 0.98f, 0.68f, 1f), 0.62f);
            // Opposing, almost complete crescents make the arriving wave read
            // as a broad cutting disc instead of another round spell burst.
            AddArc(root, "Gale arriving outer crescent", limeEdge, 0.92f,
                -156f, 156f, 34, false, Vector3.zero, 0f, 0.48f,
                new Vector3(0f, 0f, 18f), 0.62f, 0.080f);
            AddArc(root, "Gale arriving ivory crescent", ImpactIvory,
                0.68f, -148f, 148f, 30, false,
                new Vector3(0f, 0f, -0.025f), 0.018f, 0.34f,
                new Vector3(0f, 0f, -16f), 0.74f, 0.046f);
            AddArc(root, "Gale counter shear", accent, 0.78f,
                22f, 338f, 32, false, new Vector3(0f, 0f, 0.025f),
                0.035f, 0.44f, new Vector3(0f, 0f, -28f),
                0.66f, 0.052f);
            AddTexturedFlash(root, "Gale crescent flash", "impact_slash.png",
                limeEdge, 1.82f, 0.34f, 0.012f, 90f, 1.26f);
            AddBurst(root, "Gale-shear spray", accent,
                ParticleSystemShapeType.Cone, 34, 0.022f, 0.072f,
                2.2f, 5.2f, 0.40f, Vector3.zero, false, true, 0.02f, true);
        }

        private static void BuildVaultingTempest(GameObject root,
            Color color)
        {
            Color accent = Color.Lerp(color,
                new Color(0.80f, 1f, 0.22f, 1f), 0.35f);
            // A tall heel-to-sky crescent backs the real foot trail. The
            // paired white edge preserves the classic rising-kick read even
            // when the camera views the somersault at an angle.
            AddArc(root, "Vaulting outer crescent", accent, 1.46f,
                -166f, 34f, 38, false, Vector3.down * 0.34f,
                0f, 0.62f, new Vector3(0f, 0f, -72f), 0.48f, 0.110f);
            AddArc(root, "Vaulting ivory edge", ImpactIvory, 1.20f,
                -160f, 28f, 34, false, Vector3.down * 0.31f,
                0.018f, 0.48f, new Vector3(0f, 0f, -58f), 0.62f,
                0.052f);
            AddStroke(root, "Vaulting lift line", accent, new[]
            {
                new Vector3(-0.22f, -1.08f, -0.12f),
                new Vector3(0f, -0.18f, 0f),
                new Vector3(0.30f, 1.04f, 0.14f)
            }, 0.070f, 0f, 0.52f, Vector3.zero,
                Vector3.one * 0.68f, Vector3.one * 1.28f);
            AddTexturedFlash(root, "Vaulting heel slash", "impact_slash.png",
                ImpactIvory, 2.34f, 0.42f, 0.01f, -76f, 1.18f);
            AddBurst(root, "Vaulting rising sparks", accent,
                ParticleSystemShapeType.Cone, 42, 0.025f, 0.085f,
                1.7f, 4.7f, 0.46f, Vector3.down * 0.20f,
                true, true, 0.025f, true);
            AddMartialImpact(root, accent, 1.30f, true, 0.035f);
        }

        private static void BuildSkybreakerRush(GameObject root,
            Color color)
        {
            Color accent = Color.Lerp(color,
                new Color(0.05f, 0.56f, 1f, 1f), 0.36f);
            Color whiteBlue = Color.Lerp(ImpactIvory, accent, 0.18f);
            AddStroke(root, "Skybreaker driving lane", accent, new[]
            {
                new Vector3(0f, 0f, -1.65f),
                new Vector3(0f, 0f, -0.26f),
                new Vector3(0f, 0f, 0.72f)
            }, 0.20f, 0f, 0.54f, Vector3.zero,
                new Vector3(0.28f, 0.28f, 0.44f),
                new Vector3(2.05f, 2.05f, 2.12f));
            AddStroke(root, "Skybreaker left release wing", whiteBlue,
                new[]
                {
                    new Vector3(0f, 0f, -0.28f),
                    new Vector3(-0.34f, 0.20f, 0.18f),
                    new Vector3(-1.08f, 0.08f, 0.82f)
                }, 0.095f, 0.015f, 0.36f, Vector3.zero,
                Vector3.one * 0.42f, Vector3.one * 1.44f);
            AddStroke(root, "Skybreaker right release wing", whiteBlue,
                new[]
                {
                    new Vector3(0f, 0f, -0.28f),
                    new Vector3(0.34f, -0.20f, 0.18f),
                    new Vector3(1.08f, -0.08f, 0.82f)
                }, 0.095f, 0.015f, 0.36f, Vector3.zero,
                Vector3.one * 0.42f, Vector3.one * 1.44f);
            AddArc(root, "Skybreaker forward pressure hood", accent,
                0.82f, -118f, 118f, 28, false,
                new Vector3(0f, 0f, 0.20f), 0.035f, 0.38f,
                Vector3.zero, 0.88f, 0.070f);
            // The punch drives a low energy shelf through the target before
            // it blooms. Keeping these ribbons near the terrain and moving
            // them forward distinguishes the signature sliding explosion from
            // an ordinary stationary radial impact.
            GameObject groundWave = AddStroke(root,
                "Skybreaker sliding ground wave", accent, new[]
                {
                    new Vector3(-0.16f, 0f, -0.30f),
                    new Vector3(-0.58f, 0.02f, 0.18f),
                    new Vector3(-0.82f, 0f, 0.78f),
                    new Vector3(0f, 0.055f, 1.22f),
                    new Vector3(0.82f, 0f, 0.78f),
                    new Vector3(0.58f, 0.02f, 0.18f),
                    new Vector3(0.16f, 0f, -0.30f)
                }, 0.150f, 0f, 0.62f, Vector3.zero,
                new Vector3(0.34f, 0.18f, 0.30f),
                new Vector3(2.08f, 0.86f, 2.34f));
            groundWave.transform.localPosition =
                new Vector3(0f, -0.96f, -0.18f);
            groundWave.AddComponent<MonkVfxTravel>().Configure(
                Vector3.forward * 1.45f, 0.58f);
            GameObject groundCore = AddStroke(root,
                "Skybreaker sliding white core", whiteBlue, new[]
                {
                    new Vector3(-0.08f, 0f, -0.18f),
                    new Vector3(-0.32f, 0.025f, 0.30f),
                    new Vector3(0f, 0.07f, 0.86f),
                    new Vector3(0.32f, 0.025f, 0.30f),
                    new Vector3(0.08f, 0f, -0.18f)
                }, 0.090f, 0.015f, 0.48f, Vector3.zero,
                new Vector3(0.30f, 0.22f, 0.28f),
                new Vector3(1.72f, 0.94f, 2.06f));
            groundCore.transform.localPosition =
                new Vector3(0f, -0.93f, -0.12f);
            groundCore.AddComponent<MonkVfxTravel>().Configure(
                Vector3.forward * 1.62f, 0.54f, 0.015f);
            AddRing(root, "Skybreaker ground detonation ring", whiteBlue,
                0.34f, 34, true, new Vector3(0f, -0.94f, 0.72f),
                0.045f, 0.54f, Vector3.zero, 3.45f, 0.085f);
            AddRing(root, "Skybreaker delayed ground aftershock", accent,
                0.42f, 32, true, new Vector3(0f, -0.93f, 1.05f),
                0.105f, 0.60f, Vector3.zero, 3.10f, 0.060f);
            GameObject breakWall = AddStroke(root,
                "Skybreaker rising break wall", accent, new[]
                {
                    new Vector3(-0.92f, -0.90f, 0.38f),
                    new Vector3(-0.52f, -0.28f, 0.76f),
                    new Vector3(0f, 0.34f, 1.16f),
                    new Vector3(0.52f, -0.28f, 0.76f),
                    new Vector3(0.92f, -0.90f, 0.38f)
                }, 0.135f, 0.035f, 0.54f, Vector3.zero,
                Vector3.one * 0.36f, new Vector3(1.76f, 1.54f, 2.08f));
            breakWall.AddComponent<MonkVfxTravel>().Configure(
                Vector3.forward * 0.72f, 0.48f, 0.035f);
            AddTexturedFlash(root, "Skybreaker blue detonation",
                "impact_flare.png", accent, 4.10f, 0.52f, 0f, 0f, 1.46f);
            AddTexturedFlash(root, "Skybreaker white detonation core",
                "impact_soft.png", whiteBlue, 2.80f, 0.38f, 0.020f,
                18f, 1.58f);
            AddTexturedFlash(root, "Skybreaker cross blast",
                "impact_slash.png", ImpactIvory, 3.20f, 0.36f, 0.040f,
                0f, 1.48f);
            AddRadialStar(root, "Skybreaker pressure fracture", whiteBlue,
                14, 0.18f, 1.34f, false, 0.018f, 0.065f, 0.46f);
            AddBurst(root, "Skybreaker expanding blue fire", whiteBlue,
                ParticleSystemShapeType.Sphere, 52, 0.12f, 0.38f,
                1.6f, 5.6f, 0.56f, Vector3.zero, false, false,
                0.015f, true, true);
            AddBurst(root, "Skybreaker storm burst", accent,
                ParticleSystemShapeType.Cone, 76, 0.035f, 0.135f,
                3.2f, 8.4f, 0.62f, Vector3.zero, false, true, 0.04f, true);
            AddBurst(root, "Skybreaker white-hot core", ImpactIvory,
                ParticleSystemShapeType.Sphere, 30, 0.09f, 0.28f,
                0.9f, 3.8f, 0.36f, Vector3.zero, false, false,
                0f, true, true);
            AddBurst(root, "Skybreaker sliding ground fragments", accent,
                ParticleSystemShapeType.Cone, 48, 0.030f, 0.095f,
                2.8f, 7.4f, 0.54f,
                new Vector3(0f, -0.92f, 0.34f), false, true,
                0.025f, true, true);
        }

        private static Color MartialTone(Color color)
        {
            Color warmNeutral = new Color(0.78f, 0.69f, 0.50f, 1f);
            Color muted = Color.Lerp(color, warmNeutral, 0.44f);
            muted.a = Mathf.Clamp01(color.a);
            return muted;
        }

        private static void AddUtilityPolish(GameObject root,
            MonkTechniqueDefinition definition)
        {
            float tier = Mathf.Clamp01((definition.Level - 1f) / 34f);
            Color accent = Color.Lerp(definition.VfxColor,
                ImpactIvory, 0.18f);
            float identity = 0.92f + definition.VfxPattern % 5 * 0.075f;
            AddTexturedFlash(root, "Technique-specific utility aura",
                "impact_soft.png", accent,
                Mathf.Lerp(1.30f, 1.92f, tier) * identity,
                Mathf.Lerp(0.46f, 0.72f, tier), 0.012f,
                definition.VfxPattern * 31f, 1.48f);
            AddUtilitySignature(root, definition, accent, tier);

            GameObject lightObject = new GameObject("Utility focus light");
            lightObject.transform.SetParent(root.transform, false);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = accent;
            light.range = Mathf.Lerp(2.20f, 3.40f, tier) * identity;
            light.intensity = Mathf.Lerp(1.80f, 3.20f, tier) * identity;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            var pulse = lightObject.AddComponent<MonkImpactLightPulse>();
            pulse.Duration = Mathf.Lerp(0.38f, 0.72f, tier) +
                definition.VfxPattern % 3 * 0.035f;
            pulse.Peak = light.intensity;
        }

        private static void AddUtilitySignature(GameObject root,
            MonkTechniqueDefinition definition, Color accent, float tier)
        {
            switch (definition.Key)
            {
                case "open_hand_discipline":
                    AddPolygon(root, "Twin discipline seal left", accent, 6,
                        0.46f, false, new Vector3(-0.42f, 0.08f, 0f),
                        0f, 0.72f, new Vector3(0f, 0f, 92f), 0.72f,
                        0.048f);
                    AddPolygon(root, "Twin discipline seal right", ImpactIvory,
                        6, 0.38f, false, new Vector3(0.42f, 0.08f, -0.02f),
                        0.07f, 0.68f, new Vector3(0f, 0f, -118f), 0.86f,
                        0.035f);
                    break;
                case "mend":
                    AddSpiral(root, "Mending breath helix", accent, 0.46f,
                        1.55f, 2.2f, 42, 0.052f, 0.90f, true, 0f);
                    AddBurst(root, "Mending rising motes", accent,
                        ParticleSystemShapeType.Circle, 46, 0.035f, 0.12f,
                        0.22f, 1.10f, 0.92f, Vector3.down * 0.70f, true,
                        false, 0.02f, true);
                    break;
                case "focused_will":
                    for (int index = 0; index < 4; index++)
                        AddPolygon(root, "Focused will lock " + index,
                            index % 2 == 0 ? accent : ImpactIvory, 4,
                            0.32f + index * 0.12f, false, Vector3.zero,
                            index * 0.045f, 0.62f,
                            new Vector3(0f, 0f, index % 2 == 0 ? 135f : -135f),
                            -0.18f, 0.030f + index * 0.004f);
                    break;
                case "centered_breath":
                    AddSpiral(root, "Centered breath inward current", accent,
                        0.58f, 1.72f, 2.8f, 52, 0.042f, 1.05f, true, 0f);
                    AddRing(root, "Centered breath ground pulse", ImpactIvory,
                        0.52f, 38, true, Vector3.down * 0.88f, 0.08f,
                        0.78f, Vector3.zero, 1.85f, 0.045f);
                    break;
                case "feign_death":
                    AddRing(root, "Feign heartbeat collapse", accent, 1.05f,
                        40, true, Vector3.down * 0.84f, 0f, 0.64f,
                        Vector3.zero, -0.76f, 0.070f);
                    AddBurst(root, "Feign displaced dust", ImpactDust,
                        ParticleSystemShapeType.Circle, 42, 0.04f, 0.13f,
                        0.55f, 2.10f, 0.72f, Vector3.down * 0.82f, false,
                        false, 0.02f, true);
                    break;
                case "countering_palm":
                    AddPolygon(root, "Counter palm octagonal ward", accent, 8,
                        0.68f, false, Vector3.forward * 0.08f, 0f, 0.78f,
                        new Vector3(0f, 0f, -145f), 0.42f, 0.070f);
                    AddRadialStar(root, "Counter palm reflected force",
                        ImpactIvory, 8, 0.10f, 0.76f, false, 0.045f,
                        0.07f, 0.48f);
                    break;
                case "breath_of_harmony":
                    AddSpiral(root, "Harmony ascending current", accent,
                        0.52f, 1.85f, 2.1f, 46, 0.052f, 1.08f, true, 0f);
                    AddSpiral(root, "Harmony answering current", ImpactIvory,
                        0.38f, 1.62f, -2.4f, 44, 0.034f, 1.02f, true,
                        0.06f);
                    break;
                case "silent_step":
                    AddArc(root, "Silent step vanishing wake", accent, 0.88f,
                        32f, 328f, 36, true, Vector3.down * 0.82f, 0f,
                        0.84f, new Vector3(0f, 170f, 0f), 1.15f, 0.035f);
                    AddBurst(root, "Silent step air flecks", accent,
                        ParticleSystemShapeType.Cone, 28, 0.018f, 0.065f,
                        0.80f, 2.80f, 0.62f, Vector3.down * 0.62f, true,
                        true, 0.02f, true);
                    break;
                case "reed_in_the_wind":
                    AddArc(root, "Reed left sway", accent, 0.82f, 92f, 268f,
                        28, false, Vector3.left * 0.30f, 0f, 0.92f,
                        new Vector3(0f, 34f, 46f), 0.48f, 0.052f);
                    AddArc(root, "Reed right sway", ImpactIvory, 0.68f,
                        -88f, 86f, 28, false, Vector3.right * 0.30f, 0.08f,
                        0.88f, new Vector3(0f, -38f, -52f), 0.56f,
                        0.038f);
                    break;
                case "stone_stance":
                    AddRing(root, "Stone stance ground claim", accent, 0.76f,
                        8, true, Vector3.down * 0.90f, 0f, 0.92f,
                        new Vector3(0f, 38f, 0f), 1.08f, 0.090f);
                    AddBurst(root, "Stone stance weight", ImpactDust,
                        ParticleSystemShapeType.Circle, 54, 0.045f, 0.16f,
                        0.35f, 1.80f, 0.90f, Vector3.down * 0.88f, false,
                        false, 0.01f, true);
                    break;
                case "empty_body":
                    AddPolygon(root, "Empty body phase shell", accent, 12,
                        0.72f, false, Vector3.zero, 0f, 1.02f,
                        new Vector3(0f, 0f, 155f), 0.68f, 0.046f);
                    AddPolygon(root, "Empty body inner absence", ImpactIvory,
                        5, 0.34f, false, Vector3.back * 0.03f, 0.10f, 0.82f,
                        new Vector3(0f, 0f, -190f), -0.32f, 0.030f);
                    break;
                case "purify_body":
                    for (int index = 0; index < 4; index++)
                        AddRing(root, "Purifying wash " + index,
                            index % 2 == 0 ? accent : ImpactIvory,
                            0.34f + index * 0.18f, 38, true,
                            Vector3.down * (0.72f - index * 0.16f),
                            index * 0.075f, 0.78f, Vector3.zero,
                            1.45f + index * 0.18f, 0.040f);
                    break;
                case "hundred_fists":
                    for (int index = 0; index < 8; index++)
                    {
                        float side = index % 2 == 0 ? -1f : 1f;
                        float row = index / 2 * 0.15f - 0.22f;
                        AddStroke(root, "Hundred fists afterimage " + index,
                            index % 3 == 0 ? ImpactIvory : accent,
                            new[] { new Vector3(side * 0.10f, row, 0f),
                                new Vector3(side * (0.72f + index * 0.035f),
                                    row + side * 0.05f, 0f) },
                            0.055f, index * 0.025f, 0.50f, Vector3.zero,
                            Vector3.one * 0.35f, Vector3.one * 1.30f);
                    }
                    break;
                case "five_point_focus":
                    AddRadialStar(root, "Five-point focus constellation",
                        accent, 5, 0.14f, 1.02f, false, 0.072f, 0f, 0.92f);
                    AddPolygon(root, "Five-point focus core", ImpactIvory, 5,
                        0.42f, false, Vector3.back * 0.02f, 0.08f, 0.82f,
                        new Vector3(0f, 0f, -122f), 0.82f, 0.040f);
                    break;
            }
        }

        private static void AddImpactAmplifier(GameObject root,
            MonkTechniqueDefinition definition)
        {
            float tier = Mathf.Clamp01((definition.Level - 1f) / 34f);
            float readability = Mathf.Lerp(1.24f, 1f, tier);
            float force = ImpactForce(definition.Key, tier);
            Color accent = MartialTone(definition.VfxColor);

            var marker = root.AddComponent<MonkImpactAmplifierMarker>();
            marker.Key = definition.Key;
            marker.Tier = tier;
            AmplifiedImpactCount++;

            // The hot contact core is the only common layer. The readable
            // outer silhouette is authored per move below so a kick, claw,
            // projectile, pressure strike, and body check cannot collapse
            // into the same radial wheel at gameplay distance.
            float coreScale = Mathf.Lerp(0.96f, 1.42f, tier) * readability *
                Mathf.Lerp(0.94f, 1.18f,
                    Mathf.InverseLerp(0.88f, 1.62f, force));
            Color coreColor = Color.Lerp(accent, ImpactIvory,
                Mathf.Lerp(0.72f, 0.46f,
                    Mathf.InverseLerp(1.18f, 1.62f, force)));
            AddTexturedFlash(root, "Technique hot contact core",
                "impact_flare.png", coreColor, coreScale,
                Mathf.Lerp(0.22f, 0.34f, force), 0f,
                definition.VfxPattern * 37f, 1.26f + force * 0.26f);
            AddBurst(root, "Technique contact fragments", accent,
                definition.VfxPattern % 3 == 0
                    ? ParticleSystemShapeType.Sphere
                    : ParticleSystemShapeType.Cone,
                Mathf.RoundToInt((22f + definition.VfxPattern % 7 * 4f) *
                    force), 0.020f, 0.055f + force * 0.026f,
                1.7f + force * 0.4f, 3.6f + force * 2.2f,
                0.30f + force * 0.16f, Vector3.zero, false, true,
                definition.VfxPattern % 2 * 0.012f, true);
            AddTechniqueImpactSignature(root, definition, accent,
                readability, force);

            string familyTexture = ImpactFamilyTexture(definition.Key);
            float familyRotation = definition.VfxPattern * 29f % 360f;
            AddTexturedFlash(root, "Technique texture accent",
                familyTexture, accent,
                Mathf.Lerp(0.86f, 1.38f, tier) * readability * force,
                familyTexture == "motion_twirl.png" ? 0.50f : 0.38f,
                0.035f + definition.VfxPattern % 3 * 0.012f,
                familyRotation,
                familyTexture == "motion_twirl.png" ? 1.62f : 1.30f);

            GameObject lightObject = new GameObject(
                "Technique impact light");
            lightObject.transform.SetParent(root.transform, false);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Color.Lerp(accent, ImpactIvory,
                force > 1.30f ? 0.28f : 0.44f);
            float lightForce = Mathf.Lerp(0.94f, 1.18f,
                Mathf.InverseLerp(0.88f, 1.62f, force));
            light.range = Mathf.Lerp(2.10f, 3.80f, tier) * lightForce;
            light.intensity = Mathf.Lerp(2.40f, 5.20f, tier) * lightForce;
            light.shadows = LightShadows.None;
            // Without ForcePixel these short-lived lights compete for the
            // quality-settings pixel-light budget against the town's own
            // lighting, lose, and silently fall back to vertex lighting, where
            // a 0.2s flash contributes nothing visible.
            light.renderMode = LightRenderMode.ForcePixel;
            var pulse = lightObject.AddComponent<MonkImpactLightPulse>();
            pulse.Duration = Mathf.Lerp(0.12f, 0.25f, tier) +
                definition.VfxPattern % 4 * 0.012f;
            pulse.Peak = light.intensity;
        }

        private static float ImpactForce(string key, float tier)
        {
            switch (key)
            {
                case "jab": return 0.92f;
                case "pressure_point": return 0.88f;
                case "tiger_claw": return 1.00f;
                case "stone_throw": return 1.03f;
                case "shoulder_check": return 1.08f;
                case "round_kick": return 1.10f;
                case "crescent_heel": return 1.12f;
                case "eagle_strike": return 1.08f;
                case "pummel": return 1.12f;
                case "fist_of_falling_rain": return 1.18f;
                case "flying_kick": return 1.22f;
                case "sweeping_crescent": return 1.20f;
                case "gale_splitting_palm": return 1.30f;
                case "whirlwind_kick": return 1.32f;
                case "dragon_punch": return 1.42f;
                case "vaulting_tempest": return 1.38f;
                case "skybreaker_rush": return 1.60f;
                case "quivering_palm": return 1.54f;
                default: return Mathf.Lerp(1f, 1.18f, tier);
            }
        }

        private static void AddTechniqueImpactSignature(GameObject root,
            MonkTechniqueDefinition definition, Color accent,
            float readability, float force)
        {
            float width = 0.050f * readability * force;
            switch (definition.Key)
            {
                case "jab":
                    AddRadialStar(root, "Jab four-point snap", ImpactIvory, 4,
                        0.025f, 0.58f, false, width, 0f, 0.20f);
                    AddRing(root, "Jab compressed air pop", accent, 0.16f,
                        24, false, Vector3.back * 0.02f, 0.018f, 0.26f,
                        Vector3.zero, 2.10f, width * 0.72f);
                    break;
                case "shoulder_check":
                    AddStroke(root, "Shoulder driving wedge", ImpactIvory,
                        new[] { new Vector3(-0.72f, -0.46f, 0f),
                            new Vector3(0.24f, 0f, 0f),
                            new Vector3(-0.72f, 0.46f, 0f) },
                        width * 1.45f, 0f, 0.38f, Vector3.zero,
                        new Vector3(0.28f, 0.72f, 1f),
                        new Vector3(1.55f, 1.15f, 1f));
                    AddBurst(root, "Shoulder displaced mass", ImpactDust,
                        ParticleSystemShapeType.Cone, 42, 0.04f, 0.15f,
                        1.8f, 5.4f, 0.58f, Vector3.down * 0.42f, false,
                        false, 0.025f, true);
                    break;
                case "tiger_claw":
                    for (int index = -1; index <= 1; index++)
                        AddArc(root, "Tiger claw gouge " + index, accent,
                            0.58f + index * 0.06f, -68f + index * 13f,
                            72f + index * 13f, 20, false,
                            new Vector3(index * 0.17f, 0f, 0f),
                            (index + 1) * 0.018f, 0.34f,
                            new Vector3(0f, 0f, -38f), 0.62f,
                            width * 1.12f);
                    break;
                case "round_kick":
                    AddArc(root, "Round kick full-body crescent", accent,
                        0.84f, -156f, 78f, 42, false, Vector3.zero, 0f,
                        0.48f, new Vector3(0f, 0f, -92f), 0.82f,
                        width * 1.38f);
                    AddArc(root, "Round kick ivory edge", ImpactIvory,
                        0.70f, -144f, 64f, 38, false,
                        Vector3.back * 0.02f, 0.025f, 0.36f,
                        new Vector3(0f, 0f, -118f), 0.96f, width * 0.72f);
                    break;
                case "pummel":
                    for (int index = 0; index < 5; index++)
                        AddRing(root, "Pummel alternating hit " + index,
                            index % 2 == 0 ? accent : ImpactIvory,
                            0.14f + index * 0.045f, 20, false,
                            new Vector3((index % 2 == 0 ? -1f : 1f) * 0.16f,
                                (index - 2) * 0.10f, -0.02f),
                            index * 0.032f, 0.24f, Vector3.zero,
                            1.30f + index * 0.14f, width * 0.70f);
                    break;
                case "stone_throw":
                    for (int index = 0; index < 3; index++)
                        AddRing(root, "Resonant palm echo " + index,
                            index == 1 ? ImpactIvory : accent,
                            0.22f + index * 0.13f, 34, false,
                            Vector3.back * (0.02f + index * 0.01f),
                            index * 0.055f, 0.46f, Vector3.zero,
                            1.82f + index * 0.28f, width * (1f-index*0.12f));
                    break;
                case "crescent_heel":
                    AddArc(root, "Crescent heel rising blade", accent, 0.88f,
                        18f, 214f, 42, false, Vector3.zero, 0f, 0.50f,
                        new Vector3(0f, 0f, 105f), 0.92f, width * 1.42f);
                    AddRadialStar(root, "Crescent heel heel-point", ImpactIvory,
                        6, 0.04f, 0.54f, false, width * 0.64f, 0.045f,
                        0.30f);
                    break;
                case "eagle_strike":
                    for (int index = -1; index <= 1; index++)
                        AddStroke(root, "Eagle descending talon " + index,
                            index == 0 ? ImpactIvory : accent,
                            new[] { new Vector3(index * 0.18f - 0.20f, 0.70f, 0f),
                                new Vector3(index * 0.10f, 0.10f, 0f),
                                new Vector3(index * 0.22f + 0.18f, -0.62f, 0f) },
                            width, (index + 1) * 0.022f, 0.38f,
                            Vector3.zero, Vector3.one * 0.46f,
                            new Vector3(1.16f, 1.48f, 1f));
                    break;
                case "fist_of_falling_rain":
                    for (int index = 0; index < 7; index++)
                    {
                        float x = (index - 3) * 0.16f;
                        AddStroke(root, "Falling rain strike " + index,
                            index % 3 == 0 ? ImpactIvory : accent,
                            new[] { new Vector3(x - 0.12f, 0.78f, 0f),
                                new Vector3(x + 0.08f, -0.68f, 0f) },
                            width * 0.72f, index * 0.018f, 0.34f,
                            Vector3.zero, new Vector3(1f, 0.28f, 1f),
                            new Vector3(1.18f, 1.35f, 1f));
                    }
                    break;
                case "flying_kick":
                    for (int index = 0; index < 3; index++)
                        AddRing(root, "Flying kick pressure tunnel " + index,
                            index == 1 ? ImpactIvory : accent,
                            0.26f + index * 0.18f, 32, false,
                            Vector3.forward * (0.10f + index * 0.12f),
                            index * 0.035f, 0.38f, Vector3.zero,
                            1.55f + index * 0.24f, width * (1.10f-index*0.15f));
                    break;
                case "sweeping_crescent":
                    AddArc(root, "Sweeping crescent ground blade", accent,
                        1.02f, -178f, 32f, 44, true,
                        Vector3.down * 0.38f, 0f, 0.52f,
                        new Vector3(0f, -128f, 0f), 1.05f, width * 1.44f);
                    AddBurst(root, "Sweeping crescent ground spray", ImpactDust,
                        ParticleSystemShapeType.Circle, 58, 0.035f, 0.13f,
                        0.65f, 3.8f, 0.62f, Vector3.down * 0.36f, false,
                        true, 0.015f, true);
                    break;
                case "pressure_point":
                    AddRadialStar(root, "Pressure point needle star",
                        ImpactIvory, 8, 0.012f, 0.72f, false,
                        width * 0.52f, 0f, 0.24f);
                    AddRing(root, "Pressure point implosion", accent, 0.74f,
                        36, false, Vector3.back * 0.02f, 0.035f, 0.34f,
                        Vector3.zero, -0.62f, width * 0.58f);
                    break;
                case "gale_splitting_palm":
                    AddArc(root, "Gale shear impact edge", ImpactIvory, 1.06f,
                        -142f, 142f, 48, false, Vector3.zero, 0f, 0.42f,
                        new Vector3(0f, 0f, 168f), 1.18f, width * 1.30f);
                    AddArc(root, "Gale shear counter-wave", accent, 0.82f,
                        38f, 322f, 44, false, Vector3.back * 0.03f, 0.04f,
                        0.46f, new Vector3(0f, 0f, -214f), 1.36f,
                        width * 0.86f);
                    break;
                case "whirlwind_kick":
                    AddSpiral(root, "Whirlwind kick outer helix", accent,
                        0.72f, 1.32f, 2.5f, 54, width * 1.16f, 0.60f,
                        true, 0f);
                    AddSpiral(root, "Whirlwind kick ivory helix", ImpactIvory,
                        0.48f, 1.10f, -2.8f, 50, width * 0.68f, 0.52f,
                        true, 0.035f);
                    break;
                case "dragon_punch":
                    AddSpiral(root, "Dragon fist rising vortex", accent,
                        0.64f, 2.05f, 3.2f, 64, width * 1.30f, 0.70f,
                        true, 0f);
                    AddRadialStar(root, "Dragon fist crown", ImpactIvory, 10,
                        0.08f, 1.02f, false, width * 0.76f, 0.055f,
                        0.42f);
                    break;
                case "vaulting_tempest":
                    AddArc(root, "Vaulting tempest released power wave", accent,
                        1.28f, -168f, 58f, 52, false, Vector3.zero, 0f,
                        0.58f, new Vector3(0f, 0f, -148f), 1.24f,
                        width * 1.62f);
                    AddBurst(root, "Vaulting tempest airborne shards",
                        ImpactIvory, ParticleSystemShapeType.Cone, 76,
                        0.026f, 0.11f, 2.6f, 7.2f, 0.58f, Vector3.zero,
                        true, true, 0.02f, true);
                    break;
                case "skybreaker_rush":
                    AddPolygon(root, "Skybreaker detonation frame", accent, 8,
                        0.88f, false, Vector3.back * 0.04f, 0f, 0.60f,
                        new Vector3(0f, 0f, 220f), 1.46f, width * 1.72f);
                    AddRadialStar(root, "Skybreaker explosive release",
                        ImpactIvory, 16, 0.08f, 1.42f, false,
                        width * 1.04f, 0.025f, 0.48f);
                    break;
                case "quivering_palm":
                    AddPolygon(root, "Quivering palm sealed core", accent, 8,
                        0.58f, false, Vector3.back * 0.03f, 0f, 0.48f,
                        new Vector3(0f, 0f, -240f), -0.34f,
                        width * 1.12f);
                    AddRing(root, "Quivering palm delayed rupture", ImpactIvory,
                        0.18f, 44, false, Vector3.back * 0.05f, 0.085f,
                        0.54f, Vector3.zero, 5.80f, width * 1.42f);
                    AddRadialStar(root, "Quivering palm vibration fracture",
                        accent, 12, 0.04f, 1.22f, false, width * 0.78f,
                        0.12f, 0.46f);
                    break;
            }
        }

        private static string ImpactFamilyTexture(string key)
        {
            switch (key)
            {
                case "round_kick":
                case "crescent_heel":
                case "flying_kick":
                case "vaulting_tempest":
                case "sweeping_crescent":
                case "tiger_claw":
                case "eagle_strike":
                    return "impact_slash.png";
                case "whirlwind_kick":
                case "dragon_punch":
                case "skybreaker_rush":
                case "hundred_fists":
                    return "motion_twirl.png";
                case "stone_throw":
                case "countering_palm":
                case "pressure_point":
                case "quivering_palm":
                    return "impact_soft.png";
                default:
                    return "impact_flare.png";
            }
        }

        private static void AddTexturedFlash(GameObject root, string name,
            string textureName, Color color, float size, float lifetime,
            float delay, float rotationDegrees, float expansion)
        {
            Material material = GetTextureMaterial(textureName);
            if (material == null) return;
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            // Keep the authored sprite slightly in front of the target-facing
            // contact plane so character geometry cannot swallow its hot core.
            child.transform.localPosition = Vector3.back * 0.055f;
            ParticleSystem system = child.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.duration = 0.08f;
            main.startDelay = delay;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.startSize = size;
            main.startRotation = rotationDegrees * Mathf.Deg2Rad;
            main.startColor = Hdr(color, CoreGain * 0.72f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 2;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            ParticleSystem.ColorOverLifetimeModule colors =
                system.colorOverLifetime;
            colors.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[]
            {
                new GradientColorKey(Color.Lerp(color, Color.white, 0.70f), 0f),
                new GradientColorKey(color, 0.22f),
                new GradientColorKey(RimOf(color), 1f)
            }, new[]
            {
                new GradientAlphaKey(0.96f, 0f),
                new GradientAlphaKey(0.82f, 0.32f),
                new GradientAlphaKey(0f, 1f)
            });
            colors.color = gradient;
            ParticleSystem.SizeOverLifetimeModule sizes =
                system.sizeOverLifetime;
            sizes.enabled = true;
            sizes.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.34f),
                    new Keyframe(0.10f, 1f),
                    new Keyframe(1f, expansion)));
            ParticleSystem.RotationOverLifetimeModule rotation =
                system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(
                -0.55f, 0.55f);
            ParticleSystemRenderer renderer =
                child.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            system.Play();
        }

        private static Material GetTextureMaterial(string textureName)
        {
            if (TextureMaterials.TryGetValue(textureName,
                out Material cached))
                return cached;
            Texture2D texture = EmbeddedResources.LoadTexture(textureName,
                "CS Monk Authored " + textureName);
            if (texture == null)
            {
                TextureMaterials[textureName] = null;
                return null;
            }
            Material material = CreateAdditiveMaterial(texture, textureName);
            TextureMaterials[textureName] = material;
            return material;
        }

        private static void AddMartialImpact(GameObject root, Color accent,
            float strength, bool dust, float delay)
        {
            float clamped = Mathf.Clamp(strength, 0.55f, 1.40f);
            float contactRadius = 0.40f + clamped * 0.18f;
            float driveLength = 0.68f + clamped * 0.32f;
            // A real textured shockwave under the line work. The stroke and
            // ring layers describe the shape of the hit; this supplies the
            // bright soft mass that makes it land, using the authored 512px
            // sheets the addon already ships rather than more vector geometry.
            AddTexturedFlash(root, "Impact pressure bloom",
                "impact_flare.png", accent,
                0.62f + clamped * 0.52f, 0.20f + clamped * 0.055f,
                delay, clamped * 37f, 1.85f);
            AddTexturedFlash(root, "Impact contact wash",
                "impact_soft.png", ImpactIvory,
                0.44f + clamped * 0.34f, 0.155f + clamped * 0.04f,
                delay, -clamped * 24f, 1.35f);
            AddStroke(root, "Impact directional core", ImpactIvory, new[]
            {
                new Vector3(0f, 0f, -driveLength),
                new Vector3(0f, 0f, -0.18f),
                new Vector3(0f, 0f, 0.24f + clamped * 0.10f)
            }, 0.044f + clamped * 0.020f, delay, 0.32f,
                Vector3.zero, new Vector3(0.52f, 0.52f, 0.34f),
                new Vector3(1.08f, 1.08f, 1.24f));
            AddStroke(root, "Impact directional chevron", accent, new[]
            {
                new Vector3(-0.24f - clamped * 0.05f,
                    0.15f + clamped * 0.025f, -0.12f),
                new Vector3(0f, 0f, 0.28f + clamped * 0.10f),
                new Vector3(0.24f + clamped * 0.05f,
                    -0.15f - clamped * 0.025f, -0.12f)
            }, 0.052f + clamped * 0.019f, delay + 0.010f, 0.36f,
                Vector3.zero, Vector3.one * 0.58f,
                Vector3.one * 1.16f);
            AddRadialStar(root, "Impact contact rays", ImpactIvory,
                clamped >= 1.08f ? 8 : 6, 0.045f, contactRadius, false,
                0.032f + clamped * 0.011f, delay, 0.32f);
            AddRing(root, "Impact compression", ImpactIvory,
                0.15f + clamped * 0.045f, 18, false, Vector3.zero,
                delay, 0.31f, Vector3.zero, 1.70f + clamped * 0.48f,
                0.036f + clamped * 0.014f);
            AddRing(root, "Impact accent echo", accent,
                0.21f + clamped * 0.045f, 20, false, Vector3.zero,
                delay + 0.035f, 0.38f,
                new Vector3(0f, 0f, clamped >= 1.08f ? 72f : 42f),
                0.62f + clamped * 0.24f,
                0.022f + clamped * 0.009f);
            AddBurst(root, "Impact flash", ImpactIvory,
                ParticleSystemShapeType.Sphere,
                Mathf.RoundToInt(12f + clamped * 8f),
                0.070f, 0.125f + clamped * 0.040f,
                0.22f, 0.95f + clamped * 0.42f,
                0.29f, Vector3.zero, false, false, delay, true);
            AddBurst(root, "Impact sparks", accent,
                ParticleSystemShapeType.Cone,
                Mathf.RoundToInt(18f + clamped * 11f),
                0.020f, 0.045f + clamped * 0.018f,
                1.35f, 2.80f + clamped * 1.15f,
                0.43f, Vector3.zero, false, true, delay, true);
            if (!dust) return;
            AddBurst(root, "Impact ground dust", ImpactDust,
                ParticleSystemShapeType.Circle,
                Mathf.RoundToInt(20f + clamped * 9f),
                0.045f, 0.090f + clamped * 0.026f,
                0.24f, 0.88f + clamped * 0.50f,
                0.50f, Vector3.down * 0.66f, false, false,
                delay + 0.018f, true);
        }

        private static GameObject AddRing(GameObject root, string name,
            Color color, float radius, int points, bool horizontal,
            Vector3 position, float delay, float duration, Vector3 spin,
            float expansion, float width)
        {
            return AddArc(root, name, color, radius, 0f, 360f, points,
                horizontal, position, delay, duration, spin, expansion,
                width, true, true);
        }

        private static GameObject AddPolygon(GameObject root, string name,
            Color color, int sides, float radius, bool horizontal,
            Vector3 position, float delay, float duration, Vector3 spin,
            float expansion, float width)
        {
            return AddArc(root, name, color, radius, 0f, 360f, sides,
                horizontal, position, delay, duration, spin, expansion,
                width, true, false);
        }

        private static GameObject AddArc(GameObject root, string name,
            Color color, float radius, float startDegrees, float endDegrees,
            int points, bool horizontal, Vector3 position, float delay,
            float duration, Vector3 spin, float expansion, float width,
            bool loop = false, bool smooth = false)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = position;
            LineRenderer line = ConfigureLine(child, color, width);
            line.loop = loop;
            // Rings were drawn with as few as 12-20 segments, which reads as a
            // visible polygon rather than a curve. Only shapes that are meant
            // to be round get the 48-segment floor; AddPolygon's authored
            // seals must keep their exact side count.
            line.positionCount = smooth
                ? Mathf.Max(48, points)
                : Mathf.Max(2, points);
            for (int index = 0; index < line.positionCount; index++)
            {
                float denominator = loop ? line.positionCount :
                    line.positionCount - 1f;
                float t = index / denominator;
                float angle = Mathf.Lerp(startDegrees, endDegrees, t) *
                    Mathf.Deg2Rad;
                Vector3 value = horizontal
                    ? new Vector3(Mathf.Cos(angle) * radius, 0f,
                        Mathf.Sin(angle) * radius)
                    : new Vector3(Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius, 0f);
                line.SetPosition(index, value);
            }
            ConfigureMotion(child, delay, duration, spin,
                Vector3.one * Mathf.Max(0.08f, 1f - expansion * 0.32f),
                Vector3.one * Mathf.Max(0.12f, 1f + expansion));
            return child;
        }

        private static GameObject AddStroke(GameObject root, string name,
            Color color, Vector3[] points, float width, float delay,
            float duration, Vector3 spin, Vector3 startScale,
            Vector3 endScale)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            LineRenderer line = ConfigureLine(child, color, width);
            line.loop = false;
            line.positionCount = points.Length;
            line.SetPositions(points);
            ConfigureMotion(child, delay, duration, spin, startScale,
                endScale);
            return child;
        }

        private static void AddWorldStroke(GameObject root, string name,
            Color color, Vector3[] points, float width, float delay,
            float duration)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            LineRenderer line = ConfigureLine(child, color, width);
            line.useWorldSpace = true;
            line.positionCount = points.Length;
            line.SetPositions(points);
            ConfigureMotion(child, delay, duration, Vector3.zero,
                Vector3.one, Vector3.one);
        }

        private static void AddWorldRing(GameObject root, string name,
            Color color, Vector3 position, float radius, float delay,
            float duration)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            LineRenderer line = ConfigureLine(child, color, 0.025f);
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = 12;
            for (int index = 0; index < line.positionCount; index++)
            {
                float angle = index / (float)line.positionCount *
                    Mathf.PI * 2f;
                line.SetPosition(index, position + new Vector3(
                    Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius,
                    0f));
            }
            ConfigureMotion(child, delay, duration, Vector3.zero,
                Vector3.one * 0.6f, Vector3.one * 1.4f);
        }

        private static void AddSpiral(GameObject root, string name,
            Color color, float radius, float height, float turns, int points,
            float width, float duration, bool vertical, float delay)
        {
            var values = new Vector3[points];
            for (int index = 0; index < points; index++)
            {
                float t = index / (float)(points - 1);
                float angle = turns * Mathf.PI * 2f * t;
                float taperedRadius = radius * (0.72f +
                    Mathf.Sin(t * Mathf.PI) * 0.28f);
                values[index] = vertical
                    ? new Vector3(Mathf.Cos(angle) * taperedRadius,
                        Mathf.Lerp(-height * 0.5f, height * 0.5f, t),
                        Mathf.Sin(angle) * taperedRadius)
                    : new Vector3(Mathf.Cos(angle) * taperedRadius,
                        -0.70f + t * height,
                        Mathf.Sin(angle) * taperedRadius);
            }
            AddStroke(root, name, color, values, width, delay, duration,
                vertical ? new Vector3(0f, turns * 45f, 0f) : Vector3.zero,
                Vector3.one * 0.72f, Vector3.one * 1.08f);
        }

        private static void AddRadialStar(GameObject root, string name,
            Color color, int rays, float innerRadius, float outerRadius,
            bool horizontal, float width, float delay, float duration)
        {
            for (int index = 0; index < rays; index++)
            {
                float angle = index / (float)rays * Mathf.PI * 2f;
                Vector3 inner = horizontal
                    ? new Vector3(Mathf.Cos(angle) * innerRadius, -0.68f,
                        Mathf.Sin(angle) * innerRadius)
                    : new Vector3(Mathf.Cos(angle) * innerRadius,
                        Mathf.Sin(angle) * innerRadius, 0f);
                Vector3 outer = horizontal
                    ? new Vector3(Mathf.Cos(angle) * outerRadius, -0.68f,
                        Mathf.Sin(angle) * outerRadius)
                    : new Vector3(Mathf.Cos(angle) * outerRadius,
                        Mathf.Sin(angle) * outerRadius, 0f);
                AddStroke(root, name + " " + index, color,
                    new[] { inner, outer }, width, delay + index * 0.006f,
                    duration, Vector3.zero, Vector3.one * 0.35f,
                    Vector3.one * 1.25f);
            }
        }

        private static void AddFootprint(GameObject root, string name,
            Color color, Vector3 position, float yaw, float delay)
        {
            var child = AddStroke(root, name, color, new[]
            {
                new Vector3(-0.10f, 0f, -0.22f),
                new Vector3(-0.14f, 0f, 0.04f),
                new Vector3(-0.06f, 0f, 0.25f),
                new Vector3(0.06f, 0f, 0.25f),
                new Vector3(0.14f, 0f, 0.04f),
                new Vector3(0.10f, 0f, -0.22f),
                new Vector3(-0.10f, 0f, -0.22f)
            }, 0.035f, delay, 0.92f, Vector3.zero,
                Vector3.one * 0.85f, Vector3.one * 1.18f);
            child.transform.localPosition = position;
            child.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private static void AddPlate(GameObject root, string name,
            Color color, Vector3 position, float yaw, float delay)
        {
            var child = AddStroke(root, name, color, new[]
            {
                new Vector3(-0.24f, -0.34f, 0f),
                new Vector3(-0.32f, 0.18f, 0f),
                new Vector3(-0.08f, 0.42f, 0f),
                new Vector3(0.28f, 0.22f, 0f),
                new Vector3(0.22f, -0.34f, 0f),
                new Vector3(-0.24f, -0.34f, 0f)
            }, 0.075f, delay, 1.05f,
                new Vector3(0f, yaw * 0.35f, 0f),
                new Vector3(0.45f, 0.25f, 0.45f), Vector3.one);
            child.transform.localPosition = position;
            child.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private static void AddBurst(GameObject root, string name,
            Color color, ParticleSystemShapeType shapeType, int count,
            float minSize, float maxSize, float minSpeed, float maxSpeed,
            float lifetime, Vector3 position, bool rise, bool streak,
            float delay = 0f, bool immediateAlpha = false,
            bool authoredImpact = false)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = position;
            ParticleSystem system = child.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.duration = Mathf.Max(0.10f, lifetime * 0.45f);
            main.startDelay = delay;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                lifetime * 0.45f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed,
                maxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize,
                maxSize);
            main.startColor = new ParticleSystem.MinMaxGradient(
                CoreOf(color), Hdr(color, BodyGain));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = Mathf.Max(count + 8, 24);
            main.gravityModifier = rise ? -0.12f : 0.18f;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)count)
            });
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = shapeType;
            shape.radius = shapeType == ParticleSystemShapeType.Circle
                ? 0.72f : 0.34f;
            if (shapeType == ParticleSystemShapeType.Cone)
            {
                shape.angle = 24f;
                shape.radius = 0.10f;
            }

            ParticleSystem.ColorOverLifetimeModule colors =
                system.colorOverLifetime;
            colors.enabled = true;
            var gradient = new Gradient();
            // Gradient keys are clamped to LDR, so the HDR core is applied on
            // the main module's start colour and this ramp carries the shape:
            // hot centre, technique hue through the body, deep saturated rim
            // before it fades. The old ramp opened on pure white for every one
            // of the 32 techniques, discarding the identity colour in the
            // brightest and most-noticed frame.
            gradient.SetKeys(new[]
            {
                new GradientColorKey(Color.Lerp(color, Color.white, 0.78f), 0f),
                new GradientColorKey(color, 0.34f),
                new GradientColorKey(RimOf(color), 1f)
            }, new[]
            {
                new GradientAlphaKey(immediateAlpha ? 0.78f : 0f, 0f),
                new GradientAlphaKey(1f, immediateAlpha ? 0.06f : 0.10f),
                new GradientAlphaKey(0.78f, 0.55f),
                new GradientAlphaKey(0f, 1f)
            });
            colors.color = gradient;
            ParticleSystem.SizeOverLifetimeModule sizes =
                system.sizeOverLifetime;
            sizes.enabled = true;
            sizes.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.25f),
                    new Keyframe(0.18f, 1f), new Keyframe(1f, 0f)));

            ParticleSystemRenderer renderer =
                child.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = streak
                ? ParticleSystemRenderMode.Stretch
                : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = streak ? 2.8f : 1f;
            renderer.velocityScale = streak ? 0.22f : 0f;
            renderer.sharedMaterial = authoredImpact
                ? GetImpactParticleMaterial()
                : GetParticleMaterial(streak);
            if (authoredImpact && _impactParticleSheet != null)
            {
                ParticleSystem.TextureSheetAnimationModule sheet =
                    system.textureSheetAnimation;
                sheet.enabled = true;
                sheet.mode = ParticleSystemAnimationMode.Grid;
                sheet.numTilesX = 4;
                sheet.numTilesY = 2;
                sheet.animation = ParticleSystemAnimationType.WholeSheet;
                sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f,
                    AnimationCurve.Linear(0f, 0f, 1f, 1f));
                sheet.cycleCount = 1;
            }
            system.Play();
        }

        private static LineRenderer ConfigureLine(GameObject target,
            Color color, float width)
        {
            LineRenderer line = target.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.widthMultiplier = width;
            // Three cap and corner vertices left visible faceting on the tight
            // arcs and fast crescents; eight rounds them without a meaningful
            // vertex cost on strokes this short.
            line.numCapVertices = 8;
            line.numCornerVertices = 8;
            // Hot leading edge fading to a deep saturated tail. The old
            // white-lerp start and flat 0.58-alpha end gave every stroke the
            // same matte pastel body regardless of technique.
            line.startColor = CoreOf(color);
            line.endColor = Hdr(color, BodyGain);
            line.sharedMaterial = GetLineMaterial();
            line.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage =
                UnityEngine.Rendering.LightProbeUsage.Off;
            return line;
        }

        private static void ConfigureMotion(GameObject target, float delay,
            float duration, Vector3 spin, Vector3 startScale,
            Vector3 endScale)
        {
            MonkVfxMotion motion = target.AddComponent<MonkVfxMotion>();
            motion.Configure(delay, duration, spin, startScale, endScale);
        }

        /// <summary>
        /// Shared additive factory. Prefers the built-in particle shader that
        /// supports soft-particle depth fade, which removes the hard seam where
        /// an effect cuts through the character or the ground, and falls back to
        /// legacy additive when that shader or its properties are unavailable so
        /// a missing shader can never render the effect opaque or magenta.
        /// </summary>
        private static Material CreateAdditiveMaterial(Texture2D texture,
            string label)
        {
            Shader modern = Shader.Find("Particles/Standard Unlit");
            if (modern != null)
            {
                var candidate = new Material(modern)
                {
                    color = Color.white,
                    hideFlags = HideFlags.HideAndDontSave
                };
                bool configurable = candidate.HasProperty("_Mode") &&
                    candidate.HasProperty("_SrcBlend") &&
                    candidate.HasProperty("_DstBlend") &&
                    candidate.HasProperty("_MainTex");
                if (configurable)
                {
                    // Additive: One / One, no depth write, transparent queue.
                    candidate.SetFloat("_Mode", 4f);
                    candidate.SetInt("_SrcBlend",
                        (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    candidate.SetInt("_DstBlend",
                        (int)UnityEngine.Rendering.BlendMode.One);
                    candidate.SetInt("_ZWrite", 0);
                    candidate.DisableKeyword("_ALPHATEST_ON");
                    candidate.DisableKeyword("_ALPHABLEND_ON");
                    candidate.EnableKeyword("_ALPHAMODULATE_ON");
                    candidate.renderQueue = 3000;
                    if (candidate.HasProperty("_SoftParticleFadeParams"))
                    {
                        candidate.EnableKeyword("_SOFTPARTICLES_ON");
                        candidate.SetVector("_SoftParticleFadeParams",
                            new Vector4(0f, 1f / 0.55f, 0f, 0f));
                        SoftParticlesActive = true;
                    }
                    if (texture != null)
                        candidate.SetTexture("_MainTex", texture);
                    MaterialPathDescription = "Particles/Standard Unlit " +
                        "additive" + (SoftParticlesActive
                            ? " with soft-particle depth fade" : "");
                    return candidate;
                }
                UnityEngine.Object.DestroyImmediate(candidate);
            }

            Shader legacy =
                Shader.Find("Legacy Shaders/Particles/Additive") ??
                Shader.Find("Particles/Additive") ??
                Shader.Find("Sprites/Default");
            var fallback = new Material(legacy)
            {
                color = Color.white,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (texture != null && fallback.HasProperty("_MainTex"))
                fallback.SetTexture("_MainTex", texture);
            fallback.renderQueue = 3000;
            MaterialPathDescription = "legacy additive fallback (" +
                (legacy != null ? legacy.name : "none") + ") for " + label;
            return fallback;
        }

        /// <summary>
        /// Line and trail ribbons previously used the untextured
        /// <c>Sprites/Default</c> shader, which renders a matte alpha-blended
        /// strip with a hard silhouette and no glow. An additive ribbon texture
        /// feathers the edges across the width and gives the stroke a bright
        /// centre, so a trail reads as light rather than as cut paper.
        /// </summary>
        private static Material GetLineMaterial()
        {
            if (_lineMaterial != null) return _lineMaterial;
            _lineMaterial = CreateAdditiveMaterial(BuildRibbonTexture(),
                "ribbon");
            return _lineMaterial;
        }

        private static Material GetParticleMaterial(bool streak)
        {
            Material cached = streak ? _streakParticleMaterial :
                _softParticleMaterial;
            if (cached != null) return cached;
            Material material = CreateAdditiveMaterial(
                BuildParticleTexture(streak),
                streak ? "streak particle" : "soft particle");
            if (streak) _streakParticleMaterial = material;
            else _softParticleMaterial = material;
            return material;
        }

        private static Material GetImpactParticleMaterial()
        {
            if (_impactParticleMaterialInitialized)
                return _impactParticleMaterial ??
                    GetParticleMaterial(false);
            _impactParticleMaterialInitialized = true;
            _impactParticleSheet = EmbeddedResources.LoadTexture(
                "MonkRadialLightning13Rice.png",
                "CS Monk CC0 Radial Impact Sheet");
            if (_impactParticleSheet == null)
            {
                SkillsPlugin.Log.LogWarning(
                    "Monk VFX: falling back to the procedural impact particle.");
                return GetParticleMaterial(false);
            }

            _impactParticleMaterial = CreateAdditiveMaterial(
                _impactParticleSheet, "authored impact sheet");
            return _impactParticleMaterial;
        }

        /// <summary>
        /// The particle sprite was a 32x32 unmipped disc with a single
        /// smoothstep falloff: too low-resolution for the size it is drawn at,
        /// aliasing at distance for want of mips, and flat because the whole
        /// disc sits at one brightness. This builds it at 128px (256 wide for
        /// stretched streaks) with mipmaps, and layers a tight bright core over
        /// a wide soft halo so the particle has a centre instead of being a
        /// uniform blob.
        /// </summary>
        private static Texture2D BuildParticleTexture(bool streak)
        {
            int width = streak ? 256 : 128;
            int height = 128;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32,
                true)
            {
                name = streak ? "CS Monk Streak" : "CS Monk Soft Disc",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 2,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                float vertical = Mathf.Abs((y + 0.5f) / height * 2f - 1f);
                for (int x = 0; x < width; x++)
                {
                    float horizontal = Mathf.Abs((x + 0.5f) / width * 2f - 1f);
                    float distance = streak
                        ? Mathf.Sqrt(horizontal * horizontal * 0.18f +
                            vertical * vertical)
                        : Mathf.Sqrt(horizontal * horizontal +
                            vertical * vertical);
                    float falloff = Mathf.Clamp01(1f - distance);
                    // Wide halo carries the technique colour outward; the cubed
                    // core keeps a small hot centre that survives bloom.
                    float halo = Mathf.Pow(falloff, 0.85f) * 0.55f;
                    float core = Mathf.Pow(falloff, 3.2f);
                    float alpha = Mathf.Clamp01(halo + core * 0.75f);
                    float luminance = Mathf.Clamp01(0.62f + core * 0.38f);
                    pixels[y * width + x] = new Color(luminance, luminance,
                        luminance, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }

        /// <summary>
        /// Ribbon sprite for LineRenderer and TrailRenderer. U runs along the
        /// stroke and V across its width, so the width profile is what softens
        /// the silhouette: a bright thin core with a feathered falloff to fully
        /// transparent at both edges.
        /// </summary>
        private static Texture2D BuildRibbonTexture()
        {
            const int width = 16;
            const int height = 128;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32,
                true)
            {
                name = "CS Monk Ribbon",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 2,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                float across = Mathf.Abs((y + 0.5f) / height * 2f - 1f);
                float falloff = Mathf.Clamp01(1f - across);
                float edge = Mathf.Pow(falloff, 1.35f) * 0.62f;
                float core = Mathf.Pow(falloff, 5.5f);
                float alpha = Mathf.Clamp01(edge + core * 0.85f);
                float luminance = Mathf.Clamp01(0.55f + core * 0.45f);
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = new Color(luminance, luminance,
                        luminance, alpha);
            }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }

        internal static Color AttackAccent(Color color) =>
            MartialTone(color);

        internal static Color AttackIvory => ImpactIvory;

        internal static float ImpactHeight(string key)
        {
            if (key == "sweeping_crescent") return 0.42f;
            if (key == "round_kick" || key == "crescent_heel" ||
                key == "flying_kick" || key == "whirlwind_kick" ||
                key == "vaulting_tempest")
                return 0.72f;
            if (key == "dragon_punch") return 1.25f;
            return 1.02f;
        }

        internal static Material AttackLineMaterial() =>
            GetLineMaterial();

        internal static Material AttackParticleMaterial() =>
            GetParticleMaterial(false);

        internal static bool Validate(out string detail)
        {
            List<MonkTechniqueDefinition> definitions =
                MonkSkillData.All.ToList();
            bool exactCount = definitions.Count == PatternNames.Length &&
                PatternNames.Length == MonkSkillData.ExpectedCustomCount;
            bool sequentialUnique = definitions.Select(value =>
                    value.VfxPattern).OrderBy(value => value)
                .SequenceEqual(Enumerable.Range(0, PatternNames.Length));
            bool namedUnique = PatternNames.Distinct(
                StringComparer.OrdinalIgnoreCase).Count() ==
                PatternNames.Length;
            bool validColors = definitions.All(value =>
                value.VfxColor.a > 0.99f &&
                value.VfxColor.maxColorComponent > 0.40f);
            bool audio = MonkAudio.Validate(out string audioDetail);
            bool readyIdle = HandToHandReadyIdle.Validate(
                out string readyIdleDetail);
            bool authoredImpact =
                EmbeddedResources.GetBytesByFilename(
                    "MonkRadialLightning13Rice.png") != null &&
                new[] { "impact_soft.png", "impact_flare.png",
                    "impact_slash.png", "motion_twirl.png" }.All(value =>
                    EmbeddedResources.GetBytesByFilename(value) != null);
            bool offensiveIdentity = definitions
                .Where(value => value.IsOffensive)
                .Select(value => value.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
                definitions.Count(value => value.IsOffensive);
            bool authoredSignatures = definitions
                .Where(value => value.IsOffensive)
                .All(value => OffensiveSignatureKeys.Contains(value.Key)) &&
                definitions.Where(value => !value.IsOffensive)
                .All(value => UtilitySignatureKeys.Contains(value.Key)) &&
                OffensiveSignatureKeys.Count + UtilitySignatureKeys.Count ==
                PatternNames.Length;
            bool valid = exactCount && sequentialUnique && namedUnique &&
                validColors && offensiveIdentity && authoredSignatures &&
                authoredImpact && audio && readyIdle;
            // Touch the shared material so the reported path reflects the
            // shader that actually resolved on this machine rather than the
            // uninitialized default.
            GetLineMaterial();
            detail = valid
                ? $"{PatternNames.Length} bespoke silhouettes: punch trails, kick crescents, projectiles, wards, helices, constellations, and palm seals; 128px mipped soft/streak textures on {MaterialPathDescription}; HDR cores at {CoreGain:0.0}x over the game's bloom threshold; bounded cleanup"
                  + "; CC0 animated radial contact sheet plus flare, pressure, slash, and twirl layers; 18 offensive contact signatures and 14 utility signatures with per-technique force, light, and camera profiles; " +
                  audioDetail + "; " + readyIdleDetail
                : "Monk VFX pattern, authored impact, sound, ready-idle, identity, sequence, or palette is invalid";
            return valid;
        }
    }

    internal sealed class MonkImpactAmplifierMarker : MonoBehaviour
    {
        internal string Key;
        internal float Tier;
    }

    internal sealed class MonkImpactLightPulse : MonoBehaviour
    {
        internal float Duration;
        internal float Peak;
        private Light _light;
        private float _elapsed;

        private void Awake() => _light = GetComponent<Light>();

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed /
                Mathf.Max(0.05f, Duration));
            if (_light != null)
                _light.intensity = Peak *
                    (1f - Mathf.SmoothStep(0f, 1f, t));
            if (t >= 1f) Destroy(gameObject);
        }
    }

    /// <summary>
    /// Animation-spanning martial presentation. The charge and motes follow
    /// the actual striking limb, TrailRenderers preserve its authored path,
    /// and a short tapered ribbon points toward the live target before the
    /// contact-time pattern takes over.
    /// </summary>
    internal sealed class MonkAttackVfxDriver : MonoBehaviour
    {
        private const int RingPoints = 20;
        private MonkTechniqueDefinition _definition;
        private Transform _caster;
        private Transform _target;
        private Transform _primaryBone;
        private Transform _secondaryBone;
        private Transform _primaryAnchor;
        private Transform _secondaryAnchor;
        private TrailRenderer _primaryTrail;
        private TrailRenderer _secondaryTrail;
        private ParticleSystem _primaryMotes;
        private ParticleSystem _secondaryMotes;
        private LineRenderer _directionOuter;
        private LineRenderer _directionCore;
        private LineRenderer _chargeRing;
        private Transform _palmProjectile;
        private LineRenderer[] _palmProjectileRings;
        private TrailRenderer _palmProjectileWake;
        private ParticleSystem _palmProjectileMotes;
        private Transform _vaultWave;
        private LineRenderer[] _vaultWaveArcs;
        private bool _vaultWaveReleased;
        private float _vaultWaveStarted;
        private Vector3 _vaultWaveOrigin;
        private Quaternion _vaultWaveRotation;
        private Transform _skyCharge;
        private LineRenderer[] _skyChargeArcs;
        private Color _accent;
        private Color _ivory;
        private float _duration;
        private float _releaseTime;
        private float _impactTime;
        private float _started;
        private float _tier;
        private float _impactHeight;
        private bool _released;
        private float _releasedAt;
        private bool IsPalmProjectile => _definition != null &&
            _definition.Kind == MonkTechniqueKind.StoneThrow;
        private bool IsGaleShear => _definition != null &&
            _definition.Key == "gale_splitting_palm";
        private bool IsVaultingTempest => _definition != null &&
            _definition.Key == "vaulting_tempest";
        private bool IsSkybreakerRush => _definition != null &&
            _definition.Key == "skybreaker_rush";

        internal void Configure(MonkTechniqueDefinition definition,
            Animator animator, Transform caster, Transform target,
            float duration, float impactTime)
        {
            _definition = definition;
            _caster = caster;
            _target = target;
            _duration = Mathf.Max(0.24f, duration);
            _releaseTime = Mathf.Clamp(impactTime, 0.08f,
                _duration - 0.04f);
            _impactTime = IsPalmProjectile
                ? Mathf.Min(_duration - 0.04f, _releaseTime +
                    (IsGaleShear ? 0.22f : 0.20f))
                : _releaseTime;
            _tier = Mathf.Clamp01((definition.Level - 1f) / 34f);
            _accent = IsPalmProjectile
                ? Color.Lerp(definition.VfxColor, Color.white, 0.08f)
                : MonkVfx.AttackAccent(definition.VfxColor);
            _accent.a = 1f;
            _ivory = MonkVfx.AttackIvory;
            ResolveBones(animator);
            _impactHeight = ImpactHeight(definition.Key);

            float trailWidth = Mathf.Lerp(0.098f, 0.186f, _tier);
            if (IsVaultingTempest) trailWidth *= 2.20f;
            else if (IsSkybreakerRush) trailWidth *= 1.55f;
            else if (IsGaleShear) trailWidth *= 1.16f;
            _primaryAnchor = CreateMotionAnchor("Primary martial trail",
                _primaryBone, trailWidth, out _primaryTrail,
                out _primaryMotes);
            if (_secondaryBone != null)
                _secondaryAnchor = CreateMotionAnchor(
                    "Secondary martial trail", _secondaryBone,
                    trailWidth * 0.82f, out _secondaryTrail,
                    out _secondaryMotes);

            _directionOuter = CreateDirectionLine(
                "Martial direction accent", _accent,
                Mathf.Lerp(0.108f, 0.184f, _tier));
            _directionCore = CreateDirectionLine(
                "Martial direction core", _ivory,
                Mathf.Lerp(0.040f, 0.072f, _tier));
            _chargeRing = CreateDirectionLine(
                "Martial wind-up focus", _accent,
                Mathf.Lerp(0.030f, 0.052f, _tier));
            _chargeRing.loop = true;
            _chargeRing.positionCount = RingPoints;
            if (IsPalmProjectile)
                CreatePalmProjectile();
            if (IsVaultingTempest)
                CreateVaultWave();
            if (IsSkybreakerRush)
                CreateSkybreakerCharge();
            _started = Time.time;
            SetLineAlpha(_directionOuter, _accent, 0f);
            SetLineAlpha(_directionCore, _ivory, 0f);
            SetLineAlpha(_chargeRing, _accent, 0f);
            SetPalmProjectileAlpha(0f);
            SetSignatureAlpha(_vaultWaveArcs, 0f);
            SetSignatureAlpha(_skyChargeArcs, 0f);
        }

        internal void Release()
        {
            if (_released) return;
            _released = true;
            _releasedAt = Time.time;
            SetEmission(false);
            if (_palmProjectileWake != null)
                _palmProjectileWake.emitting = false;
            SetParticleEmission(_palmProjectileMotes, false);
        }

        private void LateUpdate()
        {
            if (_caster == null || _definition == null)
            {
                Destroy(gameObject);
                return;
            }
            UpdateAnchor(_primaryAnchor, _primaryBone);
            UpdateAnchor(_secondaryAnchor, _secondaryBone);
            if (_released)
            {
                float fade = 1f - Mathf.Clamp01(
                    (Time.time - _releasedAt) / 0.24f);
                SetLineAlpha(_directionOuter, _accent, fade * 0.55f);
                SetLineAlpha(_directionCore, _ivory, fade * 0.70f);
                SetLineAlpha(_chargeRing, _accent, 0f);
                SetPalmProjectileAlpha(fade * 0.28f);
                SetSignatureAlpha(_vaultWaveArcs, 0f);
                SetSignatureAlpha(_skyChargeArcs, 0f);
                if (fade <= 0f) Destroy(gameObject);
                return;
            }

            float elapsed = Time.time - _started;
            if (elapsed >= _duration)
            {
                Release();
                return;
            }
            float normalized = Mathf.Clamp01(elapsed / _duration);
            float releaseNormalized = _releaseTime / _duration;
            float impactNormalized = _impactTime / _duration;
            float driveStart = releaseNormalized * 0.28f;
            float drive = normalized <= releaseNormalized
                ? SmoothStep01(Mathf.InverseLerp(driveStart,
                    releaseNormalized, normalized))
                : 1f - SmoothStep01(Mathf.InverseLerp(releaseNormalized,
                    Mathf.Min(1f, releaseNormalized + 0.18f),
                    normalized));
            float charge = normalized <= releaseNormalized
                ? SmoothStep01(Mathf.InverseLerp(0.02f,
                    releaseNormalized, normalized))
                : 1f - SmoothStep01(Mathf.InverseLerp(releaseNormalized,
                    Mathf.Min(1f, releaseNormalized + 0.10f),
                    normalized));
            bool emit = elapsed >= 0.025f &&
                elapsed <= _releaseTime + 0.13f;
            SetEmission(emit);

            Vector3 origin = _primaryAnchor != null
                ? _primaryAnchor.position
                : _caster.position + _caster.up;
            if (IsPalmProjectile &&
                _primaryAnchor != null && _secondaryAnchor != null)
                origin = (_primaryAnchor.position +
                    _secondaryAnchor.position) * 0.5f;
            Vector3 impact = ImpactPoint();
            Vector3 direction = impact - origin;
            float distance = direction.magnitude;
            if (distance < 0.001f)
                direction = _caster.forward;
            else
                direction /= distance;
            Vector3 up = _caster.up;
            Vector3 side = Vector3.Cross(up, direction);
            if (side.sqrMagnitude < 0.001f) side = _caster.right;
            side.Normalize();

            float maximumReach = IsPalmProjectile
                ? 6f
                : Mathf.Lerp(1.35f, 2.75f, _tier);
            float length = Mathf.Min(Mathf.Max(distance, 0.65f),
                Mathf.Lerp(0.45f, maximumReach, drive));
            Vector3 start = origin - direction *
                Mathf.Lerp(0.08f, 0.22f, drive);
            Vector3 end = start + direction * length;
            float bend = Mathf.Sin(normalized * Mathf.PI) *
                Mathf.Lerp(0.035f, 0.085f, _tier);
            var points = new[]
            {
                start,
                Vector3.Lerp(start, end, 0.34f) + side * bend,
                Vector3.Lerp(start, end, 0.72f) - side * bend * 0.45f,
                end
            };
            _directionOuter.SetPositions(points);
            _directionCore.SetPositions(points);
            float directionalScale = IsPalmProjectile
                ? 0.30f : 1f;
            SetLineAlpha(_directionOuter, _accent,
                drive * 0.78f * directionalScale);
            SetLineAlpha(_directionCore, _ivory,
                drive * directionalScale);
            UpdateChargeRing(origin, direction, side, up, charge,
                normalized);
            if (IsVaultingTempest)
                UpdateVaultWave(origin, direction, up, elapsed);
            if (IsSkybreakerRush)
                UpdateSkybreakerCharge(origin, direction, up, elapsed,
                    charge);
            if (IsPalmProjectile)
                UpdatePalmProjectile(origin, impact, direction, up,
                    normalized, releaseNormalized, impactNormalized,
                    elapsed);
        }

        private void ResolveBones(Animator animator)
        {
            string key = _definition.Key;
            bool leftHand = key == "eagle_strike" ||
                key == "pressure_point" || key == "dragon_punch";
            bool leftFoot = key == "crescent_heel";
            bool rightFoot = key == "round_kick" ||
                key == "flying_kick" ||
                key == "vaulting_tempest" ||
                key == "sweeping_crescent" ||
                key == "whirlwind_kick";
            try
            {
                if (key == "shoulder_check")
                    _primaryBone = animator.GetBoneTransform(
                        HumanBodyBones.RightShoulder) ??
                        animator.GetBoneTransform(HumanBodyBones.Chest);
                else if (leftFoot)
                    _primaryBone = animator.GetBoneTransform(
                        HumanBodyBones.LeftFoot);
                else if (rightFoot)
                    _primaryBone = animator.GetBoneTransform(
                        HumanBodyBones.RightFoot);
                else
                    _primaryBone = animator.GetBoneTransform(leftHand
                        ? HumanBodyBones.LeftHand
                        : HumanBodyBones.RightHand);

                if (key == "fist_of_falling_rain" || key == "pummel")
                    _secondaryBone = animator.GetBoneTransform(
                        HumanBodyBones.LeftHand);
                else if (key == "stone_throw" ||
                    key == "gale_splitting_palm")
                    _secondaryBone = animator.GetBoneTransform(
                        HumanBodyBones.LeftHand);
                else if (key == "whirlwind_kick")
                    _secondaryBone = animator.GetBoneTransform(
                        HumanBodyBones.LeftFoot);
            }
            catch
            {
                _primaryBone = null;
                _secondaryBone = null;
            }
        }

        private Transform CreateMotionAnchor(string name, Transform bone,
            float width, out TrailRenderer trail,
            out ParticleSystem motes)
        {
            var anchor = new GameObject(name);
            anchor.transform.SetParent(transform, false);
            UpdateAnchor(anchor.transform, bone);

            trail = anchor.AddComponent<TrailRenderer>();
            trail.time = Mathf.Lerp(0.18f, 0.29f, _tier);
            // A fast arc sampled every 12mm with only three corner vertices
            // shows visible faceting along the curve; eight rounds it, and a
            // tighter sample distance keeps the ribbon smooth at the peak
            // velocity of a kick.
            trail.minVertexDistance = 0.008f;
            trail.numCapVertices = 8;
            trail.numCornerVertices = 8;
            trail.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.widthMultiplier = width;
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.16f, 1f),
                new Keyframe(0.70f, 0.58f),
                new Keyframe(1f, 0f));
            trail.colorGradient = BuildTrailGradient();
            trail.sharedMaterial = MonkVfx.AttackLineMaterial();
            trail.emitting = false;

            var motesObject = new GameObject("Following martial motes");
            motesObject.transform.SetParent(anchor.transform, false);
            motes = motesObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = motes.main;
            main.loop = true;
            main.duration = 0.32f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                0.14f, Mathf.Lerp(0.22f, 0.34f, _tier));
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.06f, 0.32f);
            main.startSize = new ParticleSystem.MinMaxCurve(
                0.025f, Mathf.Lerp(0.060f, 0.095f, _tier));
            main.startColor = new ParticleSystem.MinMaxGradient(
                MonkVfx.Hdr(_ivory, MonkVfx.TrailGainValue),
                MonkVfx.Hdr(_accent, MonkVfx.TrailGainValue));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            main.gravityModifier = 0.04f;
            ParticleSystem.EmissionModule emission = motes.emission;
            emission.rateOverTime = Mathf.Lerp(16f, 29f, _tier);
            ParticleSystem.ShapeModule shape = motes.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.055f;
            ParticleSystem.ColorOverLifetimeModule colors =
                motes.colorOverLifetime;
            colors.enabled = true;
            var colorGradient = new Gradient();
            colorGradient.SetKeys(new[]
            {
                new GradientColorKey(_ivory, 0f),
                new GradientColorKey(_accent, 0.42f),
                new GradientColorKey(MonkVfx.RimOf(_accent), 1f)
            }, new[]
            {
                new GradientAlphaKey(0.85f, 0f),
                new GradientAlphaKey(1f, 0.18f),
                new GradientAlphaKey(0f, 1f)
            });
            colors.color = colorGradient;
            ParticleSystem.SizeOverLifetimeModule sizes =
                motes.sizeOverLifetime;
            sizes.enabled = true;
            sizes.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.35f),
                    new Keyframe(0.24f, 1f), new Keyframe(1f, 0f)));
            ParticleSystemRenderer renderer =
                motesObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = MonkVfx.AttackParticleMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            motes.Play();
            emission.enabled = false;
            return anchor.transform;
        }

        private LineRenderer CreateDirectionLine(string name, Color color,
            float width)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 4;
            line.numCapVertices = 3;
            line.numCornerVertices = 3;
            line.widthMultiplier = width;
            line.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.18f, 0.72f),
                new Keyframe(0.68f, 1f),
                new Keyframe(1f, 0f));
            line.sharedMaterial = MonkVfx.AttackLineMaterial();
            line.startColor = color;
            line.endColor = color;
            return line;
        }

        private LineRenderer CreateSignatureArc(Transform parent,
            string name, Color color, float radius, float width,
            float startDegrees, float endDegrees, int points)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = false;
            line.positionCount = Mathf.Max(3, points);
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.widthMultiplier = width;
            line.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.14f, 1f),
                new Keyframe(0.82f, 0.76f),
                new Keyframe(1f, 0f));
            line.sharedMaterial = MonkVfx.AttackLineMaterial();
            for (int index = 0; index < line.positionCount; index++)
            {
                float t = index / (float)(line.positionCount - 1);
                float angle = Mathf.Lerp(startDegrees, endDegrees, t) *
                    Mathf.Deg2Rad;
                line.SetPosition(index, new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius, 0f));
            }
            line.startColor = color;
            line.endColor = color;
            return line;
        }

        private void CreateVaultWave()
        {
            var wave = new GameObject("Vaulting Tempest released heel wave");
            _vaultWave = wave.transform;
            _vaultWave.SetParent(transform, false);
            _vaultWaveArcs = new[]
            {
                CreateSignatureArc(_vaultWave, "Heel wave outer crescent",
                    _accent, 0.92f, 0.105f, -168f, 38f, 32),
                CreateSignatureArc(_vaultWave, "Heel wave white edge",
                    _ivory, 0.76f, 0.054f, -160f, 30f, 28),
                CreateSignatureArc(_vaultWave, "Heel wave trailing echo",
                    _accent, 1.06f, 0.046f, -150f, 18f, 26)
            };
            _vaultWaveArcs[2].transform.localRotation =
                Quaternion.Euler(0f, 0f, -12f);
        }

        private void UpdateVaultWave(Vector3 origin, Vector3 direction,
            Vector3 up, float elapsed)
        {
            if (_vaultWave == null) return;
            float launch = _releaseTime - 0.075f;
            if (!_vaultWaveReleased && elapsed >= launch)
            {
                _vaultWaveReleased = true;
                _vaultWaveStarted = elapsed;
                _vaultWaveOrigin = origin;
                _vaultWaveRotation = direction.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(direction, up)
                    : _caster.rotation;
            }
            if (!_vaultWaveReleased)
            {
                SetSignatureAlpha(_vaultWaveArcs, 0f);
                return;
            }

            float age = elapsed - _vaultWaveStarted;
            float t = Mathf.Clamp01(age / 0.62f);
            float expansion = Mathf.Lerp(0.22f, 1.48f,
                SmoothStep01(t));
            _vaultWave.position = _vaultWaveOrigin +
                up * Mathf.Lerp(0.02f, 0.82f, t) +
                direction * Mathf.Lerp(0f, 0.34f, t);
            _vaultWave.rotation = _vaultWaveRotation *
                Quaternion.AngleAxis(-64f + t * 12f, Vector3.forward);
            _vaultWave.localScale = Vector3.one * expansion;
            float alpha = 1f - SmoothStep01(Mathf.InverseLerp(
                0.34f, 1f, t));
            SetSignatureAlpha(_vaultWaveArcs, alpha);
        }

        private void CreateSkybreakerCharge()
        {
            var charge = new GameObject("Skybreaker fist flash charge");
            _skyCharge = charge.transform;
            _skyCharge.SetParent(transform, false);
            _skyChargeArcs = new[]
            {
                CreateSignatureArc(_skyCharge, "Charge outer compression",
                    _accent, 0.30f, 0.058f, -148f, 148f, 26),
                CreateSignatureArc(_skyCharge, "Charge white-hot core",
                    _ivory, 0.18f, 0.046f, -158f, 158f, 24),
                CreateSignatureArc(_skyCharge, "Charge counter spiral",
                    _accent, 0.37f, 0.038f, 28f, 332f, 26)
            };

            var flashObject = new GameObject("Skybreaker charged flash");
            flashObject.transform.SetParent(_skyCharge, false);
            ParticleSystem flash = flashObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = flash.main;
            main.loop = false;
            main.duration = 0.06f;
            main.startDelay = Mathf.Max(0f, _releaseTime - 0.105f);
            main.startLifetime = 0.16f;
            main.startSpeed = 0f;
            main.startSize = 0.22f;
            main.startColor = MonkVfx.Hdr(_ivory, MonkVfx.CoreGainValue);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 2;
            ParticleSystem.EmissionModule emission = flash.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)1)
            });
            ParticleSystem.ShapeModule shape = flash.shape;
            shape.enabled = false;
            ParticleSystem.ColorOverLifetimeModule colors =
                flash.colorOverLifetime;
            colors.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[]
            {
                new GradientColorKey(Color.Lerp(_accent, Color.white, 0.82f),
                    0f),
                new GradientColorKey(_accent, 0.42f),
                new GradientColorKey(MonkVfx.RimOf(_accent), 1f)
            }, new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.88f, 0.24f),
                new GradientAlphaKey(0f, 1f)
            });
            colors.color = gradient;
            ParticleSystem.SizeOverLifetimeModule sizes =
                flash.sizeOverLifetime;
            sizes.enabled = true;
            sizes.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.20f),
                    new Keyframe(0.12f, 1f),
                    new Keyframe(1f, 5.4f)));
            ParticleSystemRenderer renderer =
                flashObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = MonkVfx.AttackParticleMaterial();
            flash.Play();
        }

        private void UpdateSkybreakerCharge(Vector3 origin,
            Vector3 direction, Vector3 up, float elapsed, float charge)
        {
            if (_skyCharge == null) return;
            _skyCharge.position = origin + direction * 0.045f;
            _skyCharge.rotation = direction.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(direction, up)
                : _caster.rotation;

            float buildup = Mathf.Pow(Mathf.Clamp01(charge), 1.35f);
            float flash = SmoothStep01(Mathf.InverseLerp(
                _releaseTime - 0.12f, _releaseTime - 0.015f, elapsed));
            float fade = elapsed <= _releaseTime
                ? 1f
                : 1f - SmoothStep01(Mathf.InverseLerp(_releaseTime,
                    _releaseTime + 0.13f, elapsed));
            float pulse = 1f + Mathf.Sin(elapsed * 42f) * 0.08f;
            float size = Mathf.Lerp(0.20f, 0.86f, buildup) * pulse +
                flash * 0.42f;
            _skyCharge.localScale = Vector3.one * size;
            _skyChargeArcs[0].transform.localRotation =
                Quaternion.Euler(0f, 0f, elapsed * 420f);
            _skyChargeArcs[1].transform.localRotation =
                Quaternion.Euler(0f, 0f, -elapsed * 520f);
            _skyChargeArcs[2].transform.localRotation =
                Quaternion.Euler(0f, 0f, elapsed * 310f + 24f);
            SetSignatureAlpha(_skyChargeArcs,
                Mathf.Clamp01((buildup * 0.76f + flash) * fade));
        }

        private void SetSignatureAlpha(LineRenderer[] lines, float alpha)
        {
            if (lines == null) return;
            for (int index = 0; index < lines.Length; index++)
            {
                Color color = index == 1 ? _ivory : _accent;
                SetLineAlpha(lines[index], color,
                    alpha * (index == 1 ? 1f : 0.88f));
            }
        }

        private void CreatePalmProjectile()
        {
            bool gale = _definition.Key == "gale_splitting_palm";
            var projectile = new GameObject(
                gale ? "Gale-splitting traveling shear" :
                    "Resonant palm traveling energy sphere");
            projectile.transform.SetParent(transform, false);
            _palmProjectile = projectile.transform;
            _palmProjectileRings = gale ? new[]
            {
                CreateSignatureArc(_palmProjectile,
                    "Sonic outer broken vortex", _accent,
                    0.46f, 0.074f, -148f, 148f, 30),
                CreateSignatureArc(_palmProjectile,
                    "Sonic white cutting edge", _ivory,
                    0.38f, 0.052f, 24f, 336f, 28),
                CreateSignatureArc(_palmProjectile,
                    "Sonic inner counter crescent", _accent,
                    0.27f, 0.044f, -132f, 116f, 24),
                CreateSignatureArc(_palmProjectile,
                    "Sonic trailing crescent", _accent,
                    0.52f, 0.034f, 42f, 286f, 24)
            } : new[]
            {
                CreatePalmProjectileRing("Resonant sphere outer",
                    _accent, 0.30f, 0.042f),
                CreatePalmProjectileRing("Resonant sphere cross",
                    Color.Lerp(_accent, _ivory, 0.28f),
                    0.25f, 0.032f),
                CreatePalmProjectileRing("Resonant sphere core",
                    _ivory, 0.15f, 0.028f)
            };
            if (gale)
            {
                _palmProjectileRings[1].transform.localRotation =
                    Quaternion.Euler(0f, 0f, 34f);
                _palmProjectileRings[2].transform.localRotation =
                    Quaternion.Euler(0f, 0f, -28f);
                _palmProjectileRings[3].transform.localRotation =
                    Quaternion.Euler(0f, 0f, 72f);
            }
            else
            {
                _palmProjectileRings[1].transform.localRotation =
                    Quaternion.Euler(64f, 0f, 0f);
                _palmProjectileRings[2].transform.localRotation =
                    Quaternion.Euler(0f, 67f, 0f);
            }

            _palmProjectileWake =
                projectile.AddComponent<TrailRenderer>();
            _palmProjectileWake.time = gale ? 0.26f : 0.28f;
            _palmProjectileWake.minVertexDistance = 0.015f;
            _palmProjectileWake.numCapVertices = 4;
            _palmProjectileWake.numCornerVertices = 4;
            _palmProjectileWake.widthMultiplier = gale ? 0.44f : 0.34f;
            _palmProjectileWake.widthCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.34f, 0.72f),
                new Keyframe(1f, 0f));
            _palmProjectileWake.colorGradient =
                BuildPalmProjectileGradient();
            _palmProjectileWake.sharedMaterial =
                MonkVfx.AttackLineMaterial();
            _palmProjectileWake.emitting = false;

            var motesObject = new GameObject(
                "Resonant palm projectile motes");
            motesObject.transform.SetParent(projectile.transform, false);
            _palmProjectileMotes =
                motesObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = _palmProjectileMotes.main;
            main.loop = true;
            main.duration = 0.22f;
            main.startLifetime =
                new ParticleSystem.MinMaxCurve(0.14f, 0.30f);
            main.startSpeed =
                new ParticleSystem.MinMaxCurve(0.05f, 0.30f);
            main.startSize =
                new ParticleSystem.MinMaxCurve(0.030f, 0.090f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                MonkVfx.Hdr(_ivory, MonkVfx.TrailGainValue),
                MonkVfx.Hdr(_accent, MonkVfx.TrailGainValue));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 72;
            main.gravityModifier = 0f;
            ParticleSystem.EmissionModule emission =
                _palmProjectileMotes.emission;
            emission.rateOverTime = 48f;
            ParticleSystem.ShapeModule shape = _palmProjectileMotes.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.19f;
            ParticleSystem.ColorOverLifetimeModule colors =
                _palmProjectileMotes.colorOverLifetime;
            colors.enabled = true;
            colors.color = BuildPalmProjectileGradient();
            ParticleSystem.SizeOverLifetimeModule sizes =
                _palmProjectileMotes.sizeOverLifetime;
            sizes.enabled = true;
            sizes.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.35f),
                    new Keyframe(0.18f, 1f),
                    new Keyframe(1f, 0f)));
            ParticleSystemRenderer renderer =
                motesObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = MonkVfx.AttackParticleMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            _palmProjectileMotes.Play();
            emission.enabled = false;
        }

        private LineRenderer CreatePalmProjectileRing(string name,
            Color color, float radius, float width)
        {
            var child = new GameObject(name);
            child.transform.SetParent(_palmProjectile, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 28;
            line.numCapVertices = 3;
            line.numCornerVertices = 3;
            line.widthMultiplier = width;
            line.sharedMaterial = MonkVfx.AttackLineMaterial();
            for (int index = 0; index < line.positionCount; index++)
            {
                float angle = index / (float)line.positionCount *
                    Mathf.PI * 2f;
                line.SetPosition(index, new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius, 0f));
            }
            line.startColor = color;
            line.endColor = color;
            return line;
        }

        private void UpdatePalmProjectile(Vector3 origin, Vector3 impact,
            Vector3 direction, Vector3 up, float normalized,
            float releaseNormalized, float impactNormalized, float elapsed)
        {
            if (_palmProjectile == null) return;
            float launchStart = releaseNormalized;
            float chargeStart = Mathf.Min(0.12f, launchStart * 0.30f);
            float gathered = SmoothStep01(Mathf.InverseLerp(chargeStart,
                launchStart, normalized));
            float flight = SmoothStep01(Mathf.InverseLerp(launchStart,
                impactNormalized, normalized));
            float fadeEnd = Mathf.Min(1f, impactNormalized + 0.13f);
            float fade = normalized <= impactNormalized
                ? 1f
                : 1f - SmoothStep01(Mathf.InverseLerp(impactNormalized,
                    fadeEnd, normalized));
            float alpha = gathered * fade;
            Vector3 start = origin + direction * 0.06f;
            _palmProjectile.position = Vector3.Lerp(start, impact, flight);
            Quaternion facing = direction.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(direction, up)
                : _caster.rotation;
            float spin = _definition.Key == "gale_splitting_palm"
                ? elapsed * 430f : elapsed * 310f;
            _palmProjectile.rotation = facing *
                Quaternion.AngleAxis(spin, Vector3.forward);
            float pulse = 1f + Mathf.Sin(elapsed * 34f) * 0.065f;
            float scale = Mathf.Lerp(0.36f, 1.08f, gathered) * pulse;
            _palmProjectile.localScale = _definition.Key ==
                "gale_splitting_palm"
                    ? new Vector3(scale * 1.08f, scale * 1.18f,
                        scale * 0.58f)
                    : Vector3.one * scale;
            SetPalmProjectileAlpha(alpha);
            bool flying = normalized >= launchStart &&
                normalized <= impactNormalized + 0.02f;
            if (_palmProjectileWake != null)
                _palmProjectileWake.emitting = flying;
            SetParticleEmission(_palmProjectileMotes,
                alpha > 0.05f && normalized <= fadeEnd);
        }

        private Gradient BuildPalmProjectileGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[]
            {
                new GradientColorKey(_ivory, 0f),
                new GradientColorKey(_accent, 0.36f),
                new GradientColorKey(Color.Lerp(_accent,
                    Color.black, 0.30f), 1f)
            }, new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.92f, 0.38f),
                new GradientAlphaKey(0f, 1f)
            });
            return gradient;
        }

        private void SetPalmProjectileAlpha(float alpha)
        {
            float value = Mathf.Clamp01(alpha);
            if (_palmProjectileRings != null)
                for (int index = 0;
                    index < _palmProjectileRings.Length; index++)
                {
                    bool ivory = IsGaleShear ? index == 1 : index == 2;
                    Color color = ivory ? _ivory : _accent;
                    SetLineAlpha(_palmProjectileRings[index], color,
                        value * (ivory ? 1f : 0.90f));
                }
            if (_palmProjectileWake != null)
            {
                _palmProjectileWake.startColor = new Color(
                    _ivory.r, _ivory.g, _ivory.b, value);
                _palmProjectileWake.endColor = new Color(
                    _accent.r, _accent.g, _accent.b, 0f);
                _palmProjectileWake.enabled = value > 0.002f;
            }
        }

        private Gradient BuildTrailGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[]
            {
                new GradientColorKey(_ivory, 0f),
                new GradientColorKey(Color.Lerp(_accent, Color.white, 0.80f),
                    0.12f),
                new GradientColorKey(_accent, 0.36f),
                new GradientColorKey(MonkVfx.RimOf(_accent), 1f)
            }, new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.10f),
                new GradientAlphaKey(0.86f, 0.58f),
                new GradientAlphaKey(0f, 1f)
            });
            return gradient;
        }

        private void UpdateChargeRing(Vector3 origin, Vector3 direction,
            Vector3 side, Vector3 up, float alpha, float normalized)
        {
            Vector3 ringUp = Vector3.Cross(direction, side);
            if (ringUp.sqrMagnitude < 0.001f) ringUp = up;
            ringUp.Normalize();
            float pulse = 0.82f + Mathf.Sin(normalized * Mathf.PI * 4f) *
                0.08f;
            float radius = Mathf.Lerp(0.12f, 0.24f, _tier) * pulse;
            Vector3 center = origin + direction * 0.045f;
            for (int index = 0; index < RingPoints; index++)
            {
                float angle = index / (float)RingPoints * Mathf.PI * 2f +
                    normalized * Mathf.PI * 1.4f;
                _chargeRing.SetPosition(index, center +
                    side * (Mathf.Cos(angle) * radius) +
                    ringUp * (Mathf.Sin(angle) * radius));
            }
            SetLineAlpha(_chargeRing, _accent, alpha * 0.86f);
        }

        private Vector3 ImpactPoint()
        {
            if (_target != null)
            {
                Vector3 toward = _target.position - _caster.position;
                toward -= _caster.up * Vector3.Dot(toward, _caster.up);
                Vector3 surface = toward.sqrMagnitude > 0.001f
                    ? toward.normalized * 0.20f
                    : _caster.forward * 0.20f;
                return _target.position + _caster.up * _impactHeight -
                    surface;
            }
            return _caster.position + _caster.forward *
                Mathf.Lerp(1.65f, 2.35f, _tier) +
                _caster.up * _impactHeight;
        }

        private static float ImpactHeight(string key)
        {
            return MonkVfx.ImpactHeight(key);
        }

        private void SetEmission(bool enabled)
        {
            if (_primaryTrail != null) _primaryTrail.emitting = enabled;
            if (_secondaryTrail != null) _secondaryTrail.emitting = enabled;
            SetParticleEmission(_primaryMotes, enabled);
            SetParticleEmission(_secondaryMotes, enabled);
        }

        private static void SetParticleEmission(ParticleSystem system,
            bool enabled)
        {
            if (system == null) return;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = enabled;
        }

        private void UpdateAnchor(Transform anchor, Transform bone)
        {
            if (anchor == null) return;
            anchor.position = bone != null
                ? bone.position
                : _caster.position + _caster.up;
        }

        private static void SetLineAlpha(LineRenderer line, Color color,
            float alpha)
        {
            if (line == null) return;
            float value = Mathf.Clamp01(alpha);
            line.startColor = new Color(color.r, color.g, color.b, value);
            line.endColor = new Color(color.r, color.g, color.b,
                value * 0.34f);
            line.enabled = value > 0.002f;
        }

        private static float SmoothStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private void OnDisable()
        {
            SetEmission(false);
        }
    }

    internal sealed class MonkVfxMotion : MonoBehaviour
    {
        private float _delay;
        private float _duration;
        private float _elapsed;
        private Vector3 _spin;
        private Vector3 _startScale;
        private Vector3 _endScale;
        private LineRenderer[] _lines;
        private Color[] _startColors;
        private Color[] _endColors;
        private bool _started;

        internal void Configure(float delay, float duration, Vector3 spin,
            Vector3 startScale, Vector3 endScale)
        {
            _delay = Mathf.Max(0f, delay);
            _duration = Mathf.Max(0.08f, duration);
            _spin = spin;
            _startScale = startScale;
            _endScale = endScale;
            transform.localScale = startScale;
            _lines = GetComponentsInChildren<LineRenderer>(true);
            _startColors = _lines.Select(line => line.startColor).ToArray();
            _endColors = _lines.Select(line => line.endColor).ToArray();
            if (_delay > 0f)
                foreach (LineRenderer line in _lines) line.enabled = false;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed < _delay) return;
            if (!_started)
            {
                _started = true;
                foreach (LineRenderer line in _lines) line.enabled = true;
            }
            float t = Mathf.Clamp01((_elapsed - _delay) / _duration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            transform.localScale = Vector3.LerpUnclamped(_startScale,
                _endScale, eased);
            transform.Rotate(_spin * Time.deltaTime, Space.Self);
            float alpha = 1f - Mathf.SmoothStep(0.18f, 1f, t);
            for (int index = 0; index < _lines.Length; index++)
            {
                Color start = _startColors[index];
                Color end = _endColors[index];
                start.a *= alpha;
                end.a *= alpha;
                _lines[index].startColor = start;
                _lines[index].endColor = end;
            }
            if (t >= 1f) Destroy(gameObject);
        }
    }

    internal sealed class MonkVfxTravel : MonoBehaviour
    {
        private Vector3 _start;
        private Vector3 _travel;
        private float _duration;
        private float _delay;
        private float _elapsed;

        internal void Configure(Vector3 localTravel, float duration,
            float delay = 0f)
        {
            _start = transform.localPosition;
            _travel = localTravel;
            _duration = Mathf.Max(0.08f, duration);
            _delay = Mathf.Max(0f, delay);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed < _delay) return;
            float t = Mathf.Clamp01((_elapsed - _delay) / _duration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            transform.localPosition = _start + _travel * eased;
        }
    }
}
