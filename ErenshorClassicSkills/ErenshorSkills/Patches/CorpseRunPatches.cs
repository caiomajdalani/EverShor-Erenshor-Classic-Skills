using HarmonyLib;

namespace ErenshorSkills.Patches
{
    /// <summary>Capture only a completed player death; death-prevention effects leave Alive true.</summary>
    [HarmonyPatch(typeof(Character), "DoDeath")]
    public static class Patch_CorpseRunDeath
    {
        public static void Postfix(Character __instance)
        {
            try { Features.CorpseRunSystem.OnPlayerDeath(__instance); }
            catch (System.Exception ex)
            {
                SkillsPlugin.Log.LogError(
                    "Corpse Run death patch failed safely: " + ex);
            }
        }
    }

    /// <summary>
    /// GameManager.SaveGameData returns void even though its inner native save
    /// returns a success flag. Observe that flag so corpse transactions never
    /// declare a commit after a failed disk write, then mirror the successfully
    /// saved native inventory into the addon's recovery sidecar. The latter is
    /// required because Erenshor's inventory hotkey bypasses Inventory.ToggleInv.
    /// </summary>
    [HarmonyPatch(typeof(GameData), "SaveData")]
    public static class Patch_CorpseRunNativeSaveResult
    {
        public static void Postfix(bool __result)
        {
            Features.CorpseRunSystem.ObserveNativeSave(__result);

            if (!__result || !SkillsSaveManager.HasLoaded ||
                GameData.PlayerInv?.StoredSlots == null) return;

            try
            {
                Items.ItemFactory.SaveCustomInventory();
                if (!SkillsSaveManager.Save())
                {
                    SkillsPlugin.Log.LogError(
                        "Native save succeeded, but Classic Skills sidecars could not be synchronized.");
                }
            }
            catch (System.Exception ex)
            {
                // The native save has already completed successfully. Never
                // turn an optional recovery-snapshot failure into a game save
                // failure; retain the old atomic sidecar and report the issue.
                SkillsPlugin.Log.LogError(
                    "Native save succeeded, but Classic Skills sidecar synchronization failed: " + ex);
            }
        }
    }

    /// <summary>
    /// When the player corpse is closer than a native NPC corpse, reserve the
    /// Loot key for belongings recovery so both windows cannot activate in the
    /// same frame.
    /// </summary>
    [HarmonyPatch(typeof(PlayerCombat), "FindNearestLootable")]
    public static class Patch_CorpseRunLootPriority
    {
        public static void Postfix(ref Character __result)
        {
            if (Features.CorpseRunSystem.ShouldSuppressNativeLoot(__result))
                __result = null;
        }
    }
}
