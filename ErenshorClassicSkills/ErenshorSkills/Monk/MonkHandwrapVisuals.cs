using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ErenshorSkills.Monk
{
    /// <summary>
    /// Handwrap weapons intentionally leave Erenshor's held-weapon roots empty
    /// so punches remain unobstructed. The active humanoid glove and wrist
    /// renderers provide the visible fitted surfaces. The addon references
    /// those meshes at runtime, preserving their original skinning and finger
    /// deformation while applying its own materials;
    /// no native mesh data is embedded in the distributed assembly.
    /// </summary>
    internal static class MonkHandwrapVisuals
    {
        private static int _qaPreviewTier = -1;

        internal static void SetQaPreviewTier(int tier)
        {
            _qaPreviewTier = SkillsPlugin.CfgEnableTestMode?.Value == true
                ? tier : -1;
            Tick();
        }

        internal static void Tick()
        {
            MonkGear.ReconcileLegacyOffhand();
            Animator animator = ResolvePlayerAnimator();
            if (animator == null) return;

            Item main = GameData.PlayerInv?.MH?.MyItem;
            Item off = GameData.PlayerInv?.OH?.MyItem;
            int tier = MonkGear.GetHandwrapTier(main);
            if (tier < 0) tier = MonkGear.GetHandwrapTier(off);
            if (_qaPreviewTier >= 0) tier = _qaPreviewTier;
            bool show = tier >= 0;
            MonkHandwrapVisualDriver driver =
                animator.GetComponent<MonkHandwrapVisualDriver>();
            if (driver == null && show)
                driver = animator.gameObject.AddComponent<
                    MonkHandwrapVisualDriver>();
            if (driver == null) return;

            driver.SetLoadout(show, tier);
        }

        internal static bool ValidateEquippedVisual(Item.SlotType slot,
            out string detail)
        {
            Tick();
            Animator animator = ResolvePlayerAnimator();
            MonkHandwrapVisualDriver driver =
                animator?.GetComponent<MonkHandwrapVisualDriver>();
            bool supported = slot == Item.SlotType.Primary;
            if (driver == null || !supported)
            {
                detail = "handwrap overlay driver or weapon hand is unavailable";
                return false;
            }
            Item equipped = GameData.PlayerInv?.MH?.MyItem;
            return driver.ValidatePair(MonkGear.GetHandwrapTier(equipped),
                out detail);
        }

        internal static bool TryGetQaHandFrame(bool left,
            out Transform frame)
        {
            frame = null;
            Animator animator = ResolvePlayerAnimator();
            MonkHandwrapVisualDriver driver =
                animator?.GetComponent<MonkHandwrapVisualDriver>();
            return driver != null && driver.TryGetQaHandFrame(left,
                out frame);
        }

        internal static bool ValidateQaPreview(int tier, out string detail)
        {
            Tick();
            Animator animator = ResolvePlayerAnimator();
            MonkHandwrapVisualDriver driver =
                animator?.GetComponent<MonkHandwrapVisualDriver>();
            if (driver == null)
            {
                detail = "handwrap overlay driver is unavailable";
                return false;
            }
            return driver.ValidatePair(tier, out detail);
        }

        internal static bool ValidateQaSourceCoverage(out string detail)
        {
            Animator animator = ResolvePlayerAnimator();
            if (animator == null)
            {
                detail = "player animator unavailable";
                return false;
            }
            SkinnedMeshRenderer[] sources = animator
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => renderer != null &&
                    string.Equals(renderer.name, "LeatherHalfGlove",
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            int valid = sources.Count(renderer => renderer.sharedMesh != null &&
                renderer.sharedMesh.vertexCount > 0 && renderer.bones != null &&
                renderer.bones.Length >= 6);
            bool profiles = MonkHandwrapArt.ValidateProfiles(
                out string profileDetail);
            detail = $"fingerless glove sources={valid}/{sources.Length}; " +
                profileDetail;
            return profiles && valid >= 2;
        }

        private static Animator ResolvePlayerAnimator()
        {
            PlayerControl player = GameData.PlayerControl;
            if (player == null) return null;
            Animator animator = player.Myself?.GetMyAnim();
            if (animator != null) return animator;
            animator = player.GetComponent<Animator>();
            return animator != null
                ? animator : player.GetComponentInChildren<Animator>();
        }
    }

    internal sealed class MonkHandwrapVisualDriver : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ClothColorId =
            Shader.PropertyToID("_Color_Cloth");
        private static readonly int LeatherColorId =
            Shader.PropertyToID("_Color_Leather");
        private static readonly int PrimaryColorId =
            Shader.PropertyToID("_Color_Primary");
        private static readonly int SecondaryColorId =
            Shader.PropertyToID("_Color_Secondary");
        private static readonly int EmissionColorId =
            Shader.PropertyToID("_EmissionColor");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");
        private static readonly int GlossinessId =
            Shader.PropertyToID("_Glossiness");

        private static readonly Color[] WristColors =
        {
            new Color(0.72f, 0.58f, 0.36f, 1f),
            new Color(0.34f, 0.18f, 0.07f, 1f),
            new Color(0.04f, 0.28f, 0.16f, 1f),
            new Color(0.42f, 0.055f, 0.025f, 1f),
            new Color(0.12f, 0.13f, 0.15f, 1f),
            new Color(0.12f, 0.12f, 0.34f, 1f),
            new Color(0.025f, 0.30f, 0.43f, 1f),
            new Color(0.055f, 0.050f, 0.060f, 1f),
            new Color(0.38f, 0.025f, 0.035f, 1f),
            new Color(0.035f, 0.045f, 0.040f, 1f),
            new Color(0.035f, 0.025f, 0.095f, 1f),
            new Color(0.16f, 0.055f, 0.025f, 1f),
            new Color(0.34f, 0.29f, 0.20f, 1f),
            new Color(0.18f, 0.15f, 0.12f, 1f),
            new Color(0.48f, 0.31f, 0.08f, 1f),
            new Color(0.075f, 0.025f, 0.13f, 1f),
            new Color(0.58f, 0.48f, 0.33f, 1f),
            new Color(0.72f, 0.72f, 0.62f, 1f),
            new Color(0.12f, 0.25f, 0.10f, 1f),
            new Color(0.30f, 0.17f, 0.07f, 1f),
            new Color(0.40f, 0.56f, 0.68f, 1f),
            new Color(0.035f, 0.10f, 0.25f, 1f)
        };
        private static readonly Color[] HandColors =
        {
            new Color(0.31f, 0.19f, 0.09f, 1f),
            new Color(0.72f, 0.34f, 0.10f, 1f),
            new Color(0.16f, 0.66f, 0.38f, 1f),
            new Color(0.94f, 0.22f, 0.035f, 1f),
            new Color(0.42f, 0.46f, 0.52f, 1f),
            new Color(0.64f, 0.66f, 0.92f, 1f),
            new Color(0.20f, 0.84f, 0.94f, 1f),
            new Color(0.88f, 0.82f, 0.66f, 1f),
            new Color(0.91f, 0.72f, 0.56f, 1f),
            new Color(0.22f, 0.28f, 0.23f, 1f),
            new Color(0.65f, 0.69f, 0.77f, 1f),
            new Color(0.92f, 0.19f, 0.035f, 1f),
            new Color(0.82f, 0.77f, 0.64f, 1f),
            new Color(0.50f, 0.36f, 0.18f, 1f),
            new Color(0.16f, 0.66f, 0.96f, 1f),
            new Color(0.73f, 0.35f, 0.96f, 1f),
            new Color(0.20f, 0.12f, 0.055f, 1f),
            new Color(0.12f, 0.55f, 0.42f, 1f),
            new Color(0.54f, 0.72f, 0.13f, 1f),
            new Color(0.57f, 0.29f, 0.08f, 1f),
            new Color(0.82f, 0.94f, 1f, 1f),
            new Color(0.76f, 0.68f, 0.32f, 1f)
        };
        private static readonly string[] VariantNames =
        {
            "matte linen Novice", "copper-ringed traveler",
            "jade-scaled Serpent", "ember-marked Emberfist",
            "steel-plated Iron Temple", "silver-crescent Moonweave",
            "luminous teal Tranquil Storm", "golden Sun-and-Moon grandmaster",
            "crimson-and-ivory Priel trophy", "black-chain Darkwarden",
            "midnight-silver Nightmistress", "charred Firebone cinder",
            "ivory-sinew Boneweaver", "bronze Cryptkeeper burial",
            "sand-gold Braxonian tempest", "violet Planar champion",
            "stitched reinforced linen", "pearl-and-jade Silkstep",
            "thorned Blighthide", "riveted Ironbark",
            "ice-capped Frostweave", "star-guarded Celestial silk"
        };

        private Animator _animator;
        private GameObject _rightOverlay;
        private GameObject _leftOverlay;
        private Transform _rightQaFrame;
        private Transform _leftQaFrame;
        private readonly List<GameObject> _rightAccents =
            new List<GameObject>();
        private readonly List<GameObject> _leftAccents =
            new List<GameObject>();
        private int _rightTier = -1;
        private int _leftTier = -1;
        private bool _rightPaletteSet;
        private bool _leftPaletteSet;
        private float _nextResolveAt;
        private readonly List<Material> _ownedMaterials =
            new List<Material>();
        private readonly List<Mesh> _ownedMeshes = new List<Mesh>();
        private readonly Dictionary<AccentShape, Mesh> _accentMeshes =
            new Dictionary<AccentShape, Mesh>();
        private Material _surfaceMaterial;
        private Material _accentClothMaterial;
        private Material _accentMetalMaterial;
        private Material _accentGlowMaterial;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        internal void SetLoadout(bool show, int tier)
        {
            if (_animator == null) _animator = GetComponent<Animator>();
            if (_animator == null) return;

            if (show && (_rightOverlay == null || _leftOverlay == null))
            {
                if (Time.unscaledTime < _nextResolveAt) return;
                _nextResolveAt = Time.unscaledTime + 0.5f;
                if (_rightOverlay == null)
                    _rightOverlay = CreateOverlay(
                        HumanBodyBones.RightHand, "Right", out _rightQaFrame);
                if (_leftOverlay == null)
                    _leftOverlay = CreateOverlay(
                        HumanBodyBones.LeftHand, "Left", out _leftQaFrame);
            }

            UpdateOverlay(_rightOverlay, show, tier, _rightAccents,
                HumanBodyBones.RightHand, ref _rightTier,
                ref _rightPaletteSet);
            UpdateOverlay(_leftOverlay, show, tier, _leftAccents,
                HumanBodyBones.LeftHand, ref _leftTier,
                ref _leftPaletteSet);
        }

        internal bool ValidatePair(int expectedTier, out string detail)
        {
            bool right = ValidateSide(_rightOverlay, _rightPaletteSet,
                _rightTier, _rightAccents, expectedTier);
            bool left = ValidateSide(_leftOverlay, _leftPaletteSet,
                _leftTier, _leftAccents, expectedTier);
            bool rightSurface = MonkHandwrapArt.ValidateSurface(_rightOverlay,
                expectedTier, out string rightSurfaceDetail);
            bool leftSurface = MonkHandwrapArt.ValidateSurface(_leftOverlay,
                expectedTier, out string leftSurfaceDetail);
            string variant = expectedTier >= 0 && expectedTier < VariantNames.Length
                ? VariantNames[expectedTier] : "unknown";
            detail = right && left && rightSurface && leftSurface
                ? $"matched {variant} wrist-and-hand wraps active on both hands " +
                  $"(family={MonkHandwrapArt.GetProfile(expectedTier).Family}, " +
                  $"right accents={_rightAccents.Count}, left accents={_leftAccents.Count})"
                : $"matched {variant} wraps failed: right={right}; left={left}; " +
                  $"right surface=[{rightSurfaceDetail}]; " +
                  $"left surface=[{leftSurfaceDetail}]";
            return right && left && rightSurface && leftSurface;
        }

        internal bool TryGetQaHandFrame(bool left, out Transform frame)
        {
            frame = left ? _leftQaFrame : _rightQaFrame;
            return frame != null && frame.gameObject.activeInHierarchy;
        }

        private static bool ValidateSide(GameObject overlay, bool paletteSet,
            int actualTier, List<GameObject> accents, int expectedTier)
        {
            Transform handTop = overlay?.transform.Find("CS_HandTopWrap");
            SkinnedMeshRenderer fitted =
                handTop?.GetComponent<SkinnedMeshRenderer>();
            return overlay != null && overlay.activeInHierarchy && paletteSet &&
                actualTier == expectedTier &&
                fitted != null && fitted.enabled && fitted.sharedMesh != null &&
                fitted.bones != null && fitted.bones.Length >= 6 &&
                overlay.GetComponentsInChildren<Renderer>(true)
                    .Any(renderer => renderer != null && renderer.enabled) &&
                accents.Count == 0;
        }

        private GameObject CreateOverlay(HumanBodyBones handBone, string side,
            out Transform qaFrame)
        {
            qaFrame = null;
            Transform hand = _animator.GetBoneTransform(handBone);
            if (hand == null) return null;

            Transform wristSource = FindSkinnedSource("Palmwraps", hand);
            Transform handSource = FindSkinnedSource("LeatherHalfGlove", hand);
            if (wristSource == null || handSource == null)
            {
                SkillsPlugin.Log?.LogWarning(
                    $"Monk handwrap visual: active {side.ToLowerInvariant()} " +
                    "wrist or hand-top mesh was not found on the player rig.");
                return null;
            }

            GameObject overlay = new GameObject(
                "CS_MonkHandwrapOverlay_" + side);
            overlay.hideFlags = HideFlags.HideAndDontSave;
            // Keep the renderer container at the character root. Parenting a
            // skinned renderer to the hand would apply hand motion twice: once
            // through its bone matrices and once through its transform parent.
            overlay.transform.SetParent(_animator.transform, false);
            GameObject wristSurface = CloneSkinnedPart(wristSource,
                overlay.transform, "CS_WristWrap");
            GameObject handSurface = CloneSkinnedPart(handSource,
                overlay.transform, "CS_HandTopWrap");
            ConfigureSkinnedSurface(wristSurface);
            ConfigureSkinnedSurface(handSurface);
            if (!IsValidHandSurface(handSurface, hand) ||
                wristSurface?.GetComponent<SkinnedMeshRenderer>() == null)
            {
                SkillsPlugin.Log?.LogWarning(
                    $"Monk handwrap visual: native {side.ToLowerInvariant()} " +
                    "glove surface could not be bound to the live hand rig.");
                UnityEngine.Object.Destroy(overlay);
                return null;
            }
            MonkHandwrapArt.InitializeSurface(handSurface);
            MonkHandwrapArt.InitializeSurface(wristSurface);
            qaFrame = CreateQaFrame(handSurface, hand);
            if (_surfaceMaterial == null)
                _surfaceMaterial = MonkHandwrapArt.CreateSurfaceMaterial(
                    _ownedMaterials);
            foreach (Collider collider in
                overlay.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.Destroy(collider);
            overlay.SetActive(false);
            SkillsPlugin.Log?.LogInfo(
                $"Monk handwrap visual: reused fitted {side.ToLowerInvariant()} " +
                $"skinned wrist wrap and articulated glove from " +
                $"'{BuildPath(wristSource)}' and '{BuildPath(handSource)}'.");
            return overlay;
        }

        private static void ConfigureSkinnedSurface(GameObject surface)
        {
            SkinnedMeshRenderer renderer =
                surface?.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null) return;
            renderer.enabled = true;
            renderer.updateWhenOffscreen = true;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        private Transform CreateQaFrame(GameObject surface, Transform hand)
        {
            Renderer renderer = surface?.GetComponent<Renderer>();
            if (renderer == null) return null;
            var frame = new GameObject("CS_HandQaFrame");
            frame.hideFlags = HideFlags.HideAndDontSave;
            frame.transform.position = renderer.bounds.center;
            frame.transform.rotation = Quaternion.LookRotation(
                _animator.transform.forward, _animator.transform.up);
            frame.transform.SetParent(hand, true);
            return frame.transform;
        }

        private static bool IsValidHandSurface(GameObject surface,
            Transform hand)
        {
            SkinnedMeshRenderer renderer =
                surface?.GetComponent<SkinnedMeshRenderer>();
            return renderer != null && renderer.sharedMesh != null &&
                renderer.bones != null && renderer.bones.Length >= 6 &&
                renderer.bones.Any(bone => bone == hand) &&
                renderer.sharedMesh.vertexCount > 0;
        }

        private Transform FindSkinnedSource(string name, Transform hand)
        {
            return _animator.GetComponentsInChildren<Transform>(true)
                .Where(candidate => candidate != null &&
                    string.Equals(candidate.name, name,
                        StringComparison.OrdinalIgnoreCase) &&
                    candidate.parent != null &&
                    candidate.parent.gameObject.activeInHierarchy)
                .FirstOrDefault(candidate =>
                {
                    SkinnedMeshRenderer renderer =
                        candidate.GetComponent<SkinnedMeshRenderer>();
                    return renderer != null && renderer.bones != null &&
                        renderer.bones.Any(bone => bone == hand);
                });
        }

        private static GameObject CloneSkinnedPart(Transform source,
            Transform overlay, string name)
        {
            GameObject clone = UnityEngine.Object.Instantiate(
                source.gameObject, source.parent, false);
            clone.name = name;
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.transform.SetParent(overlay, true);
            clone.SetActive(true);
            return clone;
        }

        private void UpdateOverlay(GameObject overlay, bool show, int tier,
            List<GameObject> accents, HumanBodyBones handBone,
            ref int previousTier, ref bool paletteSet)
        {
            if (overlay == null) return;
            // Skinned renderer world bounds are authoritative for fitting the
            // attachments, so let Unity update the isolated overlay first.
            if (show && !overlay.activeSelf) overlay.SetActive(true);
            if (!paletteSet || previousTier != tier)
            {
                ApplyPalette(overlay, tier);
                RemoveAccents(accents);
                previousTier = tier;
                paletteSet = true;
            }
            if (overlay.activeSelf != show) overlay.SetActive(show);
            foreach (GameObject accent in accents)
                if (accent != null && accent.activeSelf != show)
                    accent.SetActive(show);
        }

        private static void RemoveAccents(List<GameObject> output)
        {
            foreach (GameObject old in output)
                if (old != null) UnityEngine.Object.Destroy(old);
            output.Clear();
        }

        private enum AccentShape
        {
            Stud,
            FacetedGem,
            TaperedPlate,
            Wedge,
            Cord,
            Ring,
            Bone,
            Star
        }

        private static AccentShape AccentMeshShape(string name)
        {
            if (name.IndexOf("Ring", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Link", StringComparison.OrdinalIgnoreCase) >= 0)
                return AccentShape.Ring;
            if (name.IndexOf("Star", StringComparison.OrdinalIgnoreCase) >= 0)
                return AccentShape.Star;
            if (name.IndexOf("Bone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Ivory", StringComparison.OrdinalIgnoreCase) >= 0)
                return AccentShape.Bone;
            if (name.IndexOf("Crystal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Gem", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Pearl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Opal", StringComparison.OrdinalIgnoreCase) >= 0)
                return AccentShape.FacetedGem;
            if (name.IndexOf("Claw", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Thorn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Scale", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Shard", StringComparison.OrdinalIgnoreCase) >= 0)
                return AccentShape.Wedge;
            if (name.IndexOf("Band", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Thread", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Wire", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Braid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Stitch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Sinew", StringComparison.OrdinalIgnoreCase) >= 0)
                return AccentShape.Cord;
            if (name.IndexOf("Plate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Guard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Brace", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Seal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Crescent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Ridge", StringComparison.OrdinalIgnoreCase) >= 0)
                return AccentShape.TaperedPlate;
            return AccentShape.Stud;
        }

        private GameObject CreateMeshPart(string name, Mesh mesh, Color color,
            bool metal, bool luminous)
        {
            var part = new GameObject(name);
            part.hideFlags = HideFlags.HideAndDontSave;
            MeshFilter filter = part.AddComponent<MeshFilter>();
            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = GetAccentMaterial(metal, luminous);
            var block = new MaterialPropertyBlock();
            block.SetColor(ColorId, color);
            block.SetColor(EmissionColorId,
                luminous ? color * 1.10f : Color.black);
            block.SetFloat(MetallicId, metal ? 0.72f : 0.03f);
            block.SetFloat(GlossinessId, metal ? 0.48f : 0.18f);
            renderer.SetPropertyBlock(block);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return part;
        }

        private Material GetAccentMaterial(bool metal, bool luminous)
        {
            Material cached = luminous ? _accentGlowMaterial :
                metal ? _accentMetalMaterial : _accentClothMaterial;
            if (cached != null) return cached;
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            var material = new Material(shader)
            {
                name = luminous ? "CS Handwrap Shared Glow Accent" :
                    metal ? "CS Handwrap Shared Metal Accent" :
                    "CS Handwrap Shared Organic Accent",
                color = Color.white,
                hideFlags = HideFlags.HideAndDontSave
            };
            SetColor(material, EmissionColorId,
                luminous ? Color.white : Color.black);
            SetFloat(material, MetallicId, metal ? 0.68f : 0.03f);
            SetFloat(material, GlossinessId, metal ? 0.52f : 0.20f);
            if (luminous) material.EnableKeyword("_EMISSION");
            _ownedMaterials.Add(material);
            if (luminous) _accentGlowMaterial = material;
            else if (metal) _accentMetalMaterial = material;
            else _accentClothMaterial = material;
            return material;
        }

        private Mesh CreateAccentMesh(AccentShape shape)
        {
            if (_accentMeshes.TryGetValue(shape, out Mesh cached))
                return cached;
            Mesh mesh;
            switch (shape)
            {
                case AccentShape.FacetedGem: mesh = CreateFacetedGemMesh(); break;
                case AccentShape.Wedge: mesh = CreateWedgeMesh(); break;
                case AccentShape.TaperedPlate:
                    mesh = CreateTaperedPanelMesh(
                        0.50f, 0.34f, 0.50f, 0.16f); break;
                case AccentShape.Cord:
                    mesh = CreateTaperedPanelMesh(
                        0.50f, 0.48f, 0.50f, 0.10f); break;
                case AccentShape.Ring: mesh = CreateRingMesh(); break;
                case AccentShape.Bone: mesh = CreateBoneMesh(); break;
                case AccentShape.Star: mesh = CreateStarMesh(); break;
                default: mesh = CreateHexStudMesh(); break;
            }
            _accentMeshes[shape] = mesh;
            _ownedMeshes.Add(mesh);
            return mesh;
        }

        private static Mesh CreateRingMesh()
        {
            const int segments = 10;
            const int tube = 4;
            var vertices = new Vector3[segments * tube];
            var triangles = new int[segments * tube * 6];
            for (int major = 0; major < segments; major++)
            {
                float a = major * Mathf.PI * 2f / segments;
                for (int minor = 0; minor < tube; minor++)
                {
                    float b = minor * Mathf.PI * 2f / tube;
                    float radius = 0.34f + Mathf.Cos(b) * 0.13f;
                    vertices[major * tube + minor] = new Vector3(
                        Mathf.Cos(a) * radius, Mathf.Sin(a) * radius,
                        Mathf.Sin(b) * 0.13f);
                    int nextMajor = (major + 1) % segments;
                    int nextMinor = (minor + 1) % tube;
                    int t = (major * tube + minor) * 6;
                    triangles[t] = major * tube + minor;
                    triangles[t + 1] = nextMajor * tube + minor;
                    triangles[t + 2] = nextMajor * tube + nextMinor;
                    triangles[t + 3] = major * tube + minor;
                    triangles[t + 4] = nextMajor * tube + nextMinor;
                    triangles[t + 5] = major * tube + nextMinor;
                }
            }
            return BuildMesh("CS Forged Ring", vertices, triangles);
        }

        private static Mesh CreateBoneMesh()
        {
            Vector2[] outline =
            {
                new Vector2(-0.50f, -0.22f), new Vector2(-0.34f, -0.38f),
                new Vector2(-0.18f, -0.23f), new Vector2(0.18f, -0.23f),
                new Vector2(0.34f, -0.38f), new Vector2(0.50f, -0.22f),
                new Vector2(0.34f, 0f), new Vector2(0.50f, 0.22f),
                new Vector2(0.34f, 0.38f), new Vector2(0.18f, 0.23f),
                new Vector2(-0.18f, 0.23f), new Vector2(-0.34f, 0.38f),
                new Vector2(-0.50f, 0.22f), new Vector2(-0.34f, 0f)
            };
            return ExtrudeOutline(outline, 0.16f);
        }

        private static Mesh CreateStarMesh()
        {
            var outline = new Vector2[16];
            for (int index = 0; index < outline.Length; index++)
            {
                float angle = index * Mathf.PI * 2f / outline.Length;
                float radius = index % 2 == 0 ? 0.50f : 0.20f;
                outline[index] = new Vector2(Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius);
            }
            return ExtrudeOutline(outline, 0.10f);
        }

        private static Mesh CreateTaperedPanelMesh(float lowerHalfWidth,
            float upperHalfWidth, float halfHeight, float corner)
        {
            float c = Mathf.Min(corner, Mathf.Min(lowerHalfWidth, upperHalfWidth));
            Vector2[] outline =
            {
                new Vector2(-lowerHalfWidth + c, -halfHeight),
                new Vector2(lowerHalfWidth - c, -halfHeight),
                new Vector2(lowerHalfWidth, -halfHeight + c),
                new Vector2(upperHalfWidth, halfHeight - c),
                new Vector2(upperHalfWidth - c, halfHeight),
                new Vector2(-upperHalfWidth + c, halfHeight),
                new Vector2(-upperHalfWidth, halfHeight - c),
                new Vector2(-lowerHalfWidth, -halfHeight + c)
            };
            return ExtrudeOutline(outline, 0.5f);
        }

        private static Mesh CreateHexStudMesh()
        {
            var outline = new Vector2[6];
            for (int i = 0; i < outline.Length; i++)
            {
                float angle = Mathf.PI * 2f * i / outline.Length + Mathf.PI / 6f;
                outline[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.5f;
            }
            return ExtrudeOutline(outline, 0.38f);
        }

        private static Mesh CreateFacetedGemMesh()
        {
            Vector3[] vertices =
            {
                new Vector3(0f, 0f, 0.58f), new Vector3(0f, 0.58f, 0f),
                new Vector3(0.48f, 0f, 0f), new Vector3(0f, -0.58f, 0f),
                new Vector3(-0.48f, 0f, 0f), new Vector3(0f, 0f, -0.34f)
            };
            int[] triangles =
            {
                0,1,2, 0,2,3, 0,3,4, 0,4,1,
                5,2,1, 5,3,2, 5,4,3, 5,1,4
            };
            return BuildMesh("CS Faceted Gem", vertices, triangles);
        }

        private static Mesh CreateWedgeMesh()
        {
            Vector3[] vertices =
            {
                new Vector3(-0.50f,-0.50f,-0.30f), new Vector3(0.50f,-0.50f,-0.30f),
                new Vector3(-0.34f,-0.50f,0.30f), new Vector3(0.34f,-0.50f,0.30f),
                new Vector3(0f,0.50f,-0.18f), new Vector3(0f,0.50f,0.18f)
            };
            int[] triangles =
            {
                0,1,4, 2,5,3, 0,2,3, 0,3,1,
                1,3,5, 1,5,4, 4,5,2, 4,2,0
            };
            return BuildMesh("CS Wrap Wedge", vertices, triangles);
        }

        private static Mesh ExtrudeOutline(Vector2[] outline, float halfDepth)
        {
            int count = outline.Length;
            var vertices = new Vector3[count * 2];
            for (int i = 0; i < count; i++)
            {
                vertices[i] = new Vector3(outline[i].x, outline[i].y, -halfDepth);
                vertices[i + count] = new Vector3(outline[i].x, outline[i].y, halfDepth);
            }
            var triangles = new List<int>();
            for (int i = 1; i < count - 1; i++)
            {
                triangles.Add(0); triangles.Add(i + 1); triangles.Add(i);
                triangles.Add(count); triangles.Add(count + i); triangles.Add(count + i + 1);
            }
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                triangles.Add(i); triangles.Add(next); triangles.Add(count + next);
                triangles.Add(i); triangles.Add(count + next); triangles.Add(count + i);
            }
            return BuildMesh("CS Fitted Wrap Panel", vertices, triangles.ToArray());
        }

        private static Mesh BuildMesh(string name, Vector3[] vertices, int[] triangles)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            float minX = vertices.Min(vertex => vertex.x);
            float maxX = vertices.Max(vertex => vertex.x);
            float minY = vertices.Min(vertex => vertex.y);
            float maxY = vertices.Max(vertex => vertex.y);
            float width = Mathf.Max(0.0001f, maxX - minX);
            float height = Mathf.Max(0.0001f, maxY - minY);
            var uvs = new Vector2[vertices.Length];
            for (int index = 0; index < vertices.Length; index++)
                uvs[index] = new Vector2((vertices[index].x - minX) / width,
                    (vertices[index].y - minY) / height);
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int AccentCount(int tier)
        {
            return MonkHandwrapArt.GetProfile(tier).AccentCount;
        }

        private static PrimitiveType AccentPrimitive(int tier, int index)
        {
            if (tier == 1 && index < 4) return PrimitiveType.Cylinder;
            if (tier == 3 && index < 4) return PrimitiveType.Sphere;
            if (tier == 5 && index == 0) return PrimitiveType.Sphere;
            if ((tier == 11 || tier == 14 || tier == 15) && index < 4)
                return PrimitiveType.Sphere;
            if (tier == 9 && index < 5) return PrimitiveType.Cylinder;
            if ((tier == 18 || tier == 20 || tier == 21) && index < 4)
                return PrimitiveType.Sphere;
            if (tier == 19 && index < 4) return PrimitiveType.Cylinder;
            return PrimitiveType.Cube;
        }

        private static string AccentPartName(int tier, int index)
        {
            string[][] names =
            {
                Array.Empty<string>(),
                new[] { "CopperKnuckleRing1", "CopperKnuckleRing2",
                    "CopperKnuckleRing3", "CopperKnuckleRing4",
                    "CopperHandBand", "CopperWristBand" },
                new[] { "JadeScale1", "JadeScale2", "JadeScale3",
                    "JadeScale4", "JadeScale5" },
                new[] { "EmberStud1", "EmberStud2", "EmberStud3",
                    "EmberStud4", "EmberThreadBand" },
                new[] { "IronHandPlate", "IronKnuckle1", "IronKnuckle2",
                    "IronKnuckle3", "IronKnuckle4" },
                new[] { "MoonweavePearl", "MoonweaveSilverGuard1",
                    "MoonweaveSilverGuard2" },
                new[] { "StormCrystal1", "StormCrystal2", "StormCrystal3",
                    "StormCrystal4", "StormGoldWire1", "StormGoldWire2" },
                new[] { "GrandmasterIvoryPlate", "GrandmasterObsidianPlate",
                    "GrandmasterOpal1", "GrandmasterObsidian1",
                    "GrandmasterOpal2", "GrandmasterObsidian2" },
                new[] { "PrielIvory1", "PrielIvory2", "PrielIvory3",
                    "PrielIvory4", "PrielCrimsonGuard", "PrielSealPlate" },
                new[] { "ChainLink1", "ChainLink2", "ChainLink3", "ChainLink4",
                    "ChainLink5", "DarkwardenHandPlate", "DarkwardenGem" },
                new[] { "SilkClaw1", "SilkClaw2", "SilkClaw3", "SilkClaw4",
                    "NightmistressCrescent", "NightmistressGuard" },
                new[] { "Cinder1", "Cinder2", "Cinder3", "Cinder4",
                    "FirebonePlate", "FireboneRidge" },
                new[] { "Bone1", "Bone2", "Bone3", "Bone4", "Bone5",
                    "Bone6", "SinewGuard", "BoneweaverPlate" },
                new[] { "BurialBronze1", "BurialBronze2", "BurialBronze3",
                    "BurialBronze4", "CryptSeal", "CryptWristPlate" },
                new[] { "TempestCrystal1", "TempestCrystal2", "TempestCrystal3",
                    "TempestCrystal4", "SandGoldGuard1", "SandGoldGuard2",
                    "BraxonianPlate" },
                new[] { "PlanarShard1", "PlanarShard2", "PlanarShard3",
                    "PlanarShard4", "StarmetalGuard1", "StarmetalGuard2",
                    "PlanarPlate", "PrismaticCore" },
                new[] { "LinenPad1", "LinenPad2", "LinenPad3", "LinenPad4",
                    "CrossStitchedGuard" },
                new[] { "SilkScale1", "SilkScale2", "SilkScale3", "SilkScale4",
                    "JadeBraid", "SilkstepGuard" },
                new[] { "BlightThorn1", "BlightThorn2", "BlightThorn3",
                    "BlightThorn4", "ToxicStitch1", "ToxicStitch2",
                    "BlighthidePlate" },
                new[] { "IronbarkRivet1", "IronbarkRivet2", "IronbarkRivet3",
                    "IronbarkRivet4", "IronbarkPlate", "BronzeBrace" },
                new[] { "FrostCap1", "FrostCap2", "FrostCap3", "FrostCap4",
                    "SilverThread1", "SilverThread2", "IcePlate",
                    "FrostCrystal" },
                new[] { "StarInlay1", "StarInlay2", "StarInlay3", "StarInlay4",
                    "CelestialGuard1", "CelestialGuard2", "CelestialPlate",
                    "StarCore" }
            };
            return tier >= 0 && tier < names.Length &&
                index >= 0 && index < names[tier].Length
                ? names[tier][index] : "HandwrapDetail";
        }

        private static float AccentAcross(int tier, int index)
        {
            if ((tier == 1 && index < 4) || (tier == 3 && index < 4) ||
                (tier == 4 && index > 0) || (tier == 6 && index < 4) ||
                (tier == 7 && index > 1))
            {
                int knuckle = tier == 4 ? index - 1 :
                    tier == 7 ? index - 2 : index;
                return (knuckle - 1.5f) * 0.038f;
            }
            if (tier == 2) return (index - 2f) * 0.030f;
            if (tier == 5 && index > 0) return (index == 1 ? -1f : 1f) * 0.055f;
            if (tier == 7 && index < 2) return index == 0 ? -0.045f : 0.045f;
            if (tier >= 8 && index < 4) return (index - 1.5f) * 0.038f;
            if (tier >= 8) return ((index % 3) - 1f) * 0.055f;
            return 0f;
        }

        private static float AccentAlong(int tier, int index)
        {
            string name = AccentPartName(tier, index);
            if (name.IndexOf("Wrist", StringComparison.OrdinalIgnoreCase) >= 0)
                return 0.050f;
            if (name.IndexOf("Band", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Thread", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Wire", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Braid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Stitch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Sinew", StringComparison.OrdinalIgnoreCase) >= 0)
                return 0.035f;
            if (name.IndexOf("Plate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Guard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Seal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Ridge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Crescent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0)
                return 0.010f;
            // Knuckle details remain toward the fingers, with a small stagger
            // so multi-piece variants keep their distinct layered silhouette.
            return -0.030f + (index % 2) * 0.006f;
        }

        private static Quaternion AccentRotation(int tier, int index)
        {
            if (tier == 2) return Quaternion.Euler(0f, 0f,
                index % 2 == 0 ? -22f : 22f);
            if (tier == 5 && index > 0) return Quaternion.Euler(0f, 0f,
                index == 1 ? -30f : 30f);
            if (tier == 6 && index < 4) return Quaternion.Euler(0f, 0f, 45f);
            if (tier == 7 && index < 2) return Quaternion.Euler(0f, 0f,
                index == 0 ? -18f : 18f);
            if (tier >= 8) return Quaternion.Euler(0f, 0f,
                (index % 2 == 0 ? -1f : 1f) *
                Mathf.Min(28f, 10f + (tier - 8) * 2f));
            return Quaternion.identity;
        }

        private static Vector3 AccentScale(AccentShape shape, int tier,
            int index)
        {
            float tierGrowth = Mathf.Min(0.006f, tier * 0.0003f);
            switch (shape)
            {
                case AccentShape.FacetedGem:
                    return new Vector3(0.027f + tierGrowth,
                        0.030f + tierGrowth, 0.016f);
                case AccentShape.Wedge:
                    return new Vector3(0.030f + tierGrowth,
                        0.044f + tierGrowth, 0.014f);
                case AccentShape.Cord:
                    return new Vector3(0.112f, 0.017f, 0.009f);
                case AccentShape.Ring:
                    return new Vector3(0.045f, 0.045f, 0.030f);
                case AccentShape.Bone:
                    return new Vector3(0.050f, 0.024f, 0.016f);
                case AccentShape.Star:
                    return new Vector3(0.040f, 0.040f, 0.012f);
                case AccentShape.TaperedPlate:
                    return index < 4
                        ? new Vector3(0.074f, 0.042f, 0.014f)
                        : new Vector3(0.108f, 0.046f, 0.014f);
                default:
                    return new Vector3(0.026f + tierGrowth,
                        0.026f + tierGrowth, 0.013f);
            }
        }

        private static Color AccentColor(int tier, int index)
        {
            if (tier == 1) return new Color(0.78f, 0.31f, 0.08f, 1f);
            if (tier == 2) return new Color(0.18f, 0.74f, 0.39f, 1f);
            if (tier == 3) return index < 4
                ? new Color(1f, 0.20f, 0.025f, 1f)
                : new Color(1f, 0.46f, 0.04f, 1f);
            if (tier == 4) return new Color(0.34f, 0.37f, 0.42f, 1f);
            if (tier == 5) return index == 0
                ? new Color(0.92f, 0.92f, 0.82f, 1f)
                : new Color(0.68f, 0.70f, 0.78f, 1f);
            if (tier == 6) return index < 4
                ? new Color(0.20f, 0.90f, 1f, 1f)
                : new Color(0.88f, 0.63f, 0.15f, 1f);
            if (tier == 7)
            {
                if (index == 0) return new Color(0.88f, 0.82f, 0.66f, 1f);
                if (index == 1 || index == 3 || index == 5)
                    return new Color(0.035f, 0.030f, 0.045f, 1f);
                return new Color(0.93f, 0.91f, 0.78f, 1f);
            }
            if (tier == 8) return index < 4
                ? new Color(0.90f, 0.80f, 0.62f, 1f)
                : new Color(0.62f, 0.035f, 0.04f, 1f);
            if (tier == 9) return index == 6
                ? new Color(0.35f, 0.82f, 0.30f, 1f)
                : new Color(0.12f, 0.15f, 0.14f, 1f);
            if (tier == 10) return new Color(0.76f, 0.79f, 0.88f, 1f);
            if (tier == 11) return index < 4
                ? new Color(1f, 0.24f, 0.03f, 1f)
                : new Color(0.42f, 0.28f, 0.18f, 1f);
            if (tier == 12) return index < 6
                ? new Color(0.86f, 0.80f, 0.65f, 1f)
                : new Color(0.48f, 0.10f, 0.07f, 1f);
            if (tier == 13) return new Color(0.56f, 0.39f, 0.17f, 1f);
            if (tier == 14) return index < 4
                ? new Color(0.12f, 0.70f, 1f, 1f)
                : new Color(0.78f, 0.54f, 0.12f, 1f);
            if (tier == 15) return index < 4 || index == 7
                ? new Color(0.70f, 0.25f, 1f, 1f)
                : new Color(0.15f, 0.08f, 0.22f, 1f);
            if (tier == 16) return index < 4
                ? new Color(0.22f, 0.13f, 0.06f, 1f)
                : new Color(0.68f, 0.55f, 0.36f, 1f);
            if (tier == 17) return index < 4
                ? new Color(0.88f, 0.86f, 0.68f, 1f)
                : new Color(0.08f, 0.62f, 0.43f, 1f);
            if (tier == 18) return index < 4
                ? new Color(0.06f, 0.10f, 0.045f, 1f)
                : new Color(0.52f, 0.88f, 0.10f, 1f);
            if (tier == 19) return index < 4
                ? new Color(0.55f, 0.27f, 0.07f, 1f)
                : new Color(0.64f, 0.38f, 0.14f, 1f);
            if (tier == 20) return index < 4 || index == 7
                ? new Color(0.78f, 0.94f, 1f, 1f)
                : new Color(0.72f, 0.76f, 0.82f, 1f);
            if (tier == 21) return index < 4 || index == 7
                ? new Color(0.20f, 0.66f, 1f, 1f)
                : new Color(0.82f, 0.68f, 0.25f, 1f);
            return HandColors[Mathf.Clamp(tier, 0, HandColors.Length - 1)];
        }

        private static bool AccentIsLuminous(int tier, int index) =>
            tier == 3 || (tier == 5 && index == 0) ||
            (tier == 6 && index < 4) ||
            (tier == 7 && index >= 2 && index % 2 == 0) ||
            (tier == 9 && index == 6) || tier == 11 ||
            (tier == 14 && index < 4) ||
            (tier == 15 && (index < 4 || index == 7)) ||
            (tier == 18 && index >= 4) ||
            (tier == 20 && (index < 4 || index == 7)) ||
            (tier == 21 && (index < 4 || index == 7));

        private static bool AccentIsMetal(int tier, int index) =>
            tier == 1 || tier == 4 || (tier == 5 && index > 0) ||
            (tier == 6 && index >= 4) || tier == 7 || tier == 8 ||
            tier == 9 || tier == 10 || tier == 13 || tier == 14 ||
            tier == 15 || tier == 17 || tier == 19 || tier == 20 ||
            tier == 21;

        private void MakeMaterialsUnique(GameObject overlay)
        {
            foreach (Renderer renderer in
                overlay.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                Material[] source = renderer.sharedMaterials;
                var unique = new Material[source.Length];
                for (int index = 0; index < source.Length; index++)
                {
                    if (source[index] == null) continue;
                    unique[index] = new Material(source[index])
                    {
                        name = "CS Handwrap " + renderer.name + " " + index,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    _ownedMaterials.Add(unique[index]);
                }
                renderer.sharedMaterials = unique;
            }
        }

        private void ApplyPalette(GameObject overlay, int tier)
        {
            int safeTier = Mathf.Clamp(tier, 0, WristColors.Length - 1);
            if (_surfaceMaterial == null)
                _surfaceMaterial = MonkHandwrapArt.CreateSurfaceMaterial(
                    _ownedMaterials);
            MonkHandwrapArt.ApplySurface(overlay, safeTier,
                WristColors[safeTier], HandColors[safeTier], _surfaceMaterial,
                _ownedMeshes);
        }

        private static void SetColor(Material material, int property,
            Color color)
        {
            if (material.HasProperty(property))
                material.SetColor(property, color);
        }

        private static void SetFloat(Material material, int property,
            float value)
        {
            if (material.HasProperty(property))
                material.SetFloat(property, value);
        }

        private static string BuildPath(Transform value)
        {
            if (value == null) return "<missing>";
            string path = value.name;
            for (Transform parent = value.parent; parent != null;
                parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        private void OnDestroy()
        {
            if (_rightOverlay != null)
                UnityEngine.Object.Destroy(_rightOverlay);
            if (_leftOverlay != null)
                UnityEngine.Object.Destroy(_leftOverlay);
            if (_rightQaFrame != null)
                UnityEngine.Object.Destroy(_rightQaFrame.gameObject);
            if (_leftQaFrame != null)
                UnityEngine.Object.Destroy(_leftQaFrame.gameObject);
            foreach (GameObject accent in _rightAccents)
                if (accent != null) UnityEngine.Object.Destroy(accent);
            foreach (GameObject accent in _leftAccents)
                if (accent != null) UnityEngine.Object.Destroy(accent);
            foreach (Material material in _ownedMaterials)
                if (material != null)
                    UnityEngine.Object.Destroy(material);
            _ownedMaterials.Clear();
            foreach (Mesh mesh in _ownedMeshes)
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
            _ownedMeshes.Clear();
            _accentMeshes.Clear();
        }
    }
}
