using UnityEngine;

namespace ErenshorSkills.Skills
{
    /// <summary>
    /// BIND WOUND — In EverQuest, Bind Wound was how warriors and other
    /// non-casters recovered HP between fights. You'd sit down, bandage
    /// yourself, and watch "You have been bandaged for X hit points."
    /// scroll by. It was essential for downtime management and gave melee
    /// classes a way to reduce their dependency on healers.
    ///
    /// In Erenshor, this adds an active out-of-combat self-heal:
    /// - Press the hotkey while not in combat
    /// - Heals a percentage of your max HP based on skill level
    /// - Base is configurable; healing scales by +0.15% max HP per level
    /// - Has a cooldown (reduced by skill)
    /// - Must be below 70% HP to use (EQ's old restriction)
    /// - Higher skill = more HP healed and shorter cooldown
    ///
    /// At level 40+: HP threshold increases to 90%.
    /// </summary>
    public static class BindWoundSkill
    {
        private static float _lastUseTime = float.NegativeInfinity;

        internal static void ResetRuntime()
        {
            _lastUseTime = float.NegativeInfinity;
        }

        /// <summary>Cooldown in seconds.</summary>
        public static float Cooldown =>
            SkillsPlugin.CfgTestCooldowns.Value
                ? 1f
                : Mathf.Max(45f - SkillsSaveManager.Data.BindWound.Level * 0.5f, 15f);

        /// <summary>Safe, finite cooldown time remaining for HUD display.</summary>
        public static float CooldownRemaining
        {
            get
            {
                if (float.IsNegativeInfinity(_lastUseTime)) return 0f;
                float remaining = Cooldown - (Time.time - _lastUseTime);
                if (float.IsNaN(remaining) || float.IsInfinity(remaining))
                    return 0f;
                return Mathf.Max(0f, remaining);
            }
        }

        public static bool IsReady => CooldownRemaining <= 0f;

        /// <summary>Percentage of max HP healed per use (0-100).</summary>
        public static float HealPercent
        {
            get
            {
                float basePct = SkillsPlugin.CfgBindWoundBaseHealPercent.Value;
                int level = SkillsSaveManager.Data.BindWound.Level;
                // Scales to 30% at skill 180: (30 - 3) / 180 = 0.15
                return Mathf.Clamp(basePct + level * 0.15f, 0f, 100f);
            }
        }

        /// <summary>HP threshold — must be below this % to bind wound.</summary>
        public static float HpThreshold =>
            SkillsSaveManager.Data.BindWound.Level >= 40 ? 0.9f : 0.7f;

        /// <summary>Calculate the actual HP healed based on a given max HP.</summary>
        public static int CalculateHeal(int maxHP)
        {
            return Mathf.Max(1, Mathf.RoundToInt(maxHP * HealPercent / 100f));
        }

        /// <summary>
        /// Find one Patchwork Bandage in inventory without mutating it.
        /// </summary>
        private static ItemIcon FindBandage()
        {
            try
            {
                var inv = GameData.PlayerInv;
                if (inv?.StoredSlots == null) return null;

                foreach (var slot in inv.StoredSlots)
                {
                    if (slot?.MyItem == null) continue;
                    if (slot.MyItem.ItemName != "Patchwork Bandages") continue;
                    return slot;
                }
            }
            catch { }
            return null;
        }

        public static void TryBindWound()
        {
            if (!SkillsPlugin.CfgEnableBindWound.Value) return;

            // Cooldown
            if (Time.time - _lastUseTime < Cooldown)
            {
                float remaining = Cooldown - (Time.time - _lastUseTime);
                ChatHelper.Send(
                    $"<color=#EF5350>[Bind Wound]</color> " +
                    $"You must wait {remaining:F0}s before binding wounds again.");
                return;
            }

            // Must be out of combat
            try
            {
                if (GameData.InCombat)
                {
                    ChatHelper.Send(
                        $"<color=#EF5350>[Bind Wound]</color> " +
                        $"You cannot bind wounds while in combat!");
                    return;
                }
            }
            catch (System.Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    $"Bind Wound stopped because combat state was unavailable: {ex.Message}");
                return;
            }

