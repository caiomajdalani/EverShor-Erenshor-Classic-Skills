using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace ErenshorSkills.Patches
{
    /// <summary>
    /// Makes the native character loader and saver treat Necromancer and Monk
    /// as durable class identities. The saved custom-class name is authoritative;
    /// the live custom class is a recovery source; unique custom ability IDs are
    /// the final recovery source for saves previously overwritten by a native
    /// class name.
    /// </summary>
    internal static class CustomClassIdentity
    {
        private static bool _enforcing;

        internal static void PrimeSaveSlot(int index)
        {
            SaveGameData slot = GameData.SaveSlots != null && index >= 0 &&
                index < GameData.SaveSlots.Count
                    ? GameData.SaveSlots[index] : null;
            Prime(slot);
        }

        internal static void PrimeBackupSlot(int index)
        {
            SaveGameData slot = LoadBackupSaves.AllBackups != null &&
                index >= 0 && index < LoadBackupSaves.AllBackups.Count
                    ? LoadBackupSaves.AllBackups[index] : null;
            Prime(slot);
        }

        private static void Prime(SaveGameData slot)
        {
            if (slot == null || GameData.PlayerStats == null ||
                !TryResolveName(slot.CharClass, out Class customClass))
                return;
            GameData.PlayerStats.CharacterClass = customClass;
        }

        internal static void EnforceManager(CharSelectManager manager,
            string reason)
        {
            EnforceCurrent(reason, allowAbilityRecovery: true,
                recalculate: false);
            string name = GameData.CurrentCharacterSlot?.CharClass;
            if (string.Equals(name,
                    Necromancer.NecromancerCatalog.ClassName,
                    StringComparison.OrdinalIgnoreCase))
                Necromancer.NecromancerCharacterCreation
                    .RestoreSelectedClass(manager);
            else if (string.Equals(name, Monk.MonkCatalog.ClassName,
                    StringComparison.OrdinalIgnoreCase))
                Monk.MonkCharacterCreation.RestoreSelectedClass(manager);
        }

        internal static bool EnforceCurrent(string reason,
            bool allowAbilityRecovery, bool recalculate)
        {
            if (_enforcing) return false;
            SaveGameData slot = GameData.CurrentCharacterSlot;
            Stats stats = GameData.PlayerStats;
            if (slot == null || stats == null) return false;

            _enforcing = true;
            try
            {
                Class target = null;
                string source = null;
                if (TryResolveName(slot.CharClass, out target))
                    source = "saved class";
                else if (TryResolveClass(stats.CharacterClass, out target))
                    source = "live class";
                else if (allowAbilityRecovery &&
                    TryInferFromKnownAbilities(out string inferred) &&
                    TryResolveName(inferred, out target))
                    source = "custom ability IDs";

                if (target == null) return false;
                string canonical = target.ClassName;
                bool liveChanged = !ReferenceEquals(
                    stats.CharacterClass, target);
                bool saveChanged = !string.Equals(slot.CharClass, canonical,
                    StringComparison.Ordinal);
                if (!liveChanged && !saveChanged) return false;

                stats.CharacterClass = target;
                slot.CharClass = canonical;
                if (liveChanged && recalculate)
                {
                    try { stats.CalcStats(); } catch { }
                }
                SkillsPlugin.Log?.LogWarning(
                    $"Preserved custom class '{canonical}' at {reason} " +
                    $"using {source}; liveChanged={liveChanged}; " +
                    $"saveChanged={saveChanged}.");
                return true;
            }
            finally
            {
                _enforcing = false;
            }
        }

        internal static bool RunSelfTest(out string detail)
        {
            var errors = new List<string>();
            Stats stats = GameData.PlayerStats;
            SaveGameData slot = GameData.CurrentCharacterSlot;
            Class liveBefore = stats?.CharacterClass;
            string savedBefore = slot?.CharClass;
            if (stats == null || slot == null || GameData.ClassDB?.Arcanist == null)
            {
                detail = "native player/save state is unavailable";
                return false;
            }

            try
            {
                ExerciseIdentity(Necromancer.NecromancerCatalog.ClassName,
                    GameData.ClassDB.Arcanist, stats, slot, errors);
                ExerciseIdentity(Monk.MonkCatalog.ClassName,
                    GameData.ClassDB.Arcanist, stats, slot, errors);

                if (!string.Equals(InferFromIds(
                        new[] { "CS_NECRO_SPELL_PROBE" }, Array.Empty<string>()),
                        Necromancer.NecromancerCatalog.ClassName,
                        StringComparison.Ordinal))
                    errors.Add("Necromancer ability recovery");
                if (!string.Equals(InferFromIds(Array.Empty<string>(),
                        new[] { "CS_MONK_SKILL_PROBE" }),
                        Monk.MonkCatalog.ClassName,
                        StringComparison.Ordinal))
                    errors.Add("Monk ability recovery");
                if (InferFromIds(new[] { "CS_NECRO_SPELL_PROBE" },
                        new[] { "CS_MONK_SKILL_PROBE" }) != null)
                    errors.Add("ambiguous ability recovery");
            }
            catch (Exception ex)
            {
                errors.Add(ex.GetBaseException().Message);
            }
            finally
            {
                stats.CharacterClass = liveBefore;
                slot.CharClass = savedBefore;
                try { stats.CalcStats(); } catch { }
            }

            detail = errors.Count == 0
                ? "native load/save guards preserved exact Necromancer and " +
                  "Monk identities from both saved and live state; unique " +
                  "custom ability IDs recover previously overwritten saves"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        private static void ExerciseIdentity(string name, Class nativeClass,
            Stats stats, SaveGameData slot, List<string> errors)
        {
            if (!TryResolveName(name, out Class customClass))
            {
                errors.Add(name + " resolution");
                return;
            }

            slot.CharClass = name;
            stats.CharacterClass = nativeClass;
            EnforceCurrent("self-test saved identity",
                allowAbilityRecovery: false, recalculate: false);
            if (!ReferenceEquals(stats.CharacterClass, customClass) ||
                !string.Equals(slot.CharClass, name,
                    StringComparison.Ordinal))
                errors.Add(name + " saved identity");

            slot.CharClass = nativeClass.ClassName;
            stats.CharacterClass = customClass;
            EnforceCurrent("self-test live identity",
                allowAbilityRecovery: false, recalculate: false);
            if (!ReferenceEquals(stats.CharacterClass, customClass) ||
                !string.Equals(slot.CharClass, name,
                    StringComparison.Ordinal))
                errors.Add(name + " live identity");
        }

        private static bool TryResolveName(string name,
            out Class customClass)
        {
            customClass = null;
            if (string.Equals(name,
                    Necromancer.NecromancerCatalog.ClassName,
                    StringComparison.OrdinalIgnoreCase))
            {
                Necromancer.NecromancerCatalog.EnsureClass();
                customClass = Necromancer.NecromancerCatalog.NecromancerClass;
            }
            else if (string.Equals(name, Monk.MonkCatalog.ClassName,
                    StringComparison.OrdinalIgnoreCase))
            {
                Monk.MonkCatalog.EnsureClass();
                customClass = Monk.MonkCatalog.MonkClass;
            }
            return customClass != null;
        }

        private static bool TryResolveClass(Class value,
            out Class customClass)
        {
            customClass = null;
            if (Necromancer.NecromancerCatalog.IsNecromancerClass(value))
                return TryResolveName(
                    Necromancer.NecromancerCatalog.ClassName,
                    out customClass);
            if (Monk.MonkCatalog.IsMonkClass(value))
                return TryResolveName(Monk.MonkCatalog.ClassName,
                    out customClass);
            return false;
        }

        private static bool TryInferFromKnownAbilities(out string className)
        {
            IEnumerable<string> spells = GameData.PlayerControl?
                .GetComponent<CastSpell>()?.KnownSpells?
                .Where(value => value != null).Select(value => value.Id) ??
                Array.Empty<string>();
            IEnumerable<string> skills = GameData.PlayerControl?
                .GetComponent<UseSkill>()?.KnownSkills?
                .Where(value => value != null).Select(value => value.Id) ??
                Array.Empty<string>();
            className = InferFromIds(spells, skills);
            return className != null;
        }

        private static string InferFromIds(IEnumerable<string> spellIds,
            IEnumerable<string> skillIds)
        {
            bool necromancer = (spellIds ?? Array.Empty<string>()).Any(id =>
                Necromancer.NecromancerCatalog.IsNecromancerSpellId(id));
            bool monk = (skillIds ?? Array.Empty<string>()).Any(id =>
                Monk.MonkCatalog.IsMonkSkillId(id));
            if (necromancer == monk) return null;
            return necromancer
                ? Necromancer.NecromancerCatalog.ClassName
                : Monk.MonkCatalog.ClassName;
        }
    }

    [HarmonyPatch(typeof(CharSelectManager),
        nameof(CharSelectManager.SelectSlot))]
    internal static class Patch_CustomClassSelectSlot
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(int __0) =>
            CustomClassIdentity.PrimeSaveSlot(__0);

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(CharSelectManager __instance) =>
            CustomClassIdentity.EnforceManager(__instance, "character load");

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception,
            CharSelectManager __instance)
        {
            CustomClassIdentity.EnforceManager(__instance,
                "character-load finalizer");
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CharSelectManager),
        nameof(CharSelectManager.SelectBackupSlot))]
    internal static class Patch_CustomClassSelectBackupSlot
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(int __0) =>
            CustomClassIdentity.PrimeBackupSlot(__0);

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(CharSelectManager __instance) =>
            CustomClassIdentity.EnforceManager(__instance, "backup load");

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception,
            CharSelectManager __instance)
        {
            CustomClassIdentity.EnforceManager(__instance,
                "backup-load finalizer");
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CharSelectManager), nameof(CharSelectManager.Play))]
    internal static class Patch_CustomClassPlay
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(CharSelectManager __instance) =>
            CustomClassIdentity.EnforceManager(__instance, "enter world");

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(CharSelectManager __instance) =>
            CustomClassIdentity.EnforceManager(__instance,
                "enter-world completion");

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception,
            CharSelectManager __instance)
        {
            CustomClassIdentity.EnforceManager(__instance,
                "enter-world finalizer");
            return __exception;
        }
    }

    [HarmonyPatch(typeof(CharSelectManager),
        nameof(CharSelectManager.PlayAlternate))]
    internal static class Patch_CustomClassPlayAlternate
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(CharSelectManager __instance) =>
            CustomClassIdentity.EnforceManager(__instance,
                "alternate enter world");

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(CharSelectManager __instance) =>
            CustomClassIdentity.EnforceManager(__instance,
                "alternate enter-world completion");

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception,
            CharSelectManager __instance)
        {
            CustomClassIdentity.EnforceManager(__instance,
                "alternate enter-world finalizer");
            return __exception;
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.SaveGameData))]
    internal static class Patch_CustomClassNativeSave
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix() => CustomClassIdentity.EnforceCurrent(
            "native character save", allowAbilityRecovery: true,
            recalculate: false);
    }

    [HarmonyPatch(typeof(GameData), nameof(GameData.SaveData))]
    internal static class Patch_CustomClassRawSave
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix() => CustomClassIdentity.EnforceCurrent(
            "raw character serialization", allowAbilityRecovery: true,
            recalculate: false);
    }
}
