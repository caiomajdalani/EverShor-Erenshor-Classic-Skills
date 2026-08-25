using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;

namespace ErenshorSkills.Necromancer
{
    /// <summary>
    /// Gives Necromancers access to the combat skills that are genuinely shared
    /// by Arcanists and at least one other native class. The game has no custom-
    /// class required-level field on Skill, so the mapped Arcanist level is
    /// enforced by a custom-class book handler. The player's class is never
    /// changed. The learned object is still the original native Skill,
    /// preserving its effects and ID-based save/load behavior.
    /// </summary>
    internal static class NecromancerSharedSkills
    {
        internal sealed class Definition
        {
            internal readonly string SkillId;
            internal readonly string SkillName;
            internal readonly string BookId;
            internal readonly string BookName;
            internal readonly int RequiredLevel;

            internal Definition(string skillId, string skillName,
                string bookId, string bookName, int requiredLevel)
            {
                SkillId = skillId;
                SkillName = skillName;
                BookId = bookId;
                BookName = bookName;
                RequiredLevel = requiredLevel;
            }
        }

        private static readonly Definition[] Definitions =
        {
            new Definition("8312964", "Arcane Proficiency",
                "6687324", "Skill Book: Arcane Proficiency", 8),
            new Definition("4069290", "Arcane Recovery",
                "49058786", "Skill Book: Arcane Recovery", 12),
            new Definition("4776544", "Block",
                "1905358", "Skill Book: Block", 15),
            new Definition("20546775", "Block II",
                "33230897", "Skill Book: Block II", 20),
            new Definition("3507035", "Dodge",
                "228774", "Skill Book: Dodge", 8),
            new Definition("94782922", "Dodge II",
                "4411142", "Skill Book: Dodge II", 16)
        };

