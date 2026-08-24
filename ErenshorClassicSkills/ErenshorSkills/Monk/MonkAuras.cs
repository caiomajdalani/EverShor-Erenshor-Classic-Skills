using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills.Monk
{
    internal static class MonkAuras
    {
        internal const Spell.SpellLine AuraLine = (Spell.SpellLine)91;
        internal const int ExpectedCount = 5;

        private sealed class Definition
        {
            internal readonly string Key;
            internal readonly string ItemName;
            internal readonly string Lore;

            internal Definition(string key, string itemName, string lore)
            {
                Key = key;
                ItemName = itemName;
                Lore = lore;
            }
        }

        private static readonly Definition[] Definitions =
        {
            new Definition("stillwater_breath", "Stillwater Breath",
                "A smooth teal stone that seems to breathe with its bearer."),
            new Definition("flowing_reed", "Flowing Reed",
                "A reed bends inside this gem without ever breaking."),
            new Definition("iron_body", "Iron Body",
                "The discipline of stone and sinew is sealed inside."),
            new Definition("path_of_the_gale", "Path of the Gale",
                "Wind circles the tablet even in a sealed room."),
            new Definition("grandmasters_presence", "Grandmaster's Presence",
                "A lifetime of disciplined motion rests in perfect stillness.")
        };

        private static readonly Dictionary<string, Spell> SpellsByKey =
            new Dictionary<string, Spell>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Item> ItemsByKey =
            new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Spell> TemplatesByKey =
            new Dictionary<string, Spell>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, Sprite> _embeddedIcons;

        internal static bool IsReady { get; private set; }
        internal static IReadOnlyCollection<Spell> AllSpells =>
            SpellsByKey.Values;
        internal static IReadOnlyCollection<Item> AllItems =>
            ItemsByKey.Values;
        internal static Item StarterItem => GetItem("stillwater_breath");

        internal static void TryRegister(SpellDB database)
        {
            if (IsReady)
            {
                InjectCurrentSceneVendors();
                return;
            }
            if (database?.SpellDatabase == null ||
                MonkCatalog.MonkClass == null ||
                !Items.ItemFactory.Initialized)
                return;

            Spell[] templates = database.SpellDatabase
                .Where(spell => spell != null &&
                    spell.Line == Spell.SpellLine.Aura_Duelist)
                .OrderBy(spell => spell.RequiredLevel)
                .ThenBy(spell => spell.SpellName,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (templates.Length != ExpectedCount)
            {
                SkillsPlugin.Log?.LogWarning(
                    $"Monk auras are waiting for five native Duelist aura templates; found {templates.Length}.");
                return;
            }

            SpellsByKey.Clear();
            ItemsByKey.Clear();
            TemplatesByKey.Clear();
            for (int index = 0; index < Definitions.Length; index++)
            {
                Definition definition = Definitions[index];
                Spell template = templates[index];
                Item sourceItem = GameData.ItemDB?.ItemDB?.FirstOrDefault(item =>
                    item != null && ReferenceEquals(item.Aura, template));
                Spell spell = CreateSpell(definition, template);
                Item item = CreateItem(definition, spell, sourceItem);
                SpellsByKey[definition.Key] = spell;
                ItemsByKey[definition.Key] = item;
                TemplatesByKey[definition.Key] = template;
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
            SkillsPlugin.Log?.LogInfo(
                "Monk: registered five unique auras mirrored exactly from the native Duelist aura budgets.");
        }

        private static Spell CreateSpell(Definition definition,
            Spell template)
        {
            var spell = ScriptableObject.CreateInstance<Spell>();
            spell.name = "CS MONK AURA - " + definition.ItemName;
            spell.Id = BuildSpellId(definition.Key);
            spell.UsedBy = new List<Class> { MonkCatalog.MonkClass };
            spell.Type = Spell.SpellType.Beneficial;
            spell.Line = AuraLine;
            spell.RequiredLevel = template.RequiredLevel;
            spell.SpellName = "Aura: " + definition.ItemName;
            spell.SpellDesc =
                "Increase party Dexterity, Agility, and attack speed with the exact power budget of the corresponding native martial aura.";
            spell.SpellChargeFXIndex = template.SpellChargeFXIndex;
            spell.SpellResolveFXIndex = template.SpellResolveFXIndex;
            spell.SpellChargeTime = template.SpellChargeTime;
            spell.SpellDurationInTicks = template.SpellDurationInTicks;
            spell.StatusEffectMessageOnPlayer =
                "move with a Monk's disciplined rhythm.";
            spell.StatusEffectMessageOnNPC =
                "moves with a Monk's disciplined rhythm.";
            CopyBudget(spell, template);
            spell.SpellRange = template.SpellRange;
            spell.CanHitPlayers = true;
            spell.SimUsable = false;
            spell.NoResonate = true;
            spell.ChargeSound = template.ChargeSound;
            spell.CompleteSound = template.CompleteSound;
            spell.ChargeVariations = template.ChargeVariations != null
                ? new List<AudioClip>(template.ChargeVariations)
                : new List<AudioClip>();
            spell.CompleteVariations = template.CompleteVariations != null
                ? new List<AudioClip>(template.CompleteVariations)
                : new List<AudioClip>();
            spell.SpellIcon = LoadIcon("Monk Aura Spell " + definition.Key);
            spell.color = new Color(0.10f, 0.60f, 0.52f, 1f);
            spell.hideFlags = HideFlags.HideAndDontSave;
            return spell;
        }

        private static void CopyBudget(Spell target, Spell source)
        {
            target.HP = source.HP;
            target.AC = source.AC;
            target.Mana = source.Mana;
            target.Str = source.Str;
            target.Dex = source.Dex;
            target.End = source.End;
            target.Agi = source.Agi;
            target.Int = source.Int;
            target.Wis = source.Wis;
            target.Cha = source.Cha;
            target.MR = source.MR;
            target.ER = source.ER;
            target.PR = source.PR;
            target.VR = source.VR;
            target.Haste = source.Haste;
            target.MovementSpeed = source.MovementSpeed;
            target.AtkRollModifier = source.AtkRollModifier;
            target.DamageShield = source.DamageShield;
        }

        private static Item CreateItem(Definition definition, Spell spell,
            Item sourceItem)
        {
            var item = ScriptableObject.CreateInstance<Item>();
            item.name = "CS MONK AURA - " + definition.ItemName;
            item.Id = BuildItemId(definition.Key);
            item.ItemName = definition.ItemName;
            item.ItemLevel = spell.RequiredLevel;
            item.ItemValue = Mathf.Max(1, sourceItem?.ItemValue ??
                Mathf.Max(5, spell.RequiredLevel * spell.RequiredLevel * 10));
            item.ItemIcon = LoadIcon("Monk Aura Item " + definition.Key);
            item.RequiredSlot = Item.SlotType.Aura;
            item.Classes = new List<Class> { MonkCatalog.MonkClass };
            item.Aura = spell;
            item.Lore = definition.Lore + "\n\n" + spell.SpellDesc +
                "\nRequired level: " + spell.RequiredLevel + ".";
            item.Stackable = false;
            item.Disposable = false;
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
            SkillsPlugin.Log?.LogError(
                $"Missing required unique Monk aura icon '{name}'.");
            return null;
        }

        internal static Item GetItem(string key)
        {
            ItemsByKey.TryGetValue(key ?? "", out Item item);
            return item;
        }

        internal static IEnumerable<Item> GetItemsByLevel() =>
            Definitions.Select(value => GetItem(value.Key))
                .Where(item => item != null);

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
            vendor.ItemsForSale.RemoveAll(item => item != null &&
                string.Equals(item.Id, starter?.Id,
                    StringComparison.OrdinalIgnoreCase));
            if (starter != null) vendor.ItemsForSale.Add(starter);
        }

        internal static bool IsStarterAuraMerchant(VendorInventory vendor)
        {
            if (vendor?.ItemsForSale == null ||
                !string.Equals((vendor.VendorDesc ?? "").Trim(), "Aura Gem",
                    StringComparison.OrdinalIgnoreCase))
                return false;
            string[] starters =
            {
                "Spark of Light", "Sprout", "Dawn's Light",
                "Scent of the Sea", "Rolling Clouds", "Falling Shadow"
            };
            return starters.Count(name => vendor.ItemsForSale.Any(item =>
                item != null && string.Equals(item.ItemName, name,
                    StringComparison.OrdinalIgnoreCase))) >= 4;
        }

        internal static bool ValidateCatalog(out string detail)
        {
            var errors = new List<string>();
            if (!IsReady || SpellsByKey.Count != ExpectedCount ||
                ItemsByKey.Count != ExpectedCount)
                errors.Add("catalog count");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var icons = new HashSet<int>();
            foreach (Definition definition in Definitions)
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
                if (!ids.Add(spell.Id) || !ids.Add(item.Id))
                    errors.Add("duplicate ID " + definition.ItemName);
                if (!spell.SpellIcon ||
                    !icons.Add(spell.SpellIcon.GetInstanceID()))
                    errors.Add("effect icon " + definition.ItemName);
                if (!item.ItemIcon ||
                    !icons.Add(item.ItemIcon.GetInstanceID()))
                    errors.Add("item icon " + definition.ItemName);
                if (ReferenceEquals(spell.SpellIcon, item.ItemIcon))
                    errors.Add("reused effect/item icon " +
                        definition.ItemName);
                if (spell.Line != AuraLine || spell.UsedBy == null ||
                    spell.UsedBy.Count != 1 ||
                    !spell.UsedBy.Contains(MonkCatalog.MonkClass) ||
                    item.Classes == null || item.Classes.Count != 1 ||
                    !item.Classes.Contains(MonkCatalog.MonkClass) ||
                    !ReferenceEquals(item.Aura, spell))
                    errors.Add("class/item contract " +
                        definition.ItemName);
                if (!ReferenceEquals(
                    Items.ItemFactory.GetItemById(item.Id), item))
                    errors.Add("save resolver " + definition.ItemName);
            }
            if (!ValidateNativeBudgetMirror(out string budgetDetail))
                errors.Add(budgetDetail);
            detail = errors.Count == 0
                ? "five unique aura gems/effects with Monk-only restrictions, deterministic IDs, and " + budgetDetail
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        internal static bool ValidateNativeBudgetMirror(out string detail)
        {
            var errors = new List<string>();
            foreach (Definition definition in Definitions)
            {
                Spell spell = SpellsByKey.TryGetValue(definition.Key,
                    out Spell found) ? found : null;
                Spell template = TemplatesByKey.TryGetValue(definition.Key,
                    out Spell source) ? source : null;
                if (spell == null || template == null ||
                    spell.RequiredLevel != template.RequiredLevel ||
                    spell.HP != template.HP || spell.AC != template.AC ||
                    spell.Mana != template.Mana ||
                    spell.Str != template.Str || spell.Dex != template.Dex ||
                    spell.End != template.End || spell.Agi != template.Agi ||
                    spell.Int != template.Int || spell.Wis != template.Wis ||
                    spell.Cha != template.Cha || spell.MR != template.MR ||
                    spell.ER != template.ER || spell.PR != template.PR ||
                    spell.VR != template.VR ||
                    !Mathf.Approximately(spell.Haste, template.Haste) ||
                    !Mathf.Approximately(spell.MovementSpeed,
                        template.MovementSpeed))
                    errors.Add(definition.ItemName);
            }
            detail = errors.Count == 0
                ? "all five stat, haste, resistance, movement, and level budgets exactly mirror native Duelist auras"
                : "native aura budget mismatch: " + string.Join(", ", errors);
            return errors.Count == 0;
        }

        /// <summary>
        /// Exercises only the five newly added Monk aura gems through the
        /// native Aura slot, then saves the final gem through Erenshor's
        /// ordinary character field for a restart verification.
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
                ? $"all {ExpectedCount} new Monk gems equipped in the Aura " +
                  $"slot with active status; {final.ItemName} saved and " +
                  $"resolved as {final.Id}"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        internal static bool ValidateEquippedPersistence(out string detail)
        {
            Item expected = GetItemsByLevel().LastOrDefault();
            Item equipped = GameData.PlayerInv?.AuraSlot?.MyItem;
            string savedId = GameData.CurrentCharacterSlot?.AuraItem;
            bool valid = expected != null &&
                ReferenceEquals(equipped, expected) &&
                string.Equals(savedId, expected.Id,
                    StringComparison.OrdinalIgnoreCase) &&
                ReferenceEquals(GameData.ItemDB?.GetItemByID(savedId),
                    expected);
            detail = valid
                ? $"reloaded {expected.ItemName} from deterministic ID " +
                  $"{savedId} in the native Aura slot"
                : $"expected {expected?.Id ?? "<missing>"}; saved=" +
                  $"{savedId ?? "<null>"}; equipped=" +
                  $"{equipped?.Id ?? "<null>"}";
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
            "CS_MONK_AURA_EFFECT_" + NormalizeKey(key);
        internal static string BuildItemId(string key) =>
            "CS_MONK_AURA_ITEM_" + NormalizeKey(key);
        private static bool IsAuraSpellId(string id) =>
            !string.IsNullOrEmpty(id) &&
            id.StartsWith("CS_MONK_AURA_EFFECT_",
                StringComparison.OrdinalIgnoreCase);
        private static string NormalizeKey(string key) =>
            new string((key ?? "").ToUpperInvariant().Where(character =>
                char.IsLetterOrDigit(character) || character == '_').ToArray());
    }

    [HarmonyPatch(typeof(VendorInventory), "Start")]
    internal static class Patch_MonkStarterAuraMerchant
    {
        private static void Postfix(VendorInventory __instance)
        {
            MonkCatalog.TryInitialize();
            MonkAuras.InjectVendor(__instance);
        }
    }
}
