using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills.Monk
{
    internal static class MonkAscensions
    {
        // The class tab shows eight slots, matching every native class.
        internal const int ExpectedCount = 8;
        internal const string WayOfIronId = "CS_MONK_AA_WAY_OF_IRON";
        internal const string DeepMendId = "CS_MONK_AA_DEEP_MEND";
        internal const string FlowingDefenseId =
            "CS_MONK_AA_FLOWING_DEFENSE";
        internal const string LingeringFlurryId =
            "CS_MONK_AA_LINGERING_FLURRY";
        internal const string PerfectStillnessId =
            "CS_MONK_AA_PERFECT_STILLNESS";
        internal const string HundredHandsId = "CS_MONK_AA_HUNDRED_HANDS";
        internal const string DiamondSkinId = "CS_MONK_AA_DIAMOND_SKIN";
        internal const string IronBodyId = "CS_MONK_AA_IRON_BODY";

        // The first five perks are resolved by MonkRuntime itself. These three
        // instead borrow native mechanics by id, exactly as the Necromancer
        // does, so they reuse implementations the game has already balanced.
        // Patch_MonkAscensionEffects is class-gated, so mapping an id another
        // class also borrows cannot collide.
        private static readonly Dictionary<string, string> NativeHookToCustom =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "48388782", HundredHandsId },  // 10%/rank extra attack
                { "14210880", DiamondSkinId },   // -5% damage taken/rank
                { "4886685", IronBodyId }        // +3% all resists/rank
            };

        internal static bool TryMapNativeHook(string nativeId,
            out string customId)
        {
            return NativeHookToCustom.TryGetValue(nativeId ?? "",
                out customId);
        }

        private static readonly List<Ascension> Registered =
            new List<Ascension>();
        private static SkillDB _database;

        internal static IReadOnlyList<Ascension> All => Registered;

        internal static bool Register()
        {
            SkillDB database = GameData.SkillDatabase;
            if (database?.AscensionDatabase == null) return false;
            if (ReferenceEquals(database, _database) &&
                Registered.Count == ExpectedCount && Registered.All(value =>
                    database.GetAscensionByID(value.Id) == value))
                return true;

            var merged = database.AscensionDatabase.Where(value =>
                value != null && !IsCustomId(value.Id)).ToList();
            Registered.Clear();
            Registered.Add(Create(WayOfIronId, "Way of Iron",
                "Monk techniques deal 15% more damage per rank.", 5));
            Registered.Add(Create(DeepMendId, "Deep Mend",
                "Mend restores an additional 5% of maximum health per rank.",
                4));
            Registered.Add(Create(FlowingDefenseId, "Flowing Defense",
                "Stone Stance reduces incoming damage by an additional 3% per rank.",
                4));
            Registered.Add(Create(LingeringFlurryId, "Lingering Flurry",
                "Hundred Fists lasts 3 seconds longer per rank.", 3));
            Registered.Add(Create(PerfectStillnessId, "Perfect Stillness",
                "Feign Death lasts 3 seconds longer per rank.", 3));
            Registered.Add(Create(HundredHandsId, "Hundred Hands",
                "Your hands blur: 10% chance per rank to land an extra attack.",
                5));
            Registered.Add(Create(DiamondSkinId, "Diamond Skin",
                "Conditioning turns aside 5% of all incoming damage per rank.",
                4));
            Registered.Add(Create(IronBodyId, "Iron Body",
                "Breath control raises every resistance by 3% per rank.", 3));
            merged.AddRange(Registered);
            database.AscensionDatabase = merged.ToArray();
            _database = database;
            SkillsPlugin.Log?.LogInfo(
                $"Monk: registered {Registered.Count} persistent Ascension perks.");
            return true;
        }

        private static Ascension Create(string id, string name,
            string description, int weight)
        {
            var value = ScriptableObject.CreateInstance<Ascension>();
            value.name = "CS MONK AA - " + name;
            value.Id = id;
            value.SkillName = name;
            value.SkillDesc = description;
            value.MaxRank = 3;
            value.SimPlayerWeight = weight;
            value.UsedBy = (Ascension.Class)8;
            value.hideFlags = HideFlags.HideAndDontSave;
            return value;
        }

        internal static bool IsCustomId(string id) =>
            !string.IsNullOrEmpty(id) &&
            id.StartsWith("CS_MONK_AA_", StringComparison.Ordinal);

        internal static int GetRank(UseSkill skills, string id)
        {
            if (skills?.MyAscensions == null || string.IsNullOrEmpty(id))
                return 0;
            Ascension perk = Registered.FirstOrDefault(value => value != null &&
                string.Equals(value.Id, id, StringComparison.Ordinal));
            int max = perk?.MaxRank ?? 3;
            AscensionSkillEntry entry = skills.MyAscensions.FirstOrDefault(
                value => value != null && string.Equals(value.id, id,
                    StringComparison.Ordinal));
            int rank = entry?.level ?? 0;
            if (entry != null && rank > max && IsMonkSkills(skills))
            {
                int refund = rank - max;
                entry.level = max;
                skills.AscensionPoints += refund;
                rank = max;
            }
            return Mathf.Clamp(rank, 0, max);
        }

        internal static int GetPlayerRank(string id) =>
            GetRank(GameData.PlayerControl?.Myself?.MySkills, id);

        internal static bool IsMonkSkills(UseSkill skills)
        {
            Stats stats = skills?.GetComponent<Stats>();
            return MonkCatalog.IsMonkClass(stats?.CharacterClass);
        }

        internal static bool Validate(out string detail)
        {
            var errors = new List<string>();
            if (Registered.Count != ExpectedCount)
                errors.Add("registered count " + Registered.Count);
            if (Registered.Select(value => value.Id)
                    .Distinct(StringComparer.Ordinal).Count() != ExpectedCount)
                errors.Add("duplicate IDs");
            foreach (Ascension value in Registered)
            {
                if (value == null || string.IsNullOrWhiteSpace(value.Id) ||
                    string.IsNullOrWhiteSpace(value.SkillName) ||
                    string.IsNullOrWhiteSpace(value.SkillDesc) ||
                    value.MaxRank != 3 || (int)value.UsedBy != 8)
                    errors.Add(value?.SkillName ?? "null perk");
                if (GameData.SkillDatabase?.GetAscensionByID(value?.Id) != value)
                    errors.Add("database " + (value?.SkillName ?? "null"));
            }
            detail = errors.Count == 0
                ? "eight Monk-only three-rank perks for technique damage, " +
                  "Mend, Stone Stance, Hundred Fists, Feign Death, extra " +
                  "attacks, damage reduction, and resistances"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }
    }

    /// <summary>
    /// Redirects the native ascension lookups that the borrowed Monk perks sit
    /// on, so their game-side implementations read the Monk's own rank.
    /// </summary>
    [HarmonyPatch(typeof(UseSkill), nameof(UseSkill.GetAscensionRank))]
    internal static class Patch_MonkAscensionEffects
    {
        private static void Postfix(UseSkill __instance, string _id,
            ref int __result)
        {
            if (!MonkAscensions.IsMonkSkills(__instance) ||
                !MonkAscensions.TryMapNativeHook(_id, out string customId))
                return;
            __result = Mathf.Max(__result,
                MonkAscensions.GetRank(__instance, customId));
        }
    }

    [HarmonyPatch(typeof(SkillDB), "Start")]
    internal static class Patch_MonkAscensionDatabase
    {
        private static void Postfix()
        {
            if (SkillsPlugin.CfgEnableMonk?.Value == true)
                MonkAscensions.Register();
        }
    }

    [HarmonyPatch(typeof(AAScreen), "CountSpent")]
    internal static class Patch_MonkAscensionSpent
    {
        private static void Postfix(AAScreen __instance,
            ref int ___spentPoints)
        {
            if (!MonkCatalog.IsMonkPlayer()) return;
            UseSkill skills = GameData.PlayerControl?.Myself?.MySkills;
            ___spentPoints += MonkAscensions.All.Sum(value =>
                MonkAscensions.GetRank(skills, value.Id));
            if (__instance?.spent != null)
                __instance.spent.text = "Points spent: " + ___spentPoints;
        }
    }

    [HarmonyPatch(typeof(AAScreen), nameof(AAScreen.LoadAllAAButtonData))]
    internal static class Patch_MonkAscensionClassTab
    {
        private static void Postfix(AAScreen __instance,
            bool ___classSpecific)
        {
            if (!___classSpecific || __instance?.AllAASlots == null ||
                !MonkCatalog.IsMonkPlayer()) return;
            UseSkill skills = GameData.PlayerControl?.Myself?.MySkills;
            int count = Mathf.Min(__instance.AllAASlots.Count,
                MonkAscensions.All.Count);
            for (int index = 0; index < count; index++)
            {
                Ascension perk = MonkAscensions.All[index];
                AAButton slot = __instance.AllAASlots[index];
                if (slot == null) continue;
                slot.Name.text = perk.SkillName;
                slot.SkillDescription = perk.SkillDesc;
                slot.id = perk.Id;
                slot.MaxLevel.text = " / " + perk.MaxRank;
                slot.Level.text = MonkAscensions.GetRank(
                    skills, perk.Id).ToString();
            }
        }
    }
}
