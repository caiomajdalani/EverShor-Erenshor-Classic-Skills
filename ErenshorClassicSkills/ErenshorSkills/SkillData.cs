using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ErenshorSkills
{
    // ═════════════════════════════════════════════════════════════════════
    // Individual skill data
    // ═════════════════════════════════════════════════════════════════════

    [Serializable]
    public class SkillEntry
    {
        public string Name = "";
        public int Level = 0;  // Start at 0 = untrained
        public float CurrentXp = 0f;
        public int TimesUsed = 0;
        public int Successes = 0;
        public int Failures = 0;

        /// <summary>If true, this skill uses the harder combat XP curve.</summary>
        public bool UseCombatXpCurve;

        /// <summary>Whether this skill has been unlocked (level > 0).</summary>
        public bool IsUnlocked => Level > 0;

        /// <summary>
        /// Skill cap based on player level: (PlayerLevel * 5) + 5.
        /// At level 1 cap=10, level 35 cap=180.
        /// </summary>
        public int SkillCap
        {
            get
            {
                int playerLevel = 1;
                try { playerLevel = Mathf.Max(1, GameData.PlayerStats?.Level ?? 1); }
                catch { }
                int configuredMax = 180;
                try { configuredMax = Mathf.Max(1, SkillsPlugin.CfgGlobalMaxLevel?.Value ?? 180); }
                catch { }
                return Mathf.Min((playerLevel * 5) + 5, configuredMax);
            }
        }

        /// <summary>XP needed for the next level.</summary>
        public float XpToNextLevel
        {
            get
            {
                if (Level <= 0) return 0f;
                if (Level >= SkillCap) return 0f;
                float baseXp = UseCombatXpCurve ? 80f : 40f;
                return baseXp * Mathf.Pow(1.03f, Level);
            }
        }

        public float LevelProgress
        {
            get
            {
                float needed = XpToNextLevel;
                return (needed <= 0f) ? 1f : Mathf.Clamp01(CurrentXp / needed);
            }
        }

        public bool IsMaxLevel => Level >= SkillCap;
        public bool IsAtCap => Level >= SkillCap;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Full save data container for all skills
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Tracks a custom item in the player's inventory for persistence.</summary>
    [Serializable]
    public class SavedCustomItem
    {
        public string Name;
        public int Count;
    }

    [Serializable]
    public class AllSkillsData
    {
        // Custom items in inventory that need to be re-injected on load
        public List<SavedCustomItem> CustomInventory = new List<SavedCustomItem>();

        // The native AuraItem field can be resolved before addon items have
        // registered during character selection. Keep the last deliberately
        // equipped Monk/Necromancer aura ID in the character sidecar so a
        // failed native lookup cannot turn a logout into permanent item loss.
        public string EquippedClassAuraId = "";

        /// <summary>Recipe names the player has learned. Recipes not in this list are locked.</summary>
        public List<string> KnownRecipes = new List<string>();

        /// <summary>Default starter recipes every character begins with.</summary>
        public static readonly string[] StarterRecipes = new string[]
        {
            // Smithing
            "Copper Rivets", "Crude Iron Dagger", "Hammered Iron Shortsword", "Banded Iron Helm",
            // Baking
            "Trail Rations", "Herb Bread",
            // Brewing
            "Bog Juice", "Faerie Wine",
            // Fletching
            "Crude Shortbow", "Strung Hunting Bow",
            // Jewelcraft
            "Polished Stone Band", "Amber Pendant",
            // Tailoring
            "Patchwork Bandages", "Silken Pouch", "Stitched Leather Gloves", "Padded Cloth Cap",
        };

        /// <summary>Ensure starter recipes are known.</summary>
        public void EnsureStarterRecipes()
        {
            foreach (var name in StarterRecipes)
            {
                if (!KnownRecipes.Exists(r => string.Equals(r, name,
                    StringComparison.OrdinalIgnoreCase)))
                    KnownRecipes.Add(name);
            }
        }

        /// <summary>Check if a recipe is known (learnable system).</summary>
        public bool IsRecipeKnown(string recipeName)
        {
            return !string.IsNullOrWhiteSpace(recipeName) &&
                KnownRecipes.Exists(r => string.Equals(r, recipeName,
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Learn a new recipe. Returns true if it was new.</summary>
        public bool LearnRecipe(string recipeName)
        {
            if (string.IsNullOrWhiteSpace(recipeName) || IsRecipeKnown(recipeName)) return false;
            KnownRecipes.Add(recipeName);
            return true;
        }

        /// <summary>Unspent training points. Players gain 3 per character level.</summary>
        public int TrainingPoints = 0;

        /// <summary>Last known player level for calculating new training points.</summary>
        public int LastKnownPlayerLevel = 0;

        public SkillEntry Fishing = new SkillEntry { Name = "Fishing" };
        public SkillEntry Foraging = new SkillEntry { Name = "Foraging" };
        public SkillEntry Swimming = new SkillEntry { Name = "Swimming" };
        public SkillEntry SenseHeading = new SkillEntry { Name = "Sense Heading" };
        public SkillEntry BindWound = new SkillEntry { Name = "Bind Wound" };
        public SkillEntry Meditate = new SkillEntry { Name = "Meditate" };
        public SkillEntry SafeFall = new SkillEntry { Name = "Safe Fall" };
        public SkillEntry FoodTolerance = new SkillEntry { Name = "Food Tolerance" };
        public SkillEntry Begging = new SkillEntry { Name = "Begging" };

        // ── Combat weapon skills ────────────────────────────────────
        public SkillEntry OneHandSlash = new SkillEntry { Name = "1H Slashing", UseCombatXpCurve = true };
        public SkillEntry OneHandBlunt = new SkillEntry { Name = "1H Blunt", UseCombatXpCurve = true };
        public SkillEntry Piercing = new SkillEntry { Name = "Piercing", UseCombatXpCurve = true };
        public SkillEntry TwoHandSlash = new SkillEntry { Name = "2H Slashing", UseCombatXpCurve = true };
        public SkillEntry TwoHandBlunt = new SkillEntry { Name = "2H Blunt", UseCombatXpCurve = true };
        public SkillEntry Archery = new SkillEntry { Name = "Archery", UseCombatXpCurve = true };
        public SkillEntry Wands = new SkillEntry { Name = "Wands", UseCombatXpCurve = true };
        public SkillEntry HandToHand = new SkillEntry { Name = "Hand to Hand", UseCombatXpCurve = true };

        // ── Tradeskills ─────────────────────────────────────────────
        public SkillEntry Smithing = new SkillEntry { Name = "Smithing" };
        public SkillEntry Baking = new SkillEntry { Name = "Baking" };
        public SkillEntry Brewing = new SkillEntry { Name = "Brewing" };
        public SkillEntry Fletching = new SkillEntry { Name = "Fletching" };
        public SkillEntry Jewelcraft = new SkillEntry { Name = "Jewelcraft" };
        public SkillEntry Tailoring = new SkillEntry { Name = "Tailoring" };

        // ── Magic skills ────────────────────────────────────────────
        public SkillEntry Evocation = new SkillEntry { Name = "Evocation" };
        public SkillEntry Abjuration = new SkillEntry { Name = "Abjuration" };
        public SkillEntry Alteration = new SkillEntry { Name = "Alteration" };
        public SkillEntry Conjuration = new SkillEntry { Name = "Conjuration" };

        /// <summary>Tracks how many training points have been spent (never decreases).</summary>
        public int TrainingPointsSpent = 0;

        /// <summary>
        /// Check if the player has leveled up and recalculate available training points.
        /// Available = (PlayerLevel * 3) - PointsSpent.
        /// Call this periodically (e.g. on save or UI refresh).
        /// </summary>
        public void CheckForNewTrainingPoints()
        {
            try
            {
                int playerLevel = Mathf.Max(1, GameData.PlayerStats?.Level ?? 1);
                int totalEarned = playerLevel * 3;
                int newTP = totalEarned - TrainingPointsSpent;
                if (newTP < 0) newTP = 0;

                // Notify if points increased
                if (newTP > TrainingPoints && LastKnownPlayerLevel > 0)
                {
                    int gained = newTP - TrainingPoints;
                    ChatHelper.Send(
                        $"<color=#FFD700>[Skills]</color> " +
                        $"You gained <color=#FFFFFF>{gained}</color> training points! " +
                        $"<color=#AAAAAA>({newTP} available)</color>");
                }

                TrainingPoints = newTP;
                LastKnownPlayerLevel = playerLevel;
            }
            catch { }
        }

        /// <summary>
        /// Spend a training point to raise a skill by 1 level.
        /// Returns true if successful.
        /// </summary>
        public bool SpendTrainingPoint(SkillEntry skill)
        {
            if (TrainingPoints <= 0) return false;
            if (skill.IsAtCap) return false;

            TrainingPointsSpent++;
            TrainingPoints--;
            int oldLevel = skill.Level;
            skill.Level++;
            skill.CurrentXp = 0f;

            SkillsPlugin.Log.LogInfo(
                $"SpendTP: {skill.Name} {oldLevel}->{skill.Level}, " +
                $"TP={TrainingPoints}, Spent={TrainingPointsSpent}");
            return true;
        }

        /// <summary>
        /// Find a skill by partial name match. Returns the direct field reference.
        /// </summary>
        public SkillEntry FindSkillByName(string partialName)
        {
            if (string.IsNullOrWhiteSpace(partialName)) return null;
            string lower = partialName.ToLowerInvariant();
            var all = GetAll();
            foreach (var s in all)
            {
                if ((s.Name ?? "").ToLowerInvariant().Contains(lower))
                    return s;
            }
            return null;
        }

        /// <summary>Return all utility skill entries for the skill window.</summary>
        public List<SkillEntry> GetUtilitySkills()
        {
            return new List<SkillEntry>
            {
                Foraging, Swimming,
                BindWound, Meditate, FoodTolerance, Begging
            };
        }

        /// <summary>Return all combat skill entries.</summary>
        public List<SkillEntry> GetCombatSkills()
        {
            return new List<SkillEntry>
            {
                OneHandSlash, OneHandBlunt, Piercing,
                TwoHandSlash, TwoHandBlunt, Archery, Wands, HandToHand
            };
        }

        /// <summary>Return all tradeskill entries.</summary>
        public List<SkillEntry> GetTradeskills()
        {
            return new List<SkillEntry>
            {
                Fishing, Smithing, Baking, Brewing, Fletching, Jewelcraft, Tailoring
            };
        }

        /// <summary>Return all magic skill entries.</summary>
        public List<SkillEntry> GetMagicSkills()
        {
            return new List<SkillEntry>
            {
                Evocation, Abjuration, Alteration, Conjuration
            };
        }

        /// <summary>Return all skill entries as a list for iteration.</summary>
        public List<SkillEntry> GetAll()
        {
            var list = new List<SkillEntry>();
            list.AddRange(GetUtilitySkills());
            list.AddRange(GetCombatSkills());
            list.AddRange(GetTradeskills());
            list.AddRange(GetMagicSkills());
            return list;
        }

        /// <summary>
        /// Apply catch-up skill levels for pre-existing characters.
        /// Called on first load when no save file exists.
        /// Uses the same (level * 5) + 5 formula as the skill cap,
        /// scaled by class affinity so a Druid gets higher magic/heal
        /// skills and a Duelist gets higher combat skills.
        /// </summary>
        public void ApplyCatchUpLevels(int playerLevel, string className)
        {
            if (playerLevel <= 1) return; // Level 1 characters don't need catch-up

            int configuredMax = Mathf.Max(1, SkillsPlugin.CfgGlobalMaxLevel?.Value ?? 180);
            int skillCap = Mathf.Min((playerLevel * 5) + 5, configuredMax);
            // Catch-up gives 70% of cap — the player still has room to grow
            int baseLevel = UnityEngine.Mathf.Clamp(
                UnityEngine.Mathf.RoundToInt(skillCap * 0.7f), 1, configuredMax);

            bool doCombatMagic = SkillsPlugin.CfgCatchUpCombatMagic.Value;
            bool doUtilityTrade = SkillsPlugin.CfgCatchUpUtilityTrade.Value;

            if (!doCombatMagic && !doUtilityTrade) return;

            int totalSet = 0;

            if (doCombatMagic)
            {
                // Combat skills — scale by class melee affinity
                float meleeScale = GetClassMeleeScale(className);
                int meleeLevel = UnityEngine.Mathf.RoundToInt(baseLevel * meleeScale);
                if (meleeLevel > 0)
                {
                    foreach (var s in GetCombatSkills())
                    {
                        if (s.Level < meleeLevel)
                        {
                            s.Level = meleeLevel;
                            s.CurrentXp = 0;
                            totalSet++;
                        }
                    }
                }

                // Magic skills — scale by class magic affinity
                float magicScale = GetClassMagicScale(className);
                int magicLevel = UnityEngine.Mathf.RoundToInt(baseLevel * magicScale);
                if (magicLevel > 0)
                {
                    foreach (var s in GetMagicSkills())
                    {
                        if (s.Level < magicLevel)
                        {
                            s.Level = magicLevel;
                            s.CurrentXp = 0;
                            totalSet++;
                        }
                    }
                }
            }

            if (doUtilityTrade)
            {
                // Utility skills — flat 50% of base, all classes benefit equally
                int utilLevel = UnityEngine.Mathf.RoundToInt(baseLevel * 0.5f);
                if (utilLevel > 0)
                {
                    foreach (var s in GetUtilitySkills())
                    {
                        if (s.Level < utilLevel)
                        {
                            s.Level = utilLevel;
                            s.CurrentXp = 0;
                            totalSet++;
                        }
                    }
                }

                // Tradeskills — class specialization gets a big head start,
                // other tradeskills get a smaller base level.
                // Specialist formula: (level * 5) + 5 (same as skill cap), capped at 130
                // Non-specialist: 30% of base level
                string specialTrade = GetClassTradeskill(className);
                int specialLevel = UnityEngine.Mathf.Clamp(playerLevel * 5 + 5, 1,
                    Mathf.Min(130, configuredMax));
                int genericLevel = UnityEngine.Mathf.RoundToInt(baseLevel * 0.3f);

                foreach (var s in GetTradeskills())
                {
                    int targetLevel = (s.Name == specialTrade) ? specialLevel : genericLevel;
                    if (targetLevel > 0 && s.Level < targetLevel)
                    {
                        s.Level = targetLevel;
                        s.CurrentXp = 0;
                        totalSet++;
                    }
                }
            }

            if (totalSet > 0)
            {
                SkillsPlugin.Log.LogInfo(
                    $"CatchUp: Set {totalSet} skills for level {playerLevel} {className} " +
                    $"(base={baseLevel}, cap={skillCap})");
                string specialNote = doUtilityTrade
                    ? $" {className} specialty: {GetClassTradeskill(className)}."
                    : "";
                ChatHelper.Send(
                    $"<color=#FFD700>[Classic Skills]</color> " +
                    $"<color=#AAAAAA>Catch-up applied: {totalSet} skills set based on " +
                    $"your level {playerLevel} {className}.{specialNote}</color>");
            }
        }

        // Class affinity scales for catch-up (same values as SimPlayerSkills)
        private static float GetClassMeleeScale(string className)
        {
            switch (className)
            {
                case "Duelist":     return 1.0f;
                case "Paladin":     return 0.85f;
                case "Reaver":      return 0.9f;
                case "Druid":       return 0.5f;
                case "Stormcaller": return 0.25f;
                case "Arcanist":    return 0.25f;
                default:            return 0.5f;
            }
        }

        /// <summary>
        /// Each class specializes in one tradeskill:
        ///   Paladin    → Smithing    (heavy armor, weapons)
        ///   Duelist    → Fletching   (bows, arrows, light weapons)
        ///   Reaver     → Brewing     (dark potions, poisons)
        ///   Druid      → Baking      (herbs, nature cooking)
        ///   Arcanist   → Jewelcraft  (enchanted gems, arcane foci)
        ///   Stormcaller→ Tailoring   (cloth armor, magical robes)
        /// </summary>
        private static string GetClassTradeskill(string className)
        {
            switch (className)
            {
                case "Paladin":     return "Smithing";
                case "Duelist":     return "Fletching";
                case "Reaver":      return "Brewing";
                case "Druid":       return "Baking";
                case "Arcanist":    return "Jewelcraft";
                case "Stormcaller": return "Tailoring";
                default:            return "Smithing";
            }
        }

        private static float GetClassMagicScale(string className)
        {
            switch (className)
            {
                case "Arcanist":    return 1.0f;
                case "Stormcaller": return 1.0f;
                case "Druid":       return 0.85f;
                case "Reaver":      return 0.6f;
                case "Paladin":     return 0.4f;
                case "Duelist":     return 0.0f;
                default:            return 0.3f;
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Shared XP / Level-up engine
    // ═════════════════════════════════════════════════════════════════════

    public static class SkillXpEngine
    {
        /// <summary>
        /// Award XP to a skill and handle level-ups.
        /// Returns true if a level-up occurred.
        /// </summary>
        public static bool AwardXp(SkillEntry skill, float xp, bool showChat = true,
            bool requestSave = true)
        {
            if (skill == null || float.IsNaN(xp) || float.IsInfinity(xp) || xp <= 0f)
                return false;
            // Skills at level 0 are untrained — no XP gain
            if (!skill.IsUnlocked) return false;
            if (skill.IsAtCap)
            {
                // Callers record usage/success counters before awarding XP.
                // Those counters still need a debounced save at the skill cap.
                if (requestSave) SkillsSaveManager.RequestSave();
                return false;
            }

            bool leveled = false;

            // Scale XP with skill level so the bar stays relevant at higher levels.
            // At level 1: 1.1x, level 10: 2x, level 20: 3x, level 50: 6x.
            float scaledXp = xp * (1f + skill.Level * 0.1f);

            // ── EQ-style skill-up chance on each use ────────────────
            // Chance = (SkillCap - CurrentLevel) / (SkillCap * 12)
            // Much lower than raw EQ formula to slow progression.
            // Level 1/50: ~8% per use. Level 25/50: ~4%. Level 45/50: ~0.8%.
            int cap = skill.SkillCap;
            if (cap > 0 && skill.Level < cap)
            {
                float skillUpChance = (float)(cap - skill.Level) / (float)(cap * 12);
                if (UnityEngine.Random.value < skillUpChance)
                {
                    skill.Level++;
                    skill.CurrentXp = 0f;
                    leveled = true;
                }
            }

            // ── Normal XP accumulation (still happens even if skill-up occurred) ──
            if (!skill.IsAtCap && skill.XpToNextLevel > 0)
            {
                skill.CurrentXp += scaledXp;

                while (skill.CurrentXp >= skill.XpToNextLevel && !skill.IsAtCap && skill.XpToNextLevel > 0)
                {
                    skill.CurrentXp -= skill.XpToNextLevel;
                    skill.Level++;
                    leveled = true;
                }
            }

            if (skill.IsAtCap)
                skill.CurrentXp = 0f;

            if (leveled && showChat && SkillsPlugin.CfgShowChatMessages.Value)
            {
                ChatHelper.Send(
                    $"<color=#FFD700>*** {skill.Name.ToUpperInvariant()} SKILL UP! ***</color> " +
                    $"<color=#7EC8E3>Level {skill.Level}/{skill.SkillCap}</color>");
            }

            if (requestSave) SkillsSaveManager.RequestSave();
            return leveled;
        }

        /// <summary>
        /// Award tradeskill XP with EQ-style trivial system.
        /// Skill-up chance = (TrivialLevel - CurrentLevel) / TrivialLevel.
        /// No XP or skill-ups once skill >= trivial.
        /// </summary>
        public static bool AwardTradeskillXp(SkillEntry skill, float xp, int trivialLevel)
        {
            if (skill == null || float.IsNaN(xp) || float.IsInfinity(xp) ||
                xp <= 0f || trivialLevel <= 0) return false;
            if (!skill.IsUnlocked) return false;
            if (skill.IsAtCap) return false;
            if (skill.Level >= trivialLevel) return false; // Trivial — no gains

            bool leveled = false;
            float scaledXp = xp * (1f + skill.Level * 0.1f);

            // ── EQ-style tradeskill skill-up chance ────────────────
            // Chance = (TrivialLevel - CurrentLevel) / TrivialLevel
            // Near trivial = very low chance. Far below = high chance.
            if (trivialLevel > 0)
            {
                float skillUpChance = (float)(trivialLevel - skill.Level) / (float)trivialLevel;
                if (UnityEngine.Random.value < skillUpChance)
                {
                    skill.Level++;
                    skill.CurrentXp = 0f;
                    leveled = true;
                }
            }

            // ── Normal XP accumulation ──
            if (!skill.IsAtCap && skill.Level < trivialLevel && skill.XpToNextLevel > 0)
            {
                skill.CurrentXp += scaledXp;

                while (skill.CurrentXp >= skill.XpToNextLevel && !skill.IsAtCap
                       && skill.Level < trivialLevel && skill.XpToNextLevel > 0)
                {
                    skill.CurrentXp -= skill.XpToNextLevel;
                    skill.Level++;
                    leveled = true;
                }
            }

            if (skill.IsAtCap || skill.Level >= trivialLevel)
                skill.CurrentXp = 0f;

            if (leveled && SkillsPlugin.CfgShowChatMessages.Value)
            {
                ChatHelper.Send(
                    $"<color=#FFD700>*** {skill.Name.ToUpperInvariant()} SKILL UP! ***</color> " +
                    $"<color=#7EC8E3>Level {skill.Level}/{skill.SkillCap}</color>");
            }

            SkillsSaveManager.RequestSave();
            return leveled;
        }

        /// <summary>
        /// Attempt a skill check. Higher skill = higher success rate.
        /// Optionally award XP on both success and failure (EQ style).
        /// </summary>
        public static bool SkillCheck(SkillEntry skill, float difficulty = 1f,
            float xpOnSuccess = 10f, float xpOnFailure = 3f,
            bool requestSave = true)
        {
            if (skill == null) return false;
            if (float.IsNaN(difficulty) || float.IsInfinity(difficulty) ||
                difficulty <= 0f) difficulty = 1f;
            skill.TimesUsed++;

            // Success chance: 30% base + 1.2% per level, modified by difficulty
            float chance = (30f + skill.Level * 1.2f) / difficulty;
            chance = Mathf.Clamp(chance, 5f, 95f);

            bool success = UnityEngine.Random.value * 100f < chance;

            if (success)
            {
                skill.Successes++;
                AwardXp(skill, xpOnSuccess, requestSave: false);
            }
            else
            {
                skill.Failures++;
                // EQ awarded skillups on failure too - smaller XP
                AwardXp(skill, xpOnFailure, showChat: false,
                    requestSave: false);
            }

            if (requestSave) SkillsSaveManager.RequestSave();
            return success;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Chat helper
    // ═════════════════════════════════════════════════════════════════════

    public static class ChatHelper
    {
        private static void MirrorTestMessage(string channel, string message)
        {
            if (SkillsPlugin.CfgEnableTestMode?.Value != true) return;
            string plain = Regex.Replace(message ?? "", "<[^>]+>", "");
            SkillsPlugin.Log.LogInfo($"[Test {channel}] {plain}");
        }

        public static void Send(string message)
        {
            // Test-mode chat is mirrored to the Lunaris log so automated in-game runs
            // retain durable evidence even when the player's chat filters hide
            // SystemMessages. Normal gameplay logging remains unchanged.
            MirrorTestMessage("Chat", message);
            try
            {
                // Use UpdateSocialLog.LogAdd — same approach as ErenshorQoL mod
                UpdateSocialLog.LogAdd(message);
            }
            catch
            {
                SkillsPlugin.Log.LogInfo($"[Chat] {message}");
            }
        }

        /// <summary>
        /// Sends gameplay feedback as a spell-result line so Erenshor routes it
        /// to combat-log tabs instead of the general/system-message channel.
        /// </summary>
        public static void SendCombat(string message,
            string color = "lightblue")
        {
            MirrorTestMessage("Combat", message);
            try
            {
                UpdateSocialLog.CombatLogAdd(new ChatLogLine(message,
                    ChatLogLine.LogType.PlayerSpellResults, color));
            }
            catch
            {
                SkillsPlugin.Log.LogInfo($"[Combat] {message}");
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Save / Load manager
    // ═════════════════════════════════════════════════════════════════════

    public static class SkillsSaveManager
    {
        private static AllSkillsData _data;
        private static string _charName = "";
        private static bool _saveDirty;
        private static float _lastSaveTime = -10f;
        private const float SaveDebounceSeconds = 5f;
        private const int InventorySidecarFormatVersion = 2;
        public static string CharName => _charName;
        public static string Sanitize(string name) => SanitizeName(name);
        internal static int LoadedInventoryFormatVersion { get; private set; }

        public static AllSkillsData Data
        {
            get
            {
                if (_data == null) _data = new AllSkillsData();
                return _data;
            }
        }

        public static void Load(string characterName)
        {
            _charName = SanitizeName(characterName);
            _data = new AllSkillsData();
            _saveDirty = false;
            _lastSaveTime = -10f;
            LoadedInventoryFormatVersion = 0;
            string path = GetPath();
            bool primaryHealthy = true;

            if (File.Exists(path) || File.Exists(path + ".bak"))
            {
                try
                {
                    if (!SafeFileIO.TryReadAllTextWithBackup(path, IsSkillsJsonValid,
                        out string json, out bool recovered, out string readError))
                        throw new InvalidDataException(readError);
                    if (!string.IsNullOrEmpty(json) && json.Trim() != "{}")
                    {
                        ParseSkillsJson(json);
                        ValidateLoadedData();
                        SkillsPlugin.Log.LogInfo(
                            $"Loaded skill data for '{characterName}' " +
                            $"(Foraging Lv{_data.Foraging.Level})");
                        if (recovered)
                            SkillsPlugin.Log.LogWarning($"Recovered '{characterName}' skills from the verified backup.");
                    }
                    else
                    {
                        SkillsPlugin.Log.LogWarning(
                            $"Skill file for '{characterName}' was empty.");
                    }
                }
                catch (Exception ex)
                {
                    SkillsPlugin.Log.LogError($"Failed to load skills: {ex.Message}");
                    primaryHealthy = false;
                }
            }
            else
            {
                SkillsPlugin.Log.LogInfo(
                    $"New skill profile created for '{characterName}'");
            }

            // Training points are calculated dynamically:
            // Available = (PlayerLevel * 3) - PointsSpent
            // No migration needed — CheckForNewTrainingPoints() handles it.

            // Recover an interrupted item transaction before loading any
            // sidecar it may have changed (bags, player recipes, sim recipes,
            // or bank data). Loading first would leave memory stale even after
            // the recovery repaired the file on disk.
            bool transactionHealthy = Items.BagSystem.RecoverPendingTransaction(
                GetBagsPath());

            // Load custom inventory, recipes, and bags from separate files
            bool inventoryHealthy = transactionHealthy && LoadInventoryFile();
            bool recipesHealthy = transactionHealthy && LoadRecipesFile();
            bool bagsHealthy = transactionHealthy && LoadBagsFile();
            bool simRecipesHealthy = transactionHealthy &&
                Skills.SimPlayerTradeskills.LoadSimRecipes();

            if (!primaryHealthy || !transactionHealthy || !inventoryHealthy ||
                !recipesHealthy || !bagsHealthy ||
                !simRecipesHealthy)
            {
                HasLoaded = false;
                SkillsPlugin.Log.LogError(
                    $"Classic Skills disabled for '{characterName}' because one or more " +
                    "save files and their backups could not be validated. Existing files " +
                    "will not be overwritten.");
                ChatHelper.Send(
                    "<color=#EF5350>[Classic Skills]</color> Save data could not be " +
                    "validated. The addon is disabled for this session to protect your items.");
                return;
            }

            HasLoaded = true;
            Items.BagSystem.ReconcileInventoryBagIds();
        }

        /// <summary>Parse our manual JSON format into AllSkillsData.</summary>
        private static void ParseSkillsJson(string json)
        {
            // Parse training points metadata
            _data.TrainingPoints = ParseTopLevelInt(json, "__TrainingPoints", 0);
            _data.TrainingPointsSpent = ParseTopLevelInt(json, "__TrainingPointsSpent", 0);
            _data.LastKnownPlayerLevel = ParseTopLevelInt(json, "__LastKnownPlayerLevel", 0);
            _data.EquippedClassAuraId = ParseTopLevelString(json,
                "__EquippedClassAuraId", string.Empty);

            var all = _data.GetAll();
            SkillsPlugin.Log.LogInfo($"ParseSkillsJson: parsing {all.Count} skills, TP={_data.TrainingPoints}, Spent={_data.TrainingPointsSpent}");
            foreach (var skill in all)
            {
                string key = $"\"{skill.Name}\"";
                int idx = json.IndexOf(key);
                if (idx < 0) { SkillsPlugin.Log.LogWarning($"ParseSkillsJson: '{skill.Name}' not found in JSON!"); continue; }

                int braceStart = json.IndexOf('{', idx + key.Length);
                int braceEnd = json.IndexOf('}', braceStart);
                if (braceStart < 0 || braceEnd < 0) continue;

                string block = json.Substring(braceStart + 1, braceEnd - braceStart - 1);
                skill.Level = ParseInt(block, "Level", skill.Level);
                skill.CurrentXp = ParseFloat(block, "CurrentXp", skill.CurrentXp);
                skill.TimesUsed = ParseInt(block, "TimesUsed", skill.TimesUsed);
                skill.Successes = ParseInt(block, "Successes", skill.Successes);
                skill.Failures = ParseInt(block, "Failures", skill.Failures);
            }
        }

        private static bool IsSkillsJsonValid(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            string trimmed = json.Trim();
            if (!trimmed.StartsWith("{") || !trimmed.EndsWith("}")) return false;
            if (!TryReadRequiredInt(trimmed, "__TrainingPoints", out _) ||
                !TryReadRequiredInt(trimmed, "__TrainingPointsSpent", out _) ||
                !TryReadRequiredInt(trimmed, "__LastKnownPlayerLevel", out _))
                return false;
            const string number = "-?(?:\\d+(?:\\.\\d+)?|\\.\\d+)(?:[eE][+-]?\\d+)?";
            foreach (var skill in new AllSkillsData().GetAll())
            {
                string name = Regex.Escape(skill.Name);
                string pattern = $"\\\"{name}\\\"\\s*:\\s*\\{{" +
                    $"\\s*\\\"Level\\\"\\s*:\\s*(?<level>-?\\d+)\\s*," +
                    $"\\s*\\\"CurrentXp\\\"\\s*:\\s*(?<xp>{number})\\s*," +
                    $"\\s*\\\"TimesUsed\\\"\\s*:\\s*(?<used>-?\\d+)\\s*," +
                    $"\\s*\\\"Successes\\\"\\s*:\\s*(?<success>-?\\d+)\\s*," +
                    $"\\s*\\\"Failures\\\"\\s*:\\s*(?<failure>-?\\d+)\\s*\\}}";
                var matches = Regex.Matches(trimmed, pattern);
                if (matches.Count != 1) return false;
                Match match = matches[0];
                if (!int.TryParse(match.Groups["level"].Value, out _) ||
                    !int.TryParse(match.Groups["used"].Value, out _) ||
                    !int.TryParse(match.Groups["success"].Value, out _) ||
                    !int.TryParse(match.Groups["failure"].Value, out _) ||
                    !float.TryParse(match.Groups["xp"].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float xp) || float.IsNaN(xp) || float.IsInfinity(xp))
                    return false;
            }
            return true;
        }

        private static bool TryReadRequiredInt(string json, string key, out int value)
        {
            value = 0;
            var matches = Regex.Matches(json,
                $"\\\"{Regex.Escape(key)}\\\"\\s*:\\s*(-?\\d+)");
            return matches.Count == 1 && int.TryParse(
                matches[0].Groups[1].Value, out value);
        }

        private static string ParseTopLevelString(string json, string key,
            string fallback)
        {
            var matches = Regex.Matches(json,
                $"\\\"{Regex.Escape(key)}\\\"\\s*:\\s*" +
                "(\\\"(?:\\\\.|[^\\\"\\\\])*\\\")");
            if (matches.Count != 1 || !SafeFileIO.TryDecodeJsonStringToken(
                    matches[0].Groups[1].Value, out string value))
                return fallback;
            return value;
        }

        private static void ValidateLoadedData()
        {
            int maxLevel = Mathf.Max(1, SkillsPlugin.CfgGlobalMaxLevel?.Value ?? 180);
            _data.TrainingPoints = Mathf.Max(0, _data.TrainingPoints);
            _data.TrainingPointsSpent = Mathf.Max(0, _data.TrainingPointsSpent);
            _data.LastKnownPlayerLevel = Mathf.Max(0, _data.LastKnownPlayerLevel);
            if (!Patches.ClassAuraPersistence.IsClassAuraId(
                    _data.EquippedClassAuraId))
                _data.EquippedClassAuraId = string.Empty;
            foreach (var skill in _data.GetAll())
            {
                skill.Level = Mathf.Clamp(skill.Level, 0, maxLevel);
                if (float.IsNaN(skill.CurrentXp) || float.IsInfinity(skill.CurrentXp) || skill.CurrentXp < 0f)
                    skill.CurrentXp = 0f;
                skill.TimesUsed = Mathf.Max(0, skill.TimesUsed);
                skill.Successes = Mathf.Max(0, skill.Successes);
                skill.Failures = Mathf.Max(0, skill.Failures);
            }
        }

        private static bool LoadInventoryFile()
        {
            try
            {
                string path = GetInventoryPath();
                if (!File.Exists(path) && !File.Exists(path + ".bak")) return true;
                if (!SafeFileIO.TryReadAllTextWithBackup(path,
                    IsInventoryJsonValid,
                    out string json, out bool recovered, out string readError))
                    throw new InvalidDataException(readError);
                LoadedInventoryFormatVersion = 1;
                if (string.IsNullOrEmpty(json) || json.Trim() == "{}") return true;

                _data.CustomInventory.Clear();
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match match in Regex.Matches(json,
                    "(\\\"(?:\\\\.|[^\\\"\\\\])*\\\")\\s*:\\s*(\\d+)"))
                {
                    if (!SafeFileIO.TryDecodeJsonStringToken(
                        match.Groups[1].Value, out string name) ||
                        string.IsNullOrWhiteSpace(name) || !names.Add(name))
                        throw new InvalidDataException(
                            "Custom inventory contains an invalid or duplicate item name.");
                    if (!int.TryParse(match.Groups[2].Value, out int count))
                        throw new InvalidDataException(
                            "Custom inventory contains an invalid item count.");
                    if (string.Equals(name, "__InventoryFormat",
                        StringComparison.Ordinal))
                    {
                        if (count < 1 || count > InventorySidecarFormatVersion)
                            throw new InvalidDataException(
                                $"Unsupported custom inventory format {count}.");
                        LoadedInventoryFormatVersion = count;
                        continue;
                    }
                    if (count > 0)
                        _data.CustomInventory.Add(new SavedCustomItem { Name = name, Count = count });
                }

                if (_data.CustomInventory.Count > 0)
                    SkillsPlugin.Log.LogInfo(
                        $"Loaded {_data.CustomInventory.Count} custom item types from inventory file.");
                if (recovered)
                    SkillsPlugin.Log.LogWarning("Recovered custom inventory tracking from backup.");
                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"LoadInventoryFile error: {ex.Message}");
                return false;
            }
        }

        private static int ParseTopLevelInt(string json, string key, int fallback)
        {
            return TryReadRequiredInt(json, key, out int result) ? result : fallback;
        }

        private static int ParseInt(string block, string key, int fallback)
        {
            string search = $"\"{key}\":";
            int idx = block.IndexOf(search);
            if (idx < 0) return fallback;
            int start = idx + search.Length;
            int end = block.IndexOfAny(new[] { ',', '}' }, start);
            if (end < 0) end = block.Length;
            string val = block.Substring(start, end - start).Trim();
            return int.TryParse(val, out int result) ? result : fallback;
        }

        private static float ParseFloat(string block, string key, float fallback)
        {
            string search = $"\"{key}\":";
            int idx = block.IndexOf(search);
            if (idx < 0) return fallback;
            int start = idx + search.Length;
            int end = block.IndexOfAny(new[] { ',', '}' }, start);
            if (end < 0) end = block.Length;
            string val = block.Substring(start, end - start).Trim();
            return float.TryParse(val, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float result)
                ? result : fallback;
        }

        public static bool HasLoaded { get; set; }

        /// <summary>Clear all in-memory data. Call when logging out.</summary>
        public static void Reset()
        {
            HasLoaded = false;
            _charName = "";
            _data = new AllSkillsData();
            _saveDirty = false;
            _lastSaveTime = -10f;
            LoadedInventoryFormatVersion = 0;
            Skills.ForagingSkill.ResetRuntime();
            Skills.BindWoundSkill.ResetRuntime();
            Skills.BeggingSkill.ResetRuntime();
            Skills.MeditateSkill.ResetRuntime();
            Skills.SwimmingSkill.ResetRuntime();
            Items.BagSystem.Reset();
        }

        /// <summary>
        /// Coalesce high-frequency skill XP updates. Combat can generate many
        /// hits per second; writing four sidecars on every hit caused stutter
        /// and widened the chance of an interrupted save.
        /// </summary>
        public static void RequestSave()
        {
            if (!HasLoaded || string.IsNullOrEmpty(_charName)) return;
            _saveDirty = true;
            if (Time.unscaledTime - _lastSaveTime >= SaveDebounceSeconds)
                Save();
        }

        public static void FlushIfDue()
        {
            if (_saveDirty && HasLoaded &&
                Time.unscaledTime - _lastSaveTime >= SaveDebounceSeconds)
                Save();
        }

        public static bool Save()
        {
            if (string.IsNullOrEmpty(_charName) || !HasLoaded) return false;

            // Verify the character name matches the current player
            // to prevent saving one character's data under another's name
            try
            {
                string currentName = GameData.PlayerStats?.MyName;
                if (!string.IsNullOrEmpty(currentName) &&
                    SanitizeName(currentName) != _charName)
                {
                    SkillsPlugin.Log.LogWarning(
                        $"Save aborted: _charName='{_charName}' but current player is '{currentName}'");
                    _saveDirty = true;
                    return false;
                }
            }
            catch { }
            try
            {
                // Manual JSON — JsonUtility silently fails on our data structure
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("{");

                // Save training points and player level
                sb.AppendLine($"  \"__TrainingPoints\": {_data.TrainingPoints},");
                sb.AppendLine($"  \"__TrainingPointsSpent\": {_data.TrainingPointsSpent},");
                sb.AppendLine($"  \"__LastKnownPlayerLevel\": {_data.LastKnownPlayerLevel},");
                sb.AppendLine($"  \"__EquippedClassAuraId\": \"" +
                    SafeFileIO.EscapeJson(_data.EquippedClassAuraId ?? "") +
                    "\",");

                var skills = _data.GetAll();
                for (int i = 0; i < skills.Count; i++)
                {
                    var s = skills[i];
                    sb.Append($"  \"{SafeFileIO.EscapeJson(s.Name)}\": {{" +
                        $"\"Level\":{s.Level}," +
                        $"\"CurrentXp\":{s.CurrentXp.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}," +
                        $"\"TimesUsed\":{s.TimesUsed}," +
                        $"\"Successes\":{s.Successes}," +
                        $"\"Failures\":{s.Failures}}}");
                    if (i < skills.Count - 1) sb.AppendLine(",");
                    else sb.AppendLine();
                }
                sb.AppendLine("}");

                string json = sb.ToString();
                if (!SafeFileIO.TryWriteAllTextAtomic(GetPath(), json, out string mainError))
                    throw new IOException(mainError);

                // Save custom inventory to separate file
                bool inventorySaved = SaveInventoryFile();
                bool recipesSaved = SaveRecipesFile();
                bool bagsSaved = SaveBagsFile();
                if (!inventorySaved || !recipesSaved || !bagsSaved)
                    throw new IOException("One or more Classic Skills sidecars could not be saved.");

                SkillsPlugin.Log.LogInfo(
                    $"Saved skills for '{_charName}' ({json.Length} chars)");
                _saveDirty = false;
                _lastSaveTime = Time.unscaledTime;
                return true;
            }
            catch (Exception ex)
            {
                _saveDirty = true;
                SkillsPlugin.Log.LogError($"Failed to save skills: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        private static bool SaveInventoryFile()
        {
            try
            {
                if (_data.CustomInventory == null) return true;
                var items = new List<SavedCustomItem>(_data.CustomInventory);
                items.Sort((left, right) => string.Compare(left?.Name,
                    right?.Name, StringComparison.OrdinalIgnoreCase));
                string path = GetInventoryPath();
                var sb = new System.Text.StringBuilder();
                var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                sb.AppendLine("{");
                sb.Append($"  \"__InventoryFormat\": {InventorySidecarFormatVersion}");
                if (items.Count > 0) sb.AppendLine(",");
                else sb.AppendLine();
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] == null || string.IsNullOrWhiteSpace(items[i].Name))
                        throw new InvalidDataException(
                            "Custom inventory contains an invalid item record.");
                    if (!unique.Add(items[i].Name))
                        throw new InvalidDataException(
                            "Custom inventory contains a duplicate item record.");
                    sb.Append($"  \"{SafeFileIO.EscapeJson(items[i].Name)}\": {Mathf.Max(0, items[i].Count)}");
                    if (i < items.Count - 1) sb.AppendLine(",");
                    else sb.AppendLine();
                }
                sb.AppendLine("}");
                if (!SafeFileIO.TryWriteAllTextAtomic(path, sb.ToString(), out string error))
                    throw new IOException(error);
                LoadedInventoryFormatVersion = InventorySidecarFormatVersion;
                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"SaveInventoryFile error: {ex.Message}");
                return false;
            }
        }

        private static string GetPath()
        {
            return Path.Combine(RuntimePaths.DataDirectory,
                $"{_charName}.json");
        }

        private static string GetInventoryPath()
        {
            return Path.Combine(RuntimePaths.DataDirectory,
                $"{_charName}_inventory.json");
        }

        internal static string GetRecipesPath()
        {
            return Path.Combine(RuntimePaths.DataDirectory,
                $"{_charName}_recipes.json");
        }

        private static bool LoadRecipesFile()
        {
            try
            {
                string path = GetRecipesPath();
                if (!File.Exists(path) && !File.Exists(path + ".bak"))
                {
                    // First time — grant starter recipes
                    _data.EnsureStarterRecipes();
                    return true;
                }
                if (!SafeFileIO.TryReadAllTextWithBackup(path, IsJsonArray,
                    out string loaded, out bool recovered, out string readError))
                    throw new InvalidDataException(readError);
                string json = loaded.Trim();
                if (string.IsNullOrEmpty(json) || json == "[]")
                {
                    _data.EnsureStarterRecipes();
                    return true;
                }

                _data.KnownRecipes.Clear();
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match match in Regex.Matches(json,
                    "\\\"(?:\\\\.|[^\\\"\\\\])*\\\""))
                {
                    if (!SafeFileIO.TryDecodeJsonStringToken(match.Value,
                        out string name) || string.IsNullOrWhiteSpace(name) ||
                        name.Length > 512)
                        throw new InvalidDataException(
                            "Known recipe data contains an invalid name.");
                    if (known.Add(name))
                        _data.KnownRecipes.Add(name);
                }

                _data.EnsureStarterRecipes();
                SkillsPlugin.Log.LogInfo($"Loaded {_data.KnownRecipes.Count} known recipes.");
                if (recovered)
                    SkillsPlugin.Log.LogWarning("Recovered known recipes from backup.");
                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"LoadRecipesFile error: {ex.Message}");
                return false;
            }
        }

        public static bool SaveRecipesFile()
        {
            if (!HasLoaded || string.IsNullOrEmpty(_charName)) return false;
            try
            {
                string path = GetRecipesPath();
                string json = SerializeRecipesFile();
                if (!SafeFileIO.TryWriteAllTextAtomic(path, json, out string error))
                    throw new IOException(error);
                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"SaveRecipesFile error: {ex.Message}");
                return false;
            }
        }

        internal static string SerializeRecipesFile()
        {
            var recipes = new List<string>(_data.KnownRecipes);
            recipes.Sort(StringComparer.OrdinalIgnoreCase);
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sb = new System.Text.StringBuilder();
            sb.Append("[");
            for (int i = 0; i < recipes.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(recipes[i]) ||
                    recipes[i].Length > 512 || !unique.Add(recipes[i]))
                    throw new InvalidDataException(
                        "Known recipes contain an invalid or duplicate name.");
                sb.Append($"\"{SafeFileIO.EscapeJson(recipes[i])}\"");
                if (i < recipes.Count - 1) sb.Append(", ");
            }
            sb.Append("]");
            string json = sb.ToString();
            if (!IsRecipesJsonValid(json))
                throw new InvalidDataException(
                    "Generated recipe data failed validation.");
            return json;
        }

        private static string GetBagsPath()
        {
            return Path.Combine(RuntimePaths.DataDirectory,
                $"{_charName}_bags.json");
        }

        private static bool LoadBagsFile()
        {
            try
            {
                string path = GetBagsPath();
                if (!Items.BagSystem.RecoverPendingTransaction(path))
                    return false;
                if (!File.Exists(path) && !File.Exists(path + ".bak"))
                {
                    Items.BagSystem.Reset();
                    return true;
                }
                if (!SafeFileIO.TryReadAllTextWithBackup(path,
                    Items.BagSystem.IsSerializedDataValid, out string json,
                    out bool recovered, out string readError))
                    throw new InvalidDataException(readError);
                if (!Items.BagSystem.DeserializeBags(json.Trim()))
                    throw new InvalidDataException("Bag data could not be deserialized.");
                SkillsPlugin.Log.LogInfo($"Loaded bag contents.");
                if (recovered)
                    SkillsPlugin.Log.LogWarning("Recovered bag contents from backup.");
                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"LoadBagsFile error: {ex.Message}");
                return false;
            }
        }

        public static bool SaveBagsFile()
        {
            if (string.IsNullOrEmpty(_charName)) return false;
            try
            {
                string path = GetBagsPath();
                string json = Items.BagSystem.SerializeBags();
                if (!Items.BagSystem.IsSerializedDataValid(json))
                    throw new InvalidDataException("Refusing to write invalid bag data.");
                if (!SafeFileIO.TryWriteAllTextAtomic(path, json, out string error))
                    throw new IOException(error);
                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"SaveBagsFile error: {ex.Message}");
                return false;
            }
        }

        internal static bool IsInventoryJsonValid(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            string value = json.Trim();
            if (!Regex.IsMatch(value,
                "^\\{\\s*(?:\\\"(?:\\\\.|[^\"\\\\])*\\\"\\s*:\\s*\\d+\\s*" +
                "(?:,\\s*\\\"(?:\\\\.|[^\"\\\\])*\\\"\\s*:\\s*\\d+\\s*)*)?\\}$"))
                return false;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matches = Regex.Matches(value,
                "(\\\"(?:\\\\.|[^\\\"\\\\])*\\\")\\s*:\\s*(\\d+)");
            if (matches.Count > 10000) return false;
            foreach (Match match in matches)
            {
                if (!SafeFileIO.TryDecodeJsonStringToken(match.Groups[1].Value,
                    out string name) || string.IsNullOrWhiteSpace(name) ||
                    name.Length > 512 || !names.Add(name) ||
                    !int.TryParse(match.Groups[2].Value, out int count)) return false;
                if (string.Equals(name, "__InventoryFormat",
                    StringComparison.Ordinal) &&
                    (count < 1 || count > InventorySidecarFormatVersion))
                    return false;
            }
            return true;
        }

        private static bool IsJsonArray(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            string value = json.Trim();
            if (!Regex.IsMatch(value,
                "^\\[\\s*(?:\\\"(?:\\\\.|[^\"\\\\])*\\\"\\s*" +
                "(?:,\\s*\\\"(?:\\\\.|[^\"\\\\])*\\\"\\s*)*)?\\]$"))
                return false;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matches = Regex.Matches(value,
                "\\\"(?:\\\\.|[^\\\"\\\\])*\\\"");
            if (matches.Count > 10000) return false;
            foreach (Match match in matches)
            {
                if (!SafeFileIO.TryDecodeJsonStringToken(match.Value,
                    out string name) || string.IsNullOrWhiteSpace(name) ||
                    name.Length > 512 || !names.Add(name)) return false;
            }
            return true;
        }

        internal static bool IsRecipesJsonValid(string json) => IsJsonArray(json);

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
