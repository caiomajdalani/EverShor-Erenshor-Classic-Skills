using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ErenshorSkills.Monk
{
    /// <summary>
    /// Injects the eight trophy handwraps into verified native boss loot
    /// tables, and gives every trainer-line piece the ClassMerchantStock cap
    /// keeps off the shelf a world source at its own level band.
    /// </summary>
    internal static class MonkHandwrapLoot
    {
        private enum DropTier { Uncommon, Rare, Legendary }

        private sealed class DropRule
        {
            internal readonly string NpcName;
            internal readonly string ItemKey;
            internal readonly DropTier Tier;
            internal readonly bool BossOnly;
            internal DropRule(string npcName, string itemKey, DropTier tier,
                bool bossOnly = true)
            {
                NpcName = npcName;
                ItemKey = itemKey;
                Tier = tier;
                BossOnly = bossOnly;
            }
        }

        private static readonly DropRule[] DropRules =
        {
            new DropRule("Priel Consort", MonkGear.PrielGripsKey, DropTier.Rare),
            new DropRule("Darkwarden Awxa", MonkGear.DarkwardenWrapsKey, DropTier.Rare),
            new DropRule("Darkwarden Ealdred", MonkGear.DarkwardenWrapsKey, DropTier.Rare),
            new DropRule("Darkwarden Ilyth", MonkGear.DarkwardenWrapsKey, DropTier.Rare),
            new DropRule("Molorai Nightmistress", MonkGear.NightmistressFistsKey, DropTier.Rare),
            new DropRule("Greater Azynthian Firebone", MonkGear.FireboneCestusKey, DropTier.Legendary),
            new DropRule("Boneweaver", MonkGear.BoneweaverKnucklesKey, DropTier.Rare),
            new DropRule("Crypt Keeper", MonkGear.CryptkeeperWrapsKey, DropTier.Rare),
            new DropRule("Braxonian Tempest", MonkGear.BraxonianGauntletsKey, DropTier.Rare),
            new DropRule("Braxonian Ascended", MonkGear.BraxonianGauntletsKey, DropTier.Rare),
            new DropRule("Expert Gladiator", MonkGear.PlanarChampionWrapsKey, DropTier.Legendary),
            new DropRule("Wandering Gladiator", MonkGear.PlanarChampionWrapsKey, DropTier.Legendary)
            ,
            // Extra acquisition routes on verified regular dungeon families.
            // These supplement trainer availability and never include a
            // boss-exclusive trophy or a tailored handwrap.
            new DropRule("Neophyte Necromancer", MonkGear.CopperWrapsKey,
                DropTier.Uncommon, false),
            new DropRule("Priel Malarist", MonkGear.JadeWrapsKey,
                DropTier.Uncommon, false),
            new DropRule("Priel Purist", MonkGear.JadeWrapsKey,
                DropTier.Uncommon, false),
            new DropRule("Azynthian Firebone", MonkGear.EmberWrapsKey,
                DropTier.Uncommon, false),
            new DropRule("Planar Cobra", MonkGear.MoonWrapsKey,
                DropTier.Rare, false),
            new DropRule("Planar Crawler", MonkGear.MoonWrapsKey,
                DropTier.Rare, false)
            ,
            // City trainers stock starter gear only, so every trainer-line
            // piece above level 10 needs a world source. Each pair below is a
            // regular dungeon mob at the item's own level band plus a named
            // enemy in the same zone, and no entry touches a boss-exclusive
            // trophy or a tailored wrap.
            new DropRule("Molorai Bodyguard", MonkGear.SandalsKey,
                DropTier.Uncommon, false),
            new DropRule("Overseer Orlok", MonkGear.SandalsKey,
                DropTier.Rare, false),

            new DropRule("A Seafiend Manalord", MonkGear.GiKey,
                DropTier.Uncommon, false),
            new DropRule("Worn Ancient Guardian", MonkGear.GiKey,
                DropTier.Rare, false),

            new DropRule("Hooded Stone Serpent", MonkGear.IronWrapsKey,
                DropTier.Uncommon, false),
            new DropRule("Stoneman Knight", MonkGear.IronWrapsKey,
                DropTier.Rare, false),

            new DropRule("Loomingwood Minotaur Legionnaire",
                MonkGear.LegwrapsKey, DropTier.Uncommon, false),
            new DropRule("Loomingwood Minotaur Hero", MonkGear.LegwrapsKey,
                DropTier.Rare, false),

            new DropRule("Granitus", MonkGear.TranquilFistsKey,
                DropTier.Rare, false),
            new DropRule("The Gardener of Loomingwood",
                MonkGear.TranquilFistsKey, DropTier.Rare, false),

            new DropRule("Solunarian Protector", MonkGear.SunMoonFistsKey,
                DropTier.Legendary, false),
            new DropRule("Valao the Holy", MonkGear.SunMoonFistsKey,
                DropTier.Legendary, false)
        };

        internal static bool IsReady => MonkGear.IsReady &&
            MonkGear.BossDropItems.Count() == 8;

        internal static void InjectCurrentSceneLootTables()
        {
            if (!IsReady) return;
            foreach (LootTable table in UnityEngine.Object.FindObjectsOfType<LootTable>())
                InjectLootTable(table);
        }

        internal static void InjectLootTable(LootTable table)
        {
            if (table == null || !IsReady) return;
            NPC npc = table.GetComponent<NPC>();
            string npcName = npc?.NPCName;
            if (string.IsNullOrWhiteSpace(npcName))
                npcName = table.gameObject.name.Replace("(Clone)", "").Trim();
            InjectLootTable(table, npcName);
        }

        private static void InjectLootTable(LootTable table, string npcName)
        {
            if (table == null || !IsReady || string.IsNullOrWhiteSpace(npcName)) return;
            foreach (DropRule rule in DropRules)
            {
                if (!string.Equals(rule.NpcName, npcName,
                    StringComparison.OrdinalIgnoreCase)) continue;
                Item item = MonkGear.GetItem(rule.ItemKey);
                List<Item> tier = GetOrCreateTier(table, rule.Tier);
                if (item == null || tier == null) continue;
                foreach (List<Item> list in GetAllTiers(table))
                    list?.RemoveAll(candidate => candidate != null &&
                        string.Equals(candidate.Id, item.Id,
                            StringComparison.OrdinalIgnoreCase));
                tier.Add(item);
            }
        }

        private static List<Item> GetOrCreateTier(LootTable table, DropTier tier)
        {
            if (tier == DropTier.Legendary)
                return table.LegendaryDrop ??
                    (table.LegendaryDrop = new List<Item>());
            if (tier == DropTier.Uncommon)
                return table.UncommonDrop ??
                    (table.UncommonDrop = new List<Item>());
            return table.RareDrop ?? (table.RareDrop = new List<Item>());
        }

        private static IEnumerable<List<Item>> GetAllTiers(LootTable table)
        {
            yield return table.GuaranteeOneDrop;
            yield return table.CommonDrop;
            yield return table.UncommonDrop;
            yield return table.RareDrop;
            yield return table.LegendaryDrop;
            yield return table.UltraRareDrop;
        }

        internal static bool RunAcquisitionSelfTest(out string detail)
        {
            if (!IsReady)
            {
                detail = "Monk boss handwrap catalog is not ready";
                return false;
            }
            var errors = new List<string>();
            List<Item> vendor = MonkGear.VendorItems.ToList();
            foreach (Item item in MonkGear.BossDropItems)
            {
                if (vendor.Contains(item)) errors.Add(item.ItemName + " is vendor-sold");
                if (item.PlayerCannotSell || item.Disposable ||
                    item.NoTradeNoDestroy || item.ItemValue <= 0)
                    errors.Add(item.ItemName +
                        " cannot be sold, trashed, or banked safely");
                if ((item.Lore ?? "").IndexOf("Acquisition:",
                    StringComparison.OrdinalIgnoreCase) < 0)
                    errors.Add(item.ItemName + " lacks acquisition text");
            }
            foreach (DropRule rule in DropRules.Where(rule => !rule.BossOnly))
            {
                Item item = MonkGear.GetItem(rule.ItemKey);
                if (MonkGear.IsBossDrop(item) || MonkGear.IsCrafted(item))
                    errors.Add(rule.NpcName + " regular-mob rule leaks an exclusive wrap");
            }

            GameObject probe = null;
            try
            {
                probe = new GameObject("CS Monk Handwrap Loot Probe");
                LootTable table = probe.AddComponent<LootTable>();
                table.GuaranteeOneDrop = new List<Item>();
                table.CommonDrop = new List<Item>();
                table.UncommonDrop = new List<Item>();
                table.RareDrop = new List<Item>();
                table.LegendaryDrop = new List<Item>();
                table.UltraRareDrop = new List<Item>();
                foreach (DropRule rule in DropRules)
                {
                    foreach (List<Item> list in GetAllTiers(table)) list.Clear();
                    InjectLootTable(table, rule.NpcName);
                    Item expected = MonkGear.GetItem(rule.ItemKey);
                    int total = GetAllTiers(table).Sum(list =>
                        list.Count(item => ReferenceEquals(item, expected)));
                    if (total != 1 || !GetOrCreateTier(table, rule.Tier).Contains(expected))
                        errors.Add(rule.NpcName + " loot tier mismatch");
                    InjectLootTable(table, rule.NpcName);
                    total = GetAllTiers(table).Sum(list =>
                        list.Count(item => ReferenceEquals(item, expected)));
                    if (total != 1) errors.Add(rule.NpcName + " injection is not idempotent");
                }
            }
            catch (Exception ex) { errors.Add("synthetic loot test: " + ex.Message); }
            finally { if (probe != null) UnityEngine.Object.Destroy(probe); }

            try
            {
                var native = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (GameObject prefab in Resources.LoadAll<GameObject>("NPCs"))
                {
                    if (prefab == null || prefab.GetComponent<LootTable>() == null) continue;
                    string name = prefab.GetComponent<NPC>()?.NPCName;
                    if (string.IsNullOrWhiteSpace(name)) name = prefab.name;
                    native.Add(name.Replace("(Clone)", "").Trim());
                }
                foreach (string source in DropRules.Select(rule => rule.NpcName).Distinct())
                    if (!native.Contains(source)) errors.Add("native boss not found: " + source);
            }
            catch (Exception ex) { errors.Add("native boss audit: " + ex.Message); }

            detail = errors.Count == 0
                ? "eight boss-only handwraps across twelve verified boss loot sources plus ten trainer-line wraps and armor pieces on eighteen regular dungeon and named sources; vendor/crafting boundaries, sellability, acquisition hints, and duplicate-safe injection passed"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }
    }
}
