using System;
using System.Collections.Generic;
using UnityEngine;

namespace ErenshorSkills.Monk
{
    internal enum MonkHandwrapFamily
    {
        Cloth,
        Padded,
        Leather,
        Cestus,
        Trophy,
        Arcane
    }

    internal sealed class MonkHandwrapProfile
    {
        internal readonly MonkHandwrapFamily Family;
        internal readonly int AtlasColumn;
        internal readonly int AtlasRow;
        internal readonly float ShellOffset;
        internal readonly int AccentCount;
        internal readonly float EmissionStrength;

        internal MonkHandwrapProfile(MonkHandwrapFamily family,
            int atlasColumn, int atlasRow, float shellOffset,
            int accentCount, float emissionStrength = 0f)
        {
            Family = family;
            AtlasColumn = atlasColumn;
            AtlasRow = atlasRow;
            ShellOffset = shellOffset;
            AccentCount = accentCount;
            EmissionStrength = emissionStrength;
        }
    }

    /// <summary>
    /// Runtime state attached to a handwrap surface. Native skinned meshes may
    /// be referenced directly at runtime; readable procedural meshes can also
    /// cache the six family silhouettes.
    /// </summary>
    internal sealed class MonkHandwrapSurfaceState : MonoBehaviour
    {
        internal Mesh BaseMesh;
        internal MonkHandwrapFamily Family = (MonkHandwrapFamily)(-1);
        internal readonly Dictionary<MonkHandwrapFamily, Mesh> FamilyMeshes =
            new Dictionary<MonkHandwrapFamily, Mesh>();
    }

    internal static class MonkHandwrapArt
    {
        private const string AtlasFile = "MonkHandwrapAtlas.png";
        private const string NormalFile = "MonkHandwrapNormal.png";
        private const string MetallicFile = "MonkHandwrapMetallic.png";
        private const string EmissionFile = "MonkHandwrapEmission.png";

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int BumpMapStId = Shader.PropertyToID("_BumpMap_ST");
        private static readonly int BumpScaleId = Shader.PropertyToID("_BumpScale");
        private static readonly int MetallicMapId =
            Shader.PropertyToID("_MetallicGlossMap");
        private static readonly int MetallicMapStId =
            Shader.PropertyToID("_MetallicGlossMap_ST");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissionMapStId =
            Shader.PropertyToID("_EmissionMap_ST");
        private static readonly int EmissionColorId =
            Shader.PropertyToID("_EmissionColor");

        // The six silhouette families are deliberately reused. Individual item
        // identity comes from palette, material tile and restrained attachments,
        // rather than allocating twenty-two unrelated high-overdraw renderers.
        private static readonly MonkHandwrapProfile[] Profiles =
        {
            new MonkHandwrapProfile(MonkHandwrapFamily.Cloth,   0, 1, 0.0015f, 0),
            new MonkHandwrapProfile(MonkHandwrapFamily.Padded,  0, 1, 0.0042f, 5),
            new MonkHandwrapProfile(MonkHandwrapFamily.Leather, 1, 1, 0.0030f, 3),
            new MonkHandwrapProfile(MonkHandwrapFamily.Leather, 1, 1, 0.0030f, 3, 0.45f),
            new MonkHandwrapProfile(MonkHandwrapFamily.Cestus,  2, 1, 0.0062f, 5),
            new MonkHandwrapProfile(MonkHandwrapFamily.Cloth,   0, 1, 0.0015f, 3),
            new MonkHandwrapProfile(MonkHandwrapFamily.Arcane,  2, 0, 0.0042f, 3, 0.35f),
            new MonkHandwrapProfile(MonkHandwrapFamily.Arcane,  2, 0, 0.0042f, 4),
            new MonkHandwrapProfile(MonkHandwrapFamily.Leather, 1, 1, 0.0030f, 4),
            new MonkHandwrapProfile(MonkHandwrapFamily.Trophy,  0, 0, 0.0048f, 5),
            new MonkHandwrapProfile(MonkHandwrapFamily.Leather, 1, 1, 0.0030f, 3),
            new MonkHandwrapProfile(MonkHandwrapFamily.Trophy,  0, 0, 0.0048f, 4, 0.40f),
            new MonkHandwrapProfile(MonkHandwrapFamily.Trophy,  0, 0, 0.0048f, 5),
            new MonkHandwrapProfile(MonkHandwrapFamily.Trophy,  0, 0, 0.0048f, 4),
            new MonkHandwrapProfile(MonkHandwrapFamily.Cestus,  2, 1, 0.0062f, 5),
            new MonkHandwrapProfile(MonkHandwrapFamily.Arcane,  2, 0, 0.0042f, 5, 0.55f),
            new MonkHandwrapProfile(MonkHandwrapFamily.Padded,  0, 1, 0.0042f, 5),
            new MonkHandwrapProfile(MonkHandwrapFamily.Cloth,   0, 1, 0.0015f, 4),
            new MonkHandwrapProfile(MonkHandwrapFamily.Leather, 1, 1, 0.0030f, 5),
            new MonkHandwrapProfile(MonkHandwrapFamily.Cestus,  2, 1, 0.0062f, 6),
            new MonkHandwrapProfile(MonkHandwrapFamily.Arcane,  1, 0, 0.0042f, 5, 0.35f),
            new MonkHandwrapProfile(MonkHandwrapFamily.Arcane,  2, 0, 0.0042f, 6, 0.45f)
        };

