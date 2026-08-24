using UnityEngine;
using UnityEngine.SceneManagement;

namespace ErenshorSkills.Skills
{
    /// <summary>
    /// FISHING — EverQuest's fishing was a beloved tradeskill where you'd sit
    /// by the water for hours, occasionally catching valuable items. In Erenshor,
    /// fishing already exists but has no progression. This adds XP, level-ups,
    /// and improved rare catch rates as you level.
    ///
    /// How it works in Erenshor:
    /// - Equip a Fishing Pole (from Treven Pines), face water, right-click to cast
    /// - This mod awards XP per catch and improves rare catch chance as you level
    /// </summary>
    public static class FishingSkill
    {
        /// <summary>Bonus % catch chance from fishing skill.</summary>
        public static float CatchBonus =>
            SkillsSaveManager.Data.Fishing.Level * 0.5f;

        /// <summary>Called by the Harmony patch when a catch completes.</summary>
        public static bool OnCatch(string itemName, bool isRare, bool isJunk)
        {
            var skill = SkillsSaveManager.Data.Fishing;
            string zone = SceneManager.GetActiveScene().name;

            // Zone level check — under-leveled fishers only catch junk
            int reqLevel = GetZoneMinLevel(zone);
            if (skill.Level < reqLevel)
            {
                skill.TimesUsed++;
                // Still award minimal XP so they can progress
                SkillXpEngine.AwardXp(skill, 2f);

                ChatHelper.Send(
                    $"<color=#7EC8E3>[Fishing]</color> " +
                    $"<color=#AAAAAA>You caught {itemName}, but the waters here are beyond your skill, " +
                    $"so you found no bonus cooking materials. " +
                    $"(Need Fishing {reqLevel}, have {skill.Level})</color>");
                return false;
            }

            skill.TimesUsed++;

            float xp = 10f;
            xp *= GetZoneMultiplier(SceneManager.GetActiveScene().name);

            if (isRare) { xp *= 2.5f; skill.Successes++; }
            if (isJunk)   xp *= 0.5f;

            // Night bonus — EQ fishermen knew the best catches came at night
            if (IsNight()) xp *= 1.1f;

            SkillXpEngine.AwardXp(skill, xp);
            return true;
        }

        private static bool IsNight()
        {
            try
            {
                var sun = GameObject.Find("Directional Light");
                if (sun != null)
                {
                    float x = sun.transform.eulerAngles.x;
                    return x > 180f && x < 360f;
                }
            }
            catch { }
            return false;
        }

        private static float GetZoneMultiplier(string scene)
        {
            switch (scene)
            {
                case "Stowaway": case "Tutorial":     return 1.0f;
                case "Brake": case "Vitheo":
                case "Hidden": case "Bonepits":        return 1.1f;
                case "FernallaField": case "Krakengard":return 1.3f;
                case "Duskenlight": case "Underspine":  return 1.35f;
                case "SaltedStrand": case "Rottenfoot": return 1.4f;
                case "Braxonian": case "Silkengrass":
                case "Windwashed": case "Elderstone":   return 1.5f;
                case "Loomingwood": case "Rockshade":   return 1.55f;
                case "Malaroth":                        return 1.7f;
                case "Soluna":                          return 1.8f;
                case "Blight": case "Abyssal":          return 1.85f;
                case "Ripper": case "Braxonia":
                case "PrielPlateau":                    return 1.9f;
                case "Azynthi": case "AzynthiClear":    return 2.0f;
                case "Azure":                           return 1.2f;
                default:                                return 1.0f;
            }
        }

        /// <summary>Minimum fishing level required per zone.</summary>
        private static int GetZoneMinLevel(string scene)
        {
            switch (scene)
            {
                case "Stowaway": case "Tutorial":
                case "Brake": case "Vitheo":
                case "Hidden": case "Azure":              return 1;
                case "Bonepits":                          return 10;
                case "FernallaField": case "Duskenlight":
                case "Krakengard":                        return 25;
                case "SaltedStrand": case "Rottenfoot":
                case "Underspine":                        return 45;
                case "Braxonian": case "Silkengrass":
                case "Windwashed": case "Elderstone":     return 65;
                case "Loomingwood": case "Rockshade":     return 85;
                case "Malaroth":                          return 105;
                case "Soluna":                            return 120;
                case "Blight": case "Abyssal":            return 135;
                case "Ripper": case "Braxonia":
                case "PrielPlateau": case "VitheosEnd":   return 150;
                case "Azynthi": case "AzynthiClear":      return 165;
                default:                                  return 1;
            }
        }
    }
}
