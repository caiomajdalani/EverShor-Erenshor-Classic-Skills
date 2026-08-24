using System;
using System.Collections.Generic;
using UnityEngine;

namespace ErenshorSkills.Necromancer
{
    /// <summary>
    /// Runtime-authored spell effects. Each spell receives its own registered
    /// charge and resolve prefab, while style families keep the palette and
    /// visual language coherent. Offensive spells also launch a fast visible
    /// projectile, and timed effects remain attached for their full duration.
    /// </summary>
    internal static class NecromancerVfx
    {
        private const string CastAuraObjectName =
            "CS Necromancer Evil Cast Aura";
        private const string SummoningSigilObjectName =
            "CS Necromancer Demonic Summoning Sigil";
        private const string LifetapOrbObjectName =
            "CS Necromancer Siphoned Life Orb";
        private const string GraveSmokeName = "Grave Smoke";
        private const string SoulEmbersName = "Blood Soul Embers";
        private const string OrbitingSkullsName = "Orbiting Spectral Skulls";
        // Every non-summoning spell uses this one authored casting language.
        // Spell identity belongs to projectiles and impacts, not symbols drawn
        // over the player model.
        private const float TemplateCastSeconds = 1.5f;
        private static readonly Color TemplateAccent =
            new Color(0.48f, 0.12f, 0.72f, 1f);

        private sealed class Pair
        {
            internal int Charge;
            internal int Resolve;
            internal GameObject ChargePrefab;
            internal GameObject ResolvePrefab;
        }

        private struct PersistentPlacement
        {
            internal float Height;
            internal float Scale;
            internal float Yaw;
            internal string Band;
        }

        private struct MotifRecipe
        {
            internal int Sides;
            internal float Inner;
            internal int Satellites;
            internal float Scale;
            internal float Lift;
            internal float Spin;
            internal float Pulse;
            internal float Light;
            internal bool Vertical;
            internal float Twist;

            internal string Fingerprint =>
                $"{Sides}|{Inner:0.000}|{Satellites}|{Scale:0.000}|" +
                $"{Lift:0.000}|{Spin:0.000}|{Pulse:0.000}|" +
                $"{Light:0.000}|{(Vertical ? 1 : 0)}|{Twist:0.000}";
        }

        private static readonly Dictionary<string, Pair> Effects =
            new Dictionary<string, Pair>(StringComparer.OrdinalIgnoreCase);
        private static readonly Color EvilViolet =
            new Color(0.48f, 0.055f, 0.72f, 1f);
        private static readonly Color EvilCrimson =
            new Color(0.74f, 0.025f, 0.12f, 1f);
        private static Material _baseMaterial;
        private static Material _runtimeParticleMaterial;
        private static Material _skullParticleMaterial;
        private static Material _sigilMaterial;
        private static Texture2D _softParticle;
        private static Texture2D _skullParticle;
        private static GameObject _activeResolvePreview;
        private static GameObject _castAuraTemplate;
        private static GameObject _lifetapOrbTemplate;

        /// <summary>
        /// Instance ids of the authored effect prefabs. Components that retire
        /// their own GameObject need to tell an authored template apart from a
        /// clone of it, and hide flags cannot do that because Instantiate
        /// copies them.
        /// </summary>
        private static readonly HashSet<int> AuthoredTemplateIds =
            new HashSet<int>();

        internal static bool IsAuthoredTemplate(GameObject candidate)
        {
            return candidate != null &&
                AuthoredTemplateIds.Contains(candidate.GetInstanceID());
        }
        internal static bool IsReady { get; private set; }

        internal static void RegisterEffects()
        {
            List<GameObject> database = GameData.EffectDB?.SpellEffects;
            if (database == null || database.Count == 0) return;

            // SpellEffectDB is scene-local. Character select and the world each
            // provide a different list, so previously authored persistent
            // prefabs must be rebound whenever GameData.EffectDB changes.
            if (Effects.Count == NecromancerSpellData.All.Count)
            {
                IsReady = EnsureCurrentDatabaseBindings();
                return;
            }

            _baseMaterial = FindBaseMaterial(database);
            _softParticle = CreateSoftParticleTexture();
            _skullParticle = CreateSkullParticleTexture();
            _skullParticleMaterial = CreateSkullParticleMaterial();
            Effects.Clear();
            AuthoredTemplateIds.Clear();

            if (!NecromancerImpactProfiles.Validate(
                    NecromancerSpellData.All, out string impactDetail))
            {
                SkillsPlugin.Log.LogError(
                    "Necromancer VFX impact-profile validation failed: " +
                    impactDetail);
                return;
            }
            if (!ValidateAuthoredMotifs(out string motifDetail))
            {
                SkillsPlugin.Log.LogError(
                    "Necromancer VFX motif validation failed: " +
                    motifDetail);
                return;
            }
            SkillsPlugin.Log.LogInfo(
                "Necromancer VFX impact profiles: " + impactDetail + "; " +
                motifDetail + ".");

            foreach (NecromancerSpellDefinition definition in NecromancerSpellData.All)
            {
                GameObject charge = CreateEffect(definition, true);
                GameObject resolve = CreateEffect(definition, false);
                Effects.Add(definition.Key, new Pair
                {
                    Charge = -1,
                    Resolve = -1,
                    ChargePrefab = charge,
                    ResolvePrefab = resolve
                });
            }

            IsReady = EnsureCurrentDatabaseBindings();
            SkillsPlugin.Log.LogInfo(
                $"Necromancer VFX: registered {Effects.Count * 2} custom-only spell effect prefabs.");
        }

        /// <summary>
        /// Reattaches the persistent authored prefabs to the active scene's
        /// SpellEffectDB and refreshes every stored index. Safe to call at
        /// scene load and immediately before a cast.
        /// </summary>
        internal static bool EnsureCurrentDatabaseBindings()
        {
            List<GameObject> database = GameData.EffectDB?.SpellEffects;
            if (database == null || database.Count == 0 ||
                Effects.Count != NecromancerSpellData.All.Count)
                return false;

            int attached = 0;
            foreach (NecromancerSpellDefinition definition in
                NecromancerSpellData.All)
            {
                if (!Effects.TryGetValue(definition.Key, out Pair pair) ||
                    !pair.ChargePrefab || !pair.ResolvePrefab)
                    return false;

                int chargeIndex = database.IndexOf(pair.ChargePrefab);
                if (chargeIndex < 0)
                {
                    chargeIndex = database.Count;
                    database.Add(pair.ChargePrefab);
                    attached++;
                }

                int resolveIndex = database.IndexOf(pair.ResolvePrefab);
                if (resolveIndex < 0)
                {
                    resolveIndex = database.Count;
                    database.Add(pair.ResolvePrefab);
                    attached++;
                }

                pair.Charge = chargeIndex;
                pair.Resolve = resolveIndex;
            }

            if (attached > 0)
                SkillsPlugin.Log.LogInfo(
                    $"Necromancer VFX: rebound {attached} authored effect " +
                    $"prefabs to scene database '{GameData.EffectDB.name}' " +
                    $"({database.Count} total effects).");
            return true;
        }

        internal static int GetChargeIndex(string key) => Effects[key].Charge;
        internal static int GetResolveIndex(string key) => Effects[key].Resolve;

        private static GameObject CreateEffect(NecromancerSpellDefinition d,
            bool charge)
        {
            var root = new GameObject(
                $"CS Necromancer {(charge ? "Charge" : "Resolve")} - {d.Name}");
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.position = new Vector3(0f, -10000f, 0f);
            if (charge)
                root.AddComponent<NecromancerEvilChargeMarker>();
            if (d.Kind == NecromancerSpellKind.SummonPet)
                root.AddComponent<NecromancerSummonSigilMarker>();
            // SpellDB is created in the loading scene, while casts happen in
            // later zone scenes. Keep the authored templates alive across that
            // transition so the effect indices never become destroyed Unity
            // object references.
            UnityEngine.Object.DontDestroyOnLoad(root);
            AuthoredTemplateIds.Add(root.GetInstanceID());

            // SpellVessel always instantiates its registered charge prefab.
            // Keep that compatibility object completely silent: the bespoke
            // cast aura created by PlayCastAura is the Necromancer's only
            // visible caster-side effect. This avoids cloning an entire native
            // effect hierarchy plus two redundant particle systems per cast.
            if (charge)
            {
                ConfigureSilentPlaceholder(root.AddComponent<ParticleSystem>());
                return root;
            }

            // A summon is carried entirely by the sigil raised under the thrall
            // in PlaySummonResolve. Erenshor's Pet branch instantiates the
            // resolve prefab twice — once beside the caster, once on the target
            // — so any burst authored here lands as two extra effects on the
            // caster that have nothing to do with the summon. Author the
            // resolve side silent and let the sigil be the whole event.
            if (!charge && d.Kind == NecromancerSpellKind.SummonPet)
            {
                ConfigureSilentPlaceholder(root.AddComponent<ParticleSystem>());
                root.AddComponent<NecromancerSilentEffect>();
                return root;
            }

            ParticleSystem core = root.AddComponent<ParticleSystem>();
            ConfigureCore(core, d, charge);
            ConfigureRenderer(core.GetComponent<ParticleSystemRenderer>(), d,
                charge, 0);

            var accentObject = new GameObject("Accent");
            accentObject.hideFlags = HideFlags.HideAndDontSave;
            accentObject.transform.SetParent(root.transform, false);
            ParticleSystem accent = accentObject.AddComponent<ParticleSystem>();
            ConfigureAccent(accent, d, charge);
            ConfigureRenderer(accent.GetComponent<ParticleSystemRenderer>(), d,
                charge, 1);

            if (!charge)
                CreateSpellImpactSignature(root.transform, d);

            core.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            accent.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return root;
        }

        /// <summary>
        /// An emitter that exists only to satisfy Erenshor's resolve path,
        /// which dereferences the root ParticleSystem and scales it. It emits
        /// nothing and draws nothing.
        /// </summary>
        private static void ConfigureSilentPlaceholder(ParticleSystem system)
        {
            ParticleSystem.MainModule main = system.main;
            main.duration = 0.1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.01f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.001f);
            main.maxParticles = 1;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());

