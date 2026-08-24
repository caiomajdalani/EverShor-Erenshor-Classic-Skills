using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ErenshorSkills.Necromancer
{
    /// <summary>
    /// Compact, native-budget Necromancer item progression.  Native classes
    /// share most armor, so these are identity sidegrades rather than a second
    /// full loot ladder.  The charm uses Erenshor's real world-drop mold and
    /// Smithing.Template path; every saved item resolves through a stable ID.
    /// </summary>
    public static class NecromancerGear
    {
        internal const string CowlKey = "grave_initiate_cowl";
        internal const string ShroudKey = "bone_reader_shroud";
        internal const string ScepterKey = "ossuary_scepter";
        internal const string CharmKey = "necromancer_charm";
        internal const string MoldKey = "necromancer_charm_mold";

        internal const int ExpectedCount = 5;

        private enum DropTier
        {
            Uncommon,
            Rare
        }

        private sealed class DropRule
        {
            internal readonly string NpcName;
            internal readonly string ItemKey;
            internal readonly DropTier Tier;

            internal DropRule(string npcName, string itemKey, DropTier tier)
            {
                NpcName = npcName;
                ItemKey = itemKey;
                Tier = tier;
            }
        }

        private static readonly DropRule[] DropRules =
        {
            new DropRule("Neophyte Necromancer", CowlKey,
                DropTier.Uncommon),
            new DropRule("Crypt Keeper", ShroudKey, DropTier.Rare),
            new DropRule("Boneweaver", ShroudKey, DropTier.Rare),
            // Brax's Candle is already a rare drop from these exact level-29
            // casters.  Sharing that source gives the no-proc defensive
            // sidegrade the same acquisition difficulty and level timing.
            new DropRule("Braxonian Tempest", ScepterKey, DropTier.Rare),
            new DropRule("Braxonian Ascended", ScepterKey, DropTier.Rare)
        };

        private static readonly Dictionary<string, Item> ItemsByKey =
            new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, Sprite> _embeddedIcons;

        internal static bool IsReady { get; private set; }
        internal static IReadOnlyCollection<Item> AllItems =>
            ItemsByKey.Values;
        internal static IEnumerable<Item> EquippableItems =>
            ItemsByKey.Values.Where(item => item != null &&
                item.RequiredSlot != Item.SlotType.General);
        internal static Item CharmItem => GetItem(CharmKey);
        internal static Item MoldItem => GetItem(MoldKey);

        private sealed class EquippedTestItem
        {
            internal readonly Item Item;
            internal readonly ItemIcon Slot;
            internal readonly int Quality;

            internal EquippedTestItem(Item item, ItemIcon slot, int quality)
            {
                Item = item;
                Slot = slot;
                Quality = quality;
            }
        }

        internal static void TryRegister()
        {
            if (IsReady)
            {
                InjectWorldDropMold();
                InjectCurrentSceneLootTables();
                return;
            }
            if (!Items.ItemFactory.Initialized ||
                NecromancerCatalog.NecromancerClass == null ||
                GameData.ItemDB?.ItemDB == null ||
                GameData.ItemDB.ItemDB.Length == 0)
                return;

            Item clothHood = FindNativeItem("Cloth Hood");
            Item braxonianWrap = FindNativeItem("Braxonian Wrap");
            Item spectralScepter = FindNativeItem("Spectral Sceptre");
            Item ancientCoal = FindNativeItem("Ancient Coal");
            Item citrine = FindNativeItem("Chunk of Citrine Sandsilver");
            Item braxonianOre = FindNativeItem("Braxonian Ore");
            Item luminstone = FindNativeItem("Luminstone");
            if (clothHood == null || braxonianWrap == null ||
                spectralScepter == null || ancientCoal == null ||
                citrine == null || braxonianOre == null || luminstone == null)
            {
                SkillsPlugin.Log.LogWarning(
                    "Necromancer gear is waiting for its native visual or " +
                    "charm-crafting templates.");
                return;
            }

            Sprite cowlIcon = LoadIcon("Necromancer Gear " + CowlKey);
            Sprite shroudIcon = LoadIcon("Necromancer Gear " + ShroudKey);
            Sprite scepterIcon = LoadIcon("Necromancer Gear " + ScepterKey);
            Sprite charmIcon = LoadIcon("Necromancer Gear " + CharmKey);
            Sprite moldIcon = LoadIcon("Necromancer Gear " + MoldKey);
            if (!cowlIcon || !shroudIcon || !scepterIcon || !charmIcon ||
                !moldIcon)
                return;

            ItemsByKey.Clear();
            Item cowl = CreateCowl(clothHood, cowlIcon);
            Item shroud = CreateShroud(braxonianWrap, shroudIcon);
            Item scepter = CreateScepter(spectralScepter, scepterIcon);
            Item charm = CreateCharm(charmIcon);
            Item mold = CreateCharmMold(moldIcon, charm,
                ancientCoal, citrine, braxonianOre, luminstone);

            Register(CowlKey, cowl);
            Register(ShroudKey, shroud);
            Register(ScepterKey, scepter);
            Register(CharmKey, charm);
            Register(MoldKey, mold);

            IsReady = ItemsByKey.Count == ExpectedCount;
            if (!IsReady) return;
            InjectWorldDropMold();
            InjectCurrentSceneLootTables();
            SkillsPlugin.Log.LogInfo(
                "Necromancer: registered four native-budget class gear " +
                "sidegrades and one world-drop charm mold.");
        }

        private static Item CreateCowl(Item visual, Sprite icon)
        {
            Item item = CreateBase(CowlKey, "Grave Initiate's Cowl", 7,
                255, Item.SlotType.Head, icon,
                "A novice grave-reader's hood, darkened by years of incense " +
                "and whispered vigils.");
            item.Mana = 30;
            item.End = 5;
            item.Int = 5;
            item.VR = 5;
            CopyNativeVisual(item, visual);
            item.EquipmentToActivate = "ClothHood";
            item.HideHairWhenEquipped = true;
            ApplyNecromancerPalette(item);
            return item;
        }

        private static Item CreateShroud(Item visual, Sprite icon)
        {
            Item item = CreateBase(ShroudKey, "Bone Reader's Shroud", 18,
                2200, Item.SlotType.Chest, icon,
                "Layered funeral cloth worn by those trusted to read the " +
                "last secrets held in bone.");
            // Same primary-stat, resistance, AC, and HP+mana budget as the
            // level-19 Braxonian Wrap.  WIS and elemental spread become END
            // and void defense, while 15 mana becomes class survivability.
            item.HP = 20;
            item.AC = 10;
            item.Mana = 70;
            item.End = 7;
            item.Int = 7;
            item.Cha = 7;
            item.Res = 1;
            item.VR = 3;
            CopyNativeVisual(item, visual);
            item.EquipmentToActivate = "LeatherShirt5";
            ApplyNecromancerPalette(item);
            return item;
        }

        private static Item CreateScepter(Item visual, Sprite icon)
        {
            Item item = CreateBase(ScepterKey, "Ossuary Scepter", 29,
                12855, Item.SlotType.PrimaryOrSecondary, icon,
                "A blackened ritual focus assembled around a polished relic " +
                "bone.  Its quiet strength favors survival over spectacle.");
            item.HP = 100;
            item.Mana = 45;
            item.WeaponDmg = 16;
            item.WeaponDly = 2f;
            item.End = 8;
            item.Int = 10;
            item.Cha = 10;
            item.Res = 1;
            item.VR = 2;
            item.ThisWeaponType = Item.WeaponType.OneHandMelee;
            item.IsWand = true;
            item.WandEffect = null;
            item.WandProcChance = 0f;
            item.WandBoltColor = new Color(0.48f, 0.20f, 0.68f, 1f);
            item.WandRange = 15;
            item.WandBoltSpeed = 45f;
            item.Relic = true;
            CopyNativeVisual(item, visual);
            item.EquipmentToActivate = "SpectralSceptre";
            // Copying the native visual also copies harmless audio, but never
            // copy its spell proc: the item must not move the DPS benchmark.
            item.WandEffect = null;
            item.WandProcChance = 0f;
            ApplyNecromancerPalette(item);
            return item;
        }

        private static Item CreateCharm(Sprite icon)
        {
            Item item = CreateBase(CharmKey, "Necromancer's Charm", 30,
                6000, Item.SlotType.Charm, icon,
                "An obsidian relic that rewards practiced intellect and the " +
                "endurance required to bargain with death.");
            // Native level-30 class charms spend six scaling points.  This is
            // the defensive INT/END sidegrade to Arcanist's INT/CHA charm.
            item.IntScaling = 3f;
            item.EndScaling = 3f;
            item.Relic = true;
            return item;
        }

        private static Item CreateCharmMold(Sprite icon, Item charm,
            params Item[] ingredients)
        {
            var item = ScriptableObject.CreateInstance<Item>();
            item.name = "CS NECRO GEAR - Mold: Necromancer's Charm";
            item.Id = BuildId(MoldKey);
            item.ItemName = "Mold: Necromancer's Charm";
            item.ItemLevel = 0;
            // Below the finished charm's value, but positive so duplicate
            // world drops can pass Erenshor's native vendor gate.
            item.ItemValue = 1200;
            item.ItemIcon = icon;
            item.RequiredSlot = Item.SlotType.General;
            item.Classes = new List<Class>();
            item.Lore = "Ingredients:\n\nAncient Coal\n" +
                "Chunk of Citrine Sandsilver\nBraxonian Ore\nLuminstone\n\n" +
                "One-time usable in a forge. Fuel quality does not affect " +
                "the resulting charm.";
            item.Stackable = true;
            item.Disposable = false;
            item.Unique = false;
            item.Template = true;
            item.TemplateIngredients = new List<Item>(ingredients);
            item.TemplateRewards = new List<Item> { charm };
            item.SimPlayersCantGet = false;
            item.PlayerCannotSell = false;
            item.hideFlags = HideFlags.HideAndDontSave;
            return item;
        }

        private static Item CreateBase(string key, string itemName, int level,
            int value, Item.SlotType slot, Sprite icon, string lore)
        {
            var item = ScriptableObject.CreateInstance<Item>();
            item.name = "CS NECRO GEAR - " + itemName;
            item.Id = BuildId(key);
            item.ItemName = itemName;
            item.ItemLevel = level;
            item.ItemValue = value;
            item.ItemIcon = icon;
            item.RequiredSlot = slot;
            item.Classes = new List<Class>
                { NecromancerCatalog.NecromancerClass };
            item.Lore = lore;
            item.Stackable = false;
            item.Disposable = false;
            item.Unique = false;
            item.Template = false;
            item.TemplateIngredients = new List<Item>();
            item.TemplateRewards = new List<Item>();
            item.SimPlayersCantGet = false;
            item.PlayerCannotSell = false;
            item.hideFlags = HideFlags.HideAndDontSave;
            return item;
        }

        private static void CopyNativeVisual(Item target, Item source)
        {
            if (target == null || source == null) return;
            target.EquipmentToActivate = source.EquipmentToActivate;
            target.ShoulderTrimL = source.ShoulderTrimL;
            target.ShoulderTrimR = source.ShoulderTrimR;
            target.ElbowTrimL = source.ElbowTrimL;
            target.ElbowTrimR = source.ElbowTrimR;
            target.KneeTrimL = source.KneeTrimL;
            target.KneeTrimR = source.KneeTrimR;
            target.ItemPrimaryColor = source.ItemPrimaryColor;
            target.ItemSecondaryColor = source.ItemSecondaryColor;
            target.ItemMetalPrimary = source.ItemMetalPrimary;
            target.ItemLeatherPrimary = source.ItemLeatherPrimary;
            target.ItemMetalDark = source.ItemMetalDark;
            target.ItemMetalSecondary = source.ItemMetalSecondary;
            target.ItemLeatherSecondary = source.ItemLeatherSecondary;
            target.AttackSound = source.AttackSound;
            target.WandAttackSound = source.WandAttackSound;
            target.HideHairWhenEquipped = source.HideHairWhenEquipped;
            target.HideHeadWhenEquipped = source.HideHeadWhenEquipped;
        }

        private static void ApplyNecromancerPalette(Item item)
        {
            item.ItemPrimaryColor = new Color(0.10f, 0.08f, 0.14f, 1f);
            item.ItemSecondaryColor = new Color(0.25f, 0.13f, 0.33f, 1f);
            item.ItemMetalPrimary = new Color(0.48f, 0.45f, 0.52f, 1f);
            item.ItemMetalDark = new Color(0.08f, 0.07f, 0.10f, 1f);
            item.ItemMetalSecondary = new Color(0.28f, 0.24f, 0.32f, 1f);
            item.ItemLeatherPrimary = new Color(0.15f, 0.11f, 0.17f, 1f);
            item.ItemLeatherSecondary = new Color(0.23f, 0.17f, 0.25f, 1f);
        }

        private static void Register(string key, Item item)
        {
            if (item == null) return;
            ItemsByKey[key] = item;
            Items.ItemFactory.RegisterExternalItem(item.ItemName, item);
        }

        private static Item FindNativeItem(string itemName)
        {
            return GameData.ItemDB?.ItemDB?.FirstOrDefault(item =>
                item != null && string.Equals(item.ItemName, itemName,
                    StringComparison.OrdinalIgnoreCase));
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
                $"Missing required unique Necromancer gear icon '{name}'.");
            return null;
        }

        internal static Item GetItem(string key)
        {
            ItemsByKey.TryGetValue(key ?? "", out Item item);
            return item;
        }

        private static string BuildId(string key)
        {
            string normalized = new string((key ?? "").ToUpperInvariant()
                .Where(character => char.IsLetterOrDigit(character) ||
                    character == '_').ToArray());
            return "CS_NECRO_GEAR_" + normalized;
        }

        internal static void InjectWorldDropMold()
        {
            Item mold = MoldItem;
            List<Item> molds = GameData.GM?.WorldDropMolds;
            if (mold == null || molds == null) return;
            molds.RemoveAll(item => item != null &&
                !ReferenceEquals(item, mold) &&
                string.Equals(item.Id, mold.Id,
                    StringComparison.OrdinalIgnoreCase));
            if (!molds.Contains(mold)) molds.Add(mold);
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
                Item item = GetItem(rule.ItemKey);
                List<Item> tier = GetOrCreateTier(table, rule.Tier);
                if (item == null || tier == null) continue;
                tier.RemoveAll(candidate => candidate != null &&
                    !ReferenceEquals(candidate, item) &&
                    string.Equals(candidate.Id, item.Id,
                        StringComparison.OrdinalIgnoreCase));
                if (!tier.Contains(item)) tier.Add(item);
            }
        }

        private static List<Item> GetOrCreateTier(LootTable table,
            DropTier tier)
        {
            switch (tier)
            {
                case DropTier.Uncommon:
                    return table.UncommonDrop ??
                        (table.UncommonDrop = new List<Item>());
                case DropTier.Rare:
                    return table.RareDrop ??
                        (table.RareDrop = new List<Item>());
                default:
                    return null;
            }
        }

        internal static bool RunAcquisitionSelfTest(out string detail)
        {
            if (!IsReady)
            {
                detail = "gear catalog is not ready";
                return false;
            }
            var errors = new List<string>();
            GameObject probe = null;
            try
            {
                probe = new GameObject("CS Necromancer Gear Loot Probe");
                LootTable table = probe.AddComponent<LootTable>();
                table.GuaranteeOneDrop = new List<Item>();
                table.CommonDrop = new List<Item>();
                table.UncommonDrop = new List<Item>();
                table.RareDrop = new List<Item>();
                table.LegendaryDrop = new List<Item>();
                table.UltraRareDrop = new List<Item>();
                foreach (DropRule rule in DropRules)
                {
                    table.UncommonDrop.Clear();
                    table.RareDrop.Clear();
                    InjectLootTable(table, rule.NpcName);
                    Item expected = GetItem(rule.ItemKey);
                    List<Item> tier = GetOrCreateTier(table, rule.Tier);
                    if (expected == null || tier.Count(item =>
                            ReferenceEquals(item, expected)) != 1)
                        errors.Add(rule.NpcName + " did not receive exactly " +
                            "one " + (expected?.ItemName ?? rule.ItemKey));
                    InjectLootTable(table, rule.NpcName);
                    if (tier.Count(item => ReferenceEquals(item, expected)) != 1)
                        errors.Add(rule.NpcName + " injection is not idempotent");
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
            finally
            {
                if (probe != null) UnityEngine.Object.Destroy(probe);
            }

            InjectWorldDropMold();
            int moldCount = GameData.GM?.WorldDropMolds?.Count(item =>
                ReferenceEquals(item, MoldItem)) ?? 0;
            if (moldCount != 1)
                errors.Add("world-drop mold count is " + moldCount);
            detail = errors.Count == 0
                ? "five idempotent themed loot injections and one native " +
                  "world-drop mold entry"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        internal static bool ValidateCatalog(out string detail)
        {
            var errors = new List<string>();
            if (!IsReady || ItemsByKey.Count != ExpectedCount)
                errors.Add("item count " + ItemsByKey.Count);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var icons = new HashSet<int>();
            foreach (Item item in ItemsByKey.Values)
            {
                if (item == null) { errors.Add("null item"); continue; }
                if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id))
                    errors.Add("duplicate/null ID " + item.ItemName);
                if (string.IsNullOrWhiteSpace(item.ItemName) ||
                    !names.Add(item.ItemName))
                    errors.Add("duplicate/null name " + item.ItemName);
                if (!item.ItemIcon)
                    errors.Add("missing icon " + item.ItemName);
                else if (!icons.Add(item.ItemIcon.GetInstanceID()))
                    errors.Add("reused icon " + item.ItemName);
                if (!ReferenceEquals(Items.ItemFactory.GetItemById(item.Id), item) ||
                    !ReferenceEquals(Items.ItemFactory.GetItem(item.ItemName), item))
                    errors.Add("factory resolution " + item.ItemName);
                if (item.SimPlayersCantGet)
                    errors.Add("SimPlayer-blocked " + item.ItemName);
            }

            Item cowl = GetItem(CowlKey);
            if (cowl == null || cowl.ItemLevel != 7 ||
                cowl.RequiredSlot != Item.SlotType.Head || cowl.Mana != 30 ||
                cowl.End != 5 || cowl.Int != 5 || cowl.VR != 5 ||
                cowl.EquipmentToActivate != "ClothHood")
                errors.Add("cowl native-budget contract");
            Item shroud = GetItem(ShroudKey);
            if (shroud == null || shroud.ItemLevel != 18 ||
                shroud.RequiredSlot != Item.SlotType.Chest ||
                shroud.HP != 20 || shroud.AC != 10 || shroud.Mana != 70 ||
                shroud.End != 7 || shroud.Int != 7 || shroud.Cha != 7 ||
                shroud.Res != 1 || shroud.VR != 3 ||
                shroud.EquipmentToActivate != "LeatherShirt5")
                errors.Add("shroud native-budget contract");
            Item scepter = GetItem(ScepterKey);
            if (scepter == null || scepter.ItemLevel != 29 ||
                scepter.RequiredSlot != Item.SlotType.PrimaryOrSecondary ||
                scepter.WeaponDmg != 16 ||
                !Mathf.Approximately(scepter.WeaponDly, 2f) ||
                scepter.HP != 100 || scepter.Mana != 45 ||
                scepter.End != 8 || scepter.Int != 10 || scepter.Cha != 10 ||
                scepter.Res != 1 || scepter.VR != 2 || !scepter.IsWand ||
                scepter.WandEffect != null || scepter.WandProcChance != 0f ||
                scepter.EquipmentToActivate != "SpectralSceptre")
                errors.Add("scepter no-proc native-budget contract");
            Item charm = CharmItem;
            if (charm == null || charm.ItemLevel != 30 ||
                charm.RequiredSlot != Item.SlotType.Charm ||
                !Mathf.Approximately(charm.IntScaling, 3f) ||
                !Mathf.Approximately(charm.EndScaling, 3f))
                errors.Add("charm six-point scaling contract");
            Item mold = MoldItem;
            string[] expectedIngredients = { "Ancient Coal",
                "Chunk of Citrine Sandsilver", "Braxonian Ore", "Luminstone" };
            string[] actualIngredients = mold?.TemplateIngredients?
                .Select(item => item?.ItemName ?? "<null>").ToArray() ??
                Array.Empty<string>();
            if (mold == null || !mold.Template || !mold.Stackable ||
                mold.RequiredSlot != Item.SlotType.General ||
                !actualIngredients.SequenceEqual(expectedIngredients) ||
                mold.TemplateRewards == null || mold.TemplateRewards.Count != 1 ||
                !ReferenceEquals(mold.TemplateRewards[0], charm))
                errors.Add("native charm mold recipe contract");

            foreach (Item gear in EquippableItems)
            {
                if (gear.Classes == null || gear.Classes.Count != 1 ||
                    !NecromancerCatalog.IsNecromancerClass(gear.Classes[0]))
                    errors.Add("class restriction " + gear.ItemName);
                if (gear.RequiredSlot != Item.SlotType.Charm &&
                    string.IsNullOrWhiteSpace(gear.EquipmentToActivate))
                    errors.Add("visual model " + gear.ItemName);
            }

            bool acquisition = RunAcquisitionSelfTest(
                out string acquisitionDetail);
            if (!acquisition) errors.Add("acquisition: " + acquisitionDetail);
            detail = errors.Count == 0
                ? "four Necromancer-only native-budget sidegrades, five " +
                  "unique icons, deterministic IDs, exact forge recipe; " +
                  acquisitionDetail
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        /// <summary>
        /// Equip one of each class item on the isolated QA character and save
        /// deliberately different quality values through Erenshor's native
        /// CharacterEquip/CharmItem fields. Any displaced gear is moved, with
        /// its exact quality, into empty direct inventory slots before the
        /// mutation; the test refuses to run if that cannot be done safely.
        /// </summary>
        internal static bool ExerciseEquipmentAndSave(out string detail)
        {
            var errors = new List<string>();
            List<EquippedTestItem> expected = BuildEquippedTestItems(
                out string setupError);
            Inventory inventory = GameData.PlayerInv;
            if (expected == null || inventory == null ||
                GameData.CurrentCharacterSlot == null || GameData.GM == null)
            {
                detail = setupError ??
                    "player equipment or native save state is unavailable";
                return false;
            }

            var displaced = expected.Where(entry => entry.Slot.MyItem != null &&
                !ReferenceEquals(entry.Slot.MyItem, inventory.Empty) &&
                !ReferenceEquals(entry.Slot.MyItem, entry.Item)).ToList();
            var spareSlots = (inventory.StoredSlots ?? new List<ItemIcon>())
                .Where(slot => slot != null && !slot.TrashSlot &&
                    slot.ThisSlotType == Item.SlotType.General &&
                    (slot.MyItem == null ||
                     ReferenceEquals(slot.MyItem, inventory.Empty)))
                .Take(displaced.Count).ToList();
            if (spareSlots.Count != displaced.Count)
            {
                detail = $"need {displaced.Count} empty direct inventory " +
                    $"slots to protect displaced gear; found {spareSlots.Count}";
                return false;
            }

            List<Item> originalTargetItems = expected.Select(entry =>
                entry.Slot.MyItem).ToList();
            List<int> originalTargetQualities = expected.Select(entry =>
                entry.Slot.Quantity).ToList();
            List<Item> displacedItems = displaced.Select(entry =>
                entry.Slot.MyItem).ToList();
            List<int> displacedQualities = displaced.Select(entry =>
                Mathf.Max(1, entry.Slot.Quantity)).ToList();
            try
            {
                for (int index = 0; index < displaced.Count; index++)
                {
                    ItemIcon destination = spareSlots[index];
                    destination.MyItem = displacedItems[index];
                    destination.Quantity = displacedQualities[index];
                    destination.UpdateSlotImage();
                }
                foreach (EquippedTestItem entry in expected)
                {
                    entry.Slot.MyItem = entry.Item;
                    entry.Slot.Quantity = entry.Quality;
                    entry.Slot.UpdateSlotImage();
                }
                inventory.UpdatePlayerInventory();
                Items.ItemFactory.SaveCustomInventory();
                SkillsSaveManager.Save();
                GameData.GM.SaveGameData(wScene: false);

                if (!ValidateEquippedState(expected, out string stateDetail))
                    errors.Add(stateDetail);
                for (int index = 0; index < displaced.Count; index++)
                    if (!ReferenceEquals(spareSlots[index].MyItem,
                            displacedItems[index]) ||
                        spareSlots[index].Quantity !=
                            displacedQualities[index])
                        errors.Add("displaced item or quality changed");
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }

            if (errors.Count > 0)
            {
                try
                {
                    for (int index = 0; index < expected.Count; index++)
                    {
                        expected[index].Slot.MyItem =
                            originalTargetItems[index] ?? inventory.Empty;
                        expected[index].Slot.Quantity =
                            originalTargetQualities[index];
                        expected[index].Slot.UpdateSlotImage();
                    }
                    foreach (ItemIcon slot in spareSlots)
                    {
                        slot.MyItem = inventory.Empty;
                        slot.Quantity = 1;
                        slot.UpdateSlotImage();
                    }
                    inventory.UpdatePlayerInventory();
                    Items.ItemFactory.SaveCustomInventory();
                    SkillsSaveManager.Save();
                    GameData.GM.SaveGameData(wScene: false);
                }
                catch (Exception rollbackError)
                {
                    errors.Add("rollback failed: " + rollbackError.Message);
                }
            }

            detail = errors.Count == 0
                ? "cowl q2, shroud q3, scepter q11, and charm q3 saved " +
                  "through exact native equipment fields; displaced gear was " +
                  "retained in direct inventory"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        internal static bool ValidateEquippedPersistence(out string detail)
        {
            List<EquippedTestItem> expected = BuildEquippedTestItems(
                out string setupError);
            if (expected == null)
            {
                detail = setupError;
                return false;
            }
            bool valid = ValidateEquippedState(expected, out detail);
            if (valid)
                detail = "reloaded cowl q2, shroud q3, scepter q11, and " +
                    "charm q3 from deterministic IDs in their exact native slots";
            return valid;
        }

        private static List<EquippedTestItem> BuildEquippedTestItems(
            out string error)
        {
            error = null;
            Inventory inventory = GameData.PlayerInv;
            if (!IsReady || inventory?.EquipmentSlots == null ||
                inventory.CharmSlot == null)
            {
                error = "Necromancer gear or player equipment is unavailable";
                return null;
            }
            ItemIcon head = FindEquipmentSlot(Item.SlotType.Head);
            ItemIcon chest = FindEquipmentSlot(Item.SlotType.Chest);
            ItemIcon primary = FindEquipmentSlot(Item.SlotType.Primary);
            if (head == null || chest == null || primary == null)
            {
                error = "native Head, Chest, or Primary slot is unavailable";
                return null;
            }
            return new List<EquippedTestItem>
            {
                new EquippedTestItem(GetItem(CowlKey), head, 2),
                new EquippedTestItem(GetItem(ShroudKey), chest, 3),
                new EquippedTestItem(GetItem(ScepterKey), primary, 11),
                new EquippedTestItem(CharmItem, inventory.CharmSlot, 3)
            };
        }

        private static ItemIcon FindEquipmentSlot(Item.SlotType slotType)
        {
            return GameData.PlayerInv?.EquipmentSlots?.FirstOrDefault(slot =>
                slot != null && slot.ThisSlotType == slotType);
        }

        private static bool ValidateEquippedState(
            List<EquippedTestItem> expected, out string detail)
        {
            var errors = new List<string>();
            Inventory inventory = GameData.PlayerInv;
            SaveGameData save = GameData.CurrentCharacterSlot;
            if (inventory == null || save == null)
            {
                detail = "player inventory or save state is unavailable";
                return false;
            }
            foreach (EquippedTestItem entry in expected)
            {
                if (entry.Item == null || entry.Slot == null)
                {
                    errors.Add("null item or slot");
                    continue;
                }
                if (!ReferenceEquals(entry.Slot.MyItem, entry.Item) ||
                    entry.Slot.Quantity != entry.Quality)
                    errors.Add($"live {entry.Item.ItemName} expected q" +
                        $"{entry.Quality}, found {entry.Slot.MyItem?.ItemName ?? "<empty>"} " +
                        $"q{entry.Slot.Quantity}");
                if (!ReferenceEquals(GameData.ItemDB?.GetItemByID(
                        entry.Item.Id), entry.Item))
                    errors.Add("ID resolution " + entry.Item.ItemName);
                SavedCustomItem sidecar = SkillsSaveManager.Data?
                    .CustomInventory?.FirstOrDefault(saved =>
                        saved != null && string.Equals(saved.Name,
                            entry.Item.ItemName,
                            StringComparison.OrdinalIgnoreCase));
                if (sidecar == null || sidecar.Count < 1)
                    errors.Add("sidecar inventory count " +
                        entry.Item.ItemName);

                if (ReferenceEquals(entry.Slot, inventory.CharmSlot))
                {
                    if (!string.Equals(save.CharmItem, entry.Item.Id,
                            StringComparison.OrdinalIgnoreCase) ||
                        save.CharmQual != entry.Quality)
                        errors.Add("native CharmItem/CharmQual " +
                            entry.Item.ItemName);
                    continue;
                }
                int slotIndex = inventory.EquipmentSlots.IndexOf(entry.Slot);
                if (slotIndex < 0 || save.CharacterEquip == null ||
                    save.EquipSlotQuantities == null ||
                    slotIndex >= save.CharacterEquip.Count ||
                    slotIndex >= save.EquipSlotQuantities.Count ||
                    !string.Equals(save.CharacterEquip[slotIndex],
                        entry.Item.Id, StringComparison.OrdinalIgnoreCase) ||
                    save.EquipSlotQuantities[slotIndex] != entry.Quality)
                    errors.Add("native equipment save field " +
                        entry.Item.ItemName);
            }
            detail = errors.Count == 0
                ? "all four live slots, qualities, saved IDs, and reload " +
                  "resolvers match"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }
    }

    [HarmonyPatch(typeof(LootTable), "Start")]
    public static class Patch_NecromancerGearLootTables
    {
        private static void Prefix(LootTable __instance)
        {
            NecromancerCatalog.TryInitialize();
            Monk.MonkCatalog.TryInitialize();
            NecromancerGear.InjectLootTable(__instance);
            NecromancerSpellLoot.InjectLootTable(__instance);
            Monk.MonkHandwrapLoot.InjectLootTable(__instance);
        }
    }
}
