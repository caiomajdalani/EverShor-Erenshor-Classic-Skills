using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ErenshorSkills.Skills
{
    /// <summary>
    /// TRADESKILLS — EverQuest's tradeskill system was a game within a game.
    /// Players gathered materials, found recipes, and combined items at
    /// crafting stations to create gear, consumables, and trade goods.
    /// Failing a combine could destroy your materials. Skill-ups were
    /// random and agonizing. It was glorious.
    ///
    /// Erenshor already has a basic smithing/forge system. These tradeskills
    /// layer on top of it with a leveling system and custom recipes using
    /// materials gathered from foraging, fishing, mob drops, and vendors.
    ///
    /// The six tradeskills:
    ///   Smithing    — Forge weapons and armor from ores and templates
    ///   Baking      — Cook food consumables with stat buffs
    ///   Brewing     — Brew drinks and potions with buff effects
    ///   Fletching   — Craft bows and ranged weapons
    ///   Jewelcraft  — Create rings, necklaces, and enchanted gems
    ///   Tailoring   — Sew cloth and leather armor, bags, and cloaks
    ///
    /// Each tradeskill:
    /// - Levels 1-50 with XP from successful and failed combines
    /// - Has tiered recipes unlocked at specific skill levels
    /// - Uses materials from foraging, mob drops, mining, and vendors
    /// - Higher skill = lower failure chance and access to better recipes
    /// - Recipes are executed via chat commands: /smith, /bake, /brew, etc.
    /// </summary>
    public static class Tradeskills
    {
        // ═════════════════════════════════════════════════════════════
        // Recipe definition
        // ═════════════════════════════════════════════════════════════

        public class Recipe
        {
            public string Name;              // Result item name
            public string Tradeskill;        // Which tradeskill
            public int MinSkillLevel;        // Minimum skill to attempt
            public int TrivialLevel;         // Skill level where it stops giving XP
            public float XpOnSuccess;        // XP awarded on success
            public float XpOnFailure;        // XP awarded on failure (EQ gave some)
            public string[] Materials;       // Required material names
            public int[] MaterialCounts;     // How many of each material
            public string ResultDescription; // What the item does
            public int GoldValue;            // Vendor value of result
            public bool DropOnly;            // Only learnable from enemy drops, not vendors
            public bool BossOnly;            // Only drops from boss/named mobs

            public Recipe(string name, string skill, int minLevel, int trivial,
                string[] mats, int[] counts, string desc, int gold,
                float successXp = 15f, float failXp = 5f,
                bool dropOnly = false, bool bossOnly = false)
            {
                Name = name;
                Tradeskill = skill;
                MinSkillLevel = minLevel;
                TrivialLevel = trivial;
                Materials = mats;
                MaterialCounts = counts;
                ResultDescription = desc;
                GoldValue = gold;
                XpOnSuccess = successXp;
                XpOnFailure = failXp;
                DropOnly = dropOnly;
                BossOnly = bossOnly;
            }
        }

        private sealed class BankMutation
        {
            public string Path;
            public string BeforeJson;
            public BankReadData Data;
            public bool Dirty;
            public string AfterJson => Dirty ? JsonUtility.ToJson(Data) : BeforeJson;
        }

        // ═════════════════════════════════════════════════════════════
        // Attempt a combine
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// Attempt to craft a recipe. Checks skill level, materials,
        /// and rolls for success/failure.
        /// </summary>
        public static void AttemptCombine(string tradeskill, string recipeName)
        {
            var skill = GetTradeskillEntry(tradeskill);
            if (skill == null)
            {
                ChatHelper.Send($"<color=#FF9800>[{tradeskill}]</color> Unknown tradeskill.");
                return;
            }

            // Find the recipe
            Recipe recipe = FindRecipe(tradeskill, recipeName);
            if (recipe == null)
            {
                ChatHelper.Send(
                    $"<color=#FF9800>[{tradeskill}]</color> " +
                    $"Unknown recipe: '{recipeName}'. Type /{tradeskill.ToLowerInvariant()} list " +
                    $"to see available recipes.");
                return;
            }

            // Check if recipe is known
            if (!SkillsSaveManager.Data.IsRecipeKnown(recipe.Name))
            {
                ChatHelper.Send(
                    $"<color=#FF9800>[{tradeskill}]</color> " +
                    $"You don't know the recipe for {recipe.Name}. " +
                    $"Find a trainer or recipe scroll to learn it.");
                return;
            }

            // Check minimum skill
            if (skill.Level < recipe.MinSkillLevel)
            {
                ChatHelper.Send(
                    $"<color=#FF9800>[{tradeskill}]</color> " +
                    $"You need {tradeskill} level {recipe.MinSkillLevel} " +
                    $"to attempt {recipe.Name}. (You are level {skill.Level})");
                return;
            }

            // Check materials
            if (!CheckMaterials(recipe))
            {
                ChatHelper.Send(
                    $"<color=#FF9800>[{tradeskill}]</color> " +
                    $"You don't have the required materials for {recipe.Name}.");
                ShowRecipeMaterials(recipe, tradeskill);
                return;
            }

            // Calculate success chance
            // Base 40% + 1% per level above minimum, capped at 95%
            float chance = GetSuccessChance(skill, recipe);

            bool success = UnityEngine.Random.value * 100f < chance;

            var mutation = Items.BagSystem.BeginExternalMutation();
            BankMutation bankMutation = null;
            try
            {
                bankMutation = BeginBankMutation();
                if (!ConsumeMaterials(recipe, bankMutation))
                    throw new InvalidOperationException("Required materials changed during the combine.");

                if (success && !TryGiveItem(recipe))
                    throw new InvalidOperationException(
                        $"There is no safe inventory slot for {recipe.Name}.");

                string bankPath = bankMutation?.Dirty == true ? bankMutation.Path : null;
                string bankBefore = bankMutation?.Dirty == true ? bankMutation.BeforeJson : null;
                string bankAfter = bankMutation?.Dirty == true ? bankMutation.AfterJson : null;
                if (!Items.BagSystem.CommitExternalMutation(
                    $"craft-{tradeskill}", mutation, bankPath, bankBefore, bankAfter))
                    throw new InvalidOperationException("The inventory transaction could not be saved.");

                if (bankMutation?.Dirty == true)
                    ReloadBankIfPresent();
            }
            catch (Exception ex)
            {
                Items.BagSystem.RollbackExternalMutation(mutation,
                    bankMutation?.Dirty == true ? bankMutation.Path : null,
                    bankMutation?.Dirty == true ? bankMutation.BeforeJson : null);
                if (bankMutation?.Dirty == true)
                    ReloadBankIfPresent();
                SkillsPlugin.Log.LogError($"{tradeskill} combine rolled back: {ex}");
                ChatHelper.Send(
                    $"<color=#FF9800>[{tradeskill}]</color> " +
                    $"<color=#EF5350>The combine was cancelled and all materials were restored: " +
                    $"{ex.Message}</color>");
                return;
            }

            skill.TimesUsed++;

            if (success)
            {
                skill.Successes++;

                // Tradeskill XP uses the trivial system
                SkillXpEngine.AwardTradeskillXp(skill, recipe.XpOnSuccess, recipe.TrivialLevel);

                ChatHelper.Send(
                    $"<color=#FF9800>[{tradeskill}]</color> " +
                    $"<color=#4CAF50>Success!</color> You have created: " +
                    $"<color=#FFFFFF>{recipe.Name}</color>");

                if (!string.IsNullOrEmpty(recipe.ResultDescription))
                    ChatHelper.Send(
                        $"  <color=#AAAAAA>{recipe.ResultDescription}</color>");
            }
            else
            {
                skill.Failures++;

                // EQ gave skillups on failure too — smaller XP
                SkillXpEngine.AwardTradeskillXp(skill, recipe.XpOnFailure, recipe.TrivialLevel);

                // Some materials may be lost on failure
                string[] lostMsgs = {
                    "Your attempt fails and some materials are lost.",
                    "The combine fails. Materials crumble to dust.",
                    "You fumble the process. Resources wasted.",
                    "The result is unusable. Your materials are consumed."
                };
                ChatHelper.Send(
                    $"<color=#FF9800>[{tradeskill}]</color> " +
                    $"<color=#EF5350>{lostMsgs[UnityEngine.Random.Range(0, lostMsgs.Length)]}</color>");
            }

            SkillsSaveManager.Save();
        }

        /// <summary>
        /// Deconstruct a crafted item to recover some materials.
        /// Higher tradeskill level = better recovery chance per material.
        /// </summary>
        public static void AttemptDeconstruct(string tradeskill, string itemName)
        {
            var skill = GetTradeskillEntry(tradeskill);
            if (skill == null) return;

            // Find the recipe that produces this item
            var recipes = GetRecipesForSkill(tradeskill);
            Recipe recipe = null;
            foreach (var r in recipes)
            {
                if (r.Name == itemName) { recipe = r; break; }
            }
            if (recipe == null)
            {
                ChatHelper.Send(
                    $"<color=#FF9800>[{tradeskill}]</color> " +
                    $"<color=#EF5350>{itemName} cannot be deconstructed.</color>");
                return;
            }

            var inventory = GameData.PlayerInv;
            ItemIcon sourceSlot = null;
            if (inventory?.StoredSlots != null)
            {
                foreach (var slot in inventory.StoredSlots)
                {
                    if (slot?.MyItem != null && slot.MyItem != inventory.Empty &&
                        slot.MyItem.ItemName == itemName)
                    {
                        sourceSlot = slot;
                        break;
                    }
                }
            }

            if (sourceSlot == null)
            {
                ChatHelper.Send(
                    $"<color=#FF9800>[{tradeskill}]</color> " +
                    $"<color=#EF5350>You don't have a {itemName} to deconstruct.</color>");
                return;
            }

            if (Items.BagSystem.IsBag(sourceSlot.MyItem))
            {
                ChatHelper.Send(
                    $"<color=#FF9800>[{tradeskill}]</color> " +
                    "<color=#EF5350>Bags cannot be deconstructed. Their contents and identity are protected.</color>");
                return;
            }

            // Calculate recovery chance per material based on skill level
            // Base: 30% at min skill level, scales to 80% at 50 levels above recipe
            float skillDiff = skill.Level - recipe.MinSkillLevel;
            float baseChance = Mathf.Clamp(0.30f + skillDiff * 0.01f, 0.15f, 0.80f);

            // Roll for each material
            var recoveredAmounts = new List<(string name, int amount)>();
            for (int i = 0; i < recipe.Materials.Length; i++)
            {
                string matName = recipe.Materials[i];
                int matCount = recipe.MaterialCounts[i];
                int got = 0;
                for (int j = 0; j < matCount; j++)
                {
                    if (UnityEngine.Random.value < baseChance)
                        got++;
                }
                if (got > 0)
                    recoveredAmounts.Add((matName, got));
            }

            var mutation = Items.BagSystem.BeginExternalMutation();
            try
            {
                if (sourceSlot.MyItem.Stackable && sourceSlot.Quantity > 1)
                    sourceSlot.Quantity--;
                else
                {
                    sourceSlot.MyItem = inventory.Empty;
                    sourceSlot.Quantity = 1;
                }
                sourceSlot.UpdateSlotImage();

                foreach (var recovered in recoveredAmounts)
                {
                    if (!Items.ItemFactory.GiveItemToPlayer(
                        recovered.name, recovered.amount))
                        throw new InvalidOperationException(
                            $"There is not enough inventory space for {recovered.name}.");
                }

                if (!Items.BagSystem.CommitExternalMutation(
                    $"deconstruct-{tradeskill}", mutation))
                    throw new InvalidOperationException("The inventory transaction could not be saved.");
            }
            catch (Exception ex)
            {
                Items.BagSystem.RollbackExternalMutation(mutation);
                SkillsPlugin.Log.LogError($"{tradeskill} deconstruction rolled back: {ex}");
                ChatHelper.Send(
                    $"<color=#FF9800>[{tradeskill}]</color> " +
                    $"<color=#EF5350>Deconstruction was cancelled and the item was restored: " +
                    $"{ex.Message}</color>");
                return;
            }

            // Award small XP for deconstructing
            float xp = 2f + recipe.MinSkillLevel * 0.1f;
            SkillXpEngine.AwardTradeskillXp(skill, xp, recipe.TrivialLevel);

            if (recoveredAmounts.Count > 0)
            {
                var recovered = new List<string>();
                foreach (var entry in recoveredAmounts)
                    recovered.Add($"{entry.amount}x {entry.name}");
                string matList = string.Join(", ", recovered);
                ChatHelper.Send(
                    $"<color=#FF9800>[{tradeskill}]</color> " +
                    $"Deconstructed <color=#FFFFFF>{itemName}</color>. " +
                    $"Recovered: <color=#4CAF50>{matList}</color> " +
                    $"<color=#AAAAAA>({baseChance * 100f:F0}% recovery rate)</color>");
            }
            else
            {
                ChatHelper.Send(
                    $"<color=#FF9800>[{tradeskill}]</color> " +
                    $"Deconstructed <color=#FFFFFF>{itemName}</color> but recovered nothing. " +
                    $"<color=#AAAAAA>({baseChance * 100f:F0}% recovery rate)</color>");
            }

            SkillsSaveManager.Save();
        }

        /// <summary>List available recipes for a tradeskill.</summary>
        public static void ListRecipes(string tradeskill)
        {
            var skill = GetTradeskillEntry(tradeskill);
            if (skill == null) return;

            var recipes = GetRecipesForSkill(tradeskill);

            ChatHelper.Send(
                $"<color=#FF9800>═══ {tradeskill} Recipes (Lv {skill.Level}) ═══</color>");

            foreach (var r in recipes)
            {
                bool known = SkillsSaveManager.Data.IsRecipeKnown(r.Name);
                string status;
                if (!known)
                    status = "<color=#888888>[???]</color>";
                else if (skill.Level < r.MinSkillLevel)
                    status = $"<color=#EF5350>[Lv {r.MinSkillLevel}]</color>";
                else if (skill.Level >= r.TrivialLevel)
                    status = "<color=#666666>[Trivial]</color>";
                else
                    status = $"<color=#4CAF50>[Lv {r.MinSkillLevel}]</color>";

                string name = known ? $"<color=#FFFFFF>{r.Name}</color>" : "<color=#888888>Unknown Recipe</color>";
                string trivial = known ? $" <color=#AAAAAA>(trivial at {r.TrivialLevel})</color>" : "";

                ChatHelper.Send($"  {status} {name}{trivial}");
            }

            ChatHelper.Send(
                $"<color=#FF9800>═══════════════════════════</color>");
        }

        // ═════════════════════════════════════════════════════════════
        // Recipe databases
        // ═════════════════════════════════════════════════════════════

        /// <summary>Public accessor for the UI window to read recipe lists.</summary>
        public static List<Recipe> GetRecipesForSkillPublic(string tradeskill)
        {
            return GetRecipesForSkill(tradeskill);
        }

        /// <summary>
        /// Single authoritative success calculation shared by crafting and UI.
        /// Keeping the displayed and rolled values on the same path prevents
        /// percentage drift when tuning the formula.
        /// </summary>
        public static float GetSuccessChance(SkillEntry skill, Recipe recipe)
        {
            if (skill == null || recipe == null) return 0f;
            return Mathf.Clamp(
                40f + (skill.Level - recipe.MinSkillLevel) * 1.5f,
                15f, 95f);
        }

        private static List<Recipe> GetRecipesForSkill(string tradeskill)
        {
            List<Recipe> recipes;
            switch (tradeskill)
            {
                case "Smithing":    recipes = SmithingRecipes(); break;
                case "Baking":      recipes = BakingRecipes(); break;
                case "Brewing":     recipes = BrewingRecipes(); break;
                case "Fletching":   recipes = FletchingRecipes(); break;
                case "Jewelcraft":  recipes = JewelcraftRecipes(); break;
                case "Tailoring":   recipes = TailoringRecipes(); break;
                default:            return new List<Recipe>();
            }
            // Any recipe requiring skill > 90 is automatically drop-only
            foreach (var r in recipes)
            {
                if (r.MinSkillLevel > 90)
                    r.DropOnly = true;
            }
            return recipes;
        }

        // ── SMITHING ────────────────────────────────────────────────

        private static List<Recipe> SmithingRecipes()
        {
            return new List<Recipe>
            {
                new Recipe("Copper Rivets", "Smithing", 1, 15,
                    new[] { "Chunk of Copper Ore" }, new[] { 2 },
                    "Basic fasteners. Used in other crafts.", 3),
                new Recipe("Crude Iron Dagger", "Smithing", 1, 18,
                    new[] { "Chunk of Iron Ore", "Coal" }, new[] { 1, 1 },
                    "A crude but effective blade, hammered from raw iron.", 8),
                new Recipe("Hammered Iron Shortsword", "Smithing", 2, 20,
                    new[] { "Chunk of Iron Ore", "Coal" }, new[] { 2, 2 },
                    "Forged flat on an anvil and given a rough edge.", 10),
                new Recipe("Banded Iron Helm", "Smithing", 3, 22,
                    new[] { "Chunk of Iron Ore", "Copper Rivets" }, new[] { 2, 1 },
                    "Iron bands riveted over a leather frame. Keeps the skull intact.", 12),
                new Recipe("Steel Chain Links", "Smithing", 10, 30,
                    new[] { "Chunk of Iron Ore", "Coal" }, new[] { 2, 2 },
                    "Interlocking links. Used in Tailoring for chainmail.", 10),
                new Recipe("Reinforced Buckle", "Smithing", 15, 35,
                    new[] { "Chunk of Iron Ore", "Copper Rivets" }, new[] { 1, 1 },
                    "Sturdy buckle for armor and belts.", 12),
                new Recipe("Tempered Blade Blank", "Smithing", 20, 40,
                    new[] { "Chunk of Iron Ore", "Coal", "Healing Moss" }, new[] { 2, 3, 1 },
                    "An unfinished blade. Ready for a hilt and sharpening.", 25),
                new Recipe("Mithril Wire", "Smithing", 25, 45,
                    new[] { "Runestone Shard", "Coal" }, new[] { 1, 2 },
                    "Fine wire for jewelry settings. Used in Jewelcraft.", 30),
                new Recipe("Forged Shield Boss", "Smithing", 30, 50,
                    new[] { "Chunk of Iron Ore", "Chunk of Copper Ore", "Coal" }, new[] { 3, 1, 3 },
                    "Heavy shield centerpiece. +8 AC, +18 HP, +7 End.", 45),
                new Recipe("Hardened Alloy Ingot", "Smithing", 40, 50,
                    new[] { "Malaroth Scale Fragment", "Coal", "Obsidian Shard" }, new[] { 2, 4, 1 },
                    "Dense alloy infused with dragon essence. Premium smithing material.", 80),
                new Recipe("Silver Wire", "Smithing", 35, 55,
                    new[] { "Chunk of Silver Ore", "Coal" }, new[] { 2, 1 },
                    "Fine silver wire. Used in Jewelcraft.", 15),
                new Recipe("Adamantite Rivets", "Smithing", 45, 65,
                    new[] { "Chunk of Adamantite Ore", "Coal" }, new[] { 1, 2 },
                    "Unbreakable fasteners. Used in high-tier crafts.", 35),
                new Recipe("Gold Setting", "Smithing", 50, 70,
                    new[] { "Chunk of Gold Ore", "Coal" }, new[] { 2, 2 },
                    "Ornate gold framework. Used in Jewelcraft.", 30),
                new Recipe("Adamantite Plate", "Smithing", 60, 80,
                    new[] { "Chunk of Adamantite Ore", "Coal", "Adamantite Rivets" }, new[] { 3, 4, 2 },
                    "Heavy armor plate. Used in Tailoring.", 50),
                new Recipe("Platinum Filigree", "Smithing", 70, 90,
                    new[] { "Chunk of Platinum Ore", "Coal", "Silver Wire" }, new[] { 2, 3, 1 },
                    "Delicate platinum work. Used in Jewelcraft.", 45),
                new Recipe("Soulsteel Chain Links", "Smithing", 80, 100,
                    new[] { "Soulsteel Billet", "Coal" }, new[] { 2, 3 },
                    "Spirit-infused chain links. Used in Tailoring.", 80),
                new Recipe("Starmetal Blade Blank", "Smithing", 90, 110,
                    new[] { "Starmetal Fragment", "Coal", "Adamantite Rivets" }, new[] { 2, 4, 1 },
                    "Celestial metal blade. Used in Fletching.", 70),
                new Recipe("Moonforged Hatchet", "Smithing", 100, 120,
                    new[] { "Moonsilver Bar", "Petrified Heartwood", "Adamantite Rivets" }, new[] { 2, 1, 2 },
                    "Gleaming one-hand axe. +8 Strength.", 120),
                new Recipe("Sunforged Greatsword", "Smithing", 115, 135,
                    new[] { "Sunforged Ingot", "Starmetal Blade Blank", "Tough Hide Strip" }, new[] { 2, 1, 1 },
                    "Two-hand blade of solar fire. +10 Str, +5 Fire Resist.", 200),
                new Recipe("Voidsteel Maul", "Smithing", 130, 150,
                    new[] { "Void Iron Chunk", "Elder Wyrm Bone", "Soulsteel Chain Links" }, new[] { 3, 2, 1 },
                    "Two-hand blunt of abyssal might. +12 Str, +8 End.", 280),
                new Recipe("Titan's Bulwark", "Smithing", 145, 165,
                    new[] { "Adamantite Plate", "Soulsteel Chain Links", "Titan's Knucklebone" }, new[] { 3, 2, 1 },
                    "Shield. +15 AC, +6 End, +4 all Resists.", 350),
                new Recipe("Eternity's Edge", "Smithing", 160, 180,
                    new[] { "Starmetal Blade Blank", "Crystallized Time", "Soulsteel Billet" }, new[] { 2, 1, 2 },
                    "One-hand sword. +14 Str, +8 Dex, +5 Haste.", 500),
                // ── Drop-only recipes (from enemy kills) ──
                new Recipe("Rusted Heirloom Blade", "Smithing", 10, 25,
                    new[] { "Charred Root", "Chunk of Iron Ore" }, new[] { 1, 2 },
                    "Ancient blade. +2 Str.", 8, 15f, 5f, true, false),
                new Recipe("Goblin-Forged Shiv", "Smithing", 18, 35,
                    new[] { "Chunk of Iron Ore", "Coal", "Smooth Pebble" }, new[] { 1, 2, 1 },
                    "Quick dagger. +3 Agi, +2 Dex.", 15, 15f, 5f, true, false),
                new Recipe("Burnished War Pick", "Smithing", 25, 42,
                    new[] { "Chunk of Iron Ore", "Coal", "Copper Rivets" }, new[] { 2, 3, 1 },
                    "Mining weapon. +4 Str, +2 End.", 22, 15f, 5f, true, true),
                new Recipe("Blighted Iron Mace", "Smithing", 30, 48,
                    new[] { "Chunk of Iron Ore", "Coal", "Blightvein Moss" }, new[] { 2, 3, 1 },
                    "Cursed mace. +5 Str, +4 Poison Resist.", 30, 15f, 5f, true, false),
                new Recipe("Fernallan War Axe", "Smithing", 38, 55,
                    new[] { "Chunk of Iron Ore", "Coal", "Enchanted Root" }, new[] { 3, 3, 1 },
                    "Druidic axe. +5 Str, +3 Wis.", 38, 15f, 5f, true, false),
                new Recipe("Tempered Partisan", "Smithing", 42, 60,
                    new[] { "Chunk of Iron Ore", "Coal", "Steel Chain Links" }, new[] { 3, 4, 1 },
                    "Two-hand polearm. +6 Str, +4 Dex.", 45, 15f, 5f, true, true),
                new Recipe("Braxonian Flamberge", "Smithing", 48, 68,
                    new[] { "Chunk of Iron Ore", "Coal", "Firebloom Pepper" }, new[] { 3, 4, 1 },
                    "Wavy blade. +7 Str, +5 Fire Resist.", 55, 15f, 5f, true, false),
                new Recipe("Boneweave Shield", "Smithing", 55, 75,
                    new[] { "Beast Bone Shard", "Chunk of Iron Ore", "Steel Chain Links" }, new[] { 2, 2, 2 },
                    "Shield of bone. +8 AC, +4 End.", 65, 15f, 5f, true, false),
                new Recipe("Ghostmetal Rapier", "Smithing", 60, 82,
                    new[] { "Runestone Shard", "Coal", "Silver Wire" }, new[] { 2, 3, 1 },
                    "Ethereal blade. +7 Dex, +5 Agi.", 75, 15f, 5f, true, true),
                new Recipe("Warden's Maul", "Smithing", 68, 88,
                    new[] { "Chunk of Adamantite Ore", "Coal", "Ironbark Plank" }, new[] { 2, 4, 1 },
                    "Heavy blunt. +8 Str, +6 End.", 85, 15f, 5f, true, false),
                new Recipe("Emberthorn Scimitar", "Smithing", 72, 95,
                    new[] { "Chunk of Adamantite Ore", "Coal", "Firebloom Pepper" }, new[] { 2, 4, 2 },
                    "Burning blade. +8 Str, +8 Fire Resist.", 95, 15f, 5f, true, false),
                new Recipe("Nightfall Greatsword", "Smithing", 78, 100,
                    new[] { "Chunk of Adamantite Ore", "Coal", "Void Iron Chunk" }, new[] { 2, 5, 1 },
                    "Dark two-hander. +10 Str, +6 all Resists.", 110, 15f, 5f, true, true),
                new Recipe("Runeforged Hammer", "Smithing", 85, 108,
                    new[] { "Chunk of Adamantite Ore", "Coal", "Runestone Shard" }, new[] { 3, 4, 2 },
                    "Glowing hammer. +9 Str, +7 Wis.", 120, 15f, 5f, true, false),
                new Recipe("Frostbite Battleaxe", "Smithing", 90, 115,
                    new[] { "Chunk of Adamantite Ore", "Frostweave Strand", "Coal" }, new[] { 3, 1, 4 },
                    "Frozen axe. +10 Str, +10 Cold Resist.", 135, 15f, 5f, true, false),
                new Recipe("Wyrmbone Defender", "Smithing", 95, 120,
                    new[] { "Elder Wyrm Bone", "Adamantite Plate", "Soulsteel Chain Links" }, new[] { 1, 1, 1 },
                    "Shield. +12 AC, +8 End, +6 Magic Resist.", 155, 15f, 5f, true, true),
                new Recipe("Searing Iron Dirk", "Smithing", 100, 125,
                    new[] { "Sunforged Ingot", "Coal", "Adamantite Rivets" }, new[] { 1, 3, 1 },
                    "Hot dagger. +8 Dex, +8 Agi.", 145, 15f, 5f, true, false),
                new Recipe("Stonecrusher Flail", "Smithing", 108, 132,
                    new[] { "Void Iron Chunk", "Elder Wyrm Bone", "Coal" }, new[] { 2, 1, 4 },
                    "Heavy flail. +11 Str, +8 End.", 170, 15f, 5f, true, false),
                new Recipe("Eclipse Longsword", "Smithing", 112, 138,
                    new[] { "Moonsilver Bar", "Void Iron Chunk", "Soulsteel Billet" }, new[] { 1, 1, 1 },
                    "Shadow blade. +10 Str, +8 Int.", 190, 15f, 5f, true, true),
                new Recipe("Thunderforge Warhammer", "Smithing", 118, 142,
                    new[] { "Starmetal Fragment", "Coal", "Adamantite Plate" }, new[] { 1, 5, 1 },
                    "Storm hammer. +12 Str, +8 Elemental Resist.", 210, 15f, 5f, true, false),
                new Recipe("Blightfang Dagger", "Smithing", 122, 148,
                    new[] { "Soulsteel Billet", "Breath of the Abyss", "Coal" }, new[] { 1, 1, 3 },
                    "Poison dagger. +10 Dex, +10 Agi.", 225, 15f, 5f, true, false),
                new Recipe("Worldsplitter Axe", "Smithing", 128, 152,
                    new[] { "Starmetal Fragment", "Adamantite Plate", "Elder Wyrm Bone" }, new[] { 2, 1, 1 },
                    "Two-hand axe. +13 Str, +8 End.", 250, 15f, 5f, true, true),
                new Recipe("Dawnshard Falchion", "Smithing", 132, 158,
                    new[] { "Sunforged Ingot", "Starmetal Fragment", "Moonsilver Bar" }, new[] { 1, 1, 1 },
                    "Radiant blade. +11 Str, +9 Dex.", 270, 15f, 5f, true, false),
                new Recipe("Abyssal Cleaver", "Smithing", 138, 162,
                    new[] { "Void Iron Chunk", "Soulsteel Billet", "Breath of the Abyss" }, new[] { 2, 1, 1 },
                    "Dark two-hander. +14 Str, +10 all Resists.", 295, 15f, 5f, true, false),
                new Recipe("Starforged Bastard Sword", "Smithing", 142, 168,
                    new[] { "Starmetal Fragment", "Crystallized Time", "Soulsteel Billet" }, new[] { 2, 1, 1 },
                    "Star blade. +13 Str, +10 Dex, +3% haste.", 320, 15f, 5f, true, true),
                new Recipe("Dragonlord's Lance", "Smithing", 148, 172,
                    new[] { "Elder Wyrm Bone", "Starmetal Fragment", "Adamantite Plate" }, new[] { 2, 2, 2 },
                    "Polearm. +14 Str, +12 End.", 350, 15f, 5f, true, false),
                new Recipe("Void Reaver's Khopesh", "Smithing", 152, 175,
                    new[] { "Void Iron Chunk", "Crystallized Time", "Soulsteel Chain Links" }, new[] { 2, 1, 1 },
                    "Curved blade. +13 Str, +11 Dex.", 380, 15f, 5f, true, false),
                new Recipe("Phoenix-Forged Claymore", "Smithing", 155, 178,
                    new[] { "Sunforged Ingot", "Phoenix Feather", "Starmetal Fragment" }, new[] { 2, 1, 1 },
                    "Fire two-hander. +15 Str, +12 Fire Resist.", 410, 15f, 5f, true, true),
                new Recipe("Moonbane Kukri", "Smithing", 158, 178,
                    new[] { "Moonsilver Bar", "Breath of the Abyss", "Soulsteel Billet" }, new[] { 2, 1, 1 },
                    "Silver blade. +12 Dex, +12 Agi.", 430, 15f, 5f, true, false),
                new Recipe("Warforged Sentinel Shield", "Smithing", 162, 178,
                    new[] { "Adamantite Plate", "Starmetal Fragment", "Soulsteel Chain Links" }, new[] { 3, 1, 2 },
                    "Shield. +18 AC, +10 End.", 460, 15f, 5f, true, false),
                new Recipe("Ironheart Champion's Blade", "Smithing", 165, 178,
                    new[] { "Starmetal Fragment", "Soulsteel Billet", "Crystallized Time" }, new[] { 2, 2, 1 },
                    "Sword. +13 Str, +10 Dex, +4% haste.", 480, 15f, 5f, true, true),

            };
        }

        // ── BAKING ──────────────────────────────────────────────────

        private static List<Recipe> BakingRecipes()
        {
            return new List<Recipe>
            {
                new Recipe("Trail Rations", "Baking", 1, 10,
                    new[] { "Wild Berries", "Edible Root" }, new[] { 2, 1 },
                    "Simple food. Restores small HP over time.", 2),
                new Recipe("Herb Bread", "Baking", 5, 20,
                    new[] { "Golden Wheat", "Aromatic Herb Bundle" }, new[] { 2, 1 },
                    "Fragrant bread. +2 Wisdom for 5 minutes.", 6),
                new Recipe("Mushroom Stew", "Baking", 10, 25,
                    new[] { "Fresh Mushroom", "Edible Root", "Pod of Water" }, new[] { 2, 1, 1 },
                    "Hearty stew. +3 Endurance for 5 minutes.", 10),
                new Recipe("Honeycake", "Baking", 15, 30,
                    new[] { "Honey Comb Fragment", "Golden Wheat", "Wild Berries" }, new[] { 1, 2, 2 },
                    "Sweet and energizing. +4 Charisma for 5 minutes.", 14),
                new Recipe("Peppered Trail Jerky", "Baking", 20, 35,
                    new[] { "Beast Meat", "Firebloom Pepper" }, new[] { 1, 1 },
                    "Fiery meat. +5 Strength, +3 Fire Resist for 10 min.", 22),
                new Recipe("Solunarian Feast", "Baking", 30, 45,
                    new[] { "Solunarian Fruit", "Dawn Blossom", "Golden Wheat" }, new[] { 2, 1, 3 },
                    "Celestial banquet. +5 all stats for 10 minutes.", 50),
                new Recipe("Grand Ambrosia", "Baking", 40, 50,
                    new[] { "Garden Ambrosia", "Essence of the Garden", "Solunarian Fruit" }, new[] { 1, 1, 2 },
                    "Divine food. +8 all stats, extends active buffs by 50%.", 100),
                new Recipe("Ironbark Smoked Meat", "Baking", 45, 65,
                    new[] { "Beast Meat", "Ironbark Plank", "Healing Moss" }, new[] { 2, 1, 1 },
                    "Hearty smoked meat. +6 End, +3 Str for 10 min.", 25),
                new Recipe("Frostberry Tart", "Baking", 55, 75,
                    new[] { "Wild Berries", "Frostweave Strand", "Golden Wheat" }, new[] { 3, 1, 2 },
                    "Chilled dessert. +7 Int, +4 Wis for 10 min.", 35),
                new Recipe("Drake Bone Broth", "Baking", 65, 85,
                    new[] { "Beast Meat", "Beast Bone Shard", "Pod of Water" }, new[] { 1, 1, 2 },
                    "Fortifying broth. +8 End, +5 Str for 15 min.", 50),
                new Recipe("Phoenix-Spiced Kebabs", "Baking", 80, 100,
                    new[] { "Beast Meat", "Phoenix Feather", "Firebloom Pepper" }, new[] { 2, 1, 1 },
                    "Fiery meat. +10 Str, +8 Fire Resist for 15 min.", 75),
                new Recipe("Moonlight Risotto", "Baking", 95, 115,
                    new[] { "Solunarian Fruit", "Golden Wheat", "Moonsilver Bar" }, new[] { 2, 3, 1 },
                    "Silvery rice dish. +8 Wis, +8 Int for 15 min.", 100),
                new Recipe("Titan's Feast Platter", "Baking", 110, 130,
                    new[] { "Elder Wyrm Bone", "Firesage Bundle", "Garden Ambrosia" }, new[] { 1, 1, 1 },
                    "Enormous meal. +10 all stats for 15 min.", 150),
                new Recipe("Void-Touched Confection", "Baking", 130, 150,
                    new[] { "Breath of the Abyss", "Dawn Nectar", "Essence of the Garden" }, new[] { 1, 1, 1 },
                    "Unsettling but powerful. +12 all stats for 20 min.", 220),
                new Recipe("Eternal Banquet", "Baking", 155, 175,
                    new[] { "Crystallized Time", "Essence of Eternity", "Solunarian Fruit" }, new[] { 1, 1, 2 },
                    "Time-frozen feast. +14 all stats, +5 HP regen for 20 min.", 350),
                new Recipe("Cosmos Cake", "Baking", 170, 180,
                    new[] { "Thread of Fate", "Essence of the Garden", "Dawn Nectar" }, new[] { 1, 2, 2 },
                    "Tastes of infinity. +15 all stats, +3% haste for 25 min.", 500),
                // ── Fish-based recipes (requires fish from fishing) ──
                new Recipe("Pan-Seared Fish", "Baking", 8, 22,
                    new[] { "Raw Fish", "Edible Root" }, new[] { 1, 1 },
                    "Simple cooked fish. +3 End, +2 Str for 8 min.", 8),
                new Recipe("Herb-Crusted Fish Fillet", "Baking", 20, 38,
                    new[] { "Raw Fish", "Aromatic Herb Bundle", "Golden Wheat" }, new[] { 1, 1, 1 },
                    "Seasoned fish. +5 End, +4 Str for 10 min.", 20),
                new Recipe("Spicy Fish Stew", "Baking", 35, 52,
                    new[] { "Raw Fish", "Firebloom Pepper", "Pod of Water" }, new[] { 2, 1, 2 },
                    "Warming stew. +6 End, +5 Str, +4 Fire Resist for 12 min.", 35),
                new Recipe("Moongill Sashimi", "Baking", 50, 72,
                    new[] { "Moongill Trout", "Shimmer Herb", "Dawn Nectar" }, new[] { 1, 1, 1 },
                    "Rare delicacy. +8 Int, +6 Wis, +4 MP regen for 15 min.", 65),
                new Recipe("Smoked Beast-Fish Platter", "Baking", 65, 88,
                    new[] { "Raw Fish", "Beast Meat", "Firebloom Pepper" }, new[] { 1, 1, 1 },
                    "Surf and turf. +8 Str, +8 End for 15 min.", 55),
                new Recipe("Golden Carp Feast", "Baking", 85, 110,
                    new[] { "Golden Carp", "Garden Ambrosia", "Golden Wheat" }, new[] { 1, 1, 2 },
                    "Royal fish dish. +10 all stats for 15 min.", 110),
                new Recipe("Wyrm-Smoked Fish", "Baking", 105, 130,
                    new[] { "Raw Fish", "Elder Wyrm Bone", "Aromatic Herb Bundle" }, new[] { 2, 1, 2 },
                    "Dragon-fire smoked. +12 End, +10 Str for 18 min.", 160),
                new Recipe("Celestial Fish Pie", "Baking", 135, 160,
                    new[] { "Moongill Trout", "Golden Wheat", "Essence of the Garden" }, new[] { 1, 3, 1 },
                    "Divine fish pie. +12 all stats, +4 HP regen for 20 min.", 260),
                new Recipe("Starmetal-Glazed Catch", "Baking", 160, 178,
                    new[] { "Golden Carp", "Starmetal Fragment", "Dawn Nectar" }, new[] { 1, 1, 1 },
                    "Cosmic fish dish. +14 all stats, +3% haste for 22 min.", 420),
                // ── Drop-only recipes (from enemy kills) ──
                new Recipe("Campfire Ash Cake", "Baking", 10, 25,
                    new[] { "Charred Root", "Golden Wheat" }, new[] { 2, 1 },
                    "Simple flatbread. +2 End for 5 min.", 5, 15f, 5f, true, false),
                new Recipe("Honeyed Fig Rolls", "Baking", 18, 35,
                    new[] { "Honey Comb Fragment", "Wild Berries", "Golden Wheat" }, new[] { 1, 2, 1 },
                    "Sweet rolls. +3 Cha, +2 Wis for 5 min.", 12, 15f, 5f, true, false),
                new Recipe("Goblin Pepper Steak", "Baking", 25, 42,
                    new[] { "Beast Meat", "Firebloom Pepper" }, new[] { 1, 2 },
                    "Spicy meat. +4 Str, +3 Fire Resist for 8 min.", 20, 15f, 5f, true, true),
                new Recipe("Fernallan Herb Pie", "Baking", 30, 48,
                    new[] { "Aromatic Herb Bundle", "Golden Wheat", "Healing Moss" }, new[] { 2, 2, 1 },
                    "Savory pie. +5 Wis, +3 Int for 8 min.", 28, 15f, 5f, true, false),
                new Recipe("Braxonian Cactus Bread", "Baking", 38, 55,
                    new[] { "Desert Cactus Fruit", "Golden Wheat", "Pod of Water" }, new[] { 2, 2, 1 },
                    "Dense bread. +5 End, +4 Str for 10 min.", 35, 15f, 5f, true, false),
                new Recipe("Firebloom Dumpling", "Baking", 42, 60,
                    new[] { "Firebloom Pepper", "Golden Wheat", "Healing Moss" }, new[] { 2, 3, 1 },
                    "Fiery food. +6 Str, +6 Fire Resist for 10 min.", 42, 15f, 5f, true, true),
                new Recipe("Moonpetal Scones", "Baking", 48, 68,
                    new[] { "Dawn Blossom", "Golden Wheat", "Honey Comb Fragment" }, new[] { 1, 3, 1 },
                    "Delicate pastry. +7 Wis, +5 Int for 10 min.", 50, 15f, 5f, true, false),
                new Recipe("Blightvein Mushroom Soup", "Baking", 55, 75,
                    new[] { "Blightvein Moss", "Fresh Mushroom", "Pod of Water" }, new[] { 2, 2, 2 },
                    "Dark soup. +7 Int, +5 Poison Resist for 12 min.", 60, 15f, 5f, true, false),
                new Recipe("Smoked Beast Jerky", "Baking", 60, 82,
                    new[] { "Beast Meat", "Firebloom Pepper", "Healing Moss" }, new[] { 2, 1, 1 },
                    "Tough meat. +8 End, +6 Str for 12 min.", 68, 15f, 5f, true, true),
                new Recipe("Solunarian Fruit Tart", "Baking", 68, 88,
                    new[] { "Solunarian Fruit", "Golden Wheat", "Honey Comb Fragment" }, new[] { 2, 2, 1 },
                    "Sweet tart. +7 all stats for 10 min.", 80, 15f, 5f, true, false),
                new Recipe("Phoenix Pepper Stew", "Baking", 72, 95,
                    new[] { "Phoenix Feather", "Firebloom Pepper", "Pod of Water" }, new[] { 1, 2, 3 },
                    "Burning stew. +9 Str, +10 Fire Resist for 12 min.", 90, 15f, 5f, true, false),
                new Recipe("Abyssal Mushroom Risotto", "Baking", 78, 100,
                    new[] { "Blightvein Moss", "Golden Wheat", "Breath of the Abyss" }, new[] { 2, 3, 1 },
                    "Dark rice dish. +10 Int, +8 all Resists for 12 min.", 105, 15f, 5f, true, true),
                new Recipe("Titan Root Casserole", "Baking", 85, 108,
                    new[] { "Edible Root", "Elder Wyrm Bone", "Aromatic Herb Bundle" }, new[] { 3, 1, 2 },
                    "Heavy meal. +10 End, +8 Str for 15 min.", 115, 15f, 5f, true, false),
                new Recipe("Celestial Honey Cakes", "Baking", 90, 115,
                    new[] { "Dawn Nectar", "Golden Wheat", "Honey Comb Fragment" }, new[] { 1, 3, 1 },
                    "Heavenly cakes. +8 all stats for 12 min.", 130, 15f, 5f, true, false),
                new Recipe("Frostfire Goulash", "Baking", 95, 120,
                    new[] { "Frostweave Strand", "Firebloom Pepper", "Pod of Water" }, new[] { 1, 2, 3 },
                    "Hot-cold stew. +10 Fire/Cold Resist, +6 End for 15 min.", 145, 15f, 5f, true, true),
                new Recipe("Starmetal-Seared Steak", "Baking", 100, 125,
                    new[] { "Beast Meat", "Starmetal Fragment", "Firebloom Pepper" }, new[] { 2, 1, 1 },
                    "Cosmic meat. +10 Str, +8 End for 15 min.", 155, 15f, 5f, true, false),
                new Recipe("Moonlit Garden Salad", "Baking", 108, 132,
                    new[] { "Solunarian Fruit", "Aromatic Herb Bundle", "Dawn Blossom" }, new[] { 2, 2, 1 },
                    "Fresh salad. +9 Wis, +9 Int, +5 MP regen for 15 min.", 170, 15f, 5f, true, false),
                new Recipe("Voidberry Pudding", "Baking", 112, 138,
                    new[] { "Nightshade Berry", "Dawn Nectar", "Golden Wheat" }, new[] { 2, 1, 2 },
                    "Dark dessert. +10 Int, +8 Wis, +8 Magic Resist for 15 min.", 185, 15f, 5f, true, true),
                new Recipe("Dragon's Feast", "Baking", 118, 142,
                    new[] { "Elder Wyrm Bone", "Firebloom Pepper", "Garden Ambrosia" }, new[] { 1, 2, 1 },
                    "Legendary meal. +11 Str, +11 End for 18 min.", 200, 15f, 5f, true, false),
                new Recipe("Essence-Infused Bread", "Baking", 122, 148,
                    new[] { "Essence of the Garden", "Golden Wheat", "Dawn Nectar" }, new[] { 1, 3, 1 },
                    "Glowing bread. +10 all stats for 15 min.", 220, 15f, 5f, true, false),
                new Recipe("Sunfire Souffle", "Baking", 128, 152,
                    new[] { "Sunforged Ingot", "Golden Wheat", "Phoenix Feather" }, new[] { 1, 2, 1 },
                    "Solar food. +12 Str, +10 Fire Resist for 18 min.", 240, 15f, 5f, true, true),
                new Recipe("Prismatic Fruit Bowl", "Baking", 132, 158,
                    new[] { "Solunarian Fruit", "Wild Berries", "Dawn Nectar" }, new[] { 3, 3, 1 },
                    "Rainbow fruit. +10 all stats, +3 HP regen for 18 min.", 260, 15f, 5f, true, false),
                new Recipe("Titan's Roast", "Baking", 138, 162,
                    new[] { "Beast Meat", "Titan's Knucklebone", "Firebloom Pepper" }, new[] { 3, 1, 1 },
                    "Massive roast. +14 End, +12 Str for 20 min.", 285, 15f, 5f, true, false),
                new Recipe("Worldtree Acorn Bread", "Baking", 142, 168,
                    new[] { "Worldtree Splinter", "Golden Wheat", "Dawn Nectar" }, new[] { 1, 3, 1 },
                    "Ancient bread. +12 Wis, +10 Int, +5 MP regen for 20 min.", 310, 15f, 5f, true, true),
                new Recipe("Voidfire Curry", "Baking", 148, 172,
                    new[] { "Breath of the Abyss", "Firebloom Pepper", "Pod of Water" }, new[] { 1, 2, 3 },
                    "Unstable food. +13 Int, +12 all Resists for 18 min.", 335, 15f, 5f, true, false),
                new Recipe("Celestial Banquet Spread", "Baking", 152, 175,
                    new[] { "Essence of the Garden", "Garden Ambrosia", "Solunarian Fruit" }, new[] { 1, 1, 2 },
                    "Grand feast. +12 all stats, +4 HP regen for 20 min.", 360, 15f, 5f, true, false),
                new Recipe("Phoenix Feather Souffle", "Baking", 155, 178,
                    new[] { "Phoenix Feather", "Dawn Nectar", "Golden Wheat" }, new[] { 2, 1, 3 },
                    "Reborn food. +14 End, +12 all stats for 22 min.", 390, 15f, 5f, true, true),
                new Recipe("Moonsilver-Glazed Ham", "Baking", 158, 178,
                    new[] { "Beast Meat", "Moonsilver Bar", "Firebloom Pepper" }, new[] { 3, 1, 1 },
                    "Silver meat. +13 Str, +13 End for 22 min.", 410, 15f, 5f, true, false),
                new Recipe("Starcrust Pie", "Baking", 162, 178,
                    new[] { "Starmetal Fragment", "Golden Wheat", "Essence of the Garden" }, new[] { 1, 3, 1 },
                    "Cosmic pie. +12 all stats, +3% haste for 20 min.", 440, 15f, 5f, true, false),
                new Recipe("Eternity's Harvest Feast", "Baking", 165, 178,
                    new[] { "Crystallized Time", "Garden Ambrosia", "Dawn Nectar" }, new[] { 1, 1, 1 },
                    "Timeless meal. +14 all stats, +5 HP regen for 25 min.", 470, 15f, 5f, true, true),

            };
        }

        // ── BREWING ─────────────────────────────────────────────────

        private static List<Recipe> BrewingRecipes()
        {
            return new List<Recipe>
            {
                new Recipe("Bog Juice", "Brewing", 1, 12,
                    new[] { "Swamp Root", "Pod of Water" }, new[] { 1, 2 },
                    "Murky but drinkable. Restores small mana over time.", 2),
                new Recipe("Faerie Wine", "Brewing", 5, 20,
                    new[] { "Wild Berries", "Faerie Dust", "Pod of Water" }, new[] { 2, 1, 1 },
                    "Shimmering drink. +3 Intelligence for 5 minutes.", 8),
                new Recipe("Duskbloom Tea", "Brewing", 10, 28,
                    new[] { "Duskbloom Petal", "Pod of Water" }, new[] { 2, 1 },
                    "Calming tea. +4 Wisdom, faster mana regen for 5 min.", 12),
                new Recipe("Braxonian Cactus Ale", "Brewing", 15, 32,
                    new[] { "Desert Cactus Fruit", "Pod of Water", "Handful of Nuts" }, new[] { 2, 2, 1 },
                    "Potent desert brew. +4 Endurance, +3 Strength for 5 min.", 18),
                new Recipe("Blightvein Elixir", "Brewing", 25, 40,
                    new[] { "Blightvein Moss", "Nightshade Berry", "Pod of Water" }, new[] { 1, 1, 2 },
                    "Dark potion. +6 Intelligence, +5 all Resists for 10 min.", 35),
                new Recipe("Firesage Draught", "Brewing", 35, 50,
                    new[] { "Firesage Bundle", "Ashbloom Herb", "Pod of Water" }, new[] { 1, 1, 3 },
                    "Volcanic brew. +8 Strength, +15 Fire Resist for 10 min.", 60),
                new Recipe("Elixir of Vitality", "Brewing", 45, 50,
                    new[] { "Shimmer Herb", "Essence of the Garden", "Dawn Nectar" }, new[] { 1, 1, 1 },
                    "Legendary elixir. +10 all stats for 15 minutes.", 120),
                new Recipe("Silver Moon Tonic", "Brewing", 50, 70,
                    new[] { "Chunk of Silver Ore", "Duskbloom Petal", "Pod of Water" }, new[] { 1, 2, 2 },
                    "Shimmering tonic. +7 Wis, +4 MP regen for 10 min.", 30),
                new Recipe("Drake Blood Ale", "Brewing", 65, 85,
                    new[] { "Beast Bone Shard", "Wild Berries", "Pod of Water" }, new[] { 1, 3, 2 },
                    "Crimson ale. +8 Str, +5 End for 15 min.", 50),
                new Recipe("Frostfire Flask", "Brewing", 80, 100,
                    new[] { "Frostweave Strand", "Firebloom Pepper", "Pod of Water" }, new[] { 1, 1, 3 },
                    "Burns cold. +10 Fire Resist, +10 Cold Resist for 15 min.", 75),
                new Recipe("Voidwalker's Draught", "Brewing", 95, 115,
                    new[] { "Breath of the Abyss", "Nightshade Berry", "Pod of Water" }, new[] { 1, 2, 2 },
                    "Dark potion. +10 Int, +8 all Resists for 15 min.", 100),
                new Recipe("Phoenix Tears", "Brewing", 110, 130,
                    new[] { "Phoenix Feather", "Moonvine Root", "Pod of Water" }, new[] { 1, 1, 2 },
                    "Liquid fire. Full HP restore, +12 Fire Resist for 20 min.", 150),
                new Recipe("Essence of Starmetal", "Brewing", 125, 145,
                    new[] { "Starmetal Fragment", "Shimmer Herb", "Pod of Water" }, new[] { 1, 1, 3 },
                    "Cosmic elixir. +12 all stats for 15 min.", 200),
                new Recipe("Titan's Blood Mead", "Brewing", 140, 160,
                    new[] { "Titan's Knucklebone", "Garden Ambrosia", "Pod of Water" }, new[] { 1, 1, 3 },
                    "Enormous power. +15 Str, +15 End for 20 min.", 280),
                new Recipe("Draught of Eternity", "Brewing", 160, 180,
                    new[] { "Crystallized Time", "Essence of the Garden", "Pod of Water" }, new[] { 1, 1, 3 },
                    "Time stands still. +14 all stats, +5% haste for 25 min.", 400),
                new Recipe("Elixir of the Cosmos", "Brewing", 175, 180,
                    new[] { "Thread of Fate", "Essence of Eternity", "Dawn Nectar" }, new[] { 1, 1, 1 },
                    "Universe in a bottle. +16 all stats, +8 HP/MP regen for 25 min.", 500),
                // ── Drop-only recipes (from enemy kills) ──
                new Recipe("Swamp Rot Grog", "Brewing", 10, 25,
                    new[] { "Swamp Root", "Pod of Water", "Wild Berries" }, new[] { 1, 2, 1 },
                    "Foul grog. +2 End, +1 Poison Resist for 5 min.", 5, 15f, 5f, true, false),
                new Recipe("Goblin Firewater", "Brewing", 18, 35,
                    new[] { "Firebloom Pepper", "Pod of Water" }, new[] { 2, 2 },
                    "Burns going down. +3 Str, +3 Fire Resist for 5 min.", 12, 15f, 5f, true, false),
                new Recipe("Fernallan Moonshine", "Brewing", 25, 42,
                    new[] { "Enchanted Root", "Pod of Water", "Faerie Dust" }, new[] { 1, 2, 1 },
                    "Glowing drink. +4 Int, +3 Wis for 8 min.", 20, 15f, 5f, true, true),
                new Recipe("Braxonian Cactus Tequila", "Brewing", 30, 48,
                    new[] { "Desert Cactus Fruit", "Pod of Water" }, new[] { 3, 2 },
                    "Desert spirit. +5 Str, +4 End for 8 min.", 28, 15f, 5f, true, false),
                new Recipe("Duskbloom Brandy", "Brewing", 38, 55,
                    new[] { "Duskbloom Petal", "Pod of Water", "Wild Berries" }, new[] { 2, 2, 2 },
                    "Fragrant brandy. +5 Wis, +4 MP regen for 10 min.", 35, 15f, 5f, true, false),
                new Recipe("Firesage Stout", "Brewing", 42, 60,
                    new[] { "Firesage Bundle", "Pod of Water", "Charred Root" }, new[] { 1, 3, 1 },
                    "Volcanic beer. +6 Str, +6 Fire Resist for 10 min.", 42, 15f, 5f, true, true),
                new Recipe("Blight Whiskey", "Brewing", 48, 68,
                    new[] { "Blightvein Moss", "Pod of Water", "Nightshade Berry" }, new[] { 1, 3, 1 },
                    "Dark liquor. +7 Int, +5 Poison Resist for 10 min.", 50, 15f, 5f, true, false),
                new Recipe("Solunarian Cordial", "Brewing", 55, 75,
                    new[] { "Solunarian Fruit", "Pod of Water", "Dawn Blossom" }, new[] { 1, 2, 1 },
                    "Golden drink. +6 all stats for 10 min.", 60, 15f, 5f, true, false),
                new Recipe("Beastblood Wine", "Brewing", 60, 82,
                    new[] { "Beast Bone Shard", "Wild Berries", "Pod of Water" }, new[] { 1, 3, 2 },
                    "Crimson wine. +8 Str, +6 End for 12 min.", 68, 15f, 5f, true, true),
                new Recipe("Moonpetal Mead", "Brewing", 68, 88,
                    new[] { "Dawn Blossom", "Honey Comb Fragment", "Pod of Water" }, new[] { 2, 1, 3 },
                    "Sweet mead. +8 Wis, +6 Int for 12 min.", 80, 15f, 5f, true, false),
                new Recipe("Ashbloom Sake", "Brewing", 72, 95,
                    new[] { "Ashbloom Herb", "Pod of Water", "Golden Wheat" }, new[] { 2, 3, 1 },
                    "Volcanic sake. +9 Str, +8 Fire Resist for 12 min.", 90, 15f, 5f, true, false),
                new Recipe("Essence of Shadows", "Brewing", 78, 100,
                    new[] { "Breath of the Abyss", "Pod of Water", "Duskbloom Petal" }, new[] { 1, 3, 1 },
                    "Dark potion. +10 Int, +8 all Resists for 12 min.", 105, 15f, 5f, true, true),
                new Recipe("Starlight Lager", "Brewing", 85, 108,
                    new[] { "Starmetal Fragment", "Pod of Water", "Wild Berries" }, new[] { 1, 3, 2 },
                    "Sparkling beer. +8 all stats for 12 min.", 115, 15f, 5f, true, false),
                new Recipe("Phoenix Down Tonic", "Brewing", 90, 115,
                    new[] { "Phoenix Feather", "Pod of Water", "Healing Moss" }, new[] { 1, 3, 2 },
                    "Revival tonic. +10 End, +8 HP regen for 15 min.", 130, 15f, 5f, true, false),
                new Recipe("Titan's Grog", "Brewing", 95, 120,
                    new[] { "Titan's Knucklebone", "Pod of Water", "Firebloom Pepper" }, new[] { 1, 3, 1 },
                    "Enormous drink. +12 Str, +10 End for 15 min.", 145, 15f, 5f, true, true),
                new Recipe("Voidtouched Absinthe", "Brewing", 100, 125,
                    new[] { "Nightshade Berry", "Breath of the Abyss", "Pod of Water" }, new[] { 2, 1, 3 },
                    "Reality-bending. +10 Int, +10 Wis for 15 min.", 155, 15f, 5f, true, false),
                new Recipe("Sunfire Rum", "Brewing", 108, 132,
                    new[] { "Sunforged Ingot", "Pod of Water", "Wild Berries" }, new[] { 1, 3, 2 },
                    "Solar rum. +11 Str, +10 Fire Resist for 15 min.", 170, 15f, 5f, true, false),
                new Recipe("Moonsilver Elixir", "Brewing", 112, 138,
                    new[] { "Moonsilver Bar", "Pod of Water", "Dawn Nectar" }, new[] { 1, 3, 1 },
                    "Silver potion. +10 all stats for 15 min.", 185, 15f, 5f, true, true),
                new Recipe("Worldtree Sap Wine", "Brewing", 118, 142,
                    new[] { "Worldtree Splinter", "Pod of Water", "Wild Berries" }, new[] { 1, 3, 3 },
                    "Ancient wine. +12 Wis, +10 Int, +5 MP regen for 18 min.", 200, 15f, 5f, true, false),
                new Recipe("Dragonfire Bourbon", "Brewing", 122, 148,
                    new[] { "Elder Wyrm Bone", "Firebloom Pepper", "Pod of Water" }, new[] { 1, 2, 3 },
                    "Fiery bourbon. +12 Str, +12 Fire Resist for 18 min.", 220, 15f, 5f, true, false),
                new Recipe("Soulsteel Infusion", "Brewing", 128, 152,
                    new[] { "Soulsteel Billet", "Pod of Water", "Shimmer Herb" }, new[] { 1, 3, 1 },
                    "Spirit potion. +12 all stats for 15 min.", 240, 15f, 5f, true, true),
                new Recipe("Celestial Champagne", "Brewing", 132, 158,
                    new[] { "Moonvine Root", "Dawn Nectar", "Pod of Water" }, new[] { 1, 1, 3 },
                    "Heavenly bubbly. +11 all stats, +3 HP regen for 18 min.", 260, 15f, 5f, true, false),
                new Recipe("Voidwalker's Reserve", "Brewing", 138, 162,
                    new[] { "Breath of the Abyss", "Nightshade Berry", "Pod of Water" }, new[] { 2, 2, 3 },
                    "Aged darkness. +14 Int, +12 all Resists for 18 min.", 285, 15f, 5f, true, false),
                new Recipe("Starmetal Spirits", "Brewing", 142, 168,
                    new[] { "Starmetal Fragment", "Pod of Water", "Dawn Nectar" }, new[] { 2, 3, 1 },
                    "Cosmic drink. +12 all stats, +3% haste for 18 min.", 310, 15f, 5f, true, true),
                new Recipe("Phoenix Ember Mead", "Brewing", 148, 172,
                    new[] { "Phoenix Feather", "Honey Comb Fragment", "Pod of Water" }, new[] { 2, 1, 3 },
                    "Burning mead. +14 Str, +14 End for 20 min.", 335, 15f, 5f, true, false),
                new Recipe("Titan's Reserve Vintage", "Brewing", 152, 175,
                    new[] { "Titan's Knucklebone", "Dawn Nectar", "Pod of Water" }, new[] { 1, 1, 3 },
                    "Aged giant wine. +14 all stats for 20 min.", 360, 15f, 5f, true, false),
                new Recipe("Eternity's Vintage", "Brewing", 155, 178,
                    new[] { "Crystallized Time", "Dawn Nectar", "Pod of Water" }, new[] { 1, 1, 3 },
                    "Ageless wine. +14 all stats, +5 HP regen for 22 min.", 390, 15f, 5f, true, true),
                new Recipe("Voidheart Cordial", "Brewing", 158, 178,
                    new[] { "Breath of the Abyss", "Essence of the Garden", "Pod of Water" }, new[] { 1, 1, 3 },
                    "Void elixir. +13 all stats, +10 all Resists for 22 min.", 410, 15f, 5f, true, false),
                new Recipe("Sunfire Phoenix Lager", "Brewing", 162, 178,
                    new[] { "Sunforged Ingot", "Phoenix Feather", "Pod of Water" }, new[] { 1, 1, 3 },
                    "Solar-fire beer. +15 Str, +15 End, +10 Fire Resist for 22 min.", 440, 15f, 5f, true, false),
                new Recipe("Worldtree Ambrosia", "Brewing", 165, 178,
                    new[] { "Worldtree Splinter", "Essence of the Garden", "Dawn Nectar" }, new[] { 1, 1, 1 },
                    "World drink. +14 all stats, +4% haste for 22 min.", 470, 15f, 5f, true, true),

            };
        }

        // ── FLETCHING ───────────────────────────────────────────────

        private static List<Recipe> FletchingRecipes()
        {
            return new List<Recipe>
            {
                new Recipe("Crude Shortbow", "Fletching", 1, 15,
                    new[] { "Dark Bark Strip", "Tangled String" }, new[] { 2, 2 },
                    "A simple bow. Better than nothing.", 5),
                new Recipe("Strung Hunting Bow", "Fletching", 5, 20,
                    new[] { "Dark Bark Strip", "Tangled String", "Eagle Feather" }, new[] { 3, 1, 1 },
                    "A sturdy bow with decent pull. +1 Dex.", 12),
                new Recipe("Hunting Shortbow", "Fletching", 12, 30,
                    new[] { "Dark Bark Strip", "Spider Silk Strand", "Bramble Thorn" }, new[] { 3, 2, 1 },
                    "A compact bow. Decent damage, fast draw speed.", 20),
                new Recipe("Composite Longbow", "Fletching", 25, 40,
                    new[] { "Dark Bark Strip", "Spider Silk Strand", "Tough Hide Strip" }, new[] { 4, 3, 1 },
                    "Powerful ranged weapon. High damage, medium speed.", 50),
                new Recipe("Azynthi Recurve", "Fletching", 45, 50,
                    new[] { "Enchanted Root", "Shimmer Herb", "Spider Silk Strand" }, new[] { 2, 1, 4 },
                    "Living wood bow. Top-tier ranged weapon with proc chance.", 130),
                new Recipe("Petrified Bow Stave", "Fletching", 60, 80,
                    new[] { "Petrified Heartwood", "Tough Hide Strip" }, new[] { 2, 1 },
                    "Ancient bow stave. Used in high-tier bows.", 40),
                new Recipe("Wyrm Bone Bow", "Fletching", 70, 90,
                    new[] { "Beast Bone Shard", "Petrified Bow Stave", "Steel Chain Links" }, new[] { 2, 1, 2 },
                    "Heavy bow. High damage, slow speed.", 80),
                new Recipe("Worldtree Bow Limb", "Fletching", 100, 120,
                    new[] { "Worldtree Splinter", "Abyssal Silk Thread" }, new[] { 2, 2 },
                    "Legendary bow wood. Used in endgame bows.", 80),
                new Recipe("Titan's Longbow", "Fletching", 140, 160,
                    new[] { "Worldtree Bow Limb", "Titan's Knucklebone", "Soulsteel Chain Links" }, new[] { 2, 1, 1 },
                    "Massive bow. +12 Dex, +8 Str.", 250),
                new Recipe("Bow of the Cosmos", "Fletching", 160, 180,
                    new[] { "Worldtree Bow Limb", "Crystallized Time", "Thread of Fate" }, new[] { 2, 1, 1 },
                    "Arrows warp reality. +14 Dex, +10 Str, +3% haste.", 450),
                // ── Drop-only recipes (from enemy kills) ──
                new Recipe("Goblin Shortbow", "Fletching", 10, 25,
                    new[] { "Dark Bark Strip", "Tangled String", "Eagle Feather" }, new[] { 2, 2, 1 },
                    "Crude bow. +2 Dex.", 8, 15f, 5f, true, false),
                new Recipe("Fernallan Longbow", "Fletching", 25, 42,
                    new[] { "Dark Bark Strip", "Enchanted Root", "Spider Silk Strand" }, new[] { 2, 1, 2 },
                    "Druid bow. +4 Dex, +2 Wis.", 22, 15f, 5f, true, true),
                new Recipe("Windrunner Bow", "Fletching", 38, 55,
                    new[] { "Dark Bark Strip", "Windwashed Crystal", "Spider Silk Strand" }, new[] { 3, 1, 3 },
                    "Fast bow. +5 Dex, +3 Agi.", 38, 15f, 5f, true, false),
                new Recipe("Boneframe Recurve", "Fletching", 48, 68,
                    new[] { "Beast Bone Shard", "Spider Silk Strand", "Dark Bark Strip" }, new[] { 2, 3, 2 },
                    "Bone bow. +7 Dex, +5 Str.", 55, 15f, 5f, true, false),
                new Recipe("Runewood Longbow", "Fletching", 60, 82,
                    new[] { "Runestone Shard", "Spider Silk Strand", "Silver Wire" }, new[] { 2, 4, 1 },
                    "Ghost bow. +7 Dex, +5 Int.", 75, 15f, 5f, true, true),
                new Recipe("Ironbark War Bow", "Fletching", 68, 88,
                    new[] { "Ironbark Plank", "Spider Silk Strand", "Steel Chain Links" }, new[] { 2, 3, 1 },
                    "Heavy bow. +8 Dex, +6 Str.", 85, 15f, 5f, true, false),
                new Recipe("Nightstalker Bow", "Fletching", 78, 100,
                    new[] { "Void Iron Chunk", "Ironbark Plank", "Spider Silk Strand" }, new[] { 1, 2, 3 },
                    "Shadow bow. +10 Dex, +8 Agi.", 110, 15f, 5f, true, true),
                new Recipe("Sunfire Longbow", "Fletching", 90, 115,
                    new[] { "Sunforged Ingot", "Ironbark Plank", "Spider Silk Strand" }, new[] { 1, 2, 3 },
                    "Solar bow. +10 Dex, +8 Fire Resist.", 135, 15f, 5f, true, false),
                new Recipe("Wyrm Sinew Composite", "Fletching", 95, 120,
                    new[] { "Elder Wyrm Bone", "Tough Hide Strip", "Spider Silk Strand" }, new[] { 1, 2, 4 },
                    "Flexible bow. +10 Dex, +8 Str, +4 Agi.", 155, 15f, 5f, true, true),
                new Recipe("Phoenix Recurve", "Fletching", 108, 132,
                    new[] { "Phoenix Feather", "Ironbark Plank", "Soulsteel Chain Links" }, new[] { 1, 2, 1 },
                    "Fire bow. +11 Dex, +10 Fire Resist.", 170, 15f, 5f, true, false),
                new Recipe("Starmetal Bow", "Fletching", 112, 138,
                    new[] { "Starmetal Fragment", "Ironbark Plank", "Soulsteel Chain Links" }, new[] { 1, 2, 1 },
                    "Cosmic bow. +12 Dex, +8 Str.", 190, 15f, 5f, true, true),
                new Recipe("Titan's Warbow", "Fletching", 122, 148,
                    new[] { "Titan's Knucklebone", "Worldtree Splinter", "Soulsteel Chain Links" }, new[] { 1, 1, 2 },
                    "Massive bow. +13 Dex, +10 Str.", 225, 15f, 5f, true, false),
                new Recipe("Dragonlord's Greatbow", "Fletching", 128, 152,
                    new[] { "Elder Wyrm Bone", "Worldtree Splinter", "Starmetal Fragment" }, new[] { 1, 1, 1 },
                    "Dragon bow. +14 Dex, +10 Str, +8 Fire Resist.", 250, 15f, 5f, true, true),
                new Recipe("Voidweave Longbow", "Fletching", 138, 162,
                    new[] { "Abyssal Silk Thread", "Worldtree Splinter", "Void Iron Chunk" }, new[] { 2, 1, 1 },
                    "Dark bow. +14 Dex, +12 all Resists.", 295, 15f, 5f, true, false),
                new Recipe("Soulfire Bow", "Fletching", 142, 168,
                    new[] { "Soulsteel Billet", "Sunforged Ingot", "Worldtree Splinter" }, new[] { 1, 1, 1 },
                    "Spirit bow. +13 Dex, +11 Str, +3% haste.", 320, 15f, 5f, true, true),
                new Recipe("Worldtree Warbow", "Fletching", 152, 175,
                    new[] { "Worldtree Splinter", "Soulsteel Chain Links", "Moonsilver Bar" }, new[] { 2, 2, 1 },
                    "Ancient bow. +14 Dex, +12 Str.", 380, 15f, 5f, true, false),
                new Recipe("Phoenix Flight Bow", "Fletching", 155, 178,
                    new[] { "Phoenix Feather", "Worldtree Splinter", "Starmetal Fragment" }, new[] { 2, 1, 1 },
                    "Fire bow. +15 Dex, +12 Str, +10 Fire Resist.", 410, 15f, 5f, true, true),
                new Recipe("Void Hunter's Bow", "Fletching", 158, 178,
                    new[] { "Breath of the Abyss", "Worldtree Splinter", "Soulsteel Billet" }, new[] { 1, 1, 1 },
                    "Dark bow. +14 Dex, +14 Agi.", 430, 15f, 5f, true, false),
                new Recipe("Titan Slayer's Longbow", "Fletching", 165, 178,
                    new[] { "Worldtree Splinter", "Crystallized Time", "Soulsteel Chain Links" }, new[] { 2, 1, 2 },
                    "Giant killer. +14 Dex, +12 Str, +4% haste.", 480, 15f, 5f, true, true),

            };
        }

        // ── JEWELCRAFT ──────────────────────────────────────────────

        private static List<Recipe> JewelcraftRecipes()
        {
            return new List<Recipe>
            {
                new Recipe("Polished Stone Band", "Jewelcraft", 1, 15,
                    new[] { "Smooth Pebble", "Copper Rivets" }, new[] { 2, 1 },
                    "Simple ring. +1 Endurance.", 4),
                new Recipe("Amber Pendant", "Jewelcraft", 8, 22,
                    new[] { "Glimmering Geode Fragment", "Tangled String" }, new[] { 1, 1 },
                    "Warm necklace. +2 Intelligence.", 12),
                new Recipe("Moonstone Ring", "Jewelcraft", 15, 30,
                    new[] { "Moonstone Pebble", "Copper Rivets" }, new[] { 1, 1 },
                    "Luminous ring. +3 Wisdom, +1 MP regen.", 25),
                new Recipe("Brine Pearl Earring", "Jewelcraft", 20, 35,
                    new[] { "Brine-Crusted Pearl", "Copper Rivets", "Tangled String" }, new[] { 1, 1, 1 },
                    "Ocean's gift. +4 Charisma, +2 all Resists.", 35),
                new Recipe("Bloodstone Signet", "Jewelcraft", 28, 42,
                    new[] { "Bloodstone Chip", "Mithril Wire", "Corruption Crystal" }, new[] { 1, 2, 1 },
                    "Dark ring. +5 Strength, +3 Agility.", 55),
                new Recipe("Moonvine Choker", "Jewelcraft", 35, 50,
                    new[] { "Moonvine Root", "Solunarian Flower", "Mithril Wire" }, new[] { 1, 1, 2 },
                    "Radiant necklace. +6 Wisdom, +6 Intelligence.", 80),
                new Recipe("Shimmerweave Crown", "Jewelcraft", 45, 50,
                    new[] { "Shimmer Herb", "Essence of the Garden", "Mithril Wire" }, new[] { 1, 1, 3 },
                    "Legendary headpiece. +5 all stats, +5 all Resists.", 150),
                new Recipe("Ruby Studded Band", "Jewelcraft", 50, 70,
                    new[] { "Rough Ruby", "Gold Setting", "Silver Wire" }, new[] { 1, 1, 1 },
                    "Crimson ring. +6 Str, +3 Fire Resist.", 40),
                new Recipe("Sapphire Pendant", "Jewelcraft", 60, 80,
                    new[] { "Rough Sapphire", "Gold Setting", "Mithril Wire" }, new[] { 1, 1, 1 },
                    "Ocean-blue necklace. +7 Int, +4 Cold Resist.", 55),
                new Recipe("Emerald Focus Stone", "Jewelcraft", 70, 90,
                    new[] { "Rough Emerald", "Platinum Filigree", "Faerie Dust" }, new[] { 1, 1, 2 },
                    "Green charm. +8 Wis, +5 MP regen.", 70),
                new Recipe("Diamond Encrusted Circlet", "Jewelcraft", 85, 105,
                    new[] { "Flawless Diamond", "Platinum Filigree", "Gold Setting" }, new[] { 1, 1, 1 },
                    "Brilliant headpiece. +8 Cha, +5 all Resists.", 100),
                new Recipe("Abyssal Opal Talisman", "Jewelcraft", 100, 120,
                    new[] { "Abyssal Opal", "Soulsteel Chain Links", "Void Iron Chunk" }, new[] { 1, 1, 1 },
                    "Dark talisman. +10 Int, +8 Magic Resist.", 130),
                new Recipe("Starshard Earring", "Jewelcraft", 115, 135,
                    new[] { "Starshard Crystal", "Platinum Filigree", "Moonsilver Bar" }, new[] { 1, 1, 1 },
                    "Gleaming earring. +10 Wis, +6 all stats.", 170),
                new Recipe("Phoenix Heart Amulet", "Jewelcraft", 130, 150,
                    new[] { "Phoenix Feather", "Flawless Diamond", "Sunforged Ingot" }, new[] { 1, 1, 1 },
                    "Burning amulet. +12 End, +10 Fire Resist, HP regen.", 220),
                new Recipe("Titan's Signet Ring", "Jewelcraft", 145, 165,
                    new[] { "Titan's Knucklebone", "Prismatic Jewel", "Soulsteel Billet" }, new[] { 1, 1, 1 },
                    "Massive ring. +12 Str, +12 End.", 300),
                new Recipe("Crown of the Void", "Jewelcraft", 160, 180,
                    new[] { "Prismatic Jewel", "Breath of the Abyss", "Crystallized Time" }, new[] { 1, 1, 1 },
                    "Dark crown. +14 Int, +10 Wis, +8 all Resists.", 400),
                new Recipe("Amulet of Eternity", "Jewelcraft", 175, 180,
                    new[] { "Thread of Fate", "Starshard Crystal", "Essence of the Garden" }, new[] { 1, 1, 1 },
                    "Defies time. +12 all stats, +5% haste.", 500),
                // ── Drop-only recipes (from enemy kills) ──
                new Recipe("Goblin Tooth Necklace", "Jewelcraft", 10, 25,
                    new[] { "Smooth Pebble", "Tangled String" }, new[] { 2, 1 },
                    "Crude necklace. +2 Str.", 5, 15f, 5f, true, false),
                new Recipe("Fernallan Leaf Brooch", "Jewelcraft", 18, 35,
                    new[] { "Enchanted Root", "Copper Rivets", "Tangled String" }, new[] { 1, 1, 1 },
                    "Nature brooch. +3 Wis, +2 Int.", 12, 15f, 5f, true, false),
                new Recipe("Braxonian Sand Ring", "Jewelcraft", 25, 42,
                    new[] { "Desert Cactus Fruit", "Copper Rivets", "Smooth Pebble" }, new[] { 1, 1, 2 },
                    "Desert ring. +4 End, +3 Str.", 20, 15f, 5f, true, true),
                new Recipe("Blightstone Pendant", "Jewelcraft", 30, 48,
                    new[] { "Blightvein Moss", "Mithril Wire", "Corruption Crystal" }, new[] { 1, 1, 1 },
                    "Dark pendant. +5 Int, +4 Poison Resist.", 28, 15f, 5f, true, false),
                new Recipe("Moonpetal Circlet", "Jewelcraft", 38, 55,
                    new[] { "Dawn Blossom", "Mithril Wire", "Silver Wire" }, new[] { 1, 2, 1 },
                    "Lunar headpiece. +5 Wis, +4 Int.", 35, 15f, 5f, true, false),
                new Recipe("Scale-Etched Amulet", "Jewelcraft", 42, 60,
                    new[] { "Malaroth Scale Fragment", "Silver Wire", "Mithril Wire" }, new[] { 1, 1, 1 },
                    "Dragon necklace. +6 End, +5 Fire Resist.", 42, 15f, 5f, true, true),
                new Recipe("Starlight Band", "Jewelcraft", 48, 68,
                    new[] { "Windwashed Crystal", "Silver Wire", "Mithril Wire" }, new[] { 1, 1, 1 },
                    "Sparkling ring. +7 Cha, +4 all Resists.", 50, 15f, 5f, true, false),
                new Recipe("Deep Pearl Ring", "Jewelcraft", 55, 75,
                    new[] { "Brine-Crusted Pearl", "Gold Setting", "Void Iron Chunk" }, new[] { 1, 1, 1 },
                    "Dark pearl ring. +7 Int, +6 Wis.", 60, 15f, 5f, true, false),
                new Recipe("Emberheart Pendant", "Jewelcraft", 60, 82,
                    new[] { "Phoenix Feather", "Gold Setting", "Platinum Filigree" }, new[] { 1, 1, 1 },
                    "Fire necklace. +8 End, +8 Fire Resist.", 68, 15f, 5f, true, true),
                new Recipe("Titan's Bone Ring", "Jewelcraft", 68, 88,
                    new[] { "Elder Wyrm Bone", "Gold Setting", "Adamantite Rivets" }, new[] { 1, 1, 1 },
                    "Heavy ring. +8 Str, +7 End.", 80, 15f, 5f, true, false),
                new Recipe("Celestial Moonband", "Jewelcraft", 72, 95,
                    new[] { "Moonsilver Bar", "Platinum Filigree", "Faerie Dust" }, new[] { 1, 1, 2 },
                    "Lunar ring. +8 Wis, +7 Int.", 90, 15f, 5f, true, false),
                new Recipe("Voidstone Choker", "Jewelcraft", 78, 100,
                    new[] { "Void Iron Chunk", "Platinum Filigree", "Abyssal Opal" }, new[] { 1, 1, 1 },
                    "Dark choker. +10 Int, +8 Magic Resist.", 105, 15f, 5f, true, true),
                new Recipe("Sunfire Signet", "Jewelcraft", 85, 108,
                    new[] { "Sunforged Ingot", "Platinum Filigree", "Rough Ruby" }, new[] { 1, 1, 1 },
                    "Solar ring. +9 Str, +9 Fire Resist.", 115, 15f, 5f, true, false),
                new Recipe("Starweave Tiara", "Jewelcraft", 90, 115,
                    new[] { "Starmetal Fragment", "Platinum Filigree", "Faerie Dust" }, new[] { 1, 1, 3 },
                    "Star headpiece. +8 all stats.", 130, 15f, 5f, true, false),
                new Recipe("Dragonheart Pendant", "Jewelcraft", 95, 120,
                    new[] { "Elder Wyrm Bone", "Flawless Diamond", "Gold Setting" }, new[] { 1, 1, 1 },
                    "Dragon necklace. +10 End, +10 Str.", 145, 15f, 5f, true, true),
                new Recipe("Soulbound Ring", "Jewelcraft", 100, 125,
                    new[] { "Soulsteel Billet", "Platinum Filigree", "Rough Emerald" }, new[] { 1, 1, 1 },
                    "Spirit ring. +10 Wis, +8 MP regen.", 155, 15f, 5f, true, false),
                new Recipe("Eclipse Earring", "Jewelcraft", 108, 132,
                    new[] { "Moonsilver Bar", "Void Iron Chunk", "Platinum Filigree" }, new[] { 1, 1, 1 },
                    "Dark earring. +10 Int, +8 Agi.", 170, 15f, 5f, true, false),
                new Recipe("Phoenix Heart Ring", "Jewelcraft", 112, 138,
                    new[] { "Phoenix Feather", "Flawless Diamond", "Platinum Filigree" }, new[] { 1, 1, 1 },
                    "Fire ring. +11 End, +10 Fire Resist.", 185, 15f, 5f, true, true),
                new Recipe("Worldtree Amulet", "Jewelcraft", 118, 142,
                    new[] { "Worldtree Splinter", "Platinum Filigree", "Rough Emerald" }, new[] { 1, 1, 1 },
                    "Ancient necklace. +12 Wis, +10 Int.", 200, 15f, 5f, true, false),
                new Recipe("Titan's Eye Circlet", "Jewelcraft", 122, 148,
                    new[] { "Titan's Knucklebone", "Flawless Diamond", "Platinum Filigree" }, new[] { 1, 1, 1 },
                    "Giant headpiece. +12 End, +10 Str.", 220, 15f, 5f, true, false),
                new Recipe("Voidstar Pendant", "Jewelcraft", 128, 152,
                    new[] { "Breath of the Abyss", "Starshard Crystal", "Platinum Filigree" }, new[] { 1, 1, 1 },
                    "Dark necklace. +13 Int, +12 all Resists.", 240, 15f, 5f, true, true),
                new Recipe("Sunmetal Band", "Jewelcraft", 132, 158,
                    new[] { "Sunforged Ingot", "Starshard Crystal", "Platinum Filigree" }, new[] { 1, 1, 1 },
                    "Solar ring. +11 Str, +11 Dex.", 260, 15f, 5f, true, false),
                new Recipe("Dragonlord's Signet", "Jewelcraft", 138, 162,
                    new[] { "Elder Wyrm Bone", "Prismatic Jewel", "Soulsteel Billet" }, new[] { 1, 1, 1 },
                    "Dragon ring. +12 Str, +12 End.", 285, 15f, 5f, true, false),
                new Recipe("Starweave Crown", "Jewelcraft", 142, 168,
                    new[] { "Starmetal Fragment", "Prismatic Jewel", "Crystallized Time" }, new[] { 1, 1, 1 },
                    "Star crown. +12 all stats.", 310, 15f, 5f, true, true),
                new Recipe("Soulfire Earring", "Jewelcraft", 148, 172,
                    new[] { "Soulsteel Billet", "Phoenix Feather", "Starshard Crystal" }, new[] { 1, 1, 1 },
                    "Spirit earring. +13 Wis, +12 Int.", 335, 15f, 5f, true, false),
                new Recipe("Void Eclipse Ring", "Jewelcraft", 152, 175,
                    new[] { "Void Iron Chunk", "Crystallized Time", "Prismatic Jewel" }, new[] { 1, 1, 1 },
                    "Dark ring. +13 Int, +12 Wis.", 360, 15f, 5f, true, false),
                new Recipe("Phoenix Crown", "Jewelcraft", 155, 178,
                    new[] { "Phoenix Feather", "Prismatic Jewel", "Sunforged Ingot" }, new[] { 1, 1, 1 },
                    "Fire crown. +14 End, +12 all stats.", 390, 15f, 5f, true, true),
                new Recipe("Titan's Sigil Choker", "Jewelcraft", 158, 178,
                    new[] { "Titan's Knucklebone", "Prismatic Jewel", "Soulsteel Chain Links" }, new[] { 1, 1, 1 },
                    "Giant necklace. +14 Str, +14 End.", 410, 15f, 5f, true, false),
                new Recipe("Starfire Band", "Jewelcraft", 162, 178,
                    new[] { "Starmetal Fragment", "Phoenix Feather", "Prismatic Jewel" }, new[] { 1, 1, 1 },
                    "Cosmic ring. +12 all stats, +3% haste.", 440, 15f, 5f, true, false),
                new Recipe("Eternity's Diadem", "Jewelcraft", 165, 178,
                    new[] { "Crystallized Time", "Prismatic Jewel", "Essence of the Garden" }, new[] { 1, 1, 1 },
                    "Timeless crown. +13 all stats, +4% haste.", 470, 15f, 5f, true, true),

            };
        }

        // ── TAILORING ───────────────────────────────────────────────

        private static List<Recipe> TailoringRecipes()
        {
            return new List<Recipe>
            {
                new Recipe("Patchwork Bandages", "Tailoring", 1, 12,
                    new[] { "Worn Leather Scrap", "Tangled String" }, new[] { 2, 1 },
                    "Cloth bandages. Required to use Bind Wound. One consumed per use.", 3),
                new Recipe("Stitched Leather Gloves", "Tailoring", 1, 15,
                    new[] { "Worn Leather Scrap", "Tangled String" }, new[] { 2, 2 },
                    "Double-stitched leather protects the knuckles without sacrificing grip.", 5),
                new Recipe("Reinforced Linen Knuckles", "Tailoring", 5, 20,
                    new[] { "Worn Leather Scrap", "Spider Silk Strand", "Tangled String" },
                    new[] { 2, 1, 2 },
                    "Level 4 Monk handwraps with stitched leather striking pads.", 85),
                new Recipe("Padded Cloth Cap", "Tailoring", 3, 18,
                    new[] { "Spider Silk Strand", "Tangled String" }, new[] { 2, 1 },
                    "Padded layers of cloth offer modest protection from blows to the head.", 7),
                new Recipe("Silken Pouch", "Tailoring", 5, 18,
                    new[] { "Spider Silk Strand", "Tangled String" }, new[] { 2, 1 },
                    "Right-click to open. 4 item slots.", 8),
                new Recipe("Herbalist's Satchel", "Tailoring", 10, 25,
                    new[] { "Spider Silk Strand", "Worn Leather Scrap", "Copper Rivets" }, new[] { 3, 2, 1 },
                    "Right-click to open. 6 item slots.", 18),
                new Recipe("Simple Backpack", "Tailoring", 25, 40,
                    new[] { "Worn Leather Scrap", "Spider Silk Strand", "Steel Chain Links" }, new[] { 4, 4, 2 },
                    "Right-click to open. 8 item slots.", 35),
                new Recipe("Adventurer's Backpack", "Tailoring", 50, 70,
                    new[] { "Cured Tough Leather", "Spider Silk Strand", "Adamantite Rivets" }, new[] { 3, 6, 2 },
                    "Right-click to open. 10 item slots. Sturdy and spacious.", 80),
                new Recipe("Padded Leather Vest", "Tailoring", 15, 30,
                    new[] { "Worn Leather Scrap", "Steel Chain Links", "Tangled String" }, new[] { 4, 1, 2 },
                    "A vest of padded leather, worn and serviceable.", 25),
                new Recipe("Meadow Silk Robe", "Tailoring", 22, 38,
                    new[] { "Spider Silk Strand", "Silken Grass Blade", "Golden Pollen" }, new[] { 5, 3, 1 },
                    "Woven from the silk of meadow spiders, faintly luminous in moonlight.", 40),
                new Recipe("Blighthide Cloak", "Tailoring", 30, 45,
                    new[] { "Worn Leather Scrap", "Blightvein Moss", "Spider Silk Strand" }, new[] { 4, 2, 3 },
                    "Cured in blightvein oils. The leather repels toxins and shadow alike.", 60),
                new Recipe("Silkstep Bindings", "Tailoring", 30, 48,
                    new[] { "Spider Silk Strand", "Silken Grass Blade", "Copper Rivets" },
                    new[] { 4, 2, 2 },
                    "Level 10 Monk handwraps braided for speed and flexibility.", 600),
                new Recipe("Azynthi Woven Mantle", "Tailoring", 42, 50,
                    new[] { "Spider Silk Strand", "Azynthi Petal", "Shimmer Herb" }, new[] { 6, 2, 1 },
                    "Living fibers from the Azynthi groves, woven by careful hands.", 130),
                new Recipe("Cured Tough Leather", "Tailoring", 45, 65,
                    new[] { "Tough Hide Strip", "Healing Moss", "Tangled String" }, new[] { 2, 2, 2 },
                    "Processed dragon hide. Used in higher crafts.", 35),
                new Recipe("Ironbark Reinforced Vest", "Tailoring", 55, 75,
                    new[] { "Cured Tough Leather", "Ironbark Plank", "Steel Chain Links" }, new[] { 2, 1, 2 },
                    "Reinforced with strips of ironbark over hardened leather.", 50),
                new Recipe("Blighthide Strikers", "Tailoring", 60, 80,
                    new[] { "Cured Tough Leather", "Blightvein Moss", "Steel Chain Links" },
                    new[] { 2, 2, 1 },
                    "Level 16 Monk handwraps with cured thorn-hide guards.", 2200),
                new Recipe("Abyssal Weave Cloth", "Tailoring", 65, 85,
                    new[] { "Abyssal Silk Thread", "Spider Silk Strand", "Kraken Ink Vial" }, new[] { 2, 3, 1 },
                    "Dark magic fabric. Used in higher crafts.", 50),
                new Recipe("Frostweave Cloak", "Tailoring", 80, 100,
                    new[] { "Frostweave Strand", "Abyssal Weave Cloth", "Silver Wire" }, new[] { 2, 1, 1 },
                    "Threads of frostweave shimmer with trapped winter cold.", 80),
                new Recipe("Ironbark Fistguards", "Tailoring", 90, 110,
                    new[] { "Cured Tough Leather", "Ironbark Plank", "Reinforced Buckle" },
                    new[] { 3, 2, 2 },
                    "Level 22 Monk handwraps reinforced with riveted ironbark.", 5200),
                new Recipe("Celestial Silk Bolt", "Tailoring", 95, 115,
                    new[] { "Celestial Fabric Bolt", "Abyssal Silk Thread", "Moonsilver Bar" }, new[] { 2, 2, 1 },
                    "Heavenly fabric. Used in endgame armor.", 70),
                new Recipe("Dragonscale Brigandine", "Tailoring", 110, 130,
                    new[] { "Cured Tough Leather", "Adamantite Plate", "Soulsteel Chain Links" }, new[] { 3, 2, 1 },
                    "Overlapping scales from a fallen drake, each one a shield in miniature.", 180),
                new Recipe("Voidweave Robe", "Tailoring", 125, 145,
                    new[] { "Abyssal Weave Cloth", "Breath of the Abyss", "Ethereal Gossamer" }, new[] { 2, 1, 2 },
                    "Woven from threads that drink the light. Whispers echo in its folds.", 220),
                new Recipe("Frostweave Handwraps", "Tailoring", 125, 145,
                    new[] { "Frostweave Strand", "Abyssal Weave Cloth", "Silver Wire" },
                    new[] { 3, 2, 2 },
                    "Level 28 Monk handwraps carrying a carefully bound frozen edge.", 12500),
                new Recipe("Celestial Battle Mantle", "Tailoring", 140, 160,
                    new[] { "Celestial Silk Bolt", "Phoenix Feather", "Adamantite Rivets" }, new[] { 2, 1, 2 },
                    "A mantle of celestial silk that shimmers like a clear night sky.", 300),
                new Recipe("Titan's War Harness", "Tailoring", 155, 175,
                    new[] { "Celestial Silk Bolt", "Titan's Knucklebone", "Soulsteel Chain Links" }, new[] { 2, 1, 2 },
                    "A titan's battle gear, sized for mortal shoulders. The weight is immense.", 400),
                new Recipe("Celestial Silk Cestus", "Tailoring", 160, 178,
                    new[] { "Celestial Silk Bolt", "Moonsilver Bar", "Soulsteel Chain Links" },
                    new[] { 2, 1, 1 },
                    "Level 36 Monk handwraps with light articulated celestial guards.", 36000),
                new Recipe("Mantle of Eternity", "Tailoring", 170, 180,
                    new[] { "Thread of Fate", "Celestial Silk Bolt", "Crystallized Time" }, new[] { 1, 2, 1 },
                    "Neither thread nor fiber has aged since the day it was woven. Perhaps it never will.", 500),
                // ── Drop-only recipes (from enemy kills) ──
                new Recipe("Goblin Patchwork Vest", "Tailoring", 10, 25,
                    new[] { "Worn Leather Scrap", "Tangled String" }, new[] { 3, 2 },
                    "Crude vest. +2 AC, +1 End.", 5, 15f, 5f, true, false),
                new Recipe("Fernallan Leaf Cloak", "Tailoring", 18, 35,
                    new[] { "Spider Silk Strand", "Enchanted Root", "Tangled String" }, new[] { 3, 1, 1 },
                    "Nature cloak. +3 AC, +3 Agi.", 12, 15f, 5f, true, false),
                new Recipe("Braxonian Sandcloth Robe", "Tailoring", 25, 42,
                    new[] { "Spider Silk Strand", "Desert Cactus Fruit", "Tangled String" }, new[] { 4, 1, 2 },
                    "Desert robe. +4 AC, +4 Int.", 20, 15f, 5f, true, true),
                new Recipe("Blighthide Bracers", "Tailoring", 30, 48,
                    new[] { "Worn Leather Scrap", "Blightvein Moss", "Tangled String" }, new[] { 3, 1, 2 },
                    "Cursed bracers. +4 AC, +5 Poison Resist.", 28, 15f, 5f, true, false),
                new Recipe("Moonweave Sash", "Tailoring", 38, 55,
                    new[] { "Spider Silk Strand", "Dawn Blossom", "Tangled String" }, new[] { 4, 1, 1 },
                    "Lunar belt. +4 AC, +5 Wis.", 35, 15f, 5f, true, false),
                new Recipe("Beasthide Boots", "Tailoring", 42, 60,
                    new[] { "Tough Hide Strip", "Worn Leather Scrap", "Steel Chain Links" }, new[] { 2, 2, 1 },
                    "Dragon boots. +6 AC, +5 Agi.", 42, 15f, 5f, true, true),
                new Recipe("Duskweave Cowl", "Tailoring", 48, 68,
                    new[] { "Spider Silk Strand", "Duskbloom Petal", "Tangled String" }, new[] { 5, 2, 1 },
                    "Dark hood. +5 AC, +7 Int.", 50, 15f, 5f, true, false),
                new Recipe("Ironhide Gauntlets", "Tailoring", 55, 75,
                    new[] { "Tough Hide Strip", "Steel Chain Links", "Copper Rivets" }, new[] { 2, 2, 2 },
                    "Scale gloves. +7 AC, +5 Str.", 60, 15f, 5f, true, false),
                new Recipe("Runewoven Silk Robe", "Tailoring", 60, 82,
                    new[] { "Spider Silk Strand", "Runestone Shard", "Silver Wire" }, new[] { 6, 1, 1 },
                    "Ghost robe. +6 AC, +8 Int, +5 Wis.", 68, 15f, 5f, true, true),
                new Recipe("Ironbark Reinforced Leggings", "Tailoring", 68, 88,
                    new[] { "Cured Tough Leather", "Ironbark Plank", "Steel Chain Links" }, new[] { 2, 1, 2 },
                    "Heavy legs. +8 AC, +6 End.", 80, 15f, 5f, true, false),
                new Recipe("Wyrmscale Hauberk", "Tailoring", 72, 95,
                    new[] { "Cured Tough Leather", "Steel Chain Links", "Adamantite Rivets" }, new[] { 3, 3, 1 },
                    "Dragon mail. +10 AC, +7 End.", 90, 15f, 5f, true, false),
                new Recipe("Abyssal Silk Robe", "Tailoring", 78, 100,
                    new[] { "Abyssal Weave Cloth", "Breath of the Abyss", "Silver Wire" }, new[] { 1, 1, 1 },
                    "Dark robe. +8 AC, +10 Int, +8 Magic Resist.", 105, 15f, 5f, true, true),
                new Recipe("Sunweave Cloak", "Tailoring", 85, 108,
                    new[] { "Celestial Fabric Bolt", "Sunforged Ingot", "Spider Silk Strand" }, new[] { 1, 1, 3 },
                    "Solar cloak. +9 AC, +8 all stats.", 115, 15f, 5f, true, false),
                new Recipe("Moonsilver Chainmail", "Tailoring", 90, 115,
                    new[] { "Moonsilver Bar", "Soulsteel Chain Links", "Cured Tough Leather" }, new[] { 1, 1, 2 },
                    "Silver mail. +12 AC, +8 Agi.", 130, 15f, 5f, true, false),
                new Recipe("Phoenix Feather Mantle", "Tailoring", 95, 120,
                    new[] { "Phoenix Feather", "Celestial Fabric Bolt", "Adamantite Rivets" }, new[] { 1, 1, 2 },
                    "Fire cloak. +10 AC, +10 Fire Resist, +6 End.", 145, 15f, 5f, true, true),
                new Recipe("Voidweave Bracers", "Tailoring", 100, 125,
                    new[] { "Abyssal Weave Cloth", "Void Iron Chunk", "Abyssal Silk Thread" }, new[] { 1, 1, 1 },
                    "Dark bracers. +8 AC, +8 Int, +8 Magic Resist.", 155, 15f, 5f, true, false),
                new Recipe("Titan's Hide Belt", "Tailoring", 108, 132,
                    new[] { "Cured Tough Leather", "Titan's Knucklebone", "Adamantite Rivets" }, new[] { 2, 1, 2 },
                    "Giant belt. +9 AC, +10 Str, +8 End.", 170, 15f, 5f, true, false),
                new Recipe("Starmetal Threaded Robe", "Tailoring", 112, 138,
                    new[] { "Celestial Silk Bolt", "Starmetal Fragment", "Abyssal Silk Thread" }, new[] { 1, 1, 2 },
                    "Cosmic robe. +10 AC, +12 Int, +10 Wis.", 185, 15f, 5f, true, true),
                new Recipe("Worldtree Bark Vest", "Tailoring", 118, 142,
                    new[] { "Worldtree Splinter", "Cured Tough Leather", "Soulsteel Chain Links" }, new[] { 1, 2, 1 },
                    "Ancient vest. +12 AC, +10 End, +8 Wis.", 200, 15f, 5f, true, false),
                new Recipe("Dragonlord's Greaves", "Tailoring", 122, 148,
                    new[] { "Cured Tough Leather", "Elder Wyrm Bone", "Adamantite Plate" }, new[] { 2, 1, 1 },
                    "Dragon legs. +14 AC, +10 End, +8 Fire Resist.", 220, 15f, 5f, true, false),
                new Recipe("Soulweave Mantle", "Tailoring", 128, 152,
                    new[] { "Soulsteel Chain Links", "Celestial Silk Bolt", "Breath of the Abyss" }, new[] { 1, 1, 1 },
                    "Spirit cloak. +12 AC, +12 all stats.", 240, 15f, 5f, true, true),
                new Recipe("Phoenix-Down Vest", "Tailoring", 132, 158,
                    new[] { "Phoenix Feather", "Celestial Silk Bolt", "Sunforged Ingot" }, new[] { 1, 1, 1 },
                    "Reborn vest. +14 AC, +12 End, +10 Fire Resist.", 260, 15f, 5f, true, false),
                new Recipe("Voidweave Leggings", "Tailoring", 138, 162,
                    new[] { "Abyssal Weave Cloth", "Void Iron Chunk", "Soulsteel Chain Links" }, new[] { 2, 1, 1 },
                    "Dark legs. +13 AC, +12 Int, +10 all Resists.", 285, 15f, 5f, true, false),
                new Recipe("Starweave Battle Robe", "Tailoring", 142, 168,
                    new[] { "Celestial Silk Bolt", "Starmetal Fragment", "Crystallized Time" }, new[] { 1, 1, 1 },
                    "Star robe. +14 AC, +12 all stats, +3% haste.", 310, 15f, 5f, true, true),
                new Recipe("Titan's War Greaves", "Tailoring", 148, 172,
                    new[] { "Celestial Silk Bolt", "Titan's Knucklebone", "Adamantite Plate" }, new[] { 1, 1, 1 },
                    "Giant legs. +16 AC, +14 End, +10 Str.", 335, 15f, 5f, true, false),
                new Recipe("Moonweave Battle Cloak", "Tailoring", 152, 175,
                    new[] { "Moonsilver Bar", "Celestial Silk Bolt", "Soulsteel Chain Links" }, new[] { 1, 1, 2 },
                    "Silver cloak. +14 AC, +12 all stats.", 360, 15f, 5f, true, false),
                new Recipe("Phoenix-Wing Pauldrons", "Tailoring", 155, 178,
                    new[] { "Phoenix Feather", "Celestial Silk Bolt", "Adamantite Plate" }, new[] { 2, 1, 1 },
                    "Fire shoulders. +15 AC, +14 End, +12 Fire Resist.", 390, 15f, 5f, true, true),
                new Recipe("Voidsilk Hood", "Tailoring", 158, 178,
                    new[] { "Abyssal Weave Cloth", "Crystallized Time", "Soulsteel Billet" }, new[] { 1, 1, 1 },
                    "Dark hood. +12 AC, +14 Int, +12 Wis.", 410, 15f, 5f, true, false),
                new Recipe("Starmetal Woven Girdle", "Tailoring", 162, 178,
                    new[] { "Starmetal Fragment", "Celestial Silk Bolt", "Soulsteel Chain Links" }, new[] { 1, 1, 2 },
                    "Cosmic belt. +12 AC, +12 all stats, +3% haste.", 440, 15f, 5f, true, false),
                new Recipe("Eternity's Vestments", "Tailoring", 165, 178,
                    new[] { "Crystallized Time", "Celestial Silk Bolt", "Essence of the Garden" }, new[] { 1, 1, 1 },
                    "Timeless robe. +16 AC, +13 all stats, +4% haste.", 470, 15f, 5f, true, true),

            };
        }

        // ═════════════════════════════════════════════════════════════
        // Helper methods
        // ═════════════════════════════════════════════════════════════

        private static SkillEntry GetTradeskillEntry(string tradeskill)
        {
            var data = SkillsSaveManager.Data;
            switch (tradeskill)
            {
                case "Smithing":   return data.Smithing;
                case "Baking":     return data.Baking;
                case "Brewing":    return data.Brewing;
                case "Fletching":  return data.Fletching;
                case "Jewelcraft": return data.Jewelcraft;
                case "Tailoring":  return data.Tailoring;
                default:           return null;
            }
        }

        internal static Recipe FindRecipe(string tradeskill, string recipeName)
        {
            if (string.IsNullOrWhiteSpace(tradeskill) ||
                string.IsNullOrWhiteSpace(recipeName)) return null;
            var recipes = GetRecipesForSkill(tradeskill);
            foreach (var r in recipes)
            {
                if (string.Equals(r.Name, recipeName,
                        StringComparison.OrdinalIgnoreCase) ||
                    (r.Name?.IndexOf(recipeName,
                        StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                    return r;
            }
            return null;
        }

        /// <summary>
        /// Check if the player has the required materials in inventory.
        /// Searches both the game's item database and foraged item names.
        /// </summary>
        internal static bool CheckMaterials(Recipe recipe)
        {
            try
            {
                for (int i = 0; i < recipe.Materials.Length; i++)
                {
                    string matName = recipe.Materials[i];
                    int required = recipe.MaterialCounts[i];
                    int found = CountMaterialAll(matName);
                    if (found < required) return false;
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>Count how many of a material the player has across all sources.</summary>
        private static int CountMaterialAll(string matName)
        {
            long found = 0;

            // 1. Player inventory
            try
            {
                var inv = GameData.PlayerInv;
                if (inv?.StoredSlots != null)
                {
                    foreach (var slot in inv.StoredSlots)
                    {
                        if (slot?.MyItem != null && slot.MyItem.ItemName == matName)
                            found += slot.MyItem.Stackable ? Mathf.Max(1, slot.Quantity) : 1;
                    }
                }
            }
            catch { }

            // 2. Bags (if enabled)
            if (SkillsPlugin.CfgCraftFromBags.Value)
            {
                try
                {
                    found += Items.BagSystem.CountItemInAllBags(matName);
                }
                catch { }
            }

            // 3. Bank (if enabled)
            if (SkillsPlugin.CfgCraftFromBank.Value)
            {
                try
                {
                    var bankItems = GetBankItemsSnapshot();
                    if (bankItems != null)
                    {
                        foreach (var (name, qty) in bankItems)
                        {
                            if (name == matName)
                                found += qty;
                        }
                    }
                }
                catch { }
            }

            return found > int.MaxValue ? int.MaxValue : (int)found;
        }

        /// <summary>
        /// Remove materials from inventory, bags, then a staged bank copy.
        /// Returns false if any source changed after the availability check.
        /// The caller owns rollback/commit of the encompassing transaction.
        /// </summary>
        private static bool ConsumeMaterials(Recipe recipe, BankMutation bankMutation)
        {
            try
            {
                for (int i = 0; i < recipe.Materials.Length; i++)
                {
                    string matName = recipe.Materials[i];
                    int toRemove = recipe.MaterialCounts[i];

                    // 1. Remove from inventory first
                    toRemove = RemoveFromInventory(matName, toRemove);

                    // 2. Remove from bags if needed and enabled
                    if (toRemove > 0 && SkillsPlugin.CfgCraftFromBags.Value)
                        toRemove = Items.BagSystem.RemoveItemFromAllBags(matName, toRemove);

                    // 3. Remove from bank if needed and enabled
                    if (toRemove > 0 && SkillsPlugin.CfgCraftFromBank.Value)
                        toRemove = RemoveFromBank(bankMutation, matName, toRemove);

                    if (toRemove > 0)
                        return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"ConsumeMaterials failed: {ex.Message}");
                return false;
            }
        }

        private static int RemoveFromInventory(string matName, int toRemove)
        {
            try
            {
                var inv = GameData.PlayerInv;
                if (inv?.StoredSlots == null) return toRemove;
                for (int j = inv.StoredSlots.Count - 1; j >= 0 && toRemove > 0; j--)
                {
                    var slot = inv.StoredSlots[j];
                    if (slot?.MyItem == null || slot.MyItem.ItemName != matName) continue;
                    int qty = slot.MyItem.Stackable ? Mathf.Max(1, slot.Quantity) : 1;
                    if (qty <= toRemove)
                    {
                        toRemove -= qty;
                        slot.MyItem = GameData.PlayerInv.Empty;
                        slot.Quantity = 1;
                        slot.UpdateSlotImage();
                    }
                    else if (slot.MyItem.Stackable)
                    {
                        slot.Quantity -= toRemove;
                        toRemove = 0;
                        slot.UpdateSlotImage();
                    }
                }
            }
            catch { }
            return toRemove;
        }

        /// <summary>
        /// Remove materials from the staged bank data. Disk is written only by
        /// the shared crash-recovery transaction after inventory/bags are ready.
        /// </summary>
        private static int RemoveFromBank(BankMutation mutation,
            string matName, int toRemove)
        {
            try
            {
                var saveData = mutation?.Data;
                if (saveData?.BankData == null) return toRemove;

                bool modified = false;
                for (int i = saveData.BankData.Count - 1; i >= 0 && toRemove > 0; i--)
                {
                    string itemId = saveData.BankData[i];
                    if (string.IsNullOrEmpty(itemId)) continue;

                    // Resolve ID to name
                    string itemName = null;
                    Item resolvedItem = null;
                    if (itemId.StartsWith("CS_"))
                    {
                        resolvedItem = Items.ItemFactory.GetItemById(itemId);
                        if (resolvedItem != null) itemName = resolvedItem.ItemName;
                    }
                    if (itemName == null)
                    {
                        resolvedItem = GameData.ItemDB?.GetItemByID(itemId);
                        if (resolvedItem != null && resolvedItem != GameData.PlayerInv?.Empty)
                            itemName = resolvedItem.ItemName;
                    }
                    if (itemName != matName) continue;

                    int qty = resolvedItem?.Stackable == true && i < saveData.BankCount.Count
                        ? Mathf.Max(1, saveData.BankCount[i]) : 1;
                    if (qty <= toRemove)
                    {
                        toRemove -= qty;
                        saveData.BankData[i] = "";
                        if (i < saveData.BankCount.Count) saveData.BankCount[i] = 1;
                    }
                    else if (resolvedItem?.Stackable == true)
                    {
                        if (i < saveData.BankCount.Count) saveData.BankCount[i] -= toRemove;
                        toRemove = 0;
                    }
                    modified = true;
                }

                if (modified)
                    mutation.Dirty = true;
            }
            catch (Exception ex) { SkillsPlugin.Log.LogError($"RemoveFromBank error: {ex.Message}"); }
            return toRemove;
        }

        private static BankMutation BeginBankMutation()
        {
            if (!SkillsPlugin.CfgCraftFromBank.Value) return null;
            string path = GetBankPath();
            if (path == null) return null;
            try
            {
                if (!SafeFileIO.TryReadAllTextWithBackup(path, IsBankJsonValid,
                    out string json, out bool recovered, out string error))
                {
                    SkillsPlugin.Log.LogError($"Bank data could not be read safely: {error}");
                    return null;
                }
                var data = JsonUtility.FromJson<BankReadData>(json);
                if (data?.BankData == null) return null;
                if (data.BankCount == null) data.BankCount = new List<int>();
                while (data.BankCount.Count < data.BankData.Count)
                    data.BankCount.Add(1);
                if (recovered)
                    SkillsPlugin.Log.LogWarning("Recovered global bank data from backup before crafting.");
                return new BankMutation { Path = path, BeforeJson = json, Data = data };
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"Bank transaction setup failed: {ex.Message}");
                return null;
            }
        }

        private static string GetBankPath()
        {
            string dataPath = Application.persistentDataPath;
            string path1 = System.IO.Path.Combine(dataPath, "ESSaveData", "GBDATA");
            string path2 = System.IO.Path.Combine(dataPath, "GBDATA");
            return System.IO.File.Exists(path1) ? path1 :
                System.IO.File.Exists(path2) ? path2 : null;
        }

        internal static bool IsBankJsonValid(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;
            try
            {
                string value = json.Trim();
                if (!value.StartsWith("{") || !value.EndsWith("}") ||
                    System.Text.RegularExpressions.Regex.Matches(value,
                        "\\\"BankData\\\"\\s*:").Count != 1 ||
                    System.Text.RegularExpressions.Regex.Matches(value,
                        "\\\"BankCount\\\"\\s*:").Count != 1) return false;
                var data = JsonUtility.FromJson<BankReadData>(value);
                if (data?.BankData == null || data.BankCount == null ||
                    data.BankData.Count > 100000 ||
                    data.BankCount.Count < data.BankData.Count ||
                    data.BankCount.Count > 100000) return false;
                for (int i = 0; i < data.BankData.Count; i++)
                {
                    if (data.BankData[i] == null || data.BankData[i].Length > 512 ||
                        data.BankCount[i] <= 0) return false;
                }
                return true;
            }
            catch { return false; }
        }

        private static void ReloadBankIfPresent()
        {
            try
            {
                foreach (var component in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                {
                    if (component.GetType().Name != "GlobalBank") continue;
                    component.GetType().GetMethod("LoadBank")?.Invoke(component, null);
                    break;
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning($"Bank UI refresh failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Find BankSlots via reflection to avoid compile-time dependency on BankWindow.
        /// Searches all MonoBehaviours for a component with a BankSlots field of type ItemIcon[].
        /// Cached after first successful lookup.
        /// </summary>
        /// <summary>
        /// Find BankSlots via reflection. No caching — the bank component
        /// may not exist until the player opens the bank window, and
        /// changes between characters or scenes.
        /// </summary>
        /// <summary>
        /// Read bank items via reflection on GlobalBank.StoredItems + Quantities.
        /// Returns list of (itemName, quantity) tuples.
        /// </summary>
        /// <summary>
        /// Read bank contents directly from the GBDATA save file on disk.
        /// This avoids needing the GlobalBank component to be active in the scene.
        /// Returns list of (itemName, quantity) for all non-empty slots.
        /// </summary>
        internal static List<(string name, int qty)> GetBankItemsSnapshot()
        {
            try
            {
                string gbPath = GetBankPath();
                if (gbPath == null) return null;

                if (!SafeFileIO.TryReadAllTextWithBackup(gbPath, IsBankJsonValid,
                    out string json, out _, out string readError))
                {
                    SkillsPlugin.Log.LogError($"GetBankItems read failed: {readError}");
                    return null;
                }

                var saveData = JsonUtility.FromJson<BankReadData>(json);
                if (saveData?.BankData == null) return null;

                var result = new List<(string, int)>();
                for (int i = 0; i < saveData.BankData.Count; i++)
                {
                    string itemId = saveData.BankData[i];
                    if (string.IsNullOrEmpty(itemId)) continue;
                    // Resolve item ID to item name
                    string itemName = null;
                    Item resolvedItem = null;

                    // Check our custom items first (CS_ prefix)
                    if (itemId.StartsWith("CS_"))
                    {
                        resolvedItem = Items.ItemFactory.GetItemById(itemId);
                        if (resolvedItem != null) itemName = resolvedItem.ItemName;
                    }

                    // Check game's item database
                    if (itemName == null)
                    {
                        resolvedItem = GameData.ItemDB?.GetItemByID(itemId);
                        if (resolvedItem != null && resolvedItem != GameData.PlayerInv?.Empty)
                            itemName = resolvedItem.ItemName;
                    }

                    if (!string.IsNullOrEmpty(itemName))
                    {
                        int qty = resolvedItem.Stackable && i < saveData.BankCount.Count
                            ? Mathf.Max(1, saveData.BankCount[i]) : 1;
                        result.Add((itemName, qty));
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"GetBankItems error: {ex.Message}");
            }
            return null;
        }

        /// <summary>Minimal class to deserialize GBDATA JSON.</summary>
        [System.Serializable]
        private class BankReadData
        {
            public List<string> BankData = new List<string>();
            public List<int> BankCount = new List<int>();
            public List<string> TabNames = new List<string>();
            public int GEc = 0;
        }

        /// <summary>Give the crafted item to the player via ItemFactory.</summary>
        internal static bool TryGiveItem(Recipe recipe)
        {
            try
            {
                return ErenshorSkills.Items.ItemFactory.GiveItemToPlayer(recipe.Name);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"Crafted item grant failed: {ex.Message}");
                return false;
            }
        }

        private static void ShowRecipeMaterials(Recipe recipe, string tradeskill)
        {
            ChatHelper.Send(
                $"<color=#FF9800>[{tradeskill}]</color> " +
                $"Materials for {recipe.Name}:");
            for (int i = 0; i < recipe.Materials.Length; i++)
            {
                ChatHelper.Send(
                    $"  <color=#AAAAAA>{recipe.MaterialCounts[i]}x " +
                    $"{recipe.Materials[i]}</color>");
            }
        }
    }
}
