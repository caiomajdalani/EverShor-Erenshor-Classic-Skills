using UnityEngine;

namespace ErenshorSkills.Skills
{
    /// <summary>
    /// Simulates skill bonuses for SimPlayers (AI party members).
    /// Instead of tracking actual skill XP, we derive "virtual skill levels"
    /// from the sim player's character level and class, then apply the same
    /// damage/heal multipliers that the player gets from real skills.
    ///
    /// This keeps sim players competitive with the player as both level up,
    /// preventing the player's skill bonuses from making sims feel weak.
    ///
    /// Virtual skill level formula: (simLevel * 5) + 5 (same as player skill cap formula)
    ///   Level 1 sim  → skill 10   → tiny bonus
    ///   Level 10 sim → skill 55   → moderate bonus
    ///   Level 20 sim → skill 105  → significant bonus
    ///   Level 35 sim → skill 180  → cap bonus
    ///
    /// Class determines WHICH bonuses apply:
    ///   Melee classes (Duelist, Paladin, Reaver): Combat skill bonus
    ///   Caster classes (Arcanist, Stormcaller, Druid): Magic skill bonuses
    ///   Hybrid classes get both at reduced rates.
    /// </summary>
    public static class SimPlayerSkills
    {
        /// <summary>
        /// Get the virtual skill level for a sim player.
        /// Mirrors the player's skill cap formula: (level * 5) + 5.
        /// </summary>
        public static int GetVirtualSkillLevel(int simLevel)
        {
            return Mathf.Clamp(simLevel * 5 + 5, 0, 180);
        }

        /// <summary>
        /// Check if a Character is a sim player (not the real player, not a mob).
        /// </summary>
        public static bool IsSimPlayer(Character character)
        {
            if (!SkillsPlugin.CfgEnableSimPlayerSkills.Value) return false;
            if (character == null) return false;
            if (!character.isNPC) return false;
            try
            {
                var npc = character.MyNPC;
                if (npc == null) return false;
                return npc.ThisSim != null;
            }
            catch { return false; }
        }

        /// <summary>
        /// Get melee damage multiplier for a sim player based on level and class.
        /// Melee classes get full bonus, casters get half, hybrids get 75%.
        /// </summary>
        public static float GetMeleeDamageMultiplier(Character simChar)
        {
            if (!SkillsPlugin.CfgEnableCombatSkills.Value) return 1f;
            try
            {
                int level = simChar.MyStats?.Level ?? 0;
                if (level <= 0) return 1f;
                string className = simChar.MyStats?.CharacterClass?.ClassName ?? "";
                float classScale = GetMeleeScale(className);
                if (classScale <= 0f) return 1f;
                int vSkill = GetVirtualSkillLevel(level);
                float bonus = vSkill * Mathf.Max(0f,
                    SkillsPlugin.CfgCombatSkillDamagePerLevel.Value) * classScale;
                return 1f + bonus / 100f;
            }
            catch { return 1f; }
        }

        /// <summary>
        /// Get spell damage multiplier for a sim player (Evocation equivalent).
        /// Casters get full bonus, melee gets none, hybrids get 75%.
        /// </summary>
        public static float GetSpellDamageMultiplier(Character simChar)
        {
            if (!SkillsPlugin.CfgEnableMagicSkills.Value) return 1f;
            try
            {
                int level = simChar.MyStats?.Level ?? 0;
                if (level <= 0) return 1f;
                string className = simChar.MyStats?.CharacterClass?.ClassName ?? "";
                float classScale = GetMagicScale(className);
                if (classScale <= 0f) return 1f;
                int vSkill = GetVirtualSkillLevel(level);
                float bonus = vSkill * Mathf.Max(0f,
                    SkillsPlugin.CfgEvocationDamagePerLevel.Value);
                return 1f + bonus * classScale;
            }
            catch { return 1f; }
        }

        /// <summary>
        /// Get heal multiplier for a sim player (Alteration equivalent).
        /// Healer classes get full bonus, others get partial.
        /// </summary>
        public static float GetHealMultiplier(Character simChar)
        {
            if (!SkillsPlugin.CfgEnableMagicSkills.Value) return 1f;
            try
            {
                int level = simChar.MyStats?.Level ?? 0;
                if (level <= 0) return 1f;
                string className = simChar.MyStats?.CharacterClass?.ClassName ?? "";
                float classScale = GetHealScale(className);
                if (classScale <= 0f) return 1f;
                int vSkill = GetVirtualSkillLevel(level);
                float bonus = vSkill * Mathf.Max(0f,
                    SkillsPlugin.CfgAlterationHealPerLevel.Value);
                return 1f + bonus * classScale;
            }
            catch { return 1f; }
        }

        // ── Class scaling factors ───────────────────────────────────
        // Determines how much of each bonus type a class receives.
        // 1.0 = full bonus, 0.75 = hybrid, 0.5 = off-spec, 0.0 = none

        private static float GetMeleeScale(string className)
        {
            switch (className)
            {
                case "Duelist":     return 1.0f;    // Pure melee (Windblade in-game)
                case "Paladin":     return 0.85f;   // Melee/healer hybrid
                case "Reaver":      return 0.9f;    // Melee/magic hybrid
                case "Druid":       return 0.5f;    // Caster with some melee
                case "Stormcaller": return 0.25f;   // Pure caster, minimal melee
                case "Arcanist":    return 0.25f;   // Pure caster, minimal melee
                default:            return 0.5f;
            }
        }

        private static float GetMagicScale(string className)
        {
            switch (className)
            {
                case "Arcanist":    return 1.0f;    // Pure caster
                case "Stormcaller": return 1.0f;    // Pure caster
                case "Druid":       return 0.85f;   // Caster/healer hybrid
                case "Reaver":      return 0.6f;    // Melee/magic hybrid
                case "Paladin":     return 0.4f;    // Melee with some magic
                case "Duelist":     return 0.0f;    // No magic
                default:            return 0.3f;
            }
        }

        private static float GetHealScale(string className)
        {
            switch (className)
            {
                case "Druid":       return 1.0f;    // Primary healer
                case "Paladin":     return 0.85f;   // Off-healer
                case "Arcanist":    return 0.5f;    // Some heals
                case "Stormcaller": return 0.4f;    // Minimal heals
                case "Reaver":      return 0.3f;    // Self-heal only
                case "Duelist":     return 0.0f;    // No heals
                default:            return 0.3f;
            }
        }
    }
}