        internal static MonkHandwrapProfile GetProfile(int tier) =>
            Profiles[Mathf.Clamp(tier, 0, Profiles.Length - 1)];

        internal static int ProfileCount => Profiles.Length;

        internal static bool ValidateProfiles(out string detail)
        {
            var families = new HashSet<MonkHandwrapFamily>();
            foreach (MonkHandwrapProfile profile in Profiles)
                families.Add(profile.Family);
            bool valid = Profiles.Length == 22 && families.Count == 6 &&
                Array.TrueForAll(Profiles, profile =>
                    profile.AtlasColumn >= 0 && profile.AtlasColumn < 3 &&
                    profile.AtlasRow >= 0 && profile.AtlasRow < 2 &&
                    profile.AccentCount >= 0 && profile.AccentCount <= 6);
            detail = $"profiles={Profiles.Length}/22, families={families.Count}/6";
            return valid;
        }

        internal static bool ValidateCatalog(out string detail)
        {
            bool profiles = ValidateProfiles(out string profileDetail);
            string[] resources =
                { AtlasFile, NormalFile, MetallicFile, EmissionFile };
            int embedded = 0;
            foreach (string resource in resources)
            {
                byte[] bytes = EmbeddedResources.GetBytesByFilename(resource);
                if (bytes != null && bytes.Length > 64) embedded++;
            }
            detail = profileDetail + $", PBR maps={embedded}/{resources.Length}";
            return profiles && embedded == resources.Length;
        }

        internal static Material CreateSurfaceMaterial(List<Material> owned)
        {
            Shader shader = Shader.Find("Standard") ??
                Shader.Find("Sprites/Default");
            var material = new Material(shader)
            {
                name = "CS Handwrap Shared PBR Surface",
                hideFlags = HideFlags.HideAndDontSave
            };
            Texture2D atlas = EmbeddedResources.LoadTexture(AtlasFile,
                "CS Handwrap Albedo Atlas", FilterMode.Bilinear,
                TextureWrapMode.Repeat);
            Texture2D normal = EmbeddedResources.LoadTexture(NormalFile,
                "CS Handwrap Normal Atlas", FilterMode.Bilinear,
                TextureWrapMode.Repeat, true);
            Texture2D metallic = EmbeddedResources.LoadTexture(MetallicFile,
                "CS Handwrap Metallic Atlas", FilterMode.Bilinear,
                TextureWrapMode.Repeat, true);
            Texture2D emission = EmbeddedResources.LoadTexture(EmissionFile,
                "CS Handwrap Emission Atlas", FilterMode.Bilinear,
                TextureWrapMode.Repeat, true);
            if (atlas != null && material.HasProperty(MainTexId))
                material.SetTexture(MainTexId, atlas);
            if (normal != null && material.HasProperty(BumpMapId))
            {
                material.SetTexture(BumpMapId, normal);
                material.SetFloat(BumpScaleId, 0.62f);
                material.EnableKeyword("_NORMALMAP");
            }
            if (metallic != null && material.HasProperty(MetallicMapId))
            {
                material.SetTexture(MetallicMapId, metallic);
                material.EnableKeyword("_METALLICGLOSSMAP");
            }
            if (emission != null && material.HasProperty(EmissionMapId))
            {
                material.SetTexture(EmissionMapId, emission);
                material.EnableKeyword("_EMISSION");
            }
            if (material.HasProperty(MetallicId)) material.SetFloat(MetallicId, 1f);
            if (material.HasProperty(GlossinessId)) material.SetFloat(GlossinessId, 1f);
            owned?.Add(material);
            return material;
        }

