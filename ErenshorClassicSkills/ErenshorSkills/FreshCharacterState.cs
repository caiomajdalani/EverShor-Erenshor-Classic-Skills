using System;
using System.Collections.Generic;
using System.Linq;

namespace ErenshorSkills
{
    /// <summary>
    /// Clears scene-persistent equipment state that Erenshor's native custom
    /// character flow does not reset when the destination save field is empty.
    /// This is intentionally invoked only after a valid new-character request.
    /// </summary>
    internal static class FreshCharacterState
    {
        internal static void ClearInheritedAura()
        {
            Inventory inventory = GameData.PlayerInv;
            Stats stats = GameData.PlayerStats;
            ItemIcon slot = inventory?.AuraSlot;
            Spell slotAura = slot?.MyItem?.Aura;
            Spell activeAura = stats?.MyAura;
            var staleIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(slotAura?.Id))
                staleIds.Add(slotAura.Id);
            if (!string.IsNullOrEmpty(activeAura?.Id))
                staleIds.Add(activeAura.Id);

            int removedStatuses = 0;
            if (stats?.StatusEffects != null)
            {
                for (int index = stats.StatusEffects.Length - 1;
                    index >= 0; index--)
                {
                    Spell effect = stats.StatusEffects[index]?.Effect;
                    if (effect == null ||
                        (!ReferenceEquals(effect, slotAura) &&
                         !ReferenceEquals(effect, activeAura) &&
                         (string.IsNullOrEmpty(effect.Id) ||
                          !staleIds.Contains(effect.Id))))
                        continue;
                    stats.RemoveStatusEffect(index);
                    removedStatuses++;
                }
            }

            if (slot != null)
            {
                slot.MyItem = inventory?.Empty;
                slot.Quantity = 1;
                try { slot.UpdateSlotImage(); } catch { }
            }
            if (stats != null)
            {
                stats.MyAura = null;
                try { stats.CheckAuras(); } catch { }
            }
            if (GameData.CurrentCharacterSlot != null)
                GameData.CurrentCharacterSlot.AuraItem = string.Empty;

            int removedOther = ClearInheritedStatusEffects(stats);

            if (slotAura != null || activeAura != null ||
                removedStatuses > 0 || removedOther > 0)
                SkillsPlugin.Log?.LogInfo(
                    "Fresh character: cleared an inherited aura slot, " +
                    $"{removedStatuses} stale aura status effect(s) and " +
                    $"{removedOther} other inherited status effect(s).");
        }

        /// <summary>
        /// Drops every status effect still sitting on the shared player Stats
        /// when a new character takes over a slot. The aura pass above only
        /// removed aura-derived effects, so buffs belonging to the previous
        /// occupant — food, blessings, class auras — survived into the new
        /// character and then announced themselves one by one as they expired,
        /// filling the combat log with "has worn off of you" on a character
        /// that never cast them.
        /// </summary>
        private static int ClearInheritedStatusEffects(Stats stats)
        {
            if (stats?.StatusEffects == null) return 0;
            int removed = 0;
            for (int index = stats.StatusEffects.Length - 1;
                index >= 0; index--)
            {
                if (stats.StatusEffects[index]?.Effect == null) continue;
                try
                {
                    stats.RemoveStatusEffect(index);
                    removed++;
                }
                catch { }
            }
            try { stats.CalcStats(); } catch { }
            return removed;
        }

        internal static bool RunAuraLeakSelfTest(out string detail)
        {
            Inventory inventory = GameData.PlayerInv;
            Stats stats = GameData.PlayerStats;
            ItemIcon slot = inventory?.AuraSlot;
            SaveGameData save = GameData.CurrentCharacterSlot;
            if (inventory == null || stats == null || slot == null ||
                save == null)
            {
                detail = "live player aura state is unavailable";
                return false;
            }
            if ((slot.MyItem != null &&
                 !ReferenceEquals(slot.MyItem, inventory.Empty)) ||
                stats.MyAura != null ||
                !string.IsNullOrEmpty(save.AuraItem))
            {
                detail = "refused because the QA character has a legitimate " +
                    "equipped or saved aura";
                return false;
            }

            Item inherited = Monk.MonkAuras.StarterItem ??
                Necromancer.NecromancerAuras.StarterItem;
            if (inherited?.Aura == null)
            {
                detail = "cross-class test aura is unavailable";
                return false;
            }

            slot.MyItem = inherited;
            slot.Quantity = 1;
            save.AuraItem = inherited.Id;
            stats.CheckAuras();
            bool reproduced = ReferenceEquals(stats.MyAura, inherited.Aura) &&
                stats.CheckForStatus(inherited.Aura);

            ClearInheritedAura();

            bool cleared = (slot.MyItem == null ||
                    ReferenceEquals(slot.MyItem, inventory.Empty)) &&
                stats.MyAura == null &&
                string.IsNullOrEmpty(save.AuraItem) &&
                !stats.CheckForStatus(inherited.Aura);
            detail = reproduced && cleared
                ? $"reproduced inherited '{inherited.ItemName}', then cleared " +
                  "the slot, save ID, MyAura reference, and exact status effect"
                : $"reproduced={reproduced}; cleared={cleared}; slot=" +
                  $"{slot.MyItem?.Id ?? "<null>"}; saved=" +
                  $"{save.AuraItem ?? "<null>"}; active=" +
                  $"{stats.MyAura?.Id ?? "<null>"}";
            return reproduced && cleared;
        }

        internal static bool ClearIneligibleLoadedAura(out string detail)
        {
            Inventory inventory = GameData.PlayerInv;
            Stats stats = GameData.PlayerStats;
            Item item = inventory?.AuraSlot?.MyItem;
            if (inventory == null || stats == null ||
                item == null || ReferenceEquals(item, inventory.Empty) ||
                item.Aura == null)
            {
                detail = "no equipped aura required repair";
                return false;
            }

            Class playerClass = stats.CharacterClass;
            bool classAllowed = item.Classes == null ||
                item.Classes.Count == 0 ||
                item.Classes.Any(candidate => candidate != null &&
                    (ReferenceEquals(candidate, playerClass) ||
                     string.Equals(candidate.ClassName,
                         playerClass?.ClassName,
                         StringComparison.OrdinalIgnoreCase)));
            int requiredLevel = Math.Max(item.ItemLevel,
                item.Aura.RequiredLevel);
            bool levelAllowed = requiredLevel <= Math.Max(1, stats.Level);
            if (classAllowed && levelAllowed)
            {
                detail = $"kept legitimate aura '{item.ItemName}'";
                return false;
            }

            string reason = !classAllowed
                ? $"not usable by {playerClass?.DisplayName ?? "this class"}"
                : $"requires level {requiredLevel} on a level {stats.Level} character";
            string itemName = item.ItemName ?? item.Id ?? "<unnamed aura>";
            ClearInheritedAura();
            try { GameData.GM?.SaveGameData(wScene: false); } catch { }
            detail = $"removed inherited aura '{itemName}' because it is " +
                reason;
            SkillsPlugin.Log?.LogWarning(
                "Loaded-character aura repair: " + detail + ".");
            return true;
        }
    }
}
