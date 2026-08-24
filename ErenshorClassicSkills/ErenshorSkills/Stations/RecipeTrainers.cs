using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;

namespace ErenshorSkills.Stations
{
    /// <summary>
    /// RECIPE VENDOR INJECTION SYSTEM
    ///
    /// Instead of spawning custom trainer NPCs, this hooks into the game's
    /// existing merchant system. When a vendor window opens, recipe scrolls
    /// are injected into relevant merchants (food merchant gets baking recipes,
    /// jewelry merchant gets jewelcraft recipes, etc.).
    ///
    /// Recipe scrolls are custom Item ScriptableObjects with CS_Recipe_ prefix.
    /// When purchased, the recipe is auto-learned and the scroll is consumed.
    /// </summary>
    public static class RecipeVendors
    {
        private static Dictionary<string, Item> _recipeScrolls
            = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        private static bool _initialized;

        internal static int RecipeScrollCountForTesting => _recipeScrolls.Count;

        // Map vendor names/descriptions to tradeskills
        // The game uses transform.name for the vendor identity
        private static readonly Dictionary<string, string[]> VendorTradeskillMap
            = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // Vendor name contains -> tradeskill recipes to add
            { "Smith",    new[] { "Smithing" } },
            { "Blacksmith", new[] { "Smithing" } },
            { "Forge",    new[] { "Smithing" } },
            { "Baker",    new[] { "Baking" } },
            { "Cook",     new[] { "Baking" } },
            { "Food",     new[] { "Baking" } },
            { "Brewer",   new[] { "Brewing" } },
            { "Bartend",  new[] { "Brewing" } },
            { "Tavern",   new[] { "Brewing" } },
            { "Fletcher", new[] { "Fletching" } },
            { "Bow",      new[] { "Fletching" } },
            { "Arrow",    new[] { "Fletching" } },
            { "Jewel",    new[] { "Jewelcraft" } },
            { "Gem",      new[] { "Jewelcraft" } },
            { "Tailor",   new[] { "Tailoring" } },
            { "Cloth",    new[] { "Tailoring" } },
            { "Leather",  new[] { "Tailoring" } },
            // General merchants get everything
            { "General",  new[] { "Smithing", "Baking", "Brewing", "Fletching", "Jewelcraft", "Tailoring" } },
            { "Merchant",  new[] { "Smithing", "Baking", "Brewing", "Fletching", "Jewelcraft", "Tailoring" } },
        };

