using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ErenshorSkills
{
    /// <summary>
    /// Gives add-on class trainers a class-readable silhouette while retaining
    /// their native humanoid prefab, rig, animations, and interaction.
    /// </summary>
    internal static class TrainerClassAppearance
    {
        private static readonly int[] ColorProperties =
        {
            Shader.PropertyToID("_Color"),
            Shader.PropertyToID("_BaseColor"),
            Shader.PropertyToID("_Color_Primary"),
            Shader.PropertyToID("_Color_Secondary"),
            Shader.PropertyToID("_Color_Cloth"),
            Shader.PropertyToID("_Color_Leather")
        };
        private static readonly Dictionary<string, Material> OutfitMaterials =
            new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        internal static bool ApplyMonk(GameObject trainer, out string detail)
        {
            if (trainer == null)
            {
                detail = "trainer object is unavailable";
                return false;
            }
            int weaponsHidden = SetHeldMeshVisibility(trainer, false);
            int armorHidden = HideArmorAttachments(trainer);
            int wraps = AddMonkHandwraps(trainer);
            bool headwrap = ActivateNamedVisual(trainer, "BanditMask",
                new Color(0.82f, 0.61f, 0.24f, 1f));
            if (!headwrap) headwrap = CreateMonkHeadwrap(trainer);
            // The source Reaver wears near-black. A bright ochre recolor is
            // deliberately high-contrast so the Monk reads as a different
            // class even before the gi, wraps, sash, and headwrap silhouette.
            TintOutfit(trainer, new Color(0.82f, 0.42f, 0.07f, 1f), true);
            int outfitPieces = CreateMonkOutfit(trainer);
            bool ready = wraps >= 2 && weaponsHidden >= 1 && headwrap &&
                outfitPieces >= 10;
            detail = $"unique unarmed martial silhouette with {wraps} skinned " +
                $"forearm/hand wrap part(s), {weaponsHidden} held weapon(s) hidden, " +
                $"{armorHidden} heavy attachment(s) removed, and Initiate " +
                $"headwrap active={headwrap}; complete ochre gi outfit pieces=" +
                outfitPieces;
            return ready;
        }

        internal static bool ApplyNecromancer(GameObject trainer,
            out string detail)
        {
            if (trainer == null)
            {
                detail = "trainer object is unavailable";
                return false;
            }
            int weaponsHidden = SetHeldMeshVisibility(trainer, false);
            int armorHidden = HideArmorAttachments(trainer);
            bool hood = ActivateExclusivePart(trainer,
                "All_00_HeadCoverings", "Chr_HeadCoverings_No_Hair_03");
            int hairHidden = SetGroupChildrenVisible(trainer,
                "All_01_Hair", false);
            TintOutfit(trainer, new Color(0.16f, 0.025f, 0.24f, 1f), false);
            TintNamedPart(trainer, "Chr_HeadCoverings_No_Hair_03",
                new Color(0.09f, 0.015f, 0.14f, 1f));
            int outfitPieces = CreateNecromancerOutfit(trainer);
            bool ready = hood && hairHidden >= 1 && outfitPieces >= 10;
            detail = $"unique dark ritual robe silhouette and native hood active; hood={hood}, " +
                $"hair parts hidden={hairHidden}, arcane weapons hidden=" +
                $"{weaponsHidden}, armor attachments removed={armorHidden}, " +
                $"complete class outfit pieces={outfitPieces}";
            return ready;
        }

        private static int CreateMonkOutfit(GameObject trainer)
        {
            const string rootName = "CS_MonkTrainerCompleteOutfit";
            Transform existing = FindChild(trainer, rootName);
            if (existing != null)
                return CountCustomOutfitPieces(trainer);
            Animator animator = ResolveAnimator(trainer);
            if (animator == null) return 0;
            var root = new GameObject(rootName);
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.SetParent(trainer.transform, false);
            Color ochre = new Color(0.76f, 0.34f, 0.055f, 1f);
            Color cream = new Color(0.82f, 0.72f, 0.48f, 1f);
            Color brown = new Color(0.20f, 0.105f, 0.045f, 1f);
            int created = 0;
            created += AddBonePiece(root.transform, animator, HumanBodyBones.Chest,
                "CrossedGiFront", PrimitiveType.Cube,
                new Vector3(0f, 0.02f, 0.115f), new Vector3(0.33f, 0.34f, 0.055f),
                Quaternion.Euler(0f, 0f, -8f), ochre, "MonkOchre");
            created += AddBonePiece(root.transform, animator, HumanBodyBones.Chest,
                "GiBack", PrimitiveType.Cube,
                new Vector3(0f, 0.02f, -0.105f), new Vector3(0.34f, 0.34f, 0.045f),
                Quaternion.identity, brown, "MonkBrown");
            created += AddBonePiece(root.transform, animator, HumanBodyBones.Hips,
                "WideSash", PrimitiveType.Cube,
                new Vector3(0f, 0.10f, 0f), new Vector3(0.39f, 0.085f, 0.22f),
                Quaternion.identity, cream, "MonkCream");
            created += AddBonePiece(root.transform, animator, HumanBodyBones.Hips,
                "SashTailLeft", PrimitiveType.Cube,
                new Vector3(-0.13f, -0.13f, 0.18f), new Vector3(0.075f, 0.30f, 0.035f),
                Quaternion.Euler(0f, 0f, 8f), ochre, "MonkOchre");
            created += AddBonePiece(root.transform, animator, HumanBodyBones.Hips,
                "SashTailRight", PrimitiveType.Cube,
                new Vector3(0.13f, -0.10f, 0.175f), new Vector3(0.065f, 0.24f, 0.035f),
                Quaternion.Euler(0f, 0f, -8f), brown, "MonkBrown");
            created += AddLimbPair(root.transform, animator,
                HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
                "ForearmWrap", PrimitiveType.Cylinder,
                new Vector3(0f, 0.10f, 0f), new Vector3(0.075f, 0.12f, 0.075f),
                Quaternion.Euler(0f, 0f, 90f), cream, "MonkCream");
            created += AddLimbPair(root.transform, animator,
                HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
                "ShinWrap", PrimitiveType.Cylinder,
                new Vector3(0f, 0.16f, 0f), new Vector3(0.10f, 0.20f, 0.10f),
                Quaternion.identity, brown, "MonkBrown");
            created += AddLimbPair(root.transform, animator,
                HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
                "SandaledFoot", PrimitiveType.Cube,
                new Vector3(0f, 0.02f, 0.07f), new Vector3(0.13f, 0.045f, 0.24f),
                Quaternion.identity, cream, "MonkCream");
            return created;
        }

        private static int CreateNecromancerOutfit(GameObject trainer)
        {
            const string rootName = "CS_NecromancerTrainerCompleteOutfit";
            Transform existing = FindChild(trainer, rootName);
            if (existing != null)
                return CountCustomOutfitPieces(trainer);
            Animator animator = ResolveAnimator(trainer);
            if (animator == null) return 0;
            var root = new GameObject(rootName);
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.SetParent(trainer.transform, false);
            Color black = new Color(0.035f, 0.025f, 0.045f, 1f);
            Color violet = new Color(0.20f, 0.035f, 0.28f, 1f);
            Color bone = new Color(0.72f, 0.68f, 0.53f, 1f);
            int created = 0;
            created += AddBonePiece(root.transform, animator, HumanBodyBones.Chest,
                "RitualRobeTorso", PrimitiveType.Cube,
                new Vector3(0f, 0f, 0f), new Vector3(0.42f, 0.42f, 0.25f),
                Quaternion.identity, black, "NecroBlack");
            created += AddBonePiece(root.transform, animator, HumanBodyBones.UpperChest,
                "HighMantle", PrimitiveType.Cube,
                new Vector3(0f, 0.06f, 0f), new Vector3(0.55f, 0.12f, 0.30f),
                Quaternion.identity, violet, "NecroViolet");
            created += AddBonePiece(root.transform, animator, HumanBodyBones.Hips,
                "RobeWaist", PrimitiveType.Cylinder,
                new Vector3(0f, 0.04f, 0f), new Vector3(0.30f, 0.12f, 0.30f),
                Quaternion.identity, bone, "NecroBone");
            created += AddBonePiece(root.transform, animator, HumanBodyBones.Hips,
                "LongRobeFront", PrimitiveType.Cube,
                new Vector3(0f, -0.33f, 0.12f), new Vector3(0.42f, 0.70f, 0.08f),
                Quaternion.identity, violet, "NecroViolet");
            created += AddBonePiece(root.transform, animator, HumanBodyBones.Hips,
                "LongRobeBack", PrimitiveType.Cube,
                new Vector3(0f, -0.33f, -0.10f), new Vector3(0.42f, 0.70f, 0.07f),
                Quaternion.identity, black, "NecroBlack");
            created += AddLimbPair(root.transform, animator,
                HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
                "MantleShoulder", PrimitiveType.Sphere,
                Vector3.zero, new Vector3(0.18f, 0.12f, 0.20f),
                Quaternion.identity, violet, "NecroViolet");
            created += AddLimbPair(root.transform, animator,
                HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
                "RitualSleeve", PrimitiveType.Cylinder,
                new Vector3(0f, 0.10f, 0f), new Vector3(0.13f, 0.18f, 0.13f),
                Quaternion.Euler(0f, 0f, 90f), black, "NecroBlack");
            created += AddLimbPair(root.transform, animator,
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
                "SplitRobe", PrimitiveType.Cube,
                new Vector3(0f, 0.20f, 0f), new Vector3(0.20f, 0.48f, 0.22f),
                Quaternion.identity, violet, "NecroViolet");
            created += AddBonePiece(root.transform, animator, HumanBodyBones.Chest,
                "BoneTalisman", PrimitiveType.Sphere,
                new Vector3(0f, -0.08f, 0.24f), new Vector3(0.07f, 0.09f, 0.045f),
                Quaternion.identity, bone, "NecroBone");
            return created;
        }

        private static int AddLimbPair(Transform root, Animator animator,
            HumanBodyBones left, HumanBodyBones right, string name,
            PrimitiveType primitive, Vector3 position, Vector3 scale,
            Quaternion rotation, Color color, string materialKey)
        {
            return AddBonePiece(root, animator, left, name + "Left", primitive,
                       position, scale, rotation, color, materialKey) +
                   AddBonePiece(root, animator, right, name + "Right", primitive,
                       position, scale, rotation, color, materialKey);
        }

        private static int AddBonePiece(Transform root, Animator animator,
            HumanBodyBones bone, string name, PrimitiveType primitive,
            Vector3 localPosition, Vector3 localScale,
            Quaternion localRotation, Color color, string materialKey)
        {
            Transform parent = animator.GetBoneTransform(bone) ??
                ResolveTrainerBone(animator, bone);
            if (parent == null) return 0;
            GameObject piece = GameObject.CreatePrimitive(primitive);
            piece.name = "CS_" + name;
            piece.hideFlags = HideFlags.HideAndDontSave;
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = localPosition;
            piece.transform.localRotation = localRotation;
            piece.transform.localScale = localScale;
            Collider collider = piece.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.Destroy(collider);
            MeshRenderer renderer = piece.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = GetOutfitMaterial(materialKey, color);
            return renderer != null ? 1 : 0;
        }

        private static Transform ResolveTrainerBone(Animator animator,
            HumanBodyBones bone)
        {
            if (animator == null) return null;
            string[] tokens;
            bool left = false;
            bool right = false;
            switch (bone)
            {
                case HumanBodyBones.Hips:
                    tokens = new[] { "hips", "pelvis" }; break;
                case HumanBodyBones.Chest:
                    tokens = new[] { "chest", "spine2", "spine02", "spine" };
                    break;
                case HumanBodyBones.UpperChest:
                    tokens = new[] { "upperchest", "spine3", "spine03", "chest" };
                    break;
                case HumanBodyBones.LeftUpperArm:
                    left = true; tokens = new[] { "upperarm", "armupper" }; break;
                case HumanBodyBones.RightUpperArm:
                    right = true; tokens = new[] { "upperarm", "armupper" }; break;
                case HumanBodyBones.LeftLowerArm:
                    left = true; tokens = new[] { "lowerarm", "forearm", "armlower" }; break;
                case HumanBodyBones.RightLowerArm:
                    right = true; tokens = new[] { "lowerarm", "forearm", "armlower" }; break;
                case HumanBodyBones.LeftLowerLeg:
                    left = true; tokens = new[] { "lowerleg", "calf", "shin" }; break;
                case HumanBodyBones.RightLowerLeg:
                    right = true; tokens = new[] { "lowerleg", "calf", "shin" }; break;
                case HumanBodyBones.LeftFoot:
                    left = true; tokens = new[] { "foot", "ankle" }; break;
                case HumanBodyBones.RightFoot:
                    right = true; tokens = new[] { "foot", "ankle" }; break;
                default:
                    return null;
            }
            foreach (Transform candidate in animator.GetComponentsInChildren<
                Transform>(true))
            {
                if (candidate == null) continue;
                string name = new string(candidate.name.ToLowerInvariant()
                    .Where(char.IsLetterOrDigit).ToArray());
                if (!tokens.Any(name.Contains)) continue;
                bool isLeft = name.Contains("left") || name.EndsWith("l") ||
                    name.StartsWith("l");
                bool isRight = name.Contains("right") || name.EndsWith("r") ||
                    name.StartsWith("r");
                if (left && !isLeft) continue;
                if (right && !isRight) continue;
                return candidate;
            }
            return null;
        }

        private static int CountCustomOutfitPieces(GameObject trainer) =>
            trainer.GetComponentsInChildren<MeshRenderer>(true).Count(value =>
                value != null && value.name.StartsWith("CS_",
                    StringComparison.Ordinal));

        private static Animator ResolveAnimator(GameObject trainer) =>
            trainer.GetComponent<Animator>() ??
            trainer.GetComponentInChildren<Animator>();

        private static Transform FindChild(GameObject trainer, string name) =>
            trainer.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(value => value != null &&
                    string.Equals(value.name, name,
                        StringComparison.OrdinalIgnoreCase));

        private static int AddMonkHandwraps(GameObject trainer)
        {
            int count = 0;
            string[] groups =
            {
                "Male_06_Arm_Lower_Right", "Male_07_Arm_Lower_Left",
                "Male_08_Hand_Right", "Male_09_Hand_Left"
            };
            foreach (Renderer renderer in
                trainer.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.gameObject.activeSelf)
                    continue;
                string path = BuildPath(renderer.transform);
                if (!groups.Any(path.Contains)) continue;
                ApplyPalette(renderer,
                    new Color(0.82f, 0.61f, 0.24f, 1f));
                count++;
            }
            return count;
        }

        private static int HideArmorAttachments(GameObject trainer)
        {
            string[] groups =
            {
                "All_02_Head_Attachment", "All_04_Back_Attachment",
                "All_05_Shoulder_Attachment_Right",
                "All_06_Shoulder_Attachment_Left",
                "All_07_Elbow_Attachment_Right",
                "All_08_Elbow_Attachment_Left", "All_09_Hips_Attachment",
                "All_10_Knee_Attachement_Right",
                "All_11_Knee_Attachement_Left"
            };
            return groups.Sum(group => SetGroupChildrenVisible(
                trainer, group, false));
        }

        private static int SetHeldMeshVisibility(GameObject trainer, bool visible)
        {
            int changed = 0;
            foreach (MeshRenderer renderer in
                trainer.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer == null) continue;
                string path = BuildPath(renderer.transform);
                if (!path.Contains("/Hand_R/", StringComparison.Ordinal) &&
                    !path.Contains("/Hand_L/", StringComparison.Ordinal))
                    continue;
                if (renderer.gameObject.activeSelf == visible) continue;
                renderer.gameObject.SetActive(visible);
                changed++;
            }
            return changed;
        }

        private static bool ActivateExclusivePart(GameObject trainer,
            string groupName, string partName)
        {
            Transform group = trainer.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(value => string.Equals(value.name, groupName,
                    StringComparison.OrdinalIgnoreCase));
            if (group == null) return false;
            Transform selected = null;
            foreach (Renderer renderer in
                group.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                bool match = string.Equals(renderer.name, partName,
                    StringComparison.OrdinalIgnoreCase);
                renderer.gameObject.SetActive(match);
                if (match) selected = renderer.transform;
            }
            return selected != null;
        }

        private static bool ActivateNamedVisual(GameObject trainer,
            string name, Color color)
        {
            Transform visual = trainer.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(value => value != null &&
                    string.Equals(value.name, name,
                        StringComparison.OrdinalIgnoreCase));
            if (visual == null) return false;
            visual.gameObject.SetActive(true);
            foreach (Renderer renderer in
                visual.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                renderer.gameObject.SetActive(true);
                ApplyPalette(renderer, color);
            }
            return visual.GetComponentsInChildren<Renderer>(true).Length > 0;
        }

        private static bool CreateMonkHeadwrap(GameObject trainer)
        {
            Animator animator = trainer.GetComponent<Animator>() ??
                trainer.GetComponentInChildren<Animator>();
            Transform head = animator?.GetBoneTransform(HumanBodyBones.Head);
            if (head == null)
                head = trainer.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(value => value != null &&
                        string.Equals(value.name, "Head",
                            StringComparison.OrdinalIgnoreCase));
            if (head == null) return false;
            var root = new GameObject("CS_MonkTrainerHeadwrap");
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.SetParent(head, false);
            root.transform.localPosition = new Vector3(0f, 0.10f, 0f);
            CreateHeadwrapPiece(root.transform, "Front",
                new Vector3(0f, 0f, 0.105f),
                new Vector3(0.22f, 0.035f, 0.025f));
            CreateHeadwrapPiece(root.transform, "Back",
                new Vector3(0f, 0f, -0.105f),
                new Vector3(0.22f, 0.035f, 0.025f));
            CreateHeadwrapPiece(root.transform, "Left",
                new Vector3(-0.105f, 0f, 0f),
                new Vector3(0.025f, 0.035f, 0.19f));
            CreateHeadwrapPiece(root.transform, "Right",
                new Vector3(0.105f, 0f, 0f),
                new Vector3(0.025f, 0.035f, 0.19f));
            return root.GetComponentsInChildren<MeshRenderer>(true).Length == 4;
        }

        private static void CreateHeadwrapPiece(Transform parent, string name,
            Vector3 localPosition, Vector3 localScale)
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = "CS_MonkHeadwrap_" + name;
            piece.hideFlags = HideFlags.HideAndDontSave;
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = localPosition;
            piece.transform.localRotation = Quaternion.identity;
            piece.transform.localScale = localScale;
            Collider collider = piece.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.Destroy(collider);
            MeshRenderer renderer = piece.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = GetOutfitMaterial("MonkHeadwrap",
                    new Color(0.86f, 0.50f, 0.08f, 1f));
        }

        private static Material GetOutfitMaterial(string key, Color color)
        {
            if (OutfitMaterials.TryGetValue(key, out Material existing) &&
                existing != null) return existing;
            Shader shader = Shader.Find("Standard") ??
                Shader.Find("Sprites/Default");
            var material = new Material(shader)
            {
                name = "CS Trainer Outfit " + key,
                color = color,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0.18f);
            OutfitMaterials[key] = material;
            return material;
        }

        private static int SetGroupChildrenVisible(GameObject trainer,
            string groupName, bool visible)
        {
            Transform group = trainer.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(value => string.Equals(value.name, groupName,
                    StringComparison.OrdinalIgnoreCase));
            if (group == null) return 0;
            int changed = 0;
            foreach (Renderer renderer in
                group.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null ||
                    renderer.gameObject.activeSelf == visible) continue;
                renderer.gameObject.SetActive(visible);
                changed++;
            }
            return changed;
        }

        private static void TintOutfit(GameObject trainer, Color color,
            bool includeHands)
        {
            string[] parts = includeHands
                ? new[] { "_Torso/", "_Arm_", "_Hand_", "_Hips/", "_Leg_" }
                : new[] { "_Torso/", "_Arm_", "_Hips/", "_Leg_" };
            foreach (Renderer renderer in
                trainer.GetComponentsInChildren<Renderer>(true))
            {
                // Trainers are intentionally styled while the clone root is
                // inactive, before its first rendered frame. activeInHierarchy
                // is therefore false for every valid outfit renderer here.
                if (renderer == null || !renderer.gameObject.activeSelf)
                    continue;
                string path = BuildPath(renderer.transform);
                if (parts.Any(part => path.Contains(part,
                    StringComparison.Ordinal)))
                    ApplyPalette(renderer, color);
            }
        }

        private static void TintNamedPart(GameObject trainer, string name,
            Color color)
        {
            Renderer renderer = trainer.GetComponentsInChildren<Renderer>(true)
                .FirstOrDefault(value => value != null &&
                    string.Equals(value.name, name,
                        StringComparison.OrdinalIgnoreCase));
            if (renderer != null) ApplyPalette(renderer, color);
        }

        private static void ApplyPalette(Renderer renderer, Color color)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            foreach (int property in ColorProperties)
                block.SetColor(property, color);
            renderer.SetPropertyBlock(block);
            // Some modular-character shaders ignore property blocks for their
            // cloth channels. renderer.materials gives this clone private
            // instances, so force every recognized channel without recoloring
            // the adjacent source trainer that supplied the prefab.
            foreach (Material material in renderer.materials)
            {
                if (material == null) continue;
                material.color = color;
                foreach (int property in ColorProperties)
                    if (material.HasProperty(property))
                        material.SetColor(property, color);
            }
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
    }

}
