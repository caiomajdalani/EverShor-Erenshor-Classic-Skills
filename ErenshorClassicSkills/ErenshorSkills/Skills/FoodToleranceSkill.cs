using UnityEngine;

namespace ErenshorSkills.Skills
{
    /// <summary>
    /// FOOD TOLERANCE — Inspired by EverQuest's Alcohol Tolerance, but
    /// broadened to cover all consumables in Erenshor. In EQ, drinking
    /// booze blurred your screen and Alcohol Tolerance reduced the effects.
    /// Since Erenshor's consumable system revolves around food and drink
    /// stat buffs rather than just alcohol, this skill rewards players for
    /// using all consumables — roasted meats, elixirs, draughts, everything.
    ///
    /// How it works:
    /// - Levels passively whenever you consume food or drink items
    /// - Higher skill = longer duration on all consumable buffs
    /// - At level 50: "Your constitution is legendary."
    ///
    /// This fills a niche the Erenshor community already expressed interest
    /// in (a "Food Buff Duration" mod exists on Thunderstore), but makes
    /// the bonus progressive and earned through gameplay.
    /// </summary>
    public static class FoodToleranceSkill
    {
        /// <summary>Percentage bonus to food/drink buff duration.</summary>
        public static float DurationBonus =>
            SkillsSaveManager.Data.FoodTolerance.Level * 0.556f;

        /// <summary>
        /// Called by the Harmony patch when a consumable is used.
        /// Awards XP and returns the duration multiplier.
        /// </summary>
        public static float OnConsumeItem(string itemName)
        {
            if (!SkillsPlugin.CfgEnableFoodTolerance.Value)
                return 1f;

            var skill = SkillsSaveManager.Data.FoodTolerance;
            skill.TimesUsed++;

            // Award XP for consuming items
            float xp = 8f;

            // Bonus XP for potent consumables (higher-tier food/drink)
            if (IsPotent(itemName))
                xp *= 2f;

            SkillXpEngine.AwardXp(skill, xp);

            float durationMult = 1f + DurationBonus / 100f;

            if (SkillsPlugin.CfgShowChatMessages.Value)
            {
                string bonusText = DurationBonus > 0f
                    ? $" <color=#AAAAAA>(duration +{DurationBonus:F0}%)</color>"
                    : "";
                ChatHelper.Send(
                    $"<color=#FFAB40>[Food Tolerance]</color> " +
                    $"You consume {itemName}.{bonusText}");
            }

            return durationMult;
        }

        /// <summary>Returns the title for current food tolerance level.</summary>
        public static string GetTitle()
        {
            int level = SkillsSaveManager.Data.FoodTolerance.Level;
            if (level >= 50) return "Iron Constitution";
            if (level >= 40) return "Seasoned Palette";
            if (level >= 30) return "Stout Constitution";
            if (level >= 20) return "Hearty Appetite";
            if (level >= 10) return "Developing Palate";
            return "Picky Eater";
        }

        private static bool IsPotent(string itemName)
        {
            string lower = (itemName ?? "").ToLowerInvariant();
            return lower.Contains("elixir") ||
                   lower.Contains("draught") ||
                   lower.Contains("potion") ||
                   lower.Contains("feast") ||
                   lower.Contains("aged") ||
                   lower.Contains("refined") ||
                   lower.Contains("roasted") ||
                   lower.Contains("grilled") ||
                   lower.Contains("stew") ||
                   lower.Contains("brewed");
        }
    }
}
