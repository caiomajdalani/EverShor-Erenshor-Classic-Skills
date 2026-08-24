using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace ErenshorSkills.Monk
{
    internal sealed class MonkVendorMarker : MonoBehaviour
    {
        internal string SceneName;
        internal string SourceVendorName;
        internal string SourceVendorRole;
        internal int SourceVendorScore;
        internal bool SourceIsReaverTrainer;
        internal bool ClassAppearanceReady;
        internal string ClassAppearanceDescription;
        internal string PlacementDescription;
        internal Vector3 SourcePosition;
    }

    /// <summary>
    /// Adds one native-looking martial trainer beside an existing weapon,
    /// combat, or skill merchant in each required city. Scene-local cloning
    /// preserves Erenshor's model, nameplate, hail, shop UI, and navigation.
    /// </summary>
    internal static class MonkVendors
    {
        private static readonly HashSet<string> SupportedScenes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Stowaway", "Willowwatch", "Azure" };

        internal static int SpawnedVendorCount => UnityEngine.Object
            .FindObjectsOfType<MonkVendorMarker>().Length;

        internal static void OnSceneLoaded(string sceneName)
        {
            if (SkillsPlugin.CfgEnableMonk?.Value != true ||
                !SupportedScenes.Contains(sceneName) ||
                SkillsPlugin.Instance == null) return;
            SkillsPlugin.Instance.StartCoroutine(SpawnWhenReady(sceneName));
        }

        private static IEnumerator SpawnWhenReady(string sceneName)
        {
            const float overallTimeout = 120f;
            const int stableFramesRequired = 30;
            float started = Time.realtimeSinceStartup;
            int lastCount = -1;
            int stableFrames = 0;
            while (Time.realtimeSinceStartup - started < overallTimeout)
            {
                if (!string.Equals(UnityEngine.SceneManagement.SceneManager
                        .GetActiveScene().name, sceneName,
                        StringComparison.OrdinalIgnoreCase)) yield break;
                MonkCatalog.TryInitialize();
                if (MonkCatalog.IsReady)
                {
                    if (UnityEngine.Object.FindObjectsOfType<MonkVendorMarker>()
                        .Any(marker => string.Equals(marker.SceneName,
                            sceneName, StringComparison.OrdinalIgnoreCase)))
                        yield break;
                    int count = GetEligibleSources().Count();
                    if (count == lastCount) stableFrames++;
                    else
                    {
                        lastCount = count;
                        stableFrames = 0;
                    }
                    VendorInventory source = SelectSource();
                    bool reaverTrainerReady = IsReaverTrainer(source) &&
                        stableFrames >= stableFramesRequired;
                    if (reaverTrainerReady)
                    {
                        SpawnVendor(source, sceneName);
                        yield break;
                    }
                }
                yield return null;
            }
            SkillsPlugin.Log?.LogWarning(
                $"Monk trainer could not find a native merchant in '{sceneName}'.");
        }

        private static VendorInventory SelectSource()
        {
            return GetEligibleSources()
                .OrderByDescending(GetMartialVendorScore)
                .ThenBy(value => value.GetComponent<NPC>().SimPlayer)
                .ThenBy(value => value.transform.name,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static IEnumerable<VendorInventory> GetEligibleSources()
        {
            return UnityEngine.Object.FindObjectsOfType<VendorInventory>()
                .Where(value => value != null &&
                    value.GetComponent<NPC>() != null &&
                    value.GetComponent<MonkVendorMarker>() == null &&
                    value.GetComponent<Necromancer.NecromancerVendorMarker>() ==
                        null);
        }

        private static int GetMartialVendorScore(VendorInventory vendor)
        {
            if (vendor == null) return int.MinValue;
            NPC npc = vendor.GetComponent<NPC>();
            if (npc == null || npc.SimPlayer) return int.MinValue + 1;
            string identity = ((npc.NPCName ?? "") + " " +
                (vendor.VendorDesc ?? "") + " " +
                (vendor.transform.name ?? "")).ToLowerInvariant();
            int score = 0;
            if (IsReaverTrainer(vendor)) score += 5000;
            if (identity.Contains("skill") || identity.Contains("trainer"))
                score += 950;
            if (identity.Contains("weapon") || identity.Contains("armory"))
                score += 900;
            if (identity.Contains("combat") || identity.Contains("martial"))
                score += 800;
            if (identity.Contains("windblade") ||
                identity.Contains("duelist")) score += 700;
            if (identity.Contains("armor") || identity.Contains("smith"))
                score += 350;
            int skillBooks = vendor.ItemsForSale?.Count(item => item != null &&
                item.TeachSkill != null) ?? 0;
            int weapons = vendor.ItemsForSale?.Count(item => item != null &&
                item.ThisWeaponType != Item.WeaponType.None) ?? 0;
            score += Mathf.Min(skillBooks, 20) * 90;
            score += Mathf.Min(weapons, 20) * 45;
            return score;
        }

        private static bool IsReaverTrainer(VendorInventory vendor)
        {
            if (vendor == null) return false;
            NPC npc = vendor.GetComponent<NPC>();
            string identity = ((npc?.NPCName ?? "") + " " +
                (vendor.VendorDesc ?? "") + " " +
                (vendor.transform.name ?? "")).ToLowerInvariant();
            if (identity.Contains("reaver")) return true;
            Class reaver = GameData.ClassDB?.Reaver;
            if (reaver == null || vendor.ItemsForSale == null) return false;
            return vendor.ItemsForSale.Count(item => item?.TeachSpell?.UsedBy !=
                null && item.TeachSpell.UsedBy.Contains(reaver)) >= 3;
        }

        private static void SpawnVendor(VendorInventory source,
            string sceneName)
        {
            if (source == null) return;
            Vector3 position = ChooseOpenPlacement(source,
                out string placement);
            GameObject clone = UnityEngine.Object.Instantiate(
                source.gameObject, position, source.transform.rotation);
            clone.SetActive(false);
            RemoveInheritedInteraction(clone);

            string vendorName = GetVendorName(sceneName);
            clone.name = vendorName;
            NPC npc = clone.GetComponent<NPC>();
            Character character = clone.GetComponent<Character>();
            Stats stats = clone.GetComponent<Stats>();
            VendorInventory inventory = clone.GetComponent<VendorInventory>();
            if (inventory == null)
                inventory = clone.AddComponent<VendorInventory>();
            inventory.ItemsForSale = new List<Item>(
                MonkCatalog.GetVendorStock());
            inventory.QuestRewardsForSale = new List<Quest>();
            inventory.VendorDesc = "Monk Techniques, Auras, and Handwraps";

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
                stats.StatusIcons = new List<StatusEffectIcon>();
                for (int index = 0; index < stats.StatusEffects.Length;
                    index++)
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
                character.AggressiveTowards =
                    new List<Character.Faction>();
                character.Allies = new List<Character.Faction>
                    { Character.Faction.Player, Character.Faction.PC };
            }

            NPCDialog dialog = clone.AddComponent<NPCDialog>();
            dialog.Dialog = "Still the breath, root the stance, and let every " +
                "motion have purpose. I can teach the disciplines of the Monk.";
            dialog.KeywordToActivate = new List<string>();
            dialog.RepeatingQuestDialog = dialog.Dialog;
            NPCDialogManager manager = clone.AddComponent<NPCDialogManager>();
            manager.SpecificClass = new List<Class> { MonkCatalog.MonkClass };
            manager.Rejection =
                "These disciplines require the training of a Monk.";
            manager.EngageWithSims = false;

            MonkVendorMarker marker = clone.AddComponent<MonkVendorMarker>();
            marker.SceneName = sceneName;
            marker.SourceVendorName = GetSourceName(source);
            marker.SourceVendorRole = string.IsNullOrWhiteSpace(
                source.VendorDesc) ? "native merchant" :
                source.VendorDesc.Trim();
            marker.SourceVendorScore = GetMartialVendorScore(source);
            marker.SourceIsReaverTrainer = IsReaverTrainer(source);
            marker.PlacementDescription = placement;
            marker.SourcePosition = source.transform.position;
            marker.ClassAppearanceReady = TrainerClassAppearance.ApplyMonk(
                clone, out marker.ClassAppearanceDescription);
            clone.SetActive(true);
            SkillsPlugin.Log?.LogInfo(
                $"Monk trainer '{vendorName}' spawned in {sceneName} with " +
                $"{inventory.ItemsForSale.Count} level-sorted techniques, " +
                $"auras, and class items beside {marker.SourceVendorName} " +
                $"({marker.SourceVendorRole}); reaverTrainer=" +
                $"{marker.SourceIsReaverTrainer}; martialScore=" +
                $"{marker.SourceVendorScore}; placement={placement}; " +
                $"position={position}; appearance=" +
                $"{marker.ClassAppearanceReady} " +
                $"({marker.ClassAppearanceDescription}).");
        }

        private static Vector3 ChooseOpenPlacement(VendorInventory source,
            out string description)
        {
            var candidates = new (Vector3 Offset, string Label)[]
            {
                (new Vector3(-2.5f, 0f, 0f), "merchant left side"),
                (new Vector3(2.5f, 0f, 0f), "merchant right side"),
                (new Vector3(-2.25f, 0f, 1.25f),
                    "merchant left-front side"),
                (new Vector3(2.25f, 0f, 1.25f),
                    "merchant right-front side"),
                (new Vector3(-2.25f, 0f, -1.25f),
                    "merchant left-rear side"),
                (new Vector3(2.25f, 0f, -1.25f),
                    "merchant right-rear side")
            };
            Character[] characters = UnityEngine.Object
                .FindObjectsOfType<Character>();
            Vector3 best = source.transform.position -
                source.transform.right * 2.5f;
            float bestScore = float.MinValue;
            description = "merchant left side fallback";
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
                float clearance = characters.Where(value => value != null &&
                        value.gameObject != source.gameObject)
                    .Select(value => Vector3.Distance(
                        value.transform.position, resolved))
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

        private static string GetSourceName(VendorInventory source)
        {
            string value = source?.GetComponent<NPC>()?.NPCName;
            return string.IsNullOrWhiteSpace(value)
                ? source?.transform.name ?? "native merchant" : value.Trim();
        }

        private static void RemoveInheritedInteraction(GameObject clone)
        {
            foreach (NPCDialogManager value in
                clone.GetComponents<NPCDialogManager>())
                UnityEngine.Object.DestroyImmediate(value);
            foreach (NPCDialog value in clone.GetComponents<NPCDialog>())
                UnityEngine.Object.DestroyImmediate(value);
            foreach (QuestManager value in clone.GetComponents<QuestManager>())
                UnityEngine.Object.DestroyImmediate(value);
            VendorInventory inherited = clone.GetComponent<VendorInventory>();
            if (inherited == null) return;
            inherited.ItemsForSale = new List<Item>();
            inherited.QuestRewardsForSale = new List<Quest>();
        }

        private static string GetVendorName(string sceneName)
        {
            switch (sceneName)
            {
                case "Stowaway": return "Master Ren of the Open Hand";
                case "Willowwatch": return "Sera Windstep";
                default: return "Grandmaster Kalen";
            }
        }

        internal static bool ValidateCurrentSceneVendor(out string detail)
        {
            string scene = UnityEngine.SceneManagement.SceneManager
                .GetActiveScene().name;
            if (!SupportedScenes.Contains(scene))
            {
                detail = $"scene {scene} does not require a Monk trainer";
                return true;
            }
            MonkVendorMarker[] markers = UnityEngine.Object
                .FindObjectsOfType<MonkVendorMarker>();
            if (markers.Length != 1)
            {
                detail = $"expected one trainer in {scene}, found " +
                    markers.Length;
                return false;
            }
            MonkVendorMarker marker = markers[0];
            VendorInventory vendor = marker.GetComponent<VendorInventory>();
            List<Item> expected = MonkCatalog.GetVendorStock().ToList();
            bool exact = vendor?.ItemsForSale != null &&
                vendor.ItemsForSale.Count == expected.Count;
            if (exact)
                for (int index = 0; index < expected.Count; index++)
                    exact &= ReferenceEquals(vendor.ItemsForSale[index],
                        expected[index]);
            float distance = Vector3.Distance(marker.SourcePosition,
                marker.transform.position);
            int bestAvailableScore = GetEligibleSources()
                .Select(GetMartialVendorScore)
                .DefaultIfEmpty(int.MinValue).Max();
            bool sensible = !string.IsNullOrWhiteSpace(
                    marker.SourceVendorName) && distance >= 1.5f &&
                distance <= 5f && marker.SourceVendorScore >= 0 &&
                marker.SourceVendorScore == bestAvailableScore &&
                marker.SourceIsReaverTrainer && marker.ClassAppearanceReady;
            List<string> overLevel = ClassMerchantStock
                .FindViolations(vendor?.ItemsForSale).ToList();
            detail = exact && sensible && overLevel.Count == 0
                ? $"one native trainer with {expected.Count} level-sorted " +
                  $"items, {distance:0.0}m from {marker.SourceVendorName} " +
                  $"(Reaver trainer; {marker.SourceVendorRole}, martial score " +
                  $"{marker.SourceVendorScore}) via " +
                  marker.PlacementDescription +
                  $"; no weapon or armor above level " +
                  $"{ClassMerchantStock.MaxEquipmentLevel} is stocked"
                : !exact
                    ? "trainer inventory is missing, duplicated, or unordered"
                    : overLevel.Count > 0
                        ? "trainer stocks weapons or armor above level " +
                          ClassMerchantStock.MaxEquipmentLevel + ": " +
                          string.Join(", ", overLevel)
                        : $"invalid trainer anchor (score=" +
                          $"{marker.SourceVendorScore}, " +
                          $"distance={distance:0.0}m)";
            return exact && sensible && overLevel.Count == 0;
        }

        internal static string DescribeCurrentScenePlacement()
        {
            MonkVendorMarker marker = UnityEngine.Object
                .FindObjectOfType<MonkVendorMarker>();
            if (marker == null) return "no Monk trainer is present";
            return $"{marker.transform.name} at {marker.transform.position}; " +
                $"source={marker.SourceVendorName} at " +
                $"{marker.SourcePosition}; role={marker.SourceVendorRole}; " +
                $"reaverTrainer={marker.SourceIsReaverTrainer}; " +
                $"classAppearance={marker.ClassAppearanceReady} " +
                $"({marker.ClassAppearanceDescription}); " +
                $"martialScore={marker.SourceVendorScore}; placement=" +
                $"{marker.PlacementDescription}; distance=" +
                $"{Vector3.Distance(marker.SourcePosition,
                    marker.transform.position):0.0}m";
        }
    }
}
