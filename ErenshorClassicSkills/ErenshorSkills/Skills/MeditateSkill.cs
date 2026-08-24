using UnityEngine;

namespace ErenshorSkills.Skills
{
    /// <summary>
    /// MEDITATE — In EverQuest, Meditate was THE caster skill. You'd sit
    /// down after every fight and watch your mana bar crawl back up while
    /// reading a spellbook that covered your entire screen. At higher levels
    /// the spellbook went away and mana regen improved significantly.
    /// "MEDDING" became such a core part of the game that it entered the
    /// MMO lexicon permanently.
    ///
    /// In Erenshor, all classes use mana. This skill:
    /// - Activates automatically while sitting out of combat
    /// - While meditating, mana regen rate increases based on skill level
    /// - XP awarded periodically while meditating
    /// - Standing, casting, taking damage, or entering combat cancels meditation
    /// </summary>
    public static class MeditateSkill
    {
        private static bool _isMeditating = false;
        private static float _lastMedTick = 0f;
        private static float _tickInterval = 3f;

        public static bool IsMeditating => _isMeditating;

        internal static void ResetRuntime()
        {
            _isMeditating = false;
            _lastMedTick = 0f;
        }

        /// <summary>Extra mana per regen tick while meditating.</summary>
        public static float BonusManaPerTick
        {
            get
            {
                float baseMana = SkillsPlugin.CfgMeditateBaseMana.Value;
                return Mathf.Clamp(baseMana +
                    SkillsSaveManager.Data.Meditate.Level * 0.556f, 0f, 10000f);
            }
        }

        public static void ToggleMeditate()
        {
            if (!SkillsPlugin.CfgEnableMeditate.Value) return;
            ChatHelper.Send(
                $"<color=#CE93D8>[Meditate]</color> " +
                $"Meditation activates automatically while sitting out of combat.");
        }

        /// <summary>
        /// Called every frame by Patch_MeditationTick.
        /// Detects the game's native sitting/rest state and awards bonus mana + XP
        /// only when the player is safely resting out of combat.
        /// </summary>
        public static void ProcessTick()
        {
            try
            {
                var stats = GameData.PlayerStats;
                if (stats == null) return;

                // Detect sitting — PlayerControl.Sitting is public bool
                bool isSitting = false;
                try
                {
                    isSitting = GameData.PlayerControl.Sitting;
                }
                catch { }

                bool canMeditate = isSitting && !GameData.InCombat &&
                    !GameData.PlayerTyping &&
                    stats.RecentDmg <= 0f && stats.RecentCast <= 0f;
                if (!canMeditate)
                {
                    _isMeditating = false;
                    return;
                }

                if (!_isMeditating)
                {
                    _isMeditating = true;
                    _lastMedTick = Time.time;
                }

                // Only active while mana is recovering — no XP or bonus at full mana
                if (stats.CurrentMana >= stats.GetCurrentMaxMana()) return;

                // Tick interval
                if (Time.time - _lastMedTick < _tickInterval) return;
                _lastMedTick = Time.time;

                var skill = SkillsSaveManager.Data.Meditate;

                // Award bonus mana on top of the game's natural regen
                float mana = BonusManaPerTick;
                stats.CurrentMana = Mathf.Min(
                    stats.CurrentMana + Mathf.RoundToInt(mana),
                    stats.GetCurrentMaxMana());

                // Only gain skill XP while actively recovering mana
                skill.TimesUsed++;
                SkillXpEngine.AwardXp(skill, 3f, showChat: false);
            }
            catch
            {
                _isMeditating = false;
            }
        }

    }
}
