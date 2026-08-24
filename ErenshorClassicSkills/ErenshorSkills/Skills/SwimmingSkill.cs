using UnityEngine;

namespace ErenshorSkills.Skills
{
    /// <summary>
    /// SWIMMING — In EverQuest, Swimming was one of those skills you'd
    /// frantically try to level while drowning in Qeynos Aqueducts.
    /// Higher skill meant faster movement in water and (critically)
    /// less chance of drowning. Every EQ player remembers swimming
    /// skill-ups scrolling by as they desperately tried to reach air.
    ///
    /// In Erenshor, several zones have water bodies (Port Azure,
    /// Duskenlight Coast, Blacksalt Strand, etc.). This skill:
    /// - Levels passively while moving through water
    /// - Increases swim speed proportional to level
    /// - XP ticks every few seconds while submerged
    /// </summary>
    public static class SwimmingSkill
    {
        private static float _lastSwimTick = 0f;
        private static float _tickInterval = 5f; // XP tick every 5 seconds
        private static bool _wasInWater = false;

        internal static void ResetRuntime()
        {
            _lastSwimTick = 0f;
            _wasInWater = false;
        }

        /// <summary>Swim speed bonus as a multiplier (1.0 = no bonus).</summary>
        public static float SpeedMultiplier =>
            1f + SkillsSaveManager.Data.Swimming.Level * 0.003f;

        /// <summary>
        /// Called every frame by the swim detection patch.
        /// Awards XP periodically while the player is in water.
        /// </summary>
        public static void OnSwimTick(bool isInWater, bool isMoving)
        {
            if (!SkillsPlugin.CfgEnableSwimming.Value) return;

            // Entering water message
            if (isInWater && !_wasInWater)
            {
                if (SkillsPlugin.CfgShowChatMessages.Value)
                {
                    var skill = SkillsSaveManager.Data.Swimming;
                    if (skill.Level < 5)
                        ChatHelper.Send(
                            "<color=#4FC3F7>[Swimming]</color> " +
                            "<color=#AAAAAA>You wade into the water...</color>");
                }
            }
            _wasInWater = isInWater;

            if (!isInWater) return;

            // XP ticks while swimming
            if (Time.time - _lastSwimTick < _tickInterval) return;
            _lastSwimTick = Time.time;

            var s = SkillsSaveManager.Data.Swimming;
            s.TimesUsed++;

            // More XP if actually moving vs just standing in water
            float xp = isMoving ? 8f : 3f;

            // EQ style: you could also get skillups just standing in water,
            // but slower. Moving was the way to go.
            bool leveled = SkillXpEngine.AwardXp(s, xp, showChat: false);

            // Periodic milestone messages
            if (leveled && s.Level % 10 == 0)
            {
                ChatHelper.Send(
                    $"<color=#4FC3F7>[Swimming]</color> " +
                    $"You feel more confident in the water. " +
                    $"<color=#AAAAAA>(Speed +{(SpeedMultiplier - 1f) * 100f:F0}%)</color>");
            }
        }
    }
}
