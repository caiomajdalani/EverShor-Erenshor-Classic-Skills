using System;
using UnityEngine;

namespace ErenshorSkills.Skills
{
    /// <summary>
    /// MAGIC SKILLS — EverQuest's magic system tracked proficiency in
    /// four schools of magic: Evocation, Abjuration, Alteration, and
    /// Conjuration. Each spell cast raised the corresponding skill,
    /// and higher skill meant more powerful spells.
    ///
    /// In Erenshor, we classify spells by their SpellType:
    ///   Evocation   — Direct damage (Damage, AE, PBAE)
    ///   Abjuration  — Buffs and wards (Beneficial)
    ///   Alteration  — Healing (Heal)
    ///   Conjuration — Pets and DoTs (Pet, StatusEffect with damage)
    ///
    /// Bonuses per skill level (at cap 180):
    ///   Evocation   — +0.08% spell damage per level = +14.4% at 180
    ///   Abjuration  — +0.3% buff duration per level = +54% at 180
    ///   Alteration  — +0.08% heal amount per level  = +14.4% at 180
    ///   Conjuration — +0.08% DoT/summoned-pet damage per level = +14.4% at 180
    /// </summary>
    public static class MagicSkills
    {
        public enum MagicSchool
        {
            None,
            Evocation,    // Direct damage
            Abjuration,   // Buffs/wards
            Alteration,   // Heals
            Conjuration   // Pets/DoTs
        }

        /// <summary>
        /// Intent overrides keyed by Spell.Id. Addon spells whose behaviour is
        /// implemented in our own runtime carry SpellType.Misc, which the
        /// switch below would file under Abjuration no matter what they
        /// actually do. Catalogs register the real school here so those spells
        /// train and benefit from the right skill.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string,
            MagicSchool> SchoolOverrides =
            new System.Collections.Generic.Dictionary<string, MagicSchool>(
                StringComparer.OrdinalIgnoreCase);

        public static void RegisterSchoolOverride(string spellId,
            MagicSchool school)
        {
            if (string.IsNullOrEmpty(spellId) || school == MagicSchool.None)
                return;
            SchoolOverrides[spellId] = school;
        }

        /// <summary>Classify a spell into a magic school based on its SpellType.</summary>
        public static MagicSchool ClassifySpell(Spell spell)
        {
            if (spell == null) return MagicSchool.None;
            if (!string.IsNullOrEmpty(spell.Id) &&
                SchoolOverrides.TryGetValue(spell.Id, out MagicSchool override_))
                return override_;

            switch (spell.Type)
            {
                case Spell.SpellType.Damage:
                case Spell.SpellType.AE:
                case Spell.SpellType.PBAE:
                    return MagicSchool.Evocation;

                case Spell.SpellType.Beneficial:
                    return MagicSchool.Abjuration;

                case Spell.SpellType.Heal:
                    return MagicSchool.Alteration;

                case Spell.SpellType.Pet:
                case Spell.SpellType.StatusEffect:
                    return MagicSchool.Conjuration;

                case Spell.SpellType.Misc:
                    // Misc spells: if they do damage, Evocation; otherwise Abjuration
                    if (spell.TargetDamage > 0)
                        return MagicSchool.Evocation;
                    return MagicSchool.Abjuration;

                default:
                    return MagicSchool.None;
            }
        }

        /// <summary>Get the SkillEntry for a magic school.</summary>
        public static SkillEntry GetSkillEntry(MagicSchool school)
        {
            if (!SkillsSaveManager.HasLoaded) return null;
            var data = SkillsSaveManager.Data;
            switch (school)
            {
                case MagicSchool.Evocation:   return data.Evocation;
                case MagicSchool.Abjuration:  return data.Abjuration;
                case MagicSchool.Alteration:  return data.Alteration;
                case MagicSchool.Conjuration:  return data.Conjuration;
                default: return null;
            }
        }

