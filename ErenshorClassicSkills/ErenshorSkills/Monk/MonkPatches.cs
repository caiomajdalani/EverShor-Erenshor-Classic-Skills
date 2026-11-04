using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ErenshorSkills.Monk
{
    [HarmonyPatch(typeof(ClassDB), "Awake")]
    internal static class Patch_MonkClassDatabase
    {
        private static void Postfix() => MonkCatalog.EnsureClass();
    }

    [HarmonyPatch(typeof(SkillDB), "Start")]
    internal static class Patch_MonkSkillDatabase
    {
        private static void Postfix() => MonkCatalog.TryInitialize();
    }

    [HarmonyPatch(typeof(SpellDB), "Start")]
    internal static class Patch_MonkSpellDatabase
    {
        private static void Postfix() => MonkCatalog.TryInitialize();
    }

    [HarmonyPatch(typeof(CharSelectManager), "Start")]
    internal static class Patch_MonkCharacterCreationStart
    {
        private static void Postfix(CharSelectManager __instance) =>
            MonkCharacterCreation.InstallButton(__instance);
    }

    [HarmonyPatch(typeof(CharSelectManager), nameof(CharSelectManager.SaveChar))]
    internal static class Patch_MonkStartingItems
    {
        private static void Prefix(CharSelectManager __instance) =>
            MonkCharacterCreation.GrantStartingItemsIfValid(__instance);
    }

    [HarmonyPatch(typeof(RaidManager), "InitPlayerCard")]
    internal static class Patch_MonkPlayerClassIcon
    {
        private static void Postfix(RaidManager __instance) =>
            MonkClassIconUi.ApplyPlayerCard(__instance);
    }

    [HarmonyPatch(typeof(RaidManager), "AssignClassIcon")]
    internal static class Patch_MonkRaidClassIcon
    {
        private static void Postfix(RaidMemberSlot slot) =>
            MonkClassIconUi.ApplyRaidSlot(slot);
    }

    internal static class MonkCharacterCreation
    {
        private static readonly MethodInfo CheckUsedNames = AccessTools.Method(
            typeof(CharSelectManager), "CheckUsedNames");

        internal static void InstallButton(CharSelectManager manager)
        {
            if (SkillsPlugin.CfgEnableMonk?.Value != true || manager == null)
                return;
            MonkCatalog.TryInitialize();
            MonkCatalog.EnsureClass();
            if (MonkCatalog.MonkClass == null) return;
            ErenshorSkills.Patches.EverShorCharacterCreationPager.Install(manager);
        }

        internal static void SelectMonk(CharSelectManager manager)
        {
            if (manager == null || GameData.CurrentCharacterSlot == null)
                return;
            try
            {
                GameData.PlayerAud?.PlayOneShot(GameData.Misc.Click,
                    GameData.UIVolume * GameData.MasterVol);
            }
            catch { }
            SetClassDescription(manager);
            GameData.CurrentCharacterSlot.CharClass = MonkCatalog.ClassName;
            GameData.PlayerStats.CharacterClass = MonkCatalog.MonkClass;
            manager.selClass = MonkCatalog.MonkClass;
            GameData.PlayerStats.ClearSpentProfPoints();
            GameData.PlayerStats.CalcStats();
        }

        internal static void RestoreSelectedClass(CharSelectManager manager)
        {
            if (manager == null || GameData.CurrentCharacterSlot == null ||
                !string.Equals(GameData.CurrentCharacterSlot.CharClass,
                    MonkCatalog.ClassName,
                    StringComparison.OrdinalIgnoreCase))
                return;
            MonkCatalog.TryInitialize();
            Class monk = MonkCatalog.MonkClass;
            if (monk == null) return;
            GameData.PlayerStats.CharacterClass = monk;
            manager.PlayerStats.CharacterClass = monk;
            manager.selClass = monk;
            if (manager.CharCreate != null &&
                manager.CharCreate.activeInHierarchy &&
                manager.ClassDesc != null)
                SetClassDescription(manager);
            if (manager.CharInfo != null && manager.PlayerStats != null &&
                !string.IsNullOrEmpty(manager.PlayerStats.MyName))
                manager.CharInfo.text = manager.PlayerStats.MyName +
                    " - Level " + manager.PlayerStats.Level + " " +
                    monk.DisplayName + "\n" + GameData.SceneName;
            try { manager.PlayerStats.CalcStats(); } catch { }
        }

        internal static void GrantStartingItemsIfValid(
            CharSelectManager manager)
        {
            if (manager?.selClass == null ||
                !MonkCatalog.IsMonkClass(manager.selClass) ||
                GameData.CurrentCharacterSlot == null || manager.Name == null)
                return;
            string name = manager.Name.text;
            if (string.IsNullOrWhiteSpace(name) || name == "Name Character" ||
                !Regex.IsMatch(name, "^[a-zA-Z0-9 ]+$")) return;
            try
            {
                if (CheckUsedNames != null &&
                    (bool)CheckUsedNames.Invoke(manager,
                        new object[] { name })) return;
            }
            catch { return; }

            MonkCatalog.TryInitialize();
            FreshCharacterState.ClearInheritedAura();
            IEnumerable<Item> native = manager.DueslistStart ??
                new List<Item>();
            foreach (Item item in native.Where(item => item != null &&
                item.TeachSpell == null && item.TeachSkill == null &&
                item.ThisWeaponType == Item.WeaponType.None))
                AddOnce(item);
            AddOnce(MonkGear.StarterWeapon);
            int learnedSkills = GrantStartingSkills(
                GameData.GM?.PlayerSkills?.KnownSkills);

            // Hand the level-one techniques over as tomes rather than writing
            // them straight into KnownSpells. At character select the spellbook
            // belongs to a character that does not exist yet, so the direct
            // grant silently landed nowhere and a new Monk arrived with only
            // Kick. Physical tomes follow the Necromancer's scroll pattern and
            // are visible in the pack.
            int grantedTomes = 0;
            foreach (string key in StartingTechniqueKeys)
            {
                Item tome = MonkCatalog.GetTome(key);
                if (tome == null) continue;
                AddOnce(tome);
                grantedTomes++;
            }
            SkillsPlugin.Log?.LogInfo(
                $"Monk starter kit: learned {learnedSkills} shared skill and " +
                $"granted {grantedTomes} technique tome(s); Kick is the " +
                "interrupt, and the Jab, Open Hand Discipline and Mend tomes " +
                "are in the pack ready to study.");
        }

        private static void SetClassDescription(CharSelectManager manager)
        {
            TextMeshProUGUI description = manager?.ClassDesc;
            if (description == null) return;
            description.enableWordWrapping = true;
            description.enableAutoSizing = true;
            description.fontSizeMin = 14f;
            description.fontSizeMax = Mathf.Max(14f, description.fontSize);
            description.overflowMode = TextOverflowModes.Truncate;
            description.text = MonkCatalog.ClassDescription;
        }

        private static IReadOnlyList<Skill> ResolveStartingSkills()
        {
            Skill kick = MonkSharedSkills.All
                .Where(value => string.Equals(value.SkillName, "Kick",
                    StringComparison.OrdinalIgnoreCase))
                .Select(value => GameData.SkillDatabase?
                    .GetSkillByID(value.SkillId))
                .FirstOrDefault(value => value != null);
            return new[]
            {
                kick
            }.Where(value => value != null).ToList();
        }

        /// <summary>
        /// Level-one techniques a new Monk starts with, handed over as tomes.
        /// </summary>
        private static readonly string[] StartingTechniqueKeys =
        {
            "jab", "open_hand_discipline", "mend"
        };

        private static int GrantStartingSkills(IList<Skill> known)
        {
            if (known == null) return 0;
            int learned = 0;
            foreach (Skill skill in ResolveStartingSkills())
            {
                if (known.Any(existing => existing != null &&
                    string.Equals(existing.Id, skill.Id,
                        StringComparison.OrdinalIgnoreCase)))
                    continue;
                known.Add(skill);
                learned++;
            }
            return learned;
        }

        internal static bool ValidateStartingSkillContract(
            out string detail)
        {
            MonkCatalog.TryInitialize();
            var simulatedSkills = new List<Skill>();
            int learnedSkills = GrantStartingSkills(simulatedSkills);
            Skill resolvedKick = simulatedSkills.FirstOrDefault(skill =>
                string.Equals(skill?.SkillName, "Kick",
                    StringComparison.OrdinalIgnoreCase));

            // Techniques now arrive as tomes rather than direct spellbook
            // writes, and Open Hand Discipline is an innate skill rather than a
            // spell, so validate what each starting tome actually teaches.
            var taughtNames = new List<string>();
            bool tomesResolve = true;
            bool allLevelOne = true;
            foreach (string key in StartingTechniqueKeys)
            {
                Item tome = MonkCatalog.GetTome(key);
                if (tome == null)
                {
                    tomesResolve = false;
                    continue;
                }
                if (tome.TeachSpell == null && tome.TeachSkill == null)
                {
                    tomesResolve = false;
                    continue;
                }
                taughtNames.Add(tome.TeachSpell != null
                    ? tome.TeachSpell.SpellName
                    : tome.TeachSkill.SkillName);
                MonkTechniqueDefinition definition =
                    MonkSkillData.All.FirstOrDefault(value =>
                        string.Equals(value.Key, key,
                            StringComparison.OrdinalIgnoreCase));
                if (definition == null || definition.Level != 1)
                    allLevelOne = false;
            }

            Item jabTome = MonkCatalog.GetTome("jab");
            Spell resolvedJab = jabTome?.TeachSpell;
            Item disciplineTome = MonkCatalog.GetTome("open_hand_discipline");
            Item mendTome = MonkCatalog.GetTome("mend");
            bool roleContract = resolvedKick?.Interrupt == true &&
                resolvedJab != null &&
                resolvedJab.Type == Spell.SpellType.Damage &&
                !resolvedJab.SelfOnly && resolvedJab.ManaCost > 0 &&
                // A passive belongs in the skill list, not the spellbook.
                disciplineTome?.TeachSkill != null &&
                disciplineTome.TeachSpell == null &&
                // Self-only techniques need a positive range or CastSpell
                // rejects them as out of range.
                mendTome?.TeachSpell != null &&
                mendTome.TeachSpell.SpellRange > 0f;

            string[] required =
            {
                "Jab", "Open Hand Discipline", "Mend"
            };
            bool pass = learnedSkills == 1 && resolvedKick != null &&
                tomesResolve && allLevelOne && roleContract &&
                required.All(name => taughtNames.Contains(name,
                    StringComparer.OrdinalIgnoreCase)) &&
                MonkSharedSkills.All.Any(value =>
                    string.Equals(value.SkillId, resolvedKick?.Id,
                        StringComparison.OrdinalIgnoreCase) &&
                    value.RequiredLevel == 1);
            detail = pass
                ? "fresh Monk creation learns Kick as a shared skill and " +
                  "carries level-one Jab, Open Hand Discipline, and Mend " +
                  "tomes; Open Hand Discipline teaches a skill, Mend has a " +
                  "castable self range, and active techniques use mana"
                : $"starter resolution failed: kick={resolvedKick != null}, " +
                  $"learnedSkills={learnedSkills}, tomes={tomesResolve}, " +
                  $"levelOne={allLevelOne}, roles={roleContract}, " +
                  $"taught=[{string.Join(", ", taughtNames)}]";
            return pass;
        }

        private static void AddOnce(Item item)
        {
            if (item == null || GameData.PlayerInv == null) return;
            bool present = GameData.PlayerInv.StoredSlots?.Any(slot =>
                slot?.MyItem == item) == true;
            if (!present && !GameData.PlayerInv.AddItemToInv(item))
                GameData.PlayerInv.ForceItemToInv(item);
        }
    }

    internal static class MonkClassIconUi
    {
        private static readonly Color MonkColor =
            new Color(0.24f, 0.78f, 0.66f, 1f);

        internal static void ApplyPlayerCard(RaidManager manager)
        {
            if (manager?.PlayerNameCard == null ||
                !MonkCatalog.IsMonkClass(
                    GameData.PlayerStats?.CharacterClass)) return;
            if (manager.PlayerNameCard.PlayerIcon != null)
            {
                manager.PlayerNameCard.PlayerIcon.sprite =
                    MonkCatalog.ClassIcon;
                manager.PlayerNameCard.PlayerIcon.color = MonkColor;
            }
            if (manager.PlayerNameCard.MyNamePlate != null)
                manager.PlayerNameCard.MyNamePlate.color = MonkColor;
        }

        internal static void ApplyRaidSlot(RaidMemberSlot slot)
        {
            if (slot?.AssignedAvatar == null ||
                !MonkCatalog.IsMonkClass(
                    slot.AssignedAvatar.MyStats?.CharacterClass)) return;
            if (slot.ClassIcon != null)
            {
                slot.ClassIcon.sprite = MonkCatalog.ClassIcon;
                slot.ClassIcon.color = MonkColor;
            }
            if (slot.MyNameplate != null)
                slot.MyNameplate.color = MonkColor;
        }
    }
}
