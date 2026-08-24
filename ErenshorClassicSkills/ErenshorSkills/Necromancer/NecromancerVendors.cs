using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace ErenshorSkills.Necromancer
{
    internal sealed class NecromancerVendorMarker : MonoBehaviour
    {
        internal string SceneName;
        internal string SourceVendorName;
        internal string SourceVendorRole;
        internal int SourceVendorScore;
        internal bool SourceIsArcanistTrainer;
        internal bool ClassAppearanceReady;
        internal string ClassAppearanceDescription;
        internal string PlacementDescription;
        internal Vector3 SourcePosition;
    }

    /// <summary>
    /// Adds a native NPC-based spell merchant beside an existing merchant in
    /// each required city. Cloning a scene-local merchant preserves Erenshor's
    /// model, nameplate, hail, vendor window, audio, and navigation behavior.
    /// </summary>
    internal static class NecromancerVendors
    {
        private static readonly HashSet<string> SupportedScenes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Stowaway", "Willowwatch", "Azure" };

        internal static int SpawnedVendorCount =>
            UnityEngine.Object.FindObjectsOfType<NecromancerVendorMarker>().Length;

        internal static bool ValidateCurrentSceneVendor(out string detail)
        {
            string scene = UnityEngine.SceneManagement.SceneManager
                .GetActiveScene().name;
            NecromancerVendorMarker[] markers = UnityEngine.Object
                .FindObjectsOfType<NecromancerVendorMarker>();
            if (!SupportedScenes.Contains(scene))
            {
                detail = $"scene {scene} does not require a Necromancer vendor";
                return true;
            }
            if (markers.Length != 1)
            {
                detail = $"expected one vendor in {scene}, found {markers.Length}";
                return false;
            }
            VendorInventory vendor = markers[0].GetComponent<VendorInventory>();
            List<Item> expected = NecromancerCatalog.GetVendorStock().ToList();
            bool exact = vendor?.ItemsForSale != null &&
                vendor.ItemsForSale.Count == expected.Count;
            if (exact)
                for (int i = 0; i < expected.Count; i++)
                    exact &= ReferenceEquals(vendor.ItemsForSale[i], expected[i]);
            NecromancerVendorMarker marker = markers[0];
            float anchorDistance = Vector3.Distance(marker.SourcePosition,
                marker.transform.position);
            bool sensibleAnchor = !string.IsNullOrWhiteSpace(
                    marker.SourceVendorName) && anchorDistance >= 1.5f &&
                anchorDistance <= 5f && marker.SourceVendorScore >= 900 &&
                marker.SourceIsArcanistTrainer &&
                marker.ClassAppearanceReady;
            List<string> overLevel = ClassMerchantStock
                .FindViolations(vendor?.ItemsForSale).ToList();
            detail = exact && sensibleAnchor && overLevel.Count == 0
                ? $"one native merchant with {expected.Count} level-sorted spells and auras; " +
                    $"anchored {anchorDistance:0.0}m from {marker.SourceVendorName} " +
                    $"(Arcanist trainer; {marker.SourceVendorRole}, arcane score " +
                    $"{marker.SourceVendorScore}) via {marker.PlacementDescription}" +
                    $"; no weapon or armor above level " +
                    $"{ClassMerchantStock.MaxEquipmentLevel} is stocked"
                : !exact
                    ? "merchant inventory is missing, duplicated, or out of order"
                    : overLevel.Count > 0
                        ? "merchant stocks weapons or armor above level " +
                            ClassMerchantStock.MaxEquipmentLevel + ": " +
                            string.Join(", ", overLevel)
                        : $"merchant anchor is invalid (source=" +
                            $"{marker.SourceVendorName ?? "<none>"}, role=" +
                            $"{marker.SourceVendorRole ?? "<none>"}, arcane score=" +
                            $"{marker.SourceVendorScore}, distance=" +
                            $"{anchorDistance:0.0}m)";
            return exact && sensibleAnchor && overLevel.Count == 0;
        }

        internal static void OnSceneLoaded(string sceneName)
        {
            if (SkillsPlugin.CfgEnableNecromancer?.Value != true ||
                !SupportedScenes.Contains(sceneName) || SkillsPlugin.Instance == null)
                return;
            SkillsPlugin.Instance.StartCoroutine(SpawnWhenReady(sceneName));
        }

        private static IEnumerator SpawnWhenReady(string sceneName)
        {
            const float overallTimeoutSeconds = 120f;
            const int stableFramesRequired = 30;
            float startedAt = Time.realtimeSinceStartup;
            int lastVendorCount = -1;
            int stableFrames = 0;
            while (Time.realtimeSinceStartup - startedAt <
                overallTimeoutSeconds)
            {
                if (!string.Equals(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    sceneName, StringComparison.OrdinalIgnoreCase)) yield break;
                NecromancerCatalog.TryInitialize();
                if (NecromancerCatalog.IsReady)
                {
                    if (UnityEngine.Object.FindObjectsOfType<NecromancerVendorMarker>()
                        .Any(marker => string.Equals(marker.SceneName, sceneName,
                            StringComparison.OrdinalIgnoreCase))) yield break;

                    int vendorCount = GetEligibleSourceVendors().Count();
                    if (vendorCount == lastVendorCount)
                        stableFrames++;
                    else
                    {
                        lastVendorCount = vendorCount;
                        stableFrames = 0;
                    }
                    VendorInventory source = SelectSourceVendor(sceneName);
                    bool arcanistTrainerReady = IsArcanistTrainer(source) &&
                        stableFrames >= stableFramesRequired;
                    if (arcanistTrainerReady)
                    {
                        SpawnVendor(source, sceneName);
                        yield break;
                    }
                }
                yield return null;
            }
            SkillsPlugin.Log.LogWarning(
                $"Necromancer vendor could not find a native merchant in '{sceneName}'.");
        }

        private static VendorInventory SelectSourceVendor(string sceneName)
        {
            return GetEligibleSourceVendors()
                .OrderByDescending(GetArcaneVendorScore)
                .ThenBy(value => value.GetComponent<NPC>().SimPlayer)
                .ThenBy(value => value.transform.name,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static IEnumerable<VendorInventory> GetEligibleSourceVendors()
        {
            return UnityEngine.Object.FindObjectsOfType<VendorInventory>()
                .Where(value => value != null &&
                    value.GetComponent<NPC>() != null &&
                    value.GetComponent<NecromancerVendorMarker>() == null &&
                    value.GetComponent<Monk.MonkVendorMarker>() == null);
        }

        private static int GetArcaneVendorScore(VendorInventory vendor)
        {
            if (vendor == null) return int.MinValue;
            NPC npc = vendor.GetComponent<NPC>();
            if (npc == null || npc.SimPlayer) return int.MinValue + 1;

            string identity = ((npc.NPCName ?? "") + " " +
                (vendor.VendorDesc ?? "") + " " +
                (vendor.transform.name ?? "")).ToLowerInvariant();
            int score = 0;
            if (IsArcanistTrainer(vendor)) score += 5000;
            if (identity.Contains("spell")) score += 900;
            if (identity.Contains("research")) score += 850;
            if (identity.Contains("magic") || identity.Contains("arcane"))
                score += 700;
            if (identity.Contains("scroll") || identity.Contains("mystic") ||
                identity.Contains("enchant")) score += 500;
            if (identity.Contains("alchemy") || identity.Contains("potion"))
                score += 250;
            int spellScrolls = vendor.ItemsForSale?.Count(item => item != null &&
                (item.ItemName ?? "").IndexOf("Spell Scroll",
                    StringComparison.OrdinalIgnoreCase) >= 0) ?? 0;
            score += Mathf.Min(spellScrolls, 30) * 80;
            return score;
        }

        private static bool IsArcanistTrainer(VendorInventory vendor)
        {
            if (vendor == null) return false;
            NPC npc = vendor.GetComponent<NPC>();
            string identity = ((npc?.NPCName ?? "") + " " +
                (vendor.VendorDesc ?? "") + " " +
                (vendor.transform.name ?? "")).ToLowerInvariant();
            if (identity.Contains("arcanist")) return true;
            Class arcanist = GameData.ClassDB?.Arcanist;
            if (arcanist == null || vendor.ItemsForSale == null) return false;
            return vendor.ItemsForSale.Count(item => item?.TeachSpell?.UsedBy !=
                null && item.TeachSpell.UsedBy.Contains(arcanist)) >= 3;
        }

        private static void SpawnVendor(VendorInventory source, string sceneName)
        {
            Vector3 position = ChooseOpenPlacement(source,
                out string placementDescription);

            GameObject clone = UnityEngine.Object.Instantiate(source.gameObject,
                position, source.transform.rotation);
            clone.SetActive(false);
            RemoveInheritedInteraction(clone);

            string vendorName = GetVendorName(sceneName);
            clone.name = vendorName;
            NPC npc = clone.GetComponent<NPC>();
            Character character = clone.GetComponent<Character>();
            Stats stats = clone.GetComponent<Stats>();
            VendorInventory inventory = clone.GetComponent<VendorInventory>();
            if (inventory == null) inventory = clone.AddComponent<VendorInventory>();
            inventory.ItemsForSale = new List<Item>(
                NecromancerCatalog.GetVendorStock());
            inventory.QuestRewardsForSale = new List<Quest>();
            inventory.VendorDesc = "Necromancer Spells and Auras";

            if (npc != null)
            {
                npc.NPCName = vendorName;
                npc.Mobile = false;
                npc.noClick = false;
                npc.SimPlayer = false;
                npc.InGroup = false;
                npc.CurrentAggroTarget = null;
                npc.NeverAggro = true;
                npc.AggroTable = new List<AggroSlot>();
                npc.MyBuffSpells = new List<Spell>();
                npc.MyAttackSpells = new List<Spell>();
                npc.MyHealSpells = new List<Spell>();
                npc.MyCCSpells = new List<Spell>();
                npc.MyTauntSpell = new List<Spell>();
                npc.GroupHeals = new List<Spell>();
                npc.MyAttackSkills = new List<Skill>();
                npc.SpawnWithStatus = null;
                npc.BaseAtkDmg = 0;
            }
            if (stats != null)
            {
                // A Unity clone can inherit the source merchant's serialized
                // runtime status slots. A shopkeeper must start with its own
                // clean slots so native Stats.TickEffects never sees a stale
                // or partially cloned effect reference.
                stats.StatusIcons = new List<StatusEffectIcon>();
                for (int index = 0; index < stats.StatusEffects.Length; index++)
                    stats.StatusEffects[index] = new StatusEffect(null,
                        false, false, null);
            }
            if (character != null)
            {
                character.isVendor = true;
                character.Invulnerable = true;
                character.AggroRange = 0f;
                character.MyFaction = Character.Faction.Villager;
                character.BaseFaction = Character.Faction.Villager;
                character.TempFaction = Character.Faction.Villager;
                character.AggressiveTowards = new List<Character.Faction>();
                character.Allies = new List<Character.Faction>
                    { Character.Faction.Player, Character.Faction.PC };
            }

            NPCDialog dialog = clone.AddComponent<NPCDialog>();
            dialog.Dialog =
                "Power over death is learned one page at a time. I carry the " +
                "disciplines of the Necromancer.";
            dialog.KeywordToActivate = new List<string>();
            dialog.RepeatingQuestDialog = dialog.Dialog;
            NPCDialogManager manager = clone.AddComponent<NPCDialogManager>();
            manager.SpecificClass = new List<Class>
                { NecromancerCatalog.NecromancerClass };
            manager.Rejection =
                "These rites answer only to a Necromancer.";
            manager.EngageWithSims = false;

            NecromancerVendorMarker marker =
                clone.AddComponent<NecromancerVendorMarker>();
            marker.SceneName = sceneName;
            marker.SourceVendorName = GetSourceVendorName(source);
            marker.SourceVendorRole = string.IsNullOrWhiteSpace(source.VendorDesc)
                ? "native merchant" : source.VendorDesc.Trim();
            marker.SourceVendorScore = GetArcaneVendorScore(source);
            marker.SourceIsArcanistTrainer = IsArcanistTrainer(source);
            marker.PlacementDescription = placementDescription;
            marker.SourcePosition = source.transform.position;
            marker.ClassAppearanceReady =
                TrainerClassAppearance.ApplyNecromancer(clone,
                    out marker.ClassAppearanceDescription);
            clone.SetActive(true);
            SkillsPlugin.Log.LogInfo(
                $"Necromancer vendor '{vendorName}' spawned in {sceneName} with " +
                $"{inventory.ItemsForSale.Count} level-sorted spells and auras beside " +
                $"{marker.SourceVendorName} ({marker.SourceVendorRole}); " +
                $"arcanistTrainer={marker.SourceIsArcanistTrainer}; " +
                $"arcaneScore={marker.SourceVendorScore}; " +
                $"placement={placementDescription}; position={position}; " +
                $"appearance={marker.ClassAppearanceReady} " +
                $"({marker.ClassAppearanceDescription}).");
        }

        private static Vector3 ChooseOpenPlacement(VendorInventory source,
            out string description)
        {
            var candidates = new (Vector3 Offset, string Label)[]
            {
                (new Vector3(2.5f, 0f, 0f), "merchant right side"),
                (new Vector3(-2.5f, 0f, 0f), "merchant left side"),
                (new Vector3(2.25f, 0f, -1.25f), "merchant right-rear side"),
                (new Vector3(-2.25f, 0f, -1.25f), "merchant left-rear side"),
                (new Vector3(2.25f, 0f, 1.25f), "merchant right-front side"),
                (new Vector3(-2.25f, 0f, 1.25f), "merchant left-front side")
            };
            Character[] characters = UnityEngine.Object
                .FindObjectsOfType<Character>();
            Vector3 best = source.transform.position +
                source.transform.right * 2.5f;
            float bestScore = float.MinValue;
            description = "merchant right side fallback";

            for (int index = 0; index < candidates.Length; index++)
            {
                Vector3 requested = source.transform.TransformPoint(
                    candidates[index].Offset);
                Vector3 resolved = GameData.GetSafeNavMeshPoint(requested);
                if (NavMesh.SamplePosition(requested + Vector3.up,
                    out NavMeshHit hit, 2.5f, NavMesh.AllAreas) &&
                    Mathf.Abs(hit.position.y - requested.y) <= 1.5f)
                    resolved = hit.position;

                float sourceDistance = Vector3.Distance(
                    source.transform.position, resolved);
                if (sourceDistance < 1.5f || sourceDistance > 5f) continue;
                float sampleError = Vector3.Distance(requested, resolved);
                if (sampleError > 1.5f) continue;

                float clearance = characters
                    .Where(character => character != null &&
                        character.gameObject != source.gameObject)
                    .Select(character => Vector3.Distance(
                        character.transform.position, resolved))
                    .DefaultIfEmpty(4f).Min();
                float score = 100f - index * 4f - sampleError * 20f +
                    Mathf.Min(clearance, 4f) * 7f;
                if (clearance < 1.25f) score -= 120f;
                if (score <= bestScore) continue;
                bestScore = score;
                best = resolved;
                description = candidates[index].Label;
            }
            return best;
        }

        private static string GetSourceVendorName(VendorInventory source)
        {
            string npcName = source?.GetComponent<NPC>()?.NPCName;
            return string.IsNullOrWhiteSpace(npcName)
                ? source?.transform.name ?? "native merchant" : npcName.Trim();
        }

        internal static string DescribeCurrentScenePlacement()
        {
            NecromancerVendorMarker marker = UnityEngine.Object
                .FindObjectOfType<NecromancerVendorMarker>();
            if (marker == null) return "no Necromancer vendor is present";
            return $"{marker.transform.name} at {marker.transform.position}; " +
                $"source={marker.SourceVendorName} at {marker.SourcePosition}; " +
                $"role={marker.SourceVendorRole}; " +
                $"arcanistTrainer={marker.SourceIsArcanistTrainer}; " +
                $"classAppearance={marker.ClassAppearanceReady} " +
                $"({marker.ClassAppearanceDescription}); " +
                $"arcaneScore={marker.SourceVendorScore}; " +
                $"placement={marker.PlacementDescription}; distance=" +
                $"{Vector3.Distance(marker.SourcePosition, marker.transform.position):0.0}m";
        }

        internal static IEnumerable<string> DescribeNativeVendors()
        {
            return UnityEngine.Object.FindObjectsOfType<VendorInventory>()
                .Where(value => value != null &&
                    value.GetComponent<NecromancerVendorMarker>() == null &&
                    value.GetComponent<NPC>() != null)
                .OrderByDescending(GetArcaneVendorScore)
                .ThenBy(value => GetSourceVendorName(value),
                    StringComparer.OrdinalIgnoreCase)
                .Select(value => $"{GetSourceVendorName(value)} | " +
                    $"role={value.VendorDesc ?? ""} | score=" +
                    $"{GetArcaneVendorScore(value)} | position=" +
                    $"{value.transform.position} | stock=" +
                    $"{value.ItemsForSale?.Count ?? 0}");
        }

        private static void RemoveInheritedInteraction(GameObject clone)
        {
            foreach (NPCDialogManager manager in
                clone.GetComponents<NPCDialogManager>())
                UnityEngine.Object.DestroyImmediate(manager);
            foreach (NPCDialog dialog in clone.GetComponents<NPCDialog>())
                UnityEngine.Object.DestroyImmediate(dialog);
            foreach (QuestManager manager in clone.GetComponents<QuestManager>())
                UnityEngine.Object.DestroyImmediate(manager);
            VendorInventory inherited = clone.GetComponent<VendorInventory>();
            if (inherited != null)
            {
                inherited.ItemsForSale = new List<Item>();
                inherited.QuestRewardsForSale = new List<Quest>();
            }
        }

        private static string GetVendorName(string sceneName)
        {
            switch (sceneName)
            {
                case "Stowaway": return "Ossira the Bone Reader";
                case "Willowwatch": return "Morthane the Grave Tutor";
                default: return "Veyra of the Azure Crypt";
            }
        }
    }
}
