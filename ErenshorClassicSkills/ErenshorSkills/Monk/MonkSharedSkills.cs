using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;

namespace ErenshorSkills.Monk
{
    /// <summary>
    /// Seven native martial skills retained by exact native Skill identity.
    /// Native Erenshor has fixed per-class level fields, so a temporary native
    /// class proxy is used only while the Monk consumes a supported book.
    /// </summary>
    internal static class MonkSharedSkills
    {
        internal enum ProxyClass
        {
            Duelist,
            Paladin
        }

        internal sealed class Definition
        {
            internal readonly string SkillId;
            internal readonly string SkillName;
            internal readonly string BookId;
            internal readonly string BookName;
            internal readonly int RequiredLevel;
            internal readonly ProxyClass Proxy;

            internal Definition(string skillId, string skillName,
                string bookId, string bookName, int requiredLevel,
                ProxyClass proxy)
            {
                SkillId = skillId;
                SkillName = skillName;
                BookId = bookId;
                BookName = bookName;
                RequiredLevel = requiredLevel;
                Proxy = proxy;
            }
        }

        internal struct NativeClassSwap
        {
            internal Stats Stats;
            internal Class OriginalClass;
            internal bool Changed;
            // ItemIcon.OnPointerUp gates the whole learn block on
            // item.Classes.Contains(player class) BEFORE the per-class skill
            // check. Swapping the Monk to the proxy therefore fails that outer
            // gate on Monk-only tomes, so the proxy is lent to the item's
            // class list for the duration of the call and removed afterwards.
            internal Item Item;
            internal Class LentClass;
        }

        private static readonly Definition[] Definitions =
        {
            new Definition("37647810", "Kick", "25108196",
                "Skill Book: Kick", 1, ProxyClass.Paladin),
            new Definition("3507035", "Dodge", "228774",
                "Skill Book: Dodge", 2, ProxyClass.Duelist),
            new Definition("33078850", "Dual Wield", "1233414",
                "Skill Book: Dual Wield", 2, ProxyClass.Duelist),
            new Definition("5668032", "Double Attack", "20753328",
                "Skill Book: Double Attack", 6, ProxyClass.Paladin),
            new Definition("94782922", "Dodge II", "4411142",
                "Skill Book: Dodge II", 8, ProxyClass.Duelist),
            new Definition("79193520", "Dodge III", "6461871",
                "Skill Book: Dodge III", 24, ProxyClass.Duelist),
            new Definition("481539", "Dodge IV", "35689575",
                "Skill Book: Dodge IV", 32, ProxyClass.Duelist)
        };

        internal static int ExpectedCount => Definitions.Length;
        internal static IReadOnlyList<Definition> All => Definitions;

        internal static int ReconcileSkillBooks()
        {
            if (SkillsPlugin.CfgEnableMonk?.Value != true ||
                MonkCatalog.MonkClass == null ||
                GameData.ItemDB?.ItemDB == null)
                return 0;
            int matched = 0;
            int changed = 0;
            foreach (Item item in GameData.ItemDB.ItemDB)
            {
                if (!TryGetDefinition(item, out _)) continue;
                matched++;
                if (item.Classes == null)
                    item.Classes = new List<Class>();
                item.Classes.RemoveAll(value => MonkCatalog.IsMonkClass(value) &&
                    !ReferenceEquals(value, MonkCatalog.MonkClass));
                if (!item.Classes.Contains(MonkCatalog.MonkClass))
                {
                    item.Classes.Add(MonkCatalog.MonkClass);
                    changed++;
                }
            }
            if (changed > 0)
                SkillsPlugin.Log?.LogInfo(
                    $"Monk: enabled {matched}/{ExpectedCount} native martial skill books.");
            return matched;
        }

        internal static bool TryGetDefinition(Item item,
            out Definition definition)
        {
            definition = null;
            if (item?.TeachSkill == null) return false;
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
            definition = Definitions.FirstOrDefault(value => skill != null &&
                string.Equals(value.SkillId, skill.Id,
                    StringComparison.OrdinalIgnoreCase));
            return definition != null;
        }

        internal static IEnumerable<Item> ResolveBooks()
        {
            Item[] database = GameData.ItemDB?.ItemDB ?? Array.Empty<Item>();
            foreach (Definition definition in Definitions)
            {
                Item book = database.FirstOrDefault(item => item != null &&
                    string.Equals(item.Id, definition.BookId,
                        StringComparison.OrdinalIgnoreCase));
                if (book != null) yield return book;
            }
        }

        internal static NativeClassSwap BeginNativeRules(ItemIcon icon,
            bool eligibleInput)
        {
            var state = new NativeClassSwap();
            Stats stats = GameData.PlayerStats;
            Item item = icon?.MyItem;
            if (!eligibleInput || stats == null || item?.TeachSkill == null ||
                !MonkCatalog.IsMonkClass(stats.CharacterClass))
                return state;

            Class proxy = null;
            if (TryGetDefinition(item, out Definition shared))
                proxy = ResolveProxy(shared.Proxy);
            else if (MonkCatalog.TryGetDefinition(item.TeachSkill, out _))
                proxy = GameData.ClassDB?.Duelist;
            if (proxy == null) return state;

            state.Stats = stats;
            state.OriginalClass = stats.CharacterClass;
            state.Changed = true;
            stats.CharacterClass = proxy;
            if (item.Classes != null && !item.Classes.Contains(proxy))
            {
                item.Classes.Add(proxy);
                state.Item = item;
                state.LentClass = proxy;
            }
            return state;
        }

