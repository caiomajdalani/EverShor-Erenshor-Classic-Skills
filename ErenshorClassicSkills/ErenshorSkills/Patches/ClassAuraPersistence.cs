using System;
using HarmonyLib;

namespace ErenshorSkills.Patches
{
    /// <summary>
    /// Gives addon class auras a character-sidecar fallback in addition to
    /// Erenshor's native AuraItem field. Character selection can ask the item
    /// database for an addon ID before the addon catalog is ready; without a
    /// retry, the native loader leaves the Aura slot empty and the next save
    /// permanently overwrites the valid ID.
    /// </summary>
    internal static class ClassAuraPersistence
    {
        private const string NecromancerPrefix = "CS_NECRO_AURA_ITEM_";
        private const string MonkPrefix = "CS_MONK_AURA_ITEM_";

        internal static bool IsClassAuraId(string id) =>
            !string.IsNullOrWhiteSpace(id) &&
            (id.StartsWith(NecromancerPrefix,
                 StringComparison.OrdinalIgnoreCase) ||
             id.StartsWith(MonkPrefix,
                 StringComparison.OrdinalIgnoreCase));

        internal static void CaptureBeforeNativeSave()
        {
            if (!SkillsSaveManager.HasLoaded ||
                SkillsSaveManager.Data == null) return;

            Inventory inventory = GameData.PlayerInv;
            Item equipped = inventory?.AuraSlot?.MyItem;
            if (equipped == null || ReferenceEquals(equipped, inventory.Empty))
            {
                SkillsSaveManager.Data.EquippedClassAuraId = string.Empty;
                SkillsSaveManager.Save();
                return;
            }

            SkillsSaveManager.Data.EquippedClassAuraId =
                IsClassAuraId(equipped.Id) ? equipped.Id : string.Empty;
            SkillsSaveManager.Save();
        }

        internal static bool RestoreAfterEquipmentLoad()
        {
            return TryRestore(GameData.CurrentCharacterSlot?.AuraItem,
                "native character save");
        }

        internal static bool RestoreAfterCharacterLoad()
        {
            string nativeId = GameData.CurrentCharacterSlot?.AuraItem;
            if (TryRestore(nativeId, "native character save")) return true;
            return TryRestore(
                SkillsSaveManager.Data?.EquippedClassAuraId,
                "Classic Skills character sidecar");
        }

        private static bool TryRestore(string id, string source)
        {
            if (!IsClassAuraId(id)) return false;
            Inventory inventory = GameData.PlayerInv;
            ItemIcon slot = inventory?.AuraSlot;
            if (inventory == null || slot == null) return false;

            Item current = slot.MyItem;
            if (current != null && !ReferenceEquals(current, inventory.Empty))
                return string.Equals(current.Id, id,
                    StringComparison.OrdinalIgnoreCase);

            Necromancer.NecromancerCatalog.TryInitialize();
            Monk.MonkCatalog.TryInitialize();
            Item item = Items.ItemFactory.GetItemById(id);
            if (item?.Aura == null) return false;

            slot.MyItem = item;
            slot.Quantity = 1;
            slot.UpdateSlotImage();
            if (GameData.CurrentCharacterSlot != null)
                GameData.CurrentCharacterSlot.AuraItem = item.Id;
            try
            {
                GameData.PlayerStats?.CheckAuras();
                inventory.UpdatePlayerInventory();
            }
            catch { }
            SkillsPlugin.Log?.LogInfo(
                $"Restored class aura '{item.ItemName}' from {source}.");
            return true;
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.SaveGameData))]
    internal static class Patch_ClassAuraNativeSave
    {
        private static void Prefix() =>
            ClassAuraPersistence.CaptureBeforeNativeSave();
    }

    [HarmonyPatch(typeof(CharSelectManager), "LoadEquipment")]
    internal static class Patch_ClassAuraEquipmentLoad
    {
        private static void Postfix() =>
            ClassAuraPersistence.RestoreAfterEquipmentLoad();
    }
}