        // Materials that vendors sell per tradeskill.
        private static readonly Dictionary<string, string[]> VendorMaterials
            = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // ═══════════════════════════════════════════════════════════
            // VENDOR-ONLY MATERIALS (herbs, basic crafting supplies)
            // Ores, gems, refined metals → Mining nodes (zone-tiered)
            // Monster parts → Mob drops (beast, wyrm, spider, void, etc.)
            // Endgame materials → Boss kills + T5/T6 mining nodes
            // ═══════════════════════════════════════════════════════════
            { "Smithing", new[] {
                // All ores and metals from mining nodes only
                "Healing Moss", "Smooth Pebble", "Charred Root",
                "Runestone Shard", "Obsidian Shard",
                "Enchanted Root", "Blightvein Moss",
                "Ironbark Plank"
            } },
            { "Baking", new[] {
                "Edible Root", "Wild Berries", "Golden Wheat", "Pod of Water",
                "Fresh Mushroom", "Charred Root", "Aromatic Herb Bundle",
                "Honey Comb Fragment", "Firebloom Pepper", "Desert Cactus Fruit",
                "Dawn Blossom", "Healing Moss", "Solunarian Fruit",
                "Garden Ambrosia", "Essence of the Garden",
                "Dawn Nectar", "Shimmer Herb", "Nightshade Berry",
                "Firesage Bundle", "Moonvine Root"
            } },
            { "Brewing", new[] {
                "Pod of Water", "Swamp Root", "Healing Moss",
                "Wild Berries", "Faerie Dust", "Duskbloom Petal",
                "Desert Cactus Fruit", "Handful of Nuts", "Firebloom Pepper",
                "Enchanted Root", "Blightvein Moss", "Ashbloom Herb",
                "Nightshade Berry", "Firesage Bundle",
                "Dawn Nectar", "Shimmer Herb", "Moonvine Root",
                "Honey Comb Fragment", "Essence of the Garden"
            } },
            { "Fletching", new[] {
                "Dark Bark Strip", "Eagle Feather", "Nesting Feather",
                "Bramble Thorn", "Tangled String",
                "Windwashed Crystal", "Blightvein Moss",
                "Ironbark Plank", "Obsidian Shard", "Enchanted Root"
            } },
            { "Jewelcraft", new[] {
                "Smooth Pebble", "Rough Geode", "Moonstone Pebble",
                "Tangled String", "Glimmering Geode Fragment",
                "Brine-Crusted Pearl", "Enchanted Root",
                "Bloodstone Chip", "Corruption Crystal",
                "Blightvein Moss", "Dawn Blossom", "Desert Cactus Fruit",
                "Moonvine Root", "Solunarian Flower",
                "Azynthi Petal", "Shimmer Herb", "Faerie Dust"
            } },
            { "Tailoring", new[] {
                "Tangled String",
                "Enchanted Root", "Blightvein Moss", "Dawn Blossom",
                "Desert Cactus Fruit", "Golden Pollen", "Silken Grass Blade",
                "Ethereal Gossamer"
            } },
        };
        // Cached scroll icons per tradeskill
        private static Dictionary<string, Sprite> _scrollIcons
            = new Dictionary<string, Sprite>();

        /// <summary>Create recipe scroll Item objects for all recipes.</summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _recipeScrolls.Clear();
            _scrollIcons.Clear();

            // Pre-generate one scroll icon per tradeskill
            GenerateScrollIcons();

            string[] tradeskills = { "Smithing", "Baking", "Brewing", "Fletching", "Jewelcraft", "Tailoring" };
            foreach (var ts in tradeskills)
            {
                Sprite icon = null;
                _scrollIcons.TryGetValue(ts, out icon);

                var recipes = Skills.Tradeskills.GetRecipesForSkillPublic(ts);
                foreach (var r in recipes)
                {
                    string scrollName = "Recipe: " + r.Name;
                    string scrollId = "CS_Recipe_" + r.Name.Replace(" ", "_").Replace("(", "").Replace(")", "");
                    int cost = GetRecipeCost(r.TrivialLevel);

                    var scroll = ScriptableObject.CreateInstance<Item>();
                    scroll.name = scrollName; // Unity SO name
                    scroll.ItemName = scrollName;
                    scroll.Id = scrollId;
                    scroll.ItemValue = cost;
                    scroll.Classes = new List<Class>();
                    scroll.ItemLevel = 0;
                    scroll.ItemIcon = icon;
                    scroll.Lore = $"{ts} recipe scroll.\nTeaches: {r.Name}\nRequires {ts} level {r.MinSkillLevel}.\nTrivial at level {r.TrivialLevel}.\n\n{r.ResultDescription ?? ""}";

                    // Force General slot — must be set explicitly
                    scroll.RequiredSlot = (Item.SlotType)0; // General

                    // Zero out ALL stats so tooltip hides the stat panel
                    scroll.Str = 0; scroll.End = 0; scroll.Dex = 0;
                    scroll.Agi = 0; scroll.Int = 0; scroll.Wis = 0;
                    scroll.Cha = 0; scroll.Res = 0;
                    scroll.HP = 0; scroll.Mana = 0; scroll.AC = 0;
                    scroll.MR = 0; scroll.ER = 0; scroll.PR = 0; scroll.VR = 0;
                    scroll.WeaponDmg = 0; scroll.WeaponDly = 0f;
                    scroll.Stackable = false;
                    scroll.NoTradeNoDestroy = false;
                    // The normal vendor return prevents an economy loop while
                    // still allowing duplicate or obsolete scroll disposal.
                    scroll.PlayerCannotSell = false;
                    scroll.ThisWeaponType = (Item.WeaponType)0; // None
                    scroll.Template = false;

                    // Dummy spell so right-click triggers UseConsumable → our prefix intercepts
                    var dummySpell = ScriptableObject.CreateInstance<Spell>();
                    dummySpell.SpellName = "Learn " + r.Name;
                    scroll.ItemEffectOnClick = dummySpell;

                    // Null out fields that could cause issues
                    scroll.EquipmentToActivate = null;
                    scroll.ShoulderTrimL = null; scroll.ShoulderTrimR = null;
                    scroll.ElbowTrimL = null; scroll.ElbowTrimR = null;
                    scroll.KneeTrimL = null; scroll.KneeTrimR = null;


                    _recipeScrolls[scrollName] = scroll;

                    // Register with ItemFactory so save/restore system tracks it
                    Items.ItemFactory.RegisterExternalItem(scrollName, scroll);
                }
            }
            _initialized = true;
            SkillsPlugin.Log.LogInfo($"RecipeVendors: Created {_recipeScrolls.Count} recipe scroll items.");
        }

        /// <summary>
        /// Generate a simple scroll/parchment icon for each tradeskill.
        /// 64x64 pixel icons with tradeskill-colored wax seal.
        /// </summary>
        private static void GenerateScrollIcons()
        {
            var colors = new Dictionary<string, Color>
            {
                { "Smithing",   new Color(0.85f, 0.45f, 0.15f) },
                { "Baking",     new Color(0.90f, 0.75f, 0.20f) },
                { "Brewing",    new Color(0.50f, 0.30f, 0.70f) },
                { "Fletching",  new Color(0.30f, 0.70f, 0.30f) },
                { "Jewelcraft", new Color(0.30f, 0.55f, 0.90f) },
                { "Tailoring",  new Color(0.80f, 0.30f, 0.55f) },
            };

            foreach (var kvp in colors)
            {
                var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Point;
                DrawScrollIcon(tex, kvp.Value);
                tex.Apply();
                var sprite = Sprite.Create(tex, new Rect(0, 0, 64, 64),
                    new Vector2(0.5f, 0.5f), 100f);
                _scrollIcons[kvp.Key] = sprite;
            }
            SkillsPlugin.Log.LogInfo($"RecipeVendors: Generated {_scrollIcons.Count} scroll icons.");
        }

        /// <summary>
        /// Draw a scroll/parchment icon onto a 64x64 texture.
        /// Looks like a rolled parchment scroll with a colored wax seal.
        /// </summary>
        private static void DrawScrollIcon(Texture2D tex, Color sealColor)
        {
            // Palette
            Color clear = new Color(0, 0, 0, 0);
            Color parchLight = new Color(0.92f, 0.85f, 0.68f);
            Color parchDark  = new Color(0.78f, 0.70f, 0.52f);
            Color parchEdge  = new Color(0.65f, 0.55f, 0.38f);
            Color rollTop    = new Color(0.82f, 0.74f, 0.56f);
            Color rollShade  = new Color(0.60f, 0.50f, 0.35f);
            Color lineColor  = new Color(0.45f, 0.38f, 0.28f, 0.5f);

            // Fill transparent
            for (int x = 0; x < 64; x++)
                for (int y = 0; y < 64; y++)
                    tex.SetPixel(x, y, clear);

            // Scroll body (main parchment area) — from y=8 to y=52, x=12 to x=52
            for (int y = 8; y <= 52; y++)
                for (int x = 12; x <= 52; x++)
                {
                    // Edge darkening
                    bool isEdge = x == 12 || x == 52 || y == 8 || y == 52;
                    bool nearEdge = x <= 14 || x >= 50 || y <= 10 || y >= 50;
                    tex.SetPixel(x, y, isEdge ? parchEdge : nearEdge ? parchDark : parchLight);
                }

            // Top roll (cylinder illusion) — y=53 to y=58
            for (int y = 53; y <= 58; y++)
                for (int x = 10; x <= 54; x++)
                {
                    float t = (y - 53f) / 5f;
                    Color c = Color.Lerp(rollShade, rollTop, t < 0.5f ? t * 2 : 2 - t * 2);
                    bool edge = x == 10 || x == 54;
                    tex.SetPixel(x, y, edge ? rollShade : c);
                }

            // Bottom roll — y=2 to y=7
            for (int y = 2; y <= 7; y++)
                for (int x = 10; x <= 54; x++)
                {
                    float t = (y - 2f) / 5f;
                    Color c = Color.Lerp(rollTop, rollShade, t < 0.5f ? t * 2 : 2 - t * 2);
                    bool edge = x == 10 || x == 54;
                    tex.SetPixel(x, y, edge ? rollShade : c);
                }

            // Text lines (decorative) — horizontal lines on parchment
            for (int i = 0; i < 5; i++)
            {
                int ly = 18 + i * 7;
                for (int x = 18; x <= 46; x++)
                    tex.SetPixel(x, ly, lineColor);
            }

            // Wax seal (colored circle) — center-bottom of scroll
            int sealCX = 32, sealCY = 16;
            int sealR = 6;
            Color sealDark = sealColor * 0.6f; sealDark.a = 1;
            Color sealBright = Color.Lerp(sealColor, Color.white, 0.3f); sealBright.a = 1;
            for (int y = sealCY - sealR; y <= sealCY + sealR; y++)
                for (int x = sealCX - sealR; x <= sealCX + sealR; x++)
                {
                    float dist = Mathf.Sqrt((x - sealCX) * (x - sealCX) + (y - sealCY) * (y - sealCY));
                    if (dist <= sealR)
                    {
                        // Simple shading — lighter top-left
                        float shade = Mathf.Clamp01(0.5f + (sealCX - x + sealCY - y) * 0.05f);
                        Color c = Color.Lerp(sealDark, sealBright, shade);
                        c.a = 1;
                        if (dist > sealR - 1) c = sealDark; // edge ring
                        tex.SetPixel(x, y, c);
                    }
                }
        }

        /// <summary>
        /// Inject recipe scrolls into a vendor's item list.
        /// Called from the final-priority Harmony prefix on
        /// VendorWindow.LoadWindow.
        /// </summary>
        public static void InjectRecipesIntoVendor(List<Item> vendorItems, VendorInventory vendor)
        {
            if (!_initialized) Initialize();
            if (vendorItems == null || vendor == null) return;
            if (!SkillsSaveManager.HasLoaded) return;
            // Class spell merchants are intentionally curated catalogs. The
            // generic fallback below treats unrecognized vendors as crafting
            // merchants, which would otherwise append more than a hundred
            // recipes and materials to curated Necromancer or Monk catalogs.
            if (vendor.GetComponent<Necromancer.NecromancerVendorMarker>() !=
                    null ||
                vendor.GetComponent<Monk.MonkVendorMarker>() != null)
                return;

            // Native LoadWindow appends unlocked quest rewards after prefixes,
            // which would otherwise leave those normal stock items behind the
            // recipe pages. Add them now using the game's own conditions; the
            // original method then sees them in the list and does not duplicate
            // them.
            EnsureUnlockedQuestRewardsPresent(vendorItems, vendor);

            // Vendor inventories persist between opens. Remove scrolls learned
            // since the last open so stale entries cannot be repurchased.
            vendorItems.RemoveAll(candidate =>
            {
                if (!IsRecipeScroll(candidate)) return false;
                string learnedName = candidate.ItemName.Substring(8);
                return SkillsSaveManager.Data.IsRecipeKnown(learnedName);
            });

            // Determine which tradeskills this vendor sells
            string vendorName = vendor.transform.name ?? "";
            string vendorDesc = vendor.VendorDesc ?? "";
            string combined = vendorName + " " + vendorDesc;

            var tradeskillsToAdd = new List<string>();
            foreach (var kvp in VendorTradeskillMap)
            {
                if (combined.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    foreach (var ts in kvp.Value)
                        if (!tradeskillsToAdd.Contains(ts))
                            tradeskillsToAdd.Add(ts);
                }
            }

            if (tradeskillsToAdd.Count == 0)
            {
                // Fallback: ALL vendors sell ALL tradeskill materials and recipes
                tradeskillsToAdd.AddRange(new[] { "Smithing", "Baking", "Brewing", "Fletching", "Jewelcraft", "Tailoring" });
            }

            // Add unknown recipe scrolls for matching tradeskills
            int added = 0;
            foreach (var ts in tradeskillsToAdd)
            {
                var recipes = Skills.Tradeskills.GetRecipesForSkillPublic(ts);
                foreach (var r in recipes)
                {
                    // Skip recipes the player already knows
                    if (SkillsSaveManager.Data.IsRecipeKnown(r.Name)) continue;
                    // Skip drop-only recipes — not sold by vendors
                    if (r.DropOnly) continue;
                    // Vendors only sell recipes up to skill level 90
                    if (r.MinSkillLevel > 90) continue;

                    string scrollName = "Recipe: " + r.Name;
                    if (_recipeScrolls.TryGetValue(scrollName, out Item scroll))
                    {
                        if (!vendorItems.Contains(scroll))
                        {
                            vendorItems.Add(scroll);
                            added++;
                        }
                    }
                }
            }

            // Also inject crafting materials at marked-up prices
            int matAdded = 0;
            foreach (var ts in tradeskillsToAdd)
            {
                if (!VendorMaterials.TryGetValue(ts, out var mats)) continue;
                foreach (var matName in mats)
                {
                    // Look up the item in our registry or game DB
                    var matItem = Items.ItemFactory.GetItem(matName);
                    if (matItem == null && GameData.ItemDB?.ItemDB != null)
                    {
                        foreach (var gi in GameData.ItemDB.ItemDB)
                            if (gi != null && gi.ItemName == matName)
                            { matItem = gi; break; }
                    }
                    if (matItem != null && !vendorItems.Contains(matItem))
                    {
                        // Do not mutate ItemValue here. Items are shared
                        // ScriptableObjects; multiplying the value on every vendor
                        // open inflated both buy and sell prices without bound.
                        vendorItems.Add(matItem);
                        matAdded++;
                    }
                }
            }

            // Vendor inventories persist between opens, including lists saved
            // in the old recipes-first order. Stable-partition every open so
            // native stock, quest rewards, materials, and other mods' normal
            // merchandise retain their relative order ahead of recipe pages.
            MoveRecipeScrollsToEnd(vendorItems);

            if (added > 0 || matAdded > 0)
                SkillsPlugin.Log.LogInfo(
                    $"RecipeVendors: Injected {added} scrolls + {matAdded} materials into {vendorName}");
        }

        private static void EnsureUnlockedQuestRewardsPresent(
            List<Item> vendorItems, VendorInventory vendor)
        {
            if (vendor.QuestRewardsForSale == null) return;
            Item empty = GameData.PlayerInv?.Empty;
            foreach (Quest quest in vendor.QuestRewardsForSale)
            {
                Item reward = quest?.UnlockItemForVendor;
                if (quest == null || reward == null || reward == empty ||
                    !GameData.IsQuestDone(quest.DBName) ||
                    vendorItems.Contains(reward)) continue;
                vendorItems.Add(reward);
            }
        }

        internal static void MoveRecipeScrollsToEnd(List<Item> vendorItems)
        {
            if (vendorItems == null || vendorItems.Count < 2) return;

            var scrolls = new List<Item>();
            for (int i = 0; i < vendorItems.Count; i++)
            {
                Item item = vendorItems[i];
                if (IsRecipeScroll(item)) scrolls.Add(item);
            }
            if (scrolls.Count == 0 || scrolls.Count == vendorItems.Count) return;

            vendorItems.RemoveAll(IsRecipeScroll);
            vendorItems.AddRange(scrolls);
        }

        internal static bool ProbeRecipeScrollOrderingForTesting(
            out string detail)
        {
            try
            {
                if (!_initialized) Initialize();
                Item firstScroll = null;
                Item secondScroll = null;
                foreach (Item scroll in _recipeScrolls.Values)
                {
                    if (firstScroll == null) firstScroll = scroll;
                    else
                    {
                        secondScroll = scroll;
                        break;
                    }
                }

                Item firstStock = null;
                Item secondStock = null;
                foreach (Item item in Items.ItemFactory.GetRegisteredItemsForTesting())
                {
                    if (item == null || IsRecipeScroll(item)) continue;
                    if (firstStock == null) firstStock = item;
                    else if (item != firstStock)
                    {
                        secondStock = item;
                        break;
                    }
                }

                if (firstScroll == null || secondScroll == null ||
                    firstStock == null || secondStock == null)
                {
                    detail = "insufficient stock or recipe fixtures";
                    return false;
                }

                var mixed = new List<Item>
                {
                    firstScroll, firstStock, secondScroll, secondStock
                };
                MoveRecipeScrollsToEnd(mixed);
                bool valid = mixed.Count == 4 &&
                    mixed[0] == firstStock && mixed[1] == secondStock &&
                    mixed[2] == firstScroll && mixed[3] == secondScroll;
                detail = valid
                    ? "stable stock order followed by stable recipe order"
                    : "stable recipes-last partition failed";
                return valid;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Called when the player purchases an item. If it's a recipe scroll,
        /// learn the recipe and consume the scroll.
        /// </summary>
        public static bool OnItemPurchased(Item item)
        {
            if (item == null) return false;
            if (string.IsNullOrEmpty(item.ItemName) ||
                !item.ItemName.StartsWith("Recipe: ")) return false;

            string recipeName = item.ItemName.Substring(8); // Remove "Recipe: " prefix
            if (SkillsSaveManager.Data.IsRecipeKnown(recipeName))
            {
                ChatHelper.Send(
                    $"<color=#FF6600>[Recipe]</color> You already know: " +
                    $"<color=#FFFFFF>{recipeName}</color>.");
                return false; // Don't consume the scroll
            }

            if (SkillsSaveManager.Data.LearnRecipe(recipeName))
            {
                if (!SkillsSaveManager.SaveRecipesFile())
                {
                    SkillsSaveManager.Data.KnownRecipes.Remove(recipeName);
                    ChatHelper.Send(
                        "<color=#EF5350>[Recipe]</color> The recipe could not be " +
                        "saved, so the scroll was not consumed.");
                    return false;
                }
                ChatHelper.Send(
                    $"<color=#FFD700>[Recipe]</color> You have learned: " +
                    $"<color=#FFFFFF>{recipeName}</color>!");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Learn a recipe and consume exactly one scroll as a crash-recoverable
        /// transaction. The native inventory, bag sidecar, and recipe sidecar
        /// are committed together, so an interrupted save resolves wholly to
        /// either the pre-use or post-use state on the next load.
        /// </summary>
        public static bool TryConsumeRecipeScroll(ItemIcon slot)
        {
            Item item = slot?.MyItem;
            if (!IsRecipeScroll(item)) return false;
            if (!Items.BagSystem.IsDirectInventorySlot(slot))
            {
                ChatHelper.Send(
                    "<color=#EF5350>[Recipe]</color> Move the scroll into your " +
                    "character inventory before learning it.");
                return false;
            }

            string recipeName = item.ItemName.Substring(8);
            if (SkillsSaveManager.Data.IsRecipeKnown(recipeName))
            {
                ChatHelper.Send(
                    $"<color=#FF6600>[Recipe]</color> You already know: " +
                    $"<color=#FFFFFF>{recipeName}</color>.");
                return false;
            }

            var mutation = Items.BagSystem.BeginExternalMutation();
            string recipesPath = SkillsSaveManager.GetRecipesPath();
            string recipesBefore = null;
            bool learned = false;
            try
            {
                recipesBefore = SkillsSaveManager.SerializeRecipesFile();
                learned = SkillsSaveManager.Data.LearnRecipe(recipeName);
                if (!learned) return false;
                string recipesAfter = SkillsSaveManager.SerializeRecipesFile();

                if (item.Stackable && slot.Quantity > 1)
                    slot.Quantity--;
                else
                {
                    slot.MyItem = GameData.PlayerInv.Empty;
                    slot.Quantity = 1;
                }
                slot.UpdateSlotImage();

                if (!Items.BagSystem.CommitExternalMutation(
                    "learn-recipe", mutation, recipesPath,
                    recipesBefore, recipesAfter))
                    throw new InvalidOperationException(
                        "The recipe transaction could not be saved.");

                ChatHelper.Send(
                    $"<color=#FFD700>[Recipe]</color> You have learned: " +
                    $"<color=#FFFFFF>{recipeName}</color>!");
                return true;
            }
            catch (Exception ex)
            {
                if (learned)
                    SkillsSaveManager.Data.KnownRecipes.RemoveAll(name =>
                        string.Equals(name, recipeName,
                            StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(recipesBefore) &&
                    SkillsSaveManager.IsRecipesJsonValid(recipesBefore))
                    Items.BagSystem.RollbackExternalMutation(
                        mutation, recipesPath, recipesBefore);
                else
                    Items.BagSystem.RollbackExternalMutation(mutation);
                SkillsPlugin.Log.LogError(
                    $"Recipe scroll transaction rolled back: {ex}");
                ChatHelper.Send(
                    "<color=#EF5350>[Recipe]</color> The recipe could not be " +
                    "saved, so the scroll was not consumed.");
                return false;
            }
        }

        /// <summary>Check if an item is a recipe scroll.</summary>
        public static bool IsRecipeScroll(Item item)
        {
            return item != null && !string.IsNullOrEmpty(item.ItemName) &&
                item.ItemName.StartsWith("Recipe: ");
        }

        /// <summary>Get a recipe scroll Item by recipe name. Used by the drop system.</summary>
        public static Item GetScrollForRecipe(string recipeName)
        {
            if (!_initialized) Initialize();
            string scrollName = "Recipe: " + recipeName;
            if (_recipeScrolls.TryGetValue(scrollName, out Item scroll))
                return scroll;
            return null;
        }

        public static int GetRecipeCost(int trivialLevel)
        {
            if (trivialLevel <= 20) return 10;
            if (trivialLevel <= 40) return 50;
            if (trivialLevel <= 60) return 150;
            if (trivialLevel <= 80) return 400;
            if (trivialLevel <= 100) return 800;
            if (trivialLevel <= 120) return 1500;
            if (trivialLevel <= 140) return 3000;
            if (trivialLevel <= 160) return 6000;
            return 10000;
        }
    }

    /// <summary>
    /// Inject recipe scrolls when a vendor window opens.
    /// </summary>
    [HarmonyPatch(typeof(VendorWindow), "LoadWindow")]
    public static class Patch_VendorLoadWindow
    {
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(List<Item> VendorItems, VendorInventory _incoming)
        {
            try
            {
                RecipeVendors.InjectRecipesIntoVendor(VendorItems, _incoming);
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"Patch_VendorLoadWindow error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Recipe scrolls stay in inventory after purchase.
    /// Player right-clicks them to learn (handled by Patch_UseCustomConsumable).
    /// </summary>
    [HarmonyPatch(typeof(VendorWindow), "Transaction")]
    public static class Patch_VendorPurchase
    {
        public static bool Prefix()
        {
            try
            {
                var slot = GameData.SlotActiveForVendor;
                if (slot == null || !slot.VendorSlot ||
                    !Items.ItemFactory.IsCustomItem(slot.MyItem)) return true;
                int quantity = slot.MyItem.Stackable
                    ? Mathf.Max(1, slot.Quantity) : 1;
                if (Items.ItemFactory.CanAddItemToPlayer(slot.MyItem, quantity))
                    return true;
                ChatHelper.Send(
                    "<color=#EF5350>[Vendor]</color> Your inventory is full. " +
                    "Nothing was purchased and no gold was spent.");
                return false;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    $"Custom vendor purchase preflight failed closed: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Erenshor's Sell Stack path omits the PlayerCannotSell check used by its
    /// normal sell button. Enforce it so bags and recipe scrolls cannot bypass
    /// their protections.
    /// </summary>
    [HarmonyPatch(typeof(VendorWindow), "DoSellStack")]
    public static class Patch_VendorSellStack
    {
        public static bool Prefix(VendorWindow __instance)
        {
            try
            {
                var slot = GameData.SlotActiveForVendor;
                if (slot == null || slot.VendorSlot || slot.MyItem == null)
                    return true;
                if (Items.BagSystem.IsBag(slot.MyItem))
                {
                    Items.BagTransferSafety.TryHandleVendorBagSale(
                        __instance, slot);
                    return false;
                }
                if (!slot.MyItem.PlayerCannotSell &&
                    !Items.BagSystem.IsBag(slot.MyItem)) return true;
                ChatHelper.Send(
                    "<color=#EF5350>[Vendor]</color> That item cannot be sold.");
                return false;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    $"Protected-item sale check failed closed: {ex.Message}");
                return false;
            }
        }
    }
}