        private static Class ResolveProxy(ProxyClass proxy)
        {
            return proxy == ProxyClass.Paladin
                ? GameData.ClassDB?.Paladin
                : GameData.ClassDB?.Duelist;
        }

        internal static void RestoreNativeClass(NativeClassSwap state)
        {
            if (state.Changed && state.Stats != null)
                state.Stats.CharacterClass = state.OriginalClass;
            if (state.Item?.Classes != null && state.LentClass != null)
                state.Item.Classes.Remove(state.LentClass);
        }

        internal static void AppendTooltipRequirement(ItemInfoWindow window,
            Item item)
        {
            if (window?.ReqLvl == null || item?.TeachSkill == null) return;
            int level;
            if (TryGetDefinition(item, out Definition shared))
                level = shared.RequiredLevel;
            else if (MonkCatalog.TryGetDefinition(item.TeachSkill,
                    out MonkTechniqueDefinition custom))
                level = custom.Level;
            else
                return;

            TextMeshProUGUI label = window.ReqLvl.GetComponent<TextMeshProUGUI>();
            if (label == null) return;
            string text = label.text ?? "";
            if (ContainsRequirement(text, level)) return;
            const string marker = "Skill Type:";
            int insert = text.IndexOf(marker,
                StringComparison.OrdinalIgnoreCase);
            string requirement = "Monk: " + level;
            if (insert < 0)
                label.text = text.TrimEnd('\r', '\n') + "\n" + requirement;
            else
                label.text = text.Substring(0, insert).TrimEnd('\r', '\n') +
                    "\n" + requirement + "\n\n" +
                    text.Substring(insert).TrimStart('\r', '\n');
        }

        private static bool ContainsRequirement(string text, int level)
        {
            string expected = "Monk: " + level;
            return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n').Any(line => string.Equals(
                    StripRichText(line).Trim(), expected,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static string StripRichText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var visible = new System.Text.StringBuilder(text.Length);
            bool tag = false;
            foreach (char value in text)
            {
                if (value == '<') { tag = true; continue; }
                if (value == '>' && tag) { tag = false; continue; }
                if (!tag) visible.Append(value);
            }
            return visible.ToString();
        }

        internal static bool Validate(out string detail)
        {
            var errors = new List<string>();
            if (Definitions.Length != 7) errors.Add("catalog count");
            foreach (Definition definition in Definitions)
            {
                Skill skill = GameData.SkillDatabase?.GetSkillByID(
                    definition.SkillId);
                Item book = GameData.ItemDB?.ItemDB?.FirstOrDefault(item =>
                    item != null && string.Equals(item.Id, definition.BookId,
                        StringComparison.OrdinalIgnoreCase));
                if (skill == null || book == null)
                {
                    errors.Add("missing " + definition.SkillName);
                    continue;
                }
                int proxyLevel = definition.Proxy == ProxyClass.Paladin
                    ? skill.PaladinRequiredLevel
                    : skill.DuelistRequiredLevel;
                if (proxyLevel != definition.RequiredLevel)
                    errors.Add(definition.SkillName + " proxy level " +
                        proxyLevel);
                if (!ReferenceEquals(book.TeachSkill, skill))
                    errors.Add(definition.SkillName + " native identity");
                if (book.Classes == null ||
                    !book.Classes.Contains(MonkCatalog.MonkClass))
                    errors.Add(definition.SkillName + " eligibility");
            }
            detail = errors.Count == 0
                ? "seven exact native martial skills/books at Monk levels 1/2/2/6/8/24/32 with mouse, controller, tooltip, ID-save, and hotbar compatibility"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }
    }

    [HarmonyPatch(typeof(ItemIcon), nameof(ItemIcon.OnPointerUp))]
    internal static class Patch_MonkSkillMouseUse
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(ItemIcon __instance,
            UnityEngine.EventSystems.PointerEventData eventData,
            out MonkSharedSkills.NativeClassSwap __state)
        {
            bool rightClick = eventData != null && eventData.button ==
                UnityEngine.EventSystems.PointerEventData.InputButton.Right;
            __state = MonkSharedSkills.BeginNativeRules(__instance, rightClick);
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(MonkSharedSkills.NativeClassSwap __state) =>
            MonkSharedSkills.RestoreNativeClass(__state);

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception,
            MonkSharedSkills.NativeClassSwap __state)
        {
            MonkSharedSkills.RestoreNativeClass(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ItemIcon), "GamepadConsumeOrLoot")]
    internal static class Patch_MonkSkillGamepadUse
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(ItemIcon __instance,
            out MonkSharedSkills.NativeClassSwap __state) =>
            __state = MonkSharedSkills.BeginNativeRules(__instance, true);

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(MonkSharedSkills.NativeClassSwap __state) =>
            MonkSharedSkills.RestoreNativeClass(__state);

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception,
            MonkSharedSkills.NativeClassSwap __state)
        {
            MonkSharedSkills.RestoreNativeClass(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ItemInfoWindow), nameof(ItemInfoWindow.DisplayItem))]
    internal static class Patch_MonkSkillTooltip
    {
        private static void Postfix(ItemInfoWindow __instance, Item item) =>
            MonkSharedSkills.AppendTooltipRequirement(__instance, item);
    }
}