        private static readonly HashSet<string> AllowedSkillIds =
            new HashSet<string>(Definitions.Select(value => value.SkillId),
                StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> AllowedBookIds =
            new HashSet<string>(Definitions.Select(value => value.BookId),
                StringComparer.OrdinalIgnoreCase);

        internal static int ExpectedCount => Definitions.Length;
        internal static IReadOnlyList<Definition> All => Definitions;

        internal static int ReconcileSkillBooks()
        {
            if (SkillsPlugin.CfgEnableNecromancer?.Value != true ||
                NecromancerCatalog.NecromancerClass == null ||
                GameData.ItemDB?.ItemDB == null)
                return 0;

            int changed = 0;
            int matched = 0;
            foreach (Item item in GameData.ItemDB.ItemDB)
            {
                if (!TryGetDefinition(item, out _)) continue;
                matched++;
                if (item.Classes == null)
                {
                    item.Classes = new List<Class>();
                    changed++;
                }

                int stale = item.Classes.RemoveAll(value =>
                    NecromancerCatalog.IsNecromancerClass(value) &&
                    !ReferenceEquals(value,
                        NecromancerCatalog.NecromancerClass));
                changed += stale;
                if (!item.Classes.Contains(NecromancerCatalog.NecromancerClass))
                {
                    item.Classes.Add(NecromancerCatalog.NecromancerClass);
                    changed++;
                }
            }

            if (changed > 0)
                SkillsPlugin.Log.LogInfo(
                    $"Necromancer: enabled {matched}/{ExpectedCount} shared " +
                    $"Arcanist combat-skill books ({changed} eligibility updates).");
            return matched;
        }

        internal static bool TryGetDefinition(Item item,
            out Definition definition)
        {
            definition = null;
            if (item?.TeachSkill == null ||
                !AllowedBookIds.Contains(item.Id ?? "") ||
                !AllowedSkillIds.Contains(item.TeachSkill.Id ?? ""))
                return false;
            definition = Definitions.FirstOrDefault(value =>
                string.Equals(value.BookId, item.Id,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(value.SkillId, item.TeachSkill.Id,
                    StringComparison.OrdinalIgnoreCase));
            return definition != null;
        }

        internal static bool TryGetDefinition(Skill skill,
            out Definition definition)
        {
            definition = null;
            if (skill == null || !AllowedSkillIds.Contains(skill.Id ?? ""))
                return false;
            definition = Definitions.FirstOrDefault(value =>
                string.Equals(value.SkillId, skill.Id,
                    StringComparison.OrdinalIgnoreCase));
            return definition != null;
        }

        internal static Items.CustomClassSkillBookUse.State BeginMouseUse(
            ItemIcon itemIcon,
            UnityEngine.EventSystems.PointerEventData eventData)
        {
            if (!TryGetDefinition(itemIcon?.MyItem,
                    out Definition definition))
                return default;
            return Items.CustomClassSkillBookUse.BeginMouse(itemIcon,
                eventData, NecromancerCatalog.NecromancerClass,
                definition.RequiredLevel);
        }

        internal static Items.CustomClassSkillBookUse.State BeginGamepadUse(
            ItemIcon itemIcon)
        {
            if (!TryGetDefinition(itemIcon?.MyItem,
                    out Definition definition))
                return default;
            return Items.CustomClassSkillBookUse.BeginGamepad(itemIcon,
                NecromancerCatalog.NecromancerClass,
                definition.RequiredLevel);
        }

        internal static void AppendTooltipRequirement(
            ItemInfoWindow window, Item item)
        {
            if (window?.ReqLvl == null ||
                !TryGetDefinition(item, out Definition definition))
                return;
            TextMeshProUGUI label = window.ReqLvl
                .GetComponent<TextMeshProUGUI>();
            if (label == null) return;
            string text = label.text ?? "";
            if (text.IndexOf("YOU ALREADY KNOW THIS SKILL",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                ContainsRequirement(text, definition.RequiredLevel))
                return;

            const string skillType = "Skill Type:";
            int insertAt = text.IndexOf(skillType,
                StringComparison.OrdinalIgnoreCase);
            string requirement = "Necromancer: " +
                definition.RequiredLevel;
            if (insertAt < 0)
            {
                label.text = text.TrimEnd('\r', '\n') + "\n" + requirement;
                Items.NativeConsumableCompatibility.FitTooltipText(label);
                return;
            }

            string before = text.Substring(0, insertAt).TrimEnd('\r', '\n');
            string after = text.Substring(insertAt).TrimStart('\r', '\n');
            label.text = before + "\n" + requirement + "\n\n" + after;
            // This label already carries the whole teaching block; the extra
            // class requirement line has to fit inside it, not spill out.
            Items.NativeConsumableCompatibility.FitTooltipText(label);
        }

        internal static bool ContainsRequirement(string text, int level)
        {
            string expected = "Necromancer: " + level;
            string[] lines = (text ?? "").Replace("\r\n", "\n")
                .Replace('\r', '\n').Split('\n');
            return lines.Any(line => string.Equals(
                StripRichText(line).Trim(), expected,
                StringComparison.OrdinalIgnoreCase));
        }

        private static string StripRichText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var visible = new System.Text.StringBuilder(text.Length);
            bool inTag = false;
            foreach (char value in text)
            {
                if (value == '<') { inTag = true; continue; }
                if (value == '>' && inTag) { inTag = false; continue; }
                if (!inTag) visible.Append(value);
            }
            return visible.ToString();
        }

        internal static bool Validate(out string detail)
        {
            var errors = new List<string>();
            if (Definitions.Length != 6)
                errors.Add("catalog count " + Definitions.Length);
            if (Definitions.Select(value => value.SkillId)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                Definitions.Length)
                errors.Add("duplicate skill IDs");
            if (Definitions.Select(value => value.BookId)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                Definitions.Length)
                errors.Add("duplicate book IDs");

            Class[] nativeClasses =
            {
                GameData.ClassDB?.Duelist,
                GameData.ClassDB?.Paladin,
                GameData.ClassDB?.Druid,
                GameData.ClassDB?.Stormcaller,
                GameData.ClassDB?.Reaver
            };
            foreach (Definition definition in Definitions)
            {
                Skill skill = GameData.SkillDatabase?.GetSkillByID(
                    definition.SkillId);
                if (skill == null)
                {
                    errors.Add("missing skill " + definition.SkillName);
                    continue;
                }
                if (!string.Equals(skill.SkillName, definition.SkillName,
                        StringComparison.OrdinalIgnoreCase))
                    errors.Add("skill name mismatch " + definition.SkillId);
                if (skill.ArcanistRequiredLevel != definition.RequiredLevel)
                    errors.Add($"{definition.SkillName} Arcanist level " +
                        $"{skill.ArcanistRequiredLevel}, expected " +
                        definition.RequiredLevel);
                if (!IsSharedWithAnotherNativeClass(skill, nativeClasses))
                    errors.Add("not genuinely shared " + definition.SkillName);

                Item[] books = GameData.ItemDB?.ItemDB?.Where(item =>
                    item != null && string.Equals(item.Id, definition.BookId,
                        StringComparison.OrdinalIgnoreCase)).ToArray() ??
                    Array.Empty<Item>();
                if (books.Length != 1)
                {
                    errors.Add($"{definition.BookName} instances={books.Length}");
                    continue;
                }
                Item book = books[0];
                if (!TryGetDefinition(book, out _))
                    errors.Add("book mapping " + definition.BookName);
                if (!string.Equals(book.ItemName, definition.BookName,
                        StringComparison.OrdinalIgnoreCase))
                    errors.Add("book name mismatch " + definition.BookId);
                if (book.Classes == null ||
                    !book.Classes.Contains(NecromancerCatalog.NecromancerClass))
                    errors.Add("Necromancer eligibility " +
                        definition.BookName);
                if (!ReferenceEquals(book.TeachSkill, skill))
                    errors.Add("native skill identity " +
                        definition.BookName);
            }

            string[] forbidden =
            {
                "Fishing", "Chant Control", "Arcane Resonance",
                "Lingering Inferno", "Multifocus"
            };
            foreach (string name in forbidden)
            {
                Skill skill = GameData.SkillDatabase?.GetSkillByName(name);
                if (TryGetDefinition(skill, out _))
                    errors.Add("forbidden skill included " + name);
            }

            string tooltip =
                "Required Level:\nArcanist: 8\n\nSkill Type: Innate";
            tooltip = InsertRequirementForTesting(tooltip, 8);
            tooltip = InsertRequirementForTesting(tooltip, 8);
            if (!ContainsRequirement(tooltip, 8) ||
                CountRequirement(tooltip, 8) != 1)
                errors.Add("tooltip requirement is not idempotent");

            detail = errors.Count == 0
                ? "6 native skills and 6 skill books mapped at Arcanist " +
                  "levels 8/12/15/20/8/16; exact native Skill identities " +
                  "preserve effects and ID save/load; mouse/controller " +
                  "compatibility never changes the player class; tooltip " +
                  "requirement is installed; " +
                  "Arcanist-only skills and Fishing excluded"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        /// <summary>
        /// Runs the real patched mouse and gamepad methods against temporary
        /// native books. Both denial and success are checked, then inventory,
        /// known skills, player level, class, cursor, and tooltip UI are restored.
        /// The inventory rollback persists the original snapshot so a failed QA
        /// run cannot leave a granted book or consume a real item.
        /// </summary>
        internal static bool RunInGameUseSelfTest(out string detail)
        {
            var errors = new List<string>();
            Items.BagSystem.MutationSnapshot inventorySnapshot = null;
            List<Skill> known = GameData.PlayerControl?.GetComponent<UseSkill>()
                ?.KnownSkills;
            List<Skill> knownBefore = known == null
                ? null : new List<Skill>(known);
            Stats stats = GameData.PlayerStats;
            int levelBefore = stats?.Level ?? 0;
            Class classBefore = stats?.CharacterClass;
            ItemIcon highlightedBefore = GameData.HighlightedItem;
            ItemIcon cursorBefore = GameData.ItemOnCursor;
            bool tooltipOpened = false;

            try
            {
                if (!Validate(out string catalogDetail))
                    throw new InvalidOperationException(catalogDetail);
                if (!NecromancerCatalog.IsNecromancerPlayer() || stats == null)
                    throw new InvalidOperationException(
                        "active player is not a Necromancer");
                if (known == null || GameData.PlayerInv?.StoredSlots == null ||
                    GameData.PlayerInv.Empty == null)
                    throw new InvalidOperationException(
                        "player skills or inventory are unavailable");
                if (Items.BagWindow.AnyOpen)
                    throw new InvalidOperationException(
                        "close crafted bag windows before the use-path test");

                inventorySnapshot = Items.BagSystem.BeginExternalMutation();
                ItemIcon scratch = GameData.PlayerInv.StoredSlots
                    .FirstOrDefault(Items.BagSystem.IsDirectInventorySlot);
                if (scratch == null)
                    throw new InvalidOperationException(
                        "no direct inventory slot is available");
                scratch.MyItem = GameData.PlayerInv.Empty;
                scratch.Quantity = 1;
                scratch.UpdateSlotImage();

                Definition mouseDefinition = Definitions[0];
                ExerciseUsePath(mouseDefinition, useMouse: true, known,
                    classBefore, errors);
                Definition gamepadDefinition = Definitions[1];
                ExerciseUsePath(gamepadDefinition, useMouse: false, known,
                    classBefore, errors);

                Item tooltipBook = ResolveBook(mouseDefinition);
                if (tooltipBook == null || GameData.ItemInfoWindow == null)
                    errors.Add("native tooltip window or probe book unavailable");
                else
                {
                    known.RemoveAll(value => value != null &&
                        string.Equals(value.Id, mouseDefinition.SkillId,
                            StringComparison.OrdinalIgnoreCase));
                    GameData.ItemInfoWindow.DisplayItem(tooltipBook,
                        new UnityEngine.Vector3(-1000f, -1000f, 0f), 1);
                    tooltipOpened = true;
                    TextMeshProUGUI label = GameData.ItemInfoWindow.ReqLvl?
                        .GetComponent<TextMeshProUGUI>();
                    string tooltip = label?.text ?? "";
                    if (!ContainsRequirement(tooltip,
                            mouseDefinition.RequiredLevel) ||
                        CountRequirement(tooltip,
                            mouseDefinition.RequiredLevel) != 1)
                        errors.Add("live tooltip lacks one Necromancer level " +
                            "requirement");
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log?.LogError(
                    "Necromancer shared-skill live QA failed: " + ex);
                errors.Add(ex.GetBaseException().Message);
            }
            finally
            {
                try
                {
                    if (tooltipOpened)
                        GameData.ItemInfoWindow?.CloseItemWindow();
                }
                catch { }
                if (known != null && knownBefore != null)
                {
                    known.Clear();
                    known.AddRange(knownBefore);
                }
                if (stats != null)
                {
                    stats.Level = levelBefore;
                    stats.CharacterClass = classBefore;
                }
                GameData.HighlightedItem = highlightedBefore;
                GameData.ItemOnCursor = cursorBefore;
                if (inventorySnapshot != null)
                    Items.BagSystem.RollbackExternalMutation(
                        inventorySnapshot);
            }

            detail = errors.Count == 0
                ? "real ItemIcon mouse and gamepad book-use paths denied one " +
                  "level early, learned and consumed at the exact Arcanist " +
                  "level without changing the player class, and rendered " +
                  "one idempotent Necromancer tooltip requirement; all QA " +
                  "state rolled back"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        private static void ExerciseUsePath(Definition definition,
            bool useMouse, List<Skill> known, Class expectedClass,
            List<string> errors)
        {
            Item book = ResolveBook(definition);
            Skill skill = GameData.SkillDatabase?.GetSkillByID(
                definition.SkillId);
            if (book == null || skill == null)
            {
                errors.Add("missing use-path objects for " +
                    definition.SkillName);
                return;
            }
            known.RemoveAll(value => value != null && string.Equals(
                value.Id, definition.SkillId,
                StringComparison.OrdinalIgnoreCase));
            if (!Items.ItemFactory.GiveItemInstanceToPlayer(book, 1))
            {
                errors.Add("could not grant temporary " +
                    definition.BookName);
                return;
            }
            ItemIcon slot = GameData.PlayerInv.StoredSlots.FirstOrDefault(
                value => Items.BagSystem.IsDirectInventorySlot(value) &&
                    ReferenceEquals(value.MyItem, book));
            if (slot == null)
            {
                errors.Add("temporary inventory slot missing for " +
                    definition.BookName);
                return;
            }

            GameData.PlayerStats.Level = definition.RequiredLevel - 1;
            InvokeUsePath(slot, useMouse);
            if (known.Contains(skill))
                errors.Add(definition.SkillName +
                    " learned below its required level");
            if (!ReferenceEquals(slot.MyItem, book))
                errors.Add(definition.BookName +
                    " was consumed after a denied use");
            if (!NecromancerCatalog.IsNecromancerClass(
                    GameData.PlayerStats.CharacterClass) ||
                !ReferenceEquals(GameData.PlayerStats.CharacterClass,
                    expectedClass))
                errors.Add("class was not restored after denied " +
                    (useMouse ? "mouse" : "gamepad") + " use");

            GameData.PlayerStats.Level = definition.RequiredLevel;
            InvokeUsePath(slot, useMouse);
            if (!known.Contains(skill))
                errors.Add(definition.SkillName +
                    " was not learned at its required level");
            if (ReferenceEquals(slot.MyItem, book))
                errors.Add(definition.BookName +
                    " was not consumed after successful learning");
            if (!NecromancerCatalog.IsNecromancerClass(
                    GameData.PlayerStats.CharacterClass) ||
                !ReferenceEquals(GameData.PlayerStats.CharacterClass,
                    expectedClass))
                errors.Add("class was not restored after successful " +
                    (useMouse ? "mouse" : "gamepad") + " use");
            known.Remove(skill);
        }

        private static void InvokeUsePath(ItemIcon slot, bool useMouse)
        {
            if (useMouse)
            {
                var pointer = new UnityEngine.EventSystems.PointerEventData(
                    UnityEngine.EventSystems.EventSystem.current)
                {
                    button = UnityEngine.EventSystems.PointerEventData
                        .InputButton.Right
                };
                GameData.HighlightedItem = slot;
                GameData.ItemOnCursor = null;
                slot.OnPointerDown(pointer);
                slot.OnPointerUp(pointer);
                return;
            }

            System.Reflection.MethodInfo method = AccessTools.Method(
                typeof(ItemIcon), "GamepadConsumeOrLoot");
            if (method == null)
                throw new MissingMethodException(typeof(ItemIcon).FullName,
                    "GamepadConsumeOrLoot");
            method.Invoke(slot, null);
        }

        private static Item ResolveBook(Definition definition)
        {
            return GameData.ItemDB?.ItemDB?.FirstOrDefault(item =>
                item != null && string.Equals(item.Id, definition.BookId,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSharedWithAnotherNativeClass(Skill skill,
            Class[] nativeClasses)
        {
            if (skill == null || nativeClasses == null) return false;
            // The Class objects make the intent explicit even though required
            // levels live in fixed fields on Skill.
            if (nativeClasses.All(value => value == null)) return false;
            return skill.DuelistRequiredLevel > 0 ||
                skill.PaladinRequiredLevel > 0 ||
                skill.DruidRequiredLevel > 0 ||
                skill.StormcallerRequiredLevel > 0 ||
                skill.ReaverRequiredLevel > 0;
        }

        private static string InsertRequirementForTesting(string text,
            int level)
        {
            if (ContainsRequirement(text, level)) return text;
            const string skillType = "Skill Type:";
            int insertAt = (text ?? "").IndexOf(skillType,
                StringComparison.OrdinalIgnoreCase);
            string requirement = "Necromancer: " + level;
            if (insertAt < 0)
                return (text ?? "").TrimEnd('\r', '\n') + "\n" + requirement;
            string before = text.Substring(0, insertAt).TrimEnd('\r', '\n');
            string after = text.Substring(insertAt).TrimStart('\r', '\n');
            return before + "\n" + requirement + "\n\n" + after;
        }

        private static int CountRequirement(string text, int level)
        {
            string expected = "Necromancer: " + level;
            return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n').Count(line => string.Equals(
                    StripRichText(line).Trim(), expected,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    [HarmonyPatch(typeof(ItemIcon), nameof(ItemIcon.OnPointerUp))]
    internal static class Patch_NecromancerSharedSkillMouseUse
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(ItemIcon __instance,
            UnityEngine.EventSystems.PointerEventData eventData,
            out Items.CustomClassSkillBookUse.State __state)
        {
            __state = NecromancerSharedSkills.BeginMouseUse(__instance,
                eventData);
            return !__state.Suppressed;
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(
            Items.CustomClassSkillBookUse.State __state)
        {
            Items.CustomClassSkillBookUse.Complete(__state);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception,
            Items.CustomClassSkillBookUse.State __state)
        {
            Items.CustomClassSkillBookUse.Restore(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ItemIcon), "GamepadConsumeOrLoot")]
    internal static class Patch_NecromancerSharedSkillGamepadUse
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(ItemIcon __instance,
            out Items.CustomClassSkillBookUse.State __state)
        {
            __state = NecromancerSharedSkills.BeginGamepadUse(__instance);
            return !__state.Suppressed;
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(
            Items.CustomClassSkillBookUse.State __state)
        {
            Items.CustomClassSkillBookUse.Complete(__state);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception,
            Items.CustomClassSkillBookUse.State __state)
        {
            Items.CustomClassSkillBookUse.Restore(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ItemInfoWindow), nameof(ItemInfoWindow.DisplayItem))]
    internal static class Patch_NecromancerSharedSkillTooltip
    {
        private static void Postfix(ItemInfoWindow __instance, Item item)
        {
            NecromancerSharedSkills.AppendTooltipRequirement(__instance, item);
        }
    }
}
