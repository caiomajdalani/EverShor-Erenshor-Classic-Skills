using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ErenshorSkills.Necromancer
{
    /// <summary>
    /// Gives the Necromancer the same mixed vendor/drop acquisition model as
    /// Erenshor's native classes. These are existing members of the balanced
    /// 35-spell kit, not additional abilities, so acquisition never changes
    /// combat output or the class's ability-count budget.
    /// </summary>
    internal static class NecromancerSpellLoot
    {
        private enum DropTier
        {
            Guaranteed,
            Common,
            Uncommon,
            Rare,
            Legendary,
            UltraRare
        }

        private sealed class DropRule
        {
            internal readonly string NpcName;
            internal readonly string SpellKey;
            internal readonly DropTier Tier;

            internal DropRule(string npcName, string spellKey, DropTier tier)
            {
                NpcName = npcName;
                SpellKey = spellKey;
                Tier = tier;
            }
        }

        private static readonly HashSet<string> RaidSpellKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "dread_shield",
                "harmshield",
                "plague_of_souls"
            };

        private static readonly HashSet<string> DropOnlySpellKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "soul_transfusion",
                "devouring_darkness",
                "drain_spirit",
                "funeral_pyre",
                "bone_thrall_5",
                "dread_shield",
                "harmshield",
                "plague_of_souls"
            };

        // Sources and tiers intentionally follow native scroll-drop patterns
        // already present on these NPC families in Erenshor's July 2026 data.
        private static readonly DropRule[] DropRules =
        {
            new DropRule("Priel Consort", "soul_transfusion",
                DropTier.Legendary),
            new DropRule("Priel Malarist", "soul_transfusion",
                DropTier.Legendary),
            new DropRule("Priel Purist", "soul_transfusion",
                DropTier.Legendary),

            new DropRule("Darkwarden Awxa", "devouring_darkness",
                DropTier.Uncommon),
            new DropRule("Darkwarden Ealdred", "devouring_darkness",
                DropTier.Uncommon),
            new DropRule("Darkwarden Ilyth", "devouring_darkness",
                DropTier.Uncommon),

            new DropRule("Molorai Nightmistress", "drain_spirit",
                DropTier.Uncommon),

            new DropRule("Azynthian Firebone", "funeral_pyre",
                DropTier.Legendary),
            new DropRule("Greater Azynthian Firebone", "funeral_pyre",
                DropTier.Legendary),

            new DropRule("Boneweaver", "bone_thrall_5", DropTier.Rare),
            new DropRule("Crypt Keeper", "bone_thrall_5", DropTier.Rare),

            // Planar raid sources mirror the native level-35 raid scrolls on
            // the same encounter families and use their existing rare tier.
            new DropRule("Expert Gladiator", "dread_shield", DropTier.Rare),
            new DropRule("Wandering Gladiator", "dread_shield", DropTier.Rare),

            new DropRule("Solunarian Paladin", "harmshield", DropTier.Rare),
            new DropRule("Solunarian Moonkeeper", "harmshield", DropTier.Rare),
            new DropRule("Solunarian Sunbringer", "harmshield", DropTier.Rare),

            new DropRule("Faerie Trickster", "plague_of_souls", DropTier.Rare),
            new DropRule("Pack Mother", "plague_of_souls", DropTier.Rare),
            new DropRule("Planar Cobra", "plague_of_souls", DropTier.Rare),
            new DropRule("Planar Crawler", "plague_of_souls", DropTier.Rare),
            new DropRule("Planar Faerie", "plague_of_souls", DropTier.Rare),
            new DropRule("Planar Grizzly", "plague_of_souls", DropTier.Rare),
            new DropRule("Planar Minotaur", "plague_of_souls", DropTier.Rare),
            new DropRule("Planar Wolf", "plague_of_souls", DropTier.Rare)
        };

        internal static int DropOnlySpellCount => DropOnlySpellKeys.Count;
        internal static int RaidSpellCount => RaidSpellKeys.Count;
        internal static bool IsReady => DropOnlySpellKeys.All(key =>
            NecromancerCatalog.GetScroll(key) != null);

        internal static bool IsDropOnly(string spellKey)
        {
            return !string.IsNullOrWhiteSpace(spellKey) &&
                DropOnlySpellKeys.Contains(spellKey);
        }

        internal static bool IsRaidDrop(string spellKey)
        {
            return !string.IsNullOrWhiteSpace(spellKey) &&
                RaidSpellKeys.Contains(spellKey);
        }

        internal static void ApplyScrollMetadata(string spellKey, Item scroll)
        {
            if (scroll == null || !IsDropOnly(spellKey)) return;
            bool raid = IsRaidDrop(spellKey);
            if (raid)
            {
                // Preserve native raid item-level metadata, but retain the
                // positive scroll price so unwanted duplicates can be sold.
                scroll.ItemLevel = 42;
            }
            scroll.Lore += raid
                ? "\n\nAcquisition: Rare Planar raid drop."
                : "\n\nAcquisition: Dropped by powerful enemies; not sold by merchants.";
        }

        internal static void InjectCurrentSceneLootTables()
        {
            if (!IsReady) return;
            foreach (LootTable table in UnityEngine.Object
                .FindObjectsOfType<LootTable>())
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

        internal static void InjectLootTable(LootTable table, string npcName)
        {
            if (table == null || !IsReady ||
                string.IsNullOrWhiteSpace(npcName)) return;
            foreach (DropRule rule in DropRules)
            {
                if (!string.Equals(rule.NpcName, npcName,
                    StringComparison.OrdinalIgnoreCase)) continue;
                Item scroll = NecromancerCatalog.GetScroll(rule.SpellKey);
                List<Item> tier = GetOrCreateTier(table, rule.Tier);
                if (scroll == null || tier == null) continue;
                tier.RemoveAll(candidate => candidate != null &&
                    !ReferenceEquals(candidate, scroll) &&
                    string.Equals(candidate.Id, scroll.Id,
                        StringComparison.OrdinalIgnoreCase));
                if (!tier.Contains(scroll)) tier.Add(scroll);
            }
        }

        private static List<Item> GetOrCreateTier(LootTable table,
            DropTier tier)
        {
            switch (tier)
            {
                case DropTier.Guaranteed:
                    return table.GuaranteeOneDrop ??
                        (table.GuaranteeOneDrop = new List<Item>());
                case DropTier.Common:
                    return table.CommonDrop ??
                        (table.CommonDrop = new List<Item>());
                case DropTier.Uncommon:
                    return table.UncommonDrop ??
                        (table.UncommonDrop = new List<Item>());
                case DropTier.Rare:
                    return table.RareDrop ??
                        (table.RareDrop = new List<Item>());
                case DropTier.Legendary:
                    return table.LegendaryDrop ??
                        (table.LegendaryDrop = new List<Item>());
                case DropTier.UltraRare:
                    return table.UltraRareDrop ??
                        (table.UltraRareDrop = new List<Item>());
                default:
                    return null;
            }
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
                detail = "Necromancer spell-scroll catalog is not ready";
                return false;
            }

            var errors = new List<string>();
            List<Item> vendorScrolls = NecromancerCatalog.GetVendorScrolls()
                .ToList();
            foreach (string key in DropOnlySpellKeys)
            {
                Item scroll = NecromancerCatalog.GetScroll(key);
                if (scroll == null)
                {
                    errors.Add("missing scroll " + key);
                    continue;
                }
                if (vendorScrolls.Contains(scroll))
                    errors.Add(scroll.ItemName + " is still vendor-sold");
                if (IsRaidDrop(key) &&
                    (scroll.ItemLevel != 42 || scroll.ItemValue <= 0))
                    errors.Add(scroll.ItemName + " lacks native raid metadata");
                if ((scroll.Lore ?? "").IndexOf("Acquisition:",
                    StringComparison.OrdinalIgnoreCase) < 0)
                    errors.Add(scroll.ItemName + " lacks an acquisition hint");
            }

            GameObject probe = null;
            try
            {
                probe = new GameObject("CS Necromancer Spell Loot Probe");
                LootTable table = probe.AddComponent<LootTable>();
                table.GuaranteeOneDrop = new List<Item>();
                table.CommonDrop = new List<Item>();
                table.UncommonDrop = new List<Item>();
                table.RareDrop = new List<Item>();
                table.LegendaryDrop = new List<Item>();
                table.UltraRareDrop = new List<Item>();
                foreach (DropRule rule in DropRules)
                {
                    foreach (List<Item> tier in GetAllTiers(table)) tier.Clear();
                    InjectLootTable(table, rule.NpcName);
                    Item expected = NecromancerCatalog.GetScroll(rule.SpellKey);
                    List<Item> expectedTier = GetOrCreateTier(table, rule.Tier);
                    int total = GetAllTiers(table).Where(tier => tier != null)
                        .Sum(tier => tier.Count(item =>
                            ReferenceEquals(item, expected)));
                    if (expectedTier == null || total != 1 ||
                        expectedTier.Count(item =>
                            ReferenceEquals(item, expected)) != 1)
                        errors.Add(rule.NpcName + " did not receive exactly one " +
                            (expected?.ItemName ?? rule.SpellKey) + " in " +
                            rule.Tier);
                    InjectLootTable(table, rule.NpcName);
                    total = GetAllTiers(table).Where(tier => tier != null)
                        .Sum(tier => tier.Count(item =>
                            ReferenceEquals(item, expected)));
                    if (total != 1)
                        errors.Add(rule.NpcName + " injection is not idempotent");
                }
            }
            catch (Exception ex)
            {
                errors.Add("synthetic loot-table test: " + ex.Message);
            }
            finally
            {
                if (probe != null) UnityEngine.Object.Destroy(probe);
            }

            try
            {
                var nativeSources = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (GameObject prefab in Resources.LoadAll<GameObject>("NPCs"))
                {
                    if (prefab == null || prefab.GetComponent<LootTable>() == null)
                        continue;
                    NPC npc = prefab.GetComponent<NPC>();
                    string name = npc?.NPCName;
                    if (string.IsNullOrWhiteSpace(name)) name = prefab.name;
                    nativeSources.Add(name.Replace("(Clone)", "").Trim());
                }
                foreach (string source in DropRules.Select(rule => rule.NpcName)
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                    if (!nativeSources.Contains(source))
                        errors.Add("native loot source not found: " + source);
            }
            catch (Exception ex)
            {
                errors.Add("native source audit: " + ex.Message);
            }

            detail = errors.Count == 0
                ? $"{DropOnlySpellKeys.Count} boss-only spells across " +
                  $"{DropRules.Length} verified native loot sources; " +
                  $"{RaidSpellKeys.Count} use native level-42 raid metadata; " +
                  "vendor exclusion and duplicate-safe injection passed"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }
    }
}
