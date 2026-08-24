using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ErenshorSkills.Skills
{
    /// <summary>
    /// FORAGING — In EverQuest, Rangers and Druids could forage food, drink,
    /// and zone-specific tradeskill items just by hitting a button while
    /// standing around. It was the ultimate "skill you use while waiting
    /// for the boat" activity.
    ///
    /// This Erenshor implementation goes further: foraging actually creates
    /// new items in your inventory. Items range from junk and bait to food
    /// consumables to rare rings and jewelry whose stats scale with your
    /// character level. Every zone has a unique forage table.
    ///
    /// Item categories:
    /// - JUNK: Vendor trash with flavor (old boots, rusty nails, etc.)
    /// - BAIT: Fishing grubs, worms — useful for the Fishing skill
    /// - FOOD: Consumable items that provide small stat buffs
    /// - HERBS: Crafting-adjacent ingredients themed to each zone
    /// - EQUIPMENT: Rare rings, necklaces, and trinkets whose stats
    ///   scale with the player's combat level. These are the jackpot.
    ///
    /// Press F9 (configurable) while standing still and out of combat.
    /// Higher skill = better items, lower failure rate.
    /// Each zone has its own forage table matching Erenshor's flora/fauna.
    /// Cooldown: 90 seconds base, reduced to 45s at max level (EQ-authentic).
    /// </summary>
    public static class ForagingSkill
    {
        private static float _lastForageTime = float.NegativeInfinity;

        internal static void ResetRuntime()
        {
            _lastForageTime = float.NegativeInfinity;
        }

        /// <summary>
        /// Cooldown in seconds. In EverQuest, Foraging had roughly a 
        /// 100-second cooldown that couldn't be reduced much. We start at
        /// 90 seconds and allow skill to shave it down to 45s at max level.
        /// This prevents spamming while still rewarding investment.
        /// </summary>
        public static float Cooldown =>
            SkillsPlugin.CfgTestCooldowns.Value
                ? 1f
                : Mathf.Max(90f - SkillsSaveManager.Data.Foraging.Level * 0.9f, 45f);

        /// <summary>Safe, finite cooldown time remaining for HUD display.</summary>
        public static float CooldownRemaining
        {
            get
            {
                if (float.IsNegativeInfinity(_lastForageTime)) return 0f;
                float remaining = Cooldown - (Time.time - _lastForageTime);
                if (float.IsNaN(remaining) || float.IsInfinity(remaining))
                    return 0f;
                return Mathf.Max(0f, remaining);
            }
        }

        public static bool IsReady => CooldownRemaining <= 0f;

        // ═════════════════════════════════════════════════════════════
        // Foraged item definition
        // ═════════════════════════════════════════════════════════════

        public enum ForageCategory
        {
            Junk,       // Vendor trash, flavor items
            Bait,       // Fishing bait items
            Food,       // Consumable food/drink with buffs
            Herb,       // Crafting ingredients, zone-themed
            Equipment   // Rings, necklaces, trinkets (stat-scaled)
        }

        public class ForageItem
        {
            public string Name;
            public ForageCategory Category;
            public int MinSkillLevel;   // Minimum foraging level to find this
            public int GoldValue;       // Vendor sell value
            public string Description;  // Flavor text / tooltip

            // Equipment-only fields
            public bool IsEquipment => Category == ForageCategory.Equipment;
            public string Slot;         // "Ring", "Neck", "Charm", "Waist"
            public string StatType;     // "Strength", "Intelligence", "Wisdom", etc.
            public float StatPerLevel;  // Stat points per player level

            public ForageItem(string name, ForageCategory cat, int minLevel = 1,
                int gold = 1, string desc = "")
            {
                Name = name;
                Category = cat;
                MinSkillLevel = minLevel;
                GoldValue = gold;
                Description = desc;
            }

            /// <summary>Create an equipment forage item.</summary>
            public static ForageItem MakeEquip(string name, string slot,
                string statType, float statPerLevel, int minSkillLevel,
                int gold, string desc)
            {
                return new ForageItem(name, ForageCategory.Equipment,
                    minSkillLevel, gold, desc)
                {
                    Slot = slot,
                    StatType = statType,
                    StatPerLevel = statPerLevel
                };
            }
        }

        // ═════════════════════════════════════════════════════════════
        // Main forage attempt
        // ═════════════════════════════════════════════════════════════

        public static void TryForage()
        {
            if (!SkillsPlugin.CfgEnableForaging.Value) return;

            // Cooldown check
            if (Time.time - _lastForageTime < Cooldown)
            {
                float remaining = Cooldown - (Time.time - _lastForageTime);
                ChatHelper.Send(
                    $"<color=#8BC34A>[Foraging]</color> " +
                    $"You must wait {remaining:F0}s before foraging again.");
                return;
            }

            // Must be out of combat
            try
            {
                if (GameData.InCombat)
                {
                    ChatHelper.Send(
                        $"<color=#8BC34A>[Foraging]</color> " +
                        $"You cannot forage while in combat!");
                    return;
                }
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogWarning(
                    $"Foraging stopped because combat state was unavailable: {ex.Message}");
                return;
            }

            _lastForageTime = Time.time;

            var skill = SkillsSaveManager.Data.Foraging;
            string zone = SceneManager.GetActiveScene().name;

            // Zone level check — under-leveled foragers only find junk
            int reqLevel = GetZoneMinForageLevel(zone);
            if (skill.Level < reqLevel)
            {
                // Still award minimal XP so they can progress
                SkillXpEngine.AwardXp(skill, 2f);

                string[] junkFinds = {
                    "a Handful of Dirt", "a Broken Twig",
                    "a Smooth Pebble", "a Cracked Button",
                    "a Tangled String", "a Rusty Nail",
                    "a Clump of Dead Grass", "a Worm-Eaten Root"
                };
                string junk = junkFinds[UnityEngine.Random.Range(0, junkFinds.Length)];
                ChatHelper.Send(
                    $"<color=#8BC34A>[Foraging]</color> " +
                    $"<color=#AAAAAA>You find {junk}. This area is beyond your skill. " +
                    $"(Need Foraging {reqLevel}, have {skill.Level})</color>");
                return;
            }

            float difficulty = GetZoneDifficulty(zone);

            bool success = SkillXpEngine.SkillCheck(skill, difficulty,
                xpOnSuccess: 12f * difficulty, xpOnFailure: 4f);

            if (success)
            {
                ForageItem item = RollForageItem(zone, skill.Level);
                if (AddForagedItemToInventory(item))
                    DisplayForagedItem(item);
                else
                    ChatHelper.Send(
                        "<color=#EF5350>[Foraging]</color> Your inventory is full; " +
                        "you could not collect the find.");
            }
            else
            {
                string[] failMsgs = new string[]
                {
                    "You fail to find anything useful.",
                    "Your search turns up nothing.",
                    "You forage around but come up empty-handed.",
                    "Nothing but dirt and pebbles here.",
                    "You rummage through the undergrowth fruitlessly.",
                    "The ground yields nothing of interest.",
                    "You dig around but find only rocks and mud.",
                    "A thorough search reveals nothing worthwhile."
                };
                string msg = failMsgs[UnityEngine.Random.Range(0, failMsgs.Length)];
                ChatHelper.Send(
                    $"<color=#8BC34A>[Foraging]</color> " +
                    $"<color=#AAAAAA>{msg}</color>");
            }
        }

        // ═════════════════════════════════════════════════════════════
        // Item rolling logic
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// Roll a foraged item. First determines the category based on
        /// skill level and luck, then picks a specific item from the
        /// zone's table for that category.
        /// </summary>
        private static ForageItem RollForageItem(string zone, int skillLevel)
        {
            // Determine category based on weighted roll and skill level
            ForageCategory category = RollCategory(skillLevel);

            // Get the zone's forage table for this category
            var table = GetZoneTable(zone, category);

            // Filter by skill level requirement
            var eligible = new List<ForageItem>();
            foreach (var item in table)
            {
                if (skillLevel >= item.MinSkillLevel)
                    eligible.Add(item);
            }

            if (eligible.Count == 0)
            {
                // Fallback: basic junk
                return new ForageItem("Handful of Dirt", ForageCategory.Junk,
                    1, 0, "Just... dirt.");
            }

            // Weight toward items closer to our skill level (not always the rarest)
            int index;
            float roll = UnityEngine.Random.value;
            if (roll < 0.5f)
            {
                // Common: pick from first half of eligible items
                index = UnityEngine.Random.Range(0,
                    Mathf.Max(1, eligible.Count / 2));
            }
            else if (roll < 0.85f)
            {
                // Uncommon: pick from anywhere
                index = UnityEngine.Random.Range(0, eligible.Count);
            }
            else
            {
                // Rare: pick from last third (highest skill requirement)
                int start = Mathf.Max(0, eligible.Count - eligible.Count / 3);
                index = UnityEngine.Random.Range(start, eligible.Count);
            }

            return eligible[index];
        }

        /// <summary>
        /// Determine what category of item to forage.
        /// Higher skill levels unlock better category odds.
        /// </summary>
        private static ForageCategory RollCategory(int skillLevel)
        {
            float roll = UnityEngine.Random.value * 100f;

            // Equipment chance: starts at 0% and scales with level
            // Level 15: ~1.5% chance, Level 30: ~4.5%, Level 50: ~10%
            float equipChance = Mathf.Max(0f, (skillLevel - 10) * 0.25f);

            // Food chance: always reasonable, improves with level
            float foodChance = 15f + skillLevel * 0.3f;

            // Bait chance: steady
            float baitChance = 12f + skillLevel * 0.1f;

            // Herb chance: improves with level
            float herbChance = 10f + skillLevel * 0.3f;

            // Build cumulative thresholds
            float threshold = 0f;

            threshold += equipChance;
            if (roll < threshold) return ForageCategory.Equipment;

            threshold += foodChance;
            if (roll < threshold) return ForageCategory.Food;

            threshold += herbChance;
            if (roll < threshold) return ForageCategory.Herb;

            threshold += baitChance;
            if (roll < threshold) return ForageCategory.Bait;

            // Remainder = junk
            return ForageCategory.Junk;
        }

        // ═════════════════════════════════════════════════════════════
        // Zone forage tables — organized by category
        // ═════════════════════════════════════════════════════════════

        private static List<ForageItem> GetZoneTable(string zone,
            ForageCategory category)
        {
            switch (category)
            {
                case ForageCategory.Junk:       return GetJunkTable(zone);
                case ForageCategory.Bait:       return GetBaitTable(zone);
                case ForageCategory.Food:       return GetFoodTable(zone);
                case ForageCategory.Herb:       return GetHerbTable(zone);
                case ForageCategory.Equipment:  return GetEquipmentTable(zone);
                default:                        return GetJunkTable(zone);
            }
        }

        // ── JUNK ────────────────────────────────────────────────────

        private static List<ForageItem> GetJunkTable(string zone)
        {
            // Shared junk pool + zone-specific flavor junk
            var table = new List<ForageItem>
            {
                new ForageItem("Handful of Dirt", ForageCategory.Junk, 1, 0,
                    "Just... dirt."),
                new ForageItem("Smooth Pebble", ForageCategory.Junk, 1, 0,
                    "A round, polished stone. Worthless, but satisfying to hold."),
                new ForageItem("Broken Twig", ForageCategory.Junk, 1, 0,
                    "Snapped clean. Nature's toothpick."),
                new ForageItem("Rusty Nail", ForageCategory.Junk, 1, 1,
                    "Tetanus not included."),
                new ForageItem("Cracked Button", ForageCategory.Junk, 1, 0,
                    "From someone's coat, long ago."),
                new ForageItem("Tangled String", ForageCategory.Junk, 1, 0,
                    "Too short to be useful, too long to throw away."),
                new ForageItem("Worn Leather Scrap", ForageCategory.Junk, 3, 1,
                    "A fragment of something that was once something."),
                new ForageItem("Crumpled Note", ForageCategory.Junk, 5, 1,
                    "The writing has faded beyond legibility."),
            };

            // Zone-specific junk flavor
            switch (zone)
            {
                case "Azure":
                    table.Add(new ForageItem("Soggy Cloth Shoe", ForageCategory.Junk, 1, 2,
                        "Someone's loss is your... also loss."));
                    table.Add(new ForageItem("Barnacle-Encrusted Coin", ForageCategory.Junk, 8, 3,
                        "Too corroded to spend, too interesting to discard."));
                    break;
                case "Rottenfoot":
                    table.Add(new ForageItem("Muck-Covered Boot", ForageCategory.Junk, 1, 1,
                        "The smell alone could be classified as a weapon."));
                    table.Add(new ForageItem("Suspicious Bone", ForageCategory.Junk, 5, 2,
                        "Best not to think about whose it was."));
                    break;
                case "Braxonian":
                    table.Add(new ForageItem("Bleached Skull Fragment", ForageCategory.Junk, 1, 1,
                        "The desert claims all things eventually."));
                    table.Add(new ForageItem("Sand-Scored Buckle", ForageCategory.Junk, 8, 3,
                        "Pitted and worn by endless winds."));
                    break;
                case "Blight":
                    table.Add(new ForageItem("Corrupted Shard", ForageCategory.Junk, 1, 2,
                        "Pulses faintly with wrongness."));
                    table.Add(new ForageItem("Withered Hand", ForageCategory.Junk, 10, 3,
                        "You'd rather not think about this too hard."));
                    break;
            }

            return table;
        }

        // ── BAIT ────────────────────────────────────────────────────

        private static List<ForageItem> GetBaitTable(string zone)
        {
            var table = new List<ForageItem>
            {
                new ForageItem("Fishing Grub", ForageCategory.Bait, 1, 1,
                    "A fat, wriggling grub. Fish love these."),
                new ForageItem("Earthworm", ForageCategory.Bait, 1, 1,
                    "The classic. No fish can resist."),
                new ForageItem("Cricket", ForageCategory.Bait, 3, 2,
                    "Still chirping. Won't be for long."),
                new ForageItem("Beetle Larva", ForageCategory.Bait, 5, 2,
                    "Plump and irresistible to bottom-feeders."),
                new ForageItem("Glowworm", ForageCategory.Bait, 10, 4,
                    "Emits a faint light. Attracts deep-water fish."),
                new ForageItem("Nightcrawler", ForageCategory.Bait, 15, 5,
                    "A thick, meaty worm. Premium bait for serious anglers."),
                new ForageItem("Enchanted Lure Bug", ForageCategory.Bait, 25, 10,
                    "Shimmers with a faint magic. Rare fish can't resist it."),
                new ForageItem("Golden Grub", ForageCategory.Bait, 35, 20,
                    "Practically glows. Said to attract legendary catches."),
            };

            // Zone-specific bait
            switch (zone)
            {
                case "Duskenlight":
                    table.Add(new ForageItem("Twilight Moth", ForageCategory.Bait, 8, 4,
                        "Only found at dusk. Coastal fish go mad for them."));
                    break;
                case "SaltedStrand":
                    table.Add(new ForageItem("Brine Shrimp Cluster", ForageCategory.Bait, 10, 5,
                        "A dense knot of tiny shrimp. Saltwater specialty bait."));
                    break;
                case "Rottenfoot":
                    table.Add(new ForageItem("Bog Leech", ForageCategory.Bait, 12, 6,
                        "Disgusting, but swamp fish are not picky eaters."));
                    break;
            }

            return table;
        }

        // ── FOOD ────────────────────────────────────────────────────

        private static List<ForageItem> GetFoodTable(string zone)
        {
            // Food items provide small stat buffs when consumed.
            // Organized by zone tier with appropriate stat themes.
            var table = new List<ForageItem>();

            // Universal food (all zones)
            table.Add(new ForageItem("Wild Berries", ForageCategory.Food, 1, 2,
                "Tart and refreshing. +1 Endurance for 2 min."));
            table.Add(new ForageItem("Edible Root", ForageCategory.Food, 1, 1,
                "Bland but nutritious. +1 Endurance for 2 min."));
            table.Add(new ForageItem("Pod of Water", ForageCategory.Food, 1, 0,
                "Clean water."));
            table.Add(new ForageItem("Handful of Nuts", ForageCategory.Food, 3, 2,
                "Crunchy and filling. +2 Endurance for 2 min."));
            table.Add(new ForageItem("Fresh Mushroom", ForageCategory.Food, 5, 3,
                "Earthy flavor. +1 Wisdom for 2 min."));

            // Zone-specific food
            switch (zone)
            {
                case "Stowaway":
                    table.Add(new ForageItem("Stowaway's Rations", ForageCategory.Food, 1, 2,
                        "Simple island fare. Just enough to keep going."));
                    break;
                case "Brake":
                    table.Add(new ForageItem("Faerie Apple", ForageCategory.Food, 5, 5,
                        "Faintly glowing fruit. +3 Intelligence for 5 min."));
                    table.Add(new ForageItem("Enchanted Honeycomb", ForageCategory.Food, 15, 12,
                        "Dripping with fae-touched honey. +4 Wisdom for 5 min."));
                    break;
                case "Azure":
                    table.Add(new ForageItem("Dockside Oyster", ForageCategory.Food, 3, 4,
                        "Briny and fresh. +2 Endurance for 3 min."));
                    table.Add(new ForageItem("Sailor's Hardtack", ForageCategory.Food, 8, 3,
                        "Rock-hard biscuit. +3 Endurance for 5 min."));
                    break;
                case "FernallaField":
                    table.Add(new ForageItem("Plains Wheat Cake", ForageCategory.Food, 8, 5,
                        "Simple flatbread. +3 Strength for 5 min."));
                    table.Add(new ForageItem("Revival Berry Mash", ForageCategory.Food, 15, 10,
                        "Sweet and restorative. +4 Endurance for 5 min."));
                    break;
                case "Braxonian":
                    table.Add(new ForageItem("Desert Fig", ForageCategory.Food, 10, 6,
                        "Dried by the sun. +3 Agility for 5 min."));
                    table.Add(new ForageItem("Cactus Water Pouch", ForageCategory.Food, 18, 10,
                        "Barrel cactus extract. +3 Endurance for 5 min."));
                    break;
                case "Silkengrass":
                    table.Add(new ForageItem("Meadow Honey Drops", ForageCategory.Food, 12, 8,
                        "Crystallized honey. +3 Charisma for 5 min."));
                    table.Add(new ForageItem("Silkengrass Tea Bundle", ForageCategory.Food, 20, 12,
                        "Aromatic dried tea leaves. +4 Intelligence for 5 min."));
                    break;
                case "Malaroth":
                    table.Add(new ForageItem("Dragon's Breath Pepper", ForageCategory.Food, 20, 15,
                        "Searingly hot. +5 Strength for 10 min."));
                    table.Add(new ForageItem("Chargrilled Wyrm Egg", ForageCategory.Food, 30, 25,
                        "Cooked by volcanic heat. +6 Strength for 10 min."));
                    break;
                case "Soluna":
                    table.Add(new ForageItem("Solunarian Fruit", ForageCategory.Food, 25, 18,
                        "Tastes of moonlight and sunshine. +5 Wisdom for 10 min."));
                    table.Add(new ForageItem("Dawn Nectar", ForageCategory.Food, 35, 30,
                        "Liquid gold from the first light. +6 Intelligence for 15 min."));
                    break;
                case "Azynthi": case "AzynthiClear":
                    table.Add(new ForageItem("Garden Ambrosia", ForageCategory.Food, 30, 25,
                        "The food of the Azynthi. +6 Wisdom for 10 min."));
                    table.Add(new ForageItem("Essence of Eternity", ForageCategory.Food, 45, 50,
                        "A shimmering droplet. +8 Intelligence for 15 min."));
                    break;
                case "Blight":
                    table.Add(new ForageItem("Blightcap Mushroom", ForageCategory.Food, 25, 12,
                        "Toxic to most, but edible if prepared. +4 Endurance for 5 min."));
                    break;
                case "Ripper":
                    table.Add(new ForageItem("Iron Ration Block", ForageCategory.Food, 20, 10,
                        "Military-grade sustenance. +5 Endurance for 5 min."));
                    break;
            }

            return table;
        }

        // ── HERBS ───────────────────────────────────────────────────

        private static List<ForageItem> GetHerbTable(string zone)
        {
            var table = new List<ForageItem>();

            // Universal herbs
            table.Add(new ForageItem("Common Weed", ForageCategory.Herb, 1, 1,
                "A basic herb. Useful in simple remedies."));
            table.Add(new ForageItem("Healing Moss", ForageCategory.Herb, 5, 3,
                "Applied to wounds, it speeds recovery."));
            table.Add(new ForageItem("Aromatic Herb Bundle", ForageCategory.Herb, 10, 5,
                "A mix of fragrant herbs. Valued by traders."));

            // Zone-specific herbs (higher zones = higher value)
            switch (zone)
            {
                case "Brake":
                    table.Add(new ForageItem("Faerie Dust", ForageCategory.Herb, 5, 6,
                        "Sparkling particles. Alchemists pay well for this."));
                    table.Add(new ForageItem("Dewdrop Herb", ForageCategory.Herb, 10, 8,
                        "Collected at dawn. Used in enchantments."));
                    table.Add(new ForageItem("Fae-Touched Blossom", ForageCategory.Herb, 20, 15,
                        "Rare flower imbued with fae magic."));
                    break;
                case "Vitheo":
                    table.Add(new ForageItem("Rockwort Leaves", ForageCategory.Herb, 5, 5,
                        "Tough mountain herb. Used in endurance tonics."));
                    table.Add(new ForageItem("Vitheo's Blessing Herb", ForageCategory.Herb, 15, 12,
                        "Sacred to the watchers. Potent in healing salves."));
                    break;
                case "Duskenlight":
                    table.Add(new ForageItem("Duskbloom Petal", ForageCategory.Herb, 8, 7,
                        "Only blooms at twilight. Prized by alchemists."));
                    table.Add(new ForageItem("Tidecaller's Root", ForageCategory.Herb, 18, 14,
                        "Said to have been cultivated by the tidecallers of old."));
                    break;
                case "Malaroth":
                    table.Add(new ForageItem("Ashbloom Herb", ForageCategory.Herb, 20, 18,
                        "Thrives in volcanic soil. Extremely potent."));
                    table.Add(new ForageItem("Wyrmsage Bundle", ForageCategory.Herb, 30, 30,
                        "Sage grown in dragon territory. Legendary alchemical reagent."));
                    break;
                case "Soluna":
                    table.Add(new ForageItem("Celestial Root", ForageCategory.Herb, 25, 20,
                        "Pulses faintly with divine energy."));
                    table.Add(new ForageItem("Solunarian Flower", ForageCategory.Herb, 35, 35,
                        "A flower touched by both sun and moon. Extremely rare."));
                    break;
                case "Blight":
                    table.Add(new ForageItem("Blightvein Moss", ForageCategory.Herb, 25, 16,
                        "Corrupted but usable. Poison-makers seek this."));
                    table.Add(new ForageItem("Void-Touched Berry", ForageCategory.Herb, 35, 28,
                        "Tainted by abyssal energy. Handle with care."));
                    break;
                case "Azynthi": case "AzynthiClear":
                    table.Add(new ForageItem("Prismatic Herb", ForageCategory.Herb, 30, 30,
                        "Shifts color constantly. Ultimate alchemical reagent."));
                    table.Add(new ForageItem("Essence of the Garden", ForageCategory.Herb, 45, 60,
                        "Distilled magic of Azynthi's domain. Priceless to the right buyer."));
                    break;
                default:
                    table.Add(new ForageItem("Wild Herb", ForageCategory.Herb, 1, 2,
                        "A common plant with minor medicinal value."));
                    break;
            }

            return table;
        }

        // ── EQUIPMENT ───────────────────────────────────────────────

        /// <summary>
        /// Equipment items that can be foraged. These are the rare jackpot finds.
        /// Stats scale with the PLAYER'S combat level, not the foraging skill level.
        /// The foraging skill level determines which items you CAN find.
        /// This means a level 35 player with maxed foraging finds endgame-relevant
        /// gear, while a level 10 player finds starter-appropriate pieces.
        /// </summary>
        private static List<ForageItem> GetEquipmentTable(string zone)
        {
            var table = new List<ForageItem>();

            // ── Rings (all zones, different tiers) ──────────────────
            table.Add(ForageItem.MakeEquip(
                "Tarnished Copper Band", "Ring", "Endurance", 0.3f, 10, 8,
                "A simple ring, green with age. Still carries a faint enchantment."));
            table.Add(ForageItem.MakeEquip(
                "Mossy Stone Ring", "Ring", "Wisdom", 0.4f, 15, 15,
                "Carved from river stone, overgrown with tiny moss. Sharpens the mind."));
            table.Add(ForageItem.MakeEquip(
                "Weathered Silver Band", "Ring", "Intelligence", 0.5f, 20, 25,
                "Tarnished but intact. A scholar's ring, lost to time."));
            table.Add(ForageItem.MakeEquip(
                "Buried Signet Ring", "Ring", "Charisma", 0.5f, 25, 35,
                "Bears the crest of a forgotten house. Impressive to those who notice."));
            table.Add(ForageItem.MakeEquip(
                "Earthen Band of Vigor", "Ring", "Strength", 0.6f, 30, 50,
                "Warm to the touch, as if the earth itself empowers the wearer."));
            table.Add(ForageItem.MakeEquip(
                "Loam-Crusted Loop of Fortitude", "Ring", "Endurance", 0.7f, 40, 80,
                "Ancient ring pulled from deep soil. Thick with latent power."));

            // ── Necklaces ───────────────────────────────────────────
            table.Add(ForageItem.MakeEquip(
                "Knotted Root Pendant", "Neck", "Wisdom", 0.3f, 12, 10,
                "A natural pendant formed by twisted roots. Druids find these sacred."));
            table.Add(ForageItem.MakeEquip(
                "Polished Bone Charm", "Neck", "Agility", 0.4f, 18, 20,
                "Carved from an unknown creature's bone. Quickens reflexes."));
            table.Add(ForageItem.MakeEquip(
                "Amber-Set Necklace", "Neck", "Intelligence", 0.5f, 25, 40,
                "An insect frozen in amber, set into a crude chain. Focuses the mind."));
            table.Add(ForageItem.MakeEquip(
                "Fossilized Coral Choker", "Neck", "Endurance", 0.6f, 35, 65,
                "Ancient sea coral hardened to stone. The ocean's memory protects you."));

            // ── Trinkets / Charms ───────────────────────────────────
            table.Add(ForageItem.MakeEquip(
                "Lucky Rabbit's Foot", "Charm", "Agility", 0.3f, 8, 5,
                "Questionably lucky, definitely a rabbit's foot."));
            table.Add(ForageItem.MakeEquip(
                "Carved Wooden Totem", "Charm", "Wisdom", 0.4f, 15, 12,
                "A tiny animal spirit carved from driftwood."));
            table.Add(ForageItem.MakeEquip(
                "Glimmering Geode Talisman", "Charm", "Intelligence", 0.5f, 22, 30,
                "Half a geode, crystals still sparkling inside."));
            table.Add(ForageItem.MakeEquip(
                "Petrified Dragon Tooth", "Charm", "Strength", 0.7f, 38, 75,
                "A fossilized fang from a creature long extinct. Radiates primal power."));

            // ── Zone-specific legendary equipment ───────────────────
            switch (zone)
            {
                case "Azynthi": case "AzynthiClear":
                    table.Add(ForageItem.MakeEquip(
                        "Azynthi's Verdant Loop", "Ring", "Wisdom", 0.8f, 45, 120,
                        "A living ring of intertwined vines. Thrums with garden magic."));
                    table.Add(ForageItem.MakeEquip(
                        "Pendant of the Eternal Bloom", "Neck", "Intelligence", 0.8f, 45, 120,
                        "A flower that never wilts, set in mithril. Channel the garden's power."));
                    break;
                case "Soluna":
                    table.Add(ForageItem.MakeEquip(
                        "Solunarian Twilight Band", "Ring", "Charisma", 0.7f, 40, 100,
                        "Shifts between gold and silver with the time of day."));
                    break;
                case "Malaroth":
                    table.Add(ForageItem.MakeEquip(
                        "Wyrm-Scorched Loop", "Ring", "Strength", 0.7f, 35, 90,
                        "Blackened by dragonfire but unbroken. The heat lingers."));
                    break;
                case "Blight":
                    table.Add(ForageItem.MakeEquip(
                        "Void-Touched Circlet", "Neck", "Intelligence", 0.7f, 35, 85,
                        "Pulsates with dark energy. Power at a price."));
                    break;
                case "Brake":
                    table.Add(ForageItem.MakeEquip(
                        "Fae Gossamer Ring", "Ring", "Agility", 0.5f, 20, 30,
                        "Woven from spider silk and faerie thread. Nearly weightless."));
                    break;
            }

            return table;
        }

        /// <summary>
        /// Resolve the immutable definition behind a persisted level-scaled
        /// forage item. ItemFactory uses this when the native save loader asks
        /// for a deterministic CS_FORAGE item ID after a restart.
        /// </summary>
        internal static bool TryGetEquipmentDefinition(string itemName,
            out string slot, out string statType, out float statPerLevel,
            out string description)
        {
            slot = null;
            statType = null;
            statPerLevel = 0f;
            description = null;
            if (string.IsNullOrWhiteSpace(itemName)) return false;

            string[] definitionZones =
            {
                "", "Azynthi", "Soluna", "Malaroth", "Blight", "Brake"
            };
            foreach (string zone in definitionZones)
            {
                foreach (var item in GetEquipmentTable(zone))
                {
                    if (!string.Equals(item.Name, itemName,
                        StringComparison.OrdinalIgnoreCase)) continue;
                    slot = item.Slot;
                    statType = item.StatType;
                    statPerLevel = item.StatPerLevel;
                    description = item.Description;
                    return !string.IsNullOrWhiteSpace(slot) &&
                        !string.IsNullOrWhiteSpace(statType) && statPerLevel > 0f;
                }
            }
            return false;
        }

        /// <summary>
        /// Complete deterministic list of rare equipment definitions for the
        /// opt-in in-game catalog smoke test and item-grant search.
        /// </summary>
        internal static List<string> GetEquipmentNamesForTesting()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] definitionZones =
            {
                "", "Azynthi", "Soluna", "Malaroth", "Blight", "Brake"
            };
            foreach (string zone in definitionZones)
                foreach (var item in GetEquipmentTable(zone))
                    if (item != null && !string.IsNullOrWhiteSpace(item.Name))
                        names.Add(item.Name);
            var result = new List<string>(names);
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        // ═════════════════════════════════════════════════════════════
        // Inventory integration
        // ═════════════════════════════════════════════════════════════

        /// <summary>
        /// Add a foraged item to the player's inventory using the
        /// ItemFactory system. Items are real game objects with icons,
        /// stats, and full inventory integration.
        /// </summary>
        private static bool AddForagedItemToInventory(ForageItem item)
        {
            ErenshorSkills.Items.BagSystem.MutationSnapshot mutation = null;
            Item generatedEquipment = null;
            try
            {
                if (item == null || !SkillsSaveManager.HasLoaded) return false;
                mutation = ErenshorSkills.Items.BagSystem.BeginExternalMutation();
                if (item.IsEquipment)
                {
                    // Equipment: create a level-scaled item
                    generatedEquipment = ErenshorSkills.Items.ItemFactory.CreateScaledEquipment(
                        item.Name, item.Slot, item.StatType,
                        item.StatPerLevel, item.Description);

                    if (generatedEquipment != null)
                    {
                        var inventory = GameData.PlayerInv;
                        if (inventory == null ||
                            !ErenshorSkills.Items.ItemFactory.CanAddItemToPlayer(
                                generatedEquipment, 1))
                            return false;
                        if (!inventory.AddItemToInv(generatedEquipment, 1))
                        {
                            ErenshorSkills.Items.BagSystem.RollbackExternalMutation(mutation);
                            return false;
                        }
                    }
                    else return false;
                }
                else if (!ErenshorSkills.Items.ItemFactory.GiveItemToPlayer(item.Name))
                {
                    ErenshorSkills.Items.BagSystem.RollbackExternalMutation(mutation);
                    return false;
                }

                if (ErenshorSkills.Items.BagSystem.CommitExternalMutation(
                    "foraging-reward", mutation))
                    return true;

                ErenshorSkills.Items.BagSystem.RollbackExternalMutation(mutation);
                return false;
            }
            catch (Exception ex)
            {
                if (mutation != null)
                    ErenshorSkills.Items.BagSystem.RollbackExternalMutation(mutation);
                SkillsPlugin.Log.LogError(
                    $"Error adding foraged item '{item.Name}': {ex.Message}");
                return false;
            }
        }

        // ═════════════════════════════════════════════════════════════
        // Display
        // ═════════════════════════════════════════════════════════════

        private static void DisplayForagedItem(ForageItem item)
        {
            string color;
            string prefix = "";

            switch (item.Category)
            {
                case ForageCategory.Equipment:
                    color = "#FFD700"; // Gold for equipment
                    prefix = "★ ";

                    // Show equipment stats
                    int playerLevel = 1;
                    try { playerLevel = GameData.PlayerStats?.Level ?? 1; }
                    catch { }
                    int statVal = Mathf.Max(1,
                        Mathf.RoundToInt(playerLevel * item.StatPerLevel));

                    ChatHelper.Send(
                        $"<color=#8BC34A>[Foraging]</color> " +
                        $"{prefix}<color={color}>{item.Name}</color> " +
                        $"<color=#AAAAAA>({item.Slot})</color>");
                    return;

                case ForageCategory.Food:
                    color = "#CDDC39"; // Yellow-green for food
                    prefix = "🍎 ";
                    break;
                case ForageCategory.Bait:
                    color = "#8D6E63"; // Brown for bait
                    prefix = "🪱 ";
                    break;
                case ForageCategory.Herb:
                    color = "#66BB6A"; // Green for herbs
                    prefix = "🌿 ";
                    break;
                default:
                    color = "#9E9E9E"; // Gray for junk
                    break;
            }

            ChatHelper.Send(
                $"<color=#8BC34A>[Foraging]</color> " +
                $"You found: {prefix}<color={color}>{item.Name}</color>");
        }

        // ═════════════════════════════════════════════════════════════
        // Zone difficulty
        // ═════════════════════════════════════════════════════════════

        private static float GetZoneDifficulty(string zone)
        {
            switch (zone)
            {
                case "Stowaway": case "Tutorial":        return 0.8f;
                case "Brake": case "Vitheo": case "Hidden": return 1.0f;
                case "Azure":                             return 0.9f;
                case "FernallaField": case "Duskenlight": return 1.2f;
                case "SaltedStrand": case "Rottenfoot":   return 1.4f;
                case "Braxonian": case "Silkengrass":     return 1.5f;
                case "Malaroth": case "Windwashed":
                case "Loomingwood": case "Rockshade":     return 1.7f;
                case "Soluna": case "Blight":
                case "Elderstone": case "Abyssal":        return 1.9f;
                case "Ripper": case "Azynthi":
                case "AzynthiClear": case "Braxonia":
                case "PrielPlateau": case "VitheosEnd":   return 2.0f;
                default:                                  return 1.0f;
            }
        }

        /// <summary>Minimum foraging level required per zone.</summary>
        private static int GetZoneMinForageLevel(string zone)
        {
            switch (zone)
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