        /// <summary>Award XP when a spell is cast. Called from Harmony patch.</summary>
        public static void OnSpellCast(Spell spell)
        {
            if (spell == null) return;
            if (!SkillsSaveManager.HasLoaded) return;
            if (!SkillsPlugin.CfgEnableMagicSkills.Value) return;

            var school = ClassifySpell(spell);
            if (school == MagicSchool.None) return;

            var skill = GetSkillEntry(school);
            if (skill == null) return;

            // XP scales with spell mana cost (harder spells = more XP)
            float xp = 8f + spell.ManaCost * 0.15f;
            float difficulty = 1.0f;

            SkillXpEngine.SkillCheck(skill, difficulty,
                xpOnSuccess: xp, xpOnFailure: xp * 0.5f);
        }

        /// <summary>Get spell damage multiplier from Evocation skill.</summary>
        public static float GetSpellDamageMultiplier()
        {
            if (!SkillsSaveManager.HasLoaded) return 1f;
            if (!SkillsPlugin.CfgEnableMagicSkills.Value) return 1f;
            var skill = SkillsSaveManager.Data.Evocation;
            // +0.08% per level = +14.4% at 180
            return 1f + skill.Level * Mathf.Max(0f,
                SkillsPlugin.CfgEvocationDamagePerLevel.Value);
        }

        /// <summary>Get heal amount multiplier from Alteration skill.</summary>
        public static float GetHealMultiplier()
        {
            if (!SkillsSaveManager.HasLoaded) return 1f;
            if (!SkillsPlugin.CfgEnableMagicSkills.Value) return 1f;
            var skill = SkillsSaveManager.Data.Alteration;
            // +0.08% per level = +14.4% at 180
            return 1f + skill.Level * Mathf.Max(0f,
                SkillsPlugin.CfgAlterationHealPerLevel.Value);
        }

        /// <summary>Get buff duration multiplier from Abjuration skill.</summary>
        public static float GetBuffDurationMultiplier()
        {
            if (!SkillsSaveManager.HasLoaded) return 1f;
            if (!SkillsPlugin.CfgEnableMagicSkills.Value) return 1f;
            var skill = SkillsSaveManager.Data.Abjuration;
            // +0.3% per level = +54% at 180
            return 1f + skill.Level * Mathf.Max(0f,
                SkillsPlugin.CfgAbjurationDurationPerLevel.Value);
        }

        /// <summary>Get the damage multiplier for Conjuration DoTs and summoned pets.</summary>
        public static float GetConjurationMultiplier()
        {
            if (!SkillsSaveManager.HasLoaded) return 1f;
            if (!SkillsPlugin.CfgEnableMagicSkills.Value) return 1f;
            var skill = SkillsSaveManager.Data.Conjuration;
            // +0.08% per level = +14.4% at 180
            return 1f + skill.Level * Mathf.Max(0f,
                SkillsPlugin.CfgConjurationDamagePerLevel.Value);
        }

        /// <summary>
        /// True only for a native summoned NPC that is still owned by the
        /// current player. Checking both flags avoids treating ordinary charm,
        /// SimPlayers, or another caster's summon as the player's pet.
        /// </summary>
        public static bool IsPlayerSummonedPet(Character attacker)
        {
            try
            {
                Character player = GameData.PlayerControl?.Myself;
                return attacker != null && player != null && attacker != player &&
                    attacker.isNPC && attacker.Master == player &&
                    attacker.MyNPC != null && attacker.MyNPC.SummonedByPlayer;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Scale real positive damage while preserving native sentinel values
        /// such as -1 and zero. Physical, spell, proc, and DoT paths therefore
        /// use identical rounding.
        /// </summary>
        internal static int ScalePositiveDamage(int damage, float multiplier)
        {
            if (damage <= 0 || multiplier <= 0f ||
                float.IsNaN(multiplier) || float.IsInfinity(multiplier))
                return damage;
            if (Mathf.Approximately(multiplier, 1f)) return damage;
            return Mathf.Max(1, Mathf.RoundToInt(damage * multiplier));
        }
    }
}
