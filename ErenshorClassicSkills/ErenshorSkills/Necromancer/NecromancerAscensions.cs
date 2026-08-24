using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills.Necromancer
{
    /// <summary>
    /// Necromancer-specific Ascensions. Erenshor's enum and class-tab switch
    /// predate addon classes, so the catalog uses a reserved enum value and the
    /// small Harmony adapters below extend only the missing Necromancer branch.
    /// Save data remains native AscensionSkillEntry IDs.
    /// </summary>
    internal static class NecromancerAscensions
    {
        // The class tab shows eight slots, matching every native class.
        internal const int ExpectedCount = 8;
        internal const string GraveMasteryId = "CS_NECRO_AA_GRAVE_MASTERY";
        internal const string VirulentAfflictionsId =
            "CS_NECRO_AA_VIRULENT_AFFLICTIONS";
        internal const string BoneSovereigntyId =
            "CS_NECRO_AA_BONE_SOVEREIGNTY";
        internal const string UnyieldingAfflictionId =
            "CS_NECRO_AA_UNYIELDING_AFFLICTION";
        internal const string SwiftDecayId = "CS_NECRO_AA_SWIFT_DECAY";
        internal const string ShroudOfTheGraveId =
            "CS_NECRO_AA_SHROUD_OF_THE_GRAVE";
        internal const string DeathwardId = "CS_NECRO_AA_DEATHWARD";
        internal const string DefyTheGraveId = "CS_NECRO_AA_DEFY_THE_GRAVE";

        // Native mechanics look up these IDs directly. Mapping their queries
        // to our IDs reuses the exact, already-balanced game implementations
        // while keeping Necromancer ranks independently saved and displayed.
        private static readonly Dictionary<string, string> NativeHookToCustom =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "71342250", GraveMasteryId },       // +5% spell damage/rank
                { "19108265", VirulentAfflictionsId }, // 33% DoT crit/rank
                { "34995747", BoneSovereigntyId },    // +1 pet level/rank
                { "5805602", UnyieldingAfflictionId }, // +10% resist bypass/rank
                { "7758218", SwiftDecayId },          // -10% cooldown/rank
                { "4886685", ShroudOfTheGraveId },    // +3% all resists/rank
                { "14210880", DeathwardId },          // -5% damage taken/rank
                { "73824309", DefyTheGraveId }        // 10%/rank cheat death
            };

        private static readonly List<Ascension> Registered =
            new List<Ascension>();
        private static SkillDB _registeredDatabase;

        internal static IReadOnlyList<Ascension> All => Registered;

        internal static bool Register()
        {
            SkillDB database = GameData.SkillDatabase;
            if (database?.AscensionDatabase == null) return false;
            if (ReferenceEquals(database, _registeredDatabase) &&
                Registered.Count == ExpectedCount && Registered.All(value =>
                    value != null && database.GetAscensionByID(value.Id) == value))
                return true;

            var merged = database.AscensionDatabase
                .Where(value => value != null && !IsCustomId(value.Id))
                .ToList();
            Registered.Clear();
            Registered.Add(Create(GraveMasteryId, "Grave Mastery",
                "All damaging spells deal 5% more damage per rank.", 5,
                3,
                value => value.IntelligenceScaling = 2f));
            Registered.Add(Create(VirulentAfflictionsId,
                "Virulent Afflictions",
                "Every damage-over-time tick has a 33% chance to critically hit.",
                4, 1, value => value.CriticalDotChance = 33f));
            Registered.Add(Create(BoneSovereigntyId, "Bone Sovereignty",
                "Bone Thralls are 1 level higher per rank, with level-appropriate health and offense.",
                3, 3, value => value.SummonedBeastEnhancement = 1f));
            Registered.Add(Create(UnyieldingAfflictionId,
                "Unyielding Affliction",
                "Increase the chance for your spells to bypass resists by 10% per rank.",
                4, 3, value => value.ResistModIncrease = 10f));
            Registered.Add(Create(SwiftDecayId, "Swift Decay",
                "Necromancer spell cooldowns are 10% shorter per rank.", 3,
                3,
                value => value.CooldownReduction = 10f));
            Registered.Add(Create(ShroudOfTheGraveId, "Shroud of the Grave",
                "Grave-cold wraps you: all resistances increase by 3% per rank.",
                3, 3, value =>
                {
                    value.MR = 3f;
                    value.PR = 3f;
                    value.ER = 3f;
                    value.VR = 3f;
                }));
            Registered.Add(Create(DeathwardId, "Deathward",
                "Wards of bone turn aside 5% of all incoming damage per rank.",
                4, 3, value => value.MitigationIncrease = 5f));
            Registered.Add(Create(DefyTheGraveId, "Defy the Grave",
                "When a killing blow lands you have a 10% chance per rank to " +
                "refuse it, restoring your health and mana and shrugging off " +
                "stuns and fear.", 5, 3, value => value.AdvancedIncreaseHP = 1f));
            merged.AddRange(Registered);
            database.AscensionDatabase = merged.ToArray();
            _registeredDatabase = database;
            SkillsPlugin.Log?.LogInfo(
                $"Necromancer: registered {Registered.Count} Ascension perks.");
            return true;
        }

        private static Ascension Create(string id, string name,
            string description, int weight, int maxRank,
            Action<Ascension> configure)
        {
            var value = ScriptableObject.CreateInstance<Ascension>();
            value.name = "CS NEC AA - " + name;
            value.Id = id;
            value.SkillName = name;
            value.SkillDesc = description;
            value.MaxRank = maxRank;
            value.SimPlayerWeight = weight;
            value.UsedBy = (Ascension.Class)7;
            value.hideFlags = HideFlags.HideAndDontSave;
            configure?.Invoke(value);
            return value;
        }

        internal static bool IsCustomId(string id)
        {
            return !string.IsNullOrEmpty(id) &&
                id.StartsWith("CS_NECRO_AA_", StringComparison.Ordinal);
        }

        internal static int GetRank(UseSkill skills, string customId)
        {
            if (skills?.MyAscensions == null || string.IsNullOrEmpty(customId))
                return 0;
            AscensionSkillEntry entry = skills.MyAscensions.FirstOrDefault(value =>
                value != null && string.Equals(value.id, customId,
                    StringComparison.Ordinal));
            int maxRank = Registered.FirstOrDefault(value => value != null &&
                string.Equals(value.Id, customId,
                    StringComparison.Ordinal))?.MaxRank ?? 3;
            int savedRank = entry?.level ?? 0;
            if (entry != null && savedRank > maxRank &&
                IsNecromancerSkills(skills))
            {
                int refund = savedRank - maxRank;
                entry.level = maxRank;
                skills.AscensionPoints += refund;
                SkillsPlugin.Log?.LogInfo(
                    $"Necromancer: capped {customId} at rank {maxRank} and " +
                    $"refunded {refund} Ascension point(s).");
                savedRank = maxRank;
            }
            return Mathf.Clamp(savedRank, 0, maxRank);
        }

        internal static int GetPlayerRank(string customId)
        {
            return GetRank(GameData.PlayerControl?.Myself?.MySkills, customId);
        }

        internal static bool TryMapNativeHook(string nativeId,
            out string customId)
        {
            return NativeHookToCustom.TryGetValue(nativeId ?? "",
                out customId);
        }

        internal static bool IsNecromancerSkills(UseSkill skills)
        {
            Stats stats = skills?.GetComponent<Stats>();
            return NecromancerCatalog.IsNecromancerClass(stats?.CharacterClass);
        }

        internal static bool Validate(out string detail)
        {
            var errors = new List<string>();
            if (Registered.Count != ExpectedCount)
                errors.Add($"registered count {Registered.Count}");
            if (Registered.Select(value => value.Id)
                .Distinct(StringComparer.Ordinal).Count() != ExpectedCount)
                errors.Add("duplicate IDs");
            foreach (Ascension value in Registered)
            {
                int expectedMaxRank = value?.Id == VirulentAfflictionsId
                    ? 1 : 3;
                if (value == null || string.IsNullOrWhiteSpace(value.Id) ||
                    string.IsNullOrWhiteSpace(value.SkillName) ||
                    string.IsNullOrWhiteSpace(value.SkillDesc) ||
                    value.MaxRank != expectedMaxRank ||
                    (int)value.UsedBy != 7)
                    errors.Add(value?.SkillName ?? "null perk");
                if (GameData.SkillDatabase?.GetAscensionByID(value?.Id) != value)
                    errors.Add($"database lookup {value?.SkillName}");
            }
            if (NativeHookToCustom.Count != ExpectedCount ||
                NativeHookToCustom.Values.Any(id =>
                    Registered.All(value => value.Id != id)))
                errors.Add("native effect bridge");
            detail = errors.Count == 0
                ? "eight persistent class perks; native damage, one-rank 33% DoT-critical cap, pet-level, resist, cooldown, all-resistance, damage-reduction, and cheat-death hooks mapped"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }
    }

    [HarmonyPatch(typeof(SkillDB), "Start")]
    internal static class Patch_NecromancerAscensionDatabase
    {
        private static void Postfix()
        {
            if (SkillsPlugin.CfgEnableNecromancer?.Value == true)
                NecromancerAscensions.Register();
        }
    }

    [HarmonyPatch(typeof(UseSkill), nameof(UseSkill.GetAscensionRank))]
    internal static class Patch_NecromancerAscensionEffects
    {
        private static void Postfix(UseSkill __instance, string _id,
            ref int __result)
        {
            if (!NecromancerAscensions.IsNecromancerSkills(__instance) ||
                !NecromancerAscensions.TryMapNativeHook(_id,
                    out string customId)) return;
            __result = Mathf.Max(__result,
                NecromancerAscensions.GetRank(__instance, customId));
        }
    }

    [HarmonyPatch(typeof(AAScreen), "CountSpent")]
    internal static class Patch_NecromancerAscensionSpent
    {
        private static void Postfix(AAScreen __instance,
            ref int ___spentPoints)
        {
            if (!NecromancerCatalog.IsNecromancerPlayer()) return;
            UseSkill skills = GameData.PlayerControl?.Myself?.MySkills;
            ___spentPoints += NecromancerAscensions.All.Sum(value =>
                NecromancerAscensions.GetRank(skills, value.Id));
            if (__instance?.spent != null)
                __instance.spent.text = "Points spent: " + ___spentPoints;
        }
    }

    [HarmonyPatch(typeof(AAScreen), nameof(AAScreen.LoadAllAAButtonData))]
    internal static class Patch_NecromancerAscensionClassTab
    {
        private static void Postfix(AAScreen __instance,
            bool ___classSpecific)
        {
            if (!___classSpecific || __instance?.AllAASlots == null ||
                !NecromancerCatalog.IsNecromancerPlayer()) return;
            UseSkill skills = GameData.PlayerControl?.Myself?.MySkills;
            int count = Mathf.Min(__instance.AllAASlots.Count,
                NecromancerAscensions.All.Count);
            for (int i = 0; i < count; i++)
            {
                Ascension perk = NecromancerAscensions.All[i];
                AAButton slot = __instance.AllAASlots[i];
                if (slot == null) continue;
                slot.Name.text = perk.SkillName;
                slot.SkillDescription = perk.SkillDesc;
                slot.id = perk.Id;
                slot.MaxLevel.text = " / " + perk.MaxRank;
                slot.Level.text = NecromancerAscensions.GetRank(
                    skills, perk.Id).ToString();
            }
        }
    }
}
