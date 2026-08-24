using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ErenshorSkills.Monk
{
    /// <summary>
    /// Monk-only native-budget sidegrades. Handwrap weapons keep Erenshor's
    /// held-weapon roots empty while a custom equipment driver renders every
    /// weapon as a matched wrap set on both hands.
    /// </summary>
    public static class MonkGear
    {
        internal const string HeadwrapKey = "initiates_headband";
        internal const string GiKey = "windstep_gi";
        internal const string LegwrapsKey = "grandmasters_legwraps";
        internal const string SandalsKey = "silent_step_sandals";
        internal const string NoviceWrapsKey = "novice_handwraps";
        internal const string CopperWrapsKey = "copper_ring_handwraps";
        internal const string JadeWrapsKey = "jade_serpent_bindings";
        internal const string EmberWrapsKey = "emberfist_handwraps";
        internal const string IronWrapsKey = "iron_temple_cestus";
        internal const string MoonWrapsKey = "moonweave_handwraps";
        internal const string TranquilFistsKey =
            "fists_of_the_tranquil_storm";
        internal const string SunMoonFistsKey =
            "grandmasters_sun_and_moon_fists";
        internal const string PrielGripsKey = "priel_consorts_crimson_grips";
        internal const string DarkwardenWrapsKey = "darkwardens_chainwraps";
        internal const string NightmistressFistsKey = "nightmistress_silkfists";
        internal const string FireboneCestusKey = "firebone_cinder_cestus";
        internal const string BoneweaverKnucklesKey = "boneweaver_knuckles";
        internal const string CryptkeeperWrapsKey = "cryptkeepers_burial_wraps";
        internal const string BraxonianGauntletsKey =
            "braxonian_tempest_gauntlets";
        internal const string PlanarChampionWrapsKey =
            "planar_champions_handwraps";
        internal const string ReinforcedLinenKey =
            "reinforced_linen_knuckles";
        internal const string SilkstepBindingsKey = "silkstep_bindings";
        internal const string BlighthideStrikersKey = "blighthide_strikers";
        internal const string IronbarkFistguardsKey = "ironbark_fistguards";
        internal const string FrostweaveWrapsKey = "frostweave_handwraps";
        internal const string CelestialSilkCestusKey =
            "celestial_silk_cestus";
        internal const string BeadsKey = "grandmasters_prayer_beads";
        internal const int ExpectedCount = 27;

        private static readonly string[] HandwrapKeys =
        {
            NoviceWrapsKey, CopperWrapsKey, JadeWrapsKey, EmberWrapsKey,
            IronWrapsKey, MoonWrapsKey, TranquilFistsKey, SunMoonFistsKey,
            PrielGripsKey, DarkwardenWrapsKey, NightmistressFistsKey,
            FireboneCestusKey, BoneweaverKnucklesKey, CryptkeeperWrapsKey,
            BraxonianGauntletsKey, PlanarChampionWrapsKey,
            ReinforcedLinenKey, SilkstepBindingsKey, BlighthideStrikersKey,
            IronbarkFistguardsKey, FrostweaveWrapsKey, CelestialSilkCestusKey
        };

        private static readonly HashSet<string> BossHandwrapKeys =
            new HashSet<string>(new[]
            {
                PrielGripsKey, DarkwardenWrapsKey, NightmistressFistsKey,
                FireboneCestusKey, BoneweaverKnucklesKey,
                CryptkeeperWrapsKey, BraxonianGauntletsKey,
                PlanarChampionWrapsKey
            }, StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> CraftedHandwrapKeys =
            new HashSet<string>(new[]
            {
                ReinforcedLinenKey, SilkstepBindingsKey,
                BlighthideStrikersKey, IronbarkFistguardsKey,
                FrostweaveWrapsKey, CelestialSilkCestusKey
            }, StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, Item> ItemsByKey =
            new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, Sprite> _embeddedIcons;
        private static bool _legacyOffhandReconciled;

        internal static bool IsReady { get; private set; }
        internal static IReadOnlyCollection<Item> AllItems =>
            ItemsByKey.Values;
        internal static Item StarterWeapon => GetItem(NoviceWrapsKey);
        internal static Item EndgameWeapon => GetItem(SunMoonFistsKey);
        // The trainer-line gear: everything that is neither the starting wraps
        // nor exclusive to a boss or to Tailoring. MonkCatalog.GetVendorStock
        // then applies the ClassMerchantStock cap, so only the level 1-10 part
        // of this line plus the class charm actually reaches the shop window.
        internal static IEnumerable<Item> VendorItems => ItemsByKey.Values
            .Where(item => item != null &&
                !ReferenceEquals(item, StarterWeapon) && !IsBossDrop(item) &&
                !IsCrafted(item))
            .OrderBy(item => item.ItemLevel)
            .ThenBy(item => item.ItemName,
                StringComparer.OrdinalIgnoreCase);

        internal static void TryRegister()
        {
            if (IsReady) return;
            if (!Items.ItemFactory.Initialized ||
                MonkCatalog.MonkClass == null ||
                GameData.ItemDB?.ItemDB == null)
                return;

            Item head = FindNativeItem("Bandit's Mask");
            Item chest = FindNativeItem("Tattered Leather Tunic");
            Item legs = FindNativeItem("Creationist's Legwrap");
            Item feet = FindNativeItem("Virophan Leather Slippers");
            Item weaponAudio = FindNativeItem("Rusty Shortsword");
            if (head == null || chest == null || legs == null || feet == null ||
                weaponAudio == null)
            {
                SkillsPlugin.Log?.LogWarning(
                    "Monk gear is waiting for native light-armor visual templates.");
                return;
            }

            ItemsByKey.Clear();
            Register(HeadwrapKey, CreateHeadwrap(head));
            Register(GiKey, CreateGi(chest));
            Register(LegwrapsKey, CreateLegwraps(legs));
            Register(SandalsKey, CreateSandals(feet));
            Register(NoviceWrapsKey, CreateNoviceWraps(weaponAudio));
            Register(CopperWrapsKey, CreateHandwrap(CopperWrapsKey,
                "Copper-Ring Handwraps", 5, 110, 7, 1.24f,
                "Hammered copper rings reinforce a traveler's first practiced guard."));
            Register(JadeWrapsKey, CreateHandwrap(JadeWrapsKey,
                "Jade Serpent Bindings", 10, 525, 9, 1.22f,
                "Overlapping jade scales turn a flowing strike into a serpent's bite.",
                dex: 2, agi: 2));
            Register(EmberWrapsKey, CreateHandwrap(EmberWrapsKey,
                "Emberfist Handwraps", 15, 1650, 11, 1.19f,
                "Fire-marked cloth warms as the wearer's rhythm accelerates.\n\nAcquisition: Dropped in Azynthi's Garden; not sold by merchants.",
                hp: 15, str: 3, dex: 3, agi: 2));
            Register(IronWrapsKey, CreateHandwrap(IronWrapsKey,
                "Iron Temple Cestus", 20, 3900, 13, 1.16f,
                "Temple-forged plates distribute impact across the forearm.\n\nAcquisition: Dropped in Rockshade Hold; not sold by merchants.",
                hp: 25, str: 5, end: 4, dex: 3, agi: 3));
            Register(MoonWrapsKey, CreateHandwrap(MoonWrapsKey,
                "Moonweave Handwraps", 25, 8200, 14, 1.13f,
                "Silver thread traces crescent forms over midnight-blue bindings.\n\nAcquisition: Dropped by Planar creatures; not sold by merchants.",
                hp: 35, str: 5, end: 4, dex: 7, agi: 6));
            Register(TranquilFistsKey,
                CreateTranquilFists(weaponAudio));
            Register(SunMoonFistsKey, CreateHandwrap(SunMoonFistsKey,
                "Grandmaster's Sun-and-Moon Fists", 35, 32000, 22, 1.05f,
                "A masterwork pair balancing radiant force against still lunar focus.\n\nAcquisition: Rare drop in Soluna's Landing; not sold by merchants.",
                hp: 70, str: 10, end: 7, dex: 12, agi: 10, res: 2,
                relic: true));
            Register(PrielGripsKey, CreateHandwrap(PrielGripsKey,
                "Priel Consort's Crimson Grips", 8, 420, 8, 1.21f,
                "Crimson lacquer and pale knuckle tiles mark a trophy taken from a Priel Consort.\n\nAcquisition: Boss drop; not sold by merchants.",
                hp: 8, dex: 2, agi: 2));
            Register(DarkwardenWrapsKey, CreateHandwrap(DarkwardenWrapsKey,
                "Darkwarden's Chainwraps", 12, 980, 10, 1.20f,
                "Blackened chain is sewn across the striking surface in the Darkwarden fashion.\n\nAcquisition: Boss drop; not sold by merchants.",
                hp: 12, str: 2, end: 2, dex: 3));
            Register(NightmistressFistsKey, CreateHandwrap(NightmistressFistsKey,
                "Nightmistress Silkfists", 17, 2450, 12, 1.18f,
                "Moonless silk supports hooked silver guards favored by the Molorai Nightmistress.\n\nAcquisition: Boss drop; not sold by merchants.",
                hp: 20, dex: 5, agi: 5, res: 1));
            Register(FireboneCestusKey, CreateHandwrap(FireboneCestusKey,
                "Firebone Cinder Cestus", 22, 5600, 14, 1.14f,
                "Charred bone plates hold living ember seams from an Azynthian Firebone.\n\nAcquisition: Boss drop; not sold by merchants.",
                hp: 30, str: 6, end: 4, dex: 4, res: 1, relic: true));
            Register(BoneweaverKnucklesKey, CreateHandwrap(BoneweaverKnucklesKey,
                "Boneweaver Knuckles", 27, 10800, 15, 1.12f,
                "Interlocked phalange plates are bound with alchemically hardened sinew.\n\nAcquisition: Boss drop; not sold by merchants.",
                hp: 42, str: 7, end: 5, dex: 7, agi: 5));
            Register(CryptkeeperWrapsKey, CreateHandwrap(CryptkeeperWrapsKey,
                "Cryptkeeper's Burial Wraps", 31, 18400, 18, 1.09f,
                "Ancient gravecloth lies beneath tarnished bronze burial plates.\n\nAcquisition: Boss drop; not sold by merchants.",
                hp: 55, str: 7, end: 7, dex: 9, agi: 7, res: 2,
                relic: true));
            Register(BraxonianGauntletsKey, CreateHandwrap(BraxonianGauntletsKey,
                "Braxonian Tempest Gauntlets", 34, 27500, 20, 1.07f,
                "Sand-gold guards cage storm-blue crystals charged by a Braxonian champion.\n\nAcquisition: Boss drop; not sold by merchants.",
                hp: 65, str: 9, end: 7, dex: 11, agi: 9, res: 2,
                relic: true));
            Register(PlanarChampionWrapsKey, CreateHandwrap(PlanarChampionWrapsKey,
                "Planar Champion's Handwraps", 42, 48000, 30, 1.02f,
                "Articulated violet starmetal contains a prismatic shard won in planar combat.\n\nAcquisition: Raid boss drop; not sold by merchants.",
                hp: 90, str: 12, end: 10, dex: 14, agi: 12, res: 3,
                relic: true));
            Register(ReinforcedLinenKey, CreateHandwrap(ReinforcedLinenKey,
                "Reinforced Linen Knuckles", 4, 85, 6, 1.23f,
                "Closely stitched linen and leather pads make a dependable apprentice's striking wrap.\n\nAcquisition: Crafted with Tailoring; not sold by merchants.",
                hp: 5, dex: 1, agi: 1));
            Register(SilkstepBindingsKey, CreateHandwrap(SilkstepBindingsKey,
                "Silkstep Bindings", 10, 600, 8, 1.19f,
                "Braided spider silk protects the hand while preserving a quick, fluid guard.\n\nAcquisition: Crafted with Tailoring; not sold by merchants.",
                hp: 12, dex: 3, agi: 3));
            Register(BlighthideStrikersKey, CreateHandwrap(BlighthideStrikersKey,
                "Blighthide Strikers", 16, 2200, 11, 1.18f,
                "Cured blighthide and thorned knuckle guards turn poison-country scraps into a disciplined weapon.\n\nAcquisition: Crafted with Tailoring; not sold by merchants.",
                hp: 22, str: 3, end: 2, dex: 4, agi: 3, res: 1));
            Register(IronbarkFistguardsKey,
                CreateHandwrap(IronbarkFistguardsKey,
                "Ironbark Fistguards", 22, 5200, 13, 1.14f,
                "Riveted ironbark plates spread impact without the weight of forged steel.\n\nAcquisition: Crafted with Tailoring; not sold by merchants.",
                hp: 35, str: 6, end: 5, dex: 4, agi: 2));
            Register(FrostweaveWrapsKey, CreateHandwrap(FrostweaveWrapsKey,
                "Frostweave Handwraps", 28, 12500, 15, 1.11f,
                "Silver-threaded frostweave holds an icy edge across the knuckles.\n\nAcquisition: Crafted with Tailoring; not sold by merchants.",
                hp: 48, str: 6, end: 5, dex: 9, agi: 8, res: 2,
                relic: true));
            Register(CelestialSilkCestusKey,
                CreateHandwrap(CelestialSilkCestusKey,
                "Celestial Silk Cestus", 36, 36000, 22, 1.06f,
                "Celestial silk supports articulated guards light enough to follow a master's hands.\n\nAcquisition: Crafted with Tailoring; not sold by merchants.",
                hp: 75, str: 10, end: 8, dex: 12, agi: 10, res: 3,
                relic: true));
            Register(BeadsKey, CreatePrayerBeads());
            IsReady = ItemsByKey.Count == ExpectedCount &&
                ItemsByKey.Values.All(item => item != null);
            if (IsReady)
            {
                SkillsPlugin.Log?.LogInfo(
                    "Monk: registered twenty-seven class-only gear pieces, including eight trainer-line, eight boss-only, and six tailored handwraps.");
                // The six Tailoring outputs are registered here, after the
                // base item factory. Validate only once the complete release
                // catalog exists so startup does not report false missing-item
                // errors for valid Monk recipes.
                Items.ItemFactory.ValidateTradeskillItems();
            }
        }

        private static Item CreateHeadwrap(Item visual)
        {
            Item item = CreateBase(HeadwrapKey, "Initiate's Headwrap", 7,
                260, Item.SlotType.Head,
                "A practical face and brow wrap worn by students of the open hand.");
            item.AC = 8;
            item.HP = 20;
            item.End = 3;
            item.Dex = 4;
            item.Agi = 4;
            CopyNativeVisual(item, visual);
            item.EquipmentToActivate = "BanditMask";
            ApplyPalette(item);
            return item;
        }

        private static Item CreateGi(Item visual)
        {
            Item item = CreateBase(GiKey, "Windstep Gi", 18, 2300,
                Item.SlotType.Chest,
                "Layered training cloth cut for speed and an unrestricted guard.\n\nAcquisition: Dropped in Windwashed Pass; not sold by merchants.");
            item.AC = 40;
            item.HP = 60;
            item.Str = 6;
            item.End = 6;
            item.Dex = 7;
            item.Agi = 7;
            CopyNativeVisual(item, visual);
            item.EquipmentToActivate = "LeatherTunic";
            ApplyPalette(item);
            return item;
        }

        private static Item CreateLegwraps(Item visual)
        {
            Item item = CreateBase(LegwrapsKey, "Grandmaster's Legwraps",
                25, 6600, Item.SlotType.Leg,
                "Dense wraps reinforce the stance without locking the knees.\n\nAcquisition: Dropped in Loomingwood; not sold by merchants.");
            item.AC = 35;
            item.HP = 80;
            item.Str = 7;
            item.End = 6;
            item.Dex = 8;
            item.Agi = 8;
            CopyNativeVisual(item, visual);
            item.EquipmentToActivate = "Halfwrap";
            ApplyPalette(item);
            return item;
        }

        private static Item CreateSandals(Item visual)
        {
            Item item = CreateBase(SandalsKey, "Silent-Step Sandals", 15,
                1450, Item.SlotType.Foot,
                "Soft-soled sandals that leave almost no sound on stone.\n\nAcquisition: Dropped in Krakengard; not sold by merchants.");
            item.AC = 15;
            item.HP = 30;
            item.End = 3;
            item.Dex = 4;
            item.Agi = 6;
            CopyNativeVisual(item, visual);
            item.EquipmentToActivate = "ClothShoes";
            ApplyPalette(item);
            return item;
        }

        private static Item CreateNoviceWraps(Item audioTemplate)
        {
            return CreateHandwrap(NoviceWrapsKey, "Novice Handwraps", 1,
                10, 5, 1.25f,
                "Plain linen wraps that protect the knuckles without hiding the hands.",
                audioTemplate: audioTemplate);
        }

        private static Item CreateHandwrap(string key, string name, int level,
            int value, int damage, float delay, string lore, int hp = 0,
            int str = 0, int end = 0, int dex = 0, int agi = 0, int res = 0,
            bool relic = false, Item audioTemplate = null)
        {
            Item item = CreateBase(key, name, level, value,
                Item.SlotType.Primary, lore);
            item.WeaponDmg = damage;
            item.WeaponDly = delay;
            item.HP = hp;
            item.Str = str;
            item.End = end;
            item.Dex = dex;
            item.Agi = agi;
            item.Res = res;
            item.Relic = relic;
            item.ThisWeaponType = Item.WeaponType.TwoHandMelee;
            item.AttackSound = (audioTemplate ??
                FindNativeItem("Rusty Shortsword"))?.AttackSound;
            item.EquipmentToActivate = "";
            return item;
        }

        private static Item CreateTranquilFists(Item audioTemplate)
        {
            return CreateHandwrap(TranquilFistsKey,
                "Fists of the Tranquil Storm", 30, 18000, 17, 1.10f,
                "Enchanted wraps carry the calm force of a gathering storm.\n\nAcquisition: Dropped in Windwashed Pass and Loomingwood; not sold by merchants.",
                hp: 50, str: 8, end: 5, dex: 10, agi: 8, res: 1,
                relic: true, audioTemplate: audioTemplate);
        }

        private static Item CreatePrayerBeads()
        {
            Item item = CreateBase(BeadsKey,
                "Grandmaster's Prayer Beads", 30, 6500,
                Item.SlotType.Charm,
                "Each bead marks a lesson practiced until thought became motion.");
            // Native level-30 class charms spend six scaling points.
            item.StrScaling = 2f;
            item.DexScaling = 2f;
            item.AgiScaling = 2f;
            item.Relic = true;
            return item;
        }

        private static Item CreateBase(string key, string name, int level,
            int value, Item.SlotType slot, string lore)
        {
            var item = ScriptableObject.CreateInstance<Item>();
            item.name = "CS MONK GEAR - " + name;
            item.Id = BuildId(key);
            item.ItemName = name;
            item.ItemLevel = level;
            item.ItemValue = value;
            item.ItemIcon = LoadIcon("Monk Gear " + key);
            item.RequiredSlot = slot;
            item.Classes = new List<Class> { MonkCatalog.MonkClass };
            item.Lore = lore;
            item.Stackable = false;
            item.Disposable = false;
            item.Unique = false;
            item.Template = false;
            item.TemplateIngredients = new List<Item>();
            item.TemplateRewards = new List<Item>();
            item.SimPlayersCantGet = false;
            item.PlayerCannotSell = false;
            item.NoTradeNoDestroy = false;
            item.hideFlags = HideFlags.HideAndDontSave;
            return item;
        }

        private static void CopyNativeVisual(Item target, Item source)
        {
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
            target.ItemMetalDark = source.ItemMetalDark;
            target.ItemMetalSecondary = source.ItemMetalSecondary;
            target.ItemLeatherPrimary = source.ItemLeatherPrimary;
            target.ItemLeatherSecondary = source.ItemLeatherSecondary;
            target.HideHairWhenEquipped = source.HideHairWhenEquipped;
            target.HideHeadWhenEquipped = source.HideHeadWhenEquipped;
        }

        private static void ApplyPalette(Item item)
        {
            item.ItemPrimaryColor = new Color(0.06f, 0.28f, 0.27f, 1f);
            item.ItemSecondaryColor = new Color(0.58f, 0.43f, 0.23f, 1f);
            item.ItemLeatherPrimary = new Color(0.22f, 0.15f, 0.09f, 1f);
            item.ItemLeatherSecondary = new Color(0.40f, 0.29f, 0.16f, 1f);
            item.ItemMetalPrimary = new Color(0.62f, 0.52f, 0.31f, 1f);
            item.ItemMetalSecondary = new Color(0.30f, 0.37f, 0.34f, 1f);
            item.ItemMetalDark = new Color(0.07f, 0.10f, 0.09f, 1f);
        }

        private static Item FindNativeItem(string itemName) =>
            GameData.ItemDB?.ItemDB?.FirstOrDefault(item => item != null &&
                string.Equals(item.ItemName, itemName,
                    StringComparison.OrdinalIgnoreCase));

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
                $"Missing required unique Monk gear icon '{name}'.");
            return null;
        }

        private static void Register(string key, Item item)
        {
            if (item == null) return;
            ItemsByKey[key] = item;
            Items.ItemFactory.RegisterExternalItem(item.ItemName, item);
        }

        internal static Item GetItem(string key)
        {
            ItemsByKey.TryGetValue(key ?? "", out Item item);
            return item;
        }

        internal static bool IsHandwrap(Item item)
        {
            return GetHandwrapTier(item) >= 0;
        }

        internal static bool IsBossDrop(Item item)
        {
            if (item == null) return false;
            return BossHandwrapKeys.Any(key => ReferenceEquals(GetItem(key), item));
        }

        internal static IEnumerable<Item> BossDropItems =>
            BossHandwrapKeys.Select(GetItem).Where(item => item != null);

        internal static bool IsCrafted(Item item)
        {
            if (item == null) return false;
            return CraftedHandwrapKeys.Any(key =>
                ReferenceEquals(GetItem(key), item));
        }

        internal static IEnumerable<Item> CraftedItems =>
            CraftedHandwrapKeys.Select(GetItem).Where(item => item != null);

        internal static IEnumerable<Item> AllHandwraps =>
            HandwrapKeys.Select(GetItem).Where(item => item != null);

        internal static void ReconcileLegacyOffhand()
        {
            if (_legacyOffhandReconciled || !IsReady) return;
            Inventory inventory = GameData.PlayerInv;
            ItemIcon main = inventory?.MH;
            ItemIcon off = inventory?.OH;
            if (main == null || off == null) return;
            if (!IsHandwrap(off.MyItem))
            {
                _legacyOffhandReconciled = true;
                return;
            }

            Item wrap = off.MyItem;
            int quality = Mathf.Max(1, off.Quantity);
            ItemIcon destination = ReferenceEquals(main.MyItem, inventory.Empty)
                || main.MyItem == null ? main :
                (inventory.StoredSlots ?? new List<ItemIcon>())
                    .FirstOrDefault(slot =>
                        Items.BagSystem.IsDirectInventorySlot(slot) &&
                        (slot.MyItem == null ||
                         ReferenceEquals(slot.MyItem, inventory.Empty)));
            if (destination == null)
            {
                SkillsPlugin.Log?.LogWarning(
                    "Legacy offhand handwrap needs an empty inventory slot before it can migrate to the two-handed equipment contract.");
                return;
            }
            destination.MyItem = wrap;
            destination.Quantity = quality;
            off.MyItem = inventory.Empty;
            off.Quantity = 0;
            destination.UpdateSlotImage();
            off.UpdateSlotImage();
            inventory.UpdatePlayerInventory();
            Items.ItemFactory.SaveCustomInventory();
            GameData.GM?.SaveGameData(wScene: false);
            _legacyOffhandReconciled = true;
            SkillsPlugin.Log?.LogInfo(
                $"Migrated legacy offhand {wrap.ItemName} safely into " +
                (ReferenceEquals(destination, main)
                    ? "the primary slot" : "general inventory") +
                "; all handwraps now reserve both hands.");
        }

        internal static int GetHandwrapTier(Item item)
        {
            if (item == null) return -1;
            for (int index = 0; index < HandwrapKeys.Length; index++)
            {
                Item candidate = GetItem(HandwrapKeys[index]);
                if (candidate != null && string.Equals(item.Id, candidate.Id,
                    StringComparison.OrdinalIgnoreCase))
                    return index;
            }
            return -1;
        }

        private static string BuildId(string key) =>
            "CS_MONK_GEAR_" + new string((key ?? "").ToUpperInvariant()
                .Where(character => char.IsLetterOrDigit(character) ||
                    character == '_').ToArray());

        internal static bool ValidateCatalog(out string detail)
        {
            var errors = new List<string>();
            if (!IsReady || ItemsByKey.Count != ExpectedCount)
                errors.Add("item count " + ItemsByKey.Count);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var icons = new HashSet<int>();
            foreach (Item item in ItemsByKey.Values)
            {
                if (item == null || !ids.Add(item?.Id ?? ""))
                {
                    errors.Add("null/duplicate item");
                    continue;
                }
                if (!item.ItemIcon || !icons.Add(item.ItemIcon.GetInstanceID()))
                    errors.Add("missing/reused icon " + item.ItemName);
                if (item.Classes == null || item.Classes.Count != 1 ||
                    !item.Classes.Contains(MonkCatalog.MonkClass))
                    errors.Add("class restriction " + item.ItemName);
                if (!ReferenceEquals(
                    Items.ItemFactory.GetItemById(item.Id), item))
                    errors.Add("save resolution " + item.ItemName);
            }
            Item[] handwraps = HandwrapKeys.Select(GetItem).ToArray();
            int[] expectedLevels =
                { 1, 5, 10, 15, 20, 25, 30, 35, 8, 12, 17, 22, 27, 31, 34, 42,
                  4, 10, 16, 22, 28, 36 };
            for (int index = 0; index < handwraps.Length; index++)
            {
                Item wrap = handwraps[index];
                if (!IsHandwrap(wrap) || wrap?.ItemLevel != expectedLevels[index] ||
                    wrap.ThisWeaponType != Item.WeaponType.TwoHandMelee ||
                    Skills.CombatSkills.ClassifyWeapon(wrap, false) !=
                        Skills.CombatSkills.WeaponType.HandToHand ||
                    wrap.RequiredSlot != Item.SlotType.Primary ||
                    !string.IsNullOrEmpty(wrap.EquipmentToActivate))
                    errors.Add("handwrap tier " + expectedLevels[index]);
            }
            if (BossDropItems.Count() != 8 ||
                BossDropItems.Any(item => VendorItems.Contains(item)) ||
                BossDropItems.Any(item => item.PlayerCannotSell ||
                    item.Disposable || item.NoTradeNoDestroy ||
                    item.ItemValue <= 0))
                errors.Add("boss handwrap acquisition/disposition contract");
            if (CraftedItems.Count() != 6 ||
                CraftedItems.Any(item => VendorItems.Contains(item) ||
                    BossDropItems.Contains(item)) ||
                CraftedItems.Any(item => item.PlayerCannotSell ||
                    item.Disposable || item.NoTradeNoDestroy ||
                    item.ItemValue <= 0))
                errors.Add("tailored handwrap acquisition/disposition contract");
            string[] expectedModels =
            {
                "BanditMask", "LeatherTunic", "Halfwrap", "ClothShoes"
            };
            Item[] visible =
            {
                GetItem(HeadwrapKey), GetItem(GiKey),
                GetItem(LegwrapsKey), GetItem(SandalsKey)
            };
            for (int index = 0; index < visible.Length; index++)
                if (visible[index]?.EquipmentToActivate !=
                    expectedModels[index])
                    errors.Add("visual model " +
                        (visible[index]?.ItemName ?? index.ToString()));
            if (!ValidateNativeBudget(out string budget))
                errors.Add(budget);
            if (!ValidateNativeWeaponParity(out string parity))
                errors.Add(parity);
            detail = errors.Count == 0
                ? "twenty-seven unique Monk-only icons and deterministic IDs; four native light-armor models, eight trainer-line, eight boss-only, and six tailored paired handwraps spanning levels 1-42, one six-point class charm; the trainer stocks only the level 1-" + ClassMerchantStock.MaxEquipmentLevel + " weapons and armor plus that charm; " + budget + "; " + parity
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        internal static bool ValidateNativeBudget(out string detail)
        {
            var errors = new List<string>();
            Item novice = StarterWeapon;
            Item tranquil = GetItem(TranquilFistsKey);
            Item master = EndgameWeapon;
            Item charm = GetItem(BeadsKey);
            if (novice == null || novice.ItemLevel != 1 ||
                novice.WeaponDmg != 5 ||
                !Mathf.Approximately(novice.WeaponDly, 1.25f))
                errors.Add("starter wraps");
            if (tranquil == null || tranquil.ItemLevel != 30 ||
                tranquil.WeaponDmg != 17 ||
                !Mathf.Approximately(tranquil.WeaponDly, 1.10f) ||
                tranquil.Str + tranquil.End + tranquil.Dex + tranquil.Agi != 31)
                errors.Add("level-30 wraps");
            if (master == null || master.ItemLevel != 35 ||
                master.WeaponDmg != 22 ||
                !Mathf.Approximately(master.WeaponDly, 1.05f))
                errors.Add("level-35 wraps");
            if (charm == null ||
                !Mathf.Approximately(charm.StrScaling + charm.DexScaling +
                    charm.AgiScaling, 6f))
                errors.Add("six-point charm");
            if (GetItem(GiKey)?.AC > 40 ||
                GetItem(LegwrapsKey)?.AC > 35 ||
                GetItem(HeadwrapKey)?.AC > 8 ||
                GetItem(SandalsKey)?.AC > 15)
                errors.Add("light armor AC ceiling");
            detail = errors.Count == 0
                ? "new gear stays at native light-armor ceilings; every wrap reserves both hands and follows the native two-handed power curve through level-42 raid metadata; charm uses the native six-point budget"
                : "gear budget mismatch: " + string.Join(", ", errors);
            return errors.Count == 0;
        }

        internal static bool ValidateNativeWeaponParity(out string detail)
        {
            var errors = new List<string>();
            float minimumRatio = float.MaxValue;
            float maximumRatio = float.MinValue;
            List<KeyValuePair<int, float>> curve = BuildNativeTwoHandCurve();
            if (curve.Count < 2)
            {
                detail = "native two-handed weapon curve is unavailable";
                return false;
            }
            foreach (Item wrap in AllHandwraps)
            {
                float target = ResolveNativeCurve(curve, wrap.ItemLevel);
                float ratio = NativeMeleeScore(wrap) /
                    Mathf.Max(0.01f, target);
                minimumRatio = Mathf.Min(minimumRatio, ratio);
                maximumRatio = Mathf.Max(maximumRatio, ratio);
                if (ratio < 0.70f || ratio > 1.35f)
                    errors.Add($"{wrap.ItemName} is {ratio:P0} of the native L{wrap.ItemLevel} two-hand curve");
            }
            detail = errors.Count == 0
                ? $"all {AllHandwraps.Count()} two-handed wraps are within {minimumRatio:P0}-{maximumRatio:P0} of the smoothed native same-level two-hand curve"
                : "native weapon parity: " + string.Join(", ", errors);
            return errors.Count == 0;
        }

        private sealed class NativePowerBlock
        {
            internal int MinimumLevel;
            internal int MaximumLevel;
            internal float WeightedScore;
            internal int Weight;
            internal float Mean => WeightedScore / Mathf.Max(1, Weight);
        }

        private static List<KeyValuePair<int, float>>
            BuildNativeTwoHandCurve()
        {
            Item[] database = GameData.ItemDB?.ItemDB ?? Array.Empty<Item>();
            var blocks = new List<NativePowerBlock>();
            foreach (IGrouping<int, Item> group in database.Where(item =>
                    item != null && item.WeaponDmg > 0 &&
                    item.WeaponDly > 0.1f &&
                    !(item.Id ?? "").StartsWith("CS_",
                        StringComparison.OrdinalIgnoreCase) &&
                    (item.ThisWeaponType == Item.WeaponType.TwoHandMelee ||
                     item.ThisWeaponType == Item.WeaponType.TwoHandStaff))
                .GroupBy(item => item.ItemLevel).OrderBy(group => group.Key))
            {
                List<float> scores = group.Select(NativeMeleeScore)
                    .OrderBy(score => score).ToList();
                float median = scores.Count % 2 == 0
                    ? (scores[scores.Count / 2 - 1] +
                       scores[scores.Count / 2]) * 0.5f
                    : scores[scores.Count / 2];
                blocks.Add(new NativePowerBlock
                {
                    MinimumLevel = group.Key,
                    MaximumLevel = group.Key,
                    WeightedScore = median * scores.Count,
                    Weight = scores.Count
                });
                while (blocks.Count > 1 &&
                    blocks[blocks.Count - 2].Mean >
                    blocks[blocks.Count - 1].Mean)
                {
                    NativePowerBlock right = blocks[blocks.Count - 1];
                    NativePowerBlock left = blocks[blocks.Count - 2];
                    blocks.RemoveRange(blocks.Count - 2, 2);
                    blocks.Add(new NativePowerBlock
                    {
                        MinimumLevel = left.MinimumLevel,
                        MaximumLevel = right.MaximumLevel,
                        WeightedScore = left.WeightedScore +
                            right.WeightedScore,
                        Weight = left.Weight + right.Weight
                    });
                }
            }
            var curve = new List<KeyValuePair<int, float>>();
            foreach (NativePowerBlock block in blocks)
            {
                curve.Add(new KeyValuePair<int, float>(
                    block.MinimumLevel, block.Mean));
                if (block.MaximumLevel != block.MinimumLevel)
                    curve.Add(new KeyValuePair<int, float>(
                        block.MaximumLevel, block.Mean));
            }
            return curve.OrderBy(point => point.Key).ToList();
        }

        private static float ResolveNativeCurve(
            IReadOnlyList<KeyValuePair<int, float>> curve, int level)
        {
            if (level <= curve[0].Key) return curve[0].Value;
            if (level >= curve[curve.Count - 1].Key)
                return curve[curve.Count - 1].Value;
            for (int index = 1; index < curve.Count; index++)
            {
                if (level > curve[index].Key) continue;
                KeyValuePair<int, float> left = curve[index - 1];
                KeyValuePair<int, float> right = curve[index];
                float progress = Mathf.InverseLerp(left.Key, right.Key, level);
                return Mathf.Lerp(left.Value, right.Value, progress);
            }
            return curve[curve.Count - 1].Value;
        }

        private static float NativeMeleeScore(Item item)
        {
            return item.WeaponDmg / Mathf.Max(0.1f, item.WeaponDly) +
                item.Str * 0.10f + item.Dex * 0.08f + item.End * 0.015f +
                item.Agi * 0.01f;
        }

        internal static bool ExerciseEquipmentAndSave(out string detail)
        {
            Inventory inventory = GameData.PlayerInv;
            if (!IsReady || inventory?.EquipmentSlots == null ||
                inventory.CharmSlot == null ||
                GameData.CurrentCharacterSlot == null || GameData.GM == null)
            {
                detail = "Monk gear or native save state is unavailable";
                return false;
            }

            var desired = new List<(Item Item, ItemIcon Slot, int Quality)>
            {
                (GetItem(HeadwrapKey), FindSlot(Item.SlotType.Head), 2),
                (GetItem(GiKey), FindSlot(Item.SlotType.Chest), 3),
                (GetItem(LegwrapsKey), FindSlot(Item.SlotType.Leg), 4),
                (GetItem(SandalsKey), FindSlot(Item.SlotType.Foot), 5),
                (EndgameWeapon, FindSlot(Item.SlotType.Primary), 6),
                (GetItem(BeadsKey), inventory.CharmSlot, 3)
            };
            if (desired.Any(entry => entry.Item == null || entry.Slot == null))
            {
                detail = "one or more native target slots are unavailable";
                return false;
            }

            var displaced = desired.Where(entry => entry.Slot.MyItem != null &&
                !ReferenceEquals(entry.Slot.MyItem, inventory.Empty) &&
                !ReferenceEquals(entry.Slot.MyItem, entry.Item)).ToList();
            var spare = (inventory.StoredSlots ?? new List<ItemIcon>())
                .Where(Items.BagSystem.IsDirectInventorySlot)
                .Where(slot => slot.MyItem == null ||
                    ReferenceEquals(slot.MyItem, inventory.Empty))
                .Take(displaced.Count).ToList();
            if (spare.Count != displaced.Count)
            {
                detail = $"need {displaced.Count} empty inventory slots to protect existing gear; found {spare.Count}";
                return false;
            }

            for (int index = 0; index < displaced.Count; index++)
            {
                spare[index].MyItem = displaced[index].Slot.MyItem;
                spare[index].Quantity = Mathf.Max(1,
                    displaced[index].Slot.Quantity);
                spare[index].UpdateSlotImage();
            }
            foreach (var entry in desired)
            {
                entry.Slot.MyItem = entry.Item;
                entry.Slot.Quantity = entry.Quality;
                entry.Slot.UpdateSlotImage();
            }
            inventory.UpdatePlayerInventory();
            Items.ItemFactory.SaveCustomInventory();
            SkillsSaveManager.Save();
            GameData.GM.SaveGameData(wScene: false);
            return ValidateEquippedPersistence(out detail);
        }

        internal static bool ValidateEquippedPersistence(out string detail)
        {
            Inventory inventory = GameData.PlayerInv;
            SaveGameData save = GameData.CurrentCharacterSlot;
            if (inventory?.EquipmentSlots == null || save == null)
            {
                detail = "inventory/save unavailable";
                return false;
            }
            var expected = new List<(Item Item, ItemIcon Slot, int Quality)>
            {
                (GetItem(HeadwrapKey), FindSlot(Item.SlotType.Head), 2),
                (GetItem(GiKey), FindSlot(Item.SlotType.Chest), 3),
                (GetItem(LegwrapsKey), FindSlot(Item.SlotType.Leg), 4),
                (GetItem(SandalsKey), FindSlot(Item.SlotType.Foot), 5),
                (EndgameWeapon, FindSlot(Item.SlotType.Primary), 6),
                (GetItem(BeadsKey), inventory.CharmSlot, 3)
            };
            var errors = new List<string>();
            foreach (var entry in expected)
            {
                if (entry.Item == null || entry.Slot == null ||
                    !ReferenceEquals(entry.Slot.MyItem, entry.Item) ||
                    entry.Slot.Quantity != entry.Quality)
                {
                    errors.Add(entry.Item?.ItemName ?? "missing item");
                    continue;
                }
                if (ReferenceEquals(entry.Slot, inventory.CharmSlot))
                {
                    if (!string.Equals(save.CharmItem, entry.Item.Id,
                            StringComparison.OrdinalIgnoreCase) ||
                        save.CharmQual != entry.Quality)
                        errors.Add("charm save");
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
                    errors.Add("save " + entry.Item.ItemName);
            }
            detail = errors.Count == 0
                ? "the complete equipped Monk loadout occupies the intended native slots and retains deterministic IDs plus qualities through native save fields"
                : string.Join("; ", errors);
            return errors.Count == 0;
        }

        private static ItemIcon FindSlot(Item.SlotType type) =>
            GameData.PlayerInv?.EquipmentSlots?.FirstOrDefault(slot =>
                slot != null && slot.ThisSlotType == type);
    }
}