        internal static void InitializeSurface(GameObject handSurface)
        {
            if (handSurface == null) return;
            MeshFilter filter = handSurface.GetComponent<MeshFilter>();
            SkinnedMeshRenderer skinned =
                handSurface.GetComponent<SkinnedMeshRenderer>();
            Mesh mesh = filter != null ? filter.sharedMesh : skinned?.sharedMesh;
            if (mesh == null) return;
            MonkHandwrapSurfaceState state =
                handSurface.GetComponent<MonkHandwrapSurfaceState>() ??
                handSurface.AddComponent<MonkHandwrapSurfaceState>();
            state.BaseMesh = mesh;
        }

        internal static void ApplySurface(GameObject overlay, int tier,
            Color wristColor, Color handColor, Material sharedMaterial,
            List<Mesh> ownedMeshes)
        {
            if (overlay == null || sharedMaterial == null) return;
            MonkHandwrapProfile profile = GetProfile(tier);
            Vector4 atlasTransform = new Vector4(1f / 3f, 1f / 2f,
                profile.AtlasColumn / 3f, profile.AtlasRow / 2f);
            foreach (Renderer renderer in
                overlay.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                bool hand = string.Equals(renderer.name, "CS_HandTopWrap",
                    StringComparison.OrdinalIgnoreCase);
                if (hand)
                    ApplyFamilyMesh(renderer, profile, ownedMeshes);
                renderer.sharedMaterials = new[] { sharedMaterial };
                Color sourceColor = hand ? handColor : wristColor;
                // Preserve the hand-painted material response while retaining
                // each item's established palette. Direct multiplication by
                // the darkest tier colors made leather and cloth read black.
                Color tint = Color.Lerp(Color.white, sourceColor, 0.68f);
                tint.a = 1f;
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor(ColorId, tint);
                block.SetVector(MainTexStId, atlasTransform);
                block.SetVector(BumpMapStId, atlasTransform);
                block.SetVector(MetallicMapStId, atlasTransform);
                block.SetVector(EmissionMapStId, atlasTransform);
                block.SetColor(EmissionColorId, profile.EmissionStrength > 0f
                    ? sourceColor * profile.EmissionStrength
                    : Color.black);
                block.SetFloat(MetallicId, 1f);
                block.SetFloat(GlossinessId, 1f);
                block.SetFloat(BumpScaleId,
                    profile.Family == MonkHandwrapFamily.Cloth ? 0.78f : 0.58f);
                renderer.SetPropertyBlock(block);
            }
        }

        private static void ApplyFamilyMesh(Renderer renderer,
            MonkHandwrapProfile profile, List<Mesh> ownedMeshes)
        {
            if (renderer == null) return;
            MonkHandwrapSurfaceState state =
                renderer.GetComponent<MonkHandwrapSurfaceState>();
            if (state == null || state.BaseMesh == null ||
                state.Family == profile.Family) return;
            if (!state.FamilyMeshes.TryGetValue(profile.Family, out Mesh mesh))
            {
                mesh = BuildFamilyMesh(state.BaseMesh, profile);
                if (mesh != null)
                {
                    state.FamilyMeshes[profile.Family] = mesh;
                    if (mesh != state.BaseMesh) ownedMeshes?.Add(mesh);
                }
            }
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if (mesh != null && filter != null) filter.sharedMesh = mesh;
            if (mesh != null && skinned != null) skinned.sharedMesh = mesh;
            state.Family = profile.Family;
        }

