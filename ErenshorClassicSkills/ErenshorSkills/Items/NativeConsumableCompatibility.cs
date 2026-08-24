using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;

namespace ErenshorSkills.Items
{
    /// <summary>
    /// Extends Erenshor's universal native consumables to custom classes.
    /// Native food, drink, and general-use elixirs explicitly list all six
    /// built-in classes; without this reconciliation their tooltips omit Monk
    /// and Necromancer and class-aware consumers can reject them.
    /// </summary>
    internal static class NativeConsumableCompatibility
    {
        internal static int AddClass(Class customClass)
        {
            if (customClass == null || GameData.ItemDB?.ItemDB == null)
                return 0;

            int changed = 0;
            foreach (Item item in UniversalNativeConsumables())
            {
                bool alreadyEligible = HasClass(item, customClass,
                    customClass.ClassName);
                if (EnsureClass(item, customClass) && !alreadyEligible)
                    changed++;
            }
            return changed;
        }

        /// <summary>
        /// Reconciles one universal native consumable with a custom class.
        /// Kept item-scoped so tooltip rendering can repair class lists that
        /// another mod rebuilt after the catalog-wide initialization pass.
        /// </summary>
        internal static bool EnsureClass(Item item, Class customClass)
        {
            if (item?.Classes == null || customClass == null)
                return false;

            bool explicitlyAllowed = HasClass(item, customClass,
                customClass.ClassName);
            item.Classes.RemoveAll(value =>
                value != null &&
                !ReferenceEquals(value, customClass) &&
                string.Equals(value.ClassName, customClass.ClassName,
                    StringComparison.OrdinalIgnoreCase));
            if (explicitlyAllowed && !item.Classes.Contains(customClass))
                item.Classes.Add(customClass);
            if (item.Classes.Contains(customClass)) return true;

            Class[] native = NativePlayableClasses();
            if (native.Any(value => value == null) ||
                !IsUniversalNativeConsumable(item, native))
                return false;
            item.Classes.Add(customClass);
            return true;
        }

        internal static IReadOnlyList<Item> UniversalNativeConsumables()
        {
            if (GameData.ItemDB?.ItemDB == null)
                return Array.Empty<Item>();

            Class[] native = NativePlayableClasses();
            if (native.Any(value => value == null))
                return Array.Empty<Item>();

            return GameData.ItemDB.ItemDB
                .Where(item => IsUniversalNativeConsumable(item, native))
                .ToList();
        }

        internal static bool Validate(out string detail)
        {
            IReadOnlyList<Item> items = UniversalNativeConsumables();
            Class monk = Monk.MonkCatalog.MonkClass;
            Class necromancer =
                Necromancer.NecromancerCatalog.NecromancerClass;
            var failures = new List<string>();
            foreach (Item item in items)
            {
                if (item.Classes == null ||
                    monk == null || !HasClass(item, monk,
                        Monk.MonkCatalog.ClassName))
                    failures.Add($"{item?.ItemName ?? "<null>"}:Monk");
                if (item.Classes == null ||
                    necromancer == null ||
                    !HasClass(item, necromancer,
                        Necromancer.NecromancerCatalog.ClassName))
                    failures.Add(
                        $"{item?.ItemName ?? "<null>"}:Necromancer");
            }

            string names = string.Join(", ", items.Select(value =>
                value.ItemName).OrderBy(value => value,
                    StringComparer.OrdinalIgnoreCase));
            detail = $"native universal consumables={items.Count}; " +
                $"items=[{names}]; missing=[{string.Join(", ", failures)}]";
            return items.Count > 0 && failures.Count == 0;
        }

        /// <summary>
        /// Erenshor's native ItemInfoWindow renderer hard-codes only its six
        /// built-in classes. Append addon classes that are genuinely present
        /// on the item so native food, drink, gear, tomes, and scrolls display
        /// the same eligibility that their runtime rules enforce.
        /// </summary>
        internal static void AppendCustomClassesToTooltip(
            ItemInfoWindow window, Item item)
        {
            if (window?.Usable == null || item?.Classes == null) return;

            Monk.MonkCatalog.EnsureEquipmentEligibility(item);
            Necromancer.NecromancerCatalog
                .EnsureEquipmentEligibility(item);
            EnsureClass(item, Monk.MonkCatalog.MonkClass);
            EnsureClass(item,
                Necromancer.NecromancerCatalog.NecromancerClass);

            string rendered = window.Usable.text ?? "";
            var additions = new List<string>();
            if (HasClass(item, Necromancer.NecromancerCatalog.NecromancerClass,
                    Necromancer.NecromancerCatalog.ClassName) &&
                !ContainsClassName(rendered,
                    Necromancer.NecromancerCatalog.ClassName))
                additions.Add(
                    Necromancer.NecromancerCatalog.ClassName);

            if (HasClass(item, Monk.MonkCatalog.MonkClass,
                    Monk.MonkCatalog.ClassName) &&
                !ContainsClassName(rendered, Monk.MonkCatalog.ClassName))
                additions.Add(Monk.MonkCatalog.ClassName);

            if (additions.Count == 0) return;
            string existing = rendered.Trim();
            // Join with spaces like the native class list rather than forcing a
            // newline. A hard break cost a whole extra line on items with many
            // eligible classes, which is what pushed the list past the panel.
            window.Usable.text = (existing.Length == 0
                    ? string.Empty : existing + "  ") +
                string.Join("  ", additions);
            FitTooltipText(window.Usable);
        }