            // Check HP threshold
            var playerStats = GameData.PlayerStats;
            if (playerStats == null || playerStats.CurrentMaxHP <= 0)
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Bind Wound]</color> Your health data is unavailable.");
                return;
            }
            int maxHP = playerStats.CurrentMaxHP;
            float hpRatio = (float)playerStats.CurrentHP / playerStats.CurrentMaxHP;
            if (hpRatio >= HpThreshold)
            {
                int pct = Mathf.RoundToInt(HpThreshold * 100f);
                ChatHelper.Send(
                    $"<color=#EF5350>[Bind Wound]</color> " +
                    $"You must be below {pct}% HP to bind wounds.");
                return;
            }

            // Check for bandages in inventory without consuming yet.
            var bandageSlot = FindBandage();
            if (bandageSlot == null)
            {
                ChatHelper.Send(
                    $"<color=#EF5350>[Bind Wound]</color> " +
                    $"You need Patchwork Bandages to bind wounds. " +
                    $"Craft them with Tailoring.");
                return;
            }

            var skill = SkillsSaveManager.Data.BindWound;
            int oldLevel = skill.Level;
            float oldXp = skill.CurrentXp;
            int oldTimesUsed = skill.TimesUsed;
            int oldSuccesses = skill.Successes;
            int oldFailures = skill.Failures;
            int oldHp = playerStats.CurrentHP;
            float oldLastUse = _lastUseTime;
            Items.BagSystem.MutationSnapshot mutation = null;
            string resultMessage = null;
            try
            {
                mutation = Items.BagSystem.BeginExternalMutation();

                // Consume exactly one only after the complete operation has a
                // rollback snapshot. Patchwork Bandages are stackable.
                if (bandageSlot.Quantity <= 1)
                {
                    bandageSlot.MyItem = GameData.PlayerInv.Empty;
                    bandageSlot.Quantity = 1;
                }
                else
                    bandageSlot.Quantity--;
                bandageSlot.UpdateSlotImage();

                _lastUseTime = Time.time;

                // Skill check determines if the bandage is effective
                bool success = SkillXpEngine.SkillCheck(skill, 1.0f,
                    xpOnSuccess: 15f, xpOnFailure: 5f,
                    requestSave: false);

                if (success)
                {
                    int heal = CalculateHeal(maxHP);
                    int before = playerStats.CurrentHP;
                    playerStats.CurrentHP = Mathf.Min(
                        playerStats.CurrentHP + heal, playerStats.CurrentMaxHP);
                    heal = playerStats.CurrentHP - before; // Actual amount healed

                    resultMessage =
                        $"<color=#EF5350>[Bind Wound]</color> " +
                        $"You have been bandaged for " +
                        $"<color=#FFFFFF>{heal}</color> hit points " +
                        $"<color=#AAAAAA>({HealPercent:F1}% of max HP)</color>.";
                }
                else
                {
                    string[] failMsgs = new string[]
                    {
                        "The bandage slips. You fail to bind your wounds.",
                        "You fumble with the bandage materials.",
                        "Your wound dressing doesn't take hold.",
                        "The bandage is too loose to be effective."
                    };
                    resultMessage =
                        $"<color=#EF5350>[Bind Wound]</color> " +
                        $"<color=#AAAAAA>{failMsgs[Random.Range(0, failMsgs.Length)]}</color>";
                }

                if (!Items.BagSystem.CommitExternalMutation("bind-wound", mutation))
                    throw new System.InvalidOperationException(
                        "The inventory transaction could not be saved.");

                // Do not persist progression before the bandage transaction:
                // a crash must never grant skill XP while restoring the item.
                if (!SkillsSaveManager.Save())
                    SkillsPlugin.Log.LogWarning(
                        "Bind Wound succeeded, but skill progression will be retried on the next save.");

                ChatHelper.Send(resultMessage);
            }
            catch (System.Exception ex)
            {
                // Restore non-inventory state before the inventory rollback's
                // native save, so a failed transaction cannot grant a free heal.
                skill.Level = oldLevel;
                skill.CurrentXp = oldXp;
                skill.TimesUsed = oldTimesUsed;
                skill.Successes = oldSuccesses;
                skill.Failures = oldFailures;
                playerStats.CurrentHP = oldHp;
                _lastUseTime = oldLastUse;
                if (mutation != null)
                    Items.BagSystem.RollbackExternalMutation(mutation);
                SkillsSaveManager.Save();
                SkillsPlugin.Log.LogError($"Bind Wound failed and was rolled back: {ex}");
                ChatHelper.Send(
                    "<color=#EF5350>[Bind Wound]</color> The attempt was cancelled; your bandage was restored.");
            }
        }
    }
}