        private static Mesh BuildFamilyMesh(Mesh source,
            MonkHandwrapProfile profile)
        {
            if (source == null || !source.isReadable) return source;
            try
            {
                Mesh mesh = UnityEngine.Object.Instantiate(source);
                mesh.name = "CS " + profile.Family + " Handwrap Family";
                mesh.hideFlags = HideFlags.HideAndDontSave;
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                if (normals != null && normals.Length == vertices.Length)
                {
                    float maxX = 0.0001f;
                    float maxY = 0.0001f;
                    for (int index = 0; index < vertices.Length; index++)
                    {
                        maxX = Mathf.Max(maxX, Mathf.Abs(vertices[index].x));
                        maxY = Mathf.Max(maxY, Mathf.Abs(vertices[index].y));
                    }
                    for (int index = 0; index < vertices.Length; index++)
                    {
                        float x = vertices[index].x / maxX;
                        float y = vertices[index].y / maxY;
                        switch (profile.Family)
                        {
                            case MonkHandwrapFamily.Padded:
                                vertices[index].x *= 1.06f;
                                vertices[index].y *= 1.025f;
                                vertices[index].z *= 1.30f;
                                break;
                            case MonkHandwrapFamily.Leather:
                                vertices[index].x *= 0.98f +
                                    Mathf.InverseLerp(-1f, 1f, y) * 0.08f;
                                vertices[index].z *= 1.12f;
                                break;
                            case MonkHandwrapFamily.Cestus:
                                vertices[index].x *= y > 0f ? 1.16f : 1.07f;
                                vertices[index].z *= 1.58f;
                                break;
                            case MonkHandwrapFamily.Trophy:
                                vertices[index].x *= 1.03f +
                                    Mathf.Abs(y) * 0.07f;
                                vertices[index].z *= 1.34f +
                                    Mathf.Abs(x) * 0.14f;
                                break;
                            case MonkHandwrapFamily.Arcane:
                                vertices[index].x *= 0.88f +
                                    Mathf.Abs(y) * 0.13f;
                                vertices[index].y *= 1.08f;
                                vertices[index].z *= 1.18f;
                                break;
                        }
                        vertices[index] += normals[index] * profile.ShellOffset;
                    }
                    mesh.vertices = vertices;
                    mesh.RecalculateBounds();
                }
                if (mesh.uv != null && mesh.uv.Length == mesh.vertexCount &&
                    mesh.normals != null && mesh.normals.Length == mesh.vertexCount)
                    mesh.RecalculateTangents();
                return mesh;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log?.LogWarning(
                    "Monk handwrap family mesh fallback: " + ex.Message);
                return source;
            }
        }

        internal static bool ValidateSurface(GameObject overlay, int tier,
            out string detail)
        {
            Transform surface = overlay?.transform.Find("CS_HandTopWrap");
            Renderer renderer = surface?.GetComponent<Renderer>();
            MeshFilter filter = surface?.GetComponent<MeshFilter>();
            SkinnedMeshRenderer skinned =
                surface?.GetComponent<SkinnedMeshRenderer>();
            MonkHandwrapSurfaceState state =
                surface?.GetComponent<MonkHandwrapSurfaceState>();
            MonkHandwrapProfile profile = GetProfile(tier);
            Mesh mesh = filter != null ? filter.sharedMesh : skinned?.sharedMesh;
            bool anchored = renderer != null && mesh != null &&
                surface.parent == overlay.transform &&
                overlay.transform.parent != null;
            bool fitted = skinned != null && skinned.bones != null &&
                skinned.bones.Length >= 6 && mesh.vertexCount > 0;
            var block = new MaterialPropertyBlock();
            renderer?.GetPropertyBlock(block);
            Vector4 atlas = block.GetVector(MainTexStId);
            Vector4 expected = new Vector4(1f / 3f, 1f / 2f,
                profile.AtlasColumn / 3f, profile.AtlasRow / 2f);
            bool mapped = Approximately(atlas, expected) &&
                renderer?.sharedMaterial != null;
            bool family = state != null && state.Family == profile.Family;
            detail = $"family={profile.Family}, skinned-fit={fitted}, " +
                $"hand-anchored={anchored}, atlas={mapped}, " +
                $"mesh={mesh?.name ?? "missing"}";
            return anchored && fitted && mapped && family;
        }

        private static bool Approximately(Vector4 left, Vector4 right)
        {
            return Mathf.Approximately(left.x, right.x) &&
                Mathf.Approximately(left.y, right.y) &&
                Mathf.Approximately(left.z, right.z) &&
                Mathf.Approximately(left.w, right.w);
        }
    }
}
