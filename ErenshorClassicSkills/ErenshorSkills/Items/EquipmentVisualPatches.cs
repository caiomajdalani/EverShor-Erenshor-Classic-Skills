using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ErenshorSkills.Items
{
    /// <summary>
    /// Erenshor has two Bracer equipment slots but one pair of wrist model
    /// roots. Its visual pass processes slots in inventory order, so an empty
    /// second wrist slot can erase the first slot's model. When an addon
    /// bracer is worn, process empty wrists first and occupied wrists last so
    /// the final rendered state reflects an actually equipped item.
    /// </summary>
    [HarmonyPatch(typeof(ModularParts), nameof(ModularParts.UpdatePlayerVisuals))]
    public static class Patch_ModularParts_AddonBracerOrder
    {
        public static void Prefix(ref List<ItemIcon> StatSlots)
        {
            if (StatSlots == null || StatSlots.Count < 2) return;
            var bracers = new List<ItemIcon>();
            bool hasAddonBracer = false;
            foreach (ItemIcon slot in StatSlots)
            {
                if (slot == null || slot.ThisSlotType != Item.SlotType.Bracer)
                    continue;
                bracers.Add(slot);
                Item item = slot.MyItem;
                if (item != null && item != GameData.PlayerInv?.Empty &&
                    (ItemFactory.IsCustomItem(item) ||
                     (!string.IsNullOrEmpty(item.Id) &&
                      item.Id.StartsWith("CS_", StringComparison.OrdinalIgnoreCase))))
                    hasAddonBracer = true;
            }
            if (!hasAddonBracer || bracers.Count < 2) return;

            var reordered = new List<ItemIcon>(StatSlots.Count);
            foreach (ItemIcon slot in StatSlots)
                if (slot == null || slot.ThisSlotType != Item.SlotType.Bracer)
                    reordered.Add(slot);
            foreach (ItemIcon slot in bracers)
                if (slot.MyItem == null || slot.MyItem == GameData.PlayerInv?.Empty)
                    reordered.Add(slot);
            foreach (ItemIcon slot in bracers)
                if (slot.MyItem != null && slot.MyItem != GameData.PlayerInv?.Empty)
                    reordered.Add(slot);
            StatSlots = reordered;
        }
    }
}