        /// <summary>
        /// Keeps a tooltip label inside its own rect. Appending addon lines
        /// makes these labels taller than the native layout budgeted for, and
        /// TextOverflowModes.Overflow let the surplus draw straight over
        /// neighbouring labels and past the panel border. Wrap and shrink to
        /// fit instead, down to a floor that stays readable.
        /// </summary>
        internal static void FitTooltipText(TextMeshProUGUI text)
        {
            if (text == null) return;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Truncate;
            if (text.enableAutoSizing) return;
            // Capture the authored size once; after auto-sizing is on,
            // fontSize reports the current shrunken value instead.
            text.fontSizeMax = text.fontSize;
            text.fontSizeMin = Math.Max(8f, text.fontSize * 0.55f);
            text.enableAutoSizing = true;
        }

        internal static bool ValidateRenderedTooltips(out string detail)
        {
            Monk.MonkCatalog.TryInitialize();
            Necromancer.NecromancerCatalog.TryInitialize();
            var items = new List<Item>();
            if (GameData.ItemDB?.ItemDB != null)
            {
                foreach (Item item in GameData.ItemDB.ItemDB)
                {
                    if (item?.Classes == null) continue;
                    Monk.MonkCatalog.EnsureEquipmentEligibility(item);
                    Necromancer.NecromancerCatalog
                        .EnsureEquipmentEligibility(item);
                    EnsureClass(item, Monk.MonkCatalog.MonkClass);
                    EnsureClass(item,
                        Necromancer.NecromancerCatalog.NecromancerClass);
                    if (HasClass(item, Monk.MonkCatalog.MonkClass,
                            Monk.MonkCatalog.ClassName) ||
                        HasClass(item,
                            Necromancer.NecromancerCatalog.NecromancerClass,
                            Necromancer.NecromancerCatalog.ClassName))
                        items.Add(item);
                }
            }
            ItemInfoWindow window = GameData.ItemInfoWindow;
            var failures = new List<string>();
            int rendered = 0;
            int monkExpected = 0;
            int necromancerExpected = 0;

            if (window?.ParentWindow == null || window.Usable == null)
            {
                detail = "ItemInfoWindow is unavailable";
                return false;
            }

            try
            {
                window.CloseItemWindow();
                foreach (Item item in items)
                {
                    bool expectedMonk = HasClass(item,
                        Monk.MonkCatalog.MonkClass,
                        Monk.MonkCatalog.ClassName);
                    bool expectedNecromancer = HasClass(item,
                        Necromancer.NecromancerCatalog.NecromancerClass,
                        Necromancer.NecromancerCatalog.ClassName);
                    if (expectedMonk) monkExpected++;
                    if (expectedNecromancer) necromancerExpected++;
                    window.DisplayItem(item, UnityEngine.Vector2.zero, 1);
                    string classes = window.Usable.text ?? "";
                    bool monk = ContainsClassName(classes,
                        Monk.MonkCatalog.ClassName);
                    bool necromancer = ContainsClassName(classes,
                        Necromancer.NecromancerCatalog.ClassName);
                    if ((expectedMonk && !monk) ||
                        (expectedNecromancer && !necromancer))
                        failures.Add(
                            $"{item.ItemName}:[{classes.Trim()}]");
                    else
                        rendered++;
                    window.CloseItemWindow();
                }
            }
            catch (Exception ex)
            {
                failures.Add("renderer exception: " + ex.Message);
            }
            finally
            {
                try { window.CloseItemWindow(); } catch { }
            }

            string missing = string.Join(", ", failures.Take(25));
            if (failures.Count > 25)
                missing += $", ... {failures.Count - 25} more";
            detail = $"rendered custom-class tooltips={rendered}/" +
                $"{items.Count}; Monk-eligible={monkExpected}; " +
                $"Necromancer-eligible={necromancerExpected}; " +
                $"missing=[{missing}]";
            return items.Count > 0 && rendered == items.Count &&
                failures.Count == 0;
        }

        private static bool IsUniversalNativeConsumable(Item item,
            Class[] native)
        {
            if (item == null ||
                item.RequiredSlot != Item.SlotType.General ||
                !item.Stackable || !item.Disposable ||
                item.ItemEffectOnClick == null ||
                item.TeachSpell != null || item.TeachSkill != null ||
                item.Classes == null || item.Classes.Count == 0)
                return false;

            return native.All(required => item.Classes.Contains(required));
        }

        private static bool HasClass(Item item, Class expected,
            string expectedName)
        {
            return item?.Classes?.Any(value =>
                ReferenceEquals(value, expected) ||
                (value != null && string.Equals(value.ClassName,
                    expectedName, StringComparison.OrdinalIgnoreCase))) == true;
        }

        private static bool ContainsClassName(string text, string className)
        {
            if (string.IsNullOrWhiteSpace(text) ||
                string.IsNullOrWhiteSpace(className)) return false;
            return text.Split((char[])null,
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(value => string.Equals(value.Trim(),
                    className, StringComparison.OrdinalIgnoreCase));
        }

        private static Class[] NativePlayableClasses()
        {
            return new[]
            {
                GameData.ClassDB?.Arcanist,
                GameData.ClassDB?.Druid,
                GameData.ClassDB?.Duelist,
                GameData.ClassDB?.Paladin,
                GameData.ClassDB?.Stormcaller,
                GameData.ClassDB?.Reaver
            };
        }
    }
}