            ParticleSystemRenderer renderer =
                system.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) renderer.enabled = false;
        }

        private static void TintMaterialProperty(Material material,
            string property, Color tint, float strength)
        {
            if (!material.HasProperty(property)) return;
            Color original = material.GetColor(property);
            Color value = Darken(tint, strength);
            value.a = Mathf.Min(original.a, 0.78f);
            material.SetColor(property, value);
        }

        private static float SpellVariation(string key)
        {
            unchecked
            {
                int hash = 17;
                foreach (char value in key ?? "") hash = hash * 31 + value;
                return (hash & 0x7fffffff) % 1000 / 999f;
            }
        }

        private static void ConfigureCore(ParticleSystem system,
            NecromancerSpellDefinition d, bool charge)
        {
            float variation = SpellVariation(d.Key);
            Color effectColor = EffectColor(d, 0);
            ParticleSystem.MainModule main = system.main;
            main.duration = charge ? 1.05f : ResolveDuration(d.VfxStyle);
            main.loop = charge;
            main.prewarm = false;
            main.startLifetime = charge
                ? new ParticleSystem.MinMaxCurve(0.24f, 0.48f + variation * 0.12f)
                : new ParticleSystem.MinMaxCurve(0.30f, 0.72f + variation * 0.16f);
            main.startSpeed = charge
                ? new ParticleSystem.MinMaxCurve(0.04f, 0.28f + variation * 0.12f)
                : new ParticleSystem.MinMaxCurve(0.42f, 1.45f + variation * 0.35f);
            main.startSize = charge
                ? new ParticleSystem.MinMaxCurve(0.025f, 0.075f + variation * 0.02f)
                : new ParticleSystem.MinMaxCurve(0.035f, 0.14f + variation * 0.04f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Darken(effectColor, 0.38f), Darken(effectColor, 0.78f));
            main.maxParticles = charge ? 36 : 52;
            main.simulationSpace = charge
                ? ParticleSystemSimulationSpace.Local
                : ParticleSystemSimulationSpace.World;
            // This object is a database template. SpellVessel configures the
            // instantiated root to Destroy after playback where appropriate.
            // A destructive/disable stop action here would kill or deactivate
            // the template during the initial Stop-and-clear below.
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = charge ? 7f + variation * 4f : 0f;
            if (!charge)
            {
                var bursts = new[]
                {
                    new ParticleSystem.Burst(0f, (short)ResolveBurst(d.VfxStyle)),
                    new ParticleSystem.Burst(0.16f, (short)3)
                };
                emission.SetBursts(bursts);
            }

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ShapeFor(d.VfxStyle, charge);
            shape.radius = charge ? 0.42f + variation * 0.08f :
                ResolveRadius(d.VfxStyle);
            shape.radiusThickness = charge ? 0.12f : 0.72f;
            shape.angle = 18f + variation * 10f;

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = FadeGradient(effectColor);

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            AnimationCurve curve = new AnimationCurve(
                new Keyframe(0f, charge ? 0.12f : 0.24f),
                new Keyframe(0.24f, 1f),
                new Keyframe(1f, charge ? 0.2f : 0f));
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);

            ParticleSystem.RotationOverLifetimeModule rotation =
                system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.9f, 0.9f);

            if (d.VfxStyle == NecromancerVfxStyle.Cloud ||
                d.VfxStyle == NecromancerVfxStyle.Disease)
            {
                ParticleSystem.NoiseModule noise = system.noise;
                noise.enabled = true;
                noise.strength = 0.32f;
                noise.frequency = 0.38f;
                noise.scrollSpeed = 0.22f;
            }
        }

        private static void ConfigureAccent(ParticleSystem system,
            NecromancerSpellDefinition d, bool charge)
        {
            float variation = SpellVariation(d.Key);
            Color effectColor = EffectColor(d, 1);
            ParticleSystem.MainModule main = system.main;
            main.duration = charge ? 0.9f : 0.72f;
            main.loop = charge;
            main.startLifetime = charge
                ? new ParticleSystem.MinMaxCurve(0.14f, 0.34f)
                : new ParticleSystem.MinMaxCurve(0.18f, 0.48f);
            main.startSpeed = charge
                ? new ParticleSystem.MinMaxCurve(0.16f, 0.62f)
                : new ParticleSystem.MinMaxCurve(0.75f, 2.15f + variation * 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f,
                0.045f + variation * 0.015f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Darken(effectColor, 0.78f),
                Darken(effectColor, 0.58f));
            main.maxParticles = 28;
            main.simulationSpace = charge
                ? ParticleSystemSimulationSpace.Local
                : ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = charge ? 3f + variation * 2.5f : 0f;
            if (!charge)
                emission.SetBursts(new[]
                {
                    new ParticleSystem.Burst(0f, (short)AccentBurst(d.VfxStyle))
                });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = charge
                ? ParticleSystemShapeType.Circle
                : ParticleSystemShapeType.Sphere;
            shape.radius = charge ? 0.30f : ResolveRadius(d.VfxStyle) * 0.42f;
            shape.radiusThickness = charge ? 0f : 1f;

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            color.color = FadeGradient(Darken(effectColor, 0.84f));

            ParticleSystem.TrailModule trails = system.trails;
            trails.enabled = !charge && (d.VfxStyle == NecromancerVfxStyle.Drain ||
                d.VfxStyle == NecromancerVfxStyle.Fear ||
                d.VfxStyle == NecromancerVfxStyle.Fire);
            trails.lifetime = 0.14f;
            trails.dieWithParticles = true;
        }

        private static void ConfigureRenderer(ParticleSystemRenderer renderer,
            NecromancerSpellDefinition d, bool charge, int layer)
        {
            Color effectColor = EffectColor(d, layer);
            bool streak = layer == 1 && !charge &&
                (d.VfxStyle == NecromancerVfxStyle.Drain ||
                 d.VfxStyle == NecromancerVfxStyle.Fear ||
                 d.VfxStyle == NecromancerVfxStyle.Fire);
            renderer.renderMode = streak
                ? ParticleSystemRenderMode.Stretch
                : ParticleSystemRenderMode.Billboard;
            if (streak)
            {
                renderer.lengthScale = 0.45f;
                renderer.velocityScale = 0.12f;
            }
            renderer.sortingOrder = 10 + layer;
            Material material = new Material(
                Shader.Find("Legacy Shaders/Particles/Alpha Blended") ??
                Shader.Find("Sprites/Default") ?? FindParticleShader());
            material.hideFlags = HideFlags.HideAndDontSave;
            if (_softParticle && material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", _softParticle);
            TintMaterialProperty(material, "_Color", effectColor,
                charge ? 0.54f : 0.64f);
            TintMaterialProperty(material, "_TintColor", effectColor,
                charge ? 0.48f : 0.58f);
            TintMaterialProperty(material, "_BaseColor", effectColor,
                charge ? 0.54f : 0.64f);
            TintMaterialProperty(material, "_EmissionColor", effectColor,
                charge ? 0.12f : 0.16f);
            renderer.material = material;
            renderer.trailMaterial = material;
        }

        private static void CreateSpellImpactSignature(Transform parent,
            NecromancerSpellDefinition definition)
        {
            NecromancerImpactProfile profile =
                NecromancerImpactProfiles.Get(definition.Key);
            if (profile == null)
            {
                SkillsPlugin.Log.LogError(
                    $"Necromancer VFX: no unique impact profile for " +
                    $"'{definition.Name}'.");
                return;
            }

            var marker =
                parent.gameObject.AddComponent<NecromancerImpactSignatureMarker>();
            marker.Key = definition.Key;
            marker.SignatureId = profile.Id;
            marker.Motif = profile.Motif;
            marker.VisualFingerprint =
                profile.VisualFingerprint(definition.VfxColor);

            ParticleSystem signature = CreateRuntimeParticleSystem(parent,
                $"Unique Impact {profile.Id:00} - {profile.Motif}");
            signature.transform.localPosition =
                Vector3.up * profile.Height;
            ConfigureUniqueImpactParticles(signature, definition, profile);
            ConfigureImpactRenderer(
                signature.GetComponent<ParticleSystemRenderer>(),
                definition.VfxColor, profile.Trails, 22);

            ParticleSystem rings = CreateImpactRings(parent, definition,
                profile);
            ParticleSystem skulls = profile.SkullBurst > 0
                ? CreateImpactSkulls(parent, definition, profile)
                : null;
            CreateAuthoredMotif(parent, definition, profile);

            signature.Stop(true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            if (rings != null)
                rings.Stop(true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            if (skulls != null)
                skulls.Stop(true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static GameObject CreateAuthoredMotif(Transform parent,
            NecromancerSpellDefinition definition,
            NecromancerImpactProfile profile)
        {
            EnsureRuntimeMaterials();
            MotifRecipe recipe = MotifFor(definition.Key);
            var root = new GameObject(
                $"Authored Motif {profile.Id:00} - {definition.Key}");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.up *
                (Mathf.Max(0.05f, profile.Height * 0.34f) + recipe.Lift);
            root.transform.localRotation = recipe.Vertical
                ? Quaternion.Euler(90f, recipe.Twist, 0f)
                : Quaternion.Euler(0f, recipe.Twist, 0f);
            root.transform.localScale = Vector3.one * recipe.Scale;

            var marker = root.AddComponent<NecromancerMotifSignatureMarker>();
            marker.Key = definition.Key;
            marker.GeometryFingerprint = recipe.Fingerprint;

            Color primary = Brighten(definition.VfxColor, 1.32f);
            Color secondary = Brighten(profile.Secondary, 1.18f);
            CreateSigilLine(root.transform, "Impact Binding",
                BuildCircle(40 + recipe.Sides * 2, 0.48f), true,
                secondary, 0.018f + profile.Size * 0.055f);
            CreateSigilLine(root.transform, "Impact Crest",
                BuildCrest(recipe.Sides, 0.43f, recipe.Inner,
                    recipe.Twist), true, primary,
                0.024f + profile.Size * 0.070f);

            ParticleSystem satellites = CreateRuntimeParticleSystem(
                root.transform, "Motif Satellites");
            ParticleSystem.MainModule main = satellites.main;
            main.duration = Mathf.Clamp(profile.Duration, 0.62f, 1.86f);
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                0.38f, Mathf.Clamp(profile.Duration * 0.76f, 0.58f, 1.42f));
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
            main.startSize = new ParticleSystem.MinMaxCurve(
                0.045f, 0.075f + profile.Size * 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                primary, secondary);
            main.maxParticles = Mathf.Max(16, recipe.Satellites * 3);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.stopAction = ParticleSystemStopAction.None;

            ParticleSystem.EmissionModule emission = satellites.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.035f,
                    (short)(recipe.Satellites * 2))
            });
            ParticleSystem.ShapeModule shape = satellites.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.46f;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            ParticleSystem.VelocityOverLifetimeModule velocity =
                satellites.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.orbitalY = recipe.Spin * 0.032f;
            ParticleSystem.ColorOverLifetimeModule color =
                satellites.colorOverLifetime;
            color.enabled = true;
            color.color = SemanticImpactGradient(primary, secondary);
            ParticleSystem.SizeOverLifetimeModule size =
                satellites.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.08f),
                    new Keyframe(0.20f, 1f), new Keyframe(1f, 0f)));
            ConfigureImpactRenderer(
                satellites.GetComponent<ParticleSystemRenderer>(),
                primary, true, 25);
            satellites.Stop(true,
                ParticleSystemStopBehavior.StopEmittingAndClear);

            var light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Color.Lerp(definition.VfxColor,
                profile.Secondary, 0.36f);
            light.range = 2.8f + recipe.Scale * 1.65f;
            light.intensity = Mathf.Clamp(0.42f + recipe.Light,
                0.55f, 1.75f);
            light.shadows = LightShadows.None;

            var controller = root.AddComponent<NecromancerImpactMotifVfx>();
            controller.Lifetime = Mathf.Clamp(profile.Duration + 0.28f,
                0.82f, 2.28f);
            controller.Spin = recipe.Spin;
            controller.Pulse = recipe.Pulse;
            controller.Rise = recipe.Vertical
                ? 0.18f + recipe.Lift * 0.30f
                : 0.04f;
            return root;
        }

        private static Vector3[] BuildCrest(int sides, float outer,
            float inner, float twistDegrees)
        {
            int count = sides * 2;
            var points = new Vector3[count];
            float twist = twistDegrees * Mathf.Deg2Rad;
            for (int i = 0; i < count; i++)
            {
                float radius = (i & 1) == 0 ? outer : outer * inner;
                float angle = twist + i * Mathf.PI / sides;
                points[i] = new Vector3(Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius, 0f);
            }
            return points;
        }

        private static bool ValidateAuthoredMotifs(out string detail)
        {
            var fingerprints = new HashSet<string>(StringComparer.Ordinal);
            var errors = new List<string>();
            foreach (NecromancerSpellDefinition definition in
                NecromancerSpellData.All)
            {
                MotifRecipe recipe = MotifFor(definition.Key);
                if (recipe.Sides < 3 || recipe.Satellites < 1 ||
                    recipe.Scale <= 0f)
                    errors.Add(definition.Key + ": invalid motif recipe");
                else if (!fingerprints.Add(recipe.Fingerprint))
                    errors.Add(definition.Key + ": duplicate motif geometry");
            }
            detail = errors.Count == 0
                ? $"{fingerprints.Count}/{NecromancerSpellData.All.Count} " +
                  "authored crest geometries have unique silhouettes, " +
                  "orientation, pulse, light, and motion"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        private static MotifRecipe MotifFor(string key)
        {
            switch (key)
            {
                case "bone_thrall_1": return M(5, .48f, 3, .88f, .02f, 42f, 1.8f, false, .38f, 7f);
                case "siphon_spark": return M(4, .32f, 4, .76f, .28f, -76f, 2.8f, true, .46f, 19f);
                case "rotting_fever": return M(7, .66f, 5, .94f, .01f, 18f, 1.2f, false, .32f, 31f);
                case "bone_armor_1": return M(6, .54f, 6, 1.02f, .12f, 28f, 1.5f, false, .52f, 43f);
                case "clinging_shadow": return M(8, .28f, 3, .82f, .24f, -92f, 2.2f, true, .40f, 55f);
                case "mend_bones": return M(5, .70f, 7, .90f, .34f, 34f, 1.0f, true, .48f, 67f);
                case "noxious_bolt": return M(3, .38f, 5, .72f, .42f, 112f, 3.0f, true, .54f, 79f);
                case "fear": return M(9, .24f, 8, 1.08f, .38f, -126f, 2.6f, true, .62f, 91f);
                case "gather_shadows": return M(10, .62f, 4, 1.18f, .18f, -38f, .8f, false, .44f, 103f);
                case "bone_thrall_2": return M(6, .42f, 5, 1.06f, .03f, 54f, 1.7f, false, .50f, 115f);
                case "venomous_blood": return M(7, .30f, 9, .96f, .36f, 84f, 2.4f, true, .58f, 127f);
                case "dark_covenant_1": return M(4, .72f, 6, .92f, .26f, -64f, 1.4f, true, .56f, 139f);
                case "heart_flutter": return M(8, .46f, 4, 1.00f, .48f, 22f, 3.4f, true, .66f, 151f);
                case "siphon_soul": return M(11, .26f, 7, 1.16f, .44f, -148f, 2.9f, true, .72f, 163f);
                case "bone_armor_2": return M(7, .58f, 8, 1.22f, .10f, 48f, 1.3f, false, .68f, 175f);
                case "reclaim_servant": return M(5, .34f, 10, 1.28f, .22f, -104f, 3.2f, false, .74f, 11f);
                case "deathly_guise": return M(9, .68f, 3, 1.12f, .30f, -24f, .7f, true, .36f, 23f);
                case "leeching_curse": return M(6, .22f, 9, 1.10f, .40f, 136f, 2.7f, true, .76f, 35f);
                case "intensify_death": return M(8, .64f, 11, 1.20f, .16f, 72f, 1.6f, false, .70f, 47f);
                case "bone_thrall_3": return M(7, .44f, 7, 1.24f, .04f, 66f, 1.9f, false, .72f, 59f);
                case "infectious_cloud": return M(12, .74f, 12, 1.48f, .08f, 32f, 1.1f, false, .82f, 71f);
                case "dark_covenant_2": return M(5, .60f, 8, 1.14f, .32f, -82f, 2.0f, true, .78f, 83f);
                case "screaming_terror": return M(10, .20f, 13, 1.42f, .52f, 158f, 3.6f, true, .94f, 95f);
                case "scent_of_decay": return M(4, .50f, 11, 1.04f, .46f, -118f, 2.3f, true, .72f, 107f);
                case "bond_of_death": return M(9, .56f, 10, 1.36f, .20f, 96f, 1.8f, false, .88f, 119f);
                case "bone_thrall_4": return M(8, .40f, 9, 1.44f, .05f, 78f, 2.1f, false, .90f, 131f);
                case "soul_transfusion": return M(6, .76f, 12, 1.26f, .56f, -132f, 3.0f, true, .86f, 143f);
                case "bone_armor_3": return M(10, .52f, 14, 1.52f, .14f, 58f, 1.5f, false, .96f, 155f);
                case "devouring_darkness": return M(13, .18f, 15, 1.58f, .12f, -176f, 3.8f, false, 1.04f, 167f);
                case "drain_spirit": return M(11, .36f, 13, 1.50f, .62f, 172f, 3.3f, true, 1.02f, 179f);
                case "funeral_pyre": return M(7, .24f, 16, 1.54f, .06f, 124f, 2.8f, true, 1.12f, 13f);
                case "bone_thrall_5": return M(12, .38f, 17, 1.68f, .07f, 88f, 2.4f, false, 1.18f, 25f);
                case "dread_shield": return M(14, .48f, 15, 1.62f, .18f, -154f, 1.9f, false, 1.10f, 37f);
                case "harmshield": return M(16, .80f, 18, 1.72f, .24f, 44f, 1.0f, false, 1.24f, 49f);
                case "plague_of_souls": return M(15, .16f, 20, 1.84f, .10f, -188f, 4.2f, false, 1.34f, 61f);
                case "leeching_curse_dot": return M(6, .26f, 4, .68f, .30f, 108f, 2.5f, true, .34f, 73f);
                case "infectious_cloud_dot": return M(9, .72f, 6, .86f, .02f, -46f, 1.3f, false, .38f, 85f);
                default: return default;
            }
        }

        private static MotifRecipe M(int sides, float inner, int satellites,
            float scale, float lift, float spin, float pulse, bool vertical,
            float light, float twist) => new MotifRecipe
            {
                Sides = sides,
                Inner = inner,
                Satellites = satellites,
                Scale = scale,
                Lift = lift,
                Spin = spin,
                Pulse = pulse,
                Light = light,
                Vertical = vertical,
                Twist = twist
            };

        private static void ConfigureUniqueImpactParticles(
            ParticleSystem system, NecromancerSpellDefinition definition,
            NecromancerImpactProfile profile)
        {
            ParticleSystem.MainModule main = system.main;
            main.duration = profile.Duration;
            main.loop = false;
            main.prewarm = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.28f, profile.Duration * 0.42f),
                Mathf.Max(0.46f, profile.Duration * 0.82f));
            bool inward = profile.Motion == NecromancerImpactMotion.Implode ||
                profile.Motion == NecromancerImpactMotion.Mend ||
                profile.Motion == NecromancerImpactMotion.Collapse;
            bool suspended = profile.Motion == NecromancerImpactMotion.Orbit ||
                profile.Motion == NecromancerImpactMotion.Shell ||
                profile.Motion == NecromancerImpactMotion.Seal;
            main.startSpeed = inward
                ? new ParticleSystem.MinMaxCurve(-profile.Speed,
                    -profile.Speed * 0.42f)
                : suspended
                    ? new ParticleSystem.MinMaxCurve(0.02f,
                        Mathf.Max(0.08f, profile.Speed * 0.24f))
                    : new ParticleSystem.MinMaxCurve(
                        Mathf.Max(0.08f, profile.Speed * 0.32f),
                        profile.Speed);
            main.startSize = new ParticleSystem.MinMaxCurve(
                profile.Size * 0.42f, profile.Size);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Brighten(definition.VfxColor, 1.18f),
                profile.Secondary);
            main.gravityModifier = profile.Gravity;
            main.maxParticles = Mathf.Max(64,
                profile.Burst + profile.AccentBurst + 24);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)profile.Burst),
                new ParticleSystem.Burst(
                    Mathf.Min(0.18f, profile.Duration * 0.16f),
                    (short)profile.AccentBurst)
            });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ShapeForImpact(profile.Motion);
            shape.radius = profile.Radius;
            shape.radiusThickness = RadiusThicknessForImpact(profile.Motion);
            shape.angle = 14f + profile.Id % 17;
            if (shape.shapeType == ParticleSystemShapeType.Circle)
                shape.rotation = new Vector3(90f, 0f, 0f);
            else if (shape.shapeType == ParticleSystemShapeType.Cone)
                shape.rotation = new Vector3(-90f, 0f, 0f);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            ConfigureImpactVelocity(velocity, profile);

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = profile.Noise > 0.01f;
            if (noise.enabled)
            {
                noise.strength = profile.Noise;
                noise.frequency = 0.26f + profile.Id * 0.011f;
                noise.scrollSpeed = 0.18f + profile.Id * 0.006f;
            }

            ParticleSystem.ColorOverLifetimeModule color =
                system.colorOverLifetime;
            color.enabled = true;
            color.color = SemanticImpactGradient(definition.VfxColor,
                profile.Secondary);

            ParticleSystem.SizeOverLifetimeModule size =
                system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                ImpactSizeCurve(profile.Motion));

            ParticleSystem.RotationOverLifetimeModule rotation =
                system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(
                -Mathf.Abs(profile.Spin), Mathf.Abs(profile.Spin));

            ParticleSystem.TrailModule trails = system.trails;
            trails.enabled = profile.Trails;
            if (trails.enabled)
            {
                trails.lifetime = Mathf.Clamp(
                    profile.Duration * 0.16f, 0.12f, 0.34f);
                trails.dieWithParticles = true;
            }
        }

        private static ParticleSystemShapeType ShapeForImpact(
            NecromancerImpactMotion motion)
        {
            switch (motion)
            {
                case NecromancerImpactMotion.Rise:
                case NecromancerImpactMotion.Spiral:
                case NecromancerImpactMotion.Crawl:
                case NecromancerImpactMotion.Scream:
                case NecromancerImpactMotion.Orbit:
                case NecromancerImpactMotion.Helix:
                case NecromancerImpactMotion.Rune:
                    return ParticleSystemShapeType.Circle;
                case NecromancerImpactMotion.Transfer:
                case NecromancerImpactMotion.Pyre:
                    return ParticleSystemShapeType.Cone;
                case NecromancerImpactMotion.Shatter:
                    return ParticleSystemShapeType.Hemisphere;
                default:
                    return ParticleSystemShapeType.Sphere;
            }
        }

        private static float RadiusThicknessForImpact(
            NecromancerImpactMotion motion)
        {
            switch (motion)
            {
                case NecromancerImpactMotion.Orbit:
                case NecromancerImpactMotion.Rune:
                case NecromancerImpactMotion.Helix:
                case NecromancerImpactMotion.Spiral:
                    return 0.08f;
                case NecromancerImpactMotion.Shell:
                case NecromancerImpactMotion.Seal:
                    return 0.03f;
                case NecromancerImpactMotion.Cloud:
                case NecromancerImpactMotion.Cyclone:
                    return 0.86f;
                default:
                    return 0.52f;
            }
        }

        private static void ConfigureImpactVelocity(
            ParticleSystem.VelocityOverLifetimeModule velocity,
            NecromancerImpactProfile profile)
        {
            // Unity requires all three linear velocity curves to use the same
            // MinMaxCurve mode. New particle systems begin with constant X/Y/Z,
            // so assigning a two-constant Y curve alone logs an engine error and
            // can make the authored motion evaluate inconsistently. Keep the
            // complete linear triplet in two-constant mode for every recipe.
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            switch (profile.Motion)
            {
                case NecromancerImpactMotion.Rise:
                    velocity.y = new ParticleSystem.MinMaxCurve(
                        profile.Speed * 0.45f, profile.Speed);
                    velocity.orbitalY = profile.Spin * 0.22f;
                    break;
                case NecromancerImpactMotion.Veil:
                    velocity.y = new ParticleSystem.MinMaxCurve(
                        -profile.Speed, -profile.Speed * 0.24f);
                    velocity.orbitalY = profile.Spin * 0.32f;
                    break;
                case NecromancerImpactMotion.Spiral:
                case NecromancerImpactMotion.Helix:
                    velocity.y = new ParticleSystem.MinMaxCurve(
                        profile.Speed * 0.18f, profile.Speed * 0.58f);
                    velocity.orbitalY = profile.Spin;
                    velocity.radial = -profile.Speed * 0.16f;
                    break;
                case NecromancerImpactMotion.Orbit:
                    velocity.orbitalY = profile.Spin;
                    velocity.orbitalX = profile.Spin * 0.18f;
                    velocity.y = new ParticleSystem.MinMaxCurve(
                        -0.12f, 0.18f);
                    break;
                case NecromancerImpactMotion.Shell:
                case NecromancerImpactMotion.Seal:
                    velocity.orbitalY = profile.Spin * 0.74f;
                    velocity.orbitalX = profile.Spin * 0.31f;
                    velocity.radial = -profile.Speed * 0.08f;
                    break;
                case NecromancerImpactMotion.Implode:
                case NecromancerImpactMotion.Mend:
                case NecromancerImpactMotion.Collapse:
                    velocity.orbitalY = profile.Spin * 0.55f;
                    float vertical = profile.Motion ==
                        NecromancerImpactMotion.Collapse
                        ? -profile.Speed * 0.36f
                        : profile.Speed * 0.24f;
                    velocity.y = new ParticleSystem.MinMaxCurve(vertical,
                        vertical);
                    break;
                case NecromancerImpactMotion.Crawl:
                    velocity.y = new ParticleSystem.MinMaxCurve(
                        0.01f, 0.10f);
                    velocity.radial = profile.Speed * 0.72f;
                    break;
                case NecromancerImpactMotion.Pyre:
                    velocity.y = new ParticleSystem.MinMaxCurve(
                        profile.Speed * 0.72f, profile.Speed * 1.24f);
                    velocity.orbitalY = profile.Spin * 0.46f;
                    break;
                case NecromancerImpactMotion.Cloud:
                    velocity.y = new ParticleSystem.MinMaxCurve(
                        0.06f, profile.Speed * 0.42f);
                    velocity.radial = profile.Speed * 0.28f;
                    velocity.orbitalY = profile.Spin * 0.24f;
                    break;
                case NecromancerImpactMotion.Cyclone:
                    velocity.y = new ParticleSystem.MinMaxCurve(
                        profile.Speed * 0.15f, profile.Speed * 0.82f);
                    velocity.orbitalY = profile.Spin;
                    velocity.radial = -profile.Speed * 0.12f;
                    break;
                case NecromancerImpactMotion.Transfer:
                    velocity.y = new ParticleSystem.MinMaxCurve(
                        profile.Speed * 0.32f, profile.Speed * 0.74f);
                    velocity.orbitalY = profile.Spin * 0.38f;
                    break;
                default:
                    velocity.orbitalY = profile.Spin * 0.18f;
                    velocity.radial = profile.Speed * 0.16f;
                    break;
            }
        }

        private static void ConfigureLinearVelocityY(
            ParticleSystem.VelocityOverLifetimeModule velocity,
            float minimum, float maximum)
        {
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(minimum, maximum);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }

        private static AnimationCurve ImpactSizeCurve(
            NecromancerImpactMotion motion)
        {
            if (motion == NecromancerImpactMotion.Collapse ||
                motion == NecromancerImpactMotion.Implode ||
                motion == NecromancerImpactMotion.Mend)
                return new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(0.72f, 0.46f),
                    new Keyframe(1f, 0f));
            if (motion == NecromancerImpactMotion.Seal ||
                motion == NecromancerImpactMotion.Shell)
                return new AnimationCurve(
                    new Keyframe(0f, 0.08f),
                    new Keyframe(0.20f, 1f),
                    new Keyframe(0.78f, 0.86f),
                    new Keyframe(1f, 0f));
            return new AnimationCurve(
                new Keyframe(0f, 0.12f),
                new Keyframe(0.24f, 1f),
                new Keyframe(1f, 0f));
        }

        private static ParticleSystem.MinMaxGradient SemanticImpactGradient(
            Color primary, Color secondary)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Brighten(secondary, 1.18f), 0f),
                    new GradientColorKey(primary, 0.38f),
                    new GradientColorKey(Darken(EvilViolet, 0.42f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.96f, 0.10f),
                    new GradientAlphaKey(0.72f, 0.68f),
                    new GradientAlphaKey(0f, 1f)
                });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        private static ParticleSystem CreateImpactRings(Transform parent,
            NecromancerSpellDefinition definition,
            NecromancerImpactProfile profile)
        {
            if (profile.RingCount <= 0) return null;
            ParticleSystem system = CreateRuntimeParticleSystem(parent,
                $"Semantic Rings - {profile.Motif}");
            system.transform.localPosition =
                Vector3.up * Mathf.Max(0.06f, profile.Height * 0.42f);

            ParticleSystem.MainModule main = system.main;
            main.duration = profile.Duration;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                0.34f, Mathf.Min(0.82f, profile.Duration * 0.66f));
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                profile.Speed * 0.42f + 0.18f,
                profile.Speed * 0.68f + 0.36f);
            main.startSize = new ParticleSystem.MinMaxCurve(
                profile.Size * 0.20f, profile.Size * 0.42f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Brighten(profile.Secondary, 1.18f),
                definition.VfxColor);
            main.maxParticles = profile.RingCount * 22;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            var bursts = new ParticleSystem.Burst[profile.RingCount];
            short ringParticles = (short)(10 + profile.Id % 7);
            for (int i = 0; i < profile.RingCount; i++)
                bursts[i] = new ParticleSystem.Burst(i * 0.085f,
                    ringParticles);
            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(bursts);

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.rotation = new Vector3(90f, 0f, 0f);
            shape.radius = Mathf.Max(0.10f, profile.Radius * 0.16f);
            shape.radiusThickness = 0f;

            ParticleSystem.VelocityOverLifetimeModule velocity =
                system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.orbitalY = profile.Spin * 0.12f;

            ParticleSystem.ColorOverLifetimeModule color =
                system.colorOverLifetime;
            color.enabled = true;
            color.color = SemanticImpactGradient(profile.Secondary,
                definition.VfxColor);

            ParticleSystem.SizeOverLifetimeModule size =
                system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.25f),
                    new Keyframe(0.22f, 1f),
                    new Keyframe(1f, 0f)));

            ConfigureImpactRenderer(
                system.GetComponent<ParticleSystemRenderer>(),
                profile.Secondary, true, 23);
            return system;
        }

        private static ParticleSystem CreateImpactSkulls(Transform parent,
            NecromancerSpellDefinition definition,
            NecromancerImpactProfile profile)
        {
            ParticleSystem system = CreateRuntimeParticleSystem(parent,
                $"Spectral Skulls - {profile.Motif}");
            system.transform.localPosition =
                Vector3.up * Mathf.Max(0.38f, profile.Height);

            ParticleSystem.MainModule main = system.main;
            main.duration = profile.Duration;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                0.52f, Mathf.Min(1.18f, profile.Duration));
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                profile.Speed * 0.24f, profile.Speed * 0.62f);
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.12f, profile.Size * 1.18f),
                Mathf.Max(0.20f, profile.Size * 1.92f));
            main.startColor = new ParticleSystem.MinMaxGradient(
                Brighten(definition.VfxColor, 1.35f),
                Brighten(profile.Secondary, 1.15f));
            main.maxParticles = Mathf.Max(18, profile.SkullBurst + 4);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.04f,
                    (short)profile.SkullBurst)
            });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType =
                profile.Motion == NecromancerImpactMotion.Scream
                    ? ParticleSystemShapeType.Circle
                    : ParticleSystemShapeType.Sphere;
            shape.radius = profile.Radius * 0.46f;
            shape.radiusThickness = 0.22f;
            if (shape.shapeType == ParticleSystemShapeType.Circle)
                shape.rotation = new Vector3(90f, 0f, 0f);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.orbitalY = profile.Spin * 0.72f;
            ConfigureLinearVelocityY(velocity, 0.08f,
                profile.Speed * 0.42f);

            ParticleSystem.ColorOverLifetimeModule color =
                system.colorOverLifetime;
            color.enabled = true;
            color.color = SemanticImpactGradient(definition.VfxColor,
                profile.Secondary);

            ParticleSystem.SizeOverLifetimeModule size =
                system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.06f),
                    new Keyframe(0.18f, 1f),
                    new Keyframe(0.82f, 0.86f),
                    new Keyframe(1f, 0f)));

            ParticleSystem.RotationOverLifetimeModule rotation =
                system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(
                -profile.Spin, profile.Spin);

            ParticleSystemRenderer renderer =
                system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = 24;
            renderer.sharedMaterial = _skullParticleMaterial;
            return system;
        }

        private static void ConfigureImpactRenderer(
            ParticleSystemRenderer renderer, Color color, bool streak,
            int sortingOrder)
        {
            renderer.renderMode = streak
                ? ParticleSystemRenderMode.Stretch
                : ParticleSystemRenderMode.Billboard;
            if (streak)
            {
                renderer.lengthScale = 0.38f;
                renderer.velocityScale = 0.14f;
            }
            renderer.sortingOrder = sortingOrder;
            Material material = new Material(
                Shader.Find("Legacy Shaders/Particles/Alpha Blended") ??
                Shader.Find("Sprites/Default") ?? FindParticleShader());
            material.name = "CS Necromancer Impact " + sortingOrder;
            material.hideFlags = HideFlags.HideAndDontSave;
            if (_softParticle && material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", _softParticle);
            TintMaterialProperty(material, "_Color", color, 0.78f);
            TintMaterialProperty(material, "_TintColor", color, 0.72f);
            TintMaterialProperty(material, "_BaseColor", color, 0.78f);
            TintMaterialProperty(material, "_EmissionColor", color, 0.28f);
            renderer.material = material;
            renderer.trailMaterial = material;
        }

        private static Color EffectColor(NecromancerSpellDefinition definition,
            int layer)
        {
            if (definition.Kind != NecromancerSpellKind.SummonPet)
                return definition.VfxColor;
            return layer == 0 ? EvilViolet : EvilCrimson;
        }

        private static Material FindBaseMaterial(IEnumerable<GameObject> database)
        {
            foreach (GameObject candidate in database)
            {
                ParticleSystemRenderer renderer = candidate
                    ? candidate.GetComponentInChildren<ParticleSystemRenderer>(true)
                    : null;
                if (renderer?.sharedMaterial) return renderer.sharedMaterial;
            }
            return null;
        }

        private static Shader FindParticleShader()
        {
            return Shader.Find("Particles/Standard Unlit") ??
                   Shader.Find("Legacy Shaders/Particles/Additive") ??
                   Shader.Find("Sprites/Default");
        }

        private static Texture2D CreateSoftParticleTexture()
        {
            Texture2D authored = EmbeddedResources.LoadTexture(
                "NecromancerSmokeFupi.png",
                "CS Necromancer CC0 Vapor Particle");
            if (authored != null) return authored;

            SkillsPlugin.Log.LogWarning(
                "Necromancer VFX: falling back to the procedural soft particle.");
            return CreateProceduralSoftParticleTexture();
        }

        private static Texture2D CreateProceduralSoftParticleTexture()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "CS Necromancer Soft Particle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(1f - radius);
                alpha *= alpha;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D CreateSkullParticleTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "CS Necromancer Spectral Skull Particle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = (x + 0.5f) / size * 2f - 1f;
                float py = (y + 0.5f) / size * 2f - 1f;
                float cranium = 1f - Mathf.Sqrt(
                    px * px / 0.58f +
                    (py - 0.18f) * (py - 0.18f) / 0.72f);
                float jawWidth = Mathf.Lerp(0.30f, 0.48f,
                    Mathf.Clamp01((py + 0.62f) / 0.40f));
                float jaw = py > -0.64f && py < -0.17f &&
                    Mathf.Abs(px) < jawWidth ? 0.82f : 0f;
                float alpha = Mathf.Clamp01(Mathf.Max(cranium * 8f, jaw));

                bool eye = (new Vector2(px - 0.23f, py - 0.17f)
                        .sqrMagnitude < 0.028f) ||
                    (new Vector2(px + 0.23f, py - 0.17f)
                        .sqrMagnitude < 0.028f);
                bool nose = py < -0.05f && py > -0.31f &&
                    Mathf.Abs(px) < (py + 0.33f) * 0.36f;
                bool toothGap = py < -0.38f && py > -0.66f &&
                    (Mathf.Abs(px - 0.105f) < 0.025f ||
                     Mathf.Abs(px + 0.105f) < 0.025f ||
                     Mathf.Abs(px - 0.285f) < 0.022f ||
                     Mathf.Abs(px + 0.285f) < 0.022f);
                if (eye || nose || toothGap) alpha = 0f;

                float edge = Mathf.Clamp01((1f - Mathf.Max(
                    Mathf.Abs(px), Mathf.Abs(py))) * 10f);
                alpha *= edge;
                texture.SetPixel(x, y,
                    new Color(1f, 1f, 1f, alpha));
            }
            texture.Apply(false, true);
            return texture;
        }

        private static Material CreateSkullParticleMaterial()
        {
            Material material = new Material(
                Shader.Find("Legacy Shaders/Particles/Alpha Blended") ??
                Shader.Find("Sprites/Default") ?? FindParticleShader());
            material.name = "CS Necromancer Spectral Skull Material";
            material.hideFlags = HideFlags.HideAndDontSave;
            if (_skullParticle)
            {
                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", _skullParticle);
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", _skullParticle);
            }
            TintMaterialProperty(material, "_Color", Color.white, 1f);
            TintMaterialProperty(material, "_TintColor", Color.white, 1f);
            TintMaterialProperty(material, "_BaseColor", Color.white, 1f);
            TintMaterialProperty(material, "_EmissionColor",
                new Color(0.72f, 0.24f, 1f, 1f), 0.42f);
            return material;
        }

        private static ParticleSystem.MinMaxGradient FadeGradient(Color baseColor)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Darken(baseColor, 0.88f), 0f),
                    new GradientColorKey(Darken(baseColor, 0.72f), 0.45f),
                    new GradientColorKey(Darken(baseColor, 0.42f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.72f, 0.12f),
                    new GradientAlphaKey(0.42f, 0.65f),
                    new GradientAlphaKey(0f, 1f)
                });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        private static ParticleSystemShapeType ShapeFor(
            NecromancerVfxStyle style, bool charge)
        {
            if (charge)
            {
                switch (style)
                {
                    case NecromancerVfxStyle.Disease:
                    case NecromancerVfxStyle.Cloud:
                    case NecromancerVfxStyle.Blood:
                    case NecromancerVfxStyle.Death:
                        return ParticleSystemShapeType.Sphere;
                    case NecromancerVfxStyle.Ward:
                    case NecromancerVfxStyle.Mend:
                        return ParticleSystemShapeType.Hemisphere;
                    case NecromancerVfxStyle.Fire:
                        return ParticleSystemShapeType.Cone;
                    default:
                        return ParticleSystemShapeType.Circle;
                }
            }
            switch (style)
            {
                case NecromancerVfxStyle.Fire: return ParticleSystemShapeType.Cone;
                case NecromancerVfxStyle.Cloud:
                case NecromancerVfxStyle.Disease: return ParticleSystemShapeType.Sphere;
                case NecromancerVfxStyle.Ward: return ParticleSystemShapeType.Hemisphere;
                default: return ParticleSystemShapeType.Circle;
            }
        }

        private static int ResolveBurst(NecromancerVfxStyle style)
        {
            return style == NecromancerVfxStyle.Cloud ? 16 :
                   style == NecromancerVfxStyle.Bone ? 12 : 9;
        }

        private static int AccentBurst(NecromancerVfxStyle style)
        {
            return style == NecromancerVfxStyle.Bone ? 8 : 5;
        }

        private static float ResolveRadius(NecromancerVfxStyle style)
        {
            switch (style)
            {
                case NecromancerVfxStyle.Cloud: return 1.45f;
                case NecromancerVfxStyle.Ward: return 1.05f;
                case NecromancerVfxStyle.Bone: return 0.82f;
                default: return 0.62f;
            }
        }

        private static float ResolveDuration(NecromancerVfxStyle style)
        {
            return style == NecromancerVfxStyle.Cloud ? 1.55f : 0.92f;
        }

        private static Color Brighten(Color value, float multiplier)
        {
            return new Color(Mathf.Clamp01(value.r * multiplier),
                Mathf.Clamp01(value.g * multiplier),
                Mathf.Clamp01(value.b * multiplier), value.a);
        }

        private static Color Darken(Color value, float multiplier)
        {
            return new Color(value.r * multiplier, value.g * multiplier,
                value.b * multiplier, value.a);
        }

        internal static void PlayProjectile(NecromancerSpellDefinition definition,
            Transform source, Transform target)
        {
            if (definition == null || source == null || target == null ||
                definition.SelfOnly || definition.Kind == NecromancerSpellKind.SummonPet ||
                !Effects.ContainsKey(definition.Key)) return;
            try
            {
                EnsureRuntimeMaterials();
                Vector3 start = source.position + Vector3.up * 1.25f +
                    source.forward * 0.35f;
                var projectile = new GameObject(
                    "CS Necromancer Projectile - " + definition.Name);
                projectile.transform.position = start;
                projectile.name = "CS Necromancer Projectile - " + definition.Name;
                CreateLightweightProjectileTrail(projectile.transform,
                    definition);
                var mover = projectile.AddComponent<NecromancerProjectile>();
                mover.Target = target;
                mover.Speed = definition.VfxStyle == NecromancerVfxStyle.Drain
                    ? 34f : 42f;
                mover.Lifetime = 1.2f;
                // Life-stealing spells send the stolen life back as a dark orb
                // once the bolt connects.
                mover.ReturnTo = definition.Lifetap ? source : null;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    $"Necromancer projectile VFX failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends the stolen life back to the caster as a dark orb once a
        /// lifetap bolt has struck. Deliberately slower than the outbound bolt
        /// so the theft reads as a distinct return trip rather than a rebound.
        /// </summary>
        internal static void PlayLifetapReturn(Vector3 origin, Transform caster)
        {
            if (caster == null) return;
            try
            {
                EnsureRuntimeMaterials();
                EnsureLifetapOrbTemplate();

                GameObject orb = UnityEngine.Object.Instantiate(
                    _lifetapOrbTemplate);
                orb.name = LifetapOrbObjectName;
                orb.hideFlags = HideFlags.None;
                orb.transform.position = origin;

                var mover = orb.AddComponent<NecromancerProjectile>();
                mover.Target = caster;
                mover.Speed = 24f;
                mover.Lifetime = 1.8f;
                mover.ReturnTo = null;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    $"Necromancer lifetap return VFX failed: {ex.Message}");
            }
        }

        private static void EnsureLifetapOrbTemplate()
        {
            if (_lifetapOrbTemplate) return;

            var template = new GameObject(LifetapOrbObjectName);
            template.hideFlags = HideFlags.HideAndDontSave;
            template.transform.position = new Vector3(0f, -10000f, 0f);
            UnityEngine.Object.DontDestroyOnLoad(template);

            CreateLifetapOrbVisual(template.transform);
            foreach (ParticleSystem system in
                template.GetComponentsInChildren<ParticleSystem>(true))
                system.Stop(true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);

            _lifetapOrbTemplate = template;
        }

        /// <summary>
        /// A tight, dense sphere of near-black particles with a dragging trail.
        /// The palette is fixed rather than per-spell: the orb represents
        /// stolen life itself, which should look the same whichever lifetap
        /// pulled it.
        /// </summary>
        private static void CreateLifetapOrbVisual(Transform parent)
        {
            ParticleSystem system = CreateRuntimeParticleSystem(parent,
                "Siphoned Life Core");

            ParticleSystem.MainModule main = system.main;
            main.duration = 0.6f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.14f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.29f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.04f, 0.006f, 0.07f, 1f),
                Darken(EvilCrimson, 0.55f));
            main.maxParticles = 44;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 48f;

            // Tight and solid rather than a spray — this has to read as one
            // object in flight, not a cloud.
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.085f;
            shape.radiusThickness = 1f;

            var stolen = new Gradient();
            stolen.SetKeys(
                new[]
                {
                    new GradientColorKey(Darken(EvilCrimson, 0.7f), 0f),
                    new GradientColorKey(new Color(0.06f, 0.01f, 0.10f), 0.5f),
                    new GradientColorKey(new Color(0.02f, 0f, 0.04f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.98f, 0.15f),
                    new GradientAlphaKey(0.85f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                });
            ParticleSystem.ColorOverLifetimeModule color =
                system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(stolen);

            ParticleSystem.SizeOverLifetimeModule size =
                system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(1f, 0.15f)));

            ParticleSystem.TrailModule trails = system.trails;
            trails.enabled = true;
            trails.lifetime = 0.26f;
            trails.dieWithParticles = false;
            trails.widthOverTrail = 0.55f;

            ConfigureRuntimeRenderer(
                system.GetComponent<ParticleSystemRenderer>(),
                streak: false, sortingOrder: 23);
            system.Play(true);
        }

        internal static void PlayPersistent(NecromancerSpellDefinition definition,
            Transform target, float seconds)
        {
            if (definition == null || target == null || seconds <= 0f ||
                !Effects.TryGetValue(definition.Key, out Pair pair)) return;
            try
            {
                string effectName =
                    "CS Necromancer Persistent - " + definition.Name;
                Transform existing = target.Find(effectName);
                if (existing != null)
                {
                    NecromancerTimedVfx timer =
                        existing.GetComponent<NecromancerTimedVfx>();
                    if (timer != null) timer.Lifetime = seconds;
                    return;
                }
                var aura = UnityEngine.Object.Instantiate(pair.ResolvePrefab,
                    target.position, Quaternion.identity, target);
                aura.name = effectName;
                PersistentPlacement placement =
                    PersistentPlacementFor(definition);
                aura.transform.localPosition = Vector3.up * placement.Height;
                aura.transform.localRotation =
                    Quaternion.Euler(0f, placement.Yaw, 0f);
                aura.transform.localScale *= placement.Scale;
                var placementMarker =
                    aura.AddComponent<NecromancerPersistentPlacementMarker>();
                placementMarker.Key = definition.Key;
                placementMarker.Band = placement.Band;
                placementMarker.Height = placement.Height;
                ConfigurePersistentAura(aura);
                aura.AddComponent<NecromancerTimedVfx>().Lifetime = seconds;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    $"Necromancer persistent VFX failed: {ex.Message}");
            }
        }

        internal static bool PlayCastAura(
            NecromancerSpellDefinition definition, CastSpell source,
            Spell spell, float castSeconds, out bool summonSigil)
        {
            summonSigil = false;
            if (definition == null || source == null || spell == null)
                return false;
            try
            {
                EnsureRuntimeMaterials();
                EnsureCastAuraTemplate();
                RetireNamedChildren(source.transform, CastAuraObjectName);

                GameObject aura = UnityEngine.Object.Instantiate(
                    _castAuraTemplate, source.transform, false);
                // Instantiate suffixes "(Clone)" and copies the template's
                // hide flags. Both must be undone: RetireNamedChildren,
                // HasActiveCastAura and HasActiveOrbitingSkulls all match on
                // the exact name, and the clone is a normal scene object that
                // should die with its zone.
                aura.name = CastAuraObjectName;
                aura.hideFlags = HideFlags.None;
                aura.transform.localPosition = Vector3.up * 0.28f;
                aura.transform.localRotation = Quaternion.identity;
                aura.transform.localScale = Vector3.one;

                ApplyStandardCastAura(aura.transform, castSeconds);

                var controller =
                    aura.AddComponent<NecromancerCastAuraVfx>();
                controller.Source = source;
                controller.Spell = spell;
                controller.Lifetime = Mathf.Max(0.65f, castSeconds + 0.75f);

                if (definition.Kind == NecromancerSpellKind.SummonPet)
                {
                    summonSigil = CreateDemonicSigil(source.transform,
                        definition, source, spell,
                        Mathf.Max(0.75f, castSeconds + 0.5f),
                        followCast: true, resolveBurst: false);
                }
                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    $"Necromancer caster VFX failed for " +
                    $"'{definition.Name}': {ex.Message}");
                return false;
            }
        }

        private static void CreateLightweightProjectileTrail(Transform parent,
            NecromancerSpellDefinition definition)
        {
            NecromancerImpactProfile profile =
                NecromancerImpactProfiles.Get(definition.Key);
            float identity = profile != null ? profile.Id : 1f;
            ParticleSystem system = CreateRuntimeParticleSystem(parent,
                "Bounded " + (profile?.Motif ?? "Soul Trail"));
            ParticleSystem.MainModule main = system.main;
            main.duration = 0.38f + identity % 5f * 0.025f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                0.08f + identity % 3f * 0.018f,
                0.18f + identity % 7f * 0.016f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                0.04f, 0.18f + identity % 6f * 0.035f);
            main.startSize = new ParticleSystem.MinMaxCurve(
                0.022f + identity % 4f * 0.005f,
                0.062f + (profile?.Size ?? 0.08f) * 0.34f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Brighten(definition.VfxColor, 1.22f),
                profile != null
                    ? Brighten(profile.Secondary, 1.08f)
                    : Darken(EvilViolet, 0.82f));
            main.maxParticles = 14 + (int)identity % 8;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 17f + identity % 9f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = profile != null &&
                (profile.Motion == NecromancerImpactMotion.Spiral ||
                 profile.Motion == NecromancerImpactMotion.Helix ||
                 profile.Motion == NecromancerImpactMotion.Rune)
                    ? ParticleSystemShapeType.Circle
                    : ParticleSystemShapeType.Sphere;
            shape.radius = 0.065f + identity % 5f * 0.008f;
            shape.radiusThickness = 0.42f + identity % 4f * 0.14f;

            ParticleSystem.VelocityOverLifetimeModule velocity =
                system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.orbitalZ = profile != null
                ? profile.Spin * 0.055f
                : 0.2f;

            ParticleSystem.ColorOverLifetimeModule color =
                system.colorOverLifetime;
            color.enabled = true;
            color.color = EvilGradient(definition.VfxColor,
                profile?.Secondary ?? EvilViolet);

            ParticleSystem.TrailModule trails = system.trails;
            trails.enabled = profile?.Trails == true;
            if (trails.enabled)
            {
                trails.lifetime = 0.12f + identity % 5f * 0.022f;
                trails.dieWithParticles = true;
            }

            ParticleSystem.SizeOverLifetimeModule size =
                system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.22f),
                    new Keyframe(0.24f, 1f),
                    new Keyframe(1f, 0f)));

            ConfigureRuntimeRenderer(
                system.GetComponent<ParticleSystemRenderer>(),
                streak: true, sortingOrder: 21);
            system.Play(true);
        }

        private static void ConfigurePersistentAura(GameObject aura)
        {
            foreach (ParticleSystem system in
                aura.GetComponentsInChildren<ParticleSystem>(true))
            {
                bool ambient = system.transform == aura.transform ||
                    (system.transform.parent == aura.transform &&
                     string.Equals(system.name, "Accent",
                         StringComparison.Ordinal));
                ParticleSystem.MainModule main = system.main;
                main.loop = ambient;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.stopAction = ParticleSystemStopAction.None;
                main.cullingMode = ParticleSystemCullingMode.Automatic;
                main.maxParticles = Mathf.Min(main.maxParticles,
                    ambient ? 18 : 40);

                if (ambient)
                {
                    ParticleSystem.EmissionModule emission = system.emission;
                    emission.enabled = true;
                    emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
                    emission.rateOverTime =
                        system.transform == aura.transform ? 4f : 2.5f;

                    ParticleSystem.TrailModule trails = system.trails;
                    trails.enabled = false;
                    ParticleSystem.NoiseModule noise = system.noise;
                    if (noise.enabled) noise.strengthMultiplier *= 0.45f;
                }

                system.Play(true);
            }
        }

        internal static bool PlaySummonResolve(
            NecromancerSpellDefinition definition, Transform source)
        {
            if (definition == null || source == null ||
                definition.Kind != NecromancerSpellKind.SummonPet)
                return false;
            try
            {
                EnsureRuntimeMaterials();
                NecromancerAudio.PlayResolve(definition,
                    source.position + Vector3.up * 0.72f);
                NecromancerImpactProfile profile =
                    NecromancerImpactProfiles.Get(definition.Key);
                if (profile != null)
                {
                    GameObject motif = CreateAuthoredMotif(source,
                        definition, profile);
                    foreach (ParticleSystem system in
                        motif.GetComponentsInChildren<ParticleSystem>(true))
                        system.Play(true);
                }
                return CreateDemonicSigil(source, definition, null, null, 1.55f,
                    followCast: false, resolveBurst: true);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    $"Necromancer summon resolve VFX failed for " +
                    $"'{definition.Name}': {ex.Message}");
                return false;
            }
        }

        internal static bool HasActiveCastAura(Transform source)
        {
            Transform aura = source?.Find(CastAuraObjectName);
            return aura != null && aura.gameObject.activeInHierarchy &&
                aura.GetComponent<NecromancerEvilCastAuraMarker>() != null;
        }

        internal static bool HasActiveOrbitingSkulls(Transform source)
        {
            Transform aura = source?.Find(CastAuraObjectName);
            return aura != null && aura.gameObject.activeInHierarchy &&
                aura.GetComponentInChildren<
                    NecromancerOrbitingSkullsMarker>(true) != null;
        }

        internal static bool HasActiveSummonSigil(Transform source)
        {
            Transform sigil = source?.Find(SummoningSigilObjectName);
            return sigil != null && sigil.gameObject.activeInHierarchy &&
                sigil.GetComponent<NecromancerSummonSigilMarker>() != null;
        }

        private static void EnsureRuntimeMaterials()
        {
            if (_softParticle == null)
                _softParticle = CreateSoftParticleTexture();
            if (_skullParticle == null)
                _skullParticle = CreateSkullParticleTexture();
            if (_skullParticleMaterial == null)
                _skullParticleMaterial = CreateSkullParticleMaterial();
            if (_runtimeParticleMaterial == null)
            {
                Shader alphaShader =
                    Shader.Find("Legacy Shaders/Particles/Alpha Blended") ??
                    Shader.Find("Sprites/Default") ?? FindParticleShader();
                _runtimeParticleMaterial = new Material(alphaShader);
                _runtimeParticleMaterial.name =
                    "CS Necromancer Runtime Particle Material";
                _runtimeParticleMaterial.hideFlags =
                    HideFlags.HideAndDontSave;
                if (_softParticle &&
                    _runtimeParticleMaterial.HasProperty("_MainTex"))
                    _runtimeParticleMaterial.SetTexture("_MainTex",
                        _softParticle);
                TintMaterialProperty(_runtimeParticleMaterial, "_Color",
                    Color.white, 1f);
                TintMaterialProperty(_runtimeParticleMaterial, "_TintColor",
                    Color.white, 1f);
                TintMaterialProperty(_runtimeParticleMaterial, "_BaseColor",
                    Color.white, 1f);
                TintMaterialProperty(_runtimeParticleMaterial,
                    "_EmissionColor", Color.white, 0.24f);
            }
            if (_sigilMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default") ??
                    FindParticleShader();
                _sigilMaterial = new Material(shader)
                {
                    name = "CS Necromancer Demonic Sigil Material",
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (_sigilMaterial.HasProperty("_MainTex"))
                    _sigilMaterial.SetTexture("_MainTex",
                        Texture2D.whiteTexture);
                if (_sigilMaterial.HasProperty("_Color"))
                    _sigilMaterial.SetColor("_Color", Color.white);
            }
        }

        /// <summary>
        /// Builds the shared cast-aura hierarchy once. Casting clones this
        /// instead of running three AddComponent&lt;ParticleSystem&gt; calls
        /// plus roughly thirty module writes — each allocating curves and
        /// gradients — on every single cast.
        /// </summary>
        private static void EnsureCastAuraTemplate()
        {
            if (_castAuraTemplate) return;

            var template = new GameObject(CastAuraObjectName);
            template.hideFlags = HideFlags.HideAndDontSave;
            template.transform.position = new Vector3(0f, -10000f, 0f);
            template.AddComponent<NecromancerEvilCastAuraMarker>();
            // Casts happen in zone scenes while this is authored once, so the
            // template has to survive scene transitions like the charge and
            // resolve prefabs do.
            UnityEngine.Object.DontDestroyOnLoad(template);

            // Child order is contractual — ApplyStandardCastAura indexes it.
            CreateEvilSmoke(template.transform, TemplateCastSeconds,
                TemplateAccent);
            CreateEvilEmbers(template.transform, TemplateCastSeconds);
            CreateOrbitingSkulls(template.transform, TemplateCastSeconds,
                TemplateAccent);

            foreach (ParticleSystem system in
                template.GetComponentsInChildren<ParticleSystem>(true))
                system.Stop(true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);

            _castAuraTemplate = template;
        }

        /// <summary>
        /// Applies only the cast length to a freshly cloned aura. Geometry,
        /// palette, emission and motion deliberately remain identical for
        /// every non-summoning spell.
        /// </summary>
        private static void ApplyStandardCastAura(Transform aura,
            float castSeconds)
        {
            ParticleSystem smoke = GetCastAuraLayer(aura, 0, GraveSmokeName);
            if (smoke != null)
            {
                ParticleSystem.MainModule main = smoke.main;
                main.duration = Mathf.Max(0.5f, castSeconds);
            }

            ParticleSystem embers = GetCastAuraLayer(aura, 1, SoulEmbersName);
            if (embers != null)
            {
                ParticleSystem.MainModule main = embers.main;
                main.duration = Mathf.Max(0.5f, castSeconds);
            }

            ParticleSystem skulls =
                GetCastAuraLayer(aura, 2, OrbitingSkullsName);
            if (skulls != null)
            {
                ParticleSystem.MainModule main = skulls.main;
                main.duration = Mathf.Max(0.55f, castSeconds);
            }
        }

        private static PersistentPlacement PersistentPlacementFor(
            NecromancerSpellDefinition definition)
        {
            MotifRecipe recipe = MotifFor(definition.Key);
            var placement = new PersistentPlacement
            {
                Height = 0.34f + recipe.Lift * 0.28f,
                Scale = 0.50f + Mathf.Clamp(recipe.Scale, 0.68f, 1.34f) * 0.16f,
                Yaw = recipe.Twist,
                Band = "lower-body"
            };

            if (definition.Pbaoe || definition.VfxStyle ==
                    NecromancerVfxStyle.Cloud)
            {
                placement.Height = 0.06f;
                placement.Scale = 0.82f;
                placement.Band = "ground-area";
                return placement;
            }

            switch (definition.VfxStyle)
            {
                case NecromancerVfxStyle.Disease:
                case NecromancerVfxStyle.Fire:
                    placement.Height = 0.12f + recipe.Lift * 0.10f;
                    placement.Scale = 0.70f;
                    placement.Band = "feet-ground";
                    break;
                case NecromancerVfxStyle.Fear:
                    placement.Height = 1.06f + recipe.Lift * 0.22f;
                    placement.Scale = 0.54f;
                    placement.Band = "head-control";
                    break;
                case NecromancerVfxStyle.Bone:
                case NecromancerVfxStyle.Ward:
                    placement.Height = 0.48f + recipe.Lift * 0.16f;
                    placement.Scale = 0.66f;
                    placement.Band = "torso-armor";
                    break;
                case NecromancerVfxStyle.Mend:
                    placement.Height = 0.40f + recipe.Lift * 0.20f;
                    placement.Scale = 0.62f;
                    placement.Band = "pet-body";
                    break;
                case NecromancerVfxStyle.Shadow:
                    placement.Height = definition.SelfOnly ? 0.36f : 0.18f;
                    placement.Scale = definition.SelfOnly ? 0.72f : 0.68f;
                    placement.Band = definition.SelfOnly
                        ? "body-shroud" : "feet-shadow";
                    break;
                case NecromancerVfxStyle.Drain:
                case NecromancerVfxStyle.Blood:
                case NecromancerVfxStyle.Curse:
                case NecromancerVfxStyle.Death:
                    placement.Height = 0.24f + recipe.Lift * 0.52f;
                    placement.Scale = 0.58f + recipe.Inner * 0.12f;
                    placement.Band = recipe.Vertical
                        ? "chest-vertical" : "lower-body";
                    break;
            }
            return placement;
        }

        /// <summary>
        /// Resolves a cloned aura layer by its fixed template index. The name
        /// check makes a future reorder of EnsureCastAuraTemplate fail visibly
        /// rather than silently tinting the wrong emitter.
        /// </summary>
        private static ParticleSystem GetCastAuraLayer(Transform aura,
            int index, string expectedName)
        {
            if (aura == null || index >= aura.childCount) return null;
            Transform layer = aura.GetChild(index);
            if (!string.Equals(layer.name, expectedName,
                    StringComparison.Ordinal))
            {
                SkillsPlugin.Log.LogWarning(
                    $"Necromancer cast aura layer {index} was " +
                    $"'{layer.name}', expected '{expectedName}'.");
                return null;
            }
            return layer.GetComponent<ParticleSystem>();
        }

        private static void CreateEvilSmoke(Transform parent,
            float castSeconds, Color spellAccent)
        {
            ParticleSystem system = CreateRuntimeParticleSystem(parent,
                GraveSmokeName);
            system.transform.localPosition = Vector3.up * 0.38f;

            ParticleSystem.MainModule main = system.main;
            main.duration = Mathf.Max(0.5f, castSeconds);
            main.loop = true;
            main.startLifetime =
                new ParticleSystem.MinMaxCurve(0.48f, 1.05f);
            main.startSpeed =
                new ParticleSystem.MinMaxCurve(0.08f, 0.38f);
            main.startSize =
                new ParticleSystem.MinMaxCurve(0.055f, 0.19f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Darken(EvilViolet, 0.62f), spellAccent);
            main.maxParticles = 72;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 18f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.68f;
            shape.radiusThickness = 0.78f;

            ParticleSystem.VelocityOverLifetimeModule velocity =
                system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            ConfigureLinearVelocityY(velocity, 0.24f, 0.62f);

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.24f;
            noise.frequency = 0.42f;
            noise.scrollSpeed = 0.28f;

            ParticleSystem.ColorOverLifetimeModule color =
                system.colorOverLifetime;
            color.enabled = true;
            color.color = EvilGradient(spellAccent, EvilViolet);

            ParticleSystem.SizeOverLifetimeModule size =
                system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.08f),
                    new Keyframe(0.24f, 1f),
                    new Keyframe(1f, 0f)));

            ConfigureRuntimeRenderer(
                system.GetComponent<ParticleSystemRenderer>(),
                streak: false, sortingOrder: 18);
            system.Play(true);
        }

        private static void CreateEvilEmbers(Transform parent,
            float castSeconds)
        {
            ParticleSystem system = CreateRuntimeParticleSystem(parent,
                SoulEmbersName);
            system.transform.localPosition = Vector3.up * 0.52f;

            ParticleSystem.MainModule main = system.main;
            main.duration = Mathf.Max(0.5f, castSeconds);
            main.loop = true;
            main.startLifetime =
                new ParticleSystem.MinMaxCurve(0.38f, 0.82f);
            main.startSpeed =
                new ParticleSystem.MinMaxCurve(0.12f, 0.48f);
            main.startSize =
                new ParticleSystem.MinMaxCurve(0.018f, 0.052f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                EvilCrimson, Brighten(EvilViolet, 1.25f));
            main.maxParticles = 48;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 14f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.72f;
            shape.radiusThickness = 0.18f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            ConfigureLinearVelocityY(velocity, 0.38f, 0.86f);

            ParticleSystem.ColorOverLifetimeModule color =
                system.colorOverLifetime;
            color.enabled = true;
            color.color = EvilGradient(EvilCrimson,
                Brighten(EvilViolet, 1.2f));

            ParticleSystem.TrailModule trails = system.trails;
            trails.enabled = true;
            trails.lifetime = 0.16f;
            trails.dieWithParticles = true;

            ConfigureRuntimeRenderer(
                system.GetComponent<ParticleSystemRenderer>(),
                streak: true, sortingOrder: 19);
            system.Play(true);
        }

        private static void CreateOrbitingSkulls(Transform parent,
            float castSeconds, Color spellAccent)
        {
            ParticleSystem system = CreateRuntimeParticleSystem(parent,
                OrbitingSkullsName);
            system.gameObject.AddComponent<
                NecromancerOrbitingSkullsMarker>();
            system.transform.localPosition = Vector3.up * 0.88f;

            ParticleSystem.MainModule main = system.main;
            main.duration = Mathf.Max(0.55f, castSeconds);
            main.loop = true;
            main.startLifetime =
                new ParticleSystem.MinMaxCurve(1.30f, 1.90f);
            main.startSpeed =
                new ParticleSystem.MinMaxCurve(0.015f, 0.065f);
            main.startSize =
                new ParticleSystem.MinMaxCurve(0.20f, 0.32f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Brighten(EvilViolet, 1.45f),
                Brighten(spellAccent, 1.28f));
            main.maxParticles = 24;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 3.8f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)5)
            });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.78f;
            shape.radiusThickness = 0.04f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.orbitalY = 2.65f;
            velocity.orbitalX = 0.18f;
            ConfigureLinearVelocityY(velocity, -0.08f, 0.12f);

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.08f;
            noise.frequency = 0.32f;
            noise.scrollSpeed = 0.18f;

            ParticleSystem.ColorOverLifetimeModule color =
                system.colorOverLifetime;
            color.enabled = true;
            color.color = EvilGradient(
                Brighten(EvilViolet, 1.22f), spellAccent);

            ParticleSystem.SizeOverLifetimeModule size =
                system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.04f),
                    new Keyframe(0.16f, 1f),
                    new Keyframe(0.78f, 0.92f),
                    new Keyframe(1f, 0f)));

            ParticleSystem.RotationOverLifetimeModule rotation =
                system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-1.6f, 1.6f);

            ParticleSystemRenderer renderer =
                system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = 20;
            renderer.sharedMaterial = _skullParticleMaterial;
            system.Play(true);
        }

        private static ParticleSystem CreateRuntimeParticleSystem(
            Transform parent, string name)
        {
            var layer = new GameObject(name);
            layer.transform.SetParent(parent, false);
            return layer.AddComponent<ParticleSystem>();
        }

        private static void ConfigureRuntimeRenderer(
            ParticleSystemRenderer renderer, bool streak, int sortingOrder)
        {
            renderer.renderMode = streak
                ? ParticleSystemRenderMode.Stretch
                : ParticleSystemRenderMode.Billboard;
            if (streak)
            {
                renderer.lengthScale = 0.42f;
                renderer.velocityScale = 0.16f;
            }
            renderer.sortingOrder = sortingOrder;
            renderer.sharedMaterial = _runtimeParticleMaterial;
            renderer.trailMaterial = _runtimeParticleMaterial;
        }

        private static ParticleSystem.MinMaxGradient EvilGradient(
            Color first, Color second)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Brighten(first, 1.15f), 0f),
                    new GradientColorKey(second, 0.48f),
                    new GradientColorKey(Darken(EvilViolet, 0.35f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.92f, 0.12f),
                    new GradientAlphaKey(0.68f, 0.68f),
                    new GradientAlphaKey(0f, 1f)
                });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        private static bool CreateDemonicSigil(Transform caster,
            NecromancerSpellDefinition definition, CastSpell source,
            Spell spell, float lifetime, bool followCast, bool resolveBurst)
        {
            if (caster == null) return false;
            if (followCast)
                RetireNamedChildren(caster, SummoningSigilObjectName);

            var root = new GameObject(SummoningSigilObjectName);
            if (followCast)
            {
                root.transform.SetParent(caster, false);
                root.transform.localPosition = Vector3.up * 0.045f;
            }
            else
            {
                root.transform.position = caster.position +
                    Vector3.up * 0.045f;
            }
            root.transform.rotation = Quaternion.identity;
            root.AddComponent<NecromancerSummonSigilMarker>();

            NecromancerImpactProfile profile = definition != null
                ? NecromancerImpactProfiles.Get(definition.Key)
                : null;
            Color outerColor = definition != null
                ? Brighten(definition.VfxColor, 1.08f)
                : EvilCrimson;
            Color innerColor = profile != null
                ? Brighten(profile.Secondary, 1.04f)
                : EvilViolet;
            float tierScale = definition != null
                ? 0.92f + Mathf.Clamp(definition.PetTier, 1, 5) * 0.065f
                : 1f;
            // Summoning is intentionally particle-only. Player-facing line
            // symbols obscured armor and read like UI icons pasted into the
            // world. The ritual now pulls grave energy inward, lifts fragments
            // from the ground, and erupts into darkness when the thrall arrives.
            CreateSummoningVortex(root.transform, lifetime, tierScale,
                outerColor, innerColor, resolveBurst);
            CreateSummoningFragments(root.transform, lifetime, tierScale,
                outerColor, innerColor, resolveBurst);
            if (resolveBurst)
                CreateDarknessEruption(root.transform);
            else
                CreateSigilParticles(root.transform, lifetime);
            var controller = root.AddComponent<NecromancerSigilVfx>();
            controller.Source = source;
            controller.Spell = spell;
            controller.Lifetime = Mathf.Max(0.65f, lifetime);
            controller.FollowCast = followCast;
            controller.ResolveBurst = resolveBurst;
            return true;
        }

        private static void CreateSummoningVortex(Transform parent,
            float lifetime, float tierScale, Color outerColor,
            Color innerColor, bool burst)
        {
            ParticleSystem system = CreateRuntimeParticleSystem(parent,
                burst ? "Thrall Arrival Vortex" : "Gathering Grave Vortex");
            system.transform.localPosition = Vector3.up * 0.035f;

            ParticleSystem.MainModule main = system.main;
            main.duration = burst ? 1.05f : Mathf.Max(0.55f, lifetime);
            main.loop = !burst;
            main.startLifetime = burst
                ? new ParticleSystem.MinMaxCurve(0.32f, 0.76f)
                : new ParticleSystem.MinMaxCurve(0.48f, 0.92f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.105f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Brighten(outerColor, 1.18f), Brighten(innerColor, 1.24f));
            main.maxParticles = burst ? 72 : 54;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.stopAction = ParticleSystemStopAction.None;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = burst ? 0f : 27f;
            emission.SetBursts(burst
                ? new[]
                {
                    new ParticleSystem.Burst(0f, (short)34),
                    new ParticleSystem.Burst(0.14f, (short)22)
                }
                : new[] { new ParticleSystem.Burst(0f, (short)14) });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 1.18f * tierScale;
            shape.radiusThickness = 0.20f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.03f, 0.16f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.radial = burst ? -1.55f : -0.78f;
            velocity.orbitalY = burst ? 4.8f : 2.65f;

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = burst ? 0.18f : 0.12f;
            noise.frequency = 0.46f;
            noise.scrollSpeed = 0.38f;

            ParticleSystem.ColorOverLifetimeModule color =
                system.colorOverLifetime;
            color.enabled = true;
            color.color = EvilGradient(outerColor, innerColor);

            ParticleSystem.SizeOverLifetimeModule size =
                system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.08f),
                    new Keyframe(0.18f, 1f), new Keyframe(0.78f, 0.72f),
                    new Keyframe(1f, 0f)));

            ParticleSystem.TrailModule trails = system.trails;
            trails.enabled = true;
            trails.lifetime = burst ? 0.22f : 0.16f;
            trails.dieWithParticles = true;
            ConfigureRuntimeRenderer(
                system.GetComponent<ParticleSystemRenderer>(), true, 18);
            system.Play(true);
        }

        private static void CreateSummoningFragments(Transform parent,
            float lifetime, float tierScale, Color outerColor,
            Color innerColor, bool burst)
        {
            ParticleSystem system = CreateRuntimeParticleSystem(parent,
                burst ? "Rising Thrall Fragments" : "Unearthed Bone Fragments");
            system.transform.localPosition = Vector3.up * 0.02f;

            ParticleSystem.MainModule main = system.main;
            main.duration = burst ? 1.15f : Mathf.Max(0.55f, lifetime);
            main.loop = !burst;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.52f, 1.10f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.20f, 0.64f);
            main.startSize = new ParticleSystem.MinMaxCurve(
                0.055f * tierScale, 0.15f * tierScale);
            main.startRotation = new ParticleSystem.MinMaxCurve(
                -Mathf.PI, Mathf.PI);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Color.Lerp(Color.white, outerColor, 0.42f),
                Color.Lerp(new Color(0.72f, 0.82f, 0.66f, 1f),
                    innerColor, 0.38f));
            main.gravityModifier = burst ? -0.10f : 0.04f;
            main.maxParticles = burst ? 58 : 42;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.stopAction = ParticleSystemStopAction.None;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = burst ? 0f : 12f;
            emission.SetBursts(burst
                ? new[]
                {
                    new ParticleSystem.Burst(0.04f, (short)25),
                    new ParticleSystem.Burst(0.22f, (short)16)
                }
                : new[] { new ParticleSystem.Burst(0f, (short)8) });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.88f * tierScale;
            shape.radiusThickness = 0.72f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            ConfigureLinearVelocityY(velocity,
                burst ? 1.15f : 0.46f, burst ? 2.65f : 1.18f);
            velocity.orbitalY = burst ? -2.6f : -1.25f;
            velocity.radial = burst ? -0.42f : -0.16f;

            ParticleSystem.RotationOverLifetimeModule rotation =
                system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-4.2f, 4.2f);

            ParticleSystem.ColorOverLifetimeModule color =
                system.colorOverLifetime;
            color.enabled = true;
            color.color = EvilGradient(
                Color.Lerp(Color.white, outerColor, 0.42f), innerColor);

            ParticleSystem.SizeOverLifetimeModule size =
                system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.12f),
                    new Keyframe(0.16f, 1f), new Keyframe(0.84f, 0.82f),
                    new Keyframe(1f, 0f)));

            ConfigureRuntimeRenderer(
                system.GetComponent<ParticleSystemRenderer>(), true, 19);
            system.Play(true);
        }

        private static Vector3[] BuildCircle(int segments, float radius)
        {
            var points = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                points[i] = new Vector3(Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius, 0f);
            }
            return points;
        }

        private static Vector3[] BuildGraveStar(float radius)
        {
            int[] order = { 0, 2, 4, 1, 3 };
            var points = new Vector3[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                float angle = (order[i] * 72f + 54f) * Mathf.Deg2Rad;
                points[i] = new Vector3(Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius, 0f);
            }
            return points;
        }

        private static void CreateSigilLine(Transform parent, string name,
            Vector3[] points, bool loop, Color color, float width)
        {
            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(parent, false);
            lineObject.transform.localRotation =
                Quaternion.Euler(90f, 0f, 0f);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = loop;
            line.alignment = LineAlignment.TransformZ;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.sharedMaterial = _sigilMaterial;
            line.receiveShadows = false;
        }

        /// <summary>
        /// Slow gravefire for the cast-time sigil. The summon burst does not
        /// use this — it is symbol plus CreateDarknessEruption only.
        /// </summary>
        private static void CreateSigilParticles(Transform parent,
            float lifetime)
        {
            ParticleSystem system = CreateRuntimeParticleSystem(parent,
                "Gravefire");
            system.transform.localPosition = Vector3.up * 0.04f;

            ParticleSystem.MainModule main = system.main;
            main.duration = Mathf.Max(0.5f, lifetime);
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.34f, 0.76f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.16f, 0.72f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                EvilCrimson, Brighten(EvilViolet, 1.28f));
            main.maxParticles = 58;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 20f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)10)
            });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.98f;
            shape.radiusThickness = 0.12f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            ConfigureLinearVelocityY(velocity, 0.24f, 0.72f);

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.22f;
            noise.frequency = 0.48f;
            noise.scrollSpeed = 0.32f;

            ParticleSystem.ColorOverLifetimeModule color =
                system.colorOverLifetime;
            color.enabled = true;
            color.color = EvilGradient(EvilCrimson,
                Brighten(EvilViolet, 1.2f));

            ParticleSystem.SizeOverLifetimeModule size =
                system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.1f),
                    new Keyframe(0.22f, 1f),
                    new Keyframe(1f, 0f)));

            ParticleSystem.TrailModule trails = system.trails;
            trails.enabled = true;
            trails.lifetime = 0.14f;
            trails.dieWithParticles = true;

            ConfigureRuntimeRenderer(
                system.GetComponent<ParticleSystemRenderer>(),
                streak: true, sortingOrder: 17);
            system.Play(true);
        }

        /// <summary>
        /// Column of darkness thrown up out of the summoning sigil. Deliberately
        /// unlit and low-saturation so it reads as absence of light against the
        /// crimson gravefire rather than as more embers.
        /// </summary>
        private static void CreateDarknessEruption(Transform parent)
        {
            ParticleSystem system = CreateRuntimeParticleSystem(parent,
                "Erupting Darkness");
            system.transform.localPosition = Vector3.up * 0.02f;

            ParticleSystem.MainModule main = system.main;
            main.duration = 1.1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.25f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.35f, 3.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.42f, 1.15f);
            main.startRotation =
                new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.03f, 0.005f, 0.06f, 1f),
                Darken(EvilViolet, 0.22f));
            main.gravityModifier = -0.06f;
            main.maxParticles = 90;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.Automatic;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)34),
                new ParticleSystem.Burst(0.12f, (short)22),
                new ParticleSystem.Burst(0.3f, (short)12)
            });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.62f;
            shape.radiusThickness = 1f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            ConfigureLinearVelocityY(velocity, 1.1f, 2.6f);

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.55f;
            noise.frequency = 0.3f;
            noise.scrollSpeed = 0.42f;
            noise.damping = true;

            // Hold near-full opacity through the climb, then collapse, so the
            // eruption swallows the sigil before releasing it.
            var darkness = new Gradient();
            darkness.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.05f, 0.01f, 0.08f), 0f),
                    new GradientColorKey(Darken(EvilViolet, 0.30f), 0.55f),
                    new GradientColorKey(new Color(0.02f, 0f, 0.04f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.95f, 0.16f),
                    new GradientAlphaKey(0.82f, 0.62f),
                    new GradientAlphaKey(0f, 1f)
                });
            ParticleSystem.ColorOverLifetimeModule color =
                system.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(darkness);

            ParticleSystem.SizeOverLifetimeModule size =
                system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.35f),
                    new Keyframe(0.34f, 1f),
                    new Keyframe(1f, 1.35f)));

            ParticleSystem.RotationOverLifetimeModule rotation =
                system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.9f, 0.9f);

            // Sits under the gravefire streaks so the crimson embers stay
            // legible on top of the dark column.
            ConfigureRuntimeRenderer(
                system.GetComponent<ParticleSystemRenderer>(),
                streak: false, sortingOrder: 16);
            system.Play(true);
        }

        private static void RetireNamedChildren(Transform parent, string name)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (!string.Equals(child.name, name,
                        StringComparison.Ordinal))
                    continue;
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        internal static bool ValidateSpellVfx(Spell spell, out string error)
        {
            error = null;
            List<GameObject> database = GameData.EffectDB?.SpellEffects;
            if (spell == null || database == null)
            {
                error = "spell/effect database unavailable";
                return false;
            }
            if (spell.SpellChargeFXIndex < 0 ||
                spell.SpellChargeFXIndex >= database.Count ||
                spell.SpellResolveFXIndex < 0 ||
                spell.SpellResolveFXIndex >= database.Count)
            {
                error = "effect index out of range";
                return false;
            }
            GameObject charge = database[spell.SpellChargeFXIndex];
            GameObject resolve = database[spell.SpellResolveFXIndex];
            if (!charge || !resolve ||
                charge.GetComponentInChildren<ParticleSystem>(true) == null ||
                resolve.GetComponentInChildren<ParticleSystem>(true) == null)
            {
                error = "particle system missing";
                return false;
            }
            if (charge.GetComponent<NecromancerEvilChargeMarker>() == null)
            {
                error = "evil caster charge marker missing";
                return false;
            }
            if (!NecromancerCatalog.TryGetDefinition(spell,
                    out NecromancerSpellDefinition definition))
            {
                error = "Necromancer spell definition missing";
                return false;
            }
            if (HasNativeFoundation(charge))
            {
                error = "native caster effect foundation is still present";
                return false;
            }
            ParticleSystem chargeParticles =
                charge.GetComponent<ParticleSystem>();
            ParticleSystemRenderer chargeRenderer = chargeParticles != null
                ? chargeParticles.GetComponent<ParticleSystemRenderer>() : null;
            if (chargeParticles == null || chargeRenderer == null ||
                chargeRenderer.enabled || chargeParticles.emission.enabled)
            {
                error = "native charge channel is not a silent placeholder";
                return false;
            }
            if (definition.Kind == NecromancerSpellKind.SummonPet)
            {
                // A summon authors no resolve burst on purpose. The sigil
                // raised under the thrall is the entire effect, so the resolve
                // prefab is a silent placeholder with no foundation, no accent
                // and no impact signature.
                if (charge.GetComponent<NecromancerSummonSigilMarker>() == null ||
                    resolve.GetComponent<NecromancerSummonSigilMarker>() == null)
                {
                    error = "demonic summon sigil marker missing";
                    return false;
                }
                if (resolve.GetComponent<NecromancerSilentEffect>() == null)
                {
                    error = "summon resolve prefab is not the silent placeholder";
                    return false;
                }
                return true;
            }
            if (HasNativeFoundation(resolve))
            {
                error = "native resolve effect foundation is still present";
                return false;
            }
            NecromancerImpactProfile profile =
                NecromancerImpactProfiles.Get(definition.Key);
            NecromancerImpactSignatureMarker impact =
                resolve.GetComponent<NecromancerImpactSignatureMarker>();
            if (profile == null || impact == null ||
                !string.Equals(impact.Key, definition.Key,
                    StringComparison.Ordinal) ||
                impact.SignatureId != profile.Id ||
                string.IsNullOrWhiteSpace(impact.Motif) ||
                !string.Equals(impact.Motif, profile.Motif,
                    StringComparison.Ordinal) ||
                !string.Equals(impact.VisualFingerprint,
                    profile.VisualFingerprint(definition.VfxColor),
                    StringComparison.Ordinal))
            {
                error = "unique semantic impact signature missing or invalid";
                return false;
            }
            if (resolve.transform.Find(
                    $"Unique Impact {profile.Id:00} - {profile.Motif}") == null)
            {
                error = "unique semantic impact particle layer missing";
                return false;
            }
            NecromancerMotifSignatureMarker motif =
                resolve.GetComponentInChildren<
                    NecromancerMotifSignatureMarker>(true);
            MotifRecipe recipe = MotifFor(definition.Key);
            if (motif == null ||
                !string.Equals(motif.Key, definition.Key,
                    StringComparison.Ordinal) ||
                !string.Equals(motif.GeometryFingerprint,
                    recipe.Fingerprint, StringComparison.Ordinal))
            {
                error = "authored crest geometry missing or invalid";
                return false;
            }
            return true;
        }

        private static bool HasNativeFoundation(GameObject root)
        {
            if (root == null) return false;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if ((child.name ?? "").StartsWith("Erenshor Foundation - ",
                        StringComparison.Ordinal))
                    return true;
            return false;
        }

        internal static bool PlayResolvePreview(Spell spell, Transform target,
            Vector3 offset, out string error)
        {
            error = null;
            if (target == null ||
                !NecromancerCatalog.TryGetDefinition(spell, out var definition) ||
                !Effects.TryGetValue(definition.Key, out Pair pair))
            {
                error = "spell, target, or registered effect is unavailable";
                return false;
            }
            try
            {
                ClearResolvePreview();
                // A summon's resolve prefab is a silent placeholder, so
                // instantiating it would preview nothing. Show the sigil that
                // actually plays under the thrall instead.
                if (definition.Kind == NecromancerSpellKind.SummonPet)
                {
                    if (PlaySummonResolve(definition, target)) return true;
                    error = "summon sigil preview failed";
                    return false;
                }
                GameObject preview = UnityEngine.Object.Instantiate(
                    pair.ResolvePrefab, target.position + offset,
                    Quaternion.identity);
                preview.name = "CS Necromancer VFX Preview - " + spell.SpellName;
                _activeResolvePreview = preview;
                NecromancerAudio.PlayResolve(definition,
                    preview.transform.position + Vector3.up * 0.72f);
                foreach (ParticleSystem particles in
                    preview.GetComponentsInChildren<ParticleSystem>(true))
                    particles.Play(true);
                UnityEngine.Object.Destroy(preview,
                    ResolveDuration(definition.VfxStyle) + 1.5f);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static void ClearResolvePreview()
        {
            if (_activeResolvePreview == null) return;
            foreach (ParticleSystem particles in _activeResolvePreview
                .GetComponentsInChildren<ParticleSystem>(true))
                particles.Stop(true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            _activeResolvePreview.SetActive(false);
            UnityEngine.Object.Destroy(_activeResolvePreview);
            _activeResolvePreview = null;
        }
    }

    internal sealed class NecromancerEvilChargeMarker : MonoBehaviour
    {
    }

    internal sealed class NecromancerEvilCastAuraMarker : MonoBehaviour
    {
    }

    internal sealed class NecromancerOrbitingSkullsMarker : MonoBehaviour
    {
    }

    internal sealed class NecromancerSummonSigilMarker : MonoBehaviour
    {
    }

    /// <summary>
    /// Retires the silent summon placeholder on its first frame. Erenshor's Pet
    /// resolve path instantiates the resolve prefab twice but only attaches its
    /// own DestroyObjectTimer to the second copy, so the first would otherwise
    /// sit in the scene for the rest of the session. Both copies are read and
    /// scaled during the Instantiate frame, before any Update runs.
    /// </summary>
    internal sealed class NecromancerSilentEffect : MonoBehaviour
    {
        private void Update()
        {
            // Instantiate copies this component, so the authored template
            // carries it too. Destroying the template would invalidate the
            // registered resolve prefab and every cast would then be refused
            // with "spell effects are still loading". Only clones may retire.
            if (NecromancerVfx.IsAuthoredTemplate(gameObject))
            {
                enabled = false;
                return;
            }
            Destroy(gameObject);
        }
    }

    internal sealed class NecromancerImpactSignatureMarker : MonoBehaviour
    {
        internal string Key;
        internal int SignatureId;
        internal string Motif;
        internal string VisualFingerprint;
    }

    internal sealed class NecromancerMotifSignatureMarker : MonoBehaviour
    {
        internal string Key;
        internal string GeometryFingerprint;
    }

    internal sealed class NecromancerPersistentPlacementMarker : MonoBehaviour
    {
        internal string Key;
        internal string Band;
        internal float Height;
    }

    internal sealed class NecromancerImpactMotifVfx : MonoBehaviour
    {
        internal float Lifetime;
        internal float Spin;
        internal float Pulse;
        internal float Rise;

        private LineRenderer[] _lines;
        private Color[] _startColors;
        private Color[] _endColors;
        private Light _light;
        private float _lightIntensity;
        private Vector3 _baseScale;
        private float _age;

        private void Start()
        {
            if (NecromancerVfx.IsAuthoredTemplate(transform.root.gameObject))
            {
                enabled = false;
                return;
            }
            _lines = GetComponentsInChildren<LineRenderer>(true);
            _startColors = new Color[_lines.Length];
            _endColors = new Color[_lines.Length];
            for (int index = 0; index < _lines.Length; index++)
            {
                _startColors[index] = _lines[index].startColor;
                _endColors[index] = _lines[index].endColor;
            }
            _light = GetComponent<Light>();
            _lightIntensity = _light != null ? _light.intensity : 0f;
            _baseScale = transform.localScale;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_age >= Lifetime)
            {
                Destroy(gameObject);
                return;
            }
            float normalized = Mathf.Clamp01(_age / Lifetime);
            float fade = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.58f, 1f, normalized));
            float arrival = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(normalized / 0.16f));
            float pulse = 1f + Mathf.Sin(_age * Pulse * Mathf.PI * 2f) *
                0.055f * fade;
            transform.localScale = _baseScale * arrival * pulse;
            transform.Rotate(0f, Spin * Time.deltaTime, 0f, Space.Self);
            transform.localPosition += Vector3.up * Rise * Time.deltaTime;

            if (_light != null)
                _light.intensity = _lightIntensity * fade *
                    (0.86f + pulse * 0.14f);
            if (_lines == null) return;
            for (int index = 0; index < _lines.Length; index++)
            {
                LineRenderer line = _lines[index];
                if (line == null) continue;
                Color start = _startColors[index];
                Color end = _endColors[index];
                start.a *= fade;
                end.a *= fade;
                line.startColor = start;
                line.endColor = end;
            }
        }
    }

    internal sealed class NecromancerCastAuraVfx : MonoBehaviour
    {
        internal CastSpell Source;
        internal Spell Spell;
        internal float Lifetime;

        private bool _ending;

        private void Update()
        {
            if (_ending) return;
            Lifetime -= Time.deltaTime;
            if (Lifetime > 0f && Source != null && Spell != null &&
                ReferenceEquals(Source.GetCurrentCast(), Spell))
                return;

            _ending = true;
            foreach (ParticleSystem particles in
                GetComponentsInChildren<ParticleSystem>(true))
                particles.Stop(true,
                    ParticleSystemStopBehavior.StopEmitting);
            Destroy(gameObject, 0.68f);
        }
    }

    internal sealed class NecromancerSigilVfx : MonoBehaviour
    {
        internal CastSpell Source;
        internal Spell Spell;
        internal float Lifetime;
        internal bool FollowCast;
        internal bool ResolveBurst;

        // ~30Hz is well above the 7.5–12Hz pulse, so the animation still reads
        // as smooth while costing roughly half the mesh rebuilds at 60fps and
        // proportionally fewer above that.
        private const float LineRefreshInterval = 1f / 30f;
        private const float LineEpsilon = 0.004f;

        private LineRenderer[] _lines;
        private Color[] _colors;
        private float[] _widths;
        private float _age;
        private bool _ending;
        private float _fadeRemaining;
        private float _sinceLineRefresh;
        private float _lastAlpha;
        private float _lastWidthScale;
        private bool _lineStateValid;

        private void Start()
        {
            _lines = GetComponentsInChildren<LineRenderer>(true);
            _colors = new Color[_lines.Length];
            _widths = new float[_lines.Length];
            for (int i = 0; i < _lines.Length; i++)
            {
                _colors[i] = _lines[i].startColor;
                _widths[i] = _lines[i].startWidth;
            }
        }

        private void Update()
        {
            float delta = Time.deltaTime;
            _age += delta;
            Lifetime -= delta;
            if (!_ending &&
                (Lifetime <= 0f ||
                 (FollowCast && (Source == null || Spell == null ||
                  !ReferenceEquals(Source.GetCurrentCast(), Spell)))))
                BeginFade();

            float fade = 1f;
            if (_ending)
            {
                _fadeRemaining -= delta;
                fade = Mathf.Clamp01(_fadeRemaining / 0.58f);
                if (_fadeRemaining <= 0f)
                {
                    Destroy(gameObject);
                    return;
                }
            }

            float pulse = 0.82f +
                Mathf.Sin(_age * (ResolveBurst ? 12f : 7.5f)) * 0.18f;
            float arrival = ResolveBurst
                ? Mathf.SmoothStep(0.62f, 1.12f,
                    Mathf.Clamp01(_age / 0.24f))
                : 1f;
            transform.localScale = Vector3.one * arrival *
                (0.96f + pulse * 0.06f);
            transform.Rotate(0f, (ResolveBurst ? 92f : 28f) * delta, 0f,
                Space.Self);

            if (_lines == null) return;

            // Writing startColor/endColor/startWidth/endWidth invalidates a
            // LineRenderer's baked mesh. The sigil has eight of them, and the
            // outer ring alone is a 64-segment loop with corner and cap
            // vertices, so doing this every frame rebuilds a lot of geometry
            // for a pulse the eye reads at ~8Hz. Refresh on a fixed cadence
            // and skip writes the pulse has not meaningfully moved.
            _sinceLineRefresh += delta;
            float alpha = fade * (0.72f + pulse * 0.28f);
            float widthScale = 0.90f + pulse * 0.16f;
            bool forced = _ending || !_lineStateValid;
            if (!forced && _sinceLineRefresh < LineRefreshInterval) return;
            if (!forced &&
                Mathf.Abs(alpha - _lastAlpha) < LineEpsilon &&
                Mathf.Abs(widthScale - _lastWidthScale) < LineEpsilon) return;

            _sinceLineRefresh = 0f;
            _lastAlpha = alpha;
            _lastWidthScale = widthScale;
            _lineStateValid = true;

            for (int i = 0; i < _lines.Length; i++)
            {
                if (_lines[i] == null) continue;
                Color color = _colors[i];
                color.a *= alpha;
                _lines[i].startColor = color;
                _lines[i].endColor = color;
                float width = _widths[i] * widthScale;
                _lines[i].startWidth = width;
                _lines[i].endWidth = width;
            }
        }

        private void BeginFade()
        {
            _ending = true;
            _fadeRemaining = 0.58f;
            foreach (ParticleSystem particles in
                GetComponentsInChildren<ParticleSystem>(true))
                particles.Stop(true,
                    ParticleSystemStopBehavior.StopEmitting);
        }
    }

    internal sealed class NecromancerProjectile : MonoBehaviour
    {
        internal Transform Target;
        internal float Speed;
        internal float Lifetime;

        /// <summary>
        /// Caster to send the siphoned-life orb back to once this bolt lands.
        /// Null on the returning orb itself, which is what stops it recursing.
        /// </summary>
        internal Transform ReturnTo;

        private bool _finished;

        private void Update()
        {
            Lifetime -= Time.deltaTime;
            // A lifetap that kills its target loses Target the same frame it
            // connects, so end on any of the three outcomes and still pay the
            // life back — that is precisely the kill the player wants to see.
            if (Lifetime <= 0f || Target == null)
            {
                Finish();
                return;
            }
            Vector3 destination = Target.position + Vector3.up * 0.85f;
            transform.position = Vector3.MoveTowards(transform.position,
                destination, Speed * Time.deltaTime);
            Vector3 direction = destination - transform.position;
            if (direction.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(direction.normalized);
            if (direction.sqrMagnitude < 0.04f) Finish();
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            if (ReturnTo != null)
                NecromancerVfx.PlayLifetapReturn(transform.position, ReturnTo);
            Destroy(gameObject);
        }
    }

    internal sealed class NecromancerTimedVfx : MonoBehaviour
    {
        internal float Lifetime;

        private void Update()
        {
            Lifetime -= Time.deltaTime;
            if (Lifetime <= 0f) Destroy(gameObject);
        }
    }
}
