using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills.Necromancer
{
    internal sealed class NecromancerAuraDefinition
    {
        internal string Key;
        internal string ItemName;
        internal string SpellName;
        internal string Description;
        internal string Lore;
        internal int Level;
        internal int Price;
        internal int Mana;
        internal int End;
        internal int Int;
        internal int Cha;
        internal int Vr;
    }

    /// <summary>
    /// A five-gem Necromancer aura progression built on the exact level,
    /// price, and aggregate stat budget of Erenshor's native caster aura
    /// progression. Wisdom becomes Endurance and elemental resistance becomes
    /// void resistance, which changes class identity without adding power.
    /// </summary>
    internal static class NecromancerAuras
    {
        // Erenshor currently ends SpellLine at Aura_Reaver (89). A dedicated
        // runtime value lets a Necromancer aura coexist with every native
        // class aura while still making stronger Necromancer gems supersede
        // weaker ones.
        internal const Spell.SpellLine AuraLine = (Spell.SpellLine)90;
        internal const int ExpectedCount = 5;

        private static readonly NecromancerAuraDefinition[] Definitions =
        {
            new NecromancerAuraDefinition
            {
                Key = "grave_whisper",
                ItemName = "Grave's Whisper",
                SpellName = "Aura: Grave's Whisper",
                Description = "Increase party Intelligence, Charisma, Endurance, and mana regeneration.",
                Lore = "A whisper follows this black tablet from every grave it has passed.",
                Level = 1,
                Price = 5,
                Mana = 5,
                End = 4,
                Int = 4,
                Cha = 4,
                Vr = 0
            },
            new NecromancerAuraDefinition
            {
                Key = "pale_covenant",
                ItemName = "Pale Covenant",
                SpellName = "Aura: Pale Covenant",
                Description = "Increase party Intelligence, Charisma, Endurance, and mana regeneration.",
                Lore = "The living and the dead are equal partners beneath this covenant.",
                Level = 7,
                Price = 1500,
                Mana = 8,
                End = 8,
                Int = 8,
                Cha = 8,
                Vr = 0
            },
            new NecromancerAuraDefinition
            {
                Key = "sepulchral_presence",
                ItemName = "Sepulchral Presence",
                SpellName = "Aura: Sepulchral Presence",
                Description = "Increase party Intelligence, Charisma, Endurance, void resistance, and mana regeneration.",
                Lore = "The stillness of a sealed crypt gathers around its bearer.",
                Level = 14,
                Price = 2500,
                Mana = 12,
                End = 12,
                Int = 15,
                Cha = 12,
                Vr = 4
            },
            new NecromancerAuraDefinition
            {
                Key = "dominion_of_bones",
                ItemName = "Dominion of Bones",
                SpellName = "Aura: Dominion of Bones",
                Description = "Increase party Intelligence, Charisma, Endurance, void resistance, and mana regeneration.",
                Lore = "Even ancient bones remember the authority carved into this tablet.",
                Level = 21,
                Price = 5000,
                Mana = 16,
                End = 16,
                Int = 22,
                Cha = 16,
                Vr = 5
            },
            new NecromancerAuraDefinition
            {
                Key = "sovereign_of_the_dead",
                ItemName = "Sovereign of the Dead",
                SpellName = "Aura: Sovereign of the Dead",
                Description = "Greatly increase party Intelligence, Charisma, Endurance, void resistance, and mana regeneration.",
                Lore = "A silent court of the dead recognizes the bearer of this royal seal.",
                Level = 35,
                Price = 15000,
                Mana = 20,
                End = 20,
                Int = 20,
                Cha = 20,
                Vr = 5
            }
        };

        private static readonly Dictionary<string, Spell> SpellsByKey =
            new Dictionary<string, Spell>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Item> ItemsByKey =
            new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, Sprite> _embeddedIcons;

        internal static bool IsReady { get; private set; }
        internal static IReadOnlyCollection<Spell> AllSpells =>
            SpellsByKey.Values;
        internal static IReadOnlyCollection<Item> AllItems =>
            ItemsByKey.Values;
        internal static Item StarterItem => GetItem("grave_whisper");

        internal static void TryRegister(SpellDB database)
        {
            if (IsReady)
            {
                InjectCurrentSceneVendors();
                return;
            }
            if (database?.SpellDatabase == null ||
                NecromancerCatalog.NecromancerClass == null ||
                !Items.ItemFactory.Initialized)
                return;

            Spell nativeTemplate = database.SpellDatabase.FirstOrDefault(
                spell => spell != null &&
                    spell.Line == Spell.SpellLine.Aura_Arcanist) ??
                database.SpellDatabase.FirstOrDefault(spell => spell != null &&
                    spell.Type == Spell.SpellType.Beneficial);

            SpellsByKey.Clear();
            ItemsByKey.Clear();
            foreach (NecromancerAuraDefinition definition in Definitions)
            {
                Spell spell = CreateSpell(definition, nativeTemplate);
                Item item = CreateItem(definition, spell);
                SpellsByKey.Add(definition.Key, spell);
                ItemsByKey.Add(definition.Key, item);
                Items.ItemFactory.RegisterExternalItem(item.ItemName, item);
            }

            var merged = new List<Spell>(database.SpellDatabase.Length +
                SpellsByKey.Count);
            merged.AddRange(database.SpellDatabase.Where(spell => spell != null &&
                !IsAuraSpellId(spell.Id)));
            merged.AddRange(SpellsByKey.Values);
            database.SpellDatabase = merged.ToArray();
            IsReady = true;
            InjectCurrentSceneVendors();
            SkillsPlugin.Log.LogInfo(
                "Necromancer: registered five unique class aura gems on the " +
                "native caster level, price, and stat budget.");
        }

        private static Spell CreateSpell(NecromancerAuraDefinition definition,
            Spell nativeTemplate)
        {
            var spell = ScriptableObject.CreateInstance<Spell>();
            spell.name = "CS NEC AURA - " + definition.ItemName;
            spell.Id = BuildSpellId(definition.Key);
            spell.UsedBy = new List<Class>
                { NecromancerCatalog.NecromancerClass };
            spell.Type = Spell.SpellType.Beneficial;
            spell.Line = AuraLine;
            spell.RequiredLevel = definition.Level;
            spell.SpellName = definition.SpellName;
            spell.SpellDesc = definition.Description;
            spell.SpellChargeFXIndex = nativeTemplate?.SpellChargeFXIndex ?? 0;
            spell.SpellResolveFXIndex = nativeTemplate?.SpellResolveFXIndex ?? 24;
            spell.SpellChargeTime = nativeTemplate?.SpellChargeTime ?? 60f;
            spell.SpellDurationInTicks = 2;
            spell.StatusEffectMessageOnPlayer =
                "feel the grave's patient strength.";
            spell.StatusEffectMessageOnNPC =
                "feels the grave's patient strength.";
            spell.Mana = definition.Mana;
            spell.End = definition.End;
            spell.Int = definition.Int;
            spell.Cha = definition.Cha;
            spell.VR = definition.Vr;
            spell.SpellRange = 30f;
            spell.CanHitPlayers = true;
            spell.SimUsable = false;
            spell.NoResonate = true;
            spell.ChargeSound = nativeTemplate?.ChargeSound;
            spell.CompleteSound = nativeTemplate?.CompleteSound;
            spell.ChargeVariations = nativeTemplate?.ChargeVariations != null
                ? new List<AudioClip>(nativeTemplate.ChargeVariations)
                : new List<AudioClip>();
            spell.CompleteVariations = nativeTemplate?.CompleteVariations != null
                ? new List<AudioClip>(nativeTemplate.CompleteVariations)
                : new List<AudioClip>();
            spell.SpellIcon = LoadIcon("Necromancer Aura Spell " +
                definition.Key);
            spell.color = new Color(0.36f, 0.12f, 0.48f, 1f);
            spell.hideFlags = HideFlags.HideAndDontSave;
            return spell;
        }

        private static Item CreateItem(NecromancerAuraDefinition definition,
            Spell spell)
        {
            var item = ScriptableObject.CreateInstance<Item>();
            item.name = "CS NEC AURA - " + definition.ItemName;
            item.ItemName = definition.ItemName;
            item.Id = BuildItemId(definition.Key);
            item.ItemLevel = definition.Level;
            item.ItemValue = definition.Price;
            item.ItemIcon = LoadIcon("Necromancer Aura Item " +
                definition.Key);
            item.RequiredSlot = Item.SlotType.Aura;
            item.Classes = new List<Class>
                { NecromancerCatalog.NecromancerClass };
            item.Aura = spell;
            item.Lore = definition.Lore + "\n\n" + definition.Description +
                "\nRequired level: " + definition.Level + ".";
            item.Stackable = false;
            item.Disposable = false;
            item.Unique = false;
            item.PlayerCannotSell = false;
            item.SimPlayersCantGet = false;
            item.hideFlags = HideFlags.HideAndDontSave;
            return item;
        }

        private static Sprite LoadIcon(string name)
        {
            if (_embeddedIcons == null)
                _embeddedIcons = EmbeddedResources.LoadAllIcons();
            if (_embeddedIcons.TryGetValue(name, out Sprite icon) && icon)
            {
                icon.name = name;
                return icon;
            }
            SkillsPlugin.Log.LogError(
                $"Missing required unique Necromancer aura icon '{name}'.");
            return null;
        }

        internal static Item GetItem(string key)
        {
            ItemsByKey.TryGetValue(key ?? "", out Item value);
            return value;
        }

        internal static IEnumerable<Item> GetItemsByLevel()
        {
            return Definitions.Select(definition => GetItem(definition.Key))
                .Where(item => item != null);
        }

        internal static void InjectCurrentSceneVendors()
        {
            if (!IsReady) return;
            foreach (VendorInventory vendor in UnityEngine.Object
                .FindObjectsOfType<VendorInventory>())
                InjectVendor(vendor);
        }

        internal static void InjectVendor(VendorInventory vendor)
        {
            if (!IsReady || vendor == null || !IsStarterAuraMerchant(vendor))
                return;
            if (vendor.ItemsForSale == null)
                vendor.ItemsForSale = new List<Item>();
            Item starter = StarterItem;
            if (starter == null) return;

            // Remove stale hot-load instances and duplicates by deterministic
            // ID, then append exactly one gem after the six native starters.
            vendor.ItemsForSale.RemoveAll(item => item != null &&
                string.Equals(item.Id, starter.Id,
                    StringComparison.OrdinalIgnoreCase));
            vendor.ItemsForSale.Add(starter);
        }

        internal static bool IsStarterAuraMerchant(VendorInventory vendor)
        {
            if (vendor?.ItemsForSale == null ||
                !string.Equals((vendor.VendorDesc ?? "").Trim(), "Aura Gem",
                    StringComparison.OrdinalIgnoreCase))
                return false;
            string[] nativeStarters =
            {
                "Spark of Light", "Sprout", "Dawn's Light",
                "Scent of the Sea", "Rolling Clouds", "Falling Shadow"
            };
            int matches = nativeStarters.Count(name => vendor.ItemsForSale.Any(
                item => item != null && string.Equals(item.ItemName, name,
                    StringComparison.OrdinalIgnoreCase)));
            return matches >= 4;
        }

        internal static bool ValidateStarterMerchant(out string detail)
        {
            string scene = UnityEngine.SceneManagement.SceneManager
                .GetActiveScene().name;
            if (!string.Equals(scene, "Stowaway",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(scene, "Willowwatch",
                    StringComparison.OrdinalIgnoreCase))
            {
                detail = $"scene {scene} has no starter aura merchant";
                return true;
            }

            VendorInventory[] vendors = UnityEngine.Object
                .FindObjectsOfType<VendorInventory>()
                .Where(IsStarterAuraMerchant).ToArray();
            int exactStock = vendors.Sum(vendor => vendor.ItemsForSale.Count(
                item => item != null && string.Equals(item.Id,
                    StarterItem?.Id, StringComparison.OrdinalIgnoreCase)));
            string names = string.Join(", ", vendors.Select(vendor =>
                vendor.GetComponent<NPC>()?.NPCName ?? vendor.transform.name));
            bool valid = vendors.Length == 1 && exactStock == 1;
            detail = valid
                ? $"{names} stocks exactly one {StarterItem.ItemName} beside the six native starter auras"
                : $"expected one starter aura merchant and one Necromancer gem; merchants={vendors.Length}, stock={exactStock}";
            return valid;
        }

        internal static bool ValidateCatalog(out string detail)
        {
            var errors = new List<string>();
            if (!IsReady) errors.Add("aura catalog not ready");
            if (SpellsByKey.Count != ExpectedCount)
                errors.Add($"spell count {SpellsByKey.Count}");
            if (ItemsByKey.Count != ExpectedCount)
                errors.Add($"item count {ItemsByKey.Count}");

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var icons = new HashSet<int>();
            Spell[] database = GameData.SpellDatabase?.SpellDatabase ??
                Array.Empty<Spell>();
            foreach (NecromancerAuraDefinition definition in Definitions)
            {
                Spell spell = SpellsByKey.TryGetValue(definition.Key,
                    out Spell foundSpell) ? foundSpell : null;
                Item item = ItemsByKey.TryGetValue(definition.Key,
                    out Item foundItem) ? foundItem : null;
                if (spell == null || item == null)
                {
                    errors.Add("missing " + definition.ItemName);
                    continue;
                }
                if (!names.Add(spell.SpellName) || !names.Add(item.ItemName))
                    errors.Add("duplicate name " + definition.ItemName);
                if (!ids.Add(spell.Id) || !ids.Add(item.Id))
                    errors.Add("duplicate ID " + definition.ItemName);
                if (!spell.SpellIcon || !icons.Add(spell.SpellIcon.GetInstanceID()))
                    errors.Add("missing/duplicate effect icon " +
                        definition.ItemName);
                if (!item.ItemIcon || !icons.Add(item.ItemIcon.GetInstanceID()))
                    errors.Add("missing/duplicate gem icon " +
                        definition.ItemName);
                if (spell.SpellIcon && item.ItemIcon &&
                    ReferenceEquals(spell.SpellIcon, item.ItemIcon))
                    errors.Add("gem reuses effect icon " +
                        definition.ItemName);
                if (spell.Line != AuraLine ||
                    spell.RequiredLevel != definition.Level ||
                    spell.Mana != definition.Mana ||
                    spell.End != definition.End ||
                    spell.Int != definition.Int ||
                    spell.Cha != definition.Cha ||
                    spell.VR != definition.Vr)
                    errors.Add("stat/level budget " + definition.ItemName);
                if (spell.UsedBy == null || spell.UsedBy.Count != 1 ||
                    !spell.UsedBy.Contains(
                        NecromancerCatalog.NecromancerClass))
                    errors.Add("spell class restriction " +
                        definition.ItemName);
                if (item.RequiredSlot != Item.SlotType.Aura ||
                    item.ItemLevel != definition.Level ||
                    item.ItemValue != definition.Price ||
                    item.Classes == null || item.Classes.Count != 1 ||
                    !item.Classes.Contains(
                        NecromancerCatalog.NecromancerClass) ||
                    !ReferenceEquals(item.Aura, spell))
                    errors.Add("item contract " + definition.ItemName);
                if (!ReferenceEquals(Items.ItemFactory.GetItemById(item.Id),
                    item) || !ReferenceEquals(GameData.ItemDB?.GetItemByID(
                        item.Id), item))
                    errors.Add("persistent item resolution " +
                        definition.ItemName);
                if (database.Count(candidate => candidate != null &&
                    string.Equals(candidate.Id, spell.Id,
                        StringComparison.OrdinalIgnoreCase)) != 1)
                    errors.Add("spell database resolution " +
                        definition.ItemName);
            }

            detail = errors.Count == 0
                ? "five uniquely named aura gems, five distinct effect icons, exact native caster budgets, class restrictions, and deterministic item/spell IDs"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        /// <summary>
        /// Equip every gem through the native aura slot, verify the worn
        /// status, then save the level-35 gem through Erenshor's ordinary
        /// character save field. This is restricted by the caller to the
        /// isolated John EverQuest QA character.
        /// </summary>
        internal static bool ExerciseEquipmentAndSave(out string detail)
        {
            var errors = new List<string>();
            Inventory inventory = GameData.PlayerInv;
            Stats stats = GameData.PlayerStats;
            ItemIcon slot = inventory?.AuraSlot;
            if (inventory == null || stats == null || slot == null ||
                GameData.CurrentCharacterSlot == null || GameData.GM == null)
            {
                detail = "player aura slot or save state is unavailable";
                return false;
            }

            foreach (Item item in GetItemsByLevel())
            {
                ClearCustomAuraStatuses(stats);
                stats.MyAura = null;
                slot.MyItem = item;
                slot.Quantity = 1;
                slot.UpdateSlotImage();
                stats.CheckAuras();
                bool worn = ReferenceEquals(stats.MyAura, item.Aura) &&
                    stats.CheckForStatus(item.Aura);
                if (!worn) errors.Add("equip/status " + item.ItemName);
            }

            Item final = GetItemsByLevel().LastOrDefault();
            inventory.UpdatePlayerInventory();
            GameData.GM.SaveGameData(wScene: false);
            if (final == null || !ReferenceEquals(slot.MyItem, final) ||
                !string.Equals(GameData.CurrentCharacterSlot.AuraItem,
                    final.Id, StringComparison.OrdinalIgnoreCase))
                errors.Add("native AuraItem save field");
            if (!ReferenceEquals(GameData.ItemDB.GetItemByID(
                    GameData.CurrentCharacterSlot.AuraItem), final))
                errors.Add("saved ID reload resolution");

            detail = errors.Count == 0
                ? $"all {ExpectedCount} gems equipped in Aura slot with active status; {final.ItemName} saved and resolved as {final.Id}"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        internal static bool ValidateEquippedPersistence(out string detail)
        {
            Item expected = GetItemsByLevel().LastOrDefault();
            Item equipped = GameData.PlayerInv?.AuraSlot?.MyItem;
            string savedId = GameData.CurrentCharacterSlot?.AuraItem;
            bool valid = expected != null && ReferenceEquals(equipped, expected) &&
                string.Equals(savedId, expected.Id,
                    StringComparison.OrdinalIgnoreCase) &&
                ReferenceEquals(GameData.ItemDB?.GetItemByID(savedId), expected);
            detail = valid
                ? $"reloaded {expected.ItemName} from deterministic ID {savedId} in the native Aura slot"
                : $"expected {expected?.Id ?? "<missing>"}; saved={savedId ?? "<null>"}; equipped={equipped?.Id ?? "<null>"}";
            return valid;
        }

        private static void ClearCustomAuraStatuses(Stats stats)
        {
            if (stats?.StatusEffects == null) return;
            for (int index = stats.StatusEffects.Length - 1;
                index >= 0; index--)
            {
                Spell effect = stats.StatusEffects[index]?.Effect;
                if (effect != null && IsAuraSpellId(effect.Id))
                    stats.RemoveStatusEffect(index);
            }
        }

        internal static string BuildSpellId(string key) =>
            "CS_NECRO_AURA_EFFECT_" + NormalizeKey(key);
        internal static string BuildItemId(string key) =>
            "CS_NECRO_AURA_ITEM_" + NormalizeKey(key);
        private static bool IsAuraSpellId(string id) =>
            !string.IsNullOrEmpty(id) && id.StartsWith(
                "CS_NECRO_AURA_EFFECT_", StringComparison.OrdinalIgnoreCase);
        private static string NormalizeKey(string key) =>
            new string((key ?? "").ToUpperInvariant()
                .Where(character => char.IsLetterOrDigit(character) ||
                    character == '_').ToArray());
    }

    [HarmonyPatch(typeof(VendorInventory), "Start")]
    internal static class Patch_NecromancerStarterAuraMerchant
    {
        private static void Postfix(VendorInventory __instance)
        {
            NecromancerCatalog.TryInitialize();
            NecromancerAuras.InjectVendor(__instance);
        }
    }
}
