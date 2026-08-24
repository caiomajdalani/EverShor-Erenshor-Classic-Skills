using System.Collections.Generic;
using System.Linq;

namespace ErenshorSkills
{
    /// <summary>
    /// Acquisition rule shared by the add-on's Monk and Necromancer city
    /// merchants. A class trainer stocks starter-tier equipment only: weapons
    /// and armor above level 10 come from drops or crafting instead. Charms,
    /// auras, and the tomes or scrolls a trainer exists to teach stay
    /// purchasable at every tier.
    /// </summary>
    internal static class ClassMerchantStock
    {
        internal const int MaxEquipmentLevel = 10;

        /// <summary>
        /// True for a weapon or a worn armor piece. Aura and Charm are
        /// deliberately excluded so stronger charms stay on the shelf, and
        /// General covers the technique tomes, spell scrolls, and recipes a
        /// trainer sells. Anything that teaches a skill or spell is treated as
        /// a book regardless of slot, so a native high-level skill book can
        /// never be mistaken for equipment.
        /// </summary>
        internal static bool IsWeaponOrArmor(Item item)
        {
            if (item == null) return false;
            if (item.TeachSkill != null || item.TeachSpell != null)
                return false;
            if (item.ThisWeaponType != Item.WeaponType.None) return true;
            switch (item.RequiredSlot)
            {
                case Item.SlotType.General:
                case Item.SlotType.Aura:
                case Item.SlotType.Charm:
                    return false;
                default:
                    return true;
            }
        }

        internal static bool IsStockable(Item item) =>
            item != null && (!IsWeaponOrArmor(item) ||
                item.ItemLevel <= MaxEquipmentLevel);

        internal static IEnumerable<Item> Filter(IEnumerable<Item> items) =>
            (items ?? Enumerable.Empty<Item>()).Where(IsStockable);

        /// <summary>
        /// Names every stocked item that breaks the rule so a merchant
        /// self-test can report the exact offender instead of a bare count.
        /// </summary>
        internal static IEnumerable<string> FindViolations(
            IEnumerable<Item> items) =>
            (items ?? Enumerable.Empty<Item>())
                .Where(item => item != null && !IsStockable(item))
                .Select(item => item.ItemName + " (L" + item.ItemLevel + ")");
    }
}
